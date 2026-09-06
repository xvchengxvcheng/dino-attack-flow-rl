from __future__ import annotations

import importlib

import numpy as np
import pytest

from flow_rl.envs.types import EnvStep


def _episodes_module():
    try:
        return importlib.import_module("flow_rl.tracking.episodes")
    except ModuleNotFoundError as exc:
        pytest.fail(f"episode tracker module is missing: {exc}")


def _step(
    agent_ids: list[int],
    rewards: list[float],
    terminated: list[bool],
    truncated: list[bool],
) -> EnvStep:
    return EnvStep(
        agent_ids=np.asarray(agent_ids, dtype=np.int64),
        observations=(np.asarray(agent_ids, dtype=np.float32).reshape(-1, 1),),
        rewards=np.asarray(rewards, dtype=np.float32),
        terminated=np.asarray(terminated, dtype=bool),
        truncated=np.asarray(truncated, dtype=bool),
    )


def test_episode_tracker_reconstructs_reordered_and_disappearing_agents() -> None:
    episodes = _episodes_module()
    tracker = episodes.EpisodeTracker()

    assert tracker.record(_step([20, 10], [2.0, 1.0], [False, False], [False, False])) == ()
    assert tracker.record(_step([10], [1.5], [False], [False])) == ()
    summaries = tracker.record(
        _step([20, 10], [3.0, 2.0], [True, False], [False, True])
    )

    assert [(item.agent_id, item.episode_return, item.episode_length) for item in summaries] == [
        (20, 5.0, 2),
        (10, 4.5, 3),
    ]
    assert summaries[0].terminated is True and summaries[0].truncated is False
    assert summaries[1].terminated is False and summaries[1].truncated is True


def test_episode_tracker_starts_a_new_count_after_same_agent_returns() -> None:
    episodes = _episodes_module()
    tracker = episodes.EpisodeTracker()
    first = tracker.record(_step([7], [1.0], [True], [False]))
    assert tracker.record(_step([7], [0.0], [False], [False])) == ()
    second = tracker.record(_step([7], [2.0], [True], [False]))

    assert first[0].episode_index == 0
    assert second[0].episode_index == 1
    assert second[0].episode_return == 2.0
    assert second[0].episode_length == 1


def test_terminal_is_accounted_before_same_batch_new_decision() -> None:
    episodes = _episodes_module()
    tracker = episodes.EpisodeTracker()
    tracker.record(_step([2], [1.0], [False], [False]))

    ended = tracker.record(
        _step([2, 2], [0.0, 2.0], [False, True], [False, False])
    )
    next_episode = tracker.record(_step([2], [4.0], [True], [False]))

    assert ended[0].episode_index == 0
    assert ended[0].episode_return == 3.0
    assert ended[0].episode_length == 2
    assert next_episode[0].episode_index == 1
    assert next_episode[0].episode_return == 4.0
    assert next_episode[0].episode_length == 1


def test_delayed_post_terminal_decision_is_a_baseline_not_a_transition() -> None:
    episodes = _episodes_module()
    tracker = episodes.EpisodeTracker()
    tracker.record(_step([3], [1.0], [False], [False]))
    ended = tracker.record(_step([3], [2.0], [True], [False]))

    assert tracker.record(_step([3], [99.0], [False], [False])) == ()
    next_episode = tracker.record(_step([3], [4.0], [True], [False]))

    assert ended[0].episode_return == 3.0
    assert ended[0].episode_length == 2
    assert next_episode[0].episode_index == 1
    assert next_episode[0].episode_return == 4.0
    assert next_episode[0].episode_length == 1
