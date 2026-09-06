from __future__ import annotations

import copy
from collections.abc import Callable, Sequence
from dataclasses import dataclass

import torch
from torch import nn
from torch.distributions import Normal

from flow_rl.models.critic import ValueCritic
from flow_rl.models.flow import ConditionalVelocityMLP
from flow_rl.models.state_encoder import StateEncoder


@dataclass(frozen=True)
class PolicyFlowPrior:
    latent_actions: torch.Tensor
    solver_steps: int
    velocity_nfe: int


@dataclass(frozen=True)
class PolicyFlowPolicyOutput:
    actions: torch.Tensor
    flow_x0: torch.Tensor
    actions_prior: torch.Tensor
    delta_actions: torch.Tensor
    old_delta_std: torch.Tensor
    old_delta_log_probs: torch.Tensor
    old_velocity_grid: torch.Tensor
    values: torch.Tensor
    solver_steps: int
    velocity_nfe: int


class PolicyFlowActorCritic(nn.Module):
    def __init__(
        self,
        *,
        observation_shapes: Sequence[tuple[int, ...]],
        action_size: int,
        state_size: int = 128,
        time_embedding_size: int = 32,
        velocity_hidden_sizes: Sequence[int] = (128, 128),
        critic_hidden_sizes: Sequence[int] = (128, 128),
        solver_steps: int = 2,
        log_std_min: float = -20.0,
        log_std_max: float = 4.0,
        std_init: float = 1.0,
        encoder_factory: Callable[[], StateEncoder] | None = None,
    ) -> None:
        super().__init__()
        if action_size <= 0 or solver_steps <= 0:
            raise ValueError("action_size and solver_steps must be positive")
        if not log_std_min < log_std_max or std_init <= 0.0:
            raise ValueError("variance configuration is invalid")
        self.action_size = int(action_size)
        self.solver_steps = int(solver_steps)
        self.actor = ConditionalVelocityMLP(
            observation_shapes=observation_shapes,
            action_size=action_size,
            state_size=state_size,
            time_embedding_size=time_embedding_size,
            hidden_sizes=velocity_hidden_sizes,
            state_encoder=None if encoder_factory is None else encoder_factory(),
        )
        self.snapshot = copy.deepcopy(self.actor).requires_grad_(False)
        self.snapshot.eval()
        self.critic = ValueCritic(
            observation_shapes=observation_shapes,
            hidden_sizes=critic_hidden_sizes,
            encoder_factory=encoder_factory,
        )
        self.log_std = nn.Parameter(
            torch.full((self.action_size,), float(torch.log(torch.tensor(std_init))))
        )
        self.log_std_min = float(log_std_min)
        self.log_std_max = float(log_std_max)

    @property
    def velocity_nfe(self) -> int:
        return 2 * self.solver_steps

    def delta_std(self, *, batch_size: int | None = None) -> torch.Tensor:
        std = torch.exp(torch.clamp(self.log_std, self.log_std_min, self.log_std_max))
        return std if batch_size is None else std.unsqueeze(0).expand(batch_size, -1)

    def refresh_snapshot(self) -> None:
        self.snapshot.load_state_dict(self.actor.state_dict(), strict=True)
        self.snapshot.requires_grad_(False)
        self.snapshot.eval()

    def sample_prior(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        initial_noise: torch.Tensor,
    ) -> PolicyFlowPrior:
        batch_size = observations[0].shape[0]
        expected = (batch_size, self.action_size)
        if initial_noise.dtype != torch.float32 or initial_noise.shape != expected:
            raise TypeError("initial_noise must be float32 with policy action shape")
        if initial_noise.device != observations[0].device:
            raise ValueError("initial_noise and observations must share a device")
        if not torch.isfinite(initial_noise).all():
            raise ValueError("initial_noise must be finite")
        current = initial_noise.clone()
        dt = 1.0 / self.solver_steps
        for step in range(self.solver_steps):
            time = torch.full(
                (batch_size, 1), step * dt, dtype=torch.float32, device=current.device
            )
            first_velocity = self.actor(observations, current, time)
            midpoint = current + 0.5 * dt * first_velocity
            midpoint_time = time + 0.5 * dt
            midpoint_velocity = self.actor(observations, midpoint, midpoint_time)
            current = current + dt * midpoint_velocity
        return PolicyFlowPrior(
            latent_actions=current,
            solver_steps=self.solver_steps,
            velocity_nfe=self.velocity_nfe,
        )

    def act(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        evaluation: bool,
        generator: torch.Generator,
        initial_noise: torch.Tensor | None = None,
        delta_noise: torch.Tensor | None = None,
    ) -> PolicyFlowPolicyOutput:
        batch_size = observations[0].shape[0]
        expected = (batch_size, self.action_size)
        if initial_noise is None:
            initial_noise = torch.randn(
                expected,
                dtype=torch.float32,
                device=observations[0].device,
                generator=generator,
            )
        prior = self.sample_prior(observations, initial_noise=initial_noise)
        std = self.delta_std(batch_size=batch_size)
        if evaluation:
            delta = torch.zeros_like(prior.latent_actions)
        else:
            if delta_noise is None:
                delta_noise = torch.randn(
                    expected,
                    dtype=torch.float32,
                    device=observations[0].device,
                    generator=generator,
                )
            if delta_noise.dtype != torch.float32 or delta_noise.shape != expected:
                raise TypeError("delta_noise must be float32 with policy action shape")
            delta = delta_noise * std
        old_log_probs = Normal(torch.zeros_like(delta), std).log_prob(delta).sum(dim=1)
        # Persist the behavior snapshot's velocity on the exact time grid used
        # by the updater.  This makes each transition self-contained even when
        # an asynchronous environment returns its decision after an update.
        grid_denominator = 2 * self.solver_steps
        grid_times = torch.arange(
            grid_denominator + 1, dtype=torch.float32, device=prior.latent_actions.device
        ) / float(grid_denominator)
        old_velocity_grid = []
        with torch.inference_mode():
            for grid_time in grid_times:
                time = torch.full(
                    (batch_size, 1), float(grid_time), dtype=torch.float32,
                    device=prior.latent_actions.device,
                )
                xt = (1.0 - time) * initial_noise + time * prior.latent_actions
                old_velocity_grid.append(self.snapshot(observations, xt, time))
        old_velocity_grid_tensor = torch.stack(old_velocity_grid, dim=1).detach()
        return PolicyFlowPolicyOutput(
            actions=torch.tanh(prior.latent_actions + delta).detach(),
            flow_x0=initial_noise.detach(),
            actions_prior=prior.latent_actions.detach(),
            delta_actions=delta.detach(),
            old_delta_std=std.detach(),
            old_delta_log_probs=old_log_probs.detach(),
            old_velocity_grid=old_velocity_grid_tensor,
            values=self.critic(observations).detach(),
            solver_steps=self.solver_steps,
            velocity_nfe=self.velocity_nfe,
        )
