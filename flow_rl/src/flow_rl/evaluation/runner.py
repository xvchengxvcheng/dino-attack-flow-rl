from __future__ import annotations

from collections.abc import Callable
from dataclasses import dataclass
from typing import Any, Protocol

import numpy as np

from flow_rl.envs.types import EnvStep
from flow_rl.tracking.episodes import EpisodeSummary, EpisodeTracker


class EvaluationAdapter(Protocol):
    pending_agent_ids: np.ndarray

    def __enter__(self) -> "EvaluationAdapter": ...

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None: ...

    def reset(self) -> EnvStep: ...

    def step(self, actions: np.ndarray) -> EnvStep: ...


Policy = Callable[[tuple[np.ndarray, ...], np.ndarray], np.ndarray]


@dataclass(frozen=True)
class EvaluationResult:
    episodes: tuple[EpisodeSummary, ...]
    environment_steps: int

    @property
    def mean_return(self) -> float:
        return float(np.mean([summary.episode_return for summary in self.episodes]))


def evaluate_policy(
    *,
    adapter_factory: Callable[[int], EvaluationAdapter],
    policy: Policy,
    episodes: int,
    training_worker_id: int,
    evaluation_worker_id: int,
    max_environment_steps: int = 1_000_000,
) -> EvaluationResult:
    """Evaluate a policy on a worker isolated from the training environment."""
    if training_worker_id == evaluation_worker_id:
        raise ValueError("training and evaluation worker IDs must differ")
    if episodes <= 0:
        raise ValueError("episodes must be positive")
    if max_environment_steps <= 0:
        raise ValueError("max_environment_steps must be positive")

    tracker = EpisodeTracker()
    summaries: list[EpisodeSummary] = []
    environment_steps = 0
    idle_steps = 0
    with adapter_factory(evaluation_worker_id) as adapter:
        current_step = adapter.reset()
        while len(summaries) < episodes:
            pending_ids = np.asarray(adapter.pending_agent_ids, dtype=np.int64)
            decision_count = pending_ids.shape[0]
            if environment_steps + decision_count > max_environment_steps:
                raise RuntimeError("evaluation exceeded max_environment_steps")
            decision_observations = tuple(
                observation[:decision_count] for observation in current_step.observations
            )
            actions = policy(decision_observations, pending_ids.copy())
            current_step = adapter.step(actions)
            environment_steps += decision_count
            idle_steps = idle_steps + 1 if decision_count == 0 else 0
            if idle_steps > 1_000:
                raise RuntimeError("evaluation made no Agent decision progress")
            summaries.extend(tracker.record(current_step))
    return EvaluationResult(tuple(summaries[:episodes]), environment_steps)
