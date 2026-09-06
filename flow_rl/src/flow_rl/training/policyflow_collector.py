from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from typing import Protocol

import numpy as np
import torch

from flow_rl.algorithms.policyflow import PolicyFlowBatch
from flow_rl.data.gae import compute_gae
from flow_rl.envs.types import EnvStep
from flow_rl.models.policyflow_policy import PolicyFlowPolicyOutput
from flow_rl.training.collector import CollectorCritic, CollectorNormalizer
from flow_rl.training.on_policy import (
    CollectedTransition,
    DecisionBatch,
    OnPolicyCollectorCore,
    RolloutCollection,
)


class PolicyFlowCollectorPolicy(Protocol):
    critic: CollectorCritic

    def act(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        evaluation: bool,
        generator: torch.Generator,
    ) -> PolicyFlowPolicyOutput: ...


@dataclass(frozen=True)
class PolicyFlowAuxiliary:
    flow_x0: np.ndarray
    actions_prior: np.ndarray
    delta_actions: np.ndarray
    old_delta_std: np.ndarray
    old_delta_log_prob: float
    old_velocity_grid: np.ndarray

    def __post_init__(self) -> None:
        arrays = {
            "flow_x0": self.flow_x0,
            "actions_prior": self.actions_prior,
            "delta_actions": self.delta_actions,
            "old_delta_std": self.old_delta_std,
            "old_velocity_grid": self.old_velocity_grid,
        }
        converted: dict[str, np.ndarray] = {}
        for name, value in arrays.items():
            array = np.array(value, dtype=np.float32, copy=True)
            if not np.isfinite(array).all():
                raise ValueError(f"{name} must be finite")
            array.setflags(write=False)
            converted[name] = array
            object.__setattr__(self, name, array)
        action_shape = converted["flow_x0"].shape
        if len(action_shape) != 1 or any(
            converted[name].shape != action_shape
            for name in ("actions_prior", "delta_actions", "old_delta_std")
        ):
            raise ValueError("PolicyFlow auxiliary action fields must share one shape")
        if converted["old_velocity_grid"].ndim != 2 or converted[
            "old_velocity_grid"
        ].shape[1:] != action_shape:
            raise ValueError("old_velocity_grid must have shape (grid, action_size)")
        if np.any(converted["old_delta_std"] <= 0.0):
            raise ValueError("old_delta_std must be positive")
        if not np.isfinite(self.old_delta_log_prob):
            raise ValueError("old_delta_log_prob must be finite")


class PolicyFlowCollector:
    def __init__(
        self,
        *,
        policy: PolicyFlowCollectorPolicy,
        normalizer: CollectorNormalizer,
        device: torch.device,
        gamma: float,
        gae_lambda: float,
        velocity_nfe: int,
        generator: torch.Generator,
    ) -> None:
        if not 0.0 <= gamma <= 1.0 or not 0.0 <= gae_lambda <= 1.0:
            raise ValueError("gamma and gae_lambda must be within [0, 1]")
        if velocity_nfe not in {2, 4, 8}:
            raise ValueError("velocity_nfe must be one of 2, 4, 8")
        self._policy = policy
        self._normalizer = normalizer
        self._device = device
        self._gamma = float(gamma)
        self._gae_lambda = float(gae_lambda)
        self._velocity_nfe = int(velocity_nfe)
        self._generator = generator
        self._core: OnPolicyCollectorCore[PolicyFlowAuxiliary] = OnPolicyCollectorCore()

    @property
    def pending_agent_ids(self) -> tuple[int, ...]:
        return self._core.pending_agent_ids

    @property
    def completed_transition_count(self) -> int:
        return self._core.completed_transition_count

    def trajectories(self) -> Mapping[int, tuple[CollectedTransition[PolicyFlowAuxiliary], ...]]:
        return self._core.trajectories()

    def reset(self, step: EnvStep) -> RolloutCollection:
        return self._core.reset(step, decide=self._decide, bootstrap=self._bootstrap)

    def step(self, step: EnvStep) -> RolloutCollection:
        return self._core.step(step, decide=self._decide, bootstrap=self._bootstrap)

    def replace_pending(self, step: EnvStep) -> RolloutCollection:
        return self._core.replace_pending(step, decide=self._decide)

    def build_batch(self) -> PolicyFlowBatch:
        trajectories = self._core.trajectories()
        if self.completed_transition_count == 0:
            raise RuntimeError("no completed transitions are available")
        streams: list[list[np.ndarray]] | None = None
        x0s: list[np.ndarray] = []
        priors: list[np.ndarray] = []
        deltas: list[np.ndarray] = []
        actions: list[np.ndarray] = []
        old_stds: list[np.ndarray] = []
        old_log_probs: list[float] = []
        old_velocity_grids: list[np.ndarray] = []
        old_values: list[float] = []
        advantages: list[np.ndarray] = []
        returns: list[np.ndarray] = []
        for agent_id in sorted(trajectories):
            trajectory = trajectories[agent_id]
            if streams is None:
                streams = [[] for _ in trajectory[0].observation]
            rewards = np.asarray([item.reward for item in trajectory], dtype=np.float32)
            values = np.asarray([item.value for item in trajectory], dtype=np.float32)
            next_values = np.asarray([item.next_value for item in trajectory], dtype=np.float32)
            terminated = np.asarray([item.terminated for item in trajectory], dtype=bool)
            truncated = np.asarray([item.truncated for item in trajectory], dtype=bool)
            item_advantages, item_returns = compute_gae(
                rewards, values, next_values, terminated, truncated,
                gamma=self._gamma, gae_lambda=self._gae_lambda,
            )
            advantages.append(item_advantages)
            returns.append(item_returns)
            for transition in trajectory:
                for stream, observation in zip(streams, transition.observation):
                    stream.append(observation)
                auxiliary = transition.auxiliary
                x0s.append(auxiliary.flow_x0)
                priors.append(auxiliary.actions_prior)
                deltas.append(auxiliary.delta_actions)
                actions.append(transition.action)
                old_stds.append(auxiliary.old_delta_std)
                old_log_probs.append(auxiliary.old_delta_log_prob)
                old_velocity_grids.append(auxiliary.old_velocity_grid)
                old_values.append(transition.value)
        if streams is None:
            raise RuntimeError("no completed transitions are available")
        tensor = lambda value: torch.as_tensor(np.stack(value), device=self._device)
        batch = PolicyFlowBatch(
            observations=tuple(tensor(stream) for stream in streams),
            flow_x0=tensor(x0s),
            actions_prior=tensor(priors),
            delta_actions=tensor(deltas),
            actions=tensor(actions),
            old_delta_std=tensor(old_stds),
            old_delta_log_probs=torch.as_tensor(
                np.asarray(old_log_probs, dtype=np.float32), device=self._device
            ),
            old_velocity_grid=tensor(old_velocity_grids),
            old_values=torch.as_tensor(
                np.asarray(old_values, dtype=np.float32), device=self._device
            ),
            advantages=torch.as_tensor(np.concatenate(advantages), device=self._device),
            returns=torch.as_tensor(np.concatenate(returns), device=self._device),
        )
        self._core.clear_completed()
        return batch

    def _decide(self, observations: tuple[np.ndarray, ...]) -> DecisionBatch[PolicyFlowAuxiliary]:
        normalized = self._normalizer.normalize(observations)
        tensors = tuple(torch.as_tensor(item, device=self._device) for item in normalized)
        with torch.inference_mode():
            output = self._policy.act(tensors, evaluation=False, generator=self._generator)
        if output.velocity_nfe != self._velocity_nfe:
            raise RuntimeError("PolicyFlow policy NFE does not match collector")
        x0 = output.flow_x0.cpu().numpy()
        priors = output.actions_prior.cpu().numpy()
        deltas = output.delta_actions.cpu().numpy()
        stds = output.old_delta_std.cpu().numpy()
        log_probs = output.old_delta_log_probs.cpu().numpy()
        old_velocity_grid = output.old_velocity_grid.cpu().numpy()
        return DecisionBatch(
            observations=tuple(np.array(item, dtype=np.float32, copy=True) for item in normalized),
            actions=output.actions.cpu().numpy().astype(np.float32, copy=True),
            values=output.values.cpu().numpy().astype(np.float32, copy=True),
            auxiliaries=tuple(
                PolicyFlowAuxiliary(
                    flow_x0=np.array(x0[index], dtype=np.float32, copy=True),
                    actions_prior=np.array(priors[index], dtype=np.float32, copy=True),
                    delta_actions=np.array(deltas[index], dtype=np.float32, copy=True),
                    old_delta_std=np.array(stds[index], dtype=np.float32, copy=True),
                    old_delta_log_prob=float(log_probs[index]),
                    old_velocity_grid=np.array(old_velocity_grid[index], dtype=np.float32, copy=True),
                )
                for index in range(len(x0))
            ),
        )

    def _bootstrap(self, observations: tuple[np.ndarray, ...]) -> np.ndarray:
        normalized = self._normalizer.normalize(observations)
        with torch.inference_mode():
            values = self._policy.critic(
                tuple(torch.as_tensor(item, device=self._device) for item in normalized)
            )
        return values.cpu().numpy().astype(np.float32, copy=True)
