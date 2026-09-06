from __future__ import annotations

from collections.abc import Callable, Mapping
from dataclasses import dataclass
from types import MappingProxyType
from typing import Generic, TypeVar

import numpy as np

from flow_rl.envs.types import EnvStep


AuxT = TypeVar("AuxT")


@dataclass(frozen=True)
class DecisionBatch(Generic[AuxT]):
    observations: tuple[np.ndarray, ...]
    actions: np.ndarray
    values: np.ndarray
    auxiliaries: tuple[AuxT, ...]

    def __post_init__(self) -> None:
        if not isinstance(self.observations, tuple) or not self.observations:
            raise TypeError("observations must be a non-empty tuple")
        actions = np.asarray(self.actions)
        values = np.asarray(self.values)
        if actions.dtype != np.float32 or actions.ndim != 2:
            raise TypeError("actions must be a two-dimensional float32 array")
        batch_size = actions.shape[0]
        if values.dtype != np.float32 or values.shape != (batch_size,):
            raise TypeError("values must be a one-dimensional float32 array")
        if len(self.auxiliaries) != batch_size:
            raise ValueError("auxiliaries must share the decision batch size")
        for index, observation in enumerate(self.observations):
            array = np.asarray(observation)
            if array.dtype != np.float32 or array.shape[0] != batch_size:
                raise TypeError(
                    f"observations[{index}] must be float32 with matching batch"
                )
            if not np.isfinite(array).all():
                raise ValueError("decision observations must contain only finite values")
        if not np.isfinite(actions).all() or not np.isfinite(values).all():
            raise ValueError("decision actions and values must contain only finite values")


@dataclass(frozen=True)
class PendingDecision(Generic[AuxT]):
    agent_id: int
    observation: tuple[np.ndarray, ...]
    action: np.ndarray
    value: float
    auxiliary: AuxT


@dataclass(frozen=True)
class CollectedTransition(Generic[AuxT]):
    agent_id: int
    observation: tuple[np.ndarray, ...]
    action: np.ndarray
    reward: float
    value: float
    next_value: float
    terminated: bool
    truncated: bool
    auxiliary: AuxT


@dataclass(frozen=True)
class RolloutCollection:
    actions: np.ndarray
    decision_agent_ids: tuple[int, ...]
    completed_transition_count: int


DecisionProvider = Callable[[tuple[np.ndarray, ...]], DecisionBatch[AuxT]]
BootstrapProvider = Callable[[tuple[np.ndarray, ...]], np.ndarray]


class OnPolicyCollectorCore(Generic[AuxT]):
    def __init__(self) -> None:
        self._pending: dict[int, PendingDecision[AuxT]] = {}
        self._trajectories: dict[int, list[CollectedTransition[AuxT]]] = {}
        self._action_size: int | None = None
        self._started = False

    @property
    def pending_agent_ids(self) -> tuple[int, ...]:
        return tuple(sorted(self._pending))

    @property
    def completed_transition_count(self) -> int:
        return sum(len(trajectory) for trajectory in self._trajectories.values())

    def trajectories(self) -> Mapping[int, tuple[CollectedTransition[AuxT], ...]]:
        return MappingProxyType(
            {
                agent_id: tuple(trajectory)
                for agent_id, trajectory in self._trajectories.items()
            }
        )

    def clear_completed(self) -> None:
        self._trajectories.clear()

    def reset(
        self,
        step: EnvStep,
        *,
        decide: DecisionProvider[AuxT],
        bootstrap: BootstrapProvider,
    ) -> RolloutCollection:
        self._pending.clear()
        self._trajectories.clear()
        self._action_size = None
        self._started = True
        return self._process(step, decide=decide, bootstrap=bootstrap)

    def step(
        self,
        step: EnvStep,
        *,
        decide: DecisionProvider[AuxT],
        bootstrap: BootstrapProvider,
    ) -> RolloutCollection:
        if not self._started:
            raise RuntimeError("reset() must be called before step()")
        return self._process(step, decide=decide, bootstrap=bootstrap)

    def replace_pending(
        self,
        step: EnvStep,
        *,
        decide: DecisionProvider[AuxT],
    ) -> RolloutCollection:
        """Resample current decisions after an on-policy update.

        The environment has already emitted this decision batch, but its actions
        have not yet been submitted.  Replacing the pending records after the
        behavior snapshot refresh keeps the next submitted actions and their
        stored old-log-probs on the same policy snapshot.
        """
        if not self._started:
            raise RuntimeError("reset() must be called before replace_pending()")
        ended = step.terminated | step.truncated
        decision_indices = np.flatnonzero(~ended)
        raw_decisions = tuple(
            observation[decision_indices] for observation in step.observations
        )
        if len(decision_indices) == 0:
            return RolloutCollection(
                actions=np.empty((0, 0 if self._action_size is None else self._action_size), dtype=np.float32),
                decision_agent_ids=(),
                completed_transition_count=self.completed_transition_count,
            )
        decisions = decide(raw_decisions)
        if decisions.actions.shape[0] != len(decision_indices):
            raise ValueError("decision provider returned a wrong batch size")
        if self._action_size is None:
            self._action_size = decisions.actions.shape[1]
        elif decisions.actions.shape[1] != self._action_size:
            raise ValueError("decision provider changed action size")
        decision_agent_ids: list[int] = []
        for output_index, event_index in enumerate(decision_indices):
            agent_id = int(step.agent_ids[event_index])
            if agent_id not in self._pending:
                raise RuntimeError(
                    f"cannot replace missing pending action for Agent ID {agent_id}"
                )
            decision_agent_ids.append(agent_id)
            self._pending[agent_id] = PendingDecision(
                agent_id=agent_id,
                observation=tuple(
                    np.array(stream[output_index], dtype=np.float32, copy=True)
                    for stream in decisions.observations
                ),
                action=np.array(decisions.actions[output_index], dtype=np.float32, copy=True),
                value=float(decisions.values[output_index]),
                auxiliary=decisions.auxiliaries[output_index],
            )
        return RolloutCollection(
            actions=np.array(decisions.actions, dtype=np.float32, copy=True),
            decision_agent_ids=tuple(decision_agent_ids),
            completed_transition_count=self.completed_transition_count,
        )

    def _process(
        self,
        step: EnvStep,
        *,
        decide: DecisionProvider[AuxT],
        bootstrap: BootstrapProvider,
    ) -> RolloutCollection:
        ended = step.terminated | step.truncated
        terminal_indices = np.flatnonzero(ended)
        decision_indices = np.flatnonzero(~ended)
        self._validate_event_ids(step, terminal_indices, decision_indices)
        raw_decisions = tuple(
            observation[decision_indices] for observation in step.observations
        )
        if len(decision_indices) > 0:
            decisions = decide(raw_decisions)
            if decisions.actions.shape[0] != len(decision_indices):
                raise ValueError("decision provider returned a wrong batch size")
            if self._action_size is None:
                self._action_size = decisions.actions.shape[1]
            elif decisions.actions.shape[1] != self._action_size:
                raise ValueError("decision provider changed action size")
        else:
            action_size = 0 if self._action_size is None else self._action_size
            decisions = DecisionBatch(
                observations=tuple(
                    observation[decision_indices] for observation in step.observations
                ),
                actions=np.empty((0, action_size), dtype=np.float32),
                values=np.empty(0, dtype=np.float32),
                auxiliaries=(),
            )

        truncated_indices = np.flatnonzero(step.truncated)
        truncated_values: dict[int, float] = {}
        if len(truncated_indices) > 0:
            terminal_observations = tuple(
                observation[truncated_indices] for observation in step.observations
            )
            values = np.asarray(bootstrap(terminal_observations))
            if values.dtype != np.float32 or values.shape != (len(truncated_indices),):
                raise TypeError("bootstrap values must be a one-dimensional float32 array")
            if not np.isfinite(values).all():
                raise ValueError("bootstrap values must contain only finite values")
            truncated_values = {
                int(index): float(value)
                for index, value in zip(truncated_indices, values)
            }

        for index in terminal_indices:
            agent_id = int(step.agent_ids[index])
            pending = self._pending.pop(agent_id, None)
            if pending is None:
                raise RuntimeError(
                    f"terminal event has no pending action for Agent ID {agent_id}"
                )
            self._append_transition(
                pending,
                reward=float(step.rewards[index]),
                next_value=truncated_values.get(int(index), 0.0),
                terminated=bool(step.terminated[index]),
                truncated=bool(step.truncated[index]),
            )

        decision_agent_ids: list[int] = []
        for output_index, event_index in enumerate(decision_indices):
            agent_id = int(step.agent_ids[event_index])
            decision_agent_ids.append(agent_id)
            pending = self._pending.pop(agent_id, None)
            if pending is not None:
                self._append_transition(
                    pending,
                    reward=float(step.rewards[event_index]),
                    next_value=float(decisions.values[output_index]),
                    terminated=False,
                    truncated=False,
                )
            self._pending[agent_id] = PendingDecision(
                agent_id=agent_id,
                observation=tuple(
                    np.array(stream[output_index], dtype=np.float32, copy=True)
                    for stream in decisions.observations
                ),
                action=np.array(
                    decisions.actions[output_index],
                    dtype=np.float32,
                    copy=True,
                ),
                value=float(decisions.values[output_index]),
                auxiliary=decisions.auxiliaries[output_index],
            )
        return RolloutCollection(
            actions=np.array(decisions.actions, dtype=np.float32, copy=True),
            decision_agent_ids=tuple(decision_agent_ids),
            completed_transition_count=self.completed_transition_count,
        )

    def _append_transition(
        self,
        pending: PendingDecision[AuxT],
        *,
        reward: float,
        next_value: float,
        terminated: bool,
        truncated: bool,
    ) -> None:
        self._trajectories.setdefault(pending.agent_id, []).append(
            CollectedTransition(
                agent_id=pending.agent_id,
                observation=pending.observation,
                action=pending.action,
                reward=reward,
                value=pending.value,
                next_value=next_value,
                terminated=terminated,
                truncated=truncated,
                auxiliary=pending.auxiliary,
            )
        )

    @staticmethod
    def _validate_event_ids(
        step: EnvStep,
        terminal_indices: np.ndarray,
        decision_indices: np.ndarray,
    ) -> None:
        positions: dict[int, list[int]] = {}
        for index, raw_agent_id in enumerate(step.agent_ids):
            positions.setdefault(int(raw_agent_id), []).append(index)
        terminal_set = set(int(index) for index in terminal_indices)
        decision_set = set(int(index) for index in decision_indices)
        for agent_id, indices in positions.items():
            if len(indices) == 1:
                continue
            valid_overlap = (
                len(indices) == 2
                and sum(index in terminal_set for index in indices) == 1
                and sum(index in decision_set for index in indices) == 1
            )
            if not valid_overlap:
                raise RuntimeError(f"invalid duplicate rows for Agent ID {agent_id}")
