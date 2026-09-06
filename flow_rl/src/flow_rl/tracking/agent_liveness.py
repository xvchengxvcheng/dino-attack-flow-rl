from __future__ import annotations

from collections.abc import Iterable

from flow_rl.envs.types import EnvStep


class AgentLivenessTracker:
    """Detects duplicate, unexpected, or persistently missing active Agent IDs."""

    def __init__(
        self,
        initial_agent_ids: Iterable[int],
        *,
        max_absence_steps: int,
        allow_new_agent_ids: bool = False,
    ) -> None:
        initial = tuple(int(agent_id) for agent_id in initial_agent_ids)
        if not initial:
            raise ValueError("at least one initial Agent ID is required")
        if len(initial) != len(set(initial)):
            raise ValueError("initial Agent IDs must be unique")
        if max_absence_steps < 0:
            raise ValueError("max_absence_steps cannot be negative")
        self._last_seen = {agent_id: 0 for agent_id in initial}
        self._max_absence_steps = max_absence_steps
        self._allow_new_agent_ids = allow_new_agent_ids
        self._last_unity_step = 0
        self._maximum_absence_steps_observed = 0

    @property
    def known_agent_ids(self) -> tuple[int, ...]:
        return tuple(sorted(self._last_seen))

    @property
    def all_known_seen_on_last_step(self) -> bool:
        return all(
            last_seen == self._last_unity_step
            for last_seen in self._last_seen.values()
        )

    @property
    def maximum_absence_steps_observed(self) -> int:
        return self._maximum_absence_steps_observed

    def observe(self, step: EnvStep, *, unity_step: int) -> None:
        if unity_step <= self._last_unity_step:
            raise ValueError("unity_step must increase monotonically")
        event_ids = tuple(int(agent_id) for agent_id in step.agent_ids)
        ended = step.terminated | step.truncated
        positions: dict[int, list[int]] = {}
        for index, agent_id in enumerate(event_ids):
            positions.setdefault(agent_id, []).append(index)
        invalid_duplicates = []
        for agent_id, indices in positions.items():
            if len(indices) == 1:
                continue
            valid_episode_boundary_overlap = (
                len(indices) == 2
                and sum(bool(ended[index]) for index in indices) == 1
            )
            if not valid_episode_boundary_overlap:
                invalid_duplicates.append(agent_id)
        if invalid_duplicates:
            raise RuntimeError(
                "Unity returned duplicate rows without one terminal and one "
                f"new decision for Agent IDs: {sorted(invalid_duplicates)}"
            )
        unexpected = sorted(set(event_ids) - set(self._last_seen))
        if unexpected and not self._allow_new_agent_ids:
            raise RuntimeError(f"Unity returned unexpected Agent IDs: {unexpected}")
        for agent_id in unexpected:
            self._last_seen[agent_id] = unity_step
        for agent_id in event_ids:
            self._last_seen[agent_id] = unity_step
        if self._allow_new_agent_ids:
            terminal_only_ids = {
                agent_id
                for agent_id, indices in positions.items()
                if all(bool(ended[index]) for index in indices)
            }
            for agent_id in terminal_only_ids:
                self._last_seen.pop(agent_id, None)
        maximum_absence = max(
            (unity_step - last_seen for last_seen in self._last_seen.values()),
            default=0,
        )
        self._maximum_absence_steps_observed = max(
            self._maximum_absence_steps_observed,
            maximum_absence,
        )
        stale = sorted(
            agent_id
            for agent_id, last_seen in self._last_seen.items()
            if unity_step - last_seen > self._max_absence_steps
        )
        if stale:
            raise RuntimeError(
                f"Agent IDs were not observed within {self._max_absence_steps} "
                f"Unity steps: {stale}"
            )
        self._last_unity_step = unity_step

    def finalize(self, *, observed_agent_ids: Iterable[int]) -> None:
        observed = {int(agent_id) for agent_id in observed_agent_ids}
        missing = sorted(set(self._last_seen) - observed)
        if missing:
            raise RuntimeError(
                "Agent IDs were not all observed during the final drain: "
                f"{missing}"
            )
