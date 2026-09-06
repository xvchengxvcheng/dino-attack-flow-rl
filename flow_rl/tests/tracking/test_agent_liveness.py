from __future__ import annotations

import importlib

import numpy as np
import pytest

from flow_rl.envs.types import EnvStep


def _tracker_type():
    try:
        module = importlib.import_module("flow_rl.tracking.agent_liveness")
    except ModuleNotFoundError as exc:
        pytest.fail(f"Agent liveness tracker is missing: {exc}")
    return module.AgentLivenessTracker


def _step(
    agent_ids: list[int],
    terminated: list[bool] | None = None,
) -> EnvStep:
    count = len(agent_ids)
    return EnvStep(
        agent_ids=np.asarray(agent_ids, dtype=np.int64),
        observations=(np.asarray(agent_ids, dtype=np.float32).reshape(count, 1),),
        rewards=np.zeros(count, dtype=np.float32),
        terminated=np.asarray(terminated or [False] * count, dtype=bool),
        truncated=np.zeros(count, dtype=bool),
    )


def test_terminal_only_batch_does_not_mark_other_agents_lost() -> None:
    AgentLivenessTracker = _tracker_type()
    tracker = AgentLivenessTracker([0, 1, 2], max_absence_steps=2)

    tracker.observe(_step([2], [True]), unity_step=1)
    tracker.observe(_step([0, 1, 2]), unity_step=2)

    assert tracker.known_agent_ids == (0, 1, 2)


def test_agent_missing_beyond_liveness_window_is_rejected() -> None:
    AgentLivenessTracker = _tracker_type()
    tracker = AgentLivenessTracker([0, 1, 2], max_absence_steps=2)

    tracker.observe(_step([0, 1]), unity_step=1)
    tracker.observe(_step([0, 1]), unity_step=2)
    with pytest.raises(RuntimeError, match="not observed"):
        tracker.observe(_step([0, 1]), unity_step=3)


def test_unexpected_agent_id_is_rejected_in_strict_mode() -> None:
    AgentLivenessTracker = _tracker_type()
    tracker = AgentLivenessTracker([0, 1], max_absence_steps=2)

    with pytest.raises(RuntimeError, match="unexpected Agent IDs"):
        tracker.observe(_step([0, 9]), unity_step=1)


def test_same_agent_terminal_and_new_decision_in_one_batch_is_valid() -> None:
    AgentLivenessTracker = _tracker_type()
    tracker = AgentLivenessTracker([0, 1, 2], max_absence_steps=2)

    tracker.observe(_step([2, 2], [False, True]), unity_step=1)

    assert tracker.known_agent_ids == (0, 1, 2)


def test_duplicate_decision_rows_for_same_agent_are_rejected() -> None:
    AgentLivenessTracker = _tracker_type()
    tracker = AgentLivenessTracker([0, 1], max_absence_steps=2)

    with pytest.raises(RuntimeError, match="duplicate"):
        tracker.observe(_step([0, 0]), unity_step=1)


def test_finalize_requires_every_agent_to_be_observed_during_the_final_drain() -> None:
    AgentLivenessTracker = _tracker_type()
    tracker = AgentLivenessTracker([0, 1], max_absence_steps=2)
    tracker.observe(_step([0]), unity_step=1)

    with pytest.raises(RuntimeError, match="final drain"):
        tracker.finalize(observed_agent_ids=[0])

    tracker.observe(_step([0, 1]), unity_step=2)
    tracker.finalize(observed_agent_ids=[0, 1])
    assert tracker.all_known_seen_on_last_step is True
    assert tracker.maximum_absence_steps_observed == 1


def test_dynamic_agent_set_retires_terminal_id_and_accepts_replacement() -> None:
    AgentLivenessTracker = _tracker_type()
    tracker = AgentLivenessTracker(
        [0],
        max_absence_steps=2,
        allow_new_agent_ids=True,
    )

    tracker.observe(_step([0], [True]), unity_step=1)
    assert tracker.known_agent_ids == ()

    tracker.observe(_step([]), unity_step=2)
    tracker.observe(_step([]), unity_step=3)
    tracker.observe(_step([1]), unity_step=4)

    assert tracker.known_agent_ids == (1,)
    tracker.finalize(observed_agent_ids=[1])


def test_dynamic_agent_set_keeps_same_id_when_terminal_overlaps_new_decision() -> None:
    AgentLivenessTracker = _tracker_type()
    tracker = AgentLivenessTracker(
        [0],
        max_absence_steps=2,
        allow_new_agent_ids=True,
    )

    tracker.observe(_step([0, 0], [True, False]), unity_step=1)

    assert tracker.known_agent_ids == (0,)
