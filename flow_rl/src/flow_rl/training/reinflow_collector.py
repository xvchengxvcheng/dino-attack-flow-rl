from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from typing import Protocol

import numpy as np
import torch

from flow_rl.algorithms.reinflow import ReinFlowBatch
from flow_rl.data.gae import compute_gae
from flow_rl.envs.types import EnvStep
from flow_rl.models.reinflow_policy import ReinFlowPolicyOutput
from flow_rl.training.collector import CollectorCritic, CollectorNormalizer
from flow_rl.training.on_policy import (
    CollectedTransition,
    DecisionBatch,
    OnPolicyCollectorCore,
    RolloutCollection,
)


class ReinFlowCollectorPolicy(Protocol):
    critic: CollectorCritic

    def act(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        evaluation: bool,
        generator: torch.Generator,
    ) -> ReinFlowPolicyOutput: ...


@dataclass(frozen=True)
class ReinFlowAuxiliary:
    chain: np.ndarray
    old_log_prob: float
    entropy_rate: float
    mean_noise_std: float


class ReinFlowCollector:
    def __init__(
        self,
        *,
        policy: ReinFlowCollectorPolicy,
        normalizer: CollectorNormalizer,
        device: torch.device,
        gamma: float,
        gae_lambda: float,
        nfe: int,
        generator: torch.Generator,
    ) -> None:
        if not 0.0 <= gamma <= 1.0 or not 0.0 <= gae_lambda <= 1.0:
            raise ValueError("gamma and gae_lambda must be within [0, 1]")
        if nfe not in {1, 2, 4, 8}:
            raise ValueError("nfe must be one of 1, 2, 4, 8")
        self._policy = policy
        self._normalizer = normalizer
        self._device = device
        self._gamma = float(gamma)
        self._gae_lambda = float(gae_lambda)
        self._nfe = int(nfe)
        self._generator = generator
        self._core: OnPolicyCollectorCore[ReinFlowAuxiliary] = OnPolicyCollectorCore()

    @property
    def pending_agent_ids(self) -> tuple[int, ...]:
        return self._core.pending_agent_ids

    @property
    def completed_transition_count(self) -> int:
        return self._core.completed_transition_count

    def trajectories(
        self,
    ) -> Mapping[int, tuple[CollectedTransition[ReinFlowAuxiliary], ...]]:
        return self._core.trajectories()

    def reset(self, step: EnvStep) -> RolloutCollection:
        return self._core.reset(
            step,
            decide=self._decide,
            bootstrap=self._bootstrap,
        )

    def step(self, step: EnvStep) -> RolloutCollection:
        return self._core.step(
            step,
            decide=self._decide,
            bootstrap=self._bootstrap,
        )

    def build_batch(self) -> ReinFlowBatch:
        trajectories = self._core.trajectories()
        if self.completed_transition_count == 0:
            raise RuntimeError("no completed transitions are available")
        observation_streams: list[list[np.ndarray]] | None = None
        chains: list[np.ndarray] = []
        actions: list[np.ndarray] = []
        old_log_probs: list[float] = []
        old_values: list[float] = []
        advantages: list[np.ndarray] = []
        returns: list[np.ndarray] = []
        for agent_id in sorted(trajectories):
            trajectory = trajectories[agent_id]
            if observation_streams is None:
                observation_streams = [[] for _ in trajectory[0].observation]
            rewards = np.asarray([item.reward for item in trajectory], dtype=np.float32)
            values = np.asarray([item.value for item in trajectory], dtype=np.float32)
            next_values = np.asarray([item.next_value for item in trajectory], dtype=np.float32)
            terminated = np.asarray([item.terminated for item in trajectory], dtype=bool)
            truncated = np.asarray([item.truncated for item in trajectory], dtype=bool)
            item_advantages, item_returns = compute_gae(
                rewards,
                values,
                next_values,
                terminated,
                truncated,
                gamma=self._gamma,
                gae_lambda=self._gae_lambda,
            )
            advantages.append(item_advantages)
            returns.append(item_returns)
            for transition in trajectory:
                for stream, observation in zip(
                    observation_streams, transition.observation
                ):
                    stream.append(observation)
                chains.append(transition.auxiliary.chain)
                actions.append(transition.action)
                old_log_probs.append(transition.auxiliary.old_log_prob)
                old_values.append(transition.value)
        if observation_streams is None:
            raise RuntimeError("no completed transitions are available")
        batch = ReinFlowBatch(
            observations=tuple(
                torch.as_tensor(np.stack(stream), device=self._device)
                for stream in observation_streams
            ),
            chains=torch.as_tensor(np.stack(chains), device=self._device),
            actions=torch.as_tensor(np.stack(actions), device=self._device),
            old_log_probs=torch.as_tensor(
                np.asarray(old_log_probs, dtype=np.float32), device=self._device
            ),
            old_values=torch.as_tensor(
                np.asarray(old_values, dtype=np.float32), device=self._device
            ),
            advantages=torch.as_tensor(np.concatenate(advantages), device=self._device),
            returns=torch.as_tensor(np.concatenate(returns), device=self._device),
        )
        self._core.clear_completed()
        return batch

    def _decide(
        self, observations: tuple[np.ndarray, ...]
    ) -> DecisionBatch[ReinFlowAuxiliary]:
        # ReinFlow deliberately freezes the BC dataset normalizer online.
        normalized = self._normalizer.normalize(observations)
        tensors = tuple(
            torch.as_tensor(item, device=self._device) for item in normalized
        )
        with torch.inference_mode():
            output = self._policy.act(
                tensors,
                evaluation=False,
                generator=self._generator,
            )
        if output.nfe != self._nfe:
            raise RuntimeError("ReinFlow policy NFE does not match collector")
        chain_values = output.chains.cpu().numpy()
        log_probs = output.old_log_probs.cpu().numpy()
        entropies = output.entropy_rate.cpu().numpy()
        noise_stds = output.mean_noise_std.cpu().numpy()
        return DecisionBatch(
            observations=tuple(
                np.array(item, dtype=np.float32, copy=True) for item in normalized
            ),
            actions=output.actions.cpu().numpy().astype(np.float32, copy=True),
            values=output.values.cpu().numpy().astype(np.float32, copy=True),
            auxiliaries=tuple(
                ReinFlowAuxiliary(
                    chain=np.array(chain_values[index], dtype=np.float32, copy=True),
                    old_log_prob=float(log_probs[index]),
                    entropy_rate=float(entropies[index]),
                    mean_noise_std=float(noise_stds[index]),
                )
                for index in range(len(chain_values))
            ),
        )

    def _bootstrap(self, observations: tuple[np.ndarray, ...]) -> np.ndarray:
        normalized = self._normalizer.normalize(observations)
        with torch.inference_mode():
            values = self._policy.critic(
                tuple(
                    torch.as_tensor(item, device=self._device)
                    for item in normalized
                )
            )
        return values.cpu().numpy().astype(np.float32, copy=True)
