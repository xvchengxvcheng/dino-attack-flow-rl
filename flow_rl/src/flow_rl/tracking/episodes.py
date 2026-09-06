from __future__ import annotations

from dataclasses import dataclass

import numpy as np

from flow_rl.envs.types import EnvStep


@dataclass(frozen=True)
class EpisodeSummary:
    agent_id: int
    episode_index: int
    episode_return: float
    episode_length: int
    terminated: bool
    truncated: bool

    def __post_init__(self) -> None:
        if self.episode_index < 0:
            raise ValueError("episode_index cannot be negative")
        if self.episode_length <= 0:
            raise ValueError("episode_length must be positive")
        if not np.isfinite(self.episode_return):
            raise ValueError("episode_return must be finite")
        if self.terminated == self.truncated:
            raise ValueError("episode must be exactly one of terminated or truncated")


class EpisodeTracker:
    """Reconstructs episode returns and decision lengths by stable Agent ID."""

    def __init__(self) -> None:
        self._returns: dict[int, float] = {}
        self._lengths: dict[int, int] = {}
        self._episode_indices: dict[int, int] = {}
        self._awaiting_initial_decision: set[int] = set()

    def record(self, step: EnvStep) -> tuple[EpisodeSummary, ...]:
        summaries: list[EpisodeSummary] = []
        ended = step.terminated | step.truncated
        terminal_indices = [
            index for index in range(len(step.agent_ids)) if ended[index]
        ]
        decision_indices = [
            index for index in range(len(step.agent_ids)) if not ended[index]
        ]
        for index in terminal_indices:
            raw_agent_id = step.agent_ids[index]
            agent_id = int(raw_agent_id)
            self._returns[agent_id] = self._returns.get(agent_id, 0.0) + float(
                step.rewards[index]
            )
            self._lengths[agent_id] = self._lengths.get(agent_id, 0) + 1
            terminated = bool(step.terminated[index])
            truncated = bool(step.truncated[index])
            episode_index = self._episode_indices.get(agent_id, 0)
            summaries.append(
                EpisodeSummary(
                    agent_id=agent_id,
                    episode_index=episode_index,
                    episode_return=self._returns.pop(agent_id),
                    episode_length=self._lengths.pop(agent_id),
                    terminated=terminated,
                    truncated=truncated,
                )
            )
            self._episode_indices[agent_id] = episode_index + 1
            self._awaiting_initial_decision.add(agent_id)

        for index in decision_indices:
            agent_id = int(step.agent_ids[index])
            if agent_id in self._awaiting_initial_decision:
                self._awaiting_initial_decision.remove(agent_id)
                continue
            self._returns[agent_id] = self._returns.get(agent_id, 0.0) + float(
                step.rewards[index]
            )
            self._lengths[agent_id] = self._lengths.get(agent_id, 0) + 1
        return tuple(summaries)
