from __future__ import annotations

import math
from collections.abc import Sequence
from dataclasses import dataclass

import torch
from torch import nn
from torch.distributions import Normal

from flow_rl.models.critic import ValueCritic
from flow_rl.models.flow import ConditionalVelocityMLP


class LearnableFlowNoise(nn.Module):
    def __init__(
        self,
        *,
        actor: ConditionalVelocityMLP,
        action_size: int,
        nfe: int,
        min_std: float,
        max_std: float,
        hidden_sizes: Sequence[int] = (128, 128),
    ) -> None:
        super().__init__()
        if nfe <= 0:
            raise ValueError("nfe must be positive")
        if not 0.0 < min_std < max_std:
            raise ValueError("noise bounds must satisfy 0 < min_std < max_std")
        self.state_encoder = actor.state_encoder
        self.time_embedding = actor.time_embedding
        self.action_size = int(action_size)
        self.nfe = int(nfe)
        self.register_buffer("logvar_min", torch.tensor(2.0 * math.log(min_std)))
        self.register_buffer("logvar_max", torch.tensor(2.0 * math.log(max_std)))
        input_size = (
            actor.state_encoder.state_size
            + actor.time_embedding.embedding_size
            + 1
        )
        layers: list[nn.Module] = []
        for size in hidden_sizes:
            if int(size) <= 0:
                raise ValueError("noise hidden sizes must be positive")
            layers.extend((nn.Linear(input_size, int(size)), nn.SiLU()))
            input_size = int(size)
        layers.append(nn.Linear(input_size, self.action_size))
        self.noise_mlp = nn.Sequential(*layers)

    def forward(
        self,
        observations: tuple[torch.Tensor, ...],
        time: torch.Tensor,
        *,
        step: int,
    ) -> torch.Tensor:
        if isinstance(step, bool) or not isinstance(step, int) or not 0 <= step < self.nfe:
            raise ValueError("step must index a configured Flow transition")
        state = self.state_encoder(observations)
        if time.dtype != torch.float32 or time.shape != (state.shape[0], 1):
            raise TypeError("time must be float32 with shape (batch, 1)")
        time_features = self.time_embedding(time).detach()
        step_feature = torch.full_like(time, step / max(1, self.nfe - 1))
        raw = self.noise_mlp(torch.cat((state, time_features, step_feature), dim=1))
        midpoint = 0.5 * (self.logvar_min + self.logvar_max)
        half_range = 0.5 * (self.logvar_max - self.logvar_min)
        return torch.exp(0.5 * (midpoint + half_range * torch.tanh(raw)))


@dataclass(frozen=True)
class ReinFlowPolicyOutput:
    actions: torch.Tensor
    chains: torch.Tensor
    old_log_probs: torch.Tensor
    values: torch.Tensor
    entropy_rate: torch.Tensor
    mean_noise_std: torch.Tensor
    nfe: int


class ReinFlowActorCritic(nn.Module):
    def __init__(
        self,
        *,
        observation_shapes: Sequence[tuple[int, ...]],
        action_size: int,
        state_size: int = 128,
        time_embedding_size: int = 32,
        velocity_hidden_sizes: Sequence[int] = (128, 128),
        critic_hidden_sizes: Sequence[int] = (128, 128),
        noise_hidden_sizes: Sequence[int] = (128, 128),
        nfe: int = 4,
        min_noise_std: float = 0.1,
        max_noise_std: float = 0.24,
    ) -> None:
        super().__init__()
        if action_size <= 0:
            raise ValueError("action_size must be positive")
        self.action_size = int(action_size)
        self.nfe = int(nfe)
        self.actor = ConditionalVelocityMLP(
            observation_shapes=observation_shapes,
            action_size=action_size,
            state_size=state_size,
            time_embedding_size=time_embedding_size,
            hidden_sizes=velocity_hidden_sizes,
        )
        self.noise_head = LearnableFlowNoise(
            actor=self.actor,
            action_size=action_size,
            nfe=nfe,
            min_std=min_noise_std,
            max_std=max_noise_std,
            hidden_sizes=noise_hidden_sizes,
        )
        self.critic = ValueCritic(
            observation_shapes=observation_shapes,
            hidden_sizes=critic_hidden_sizes,
        )

    def act(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        evaluation: bool,
        generator: torch.Generator,
        initial_noise: torch.Tensor | None = None,
        transition_noise: torch.Tensor | None = None,
    ) -> ReinFlowPolicyOutput:
        batch_size = observations[0].shape[0]
        expected = (batch_size, self.action_size)
        if initial_noise is None:
            current = torch.randn(
                expected,
                dtype=torch.float32,
                device=observations[0].device,
                generator=generator,
            )
        else:
            if initial_noise.dtype != torch.float32 or initial_noise.shape != expected:
                raise TypeError("initial_noise has invalid dtype or shape")
            current = initial_noise.clone()
        if transition_noise is not None and transition_noise.shape != (
            batch_size,
            self.nfe,
            self.action_size,
        ):
            raise ValueError("transition_noise has invalid shape")
        chains = [current]
        log_prob = Normal(torch.zeros_like(current), torch.ones_like(current)).log_prob(
            current
        ).sum(dim=1)
        entropy = Normal(
            torch.zeros_like(current), torch.ones_like(current)
        ).entropy().sum(dim=1)
        noise_stds: list[torch.Tensor] = []
        dt = 1.0 / self.nfe
        for step in range(self.nfe):
            sample_time = torch.full(
                (batch_size, 1),
                step * dt,
                dtype=torch.float32,
                device=current.device,
            )
            velocity = self.actor(observations, current, sample_time)
            mean = torch.clamp(current + dt * velocity, -1.0, 1.0)
            std = self.noise_head(observations, sample_time, step=step)
            distribution = Normal(mean, std)
            if evaluation:
                current = mean
            else:
                if transition_noise is None:
                    epsilon = torch.randn(
                        expected,
                        dtype=torch.float32,
                        device=current.device,
                        generator=generator,
                    )
                else:
                    epsilon = transition_noise[:, step]
                current = mean + torch.clamp(epsilon, -3.0, 3.0) * std
            if step == self.nfe - 1:
                current = torch.clamp(current, -1.0, 1.0)
            log_prob = log_prob + distribution.log_prob(current).sum(dim=1)
            entropy = entropy + distribution.entropy().sum(dim=1)
            chains.append(current)
            noise_stds.append(std)
        normalization = float((self.nfe + 1) * self.action_size)
        values = self.critic(observations)
        return ReinFlowPolicyOutput(
            actions=current.detach(),
            chains=torch.stack(chains, dim=1).detach(),
            old_log_probs=(log_prob / normalization).detach(),
            values=values.detach(),
            entropy_rate=(entropy / normalization).detach(),
            mean_noise_std=torch.stack(noise_stds, dim=1).mean(dim=(1, 2)).detach(),
            nfe=self.nfe,
        )
