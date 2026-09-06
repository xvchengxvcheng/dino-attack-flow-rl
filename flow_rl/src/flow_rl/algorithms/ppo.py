from __future__ import annotations

from dataclasses import asdict, dataclass

import torch
from torch import nn

from flow_rl.models.policy import GaussianActorCritic


def _validate_float_tensor(
    tensor: torch.Tensor,
    *,
    name: str,
    batch_size: int | None = None,
) -> None:
    if not isinstance(tensor, torch.Tensor):
        raise TypeError(f"{name} must be a torch.Tensor")
    if tensor.dtype != torch.float32:
        raise TypeError(f"{name} must have dtype float32")
    if tensor.ndim < 1:
        raise ValueError(f"{name} must include a batch dimension")
    if batch_size is not None and tensor.shape[0] != batch_size:
        raise ValueError(f"{name} batch dimension must be {batch_size}")
    if not torch.isfinite(tensor).all():
        raise ValueError(f"{name} must contain only finite values")


@dataclass(frozen=True)
class PPOBatch:
    observations: tuple[torch.Tensor, ...]
    actions: torch.Tensor
    old_log_probs: torch.Tensor
    old_values: torch.Tensor
    advantages: torch.Tensor
    returns: torch.Tensor

    def __post_init__(self) -> None:
        if not isinstance(self.observations, tuple) or not self.observations:
            raise TypeError("observations must be a non-empty tuple")
        _validate_float_tensor(self.actions, name="actions")
        if self.actions.ndim != 2:
            raise ValueError("actions must have shape (batch, action_size)")
        batch_size = self.actions.shape[0]
        device = self.actions.device
        for index, observation in enumerate(self.observations):
            _validate_float_tensor(
                observation,
                name=f"observations[{index}]",
                batch_size=batch_size,
            )
            if observation.device != device:
                raise ValueError("all PPOBatch tensors must share a device")
        for name in (
            "old_log_probs",
            "old_values",
            "advantages",
            "returns",
        ):
            tensor = getattr(self, name)
            _validate_float_tensor(tensor, name=name, batch_size=batch_size)
            if tensor.ndim != 1:
                raise ValueError(f"{name} must have shape (batch,)")
            if tensor.device != device:
                raise ValueError("all PPOBatch tensors must share a device")
        if torch.any(self.actions < -1.0) or torch.any(self.actions > 1.0):
            raise ValueError("actions must be within [-1, 1]")

    def __len__(self) -> int:
        return self.actions.shape[0]

    @property
    def device(self) -> torch.device:
        return self.actions.device

    def select(self, indices: torch.Tensor) -> "PPOBatch":
        if indices.dtype != torch.int64 or indices.ndim != 1:
            raise TypeError("indices must be a one-dimensional int64 tensor")
        selected = indices.to(self.device)
        return PPOBatch(
            observations=tuple(observation[selected] for observation in self.observations),
            actions=self.actions[selected],
            old_log_probs=self.old_log_probs[selected],
            old_values=self.old_values[selected],
            advantages=self.advantages[selected],
            returns=self.returns[selected],
        )


@dataclass(frozen=True)
class PPOMetrics:
    policy_loss: float
    value_loss: float
    entropy: float
    total_loss: float
    approximate_kl: float
    clip_fraction: float
    ratio_mean: float
    actor_gradient_norm: float
    critic_gradient_norm: float
    learning_rate: float
    clip_range: float
    entropy_coefficient: float
    samples_processed: int
    minibatches: int

    def as_dict(self) -> dict[str, float | int]:
        return asdict(self)


def linear_schedule(
    initial_value: float,
    final_value: float,
    environment_steps: int,
    total_environment_steps: int,
) -> float:
    if total_environment_steps <= 0:
        raise ValueError("total_environment_steps must be positive")
    progress = min(max(environment_steps / total_environment_steps, 0.0), 1.0)
    return float(initial_value + progress * (final_value - initial_value))


def normalize_advantages(advantages: torch.Tensor) -> torch.Tensor:
    _validate_float_tensor(advantages, name="advantages")
    if advantages.ndim != 1:
        raise ValueError("advantages must have shape (batch,)")
    return (advantages - advantages.mean()) / (
        advantages.std(unbiased=False) + 1e-8
    )


def clipped_policy_loss(
    new_log_probs: torch.Tensor,
    old_log_probs: torch.Tensor,
    advantages: torch.Tensor,
    *,
    clip_range: float,
) -> tuple[torch.Tensor, torch.Tensor, torch.Tensor]:
    if clip_range < 0.0:
        raise ValueError("clip_range cannot be negative")
    for name, tensor in (
        ("new_log_probs", new_log_probs),
        ("old_log_probs", old_log_probs),
        ("advantages", advantages),
    ):
        _validate_float_tensor(tensor, name=name)
    if new_log_probs.shape != old_log_probs.shape or new_log_probs.shape != advantages.shape:
        raise ValueError("policy loss inputs must share a shape")
    ratios = torch.exp(new_log_probs - old_log_probs)
    if not torch.isfinite(ratios).all():
        raise ValueError("policy ratios must be finite")
    unclipped = ratios * advantages
    clipped = torch.clamp(ratios, 1.0 - clip_range, 1.0 + clip_range) * advantages
    loss = -torch.minimum(unclipped, clipped).mean()
    clip_fraction = (torch.abs(ratios - 1.0) > clip_range).float().mean()
    return loss, ratios, clip_fraction


def clipped_value_loss(
    values: torch.Tensor,
    old_values: torch.Tensor,
    returns: torch.Tensor,
    *,
    clip_range: float,
) -> torch.Tensor:
    if clip_range < 0.0:
        raise ValueError("clip_range cannot be negative")
    for name, tensor in (
        ("values", values),
        ("old_values", old_values),
        ("returns", returns),
    ):
        _validate_float_tensor(tensor, name=name)
    if values.shape != old_values.shape or values.shape != returns.shape:
        raise ValueError("value loss inputs must share a shape")
    clipped_values = old_values + torch.clamp(
        values - old_values,
        -clip_range,
        clip_range,
    )
    unclipped_error = torch.square(values - returns)
    clipped_error = torch.square(clipped_values - returns)
    return 0.5 * torch.maximum(unclipped_error, clipped_error).mean()


class PPOUpdater:
    def __init__(
        self,
        policy: GaussianActorCritic,
        *,
        learning_rate: float,
        final_learning_rate: float,
        clip_range: float,
        final_clip_range: float,
        entropy_coefficient: float,
        final_entropy_coefficient: float,
        value_coefficient: float,
        max_gradient_norm: float,
        total_environment_steps: int,
        batch_size: int,
        epochs: int,
        generator: torch.Generator,
    ) -> None:
        if learning_rate <= 0.0 or final_learning_rate < 0.0:
            raise ValueError("learning rates must be positive/nonnegative")
        if clip_range < 0.0 or final_clip_range < 0.0:
            raise ValueError("clip ranges cannot be negative")
        if entropy_coefficient < 0.0 or final_entropy_coefficient < 0.0:
            raise ValueError("entropy coefficients cannot be negative")
        if value_coefficient < 0.0:
            raise ValueError("value_coefficient cannot be negative")
        if max_gradient_norm <= 0.0:
            raise ValueError("max_gradient_norm must be positive")
        if total_environment_steps <= 0 or batch_size <= 0 or epochs <= 0:
            raise ValueError("step, batch, and epoch counts must be positive")
        self.policy = policy
        self.optimizer = torch.optim.Adam(policy.parameters(), lr=learning_rate)
        self._learning_rate = float(learning_rate)
        self._final_learning_rate = float(final_learning_rate)
        self._clip_range = float(clip_range)
        self._final_clip_range = float(final_clip_range)
        self._entropy_coefficient = float(entropy_coefficient)
        self._final_entropy_coefficient = float(final_entropy_coefficient)
        self._value_coefficient = float(value_coefficient)
        self._max_gradient_norm = float(max_gradient_norm)
        self._total_environment_steps = int(total_environment_steps)
        self._batch_size = int(batch_size)
        self._epochs = int(epochs)
        self._generator = generator

    def update(self, batch: PPOBatch, *, environment_steps: int) -> PPOMetrics:
        if len(batch) == 0:
            raise ValueError("PPO batch cannot be empty")
        learning_rate = linear_schedule(
            self._learning_rate,
            self._final_learning_rate,
            environment_steps,
            self._total_environment_steps,
        )
        clip_range = linear_schedule(
            self._clip_range,
            self._final_clip_range,
            environment_steps,
            self._total_environment_steps,
        )
        entropy_coefficient = linear_schedule(
            self._entropy_coefficient,
            self._final_entropy_coefficient,
            environment_steps,
            self._total_environment_steps,
        )
        for group in self.optimizer.param_groups:
            group["lr"] = learning_rate
        normalized_advantages = normalize_advantages(batch.advantages)
        normalized_batch = PPOBatch(
            observations=batch.observations,
            actions=batch.actions,
            old_log_probs=batch.old_log_probs,
            old_values=batch.old_values,
            advantages=normalized_advantages,
            returns=batch.returns,
        )
        totals = {
            "policy_loss": 0.0,
            "value_loss": 0.0,
            "entropy": 0.0,
            "total_loss": 0.0,
            "approximate_kl": 0.0,
            "clip_fraction": 0.0,
            "ratio_mean": 0.0,
            "actor_gradient_norm": 0.0,
            "critic_gradient_norm": 0.0,
        }
        samples_processed = 0
        minibatches = 0
        actor_parameters = tuple(self.policy.actor.parameters())
        critic_parameters = tuple(self.policy.critic.parameters())
        for _ in range(self._epochs):
            permutation = torch.randperm(len(batch), generator=self._generator)
            for start in range(0, len(batch), self._batch_size):
                indices = permutation[start : start + self._batch_size]
                minibatch = normalized_batch.select(indices)
                new_log_probs, entropy, values = self.policy.evaluate_actions(
                    minibatch.observations,
                    minibatch.actions,
                )
                policy_loss, ratios, clip_fraction = clipped_policy_loss(
                    new_log_probs,
                    minibatch.old_log_probs,
                    minibatch.advantages,
                    clip_range=clip_range,
                )
                value_loss = clipped_value_loss(
                    values,
                    minibatch.old_values,
                    minibatch.returns,
                    clip_range=clip_range,
                )
                entropy_mean = entropy.mean()
                total_loss = (
                    policy_loss
                    + self._value_coefficient * value_loss
                    - entropy_coefficient * entropy_mean
                )
                if not torch.isfinite(total_loss):
                    raise RuntimeError("PPO total loss must be finite")
                self.optimizer.zero_grad(set_to_none=True)
                total_loss.backward()
                actor_gradient_norm = nn.utils.clip_grad_norm_(
                    actor_parameters,
                    self._max_gradient_norm,
                )
                critic_gradient_norm = nn.utils.clip_grad_norm_(
                    critic_parameters,
                    self._max_gradient_norm,
                )
                if not torch.isfinite(actor_gradient_norm) or not torch.isfinite(
                    critic_gradient_norm
                ):
                    raise RuntimeError("PPO gradient norms must be finite")
                self.optimizer.step()
                if any(
                    not torch.isfinite(parameter).all()
                    for parameter in self.policy.parameters()
                ):
                    raise RuntimeError("PPO parameters must remain finite")
                count = len(minibatch)
                approximate_kl = (minibatch.old_log_probs - new_log_probs).mean()
                values_to_accumulate = {
                    "policy_loss": policy_loss,
                    "value_loss": value_loss,
                    "entropy": entropy_mean,
                    "total_loss": total_loss,
                    "approximate_kl": approximate_kl,
                    "clip_fraction": clip_fraction,
                    "ratio_mean": ratios.mean(),
                    "actor_gradient_norm": actor_gradient_norm,
                    "critic_gradient_norm": critic_gradient_norm,
                }
                for name, value in values_to_accumulate.items():
                    totals[name] += float(value.detach().cpu()) * count
                samples_processed += count
                minibatches += 1
        averaged = {
            name: value / samples_processed for name, value in totals.items()
        }
        return PPOMetrics(
            **averaged,
            learning_rate=learning_rate,
            clip_range=clip_range,
            entropy_coefficient=entropy_coefficient,
            samples_processed=samples_processed,
            minibatches=minibatches,
        )
