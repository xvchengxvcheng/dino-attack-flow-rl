from __future__ import annotations

import queue
from collections import defaultdict
from pathlib import Path
from threading import Event, Lock, Thread

import numpy as np
import pytest

from flow_rl.envs.parallel_unity import (
    AsyncUnityVectorEnv,
    EnvironmentHandle,
    ParallelEnvironmentFailure,
)
from flow_rl.envs.types import EnvStep


def _step(value: float = 0.0) -> EnvStep:
    return EnvStep(
        agent_ids=np.asarray([7], dtype=np.int64),
        observations=(np.asarray([[value]], dtype=np.float32),),
        rewards=np.asarray([value], dtype=np.float32),
        terminated=np.asarray([False], dtype=bool),
        truncated=np.asarray([False], dtype=bool),
    )


class ControlledAdapter:
    def __init__(self, handle: EnvironmentHandle, gates: dict[int, Event]) -> None:
        self.handle = handle
        self._gates = gates
        self._lock = Lock()
        self.active_calls = 0
        self.maximum_active_calls = 0
        self.fail = False
        self.closed = False

    def step(self, actions: np.ndarray) -> EnvStep:
        with self._lock:
            self.active_calls += 1
            self.maximum_active_calls = max(self.maximum_active_calls, self.active_calls)
        try:
            assert self._gates[self.handle.environment_id].wait(timeout=2.0)
            if self.fail:
                raise ConnectionError(f"env-{self.handle.environment_id} failed")
            return _step(float(actions[0, 0]))
        finally:
            with self._lock:
                self.active_calls -= 1

    def close(self) -> None:
        self.closed = True


class CloseFailingAdapter(ControlledAdapter):
    def close(self) -> None:
        self.closed = True
        raise RuntimeError(f"cleanup-{self.handle.environment_id}")


class PausingCompletionQueue(queue.Queue):
    def __init__(self) -> None:
        super().__init__()
        self.before_put = Event()
        self.release_put = Event()

    def put(self, item: object, block: bool = True, timeout: float | None = None) -> None:
        self.before_put.set()
        assert self.release_put.wait(timeout=2.0)
        super().put(item, block=block, timeout=timeout)


def test_events_arrive_in_completion_order_and_same_adapter_is_never_concurrent(
    tmp_path: Path,
) -> None:
    gates = {index: Event() for index in range(4)}
    adapters: list[ControlledAdapter] = []

    def factory(handle: EnvironmentHandle) -> ControlledAdapter:
        adapter = ControlledAdapter(handle, gates)
        adapters.append(adapter)
        return adapter

    vector = AsyncUnityVectorEnv(
        factory,
        num_envs=4,
        base_worker_id=40,
        base_seed=900,
        log_directory=tmp_path,
    )
    try:
        vector.submit_actions(
            {
                index: np.asarray([[index]], dtype=np.float32)
                for index in range(4)
            }
        )
        with pytest.raises(RuntimeError, match="already has an in-flight step"):
            vector.submit_actions({0: np.asarray([[99.0]], dtype=np.float32)})

        observed: list[int] = []
        for environment_id in (2, 0, 3, 1):
            gates[environment_id].set()
            events = vector.poll_ready(timeout=2.0, max_events=1)
            assert len(events) == 1
            assert events[0].completed_at_seconds > 0.0
            observed.append(events[0].environment_id)
        assert observed == [2, 0, 3, 1]
        assert all(adapter.maximum_active_calls == 1 for adapter in adapters)
    finally:
        vector.close()


def test_restart_preserves_worker_and_environment_seed_but_advances_generation(
    tmp_path: Path,
) -> None:
    gates = {index: Event() for index in range(4)}
    for gate in gates.values():
        gate.set()
    adapters: list[ControlledAdapter] = []

    def factory(handle: EnvironmentHandle) -> ControlledAdapter:
        adapter = ControlledAdapter(handle, gates)
        adapters.append(adapter)
        return adapter

    vector = AsyncUnityVectorEnv(
        factory,
        num_envs=4,
        base_worker_id=70,
        base_seed=1200,
        log_directory=tmp_path,
    )
    original = vector.handles[1]
    restarted = vector.restart_environment(1)
    try:
        assert restarted.environment_id == original.environment_id == 1
        assert restarted.worker_id == original.worker_id == 71
        assert restarted.environment_seed == original.environment_seed == 1201
        assert restarted.generation == original.generation + 1
        assert restarted.episode_index == original.episode_index + 1
        assert restarted.episode_seed != original.episode_seed
        assert restarted.log_directory == tmp_path / "environment-1" / "generation-1"
        assert adapters[1].closed
    finally:
        vector.close()
    assert all(adapter.closed for adapter in adapters)


def test_drain_waits_until_a_completed_callback_publishes_its_event(
    tmp_path: Path,
) -> None:
    gate = Event()
    adapters: list[ControlledAdapter] = []

    def factory(handle: EnvironmentHandle) -> ControlledAdapter:
        adapter = ControlledAdapter(handle, {0: gate})
        adapters.append(adapter)
        return adapter

    vector = AsyncUnityVectorEnv(
        factory,
        num_envs=1,
        base_worker_id=90,
        base_seed=1400,
        log_directory=tmp_path,
    )
    pausing_queue = PausingCompletionQueue()
    vector._completed = pausing_queue
    drain_done = Event()
    drained: list[object] = []

    def run_drain() -> None:
        drained.extend(vector.drain())
        drain_done.set()

    try:
        vector.submit_actions({0: np.asarray([[1.0]], dtype=np.float32)})
        gate.set()
        assert pausing_queue.before_put.wait(timeout=2.0)
        drain_thread = Thread(target=run_drain)
        drain_thread.start()
        assert not drain_done.wait(timeout=0.2), (
            "drain returned before the completed callback published its event"
        )
        pausing_queue.release_put.set()
        drain_thread.join(timeout=2.0)
        assert not drain_thread.is_alive()
        assert len(drained) == 1
        assert drained[0].environment_id == 0
    finally:
        pausing_queue.release_put.set()
        vector.close()


def test_one_failure_does_not_block_other_environments_and_third_consecutive_aborts(
    tmp_path: Path,
) -> None:
    gates = {index: Event() for index in range(4)}
    for gate in gates.values():
        gate.set()
    adapters_by_environment: dict[int, list[ControlledAdapter]] = defaultdict(list)

    def factory(handle: EnvironmentHandle) -> ControlledAdapter:
        adapter = ControlledAdapter(handle, gates)
        adapters_by_environment[handle.environment_id].append(adapter)
        if handle.environment_id == 2:
            adapter.fail = True
        return adapter

    vector = AsyncUnityVectorEnv(
        factory,
        num_envs=4,
        base_worker_id=100,
        base_seed=500,
        log_directory=tmp_path,
        max_consecutive_failures=3,
    )
    try:
        for attempt in range(3):
            vector.submit_actions(
                {
                    0: np.asarray([[attempt]], dtype=np.float32),
                    2: np.asarray([[attempt]], dtype=np.float32),
                }
            )
            if attempt < 2:
                events = vector.drain()
                assert {event.environment_id for event in events} == {0, 2}
                failed = next(event for event in events if event.environment_id == 2)
                assert failed.step is None
                assert isinstance(failed.error, ConnectionError)
                assert next(event for event in events if event.environment_id == 0).step is not None
                vector.restart_environment(2)
            else:
                with pytest.raises(ParallelEnvironmentFailure, match="three consecutive failures"):
                    vector.drain()
                assert all(
                    adapter.closed
                    for environment_adapters in adapters_by_environment.values()
                    for adapter in environment_adapters
                ), "fatal failure must close all adapters before surfacing"
    finally:
        vector.close()
    assert all(
        adapter.closed
        for environment_adapters in adapters_by_environment.values()
        for adapter in environment_adapters
    )


def test_fatal_failure_preserves_primary_error_when_multiple_adapter_closes_fail(
    tmp_path: Path,
) -> None:
    gates = {0: Event(), 1: Event()}
    for gate in gates.values():
        gate.set()
    adapters: list[CloseFailingAdapter] = []

    def factory(handle: EnvironmentHandle) -> CloseFailingAdapter:
        adapter = CloseFailingAdapter(handle, gates)
        adapter.fail = handle.environment_id == 0
        adapters.append(adapter)
        return adapter

    vector = AsyncUnityVectorEnv(
        factory,
        num_envs=2,
        base_worker_id=110,
        base_seed=700,
        log_directory=tmp_path,
        max_consecutive_failures=1,
    )
    vector.submit_actions({0: np.asarray([[0.0]], dtype=np.float32)})

    with pytest.raises(ParallelEnvironmentFailure) as captured:
        vector.drain()

    assert isinstance(captured.value.__cause__, ConnectionError)
    assert str(captured.value.__cause__) == "env-0 failed"
    assert captured.value.cleanup_errors == (
        "RuntimeError: cleanup-0",
        "RuntimeError: cleanup-1",
    )
    assert all(adapter.closed for adapter in adapters)
