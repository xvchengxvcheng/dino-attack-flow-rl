from __future__ import annotations

from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from typing import Generic, TypeVar

import numpy as np

from flow_rl.envs.parallel_unity import ParallelEnvEvent


AuxT = TypeVar("AuxT")


def _readonly(array: np.ndarray, *, dtype: np.dtype | None = None) -> np.ndarray:
    result = np.array(array, dtype=dtype, copy=True)
    result.setflags(write=False)
    return result


@dataclass(frozen=True, order=True)
class AgentIdentity:
    environment_id: int
    process_generation: int
    agent_id: int

    def __post_init__(self) -> None:
        if self.environment_id < 0 or self.process_generation < 0:
            raise ValueError("environment and generation IDs cannot be negative")


@dataclass(frozen=True, order=True)
class BootstrapTarget:
    identity: AgentIdentity
    trajectory_id: int


@dataclass(frozen=True)
class VersionedActionInfo(Generic[AuxT]):
    environment_id: int
    process_generation: int
    policy_version: int
    agent_ids: np.ndarray
    observations: tuple[np.ndarray, ...]
    actions: np.ndarray
    values: np.ndarray
    auxiliaries: tuple[AuxT, ...]

    def __post_init__(self) -> None:
        if self.environment_id < 0 or self.process_generation < 0:
            raise ValueError("environment and generation IDs cannot be negative")
        if self.policy_version < 0:
            raise ValueError("policy_version cannot be negative")
        agent_ids = np.asarray(self.agent_ids)
        actions = np.asarray(self.actions)
        values = np.asarray(self.values)
        if agent_ids.ndim != 1 or not np.issubdtype(agent_ids.dtype, np.integer):
            raise TypeError("agent_ids must be a one-dimensional integer array")
        batch_size = len(agent_ids)
        if len(set(int(value) for value in agent_ids)) != batch_size:
            raise ValueError("action info Agent IDs must be unique")
        if actions.dtype != np.float32 or actions.ndim != 2:
            raise TypeError("actions must be a two-dimensional float32 array")
        if actions.shape[0] != batch_size:
            raise ValueError("actions must share the Agent ID batch size")
        if values.dtype != np.float32 or values.shape != (batch_size,):
            raise TypeError("values must be a one-dimensional float32 array")
        if len(self.auxiliaries) != batch_size:
            raise ValueError("auxiliaries must share the Agent ID batch size")
        if not isinstance(self.observations, tuple) or not self.observations:
            raise TypeError("observations must be a non-empty tuple")
        observations: list[np.ndarray] = []
        for index, observation in enumerate(self.observations):
            array = np.asarray(observation)
            if array.dtype != np.float32 or array.shape[0] != batch_size:
                raise TypeError(
                    f"observations[{index}] must be float32 with matching batch"
                )
            if not np.isfinite(array).all():
                raise ValueError("observations must contain only finite values")
            observations.append(_readonly(array))
        if not np.isfinite(actions).all() or not np.isfinite(values).all():
            raise ValueError("actions and values must contain only finite values")
        object.__setattr__(self, "agent_ids", _readonly(agent_ids, dtype=np.int64))
        object.__setattr__(self, "observations", tuple(observations))
        object.__setattr__(self, "actions", _readonly(actions))
        object.__setattr__(self, "values", _readonly(values))


@dataclass(frozen=True)
class VersionedTransition(Generic[AuxT]):
    identity: AgentIdentity
    trajectory_id: int
    policy_version: int
    observation: tuple[np.ndarray, ...]
    action: np.ndarray
    reward: float
    value: float
    next_observation: tuple[np.ndarray, ...]
    next_value: float
    terminated: bool
    truncated: bool
    auxiliary: AuxT


@dataclass(frozen=True)
class InfrastructureDiagnostic:
    environment_id: int
    process_generation: int
    policy_version: int
    infrastructure_truncated: bool
    final_observation_available: bool
    discarded_transitions: int
    error_type: str
    error_message: str


@dataclass(frozen=True)
class VersionedBatch(Generic[AuxT]):
    policy_version: int
    transitions: tuple[VersionedTransition[AuxT], ...]
    diagnostics: tuple[InfrastructureDiagnostic, ...]

    def trajectories(
        self,
    ) -> Mapping[BootstrapTarget, tuple[VersionedTransition[AuxT], ...]]:
        grouped: dict[BootstrapTarget, list[VersionedTransition[AuxT]]] = {}
        for transition in self.transitions:
            target = BootstrapTarget(transition.identity, transition.trajectory_id)
            grouped.setdefault(target, []).append(transition)
        return {target: tuple(items) for target, items in grouped.items()}


@dataclass
class _Transition(Generic[AuxT]):
    identity: AgentIdentity
    trajectory_id: int
    policy_version: int
    observation: tuple[np.ndarray, ...]
    action: np.ndarray
    reward: float
    value: float
    next_observation: tuple[np.ndarray, ...]
    next_value: float | None
    terminated: bool
    truncated: bool
    auxiliary: AuxT


@dataclass
class _Fragment(Generic[AuxT]):
    identity: AgentIdentity
    trajectory_id: int
    transitions: list[_Transition[AuxT]]


class VersionedCollector(Generic[AuxT]):
    """Collect one drained rollout from exactly one frozen policy snapshot."""

    def __init__(self, *, target_transitions: int) -> None:
        if target_transitions <= 0:
            raise ValueError("target_transitions must be positive")
        self.target_transitions = int(target_transitions)
        self._policy_version: int | None = None
        self._fragments: dict[AgentIdentity, _Fragment[AuxT]] = {}
        self._completed: list[_Fragment[AuxT]] = []
        self._diagnostics: list[InfrastructureDiagnostic] = []
        self._next_trajectory_id = 0
        self._sealed = False
        self._batch_built = False
        self._optimizer_updated = True
        self._target_reached = False

    @property
    def policy_version(self) -> int | None:
        return self._policy_version

    @property
    def accepted_transition_count(self) -> int:
        return sum(len(fragment.transitions) for fragment in self._all_fragments())

    @property
    def accepting_submissions(self) -> bool:
        return (
            self._policy_version is not None
            and not self._sealed
            and not self._target_reached
        )

    @property
    def bootstrap_targets(self) -> tuple[BootstrapTarget, ...]:
        return tuple(
            BootstrapTarget(fragment.identity, fragment.trajectory_id)
            for fragment in self._all_fragments()
            if fragment.transitions and fragment.transitions[-1].next_value is None
        )

    @property
    def bootstrap_observations(
        self,
    ) -> Mapping[BootstrapTarget, tuple[np.ndarray, ...]]:
        return {
            target: tuple(
                np.array(observation, dtype=np.float32, copy=True)
                for observation in self._fragment_for(target).transitions[-1].next_observation
            )
            for target in self.bootstrap_targets
        }

    def begin(self, version: int) -> None:
        if version < 0:
            raise ValueError("policy version cannot be negative")
        if self._policy_version is not None:
            if not self._optimizer_updated:
                raise RuntimeError(
                    "cannot begin a new policy version before the optimizer update"
                )
            if version != self._policy_version + 1:
                raise ValueError(
                    f"next policy version must be {self._policy_version + 1}, got {version}"
                )
        self._policy_version = int(version)
        self._fragments.clear()
        self._completed.clear()
        self._diagnostics.clear()
        self._next_trajectory_id = 0
        self._sealed = False
        self._batch_built = False
        self._optimizer_updated = False
        self._target_reached = False

    def record(
        self,
        event: ParallelEnvEvent,
        action_info: VersionedActionInfo[AuxT],
    ) -> None:
        self._ensure_recording()
        assert self._policy_version is not None
        if action_info.policy_version != self._policy_version:
            raise ValueError(
                f"mixed policy version: expected {self._policy_version}, "
                f"got {action_info.policy_version}"
            )
        if (
            event.environment_id != action_info.environment_id
            or event.process_generation != action_info.process_generation
        ):
            raise ValueError("event and action info environment provenance differ")
        if event.error is not None:
            self._discard_infrastructure_fragment(event)
            return
        assert event.step is not None
        step = event.step
        if len(step.observations) != len(action_info.observations):
            raise ValueError("event and action info expose different observation streams")
        for action_index, raw_agent_id in enumerate(action_info.agent_ids):
            agent_id = int(raw_agent_id)
            identity = AgentIdentity(
                event.environment_id,
                event.process_generation,
                agent_id,
            )
            event_index = self._match_event_row(step, agent_id)
            fragment = self._fragments.get(identity)
            if fragment is None:
                fragment = self._new_fragment(identity)
                self._fragments[identity] = fragment
            elif fragment.transitions and fragment.transitions[-1].next_value is None:
                fragment.transitions[-1].next_value = float(action_info.values[action_index])
            transition = _Transition(
                identity=identity,
                trajectory_id=fragment.trajectory_id,
                policy_version=self._policy_version,
                observation=tuple(
                    np.array(stream[action_index], dtype=np.float32, copy=True)
                    for stream in action_info.observations
                ),
                action=np.array(
                    action_info.actions[action_index], dtype=np.float32, copy=True
                ),
                reward=float(step.rewards[event_index]),
                value=float(action_info.values[action_index]),
                next_observation=tuple(
                    np.array(stream[event_index], dtype=np.float32, copy=True)
                    for stream in step.observations
                ),
                next_value=(0.0 if bool(step.terminated[event_index]) else None),
                terminated=bool(step.terminated[event_index]),
                truncated=bool(step.truncated[event_index]),
                auxiliary=action_info.auxiliaries[action_index],
            )
            fragment.transitions.append(transition)
            if transition.terminated or transition.truncated:
                self._completed.append(fragment)
                del self._fragments[identity]
        if self.accepted_transition_count >= self.target_transitions:
            self._target_reached = True

    def seal_and_bootstrap(
        self,
        values: Mapping[AgentIdentity | BootstrapTarget, float] | Sequence[float],
    ) -> None:
        self._ensure_recording()
        targets = self.bootstrap_targets
        resolved = self._resolve_bootstrap_values(targets, values)
        for target, value in zip(targets, resolved):
            transition = self._fragment_for(target).transitions[-1]
            if transition.terminated:
                raise RuntimeError("natural terminals must not be bootstrapped")
            transition.next_value = value
        self._completed.extend(self._fragments.values())
        self._fragments.clear()
        self._sealed = True

    def build_batch(self) -> VersionedBatch[AuxT]:
        if not self._sealed:
            raise RuntimeError("seal_and_bootstrap() must run before build_batch()")
        assert self._policy_version is not None
        transitions = tuple(
            self._freeze_transition(transition)
            for fragment in self._completed
            for transition in fragment.transitions
        )
        if not transitions:
            raise RuntimeError("no accepted transitions are available")
        versions = {transition.policy_version for transition in transitions}
        if versions != {self._policy_version}:
            raise RuntimeError(f"mixed policy versions reached batch construction: {versions}")
        self._batch_built = True
        return VersionedBatch(
            policy_version=self._policy_version,
            transitions=transitions,
            diagnostics=tuple(self._diagnostics),
        )

    def complete_optimizer_update(self, version: int, *, succeeded: bool) -> None:
        if self._policy_version is None or version != self._policy_version:
            raise ValueError("optimizer update policy version does not match the batch")
        if not self._batch_built:
            raise RuntimeError("build_batch() must run before the optimizer update")
        if not succeeded:
            raise RuntimeError("optimizer update failed; policy version cannot advance")
        self._optimizer_updated = True

    def _discard_infrastructure_fragment(self, event: ParallelEnvEvent) -> None:
        assert event.error is not None
        matching = [
            identity
            for identity in self._fragments
            if identity.environment_id == event.environment_id
            and identity.process_generation == event.process_generation
        ]
        discarded = sum(
            len(self._fragments[identity].transitions) for identity in matching
        )
        for identity in matching:
            del self._fragments[identity]
        assert self._policy_version is not None
        self._diagnostics.append(
            InfrastructureDiagnostic(
                environment_id=event.environment_id,
                process_generation=event.process_generation,
                policy_version=self._policy_version,
                infrastructure_truncated=True,
                final_observation_available=False,
                discarded_transitions=discarded,
                error_type=type(event.error).__name__,
                error_message=str(event.error),
            )
        )

    @staticmethod
    def _match_event_row(step: object, agent_id: int) -> int:
        indices = np.flatnonzero(step.agent_ids == agent_id)
        if len(indices) == 0:
            raise RuntimeError(f"returned event is missing submitted Agent ID {agent_id}")
        terminal = [
            int(index)
            for index in indices
            if bool(step.terminated[index]) or bool(step.truncated[index])
        ]
        if len(terminal) == 1:
            return terminal[0]
        if len(indices) == 1:
            return int(indices[0])
        raise RuntimeError(f"returned event has ambiguous rows for Agent ID {agent_id}")

    def _new_fragment(self, identity: AgentIdentity) -> _Fragment[AuxT]:
        fragment = _Fragment(
            identity=identity,
            trajectory_id=self._next_trajectory_id,
            transitions=[],
        )
        self._next_trajectory_id += 1
        return fragment

    def _all_fragments(self) -> tuple[_Fragment[AuxT], ...]:
        return tuple(self._completed) + tuple(self._fragments.values())

    def _fragment_for(self, target: BootstrapTarget) -> _Fragment[AuxT]:
        for fragment in self._all_fragments():
            if (
                fragment.identity == target.identity
                and fragment.trajectory_id == target.trajectory_id
            ):
                return fragment
        raise KeyError(f"unknown bootstrap target {target}")

    @staticmethod
    def _resolve_bootstrap_values(
        targets: tuple[BootstrapTarget, ...],
        values: Mapping[AgentIdentity | BootstrapTarget, float] | Sequence[float],
    ) -> tuple[float, ...]:
        if isinstance(values, Mapping):
            resolved: list[float] = []
            for target in targets:
                if target in values:
                    raw = values[target]
                elif target.identity in values:
                    same_identity = [
                        item for item in targets if item.identity == target.identity
                    ]
                    if len(same_identity) != 1:
                        raise ValueError(
                            "ambiguous AgentIdentity bootstrap key; use BootstrapTarget"
                        )
                    raw = values[target.identity]
                else:
                    raise KeyError(f"missing bootstrap value for {target}")
                resolved.append(float(raw))
        else:
            if len(values) != len(targets):
                raise ValueError("bootstrap values must match bootstrap targets")
            resolved = [float(value) for value in values]
        if not np.isfinite(np.asarray(resolved, dtype=np.float64)).all():
            raise ValueError("bootstrap values must be finite")
        return tuple(resolved)

    @staticmethod
    def _freeze_transition(
        transition: _Transition[AuxT],
    ) -> VersionedTransition[AuxT]:
        if transition.next_value is None:
            raise RuntimeError("an unbootstrapped fragment reached batch construction")
        return VersionedTransition(
            identity=transition.identity,
            trajectory_id=transition.trajectory_id,
            policy_version=transition.policy_version,
            observation=tuple(_readonly(item) for item in transition.observation),
            action=_readonly(transition.action),
            reward=transition.reward,
            value=transition.value,
            next_observation=tuple(
                _readonly(item) for item in transition.next_observation
            ),
            next_value=float(transition.next_value),
            terminated=transition.terminated,
            truncated=transition.truncated,
            auxiliary=transition.auxiliary,
        )

    def _ensure_recording(self) -> None:
        if self._policy_version is None:
            raise RuntimeError("begin() must run before collection")
        if self._sealed:
            raise RuntimeError("the current policy version is already sealed")
