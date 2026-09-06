from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from types import MappingProxyType
from typing import Protocol

import numpy as np
import torch

from flow_rl.algorithms.ppo import PPOBatch
from flow_rl.data.gae import compute_gae
from flow_rl.envs.types import EnvStep
from flow_rl.models.policy import PolicyOutput
from flow_rl.training.on_policy import DecisionBatch, OnPolicyCollectorCore, RolloutCollection


class CollectorNormalizer(Protocol):
    def update(self, observations: tuple[np.ndarray, ...]) -> None: ...

    def normalize(self, observations: tuple[np.ndarray, ...]) -> tuple[np.ndarray, ...]: ...


class CollectorCritic(Protocol):
    def __call__(self, observations: tuple[torch.Tensor, ...]) -> torch.Tensor: ...


class CollectorPolicy(Protocol):
    critic: CollectorCritic

    def act(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        deterministic: bool,
        generator: torch.Generator | None = None,
    ) -> PolicyOutput: ...


@dataclass(frozen=True)
class PPOAuxiliary:
    old_log_prob: float


@dataclass(frozen=True)
class CollectedTransition:
    agent_id: int
    observation: tuple[np.ndarray, ...]
    action: np.ndarray
    old_log_prob: float
    reward: float
    value: float
    next_value: float
    terminated: bool
    truncated: bool


class PPOCollector:
    def __init__(
        self,
        *,
        policy: CollectorPolicy,
        normalizer: CollectorNormalizer,
        device: torch.device,
        gamma: float,
        gae_lambda: float,
        generator: torch.Generator,
    ) -> None:
        if not 0.0 <= gamma <= 1.0:
            raise ValueError("gamma must be within [0, 1]")
        if not 0.0 <= gae_lambda <= 1.0:
            raise ValueError("gae_lambda must be within [0, 1]")
        self._policy = policy
        self._normalizer = normalizer
        self._device = device
        self._gamma = float(gamma)
        self._gae_lambda = float(gae_lambda)
        self._generator = generator
        self._core: OnPolicyCollectorCore[PPOAuxiliary] = OnPolicyCollectorCore()

    @property
    def pending_agent_ids(self) -> tuple[int, ...]:
        return self._core.pending_agent_ids

    @property
    def completed_transition_count(self) -> int:
        return self._core.completed_transition_count

    def trajectories(self) -> Mapping[int, tuple[CollectedTransition, ...]]:
        return MappingProxyType(
            {
                agent_id: tuple(
                    CollectedTransition(
                        agent_id=transition.agent_id,
                        observation=transition.observation,
                        action=transition.action,
                        old_log_prob=transition.auxiliary.old_log_prob,
                        reward=transition.reward,
                        value=transition.value,
                        next_value=transition.next_value,
                        terminated=transition.terminated,
                        truncated=transition.truncated,
                    )
                    for transition in trajectory
                )
                for agent_id, trajectory in self._core.trajectories().items()
            }
        )

    def reset(self, step: EnvStep) -> RolloutCollection:
        return self._core.reset(step, decide=self._decide, bootstrap=self._bootstrap)

    def step(self, step: EnvStep) -> RolloutCollection:
        return self._core.step(step, decide=self._decide, bootstrap=self._bootstrap)

    def build_batch(self) -> PPOBatch:
        trajectories = self._core.trajectories()
        if self.completed_transition_count == 0:
            raise RuntimeError("no completed transitions are available")
        observation_streams: list[list[np.ndarray]] | None = None
        actions: list[np.ndarray] = []
        old_log_probs: list[float] = []
        old_values: list[float] = []
        advantages: list[np.ndarray] = []
        returns: list[np.ndarray] = []
        for agent_id in sorted(trajectories):
            trajectory = trajectories[agent_id]
            if not trajectory:
                continue
            if observation_streams is None:
                observation_streams = [[] for _ in trajectory[0].observation]
            rewards = np.asarray([item.reward for item in trajectory], dtype=np.float32)
            values = np.asarray([item.value for item in trajectory], dtype=np.float32)
            next_values = np.asarray([item.next_value for item in trajectory], dtype=np.float32)
            terminated = np.asarray([item.terminated for item in trajectory], dtype=bool)
            truncated = np.asarray([item.truncated for item in trajectory], dtype=bool)
            trajectory_advantages, trajectory_returns = compute_gae(
                rewards,
                values,
                next_values,
                terminated,
                truncated,
                gamma=self._gamma,
                gae_lambda=self._gae_lambda,
            )
            advantages.append(trajectory_advantages)
            returns.append(trajectory_returns)
            for transition in trajectory:
                for stream, observation in zip(observation_streams, transition.observation):
                    stream.append(observation)
                actions.append(transition.action)
                old_log_probs.append(transition.auxiliary.old_log_prob)
                old_values.append(transition.value)
        if observation_streams is None:
            raise RuntimeError("no completed transitions are available")
        batch = PPOBatch(
            observations=tuple(
                torch.as_tensor(np.stack(stream), device=self._device)
                for stream in observation_streams
            ),
            actions=torch.as_tensor(np.stack(actions), device=self._device),
            old_log_probs=torch.as_tensor(np.asarray(old_log_probs, dtype=np.float32), device=self._device),
            old_values=torch.as_tensor(np.asarray(old_values, dtype=np.float32), device=self._device),
            advantages=torch.as_tensor(np.concatenate(advantages), device=self._device),
            returns=torch.as_tensor(np.concatenate(returns), device=self._device),
        )
        self._core.clear_completed()
        return batch

    def _decide(self, observations: tuple[np.ndarray, ...]) -> DecisionBatch[PPOAuxiliary]:
        self._normalizer.update(observations)
        normalized = self._normalizer.normalize(observations)
        with torch.inference_mode():
            output = self._policy.act(
                self._to_tensors(normalized),
                deterministic=False,
                generator=self._generator,
            )
        actions = output.actions.detach().cpu().numpy().astype(np.float32, copy=True)
        values = output.values.detach().cpu().numpy().astype(np.float32, copy=True)
        log_probs = output.log_probs.detach().cpu().numpy()
        return DecisionBatch(
            observations=tuple(np.array(item, dtype=np.float32, copy=True) for item in normalized),
            actions=actions,
            values=values,
            auxiliaries=tuple(PPOAuxiliary(float(value)) for value in log_probs),
        )

    def _bootstrap(self, observations: tuple[np.ndarray, ...]) -> np.ndarray:
        normalized = self._normalizer.normalize(observations)
        with torch.inference_mode():
            values = self._policy.critic(self._to_tensors(normalized))
        return values.detach().cpu().numpy().astype(np.float32, copy=True)

    def _to_tensors(
        self,
        observations: tuple[np.ndarray, ...],
    ) -> tuple[torch.Tensor, ...]:
        return tuple(torch.as_tensor(item, device=self._device) for item in observations)
