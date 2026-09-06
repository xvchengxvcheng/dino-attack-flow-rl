from __future__ import annotations

import math
from collections.abc import Sequence
from dataclasses import dataclass

import torch
from torch import nn

from flow_rl.models.gaussian import flatten_observations
from flow_rl.models.state_encoder import StateEncoder


def _normalize_shapes(
    observation_shapes: Sequence[tuple[int, ...]],
) -> tuple[tuple[int, ...], ...]:
    shapes = tuple(
        tuple(int(dimension) for dimension in shape)
        for shape in observation_shapes
    )
    if not shapes or any(not shape for shape in shapes):
        raise ValueError("observation_shapes must contain non-empty shapes")
    if any(dimension <= 0 for shape in shapes for dimension in shape):
        raise ValueError("observation dimensions must be positive")
    return shapes


def _normalize_sizes(sizes: Sequence[int], *, name: str) -> tuple[int, ...]:
    normalized = tuple(int(size) for size in sizes)
    if any(size <= 0 for size in normalized):
        raise ValueError(f"{name} must contain positive integers")
    return normalized


def _validate_observation_shapes(
    observations: tuple[torch.Tensor, ...],
    expected_shapes: tuple[tuple[int, ...], ...],
) -> torch.Tensor:
    features = flatten_observations(observations)
    if len(observations) != len(expected_shapes):
        raise ValueError("observation stream count does not match model")
    for index, (observation, expected) in enumerate(
        zip(observations, expected_shapes)
    ):
        if observation.shape[1:] != expected:
            raise ValueError(
                f"observations[{index}] feature shape must be {expected}"
            )
    return features


class FlowTimeEmbedding(nn.Module):
    def __init__(self, *, embedding_size: int) -> None:
        super().__init__()
        if embedding_size < 4 or embedding_size % 2 != 0:
            raise ValueError("embedding_size must be an even integer of at least 4")
        self.embedding_size = int(embedding_size)
        half_size = self.embedding_size // 2
        frequencies = torch.exp(
            -math.log(10_000.0)
            * torch.arange(half_size, dtype=torch.float32)
            / float(half_size - 1)
        )
        self.register_buffer("frequencies", frequencies)
        self.projection = nn.Sequential(
            nn.Linear(self.embedding_size, self.embedding_size),
            nn.SiLU(),
        )

    def forward(self, time: torch.Tensor) -> torch.Tensor:
        if not isinstance(time, torch.Tensor):
            raise TypeError("time must be a torch.Tensor")
        if time.dtype != torch.float32:
            raise TypeError("time must have dtype float32")
        if time.ndim != 2 or time.shape[1] != 1:
            raise ValueError("time must have shape (batch, 1)")
        if not torch.isfinite(time).all():
            raise ValueError("time must contain only finite values")
        phases = time * self.frequencies.unsqueeze(0)
        return self.projection(torch.cat((torch.sin(phases), torch.cos(phases)), dim=1))


class FlowStateEncoder(nn.Module):
    def __init__(
        self,
        *,
        observation_shapes: Sequence[tuple[int, ...]],
        state_size: int,
    ) -> None:
        super().__init__()
        self.observation_shapes = _normalize_shapes(observation_shapes)
        if state_size <= 0:
            raise ValueError("state_size must be positive")
        self.state_size = int(state_size)
        feature_size = sum(math.prod(shape) for shape in self.observation_shapes)
        self.network = nn.Sequential(
            nn.Linear(feature_size, self.state_size),
            nn.SiLU(),
        )

    def forward(self, observations: tuple[torch.Tensor, ...]) -> torch.Tensor:
        features = _validate_observation_shapes(
            observations,
            self.observation_shapes,
        )
        return self.network(features)


class ConditionalVelocityMLP(nn.Module):
    def __init__(
        self,
        *,
        observation_shapes: Sequence[tuple[int, ...]],
        action_size: int,
        state_size: int = 128,
        time_embedding_size: int = 32,
        hidden_sizes: Sequence[int] = (128, 128),
        state_encoder: StateEncoder | None = None,
    ) -> None:
        super().__init__()
        if action_size <= 0:
            raise ValueError("action_size must be positive")
        self.action_size = int(action_size)
        normalized_shapes = _normalize_shapes(observation_shapes)
        if state_encoder is None:
            self.state_encoder = FlowStateEncoder(
                observation_shapes=normalized_shapes,
                state_size=state_size,
            )
        else:
            if not isinstance(state_encoder, StateEncoder):
                raise TypeError("state_encoder must be a StateEncoder")
            if state_encoder.observation_shapes != normalized_shapes:
                raise ValueError("state_encoder observation_shapes do not match velocity model")
            if state_encoder.output_size != state_size:
                raise ValueError("state_encoder output_size must match state_size")
            self.state_encoder = state_encoder
        self.time_embedding = FlowTimeEmbedding(
            embedding_size=time_embedding_size,
        )
        widths = _normalize_sizes(hidden_sizes, name="hidden_sizes")
        layers: list[nn.Module] = []
        input_size = state_size + time_embedding_size + self.action_size
        for width in widths:
            layers.extend((nn.Linear(input_size, width), nn.SiLU()))
            input_size = width
        layers.append(nn.Linear(input_size, self.action_size))
        self.velocity_head = nn.Sequential(*layers)

    def forward(
        self,
        observations: tuple[torch.Tensor, ...],
        latent_actions: torch.Tensor,
        time: torch.Tensor,
    ) -> torch.Tensor:
        state = self.state_encoder(observations)
        if not isinstance(latent_actions, torch.Tensor):
            raise TypeError("latent_actions must be a torch.Tensor")
        if latent_actions.dtype != torch.float32:
            raise TypeError("latent_actions must have dtype float32")
        expected_shape = (state.shape[0], self.action_size)
        if latent_actions.shape != expected_shape:
            raise ValueError(f"latent_actions must have shape {expected_shape}")
        if latent_actions.device != state.device:
            raise ValueError("latent_actions and observations must share a device")
        if not torch.isfinite(latent_actions).all():
            raise ValueError("latent_actions must contain only finite values")
        if time.device != state.device:
            raise ValueError("time and observations must share a device")
        time_features = self.time_embedding(time)
        if time_features.shape[0] != state.shape[0]:
            raise ValueError("time and observations must share a batch dimension")
        velocity = self.velocity_head(
            torch.cat((state, time_features, latent_actions), dim=1)
        )
        if not torch.isfinite(velocity).all():
            raise RuntimeError("velocity output must contain only finite values")
        return velocity


@dataclass(frozen=True)
class FlowSample:
    latent_actions: torch.Tensor
    actions: torch.Tensor
    nfe: int
    path: torch.Tensor | None


class EulerFlowSampler:
    def __init__(self, velocity_model: nn.Module) -> None:
        action_size = getattr(velocity_model, "action_size", None)
        if not isinstance(action_size, int) or action_size <= 0:
            raise TypeError("velocity_model must expose a positive integer action_size")
        self.velocity_model = velocity_model
        self.action_size = action_size

    def sample(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        nfe: int,
        generator: torch.Generator | None = None,
        initial_noise: torch.Tensor | None = None,
        return_path: bool = False,
    ) -> FlowSample:
        if isinstance(nfe, bool) or not isinstance(nfe, int) or nfe <= 0:
            raise ValueError("nfe must be a positive integer")
        flattened = flatten_observations(observations)
        batch_size = flattened.shape[0]
        expected_shape = (batch_size, self.action_size)
        if initial_noise is None:
            latent_actions = torch.randn(
                expected_shape,
                dtype=torch.float32,
                device=flattened.device,
                generator=generator,
            )
        else:
            if initial_noise.dtype != torch.float32:
                raise TypeError("initial_noise must have dtype float32")
            if initial_noise.shape != expected_shape:
                raise ValueError(f"initial_noise must have shape {expected_shape}")
            if initial_noise.device != flattened.device:
                raise ValueError("initial_noise and observations must share a device")
            if not torch.isfinite(initial_noise).all():
                raise ValueError("initial_noise must contain only finite values")
            latent_actions = initial_noise.clone()
        path = [latent_actions] if return_path else None
        dt = 1.0 / float(nfe)
        for step in range(nfe):
            time = torch.full(
                (batch_size, 1),
                step * dt,
                dtype=torch.float32,
                device=flattened.device,
            )
            velocity = self.velocity_model(observations, latent_actions, time)
            if velocity.shape != expected_shape:
                raise ValueError(f"velocity must have shape {expected_shape}")
            if velocity.dtype != torch.float32 or velocity.device != flattened.device:
                raise TypeError("velocity must be float32 on the observation device")
            if not torch.isfinite(velocity).all():
                raise RuntimeError("velocity must contain only finite values")
            latent_actions = latent_actions + dt * velocity
            if path is not None:
                path.append(latent_actions)
        actions = torch.tanh(latent_actions)
        return FlowSample(
            latent_actions=latent_actions,
            actions=actions,
            nfe=nfe,
            path=None if path is None else torch.stack(path, dim=1),
        )
