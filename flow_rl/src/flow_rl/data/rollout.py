from __future__ import annotations

from collections.abc import Mapping
from dataclasses import dataclass
from types import MappingProxyType

import numpy as np


def _readonly_float32(array: np.ndarray, name: str) -> np.ndarray:
    source = np.asarray(array)
    if source.dtype != np.float32:
        raise TypeError(f"{name} must have dtype float32")
    if not np.isfinite(source).all():
        raise ValueError(f"{name} must contain only finite values")
    owned = source.copy()
    owned.setflags(write=False)
    return owned


@dataclass(frozen=True)
class Transition:
    agent_id: int
    observation: tuple[np.ndarray, ...]
    action: np.ndarray
    reward: float
    value: float
    next_value: float
    terminated: bool
    truncated: bool

    def __post_init__(self) -> None:
        if isinstance(self.agent_id, bool) or not isinstance(
            self.agent_id, (int, np.integer)
        ):
            raise TypeError("agent_id must be an integer")
        if not isinstance(self.observation, tuple) or not self.observation:
            raise TypeError("observation must be a non-empty tuple")
        observations = tuple(
            _readonly_float32(array, f"observation[{index}]")
            for index, array in enumerate(self.observation)
        )
        action = _readonly_float32(self.action, "action")
        scalars = (self.reward, self.value, self.next_value)
        if not np.isfinite(np.asarray(scalars, dtype=np.float64)).all():
            raise ValueError("reward and values must be finite")
        if not isinstance(self.terminated, (bool, np.bool_)) or not isinstance(
            self.truncated, (bool, np.bool_)
        ):
            raise TypeError("terminated and truncated must be booleans")
        if self.terminated and self.truncated:
            raise ValueError("a transition cannot be both terminated and truncated")
        object.__setattr__(self, "agent_id", int(self.agent_id))
        object.__setattr__(self, "observation", observations)
        object.__setattr__(self, "action", action)
        object.__setattr__(self, "reward", float(self.reward))
        object.__setattr__(self, "value", float(self.value))
        object.__setattr__(self, "next_value", float(self.next_value))
        object.__setattr__(self, "terminated", bool(self.terminated))
        object.__setattr__(self, "truncated", bool(self.truncated))


class AgentRolloutBuffer:
    """Stores chronological trajectories independently for each Agent ID."""

    def __init__(self) -> None:
        self._trajectories: dict[int, list[Transition]] = {}

    def append(self, transition: Transition) -> None:
        if not isinstance(transition, Transition):
            raise TypeError("transition must be a Transition")
        self._trajectories.setdefault(transition.agent_id, []).append(transition)

    def trajectories(self) -> Mapping[int, tuple[Transition, ...]]:
        snapshot = {
            agent_id: tuple(transitions)
            for agent_id, transitions in self._trajectories.items()
        }
        return MappingProxyType(snapshot)

    def clear(self) -> None:
        self._trajectories.clear()

