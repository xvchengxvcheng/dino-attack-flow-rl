from __future__ import annotations

from dataclasses import dataclass
from types import SimpleNamespace

import numpy as np
import pytest
import torch

from flow_rl.envs.parallel_unity import ParallelEnvEvent
from flow_rl.envs.types import EnvStep
from flow_rl.training.collector import PPOAuxiliary
from flow_rl.training.dino_parallel_ppo import (
    DinoParallelPPOTrainer,
    DinoParallelRestartState,
)
from flow_rl.training.versioned_collector import VersionedActionInfo


def _step(
    environment_id: int,
    observation: float,
    *,
    reward: float = 1.0,
    terminated: bool = False,
    truncated: bool = False,
) -> EnvStep:
    return EnvStep(
        agent_ids=np.asarray([environment_id + 1], dtype=np.int64),
        observations=(np.asarray([[observation]], dtype=np.float32),),
        rewards=np.asarray([reward], dtype=np.float32),
        terminated=np.asarray([terminated], dtype=bool),
        truncated=np.asarray([truncated], dtype=bool),
    )


class _Adapter:
    def __init__(self, environment_id: int, *, agent_id: int | None = None) -> None:
        self.pending_agent_ids = np.asarray(
            [environment_id + 1 if agent_id is None else agent_id], dtype=np.int64
        )
        self.continuous_action_size = 1


class _VectorEnv:
    def __init__(
        self,
        event_log: list[tuple[str, int, int]],
        *,
        environment_count: int = 4,
        close_error: BaseException | None = None,
        return_no_events: bool = False,
    ) -> None:
        self.handles = {
            environment_id: SimpleNamespace(generation=0)
            for environment_id in range(environment_count)
        }
        self._ready: list[ParallelEnvEvent] = []
        self._submitted_versions: dict[int, int] = {}
        self._event_log = event_log
        self._close_error = close_error
        self._return_no_events = return_no_events
        self.closed = False

    def submit_actions(self, actions_by_environment: dict[int, np.ndarray]) -> None:
        for environment_id, actions in actions_by_environment.items():
            version = int(round(float(actions[0, 0])))
            self._submitted_versions[environment_id] = version
            self._ready.append(
                ParallelEnvEvent(
                    environment_id=environment_id,
                    process_generation=0,
                    step=_step(environment_id, observation=version + 1.0),
                    error=None,
                )
            )

    def poll_ready(
        self, *, timeout: float | None = 0.0, max_events: int | None = None
    ) -> tuple[ParallelEnvEvent, ...]:
        del timeout
        if self._return_no_events:
            return ()
        if not self._ready:
            return ()
        count = len(self._ready) if max_events is None else max_events
        events = tuple(self._ready[:count])
        del self._ready[:count]
        for event in events:
            self._event_log.append(
                (
                    "complete",
                    event.environment_id,
                    self._submitted_versions[event.environment_id],
                )
            )
        return events

    def restart_environment(self, environment_id: int) -> SimpleNamespace:
        raise AssertionError(f"unexpected restart for environment {environment_id}")

    def close(self) -> None:
        self.closed = True
        if self._close_error is not None:
            raise self._close_error


class _DelayedMapVector(_VectorEnv):
    def __init__(
        self, event_log: list[tuple[str, int, int]], *, environment_count: int = 4
    ) -> None:
        super().__init__(event_log, environment_count=environment_count)
        self.submitted_actions: dict[int, list[float]] = {
            environment_id: [] for environment_id in self.handles
        }

    def submit_actions(self, actions_by_environment: dict[int, np.ndarray]) -> None:
        for environment_id, actions in actions_by_environment.items():
            action = float(actions[0, 0])
            self.submitted_actions[environment_id].append(action)
            self._submitted_versions[environment_id] = int(round(action))
            self._ready.append(
                ParallelEnvEvent(
                    environment_id=environment_id,
                    process_generation=0,
                    step=_step(
                        environment_id,
                        observation=float(len(self.submitted_actions[environment_id])),
                    ),
                    error=None,
                )
            )


class _CrossUpdateEpisodeVector(_VectorEnv):
    def __init__(self, event_log: list[tuple[str, int, int]]) -> None:
        super().__init__(event_log)
        self._steps = {environment_id: 0 for environment_id in self.handles}

    def submit_actions(self, actions_by_environment: dict[int, np.ndarray]) -> None:
        for environment_id, actions in actions_by_environment.items():
            self._steps[environment_id] += 1
            step_index = self._steps[environment_id]
            version = int(round(float(actions[0, 0])))
            self._submitted_versions[environment_id] = version
            self._ready.append(
                ParallelEnvEvent(
                    environment_id=environment_id,
                    process_generation=0,
                    step=_step(
                        environment_id,
                        observation=float(step_index),
                        reward=1.0 if step_index == 1 else 10.0,
                        terminated=step_index == 2,
                    ),
                    error=None,
                )
            )


class _SynchronousProbeVector(_VectorEnv):
    def __init__(
        self, event_log: list[tuple[str, int, int]], *, environment_count: int
    ) -> None:
        super().__init__(event_log, environment_count=environment_count)
        self.completed_count = 0
        self.submission_completed_counts: list[int] = []
        self.submission_environment_ids: list[tuple[int, ...]] = []

    def submit_actions(self, actions_by_environment: dict[int, np.ndarray]) -> None:
        self.submission_completed_counts.append(self.completed_count)
        self.submission_environment_ids.append(tuple(sorted(actions_by_environment)))
        super().submit_actions(actions_by_environment)

    def poll_ready(
        self, *, timeout: float | None = 0.0, max_events: int | None = None
    ) -> tuple[ParallelEnvEvent, ...]:
        events = super().poll_ready(timeout=timeout, max_events=max_events)
        self.completed_count += len(events)
        return events


@dataclass(frozen=True)
class _Metrics:
    loss: float

    def as_dict(self) -> dict[str, float]:
        return {"loss": self.loss}


class _Updater:
    def __init__(self, event_log: list[tuple[str, int, int]], *, fail: bool = False) -> None:
        self._event_log = event_log
        self._fail = fail
        self.versions: list[int] = []
        self.environment_steps: list[int] = []

    def update(self, batch: object, *, environment_steps: int) -> _Metrics:
        version = int(round(float(batch.actions[0, 0].item())))
        self._event_log.append(("update", version, environment_steps))
        self.versions.append(version)
        self.environment_steps.append(environment_steps)
        if self._fail:
            raise ArithmeticError("non-finite update")
        return _Metrics(loss=0.25 + version)


def _trainer(
    event_log: list[tuple[str, int, int]],
    *,
    fail_update: bool = False,
    vector: _VectorEnv | None = None,
    initial_environment_steps: int = 0,
    total_environment_steps: int = 8,
    initial_policy_version: int = 0,
    restart_state_resolver=None,
    action_info_override: tuple[str, int] | None = None,
    map_classifier=None,
    rollout_size: int = 1,
    sampling_mode: str = "asynchronous",
    batch_decision_sizes: list[int] | None = None,
    inference_batch_size: int = 1,
    inference_batch_wait_seconds: float = 0.0,
    update_function=None,
) -> tuple[DinoParallelPPOTrainer, _VectorEnv, _Updater]:
    vector = vector or _VectorEnv(event_log)
    environment_ids = tuple(sorted(vector.handles))
    adapters = {environment_id: _Adapter(environment_id) for environment_id in environment_ids}
    initial_steps = {
        environment_id: _step(environment_id, observation=0.0)
        for environment_id in environment_ids
    }
    updater = _Updater(event_log, fail=fail_update)

    def decide(
        environment_id: int,
        process_generation: int,
        policy_version: int,
        step: EnvStep,
        adapter: _Adapter,
    ) -> VersionedActionInfo[PPOAuxiliary]:
        event_log.append(("decide", environment_id, policy_version))
        event_log.append(
            (
                "decision-state",
                process_generation,
                int(adapter.pending_agent_ids[0]),
                int(step.observations[0][0, 0]),
            )
        )
        observations = tuple(
            np.array(stream[:1], dtype=np.float32, copy=True)
            for stream in step.observations
        )
        provenance = {
            "environment_id": environment_id,
            "process_generation": process_generation,
            "policy_version": policy_version,
        }
        if action_info_override is not None:
            provenance[action_info_override[0]] = action_info_override[1]
        return VersionedActionInfo(
            **provenance,
            agent_ids=adapter.pending_agent_ids,
            observations=observations,
            actions=np.asarray([[float(policy_version)]], dtype=np.float32),
            values=np.asarray([0.0], dtype=np.float32),
            auxiliaries=(PPOAuxiliary(old_log_prob=-0.5),),
        )

    def decide_batch(requests):
        if batch_decision_sizes is not None:
            batch_decision_sizes.append(len(requests))
        return {
            request.environment_id: decide(
                request.environment_id,
                request.process_generation,
                request.policy_version,
                request.step,
                request.adapter,
            )
            for request in requests
        }

    trainer_kwargs = {}
    if update_function is not None:
        trainer_kwargs["update_function"] = update_function
    trainer = DinoParallelPPOTrainer(
        vector_env=vector,
        adapters=adapters,
        initial_steps=initial_steps,
        updater=updater,
        device=torch.device("cpu"),
        # One accepted completion closes submissions immediately; the other
        # three already in-flight actions must still be drained.
        rollout_size=rollout_size,
        total_environment_steps=total_environment_steps,
        initial_environment_steps=initial_environment_steps,
        gamma=0.995,
        gae_lambda=0.95,
        decide=decide,
        bootstrap=lambda collector: {
            target: 0.0 for target in collector.bootstrap_targets
        },
        map_classifier=map_classifier
        or (
            lambda step, count: (
                "map1" if int(step.agent_ids[0]) <= 2 else "map2"
            )
        ),
        restart_state_resolver=restart_state_resolver
        or (
            lambda environment_id, handle: (_ for _ in ()).throw(
                AssertionError(f"unexpected restart for environment {environment_id}")
            )
        ),
        poll_timeout=0.01,
        max_consecutive_no_progress=3,
        initial_policy_version=initial_policy_version,
        sampling_mode=sampling_mode,
        decide_batch=decide_batch,
        inference_batch_size=inference_batch_size,
        inference_batch_wait_seconds=inference_batch_wait_seconds,
        **trainer_kwargs,
    )
    return trainer, vector, updater


def test_parallel_core_accepts_an_injected_on_policy_update_strategy() -> None:
    event_log: list[tuple[str, int, int]] = []
    calls: list[tuple[float, float, str, int]] = []

    def update_function(
        collector,
        updater,
        *,
        gamma,
        gae_lambda,
        device,
        environment_steps,
    ):
        del collector, updater
        calls.append((gamma, gae_lambda, device.type, environment_steps))
        return _Metrics(loss=0.75)

    trainer, vector, _ = _trainer(
        event_log,
        total_environment_steps=4,
        update_function=update_function,
    )

    summary = trainer.train()

    assert calls == [(0.995, 0.95, "cpu", 4)]
    assert summary.updates[0].metrics == (("loss", 0.75),)
    assert vector.closed


def test_asynchronous_sampling_microbatches_ready_environments() -> None:
    event_log: list[tuple[str, int, int]] = []
    batch_decision_sizes: list[int] = []
    trainer, vector, _ = _trainer(
        event_log,
        rollout_size=8,
        total_environment_steps=8,
        batch_decision_sizes=batch_decision_sizes,
        inference_batch_size=4,
        inference_batch_wait_seconds=0.002,
    )

    summary = trainer.train()

    assert batch_decision_sizes == [4, 4]
    assert summary.total_transitions == 8
    assert vector.closed


def test_asynchronous_sampling_supports_twenty_four_environment_microbatches() -> None:
    event_log: list[tuple[str, int, int]] = []
    batch_decision_sizes: list[int] = []
    vector = _VectorEnv(event_log, environment_count=24)
    trainer, _, _ = _trainer(
        event_log,
        vector=vector,
        rollout_size=48,
        total_environment_steps=48,
        batch_decision_sizes=batch_decision_sizes,
        inference_batch_size=24,
        inference_batch_wait_seconds=0.002,
    )

    summary = trainer.train()

    assert batch_decision_sizes == [24, 24]
    assert summary.total_transitions == 48
    assert vector.closed


@pytest.mark.parametrize("environment_count", (16, 20, 24))
def test_synchronous_sampling_waits_for_the_full_environment_round(
    environment_count: int,
) -> None:
    event_log: list[tuple[str, int, int]] = []
    vector = _SynchronousProbeVector(
        event_log, environment_count=environment_count
    )
    batch_decision_sizes: list[int] = []
    trainer, _, _ = _trainer(
        event_log,
        vector=vector,
        rollout_size=environment_count * 2,
        total_environment_steps=environment_count * 2,
        sampling_mode="synchronous",
        batch_decision_sizes=batch_decision_sizes,
    )

    summary = trainer.train()

    assert vector.submission_environment_ids == [
        tuple(range(environment_count)),
        tuple(range(environment_count)),
    ]
    assert vector.submission_completed_counts == [0, environment_count]
    assert batch_decision_sizes == [environment_count, environment_count]
    assert summary.total_transitions == environment_count * 2
    assert vector.closed


def test_synchronous_preclassification_round_is_not_mistaken_for_a_stall() -> None:
    event_log: list[tuple[str, int, int]] = []
    vector = _DelayedMapVector(event_log, environment_count=16)
    trainer, _, _ = _trainer(
        event_log,
        vector=vector,
        rollout_size=16,
        total_environment_steps=16,
        sampling_mode="synchronous",
        map_classifier=lambda step, count: (
            None
            if float(step.observations[0][0, 0]) < 2.0
            else ("map1" if int(step.agent_ids[0]) <= 8 else "map2")
        ),
    )

    summary = trainer.train()

    assert summary.total_transitions == 16
    assert [len(items) for items in vector.submitted_actions.values()] == [3] * 16
    assert vector.closed


def test_preclassification_step_uses_wait_and_is_not_added_to_rollout() -> None:
    event_log: list[tuple[str, int, int]] = []
    vector = _DelayedMapVector(event_log)
    trainer, _, updater = _trainer(
        event_log,
        vector=vector,
        map_classifier=lambda step, count: (
            None
            if float(step.observations[0][0, 0]) == 0.0
            else ("map1" if int(step.agent_ids[0]) <= 2 else "map2")
        ),
    )

    summary = trainer.train()

    assert [actions[0] for actions in vector.submitted_actions.values()] == [-1.0] * 4
    assert updater.versions == [0, 1]
    assert summary.total_transitions == 8
    assert dict(summary.updates[0].episode_starts_by_map) == {"map1": 2, "map2": 2}
    assert vector.closed


def test_repeated_updates_drain_old_actions_before_advancing_and_use_all_four_envs() -> None:
    event_log: list[tuple[str, int, int]] = []
    trainer, vector, updater = _trainer(event_log)

    summary = trainer.train()

    assert [item.policy_version for item in summary.updates] == [0, 1]
    assert [item.transition_count for item in summary.updates] == [4, 4]
    assert [dict(item.environment_contributions) for item in summary.updates] == [
        {0: 1, 1: 1, 2: 1, 3: 1},
        {0: 1, 1: 1, 2: 1, 3: 1},
    ]
    assert [dict(item.transitions_by_map) for item in summary.updates] == [
        {"map1": 2, "map2": 2},
        {"map1": 2, "map2": 2},
    ]
    first_v1_decision = event_log.index(("decide", 0, 1))
    assert all(
        event_log.index(("complete", environment_id, 0)) < first_v1_decision
        for environment_id in range(4)
    )
    assert updater.versions == [0, 1]
    assert updater.environment_steps == [4, 8]
    assert event_log.index(("update", 0, 4)) < first_v1_decision
    assert summary.total_transitions == 8
    assert vector.closed


def test_completed_episode_spans_updates_and_reports_real_terminal_success() -> None:
    event_log: list[tuple[str, int, int]] = []
    vector = _CrossUpdateEpisodeVector(event_log)
    trainer, _, _ = _trainer(event_log, vector=vector)

    summary = trainer.train()

    assert summary.updates[0].completed_episodes == ()
    episodes = summary.updates[1].completed_episodes
    assert len(episodes) == 4
    assert [episode.environment_id for episode in episodes] == [0, 1, 2, 3]
    assert [episode.map_name for episode in episodes] == [
        "map1",
        "map1",
        "map2",
        "map2",
    ]
    assert [episode.episode_return for episode in episodes] == [11.0] * 4
    assert [episode.episode_length for episode in episodes] == [2] * 4
    assert all(episode.terminated and not episode.truncated for episode in episodes)
    assert all(episode.success for episode in episodes)


def test_resume_reports_absolute_environment_steps_to_the_updater() -> None:
    event_log: list[tuple[str, int, int]] = []
    trainer, vector, updater = _trainer(
        event_log,
        initial_environment_steps=4,
        total_environment_steps=8,
        initial_policy_version=1,
    )

    summary = trainer.train()

    assert [item.policy_version for item in summary.updates] == [1]
    assert updater.environment_steps == [8]
    assert summary.initial_environment_steps == 4
    assert summary.final_environment_steps == 8
    assert summary.total_transitions == 4
    assert vector.closed


def test_one_update_accepts_and_requires_contributions_from_all_eight_envs() -> None:
    event_log: list[tuple[str, int, int]] = []
    vector = _VectorEnv(event_log, environment_count=8)
    trainer, _, updater = _trainer(
        event_log,
        vector=vector,
        total_environment_steps=8,
    )

    summary = trainer.train()

    assert [dict(item.environment_contributions) for item in summary.updates] == [
        {environment_id: 1 for environment_id in range(8)}
    ]
    assert updater.environment_steps == [8]
    assert summary.total_transitions == 8
    assert vector.closed


def test_one_update_accepts_and_requires_contributions_from_all_twelve_envs() -> None:
    event_log: list[tuple[str, int, int]] = []
    vector = _VectorEnv(event_log, environment_count=12)
    trainer, _, updater = _trainer(
        event_log,
        vector=vector,
        total_environment_steps=12,
    )

    summary = trainer.train()

    assert [dict(item.environment_contributions) for item in summary.updates] == [
        {environment_id: 1 for environment_id in range(12)}
    ]
    assert updater.environment_steps == [12]
    assert summary.total_transitions == 12
    assert vector.closed


def test_one_update_accepts_and_requires_contributions_from_all_sixteen_envs() -> None:
    event_log: list[tuple[str, int, int]] = []
    vector = _VectorEnv(event_log, environment_count=16)
    trainer, _, updater = _trainer(
        event_log,
        vector=vector,
        total_environment_steps=16,
    )

    summary = trainer.train()

    assert [dict(item.environment_contributions) for item in summary.updates] == [
        {environment_id: 1 for environment_id in range(16)}
    ]
    assert updater.environment_steps == [16]
    assert summary.total_transitions == 16
    assert vector.closed


def test_preflight_validation_still_closes_the_vector() -> None:
    event_log: list[tuple[str, int, int]] = []
    vector = _VectorEnv(event_log, environment_count=3)
    trainer, _, _ = _trainer(event_log, vector=vector)

    with pytest.raises(
        ValueError, match="four, eight, twelve, sixteen, twenty, or twenty-four"
    ):
        trainer.train()

    assert vector.closed


class _RestartingVector(_VectorEnv):
    def __init__(self, event_log: list[tuple[str, int, int]]) -> None:
        super().__init__(event_log)
        self._failed_once = False
        self._agent_ids = {environment_id: environment_id + 1 for environment_id in range(4)}

    def submit_actions(self, actions_by_environment: dict[int, np.ndarray]) -> None:
        for environment_id, actions in actions_by_environment.items():
            version = int(round(float(actions[0, 0])))
            self._submitted_versions[environment_id] = version
            generation = int(self.handles[environment_id].generation)
            if environment_id == 0 and not self._failed_once:
                self._failed_once = True
                self._ready.append(
                    ParallelEnvEvent(
                        environment_id=0,
                        process_generation=generation,
                        step=None,
                        error=ConnectionError("player exited"),
                    )
                )
                continue
            agent_id = self._agent_ids[environment_id]
            self._ready.append(
                ParallelEnvEvent(
                    environment_id=environment_id,
                    process_generation=generation,
                    step=EnvStep(
                        agent_ids=np.asarray([agent_id], dtype=np.int64),
                        observations=(
                            np.asarray([[99.0 if generation else 1.0]], dtype=np.float32),
                        ),
                        rewards=np.asarray([1.0], dtype=np.float32),
                        terminated=np.asarray([False], dtype=bool),
                        truncated=np.asarray([False], dtype=bool),
                    ),
                    error=None,
                )
            )

    def restart_environment(self, environment_id: int) -> SimpleNamespace:
        self.handles[environment_id] = SimpleNamespace(generation=1)
        self._agent_ids[environment_id] = 101
        return self.handles[environment_id]


def test_restart_uses_explicit_replacement_adapter_step_and_generation() -> None:
    event_log: list[tuple[str, int, int]] = []
    vector = _RestartingVector(event_log)
    adapters = {environment_id: _Adapter(environment_id) for environment_id in range(4)}
    initial_steps = {
        environment_id: _step(environment_id, observation=0.0)
        for environment_id in range(4)
    }
    updater = _Updater(event_log)

    def decide(environment_id, generation, version, step, adapter):
        event_log.append(
            (
                "decision-state",
                generation,
                int(adapter.pending_agent_ids[0]),
                int(step.observations[0][0, 0]),
            )
        )
        return VersionedActionInfo(
            environment_id=environment_id,
            process_generation=generation,
            policy_version=version,
            agent_ids=adapter.pending_agent_ids,
            observations=tuple(np.array(item[:1], copy=True) for item in step.observations),
            actions=np.asarray([[float(version)]], dtype=np.float32),
            values=np.asarray([0.0], dtype=np.float32),
            auxiliaries=(PPOAuxiliary(old_log_prob=-0.5),),
        )

    trainer = DinoParallelPPOTrainer(
        vector_env=vector,
        adapters=adapters,
        initial_steps=initial_steps,
        updater=updater,
        device=torch.device("cpu"),
        rollout_size=1,
        total_environment_steps=4,
        initial_environment_steps=0,
        gamma=0.995,
        gae_lambda=0.95,
        decide=decide,
        bootstrap=lambda collector: {
            target: 0.0 for target in collector.bootstrap_targets
        },
        map_classifier=lambda step, count: "map1",
        restart_state_resolver=lambda environment_id, handle: DinoParallelRestartState(
            adapter=_Adapter(environment_id, agent_id=101),
            initial_step=EnvStep(
                agent_ids=np.asarray([101], dtype=np.int64),
                observations=(np.asarray([[77.0]], dtype=np.float32),),
                rewards=np.asarray([0.0], dtype=np.float32),
                terminated=np.asarray([False], dtype=bool),
                truncated=np.asarray([False], dtype=bool),
            ),
        ),
        poll_timeout=0.01,
        max_consecutive_no_progress=3,
    )

    summary = trainer.train()

    assert summary.total_transitions == 4
    assert ("decision-state", 1, 101, 77) in event_log
    assert vector.closed


def test_close_failure_does_not_mask_the_training_failure() -> None:
    event_log: list[tuple[str, int, int]] = []
    vector = _VectorEnv(event_log, close_error=OSError("close failed"))
    trainer, _, _ = _trainer(event_log, fail_update=True, vector=vector)

    with pytest.raises(ArithmeticError, match="non-finite update") as raised:
        trainer.train()

    assert isinstance(raised.value.__cause__, OSError)
    assert vector.closed


def test_close_failure_after_success_is_reported() -> None:
    event_log: list[tuple[str, int, int]] = []
    vector = _VectorEnv(event_log, close_error=OSError("close failed"))
    trainer, _, _ = _trainer(event_log, vector=vector)

    with pytest.raises(OSError, match="close failed"):
        trainer.train()

    assert vector.closed


def test_repeated_empty_polls_fail_instead_of_waiting_forever() -> None:
    event_log: list[tuple[str, int, int]] = []
    vector = _VectorEnv(event_log, return_no_events=True)
    trainer, _, _ = _trainer(event_log, vector=vector)

    with pytest.raises(TimeoutError, match="no rollout progress"):
        trainer.train()

    assert vector.closed


class _AlwaysFailingVector(_VectorEnv):
    def submit_actions(self, actions_by_environment: dict[int, np.ndarray]) -> None:
        for environment_id, actions in actions_by_environment.items():
            version = int(round(float(actions[0, 0])))
            self._submitted_versions[environment_id] = version
            generation = int(self.handles[environment_id].generation)
            self._ready.append(
                ParallelEnvEvent(
                    environment_id=environment_id,
                    process_generation=generation,
                    step=None,
                    error=ConnectionError("player exited"),
                )
            )

    def restart_environment(self, environment_id: int) -> SimpleNamespace:
        generation = int(self.handles[environment_id].generation) + 1
        self.handles[environment_id] = SimpleNamespace(generation=generation)
        return self.handles[environment_id]


def test_repeated_events_without_transitions_fail_instead_of_spinning() -> None:
    event_log: list[tuple[str, int, int]] = []
    vector = _AlwaysFailingVector(event_log)
    trainer, _, _ = _trainer(
        event_log,
        vector=vector,
        restart_state_resolver=lambda environment_id, handle: DinoParallelRestartState(
            adapter=_Adapter(environment_id),
            initial_step=_step(environment_id, observation=0.0),
        ),
    )

    with pytest.raises(TimeoutError, match="no rollout progress"):
        trainer.train()

    assert vector.closed


@pytest.mark.parametrize(
    ("field", "wrong_value", "message"),
    (
        ("environment_id", 9, "environment provenance"),
        ("process_generation", 9, "generation provenance"),
        ("policy_version", 9, "policy provenance"),
    ),
)
def test_action_info_provenance_is_rejected_before_submit(
    field: str, wrong_value: int, message: str
) -> None:
    event_log: list[tuple[str, int, int]] = []
    trainer, vector, _ = _trainer(
        event_log,
        action_info_override=(field, wrong_value),
    )

    with pytest.raises(ValueError, match=message):
        trainer.train()

    assert not vector._ready
    assert vector.closed


def test_update_failure_closes_all_owned_players_and_does_not_advance_version() -> None:
    event_log: list[tuple[str, int, int]] = []
    trainer, vector, updater = _trainer(event_log, fail_update=True)

    with pytest.raises(ArithmeticError, match="non-finite update"):
        trainer.train()

    assert updater.versions == [0]
    assert not any(item == ("decide", 0, 1) for item in event_log)
    assert vector.closed
