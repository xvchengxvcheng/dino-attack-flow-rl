from __future__ import annotations

import math
import time
from collections import Counter, defaultdict
from collections.abc import Callable, Mapping
from dataclasses import dataclass
from typing import Any, Protocol

import numpy as np
import torch

from flow_rl.envs.parallel_unity import ParallelEnvEvent
from flow_rl.envs.types import EnvStep
from flow_rl.tracking.episodes import EpisodeTracker
from flow_rl.training.collector import PPOAuxiliary
from flow_rl.training.trainer import update_versioned_ppo
from flow_rl.training.versioned_collector import (
    AgentIdentity,
    BootstrapTarget,
    VersionedActionInfo,
    VersionedCollector,
)


class _Adapter(Protocol):
    @property
    def pending_agent_ids(self) -> np.ndarray: ...


class _VectorEnv(Protocol):
    @property
    def handles(self) -> Mapping[int, Any]: ...

    def submit_actions(self, actions_by_environment: Mapping[int, np.ndarray]) -> None: ...

    def poll_ready(
        self, *, timeout: float | None = 0.0, max_events: int | None = None
    ) -> tuple[ParallelEnvEvent, ...]: ...

    def restart_environment(self, environment_id: int) -> Any: ...

    def close(self) -> None: ...


DecisionFunction = Callable[
    [int, int, int, EnvStep, _Adapter], VersionedActionInfo[PPOAuxiliary]
]


@dataclass(frozen=True)
class DinoParallelDecisionRequest:
    environment_id: int
    process_generation: int
    policy_version: int
    step: EnvStep
    adapter: _Adapter


BatchDecisionFunction = Callable[
    [tuple[DinoParallelDecisionRequest, ...]],
    Mapping[int, VersionedActionInfo[PPOAuxiliary]],
]
BootstrapFunction = Callable[
    [VersionedCollector[PPOAuxiliary]], Mapping[AgentIdentity | BootstrapTarget, float]
]
MapClassifier = Callable[[EnvStep, int], str | None]


@dataclass(frozen=True)
class DinoParallelRestartState:
    adapter: _Adapter
    initial_step: EnvStep


RestartStateResolver = Callable[[int, Any], DinoParallelRestartState]


@dataclass(frozen=True)
class DinoParallelEpisodeSummary:
    environment_id: int
    process_generation: int
    map_name: str
    agent_id: int
    episode_index: int
    episode_return: float
    episode_length: int
    terminated: bool
    truncated: bool
    terminal_reward: float
    success: bool


@dataclass(frozen=True)
class DinoParallelUpdateSummary:
    policy_version: int
    transition_count: int
    environment_contributions: tuple[tuple[int, int], ...]
    transitions_by_map: tuple[tuple[str, int], ...]
    episode_starts_by_map: tuple[tuple[str, int], ...]
    natural_terminals_by_map: tuple[tuple[str, int], ...]
    truncated_terminals_by_map: tuple[tuple[str, int], ...]
    metrics: tuple[tuple[str, float], ...]
    response_wait_mean_seconds: float = 0.0
    response_wait_max_seconds: float = 0.0
    response_wait_count: int = 0
    microbatch_wait_seconds: float = 0.0
    optimizer_update_seconds: float = 0.0
    completed_episodes: tuple[DinoParallelEpisodeSummary, ...] = ()


@dataclass(frozen=True)
class DinoParallelTrainingSummary:
    updates: tuple[DinoParallelUpdateSummary, ...]
    total_transitions: int
    initial_environment_steps: int
    final_environment_steps: int


UpdateCompletedCallback = Callable[[DinoParallelUpdateSummary, int], None]
OnPolicyUpdateFunction = Callable[..., object]


class DinoParallelPPOTrainer:
    """Run drained, version-isolated PPO updates over Unity Players.

    The caller creates the adapters and ``AsyncUnityVectorEnv`` so Task 2 can own
    launch configuration and resume semantics. This core owns the vector for the
    duration of ``train`` and always closes it, including update failures.
    """

    def __init__(
        self,
        *,
        vector_env: _VectorEnv,
        adapters: Mapping[int, _Adapter],
        initial_steps: Mapping[int, EnvStep],
        updater: Any,
        device: torch.device,
        rollout_size: int,
        total_environment_steps: int,
        initial_environment_steps: int,
        gamma: float,
        gae_lambda: float,
        decide: DecisionFunction,
        bootstrap: BootstrapFunction,
        map_classifier: MapClassifier,
        restart_state_resolver: RestartStateResolver,
        poll_timeout: float,
        max_consecutive_no_progress: int,
        initial_policy_version: int = 0,
        on_update_completed: UpdateCompletedCallback | None = None,
        sampling_mode: str = "asynchronous",
        decide_batch: BatchDecisionFunction | None = None,
        inference_batch_size: int = 1,
        inference_batch_wait_seconds: float = 0.0,
        success_reward_threshold: float = 5.0,
        update_function: OnPolicyUpdateFunction = update_versioned_ppo,
    ) -> None:
        if rollout_size <= 0 or total_environment_steps <= 0:
            raise ValueError("rollout and total environment steps must be positive")
        if initial_environment_steps < 0:
            raise ValueError("initial_environment_steps cannot be negative")
        if initial_environment_steps >= total_environment_steps:
            raise ValueError(
                "total_environment_steps must exceed initial_environment_steps"
            )
        if initial_policy_version < 0:
            raise ValueError("initial_policy_version cannot be negative")
        if not 0.0 <= gamma <= 1.0 or not 0.0 <= gae_lambda <= 1.0:
            raise ValueError("gamma and gae_lambda must be within [0, 1]")
        if poll_timeout <= 0.0:
            raise ValueError("poll_timeout must be positive")
        if max_consecutive_no_progress <= 0:
            raise ValueError("max_consecutive_no_progress must be positive")
        if sampling_mode not in ("asynchronous", "synchronous"):
            raise ValueError(
                "sampling_mode must be 'asynchronous' or 'synchronous'"
            )
        if sampling_mode == "synchronous" and decide_batch is None:
            raise ValueError("synchronous sampling requires decide_batch")
        if (
            isinstance(inference_batch_size, bool)
            or not isinstance(inference_batch_size, int)
            or inference_batch_size <= 0
        ):
            raise ValueError("inference_batch_size must be positive")
        if not 0.0 <= float(inference_batch_wait_seconds) <= 0.1:
            raise ValueError(
                "inference_batch_wait_seconds must be within [0, 0.1]"
            )
        if inference_batch_size > 1 and decide_batch is None:
            raise ValueError("microbatched inference requires decide_batch")
        if not math.isfinite(success_reward_threshold):
            raise ValueError("success_reward_threshold must be finite")
        self._vector = vector_env
        self._adapters = adapters
        self._initial_steps = initial_steps
        self._updater = updater
        self._device = device
        self._rollout_size = int(rollout_size)
        self._total_environment_steps = int(total_environment_steps)
        self._initial_environment_steps = int(initial_environment_steps)
        self._gamma = float(gamma)
        self._gae_lambda = float(gae_lambda)
        self._initial_policy_version = int(initial_policy_version)
        self._decide = decide
        self._bootstrap = bootstrap
        self._map_classifier = map_classifier
        self._restart_state_resolver = restart_state_resolver
        self._poll_timeout = float(poll_timeout)
        self._max_consecutive_no_progress = int(max_consecutive_no_progress)
        self._on_update_completed = on_update_completed
        self._sampling_mode = sampling_mode
        self._decide_batch = decide_batch
        self._inference_batch_size = int(inference_batch_size)
        self._inference_batch_wait_seconds = float(inference_batch_wait_seconds)
        self._success_reward_threshold = float(success_reward_threshold)
        self._update_function = update_function
        self._ready_since: dict[int, float] = {}

    def train(self) -> DinoParallelTrainingSummary:
        try:
            summary = self._train_open_vector()
        except BaseException as training_error:
            try:
                self._vector.close()
            except BaseException as close_error:
                raise training_error from close_error
            raise
        self._vector.close()
        return summary

    def _train_open_vector(self) -> DinoParallelTrainingSummary:
        handles = dict(self._vector.handles)
        environment_ids = tuple(sorted(handles))
        if len(environment_ids) not in (4, 8, 12, 16, 20, 24):
            raise ValueError(
                "Dino parallel PPO requires four, eight, twelve, sixteen, twenty, or twenty-four environments, "
                f"got {len(environment_ids)}"
            )
        if set(environment_ids) != set(self._adapters) or set(environment_ids) != set(
            self._initial_steps
        ):
            raise ValueError("handles, adapters, and initial steps must share environment IDs")

        current_steps = dict(self._initial_steps)
        current_adapters = dict(self._adapters)
        current_maps: dict[int, str] = {}
        policy_version = self._initial_policy_version
        total_transitions = 0
        environment_steps = self._initial_environment_steps
        summaries: list[DinoParallelUpdateSummary] = []
        episode_trackers: dict[tuple[int, int], EpisodeTracker] = {}
        while environment_steps < self._total_environment_steps:
            collector: VersionedCollector[PPOAuxiliary] = VersionedCollector(
                target_transitions=self._rollout_size
            )
            collector.begin(policy_version)
            update_summary = self._collect_and_update(
                collector=collector,
                policy_version=policy_version,
                environment_steps_before=environment_steps,
                current_steps=current_steps,
                current_adapters=current_adapters,
                current_maps=current_maps,
                environment_ids=environment_ids,
                episode_trackers=episode_trackers,
            )
            summaries.append(update_summary)
            total_transitions += update_summary.transition_count
            environment_steps += update_summary.transition_count
            if self._on_update_completed is not None:
                self._on_update_completed(update_summary, environment_steps)
            policy_version += 1
        return DinoParallelTrainingSummary(
            updates=tuple(summaries),
            total_transitions=total_transitions,
            initial_environment_steps=self._initial_environment_steps,
            final_environment_steps=environment_steps,
        )

    def _collect_and_update(
        self,
        *,
        collector: VersionedCollector[PPOAuxiliary],
        policy_version: int,
        environment_steps_before: int,
        current_steps: dict[int, EnvStep],
        current_adapters: dict[int, _Adapter],
        current_maps: dict[int, str],
        environment_ids: tuple[int, ...],
        episode_trackers: dict[tuple[int, int], EpisodeTracker],
    ) -> DinoParallelUpdateSummary:
        episode_starts: Counter[str] = Counter()
        natural_terminals: Counter[str] = Counter()
        truncated_terminals: Counter[str] = Counter()
        pending: dict[int, VersionedActionInfo[PPOAuxiliary] | None] = {}
        pending_maps: dict[int, str | None] = {}
        recorded_maps: dict[AgentIdentity, list[str]] = defaultdict(list)
        completed_episodes: list[DinoParallelEpisodeSummary] = []
        response_waits: list[float] = []
        microbatch_wait_seconds = 0.0

        for environment_id in environment_ids:
            self._classify_current_map(
                environment_id,
                current_steps[environment_id],
                current_adapters,
                current_maps,
                episode_starts,
            )
        if self._sampling_mode == "synchronous":
            submissions = self._prepare_synchronous_submissions(
                environment_ids=environment_ids,
                policy_version=policy_version,
                current_steps=current_steps,
                current_adapters=current_adapters,
                current_maps=current_maps,
                pending=pending,
                pending_maps=pending_maps,
            )
        elif self._decide_batch is not None and self._inference_batch_size > 1:
            submissions = {}
            for offset in range(0, len(environment_ids), self._inference_batch_size):
                chunk = environment_ids[offset : offset + self._inference_batch_size]
                submissions.update(
                    self._prepare_synchronous_submissions(
                        environment_ids=chunk,
                        policy_version=policy_version,
                        current_steps=current_steps,
                        current_adapters=current_adapters,
                        current_maps=current_maps,
                        pending=pending,
                        pending_maps=pending_maps,
                    )
                )
        else:
            submissions = {}
            for environment_id in environment_ids:
                handle = self._vector.handles[environment_id]
                if environment_id not in current_maps:
                    pending[environment_id] = None
                    pending_maps[environment_id] = None
                    submissions[environment_id] = self._wait_actions(
                        current_adapters[environment_id]
                    )
                else:
                    info = self._decide(
                        environment_id,
                        int(handle.generation),
                        policy_version,
                        current_steps[environment_id],
                        current_adapters[environment_id],
                    )
                    self._validate_action_info(
                        info,
                        environment_id=environment_id,
                        process_generation=int(handle.generation),
                        policy_version=policy_version,
                    )
                    pending[environment_id] = info
                    pending_maps[environment_id] = current_maps[environment_id]
                    submissions[environment_id] = info.actions
        self._submit_actions(submissions, response_waits)

        consecutive_no_progress = 0
        synchronous_round_made_progress = False
        while pending:
            event_limit = (
                1
                if self._sampling_mode == "synchronous"
                else self._inference_batch_size
            )
            events = self._vector.poll_ready(
                timeout=self._poll_timeout,
                max_events=event_limit,
            )
            if not events:
                consecutive_no_progress += 1
                self._raise_if_stalled(consecutive_no_progress)
                continue
            if (
                self._sampling_mode == "asynchronous"
                and len(events) < event_limit
                and self._inference_batch_wait_seconds > 0.0
            ):
                microbatch_wait_started = time.perf_counter()
                events += self._vector.poll_ready(
                    timeout=self._inference_batch_wait_seconds,
                    max_events=event_limit - len(events),
                )
                microbatch_wait_seconds += (
                    time.perf_counter() - microbatch_wait_started
                )
            if self._sampling_mode == "synchronous" and len(events) != 1:
                raise RuntimeError("parallel environment returned multiple completion events")
            completed_environment_ids: list[int] = []
            batch_made_progress = False
            for event in events:
                if event.environment_id not in pending:
                    raise RuntimeError(
                        f"environment {event.environment_id} completed without an in-flight action"
                    )
                completed_environment_ids.append(event.environment_id)
                info = pending.pop(event.environment_id)
                submitted_map = pending_maps.pop(event.environment_id)
                transitions_before = collector.accepted_transition_count
                if info is not None:
                    collector.record(event, info)
                if collector.accepted_transition_count > transitions_before:
                    batch_made_progress = True
                    synchronous_round_made_progress = True

                if event.error is not None:
                    handle = self._vector.restart_environment(event.environment_id)
                    replacement = self._restart_state_resolver(
                        event.environment_id,
                        handle,
                    )
                    if not isinstance(replacement, DinoParallelRestartState):
                        raise TypeError(
                            "restart_state_resolver must return DinoParallelRestartState"
                        )
                    current_adapters[event.environment_id] = replacement.adapter
                    current_steps[event.environment_id] = replacement.initial_step
                    current_maps.pop(event.environment_id, None)
                    self._classify_current_map(
                        event.environment_id,
                        current_steps[event.environment_id],
                        current_adapters,
                        current_maps,
                        episode_starts,
                    )
                    continue

                assert event.step is not None
                if event.completed_at_seconds > 0.0:
                    self._ready_since[event.environment_id] = (
                        event.completed_at_seconds
                    )
                current_steps[event.environment_id] = event.step
                tracker = episode_trackers.setdefault(
                    (event.environment_id, event.process_generation),
                    EpisodeTracker(),
                )
                for episode in tracker.record(event.step):
                    ended = event.step.terminated | event.step.truncated
                    terminal_indices = np.flatnonzero(
                        (event.step.agent_ids == episode.agent_id) & ended
                    )
                    if len(terminal_indices) != 1:
                        raise RuntimeError(
                            "completed episode must have exactly one terminal event"
                        )
                    terminal_reward = float(
                        event.step.rewards[int(terminal_indices[0])]
                    )
                    completed_episodes.append(
                        DinoParallelEpisodeSummary(
                            environment_id=event.environment_id,
                            process_generation=event.process_generation,
                            map_name=(
                                submitted_map
                                or current_maps.get(event.environment_id)
                                or "unclassified"
                            ),
                            agent_id=episode.agent_id,
                            episode_index=episode.episode_index,
                            episode_return=episode.episode_return,
                            episode_length=episode.episode_length,
                            terminated=episode.terminated,
                            truncated=episode.truncated,
                            terminal_reward=terminal_reward,
                            success=(
                                episode.terminated
                                and terminal_reward >= self._success_reward_threshold
                            ),
                        )
                    )
                if info is not None:
                    if len(info.agent_ids) and submitted_map is None:
                        raise RuntimeError(
                            f"environment {event.environment_id} produced transitions before map classification"
                        )
                    for raw_agent_id in info.agent_ids:
                        identity = AgentIdentity(
                            event.environment_id,
                            event.process_generation,
                            int(raw_agent_id),
                        )
                        recorded_maps[identity].append(submitted_map)
                    natural = int(event.step.terminated.sum())
                    truncated = int(event.step.truncated.sum())
                    natural_terminals[submitted_map] += natural
                    truncated_terminals[submitted_map] += truncated
                    if natural or truncated:
                        current_maps.pop(event.environment_id, None)
                self._classify_current_map(
                    event.environment_id,
                    event.step,
                    current_adapters,
                    current_maps,
                    episode_starts,
                )
                if info is None and event.environment_id in current_maps:
                    batch_made_progress = True
                    synchronous_round_made_progress = True

            if batch_made_progress:
                consecutive_no_progress = 0
            elif self._sampling_mode != "synchronous":
                consecutive_no_progress += 1

            if not collector.accepting_submissions:
                self._raise_if_stalled(consecutive_no_progress)
                continue
            if self._sampling_mode == "synchronous":
                if pending:
                    self._raise_if_stalled(consecutive_no_progress)
                    continue
                if synchronous_round_made_progress:
                    consecutive_no_progress = 0
                else:
                    consecutive_no_progress += 1
                self._raise_if_stalled(consecutive_no_progress)
                synchronous_round_made_progress = False
                submissions = self._prepare_synchronous_submissions(
                    environment_ids=environment_ids,
                    policy_version=policy_version,
                    current_steps=current_steps,
                    current_adapters=current_adapters,
                    current_maps=current_maps,
                    pending=pending,
                    pending_maps=pending_maps,
                )
                self._submit_actions(submissions, response_waits)
                self._raise_if_stalled(consecutive_no_progress)
                continue
            submissions = {}
            for offset in range(
                0, len(completed_environment_ids), self._inference_batch_size
            ):
                chunk = tuple(
                    completed_environment_ids[
                        offset : offset + self._inference_batch_size
                    ]
                )
                submissions.update(
                    self._prepare_synchronous_submissions(
                        environment_ids=chunk,
                        policy_version=policy_version,
                        current_steps=current_steps,
                        current_adapters=current_adapters,
                        current_maps=current_maps,
                        pending=pending,
                        pending_maps=pending_maps,
                    )
                )
            self._submit_actions(submissions, response_waits)
            self._raise_if_stalled(consecutive_no_progress)

        collector.seal_and_bootstrap(self._bootstrap(collector))
        batch = collector.build_batch()
        contributions = Counter(
            transition.identity.environment_id for transition in batch.transitions
        )
        if set(contributions) != set(environment_ids) or any(
            contributions[environment_id] <= 0 for environment_id in environment_ids
        ):
            raise RuntimeError(
                f"not all parallel environments contributed transitions: {dict(contributions)}"
            )

        transitions_by_map: Counter[str] = Counter()
        map_offsets: Counter[AgentIdentity] = Counter()
        for transition in batch.transitions:
            offset = map_offsets[transition.identity]
            maps = recorded_maps[transition.identity]
            if offset >= len(maps):
                raise RuntimeError("a collected transition is missing map provenance")
            transitions_by_map[maps[offset]] += 1
            map_offsets[transition.identity] += 1

        optimizer_update_started = time.perf_counter()
        metrics_object = self._update_function(
            collector,
            self._updater,
            gamma=self._gamma,
            gae_lambda=self._gae_lambda,
            device=self._device,
            environment_steps=environment_steps_before + len(batch.transitions),
        )
        optimizer_update_seconds = time.perf_counter() - optimizer_update_started
        metrics = self._finite_metrics(metrics_object)
        response_wait_mean_seconds = (
            sum(response_waits) / len(response_waits) if response_waits else 0.0
        )
        return DinoParallelUpdateSummary(
            policy_version=policy_version,
            transition_count=len(batch.transitions),
            environment_contributions=tuple(sorted(contributions.items())),
            transitions_by_map=tuple(sorted(transitions_by_map.items())),
            episode_starts_by_map=tuple(sorted(episode_starts.items())),
            natural_terminals_by_map=tuple(sorted(natural_terminals.items())),
            truncated_terminals_by_map=tuple(sorted(truncated_terminals.items())),
            metrics=tuple(sorted(metrics.items())),
            response_wait_mean_seconds=response_wait_mean_seconds,
            response_wait_max_seconds=max(response_waits, default=0.0),
            response_wait_count=len(response_waits),
            microbatch_wait_seconds=microbatch_wait_seconds,
            optimizer_update_seconds=optimizer_update_seconds,
            completed_episodes=tuple(completed_episodes),
        )

    def _submit_actions(
        self,
        submissions: Mapping[int, np.ndarray],
        response_waits: list[float],
    ) -> None:
        submitted_at = time.perf_counter()
        for environment_id in submissions:
            ready_at = self._ready_since.pop(environment_id, None)
            if ready_at is not None:
                response_waits.append(max(0.0, submitted_at - ready_at))
        self._vector.submit_actions(submissions)

    def _prepare_synchronous_submissions(
        self,
        *,
        environment_ids: tuple[int, ...],
        policy_version: int,
        current_steps: Mapping[int, EnvStep],
        current_adapters: Mapping[int, _Adapter],
        current_maps: Mapping[int, str],
        pending: dict[int, VersionedActionInfo[PPOAuxiliary] | None],
        pending_maps: dict[int, str | None],
    ) -> dict[int, np.ndarray]:
        requests: list[DinoParallelDecisionRequest] = []
        submissions: dict[int, np.ndarray] = {}
        for environment_id in environment_ids:
            if environment_id not in current_maps:
                pending[environment_id] = None
                pending_maps[environment_id] = None
                submissions[environment_id] = self._wait_actions(
                    current_adapters[environment_id]
                )
                continue
            handle = self._vector.handles[environment_id]
            requests.append(
                DinoParallelDecisionRequest(
                    environment_id=environment_id,
                    process_generation=int(handle.generation),
                    policy_version=policy_version,
                    step=current_steps[environment_id],
                    adapter=current_adapters[environment_id],
                )
            )

        if not requests:
            return submissions
        if self._decide_batch is None:
            infos = {
                request.environment_id: self._decide(
                    request.environment_id,
                    request.process_generation,
                    request.policy_version,
                    request.step,
                    request.adapter,
                )
                for request in requests
            }
        else:
            infos = dict(self._decide_batch(tuple(requests)))
        expected_ids = {request.environment_id for request in requests}
        if set(infos) != expected_ids:
            raise ValueError(
                "batch decision environment IDs mismatch; "
                f"expected={sorted(expected_ids)}, actual={sorted(infos)}"
            )
        for request in requests:
            info = infos[request.environment_id]
            self._validate_action_info(
                info,
                environment_id=request.environment_id,
                process_generation=request.process_generation,
                policy_version=request.policy_version,
            )
            pending[request.environment_id] = info
            pending_maps[request.environment_id] = current_maps[request.environment_id]
            submissions[request.environment_id] = info.actions
        return submissions

    def _classify_current_map(
        self,
        environment_id: int,
        step: EnvStep,
        current_adapters: Mapping[int, _Adapter],
        current_maps: dict[int, str],
        episode_starts: Counter[str],
    ) -> None:
        if environment_id in current_maps:
            return
        decision_count = len(current_adapters[environment_id].pending_agent_ids)
        observed = self._map_classifier(step, decision_count)
        if observed is None:
            return
        current_maps[environment_id] = observed
        episode_starts[observed] += 1

    @staticmethod
    def _wait_actions(adapter: _Adapter) -> np.ndarray:
        action_size = int(adapter.continuous_action_size)
        if action_size <= 0:
            raise ValueError("Dino warm-up requires at least one continuous action")
        actions = np.zeros(
            (len(adapter.pending_agent_ids), action_size), dtype=np.float32
        )
        actions[:, -1] = -1.0
        return actions

    def _raise_if_stalled(self, consecutive_no_progress: int) -> None:
        if consecutive_no_progress >= self._max_consecutive_no_progress:
            raise TimeoutError(
                "no rollout progress after "
                f"{consecutive_no_progress} consecutive polls/events"
            )

    @staticmethod
    def _validate_action_info(
        info: VersionedActionInfo[PPOAuxiliary],
        *,
        environment_id: int,
        process_generation: int,
        policy_version: int,
    ) -> None:
        if info.environment_id != environment_id:
            raise ValueError("action info environment provenance does not match submission")
        if info.process_generation != process_generation:
            raise ValueError("action info generation provenance does not match submission")
        if info.policy_version != policy_version:
            raise ValueError("action info policy provenance does not match submission")

    @staticmethod
    def _finite_metrics(metrics_object: object) -> dict[str, float]:
        raw = (
            metrics_object.as_dict()
            if hasattr(metrics_object, "as_dict")
            else metrics_object
        )
        if not isinstance(raw, Mapping):
            raise TypeError("PPO updater metrics must be a mapping or expose as_dict()")
        metrics: dict[str, float] = {}
        for name, value in raw.items():
            if isinstance(value, bool) or not isinstance(value, (int, float, np.number)):
                raise TypeError(f"metric {name!r} must be numeric")
            number = float(value)
            if not math.isfinite(number):
                raise RuntimeError(f"metric {name!r} is not finite")
            metrics[str(name)] = number
        return metrics
