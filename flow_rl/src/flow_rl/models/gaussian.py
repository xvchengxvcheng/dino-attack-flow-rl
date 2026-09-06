from __future__ import annotations

import math
from collections.abc import Callable, Sequence
from dataclasses import dataclass

import torch
from torch import nn
from torch.nn import functional as F

from flow_rl.models.state_encoder import StateEncoder


_ACTION_EPSILON = 1e-6
_LOG_TWO = math.log(2.0)
_LOG_TWO_PI = math.log(2.0 * math.pi)


def flatten_observations(
    observations: tuple[torch.Tensor, ...],
) -> torch.Tensor:
    if not isinstance(observations, tuple) or not observations:
        raise TypeError("observations must be a non-empty tuple")
    batch_size: int | None = None
    flattened: list[torch.Tensor] = []
    for index, observation in enumerate(observations):
        if not isinstance(observation, torch.Tensor):
            raise TypeError(f"observations[{index}] must be a torch.Tensor")
        if observation.dtype != torch.float32:
            raise TypeError(f"observations[{index}] must have dtype float32")
        if observation.ndim < 2:
            raise ValueError(f"observations[{index}] must include batch and feature axes")
        if batch_size is None:
            batch_size = observation.shape[0]
        elif observation.shape[0] != batch_size:
            raise ValueError("observation streams must share a batch dimension")
        if not torch.isfinite(observation).all():
            raise ValueError(f"observations[{index}] must contain only finite values")
        feature_size = math.prod(observation.shape[1:])
        flattened.append(observation.reshape(observation.shape[0], feature_size))
    return torch.cat(flattened, dim=1)


def _validate_shapes(shapes: Sequence[tuple[int, ...]]) -> tuple[tuple[int, ...], ...]:
    normalized = tuple(tuple(int(dimension) for dimension in shape) for shape in shapes)
    if not normalized or any(not shape for shape in normalized):
        raise ValueError("observation_shapes must contain non-empty shapes")
    if any(dimension <= 0 for shape in normalized for dimension in shape):
        raise ValueError("observation dimensions must be positive")
    return normalized


def _feature_size(shapes: Sequence[tuple[int, ...]]) -> int:
    return sum(math.prod(shape) for shape in shapes)


def _build_encoder(input_size: int, hidden_sizes: Sequence[int]) -> tuple[nn.Module, int]:
    layers: list[nn.Module] = []
    previous_size = input_size
    for hidden_size in hidden_sizes:
        if hidden_size <= 0:
            raise ValueError("hidden sizes must be positive")
        layers.extend((nn.Linear(previous_size, hidden_size), nn.SiLU()))
        previous_size = hidden_size
    return nn.Sequential(*layers), previous_size


@dataclass(frozen=True)
class GaussianSample:
    actions: torch.Tensor
    pre_tanh: torch.Tensor
    log_probs: torch.Tensor
    entropy: torch.Tensor


class TanhGaussianActor(nn.Module):
    def __init__(
        self,
        *,
        observation_shapes: Sequence[tuple[int, ...]],
        action_size: int,
        hidden_sizes: Sequence[int] = (128, 128),
        encoder_factory: Callable[[], StateEncoder] | None = None,
    ) -> None:
        super().__init__()
        self.observation_shapes = _validate_shapes(observation_shapes)
        if action_size <= 0:
            raise ValueError("action_size must be positive")
        self.action_size = int(action_size)
        widths = tuple(int(size) for size in hidden_sizes)
        self._structured_encoder = encoder_factory is not None
        if encoder_factory is None:
            self.encoder, encoded_size = _build_encoder(
                _feature_size(self.observation_shapes),
                widths,
            )
        else:
            encoder = encoder_factory()
            if not isinstance(encoder, StateEncoder):
                raise TypeError("encoder_factory must return a StateEncoder")
            if encoder.observation_shapes != self.observation_shapes:
                raise ValueError("encoder observation_shapes do not match actor")
            self.encoder = encoder
            self.policy_head, encoded_size = _build_encoder(
                encoder.output_size,
                widths,
            )
        self.mean_head = nn.Linear(encoded_size, self.action_size)
        self.log_std = nn.Parameter(torch.zeros(self.action_size, dtype=torch.float32))

    def sample(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        deterministic: bool,
        generator: torch.Generator | None = None,
    ) -> GaussianSample:
        mean, log_std = self._distribution_parameters(observations)
        if deterministic:
            pre_tanh = mean
        else:
            noise = torch.randn(
                mean.shape,
                dtype=mean.dtype,
                device=mean.device,
                generator=generator,
            )
            pre_tanh = mean + torch.exp(log_std) * noise
        actions = torch.tanh(pre_tanh)
        log_probs = self._log_prob_from_pre_tanh(mean, log_std, pre_tanh)
        entropy = self._entropy(log_std)
        return GaussianSample(actions, pre_tanh, log_probs, entropy)

    def evaluate_actions(
        self,
        observations: tuple[torch.Tensor, ...],
        actions: torch.Tensor,
    ) -> tuple[torch.Tensor, torch.Tensor]:
        mean, log_std = self._distribution_parameters(observations)
        expected_shape = (mean.shape[0], self.action_size)
        if actions.shape != expected_shape:
            raise ValueError(f"actions shape must be {expected_shape}")
        if actions.dtype != torch.float32:
            raise TypeError("actions must have dtype float32")
        if actions.device != mean.device:
            raise ValueError("actions and observations must share a device")
        if not torch.isfinite(actions).all():
            raise ValueError("actions must contain only finite values")
        if torch.any(actions < -1.0) or torch.any(actions > 1.0):
            raise ValueError("actions must be within [-1, 1]")
        capped = torch.clamp(actions, -1.0 + _ACTION_EPSILON, 1.0 - _ACTION_EPSILON)
        pre_tanh = torch.atanh(capped)
        return self._log_prob_from_pre_tanh(mean, log_std, pre_tanh), self._entropy(
            log_std
        )

    def _distribution_parameters(
        self, observations: tuple[torch.Tensor, ...]
    ) -> tuple[torch.Tensor, torch.Tensor]:
        if len(observations) != len(self.observation_shapes):
            raise ValueError("observation stream count does not match actor")
        for index, (observation, shape) in enumerate(
            zip(observations, self.observation_shapes)
        ):
            if observation.shape[1:] != shape:
                raise ValueError(
                    f"observations[{index}] feature shape must be {shape}"
                )
        if self._structured_encoder:
            encoded = self.policy_head(self.encoder(observations))
        else:
            features = flatten_observations(observations)
            encoded = self.encoder(features)
        mean = self.mean_head(encoded)
        log_std = torch.clamp(self.log_std, min=-20.0, max=2.0).expand_as(mean)
        return mean, log_std

    @staticmethod
    def _log_prob_from_pre_tanh(
        mean: torch.Tensor,
        log_std: torch.Tensor,
        pre_tanh: torch.Tensor,
    ) -> torch.Tensor:
        inverse_variance = torch.exp(-2.0 * log_std)
        base_log_prob = (
            -0.5 * torch.square(pre_tanh - mean) * inverse_variance
            - log_std
            - 0.5 * _LOG_TWO_PI
        )
        log_jacobian = 2.0 * (
            _LOG_TWO - pre_tanh - F.softplus(-2.0 * pre_tanh)
        )
        return torch.sum(base_log_prob - log_jacobian, dim=-1)

    @staticmethod
    def _entropy(log_std: torch.Tensor) -> torch.Tensor:
        return torch.sum(0.5 * (1.0 + _LOG_TWO_PI) + log_std, dim=-1)
