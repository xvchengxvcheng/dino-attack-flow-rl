from __future__ import annotations

from dataclasses import asdict, dataclass

import torch
from torch import nn
from torch.distributions import Normal

from flow_rl.algorithms.ppo import normalize_advantages
from flow_rl.models.reinflow_policy import ReinFlowActorCritic


def _validate_float(value: torch.Tensor, *, name: str) -> None:
    if not isinstance(value, torch.Tensor) or value.dtype != torch.float32:
        raise TypeError(f"{name} must be a float32 tensor")
    if not torch.isfinite(value).all():
        raise ValueError(f"{name} must be finite")


@dataclass(frozen=True)
class ReinFlowChainStatistics:
    log_probs: torch.Tensor
    entropy_rate: torch.Tensor
    mean_noise_std: torch.Tensor


def reinflow_chain_statistics(
    policy: ReinFlowActorCritic,
    observations: tuple[torch.Tensor, ...],
    chains: torch.Tensor,
    *,
    account_for_initial_stochasticity: bool = True,
) -> ReinFlowChainStatistics:
    _validate_float(chains, name="chains")
    if chains.ndim != 3 or chains.shape[1:] != (
        policy.nfe + 1,
        policy.action_size,
    ):
        raise ValueError("chains must have shape (batch, nfe + 1, action_size)")
    batch_size = chains.shape[0]
    if any(item.shape[0] != batch_size for item in observations):
        raise ValueError("observations and chains must share a batch dimension")
    log_prob = torch.zeros(batch_size, dtype=torch.float32, device=chains.device)
    entropy = torch.zeros_like(log_prob)
    probability_steps = 0
    if account_for_initial_stochasticity:
        initial = Normal(torch.zeros_like(chains[:, 0]), torch.ones_like(chains[:, 0]))
        log_prob = log_prob + initial.log_prob(chains[:, 0]).sum(dim=1)
        entropy = entropy + initial.entropy().sum(dim=1)
        probability_steps += 1
    stds: list[torch.Tensor] = []
    dt = 1.0 / policy.nfe
    for step in range(policy.nfe):
        sample_time = torch.full(
            (batch_size, 1),
            step * dt,
            dtype=torch.float32,
            device=chains.device,
        )
        previous = chains[:, step]
        velocity = policy.actor(observations, previous, sample_time)
        mean = torch.clamp(previous + dt * velocity, -1.0, 1.0)
        std = policy.noise_head(observations, sample_time, step=step)
        distribution = Normal(mean, std)
        log_prob = log_prob + distribution.log_prob(chains[:, step + 1]).sum(dim=1)
        entropy = entropy + distribution.entropy().sum(dim=1)
        stds.append(std)
        probability_steps += 1
    normalization = float(probability_steps * policy.action_size)
    result = ReinFlowChainStatistics(
        log_probs=log_prob / normalization,
        entropy_rate=entropy / normalization,
        mean_noise_std=torch.stack(stds, dim=1).mean(dim=(1, 2)),
    )
    if not all(
        torch.isfinite(value).all()
        for value in (result.log_probs, result.entropy_rate, result.mean_noise_std)
    ):
        raise RuntimeError("ReinFlow chain statistics must be finite")
    return result


@dataclass(frozen=True)
class ReinFlowSurrogate:
    policy_loss: torch.Tensor
    ratios: torch.Tensor
    log_ratios: torch.Tensor
    clip_fraction: torch.Tensor
    approximate_kl: torch.Tensor


def reinflow_clipped_policy_loss(
    *,
    new_log_probs: torch.Tensor,
    old_log_probs: torch.Tensor,
    advantages: torch.Tensor,
    clip_range: float,
    log_prob_min: float,
    log_prob_max: float,
) -> ReinFlowSurrogate:
    for name, value in (
        ("new_log_probs", new_log_probs),
        ("old_log_probs", old_log_probs),
        ("advantages", advantages),
    ):
        _validate_float(value, name=name)
    if new_log_probs.shape != old_log_probs.shape or new_log_probs.shape != advantages.shape:
        raise ValueError("log probabilities and advantages must share a shape")
    if clip_range < 0.0 or not log_prob_min < log_prob_max:
        raise ValueError("ratio clipping configuration is invalid")
    new_clamped = torch.clamp(new_log_probs, log_prob_min, log_prob_max)
    old_clamped = torch.clamp(old_log_probs, log_prob_min, log_prob_max)
    log_ratios = new_clamped - old_clamped
    ratios = torch.exp(log_ratios)
    unclipped = ratios * advantages
    clipped = torch.clamp(ratios, 1.0 - clip_range, 1.0 + clip_range) * advantages
    policy_loss = -torch.minimum(unclipped, clipped).mean()
    clip_fraction = (torch.abs(ratios - 1.0) > clip_range).float().mean()
    approximate_kl = ((ratios - 1.0) - log_ratios).mean()
    return ReinFlowSurrogate(
        policy_loss=policy_loss,
        ratios=ratios,
        log_ratios=log_ratios,
        clip_fraction=clip_fraction,
        approximate_kl=approximate_kl,
    )


@dataclass(frozen=True)
class ReinFlowBatch:
    observations: tuple[torch.Tensor, ...]
    chains: torch.Tensor
    actions: torch.Tensor
    old_log_probs: torch.Tensor
    old_values: torch.Tensor
    advantages: torch.Tensor
    returns: torch.Tensor

    def __post_init__(self) -> None:
        _validate_float(self.chains, name="chains")
        if self.chains.ndim != 3:
            raise ValueError("chains must have shape (batch, steps, action_size)")
        batch_size, _, action_size = self.chains.shape
        for name in ("actions", "old_log_probs", "old_values", "advantages", "returns"):
            value = getattr(self, name)
            _validate_float(value, name=name)
            if value.shape[0] != batch_size:
                raise ValueError(f"{name} batch dimension does not match chains")
        if self.actions.shape != (batch_size, action_size):
            raise ValueError("actions must have shape (batch, action_size)")
        if any(
            getattr(self, name).shape != (batch_size,)
            for name in ("old_log_probs", "old_values", "advantages", "returns")
        ):
            raise ValueError("scalar ReinFlow batch fields must have shape (batch,)")
        if torch.any(self.actions < -1.0) or torch.any(self.actions > 1.0):
            raise ValueError("actions must be bounded")
        if not self.observations or any(item.shape[0] != batch_size for item in self.observations):
            raise ValueError("observations must be nonempty and match batch size")

    def __len__(self) -> int:
        return self.chains.shape[0]

    def select(self, indices: torch.Tensor) -> "ReinFlowBatch":
        selected = indices.to(self.chains.device)
        return ReinFlowBatch(
            observations=tuple(item[selected] for item in self.observations),
            chains=self.chains[selected],
            actions=self.actions[selected],
            old_log_probs=self.old_log_probs[selected],
            old_values=self.old_values[selected],
            advantages=self.advantages[selected],
            returns=self.returns[selected],
        )


@dataclass(frozen=True)
class ReinFlowMetrics:
    policy_loss: float
    critic_loss: float
    entropy_rate: float
    total_actor_loss: float
    approximate_kl: float
    clip_fraction: float
    ratio_mean: float
    mean_noise_std: float
    actor_gradient_norm: float
    critic_gradient_norm: float
    actor_updated: bool
    samples_processed: int
    minibatches: int

    def as_dict(self) -> dict[str, float | int | bool]:
        return asdict(self)


class ReinFlowUpdater:
    def __init__(
        self,
        policy: ReinFlowActorCritic,
        *,
        actor_learning_rate: float,
        critic_learning_rate: float,
        clip_range: float,
        entropy_coefficient: float,
        max_gradient_norm: float,
        batch_size: int,
        epochs: int,
        critic_warmup_environment_steps: int,
        log_prob_min: float,
        log_prob_max: float,
        generator: torch.Generator,
    ) -> None:
        if min(actor_learning_rate, critic_learning_rate, max_gradient_norm) <= 0.0:
            raise ValueError("learning rates and gradient norm must be positive")
        if entropy_coefficient < 0.0 or batch_size <= 0 or epochs <= 0:
            raise ValueError("updater configuration is invalid")
        self.policy = policy
        actor_parameters = _unique_parameters(
            tuple(policy.actor.parameters()) + tuple(policy.noise_head.parameters())
        )
        self._actor_parameters = actor_parameters
        self._critic_parameters = tuple(policy.critic.parameters())
        self.actor_optimizer = torch.optim.Adam(actor_parameters, lr=actor_learning_rate)
        self.critic_optimizer = torch.optim.Adam(
            self._critic_parameters, lr=critic_learning_rate
        )
        self._clip_range = float(clip_range)
        self._entropy_coefficient = float(entropy_coefficient)
        self._max_gradient_norm = float(max_gradient_norm)
        self._batch_size = int(batch_size)
        self._epochs = int(epochs)
        self._warmup = int(critic_warmup_environment_steps)
        self._log_prob_min = float(log_prob_min)
        self._log_prob_max = float(log_prob_max)
        self._generator = generator

    def update(self, batch: ReinFlowBatch, *, environment_steps: int) -> ReinFlowMetrics:
        if len(batch) == 0:
            raise ValueError("ReinFlow batch cannot be empty")
        normalized = ReinFlowBatch(
            observations=batch.observations,
            chains=batch.chains,
            actions=batch.actions,
            old_log_probs=batch.old_log_probs,
            old_values=batch.old_values,
            advantages=normalize_advantages(batch.advantages),
            returns=batch.returns,
        )
        actor_active = environment_steps >= self._warmup
        totals = {name: 0.0 for name in (
            "policy_loss", "critic_loss", "entropy_rate", "total_actor_loss",
            "approximate_kl", "clip_fraction", "ratio_mean", "mean_noise_std",
            "actor_gradient_norm", "critic_gradient_norm",
        )}
        samples_processed = 0
        minibatches = 0
        for _ in range(self._epochs):
            permutation = torch.randperm(len(batch), generator=self._generator)
            for start in range(0, len(batch), self._batch_size):
                minibatch = normalized.select(permutation[start : start + self._batch_size])
                statistics = reinflow_chain_statistics(
                    self.policy, minibatch.observations, minibatch.chains
                )
                surrogate = reinflow_clipped_policy_loss(
                    new_log_probs=statistics.log_probs,
                    old_log_probs=minibatch.old_log_probs,
                    advantages=minibatch.advantages,
                    clip_range=self._clip_range,
                    log_prob_min=self._log_prob_min,
                    log_prob_max=self._log_prob_max,
                )
                total_actor_loss = surrogate.policy_loss - (
                    self._entropy_coefficient * statistics.entropy_rate.mean()
                )
                actor_gradient_norm = torch.tensor(0.0, device=batch.chains.device)
                if actor_active:
                    if not torch.isfinite(total_actor_loss):
                        raise RuntimeError("ReinFlow actor loss must be finite")
                    self.actor_optimizer.zero_grad(set_to_none=True)
                    total_actor_loss.backward()
                    actor_gradient_norm = nn.utils.clip_grad_norm_(
                        self._actor_parameters, self._max_gradient_norm
                    )
                    if not torch.isfinite(actor_gradient_norm):
                        raise RuntimeError("ReinFlow actor gradient must be finite")
                    self.actor_optimizer.step()
                values = self.policy.critic(minibatch.observations)
                critic_loss = torch.square(values - minibatch.returns).mean()
                self.critic_optimizer.zero_grad(set_to_none=True)
                critic_loss.backward()
                critic_gradient_norm = nn.utils.clip_grad_norm_(
                    self._critic_parameters, self._max_gradient_norm
                )
                if not torch.isfinite(critic_gradient_norm):
                    raise RuntimeError("ReinFlow critic gradient must be finite")
                self.critic_optimizer.step()
                count = len(minibatch)
                values_to_add = {
                    "policy_loss": surrogate.policy_loss,
                    "critic_loss": critic_loss,
                    "entropy_rate": statistics.entropy_rate.mean(),
                    "total_actor_loss": total_actor_loss,
                    "approximate_kl": surrogate.approximate_kl,
                    "clip_fraction": surrogate.clip_fraction,
                    "ratio_mean": surrogate.ratios.mean(),
                    "mean_noise_std": statistics.mean_noise_std.mean(),
                    "actor_gradient_norm": actor_gradient_norm,
                    "critic_gradient_norm": critic_gradient_norm,
                }
                for name, value in values_to_add.items():
                    if not torch.isfinite(value):
                        raise RuntimeError(f"ReinFlow metric {name} must be finite")
                    totals[name] += float(value.detach()) * count
                samples_processed += count
                minibatches += 1
        averaged = {name: value / samples_processed for name, value in totals.items()}
        return ReinFlowMetrics(
            **averaged,
            actor_updated=actor_active,
            samples_processed=samples_processed,
            minibatches=minibatches,
        )


def _unique_parameters(parameters: tuple[nn.Parameter, ...]) -> tuple[nn.Parameter, ...]:
    unique: list[nn.Parameter] = []
    seen: set[int] = set()
    for parameter in parameters:
        if id(parameter) not in seen:
            unique.append(parameter)
            seen.add(id(parameter))
    return tuple(unique)
