from __future__ import annotations

import hashlib
import math
import queue
import threading
import time
from collections.abc import Callable, Mapping, Sequence
from concurrent.futures import Future, ThreadPoolExecutor
from dataclasses import dataclass
from pathlib import Path
from typing import Protocol

import numpy as np

from flow_rl.envs.types import EnvStep


class BlockingEnvironmentAdapter(Protocol):
    def step(self, actions: np.ndarray) -> EnvStep: ...

    def close(self) -> None: ...


@dataclass(frozen=True)
class EnvironmentHandle:
    environment_id: int
    worker_id: int
    environment_seed: int
    episode_index: int
    episode_seed: int
    log_directory: Path
    generation: int


@dataclass(frozen=True)
class ParallelEnvEvent:
    environment_id: int
    process_generation: int
    step: EnvStep | None
    error: BaseException | None
    completed_at_seconds: float = 0.0

    def __post_init__(self) -> None:
        if self.environment_id < 0 or self.process_generation < 0:
            raise ValueError("environment_id and process_generation cannot be negative")
        if (self.step is None) == (self.error is None):
            raise ValueError("an event must contain exactly one of step or error")
        if (
            not math.isfinite(self.completed_at_seconds)
            or self.completed_at_seconds < 0.0
        ):
            raise ValueError("completed_at_seconds must be finite and non-negative")


class ParallelEnvironmentFailure(RuntimeError):
    """Raised when one environment reaches its consecutive failure budget."""

    def __init__(self, event: ParallelEnvEvent, failure_count: int) -> None:
        self.event = event
        self.failure_count = failure_count
        self.cleanup_errors: tuple[str, ...] = ()
        super().__init__(
            f"environment {event.environment_id} reached three consecutive failures "
            f"(configured limit {failure_count}): {event.error}"
        )

    def attach_cleanup_errors(self, errors: Sequence[BaseException]) -> None:
        diagnostics = tuple(
            f"{type(error).__name__}: {error}" for error in errors
        )
        self.cleanup_errors = diagnostics
        if diagnostics:
            primary = str(self.args[0])
            self.args = (
                f"{primary}; close-all diagnostics: {'; '.join(diagnostics)}",
            )


class _AdapterCloseFailure(RuntimeError):
    def __init__(self, errors: Sequence[BaseException]) -> None:
        self.errors = tuple(errors)
        super().__init__(
            "one or more adapters failed to close: "
            + "; ".join(
                f"{type(error).__name__}: {error}" for error in self.errors
            )
        )


AdapterFactory = Callable[[EnvironmentHandle], BlockingEnvironmentAdapter]


def derive_episode_seed(environment_seed: int, episode_index: int) -> int:
    """Derive a stable non-negative 31-bit seed without Python's salted hash."""

    if environment_seed < 0 or episode_index < 0:
        raise ValueError("environment_seed and episode_index cannot be negative")
    digest = hashlib.sha256(
        f"flow-rl-episode:{environment_seed}:{episode_index}".encode("ascii")
    ).digest()
    return int.from_bytes(digest[:4], "little") & 0x7FFF_FFFF


class AsyncUnityVectorEnv:
    """Bounded asynchronous stepping for independent blocking LLAPI adapters.

    A handle owns one adapter and may have at most one submitted step. Completion
    callbacks only enqueue immutable events; adapter replacement and close remain
    serialized in the caller thread.
    """

    def __init__(
        self,
        adapter_factory: AdapterFactory,
        *,
        num_envs: int = 4,
        base_worker_id: int = 0,
        base_seed: int = 0,
        log_directory: Path,
        max_consecutive_failures: int = 3,
    ) -> None:
        if num_envs <= 0:
            raise ValueError("num_envs must be positive")
        if base_worker_id < 0 or base_seed < 0:
            raise ValueError("base_worker_id and base_seed cannot be negative")
        if max_consecutive_failures <= 0:
            raise ValueError("max_consecutive_failures must be positive")
        self._factory = adapter_factory
        self._max_consecutive_failures = int(max_consecutive_failures)
        self._executor = ThreadPoolExecutor(
            max_workers=num_envs,
            thread_name_prefix="flow-rl-unity",
        )
        self._lock = threading.Lock()
        self._completed: queue.Queue[tuple[ParallelEnvEvent, int]] = queue.Queue()
        self._handles: dict[int, EnvironmentHandle] = {}
        self._adapters: dict[int, BlockingEnvironmentAdapter] = {}
        self._all_adapters: list[BlockingEnvironmentAdapter] = []
        self._closed_adapter_ids: set[int] = set()
        self._inflight: dict[int, Future[EnvStep]] = {}
        self._consecutive_failures = {index: 0 for index in range(num_envs)}
        self._closed = False
        root = Path(log_directory)
        try:
            for environment_id in range(num_envs):
                handle = self._make_handle(
                    root=root,
                    environment_id=environment_id,
                    worker_id=base_worker_id + environment_id,
                    environment_seed=base_seed + environment_id,
                    episode_index=0,
                    generation=0,
                )
                self._install(handle)
        except BaseException:
            self.close()
            raise

    @property
    def handles(self) -> Mapping[int, EnvironmentHandle]:
        with self._lock:
            return dict(self._handles)

    @property
    def in_flight_environment_ids(self) -> tuple[int, ...]:
        with self._lock:
            return tuple(sorted(self._inflight))

    def submit_actions(self, actions_by_environment: Mapping[int, np.ndarray]) -> None:
        self._ensure_open()
        if not actions_by_environment:
            return
        with self._lock:
            unknown = set(actions_by_environment) - set(self._adapters)
            if unknown:
                raise KeyError(f"unknown environment IDs: {sorted(unknown)}")
            busy = set(actions_by_environment) & set(self._inflight)
            if busy:
                environment_id = min(busy)
                raise RuntimeError(
                    f"environment {environment_id} already has an in-flight step"
                )
            submissions = tuple(
                (
                    environment_id,
                    self._handles[environment_id].generation,
                    self._adapters[environment_id],
                    np.array(actions, dtype=np.float32, copy=True),
                )
                for environment_id, actions in actions_by_environment.items()
            )
            futures: list[tuple[int, int, Future[EnvStep]]] = []
            for environment_id, generation, adapter, actions in submissions:
                future = self._executor.submit(adapter.step, actions)
                self._inflight[environment_id] = future
                futures.append((environment_id, generation, future))
        # A Future may already be complete, in which case add_done_callback()
        # invokes inline. Never attach callbacks while holding the state lock.
        for environment_id, generation, future in futures:
            future.add_done_callback(
                lambda completed, eid=environment_id, gen=generation: self._on_done(
                    eid, gen, completed
                )
            )

    def poll_ready(
        self,
        *,
        timeout: float | None = 0.0,
        max_events: int | None = None,
    ) -> tuple[ParallelEnvEvent, ...]:
        self._ensure_open()
        if timeout is not None and timeout < 0.0:
            raise ValueError("timeout cannot be negative")
        if max_events is not None and max_events <= 0:
            raise ValueError("max_events must be positive")
        events: list[ParallelEnvEvent] = []
        first = True
        while max_events is None or len(events) < max_events:
            try:
                queued = self._completed.get(
                    block=first and timeout != 0.0,
                    timeout=timeout if first and timeout is not None else None,
                ) if first and timeout != 0.0 else self._completed.get_nowait()
            except queue.Empty:
                break
            first = False
            event, failure_count = queued
            if event.error is not None and failure_count >= self._max_consecutive_failures:
                failure = ParallelEnvironmentFailure(event, failure_count)
                try:
                    self.close()
                except BaseException as cleanup_error:
                    failure.attach_cleanup_errors(
                        cleanup_error.errors
                        if isinstance(cleanup_error, _AdapterCloseFailure)
                        else (cleanup_error,)
                    )
                raise failure from event.error
            events.append(event)
        return tuple(events)

    def drain(self) -> tuple[ParallelEnvEvent, ...]:
        self._ensure_open()
        events: list[ParallelEnvEvent] = []
        while True:
            with self._lock:
                pending = bool(self._inflight)
            if not pending:
                events.extend(self.poll_ready())
                return tuple(events)
            events.extend(self.poll_ready(timeout=None, max_events=1))

    def restart_environment(self, environment_id: int) -> EnvironmentHandle:
        self._ensure_open()
        with self._lock:
            if environment_id not in self._handles:
                raise KeyError(f"unknown environment ID {environment_id}")
            if environment_id in self._inflight:
                raise RuntimeError(
                    f"cannot restart environment {environment_id} with an in-flight step"
                )
            previous = self._handles[environment_id]
            previous_adapter = self._adapters[environment_id]
        self._close_adapter(previous_adapter)
        handle = self._make_handle(
            root=previous.log_directory.parents[1],
            environment_id=environment_id,
            worker_id=previous.worker_id,
            environment_seed=previous.environment_seed,
            episode_index=previous.episode_index + 1,
            generation=previous.generation + 1,
        )
        self._install(handle)
        return handle

    def close(self) -> None:
        with self._lock:
            if self._closed:
                return
            self._closed = True
        self._executor.shutdown(wait=True, cancel_futures=False)
        cleanup_errors: list[BaseException] = []
        for adapter in tuple(self._all_adapters):
            try:
                self._close_adapter(adapter)
            except BaseException as error:  # close every owned process before surfacing one
                cleanup_errors.append(error)
        if cleanup_errors:
            raise _AdapterCloseFailure(cleanup_errors) from cleanup_errors[0]

    def __enter__(self) -> "AsyncUnityVectorEnv":
        self._ensure_open()
        return self

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None:
        self.close()

    def _make_handle(
        self,
        *,
        root: Path,
        environment_id: int,
        worker_id: int,
        environment_seed: int,
        episode_index: int,
        generation: int,
    ) -> EnvironmentHandle:
        return EnvironmentHandle(
            environment_id=environment_id,
            worker_id=worker_id,
            environment_seed=environment_seed,
            episode_index=episode_index,
            episode_seed=derive_episode_seed(environment_seed, episode_index),
            log_directory=(
                root / f"environment-{environment_id}" / f"generation-{generation}"
            ),
            generation=generation,
        )

    def _install(self, handle: EnvironmentHandle) -> None:
        handle.log_directory.mkdir(parents=True, exist_ok=False)
        adapter = self._factory(handle)
        with self._lock:
            self._handles[handle.environment_id] = handle
            self._adapters[handle.environment_id] = adapter
            self._all_adapters.append(adapter)

    def _on_done(
        self,
        environment_id: int,
        generation: int,
        future: Future[EnvStep],
    ) -> None:
        completed_at_seconds = time.perf_counter()
        try:
            step = future.result()
            if not isinstance(step, EnvStep):
                raise TypeError("adapter.step() must return EnvStep")
            event = ParallelEnvEvent(
                environment_id,
                generation,
                step,
                None,
                completed_at_seconds,
            )
        except BaseException as error:
            event = ParallelEnvEvent(
                environment_id,
                generation,
                None,
                error,
                completed_at_seconds,
            )
        with self._lock:
            current = self._inflight.get(environment_id)
            if event.error is None:
                self._consecutive_failures[environment_id] = 0
            else:
                self._consecutive_failures[environment_id] += 1
            failure_count = self._consecutive_failures[environment_id]
            # Publication and removal share the drain predicate's lock. Drain
            # therefore cannot observe zero in-flight work before this event is
            # visible in the completion queue.
            self._completed.put((event, failure_count))
            if current is future:
                del self._inflight[environment_id]

    def _close_adapter(self, adapter: BlockingEnvironmentAdapter) -> None:
        identity = id(adapter)
        if identity in self._closed_adapter_ids:
            return
        adapter.close()
        self._closed_adapter_ids.add(identity)

    def _ensure_open(self) -> None:
        with self._lock:
            if self._closed:
                raise RuntimeError("AsyncUnityVectorEnv is closed")
