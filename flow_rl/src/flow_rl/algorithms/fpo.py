from __future__ import annotations

from dataclasses import asdict, dataclass

import torch
from torch import nn
from torch.nn import functional as F

from flow_rl.algorithms.ppo import linear_schedule, normalize_advantages


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
    if batch_size is not None and tensor.shape[0] != batch_size:
        raise ValueError(f"{name} batch dimension must be {batch_size}")
    if not torch.isfinite(tensor).all():
        raise ValueError(f"{name} must contain only finite values")


@dataclass(frozen=True)
class FPOBatch:
    observations: tuple[torch.Tensor, ...]
    latent_actions: torch.Tensor
    bounded_actions: torch.Tensor
    loss_eps: torch.Tensor
    loss_t: torch.Tensor
    old_cfm_losses: torch.Tensor
    old_values: torch.Tensor
    advantages: torch.Tensor
    returns: torch.Tensor

    def __post_init__(self) -> None:
        if not isinstance(self.observations, tuple) or not self.observations:
            raise TypeError("observations must be a non-empty tuple")
        _validate_float_tensor(self.latent_actions, name="latent_actions")
        if self.latent_actions.ndim != 2:
            raise ValueError("latent_actions must have shape (batch, action_size)")
        batch_size, action_size = self.latent_actions.shape
        device = self.latent_actions.device
        _validate_float_tensor(self.bounded_actions, name="bounded_actions", batch_size=batch_size)
        if self.bounded_actions.shape != (batch_size, action_size):
            raise ValueError("bounded_actions must match latent_actions")
        if torch.any(self.bounded_actions < -1.0) or torch.any(self.bounded_actions > 1.0):
            raise ValueError("bounded_actions must be within [-1, 1]")
        for index, observation in enumerate(self.observations):
            _validate_float_tensor(observation, name=f"observations[{index}]", batch_size=batch_size)
            if observation.device != device:
                raise ValueError("all FPOBatch tensors must share a device")
        _validate_float_tensor(self.loss_eps, name="loss_eps", batch_size=batch_size)
        if self.loss_eps.ndim != 3 or self.loss_eps.shape[2] != action_size:
            raise ValueError("loss_eps must have shape (batch, samples, action_size)")
        sample_count = self.loss_eps.shape[1]
        _validate_float_tensor(self.loss_t, name="loss_t", batch_size=batch_size)
        if self.loss_t.shape != (batch_size, sample_count, 1):
            raise ValueError("loss_t must have shape (batch, samples, 1)")
        _validate_float_tensor(self.old_cfm_losses, name="old_cfm_losses", batch_size=batch_size)
        if self.old_cfm_losses.shape != (batch_size, sample_count):
            raise ValueError("old_cfm_losses must have shape (batch, samples)")
        for name in ("old_values", "advantages", "returns"):
            tensor = getattr(self, name)
            _validate_float_tensor(tensor, name=name, batch_size=batch_size)
            if tensor.shape != (batch_size,):
                raise ValueError(f"{name} must have shape (batch,)")
        for tensor in (
            self.bounded_actions,
            self.loss_eps,
            self.loss_t,
            self.old_cfm_losses,
            self.old_values,
            self.advantages,
            self.returns,
        ):
            if tensor.device != device:
                raise ValueError("all FPOBatch tensors must share a device")

    def __len__(self) -> int:
        return self.latent_actions.shape[0]

    @property
    def device(self) -> torch.device:
        return self.latent_actions.device

    def select(self, indices: torch.Tensor) -> "FPOBatch":
        if indices.dtype != torch.int64 or indices.ndim != 1:
            raise TypeError("indices must be a one-dimensional int64 tensor")
        selected = indices.to(self.device)
        return FPOBatch(
            observations=tuple(item[selected] for item in self.observations),
            latent_actions=self.latent_actions[selected],
            bounded_actions=self.bounded_actions[selected],
            loss_eps=self.loss_eps[selected],
            loss_t=self.loss_t[selected],
            old_cfm_losses=self.old_cfm_losses[selected],
            old_values=self.old_values[selected],
            advantages=self.advantages[selected],
            returns=self.returns[selected],
        )


def conditional_flow_matching_loss(
    velocity_model: nn.Module,
    observations: tuple[torch.Tensor, ...],
    latent_actions: torch.Tensor,
    loss_eps: torch.Tensor,
    loss_t: torch.Tensor,
) -> torch.Tensor:
    _validate_float_tensor(latent_actions, name="latent_actions")
    if latent_actions.ndim != 2:
        raise ValueError("latent_actions must have shape (batch, action_size)")
    batch_size, action_size = latent_actions.shape
    _validate_float_tensor(loss_eps, name="loss_eps", batch_size=batch_size)
    if loss_eps.ndim != 3 or loss_eps.shape[2] != action_size:
        raise ValueError("loss_eps must have shape (batch, samples, action_size)")
    sample_count = loss_eps.shape[1]
    _validate_float_tensor(loss_t, name="loss_t", batch_size=batch_size)
    if loss_t.shape != (batch_size, sample_count, 1):
        raise ValueError("loss_t must have shape (batch, samples, 1)")
    if len(observations) == 0:
        raise ValueError("observations cannot be empty")
    expanded_observations = tuple(
        observation.unsqueeze(1)
        .expand(batch_size, sample_count, *observation.shape[1:])
        .reshape(batch_size * sample_count, *observation.shape[1:])
        for observation in observations
    )
    expanded_actions = latent_actions.unsqueeze(1).expand(-1, sample_count, -1)
    path_points = (1.0 - loss_t) * loss_eps + loss_t * expanded_actions
    targets = expanded_actions - loss_eps
    predictions = velocity_model(
        expanded_observations,
        path_points.reshape(batch_size * sample_count, action_size),
        loss_t.reshape(batch_size * sample_count, 1),
    ).reshape(batch_size, sample_count, action_size)
    if not torch.isfinite(predictions).all():
        raise RuntimeError("CFM velocity predictions must be finite")
    return torch.square(predictions - targets).mean(dim=2)


def fpo_ratio(
    old_losses: torch.Tensor,
    new_losses: torch.Tensor,
    *,
    difference_clip: float,
) -> tuple[torch.Tensor, torch.Tensor]:
    if difference_clip <= 0.0:
        raise ValueError("difference_clip must be positive")
    _validate_float_tensor(old_losses, name="old_losses")
    _validate_float_tensor(new_losses, name="new_losses")
    if old_losses.shape != new_losses.shape or old_losses.ndim != 2:
        raise ValueError("old_losses and new_losses must share shape (batch, samples)")
    differences = torch.clamp(
        old_losses - new_losses,
        -difference_clip,
        difference_clip,
    )
    log_ratio = torch.clamp(
        differences.mean(dim=1),
        -difference_clip,
        difference_clip,
    )
    ratio = torch.exp(log_ratio)
    if not torch.isfinite(ratio).all():
        raise RuntimeError("FPO ratios must be finite")
    return ratio, log_ratio


def fpo_clipped_policy_loss(
    ratio: torch.Tensor,
    advantages: torch.Tensor,
    *,
    clip_range: float,
) -> tuple[torch.Tensor, torch.Tensor]:
    if clip_range < 0.0:
        raise ValueError("clip_range cannot be negative")
    _validate_float_tensor(ratio, name="ratio")
    _validate_float_tensor(advantages, name="advantages")
    if ratio.shape != advantages.shape or ratio.ndim != 1:
        raise ValueError("ratio and advantages must share shape (batch,)")
    unclipped = ratio * advantages
    clipped = torch.clamp(ratio, 1.0 - clip_range, 1.0 + clip_range) * advantages
    loss = -torch.minimum(unclipped, clipped).mean()
    clip_fraction = (torch.abs(ratio - 1.0) > clip_range).float().mean()
    return loss, clip_fraction


@dataclass(frozen=True)
class FPOMetrics:
    policy_loss: float
    critic_loss: float
    flow_loss: float
    approximate_kl: float
    clip_fraction: float
    ratio_mean: float
    ratio_min: float
    ratio_max: float
    log_ratio_mean: float
    actor_gradient_norm: float
    critic_gradient_norm: float
    learning_rate: float
    clip_range: float
    samples_processed: int
    minibatches: int

    def as_dict(self) -> dict[str, float | int]:
        return asdict(self)


class FPOUpdater:
    def __init__(
        self,
        policy: nn.Module,
        *,
        learning_rate: float,
        final_learning_rate: float,
        clip_range: float,
        final_clip_range: float,
        max_gradient_norm: float,
        total_environment_steps: int,
        batch_size: int,
        epochs: int,
        difference_clip: float,
        positive_advantage: bool,
        generator: torch.Generator,
    ) -> None:
        if learning_rate <= 0.0 or final_learning_rate < 0.0:
            raise ValueError("learning rates are invalid")
        if clip_range < 0.0 or final_clip_range < 0.0:
            raise ValueError("clip ranges cannot be negative")
        if max_gradient_norm <= 0.0 or difference_clip <= 0.0:
            raise ValueError("gradient norm and difference clip must be positive")
        if total_environment_steps <= 0 or batch_size <= 0 or epochs <= 0:
            raise ValueError("step, batch, and epoch counts must be positive")
        self.policy = policy
        self.actor_optimizer = torch.optim.Adam(policy.actor.parameters(), lr=learning_rate)
        self.critic_optimizer = torch.optim.Adam(policy.critic.parameters(), lr=learning_rate)
        self._learning_rate = float(learning_rate)
        self._final_learning_rate = float(final_learning_rate)
        self._clip_range = float(clip_range)
        self._final_clip_range = float(final_clip_range)
        self._max_gradient_norm = float(max_gradient_norm)
        self._total_environment_steps = int(total_environment_steps)
        self._batch_size = int(batch_size)
        self._epochs = int(epochs)
        self._difference_clip = float(difference_clip)
        self._positive_advantage = bool(positive_advantage)
        self._generator = generator

    def update(self, batch: FPOBatch, *, environment_steps: int) -> FPOMetrics:
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
        for optimizer in (self.actor_optimizer, self.critic_optimizer):
            for group in optimizer.param_groups:
                group["lr"] = learning_rate
        advantages = (
            F.softplus(batch.advantages)
            if self._positive_advantage
            else normalize_advantages(batch.advantages)
        )
        normalized_batch = FPOBatch(
            observations=batch.observations,
            latent_actions=batch.latent_actions,
            bounded_actions=batch.bounded_actions,
            loss_eps=batch.loss_eps,
            loss_t=batch.loss_t,
            old_cfm_losses=batch.old_cfm_losses,
            old_values=batch.old_values,
            advantages=advantages,
            returns=batch.returns,
        )
        totals = {
            "policy_loss": 0.0,
            "critic_loss": 0.0,
            "flow_loss": 0.0,
            "approximate_kl": 0.0,
            "clip_fraction": 0.0,
            "ratio_mean": 0.0,
            "ratio_min": 0.0,
            "ratio_max": 0.0,
            "log_ratio_mean": 0.0,
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
                minibatch = normalized_batch.select(
                    permutation[start : start + self._batch_size]
                )
                new_losses = conditional_flow_matching_loss(
                    self.policy.actor,
                    minibatch.observations,
                    minibatch.latent_actions,
                    minibatch.loss_eps,
                    minibatch.loss_t,
                )
                ratio, log_ratio = fpo_ratio(
                    minibatch.old_cfm_losses,
                    new_losses,
                    difference_clip=self._difference_clip,
                )
                policy_loss, clip_fraction = fpo_clipped_policy_loss(
                    ratio,
                    minibatch.advantages,
                    clip_range=clip_range,
                )
                self.actor_optimizer.zero_grad(set_to_none=True)
                policy_loss.backward()
                actor_gradient_norm = nn.utils.clip_grad_norm_(
                    actor_parameters,
                    self._max_gradient_norm,
                )
                if not torch.isfinite(actor_gradient_norm):
                    raise RuntimeError("FPO actor gradient norm must be finite")
                self.actor_optimizer.step()

                values = self.policy.critic(minibatch.observations)
                critic_loss = torch.square(values - minibatch.returns).mean()
                self.critic_optimizer.zero_grad(set_to_none=True)
                critic_loss.backward()
                critic_gradient_norm = nn.utils.clip_grad_norm_(
                    critic_parameters,
                    self._max_gradient_norm,
                )
                if not torch.isfinite(critic_gradient_norm):
                    raise RuntimeError("FPO critic gradient norm must be finite")
                self.critic_optimizer.step()
                if any(
                    not torch.isfinite(parameter).all()
                    for parameter in self.policy.parameters()
                ):
                    raise RuntimeError("FPO parameters must remain finite")
                count = len(minibatch)
                metrics = {
                    "policy_loss": policy_loss,
                    "critic_loss": critic_loss,
                    "flow_loss": new_losses.mean(),
                    "approximate_kl": -log_ratio.mean(),
                    "clip_fraction": clip_fraction,
                    "ratio_mean": ratio.mean(),
                    "ratio_min": ratio.min(),
                    "ratio_max": ratio.max(),
                    "log_ratio_mean": log_ratio.mean(),
                    "actor_gradient_norm": actor_gradient_norm,
                    "critic_gradient_norm": critic_gradient_norm,
                }
                for name, value in metrics.items():
                    if not torch.isfinite(value):
                        raise RuntimeError(f"FPO metric {name} must be finite")
                    totals[name] += float(value.detach().cpu()) * count
                samples_processed += count
                minibatches += 1
        averaged = {name: value / samples_processed for name, value in totals.items()}
        return FPOMetrics(
            **averaged,
            learning_rate=learning_rate,
            clip_range=clip_range,
            samples_processed=samples_processed,
            minibatches=minibatches,
        )
