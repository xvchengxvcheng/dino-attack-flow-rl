from __future__ import annotations

import time
from dataclasses import dataclass
from typing import Any

import numpy as np
import torch

from flow_rl.training.collector import PPOAuxiliary
from flow_rl.training.fpo_collector import FPOAuxiliary
from flow_rl.training.policyflow_collector import PolicyFlowAuxiliary
from flow_rl.training.dino_parallel_ppo import DinoParallelDecisionRequest
from flow_rl.training.versioned_collector import VersionedActionInfo


@dataclass(frozen=True)
class DinoInferenceBatchStatistics:
    batch_count: int
    environment_count: int
    agent_count: int
    mean_environment_batch_size: float
    max_environment_batch_size: int
    inference_seconds: float


class DinoBatchedPolicyDecider:
    """Run one policy inference for an ordered synchronous environment round."""

    def __init__(
        self,
        *,
        policy: Any,
        device: torch.device,
        action_size: int,
        generator: torch.Generator,
    ) -> None:
        if action_size <= 0:
            raise ValueError("action_size must be positive")
        self._policy = policy
        self._device = device
        self._action_size = int(action_size)
        self._generator = generator
        self._batch_count = 0
        self._environment_count = 0
        self._agent_count = 0
        self._max_environment_batch_size = 0
        self._inference_seconds = 0.0

    def statistics(self) -> DinoInferenceBatchStatistics:
        mean_batch_size = (
            0.0
            if self._batch_count == 0
            else self._environment_count / self._batch_count
        )
        return DinoInferenceBatchStatistics(
            batch_count=self._batch_count,
            environment_count=self._environment_count,
            agent_count=self._agent_count,
            mean_environment_batch_size=mean_batch_size,
            max_environment_batch_size=self._max_environment_batch_size,
            inference_seconds=self._inference_seconds,
        )

    def __call__(
        self, requests: tuple[DinoParallelDecisionRequest, ...]
    ) -> dict[int, VersionedActionInfo[PPOAuxiliary]]:
        if not requests:
            return {}
        started = time.perf_counter()
        environment_ids = [request.environment_id for request in requests]
        if len(set(environment_ids)) != len(environment_ids):
            raise ValueError("batched decision requests require unique environment IDs")

        prepared: list[tuple[DinoParallelDecisionRequest, np.ndarray, tuple[np.ndarray, ...]]] = []
        stream_count = len(requests[0].step.observations)
        for request in requests:
            agent_ids = np.asarray(request.adapter.pending_agent_ids, dtype=np.int64)
            if len(request.step.observations) != stream_count:
                raise ValueError("batched decision observation stream counts must match")
            observations = tuple(
                np.array(stream[: len(agent_ids)], dtype=np.float32, copy=True)
                for stream in request.step.observations
            )
            prepared.append((request, agent_ids, observations))

        total_agents = sum(len(agent_ids) for _, agent_ids, _ in prepared)
        if total_agents:
            batched_observations = tuple(
                np.concatenate(
                    [observations[index] for _, _, observations in prepared], axis=0
                )
                for index in range(stream_count)
            )
            tensors = tuple(
                torch.as_tensor(observation, device=self._device)
                for observation in batched_observations
            )
            with torch.inference_mode():
                output = self._policy.act(
                    tensors,
                    deterministic=False,
                    generator=self._generator,
                )
            actions = output.actions.detach().cpu().numpy().astype(np.float32, copy=True)
            values = output.values.detach().cpu().numpy().astype(np.float32, copy=True)
            log_probs = (
                output.log_probs.detach().cpu().numpy().astype(np.float32, copy=True)
            )
            if actions.shape != (total_agents, self._action_size):
                raise ValueError(
                    "batched policy returned wrong action shape: "
                    f"{actions.shape}, expected {(total_agents, self._action_size)}"
                )
            if values.shape != (total_agents,) or log_probs.shape != (total_agents,):
                raise ValueError("batched policy returned wrong value or log-prob shape")
            if not (
                np.isfinite(actions).all()
                and np.isfinite(values).all()
                and np.isfinite(log_probs).all()
            ):
                raise RuntimeError("batched policy produced a non-finite output")
        else:
            actions = np.empty((0, self._action_size), dtype=np.float32)
            values = np.empty(0, dtype=np.float32)
            log_probs = np.empty(0, dtype=np.float32)

        results: dict[int, VersionedActionInfo[PPOAuxiliary]] = {}
        offset = 0
        for request, agent_ids, observations in prepared:
            stop = offset + len(agent_ids)
            results[request.environment_id] = VersionedActionInfo(
                environment_id=request.environment_id,
                process_generation=request.process_generation,
                policy_version=request.policy_version,
                agent_ids=agent_ids,
                observations=observations,
                actions=np.array(actions[offset:stop], copy=True),
                values=np.array(values[offset:stop], copy=True),
                auxiliaries=tuple(
                    PPOAuxiliary(float(value)) for value in log_probs[offset:stop]
                ),
            )
            offset = stop
        self._batch_count += 1
        self._environment_count += len(requests)
        self._agent_count += total_agents
        self._max_environment_batch_size = max(
            self._max_environment_batch_size, len(requests)
        )
        self._inference_seconds += time.perf_counter() - started
        return results


class DinoBatchedFPOPolicyDecider:
    """Batch one ordered environment round through the FPO actor-critic."""

    def __init__(
        self,
        *,
        policy: Any,
        device: torch.device,
        action_size: int,
        nfe: int,
        num_fpo_samples: int,
        generator: torch.Generator,
    ) -> None:
        if action_size <= 0:
            raise ValueError("action_size must be positive")
        if nfe not in {1, 2, 4, 8}:
            raise ValueError("nfe must be one of 1, 2, 4, 8")
        if num_fpo_samples <= 0:
            raise ValueError("num_fpo_samples must be positive")
        self._policy = policy
        self._device = device
        self._action_size = int(action_size)
        self._nfe = int(nfe)
        self._num_fpo_samples = int(num_fpo_samples)
        self._generator = generator
        self._batch_count = 0
        self._environment_count = 0
        self._agent_count = 0
        self._max_environment_batch_size = 0
        self._inference_seconds = 0.0

    def statistics(self) -> DinoInferenceBatchStatistics:
        mean_batch_size = (
            0.0 if self._batch_count == 0 else self._environment_count / self._batch_count
        )
        return DinoInferenceBatchStatistics(
            batch_count=self._batch_count,
            environment_count=self._environment_count,
            agent_count=self._agent_count,
            mean_environment_batch_size=mean_batch_size,
            max_environment_batch_size=self._max_environment_batch_size,
            inference_seconds=self._inference_seconds,
        )

    def __call__(
        self, requests: tuple[DinoParallelDecisionRequest, ...]
    ) -> dict[int, VersionedActionInfo[FPOAuxiliary]]:
        if not requests:
            return {}
        started = time.perf_counter()
        environment_ids = [request.environment_id for request in requests]
        if len(set(environment_ids)) != len(environment_ids):
            raise ValueError("batched decision requests require unique environment IDs")

        prepared: list[tuple[DinoParallelDecisionRequest, np.ndarray, tuple[np.ndarray, ...]]] = []
        stream_count = len(requests[0].step.observations)
        for request in requests:
            agent_ids = np.asarray(request.adapter.pending_agent_ids, dtype=np.int64)
            if len(request.step.observations) != stream_count:
                raise ValueError("batched decision observation stream counts must match")
            observations = tuple(
                np.array(stream[: len(agent_ids)], dtype=np.float32, copy=True)
                for stream in request.step.observations
            )
            prepared.append((request, agent_ids, observations))

        total_agents = sum(len(agent_ids) for _, agent_ids, _ in prepared)
        if total_agents:
            batched_observations = tuple(
                np.concatenate([item[2][index] for item in prepared], axis=0)
                for index in range(stream_count)
            )
            tensors = tuple(
                torch.as_tensor(observation, device=self._device)
                for observation in batched_observations
            )
            with torch.inference_mode():
                output = self._policy.act(
                    tensors,
                    nfe=self._nfe,
                    num_fpo_samples=self._num_fpo_samples,
                    generator=self._generator,
                )
            arrays = {
                "actions": output.actions,
                "latent_actions": output.latent_actions,
                "loss_eps": output.loss_eps,
                "loss_t": output.loss_t,
                "old_cfm_losses": output.old_cfm_losses,
                "values": output.values,
            }
            converted = {
                name: tensor.detach().cpu().numpy().astype(np.float32, copy=True)
                for name, tensor in arrays.items()
            }
            actions = converted["actions"]
            latent = converted["latent_actions"]
            loss_eps = converted["loss_eps"]
            loss_t = converted["loss_t"]
            old_losses = converted["old_cfm_losses"]
            values = converted["values"]
            expected = {
                "actions": (total_agents, self._action_size),
                "latent_actions": (total_agents, self._action_size),
                "loss_eps": (total_agents, self._num_fpo_samples, self._action_size),
                "loss_t": (total_agents, self._num_fpo_samples, 1),
                "old_cfm_losses": (total_agents, self._num_fpo_samples),
                "values": (total_agents,),
            }
            for name, shape in expected.items():
                if converted[name].shape != shape:
                    raise ValueError(
                        f"batched FPO policy returned wrong {name} shape: "
                        f"{converted[name].shape}, expected {shape}"
                    )
                if not np.isfinite(converted[name]).all():
                    raise RuntimeError(f"batched FPO policy produced non-finite {name}")
        else:
            actions = latent = np.empty((0, self._action_size), dtype=np.float32)
            loss_eps = np.empty(
                (0, self._num_fpo_samples, self._action_size), dtype=np.float32
            )
            loss_t = np.empty((0, self._num_fpo_samples, 1), dtype=np.float32)
            old_losses = np.empty((0, self._num_fpo_samples), dtype=np.float32)
            values = np.empty(0, dtype=np.float32)

        results: dict[int, VersionedActionInfo[FPOAuxiliary]] = {}
        offset = 0
        for request, agent_ids, observations in prepared:
            stop = offset + len(agent_ids)
            results[request.environment_id] = VersionedActionInfo(
                environment_id=request.environment_id,
                process_generation=request.process_generation,
                policy_version=request.policy_version,
                agent_ids=agent_ids,
                observations=observations,
                actions=np.array(actions[offset:stop], copy=True),
                values=np.array(values[offset:stop], copy=True),
                auxiliaries=tuple(
                    FPOAuxiliary(
                        latent_action=np.array(latent[index], copy=True),
                        loss_eps=np.array(loss_eps[index], copy=True),
                        loss_t=np.array(loss_t[index], copy=True),
                        old_cfm_losses=np.array(old_losses[index], copy=True),
                    )
                    for index in range(offset, stop)
                ),
            )
            offset = stop
        self._batch_count += 1
        self._environment_count += len(requests)
        self._agent_count += total_agents
        self._max_environment_batch_size = max(
            self._max_environment_batch_size, len(requests)
        )
        self._inference_seconds += time.perf_counter() - started
        return results


class DinoBatchedPolicyFlowDecider:
    """Batch Dino decisions while freezing every PolicyFlow behavior field."""

    def __init__(
        self,
        *,
        policy: Any,
        device: torch.device,
        action_size: int,
        velocity_nfe: int,
        generator: torch.Generator,
    ) -> None:
        if action_size <= 0:
            raise ValueError("action_size must be positive")
        if velocity_nfe not in {2, 4, 8}:
            raise ValueError("velocity_nfe must be one of 2, 4, 8")
        self._policy = policy
        self._device = device
        self._action_size = int(action_size)
        self._velocity_nfe = int(velocity_nfe)
        self._generator = generator
        self._batch_count = 0
        self._environment_count = 0
        self._agent_count = 0
        self._max_environment_batch_size = 0
        self._inference_seconds = 0.0

    def statistics(self) -> DinoInferenceBatchStatistics:
        return DinoInferenceBatchStatistics(
            batch_count=self._batch_count,
            environment_count=self._environment_count,
            agent_count=self._agent_count,
            mean_environment_batch_size=(
                0.0
                if self._batch_count == 0
                else self._environment_count / self._batch_count
            ),
            max_environment_batch_size=self._max_environment_batch_size,
            inference_seconds=self._inference_seconds,
        )

    def __call__(
        self, requests: tuple[DinoParallelDecisionRequest, ...]
    ) -> dict[int, VersionedActionInfo[PolicyFlowAuxiliary]]:
        if not requests:
            return {}
        started = time.perf_counter()
        environment_ids = [request.environment_id for request in requests]
        if len(set(environment_ids)) != len(environment_ids):
            raise ValueError("batched decision requests require unique environment IDs")
        stream_count = len(requests[0].step.observations)
        prepared = []
        for request in requests:
            agent_ids = np.asarray(request.adapter.pending_agent_ids, dtype=np.int64)
            if len(request.step.observations) != stream_count:
                raise ValueError("batched decision observation stream counts must match")
            observations = tuple(
                np.array(stream[: len(agent_ids)], dtype=np.float32, copy=True)
                for stream in request.step.observations
            )
            prepared.append((request, agent_ids, observations))
        total_agents = sum(len(agent_ids) for _, agent_ids, _ in prepared)
        if total_agents:
            batched_observations = tuple(
                np.concatenate([item[2][index] for item in prepared], axis=0)
                for index in range(stream_count)
            )
            tensors = tuple(
                torch.as_tensor(observation, device=self._device)
                for observation in batched_observations
            )
            with torch.inference_mode():
                output = self._policy.act(
                    tensors, evaluation=False, generator=self._generator
                )
            if output.velocity_nfe != self._velocity_nfe:
                raise RuntimeError("PolicyFlow policy NFE does not match decider")
            tensor_fields = {
                "actions": output.actions,
                "flow_x0": output.flow_x0,
                "actions_prior": output.actions_prior,
                "delta_actions": output.delta_actions,
                "old_delta_std": output.old_delta_std,
                "old_delta_log_probs": output.old_delta_log_probs,
                "old_velocity_grid": output.old_velocity_grid,
                "values": output.values,
            }
            arrays = {
                name: value.detach().cpu().numpy().astype(np.float32, copy=True)
                for name, value in tensor_fields.items()
            }
            expected_actions = (total_agents, self._action_size)
            for name in (
                "actions", "flow_x0", "actions_prior", "delta_actions",
                "old_delta_std",
            ):
                if arrays[name].shape != expected_actions:
                    raise ValueError(f"batched PolicyFlow {name} has wrong shape")
            if arrays["old_delta_log_probs"].shape != (total_agents,) or arrays[
                "values"
            ].shape != (total_agents,):
                raise ValueError("batched PolicyFlow scalar output has wrong shape")
            if arrays["old_velocity_grid"].ndim != 3 or arrays[
                "old_velocity_grid"
            ].shape[0] != total_agents or arrays["old_velocity_grid"].shape[
                2
            ] != self._action_size:
                raise ValueError("batched PolicyFlow velocity grid has wrong shape")
            if not all(np.isfinite(value).all() for value in arrays.values()):
                raise RuntimeError("batched PolicyFlow policy produced non-finite output")
        else:
            arrays = {
                "actions": np.empty((0, self._action_size), dtype=np.float32),
                "flow_x0": np.empty((0, self._action_size), dtype=np.float32),
                "actions_prior": np.empty((0, self._action_size), dtype=np.float32),
                "delta_actions": np.empty((0, self._action_size), dtype=np.float32),
                "old_delta_std": np.empty((0, self._action_size), dtype=np.float32),
                "old_delta_log_probs": np.empty(0, dtype=np.float32),
                "old_velocity_grid": np.empty(
                    (0, self._velocity_nfe + 1, self._action_size), dtype=np.float32
                ),
                "values": np.empty(0, dtype=np.float32),
            }
        results: dict[int, VersionedActionInfo[PolicyFlowAuxiliary]] = {}
        offset = 0
        for request, agent_ids, observations in prepared:
            stop = offset + len(agent_ids)
            results[request.environment_id] = VersionedActionInfo(
                environment_id=request.environment_id,
                process_generation=request.process_generation,
                policy_version=request.policy_version,
                agent_ids=agent_ids,
                observations=observations,
                actions=np.array(arrays["actions"][offset:stop], copy=True),
                values=np.array(arrays["values"][offset:stop], copy=True),
                auxiliaries=tuple(
                    PolicyFlowAuxiliary(
                        flow_x0=arrays["flow_x0"][index],
                        actions_prior=arrays["actions_prior"][index],
                        delta_actions=arrays["delta_actions"][index],
                        old_delta_std=arrays["old_delta_std"][index],
                        old_delta_log_prob=float(
                            arrays["old_delta_log_probs"][index]
                        ),
                        old_velocity_grid=arrays["old_velocity_grid"][index],
                    )
                    for index in range(offset, stop)
                ),
            )
            offset = stop
        self._batch_count += 1
        self._environment_count += len(requests)
        self._agent_count += total_agents
        self._max_environment_batch_size = max(
            self._max_environment_batch_size, len(requests)
        )
        self._inference_seconds += time.perf_counter() - started
        return results
