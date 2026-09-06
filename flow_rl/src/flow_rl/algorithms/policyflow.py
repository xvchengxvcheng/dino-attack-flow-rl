from __future__ import annotations

from dataclasses import asdict, dataclass

import torch
from torch.distributions import Normal
from torch.nn import functional as F

from flow_rl.algorithms.ppo import normalize_advantages
from flow_rl.models.policyflow_policy import PolicyFlowActorCritic


def _float_tensor(value: torch.Tensor, *, name: str) -> None:
    if not isinstance(value, torch.Tensor) or value.dtype != torch.float32:
        raise TypeError(f"{name} must be a float32 tensor")
    if not torch.isfinite(value).all():
        raise ValueError(f"{name} must be finite")


@dataclass(frozen=True)
class PolicyFlowVariation:
    delta_velocity: torch.Tensor
    delta_std: torch.Tensor
    brownian_loss: torch.Tensor
    velocity_norm: torch.Tensor


def policyflow_variation(
    policy: PolicyFlowActorCritic,
    observations: tuple[torch.Tensor, ...],
    *,
    x0: torch.Tensor,
    x1: torch.Tensor,
    time: torch.Tensor,
    old_velocity: torch.Tensor | None = None,
) -> PolicyFlowVariation:
    for name, value in (("x0", x0), ("x1", x1), ("time", time)):
        _float_tensor(value, name=name)
    if x0.shape != x1.shape or x0.ndim != 2:
        raise ValueError("x0 and x1 must have the same (batch, action_size) shape")
    if time.shape != (x0.shape[0], 1):
        raise ValueError("time must have shape (batch, 1)")
    xt = (1.0 - time) * x0 + time * x1
    if old_velocity is None:
        with torch.inference_mode():
            old_velocity = policy.snapshot(observations, xt, time).detach()
    else:
        _float_tensor(old_velocity, name="old_velocity")
        if old_velocity.shape != x0.shape:
            raise ValueError("old_velocity must match x0 shape")
        old_velocity = old_velocity.detach()
    current_velocity = policy.actor(observations, xt, time)
    delta_velocity = current_velocity - old_velocity
    brownian_loss = F.mse_loss(
        (1.0 - time) * current_velocity,
        xt - time * old_velocity,
    )
    result = PolicyFlowVariation(
        delta_velocity=delta_velocity,
        delta_std=policy.delta_std(batch_size=x0.shape[0]),
        brownian_loss=brownian_loss,
        velocity_norm=current_velocity.norm(dim=1).mean(),
    )
    if not all(
        torch.isfinite(value).all()
        for value in (
            result.delta_velocity,
            result.delta_std,
            result.brownian_loss,
            result.velocity_norm,
        )
    ):
        raise RuntimeError("PolicyFlow variation must be finite")
    return result


@dataclass(frozen=True)
class PolicyFlowSurrogate:
    policy_loss: torch.Tensor
    ratios: torch.Tensor
    log_ratios: torch.Tensor
    clip_fraction: torch.Tensor
    approximate_kl: torch.Tensor


def policyflow_clipped_surrogate(
    *,
    new_log_probs: torch.Tensor,
    old_log_probs: torch.Tensor,
    advantages: torch.Tensor,
    clip_range: float,
) -> PolicyFlowSurrogate:
    for name, value in (
        ("new_log_probs", new_log_probs),
        ("old_log_probs", old_log_probs),
        ("advantages", advantages),
    ):
        _float_tensor(value, name=name)
    if new_log_probs.shape != old_log_probs.shape or new_log_probs.shape != advantages.shape:
        raise ValueError("log probabilities and advantages must share a shape")
    if not 0.0 <= clip_range < 1.0:
        raise ValueError("clip_range must be within [0, 1)")
    # Match the fixed official implementation exactly; the updater rejects
    # non-finite metrics rather than silently changing the surrogate.
    log_ratios = new_log_probs - old_log_probs
    ratios = torch.exp(log_ratios)
    unclipped = ratios * advantages
    clipped = torch.clamp(ratios, 1.0 - clip_range, 1.0 + clip_range) * advantages
    return PolicyFlowSurrogate(
        policy_loss=-torch.minimum(unclipped, clipped).mean(),
        ratios=ratios,
        log_ratios=log_ratios,
        clip_fraction=(torch.abs(ratios - 1.0) > clip_range).float().mean(),
        approximate_kl=((ratios - 1.0) - log_ratios).mean(),
    )


def gaussian_kl_old_to_new(
    new_mean: torch.Tensor,
    new_std: torch.Tensor,
    old_mean: torch.Tensor,
    old_std: torch.Tensor,
) -> torch.Tensor:
    for name, value in (
        ("new_mean", new_mean), ("new_std", new_std),
        ("old_mean", old_mean), ("old_std", old_std),
    ):
        _float_tensor(value, name=name)
    if not (new_mean.shape == new_std.shape == old_mean.shape == old_std.shape):
        raise ValueError("Gaussian parameters must share a shape")
    if torch.any(new_std <= 0.0) or torch.any(old_std <= 0.0):
        raise ValueError("Gaussian standard deviations must be positive")
    return (
        torch.log(new_std / old_std)
        + (old_std.square() + (old_mean - new_mean).square()) / (2.0 * new_std.square())
        - 0.5
    ).sum(dim=1)


@dataclass(frozen=True)
class PolicyFlowBatch:
    observations: tuple[torch.Tensor, ...]
    flow_x0: torch.Tensor
    actions_prior: torch.Tensor
    delta_actions: torch.Tensor
    actions: torch.Tensor
    old_delta_std: torch.Tensor
    old_delta_log_probs: torch.Tensor
    old_velocity_grid: torch.Tensor
    old_values: torch.Tensor
    advantages: torch.Tensor
    returns: torch.Tensor

    def __post_init__(self) -> None:
        fields = (
            "flow_x0", "actions_prior", "delta_actions", "actions",
            "old_delta_std", "old_delta_log_probs", "old_values",
            "advantages", "returns", "old_velocity_grid",
        )
        for name in fields:
            _float_tensor(getattr(self, name), name=name)
        batch_size, action_size = self.flow_x0.shape
        if self.flow_x0.ndim != 2:
            raise ValueError("flow_x0 must have shape (batch, action_size)")
        if any(
            getattr(self, name).shape != (batch_size, action_size)
            for name in ("actions_prior", "delta_actions", "actions", "old_delta_std")
        ):
            raise ValueError("PolicyFlow action fields must share a shape")
        if any(
            getattr(self, name).shape != (batch_size,)
            for name in ("old_delta_log_probs", "old_values", "advantages", "returns")
        ):
            raise ValueError("PolicyFlow scalar fields must have shape (batch,)")
        if self.old_velocity_grid.ndim != 3 or self.old_velocity_grid.shape[0] != batch_size \
                or self.old_velocity_grid.shape[2] != action_size:
            raise ValueError("old_velocity_grid must have shape (batch, grid, action_size)")
        if not self.observations or any(item.shape[0] != batch_size for item in self.observations):
            raise ValueError("observations must match PolicyFlow batch size")
        if torch.any(self.actions < -1.0) or torch.any(self.actions > 1.0):
            raise ValueError("actions must be bounded")
        if torch.any(self.old_delta_std <= 0.0):
            raise ValueError("old_delta_std must be positive")

    def __len__(self) -> int:
        return self.flow_x0.shape[0]

    def select(self, indices: torch.Tensor) -> "PolicyFlowBatch":
        selected = indices.to(self.flow_x0.device)
        return PolicyFlowBatch(
            observations=tuple(item[selected] for item in self.observations),
            flow_x0=self.flow_x0[selected],
            actions_prior=self.actions_prior[selected],
            delta_actions=self.delta_actions[selected],
            actions=self.actions[selected],
            old_delta_std=self.old_delta_std[selected],
            old_delta_log_probs=self.old_delta_log_probs[selected],
            old_velocity_grid=self.old_velocity_grid[selected],
            old_values=self.old_values[selected],
            advantages=self.advantages[selected],
            returns=self.returns[selected],
        )


@dataclass(frozen=True)
class PolicyFlowMetrics:
    policy_loss: float
    critic_loss: float
    gaussian_entropy_loss: float
    brownian_loss: float
    total_actor_loss: float
    approximate_kl: float
    analytic_kl: float
    clip_fraction: float
    ratio_mean: float
    ratio_min: float
    ratio_max: float
    delta_std_mean: float
    velocity_norm: float
    actor_gradient_norm: float
    critic_gradient_norm: float
    samples_processed: int
    minibatches: int

    def as_dict(self) -> dict[str, float | int]:
        return asdict(self)


class PolicyFlowUpdater:
    def __init__(
        self,
        policy: PolicyFlowActorCritic,
        *,
        actor_learning_rate: float,
        critic_learning_rate: float,
        clip_range: float,
        gaussian_entropy_coefficient: float,
        brownian_coefficient: float,
        value_clip: float,
        max_gradient_norm: float,
        batch_size: int,
        epochs: int,
        generator: torch.Generator,
    ) -> None:
        if min(actor_learning_rate, critic_learning_rate, max_gradient_norm) <= 0.0:
            raise ValueError("learning rates and gradient norm must be positive")
        if not 0.0 <= clip_range < 1.0:
            raise ValueError("clip_range must be within [0, 1)")
        if min(gaussian_entropy_coefficient, brownian_coefficient, value_clip) < 0.0:
            raise ValueError("loss coefficients and value_clip must be nonnegative")
        if batch_size <= 0 or epochs <= 0:
            raise ValueError("batch_size and epochs must be positive")
        self.policy = policy
        self._actor_parameters = tuple(policy.actor.parameters()) + (policy.log_std,)
        self._critic_parameters = tuple(policy.critic.parameters())
        self.actor_optimizer = torch.optim.AdamW(
            self._actor_parameters, lr=actor_learning_rate, weight_decay=1e-5
        )
        self.critic_optimizer = torch.optim.AdamW(
            self._critic_parameters, lr=critic_learning_rate, weight_decay=1e-5
        )
        self._clip_range = float(clip_range)
        self._entropy_coefficient = float(gaussian_entropy_coefficient)
        self._brownian_coefficient = float(brownian_coefficient)
        self._value_clip = float(value_clip)
        self._max_gradient_norm = float(max_gradient_norm)
        self._batch_size = int(batch_size)
        self._epochs = int(epochs)
        self._generator = generator

    def update(self, batch: PolicyFlowBatch) -> PolicyFlowMetrics:
        if len(batch) == 0:
            raise ValueError("PolicyFlow batch cannot be empty")
        normalized = PolicyFlowBatch(
            observations=batch.observations,
            flow_x0=batch.flow_x0,
            actions_prior=batch.actions_prior,
            delta_actions=batch.delta_actions,
            actions=batch.actions,
            old_delta_std=batch.old_delta_std,
            old_delta_log_probs=batch.old_delta_log_probs,
            old_velocity_grid=batch.old_velocity_grid,
            old_values=batch.old_values,
            advantages=normalize_advantages(batch.advantages),
            returns=batch.returns,
        )
        names = (
            "policy_loss", "critic_loss", "gaussian_entropy_loss",
            "brownian_loss", "total_actor_loss", "approximate_kl",
            "analytic_kl", "clip_fraction", "ratio_mean", "delta_std_mean",
            "velocity_norm", "actor_gradient_norm", "critic_gradient_norm",
        )
        totals = {name: 0.0 for name in names}
        ratio_min = float("inf")
        ratio_max = float("-inf")
        samples_processed = 0
        minibatches = 0
        grid_denominator = 2 * self.policy.solver_steps
        for _ in range(self._epochs):
            permutation = torch.randperm(
                len(normalized), device=normalized.flow_x0.device,
                generator=self._generator,
            )
            for offset in range(0, len(normalized), self._batch_size):
                selected = normalized.select(permutation[offset : offset + self._batch_size])
                time_indices = torch.randint(
                    0, grid_denominator + 1, (len(selected), 1),
                    device=selected.flow_x0.device, generator=self._generator,
                )
                time = time_indices.float() / float(grid_denominator)
                variation = policyflow_variation(
                    self.policy,
                    selected.observations,
                    x0=selected.flow_x0,
                    x1=selected.actions_prior,
                    time=time,
                    old_velocity=selected.old_velocity_grid[
                        torch.arange(len(selected), device=selected.flow_x0.device),
                        time_indices[:, 0],
                    ],
                )
                distribution = Normal(variation.delta_velocity, variation.delta_std)
                new_log_probs = distribution.log_prob(selected.delta_actions).sum(dim=1)
                surrogate = policyflow_clipped_surrogate(
                    new_log_probs=new_log_probs,
                    old_log_probs=selected.old_delta_log_probs,
                    advantages=selected.advantages,
                    clip_range=self._clip_range,
                )
                entropy_loss = -self._entropy_coefficient * distribution.entropy().sum(
                    dim=1
                ).mean()
                scaled_brownian = self._brownian_coefficient * variation.brownian_loss
                actor_loss = surrogate.policy_loss + entropy_loss + scaled_brownian
                analytic_kl = gaussian_kl_old_to_new(
                    variation.delta_velocity,
                    variation.delta_std,
                    torch.zeros_like(variation.delta_velocity),
                    selected.old_delta_std,
                ).mean()

                self.actor_optimizer.zero_grad(set_to_none=True)
                actor_loss.backward()
                actor_norm = torch.nn.utils.clip_grad_norm_(
                    self._actor_parameters, self._max_gradient_norm
                )
                self.actor_optimizer.step()

                predicted_values = self.policy.critic(selected.observations)
                if self._value_clip > 0.0:
                    predicted_values = selected.old_values + torch.clamp(
                        predicted_values - selected.old_values,
                        -self._value_clip,
                        self._value_clip,
                    )
                critic_loss = F.mse_loss(predicted_values, selected.returns)
                self.critic_optimizer.zero_grad(set_to_none=True)
                critic_loss.backward()
                critic_norm = torch.nn.utils.clip_grad_norm_(
                    self._critic_parameters, self._max_gradient_norm
                )
                self.critic_optimizer.step()

                values = {
                    "policy_loss": surrogate.policy_loss,
                    "critic_loss": critic_loss,
                    "gaussian_entropy_loss": entropy_loss,
                    "brownian_loss": scaled_brownian,
                    "total_actor_loss": actor_loss,
                    "approximate_kl": surrogate.approximate_kl,
                    "analytic_kl": analytic_kl,
                    "clip_fraction": surrogate.clip_fraction,
                    "ratio_mean": surrogate.ratios.mean(),
                    "delta_std_mean": variation.delta_std.mean(),
                    "velocity_norm": variation.velocity_norm,
                    "actor_gradient_norm": actor_norm,
                    "critic_gradient_norm": critic_norm,
                }
                if not all(torch.isfinite(value).all() for value in values.values()):
                    raise RuntimeError("PolicyFlow update produced non-finite metrics")
                weight = len(selected)
                for name, value in values.items():
                    totals[name] += float(value.detach().item()) * weight
                ratio_min = min(ratio_min, float(surrogate.ratios.min().detach().item()))
                ratio_max = max(ratio_max, float(surrogate.ratios.max().detach().item()))
                samples_processed += weight
                minibatches += 1
        self.policy.refresh_snapshot()
        averaged = {name: totals[name] / samples_processed for name in names}
        return PolicyFlowMetrics(
            **averaged,
            ratio_min=ratio_min,
            ratio_max=ratio_max,
            samples_processed=samples_processed,
            minibatches=minibatches,
        )
