from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from typing import Protocol

import numpy as np
import torch

from flow_rl.algorithms.fpo import FPOBatch
from flow_rl.data.gae import compute_gae
from flow_rl.envs.types import EnvStep
from flow_rl.models.fpo_policy import FPOPolicyOutput
from flow_rl.training.collector import CollectorCritic, CollectorNormalizer
from flow_rl.training.on_policy import CollectedTransition, DecisionBatch, OnPolicyCollectorCore, RolloutCollection


class FPOCollectorPolicy(Protocol):
    critic: CollectorCritic

    def act(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        nfe: int,
        num_fpo_samples: int,
        generator: torch.Generator,
    ) -> FPOPolicyOutput: ...


@dataclass(frozen=True)
class FPOAuxiliary:
    latent_action: np.ndarray
    loss_eps: np.ndarray
    loss_t: np.ndarray
    old_cfm_losses: np.ndarray


class FPOCollector:
    def __init__(
        self,
        *,
        policy: FPOCollectorPolicy,
        normalizer: CollectorNormalizer,
        device: torch.device,
        gamma: float,
        gae_lambda: float,
        nfe: int,
        num_fpo_samples: int,
        generator: torch.Generator,
    ) -> None:
        if not 0.0 <= gamma <= 1.0 or not 0.0 <= gae_lambda <= 1.0:
            raise ValueError("gamma and gae_lambda must be within [0, 1]")
        if nfe not in {1, 2, 4, 8}:
            raise ValueError("nfe must be one of 1, 2, 4, 8")
        if num_fpo_samples <= 0:
            raise ValueError("num_fpo_samples must be positive")
        self._policy = policy
        self._normalizer = normalizer
        self._device = device
        self._gamma = float(gamma)
        self._gae_lambda = float(gae_lambda)
        self._nfe = int(nfe)
        self._num_fpo_samples = int(num_fpo_samples)
        self._generator = generator
        self._core: OnPolicyCollectorCore[FPOAuxiliary] = OnPolicyCollectorCore()

    @property
    def pending_agent_ids(self) -> tuple[int, ...]:
        return self._core.pending_agent_ids

    @property
    def completed_transition_count(self) -> int:
        return self._core.completed_transition_count

    def trajectories(self) -> Mapping[int, tuple[CollectedTransition[FPOAuxiliary], ...]]:
        return self._core.trajectories()

    def reset(self, step: EnvStep) -> RolloutCollection:
        return self._core.reset(step, decide=self._decide, bootstrap=self._bootstrap)

    def step(self, step: EnvStep) -> RolloutCollection:
        return self._core.step(step, decide=self._decide, bootstrap=self._bootstrap)

    def build_batch(self) -> FPOBatch:
        trajectories = self._core.trajectories()
        if self.completed_transition_count == 0:
            raise RuntimeError("no completed transitions are available")
        observation_streams: list[list[np.ndarray]] | None = None
        bounded_actions: list[np.ndarray] = []
        latent_actions: list[np.ndarray] = []
        loss_eps: list[np.ndarray] = []
        loss_t: list[np.ndarray] = []
        old_cfm_losses: list[np.ndarray] = []
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
                for stream, observation in zip(observation_streams, transition.observation):
                    stream.append(observation)
                bounded_actions.append(transition.action)
                latent_actions.append(transition.auxiliary.latent_action)
                loss_eps.append(transition.auxiliary.loss_eps)
                loss_t.append(transition.auxiliary.loss_t)
                old_cfm_losses.append(transition.auxiliary.old_cfm_losses)
                old_values.append(transition.value)
        if observation_streams is None:
            raise RuntimeError("no completed transitions are available")
        batch = FPOBatch(
            observations=tuple(torch.as_tensor(np.stack(stream), device=self._device) for stream in observation_streams),
            latent_actions=torch.as_tensor(np.stack(latent_actions), device=self._device),
            bounded_actions=torch.as_tensor(np.stack(bounded_actions), device=self._device),
            loss_eps=torch.as_tensor(np.stack(loss_eps), device=self._device),
            loss_t=torch.as_tensor(np.stack(loss_t), device=self._device),
            old_cfm_losses=torch.as_tensor(np.stack(old_cfm_losses), device=self._device),
            old_values=torch.as_tensor(np.asarray(old_values, dtype=np.float32), device=self._device),
            advantages=torch.as_tensor(np.concatenate(advantages), device=self._device),
            returns=torch.as_tensor(np.concatenate(returns), device=self._device),
        )
        self._core.clear_completed()
        return batch

    def _decide(self, observations: tuple[np.ndarray, ...]) -> DecisionBatch[FPOAuxiliary]:
        self._normalizer.update(observations)
        normalized = self._normalizer.normalize(observations)
        tensors = tuple(torch.as_tensor(item, device=self._device) for item in normalized)
        with torch.inference_mode():
            output = self._policy.act(
                tensors,
                nfe=self._nfe,
                num_fpo_samples=self._num_fpo_samples,
                generator=self._generator,
            )
        latent = output.latent_actions.cpu().numpy()
        eps = output.loss_eps.cpu().numpy()
        times = output.loss_t.cpu().numpy()
        losses = output.old_cfm_losses.cpu().numpy()
        return DecisionBatch(
            observations=tuple(np.array(item, dtype=np.float32, copy=True) for item in normalized),
            actions=output.actions.cpu().numpy().astype(np.float32, copy=True),
            values=output.values.cpu().numpy().astype(np.float32, copy=True),
            auxiliaries=tuple(
                FPOAuxiliary(
                    latent_action=np.array(latent[index], dtype=np.float32, copy=True),
                    loss_eps=np.array(eps[index], dtype=np.float32, copy=True),
                    loss_t=np.array(times[index], dtype=np.float32, copy=True),
                    old_cfm_losses=np.array(losses[index], dtype=np.float32, copy=True),
                )
                for index in range(len(latent))
            ),
        )

    def _bootstrap(self, observations: tuple[np.ndarray, ...]) -> np.ndarray:
        normalized = self._normalizer.normalize(observations)
        with torch.inference_mode():
            values = self._policy.critic(
                tuple(torch.as_tensor(item, device=self._device) for item in normalized)
            )
        return values.cpu().numpy().astype(np.float32, copy=True)
