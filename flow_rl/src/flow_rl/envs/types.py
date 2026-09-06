from __future__ import annotations

from dataclasses import dataclass

import numpy as np


def _readonly_copy(array: np.ndarray, *, dtype: np.dtype | None = None) -> np.ndarray:
    owned = np.array(array, dtype=dtype, copy=True)
    owned.setflags(write=False)
    return owned


@dataclass(frozen=True)
class EnvStep:
    """One batch of decision and terminal events from a single behavior."""

    agent_ids: np.ndarray
    observations: tuple[np.ndarray, ...]
    rewards: np.ndarray
    terminated: np.ndarray
    truncated: np.ndarray

    def __post_init__(self) -> None:
        agent_ids = np.asarray(self.agent_ids)
        if agent_ids.ndim != 1 or not np.issubdtype(agent_ids.dtype, np.integer):
            raise TypeError("agent_ids must be a one-dimensional integer array")
        batch_size = agent_ids.shape[0]

        if not isinstance(self.observations, tuple) or not self.observations:
            raise TypeError("observations must be a non-empty tuple of arrays")
        observations: list[np.ndarray] = []
        for index, observation in enumerate(self.observations):
            array = np.asarray(observation)
            if array.ndim < 1 or array.shape[0] != batch_size:
                raise ValueError(
                    f"observations[{index}] batch dimension must equal agent_ids"
                )
            if array.dtype != np.float32:
                raise TypeError(f"observations[{index}] must have dtype float32")
            if not np.isfinite(array).all():
                raise ValueError(f"observations[{index}] must contain only finite values")
            observations.append(_readonly_copy(array))

        rewards = np.asarray(self.rewards)
        if rewards.dtype != np.float32:
            raise TypeError("rewards must have dtype float32")
        if rewards.shape != (batch_size,):
            raise ValueError("rewards batch dimension must equal agent_ids")
        if not np.isfinite(rewards).all():
            raise ValueError("rewards must contain only finite values")

        flags: dict[str, np.ndarray] = {}
        for name, source in (
            ("terminated", self.terminated),
            ("truncated", self.truncated),
        ):
            flag = np.asarray(source)
            if flag.dtype != np.bool_:
                raise TypeError(f"{name} must have dtype bool")
            if flag.shape != (batch_size,):
                raise ValueError(f"{name} batch dimension must equal agent_ids")
            flags[name] = flag
        if np.logical_and(flags["terminated"], flags["truncated"]).any():
            raise ValueError("an event cannot be both terminated and truncated")

        object.__setattr__(self, "agent_ids", _readonly_copy(agent_ids, dtype=np.int64))
        object.__setattr__(self, "observations", tuple(observations))
        object.__setattr__(self, "rewards", _readonly_copy(rewards))
        object.__setattr__(self, "terminated", _readonly_copy(flags["terminated"]))
        object.__setattr__(self, "truncated", _readonly_copy(flags["truncated"]))

