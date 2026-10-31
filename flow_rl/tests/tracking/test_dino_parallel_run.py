from __future__ import annotations

import csv
import importlib
from collections import Counter
from dataclasses import replace

import pytest
from tensorboard.backend.event_processing.event_accumulator import EventAccumulator

from flow_rl.training.dino_parallel_ppo import (
    DinoParallelEpisodeSummary,
    DinoParallelUpdateSummary,
)


def _reporter_class():
    try:
        module = importlib.import_module("flow_rl.tracking.dino_parallel_run")
    except ModuleNotFoundError as exc:
        pytest.fail(f"Dino parallel run reporter is missing: {exc}")
    return module.DinoParallelRunReporter


def test_reporter_flushes_update_episode_and_tensorboard_before_close(tmp_path) -> None:
    reporter_class = _reporter_class()
    update = DinoParallelUpdateSummary(
        policy_version=2,
        transition_count=8,
        environment_contributions=((0, 3), (1, 5)),
        transitions_by_map=(("map1", 3), ("map2", 5)),
        episode_starts_by_map=(("map1", 1),),
        natural_terminals_by_map=(("map1", 1),),
        truncated_terminals_by_map=(),
        metrics=(("policy_loss", -0.25), ("value_loss", 1.5)),
        response_wait_mean_seconds=0.012,
        response_wait_max_seconds=0.075,
        response_wait_count=8,
        microbatch_wait_seconds=0.004,
        optimizer_update_seconds=0.5,
        completed_episodes=(
            DinoParallelEpisodeSummary(
                environment_id=0,
                process_generation=1,
                map_name="map1",
                agent_id=17,
                episode_index=4,
                episode_return=11.25,
                episode_length=9,
                terminated=True,
                truncated=False,
                terminal_reward=10.5,
                success=True,
            ),
        ),
    )

    with reporter_class(
        tmp_path,
        environment_ids=(0, 1),
        map_names=("map1", "map2"),
    ) as reporter:
        reporter.log_update(
            update,
            environment_steps=8192,
            update_index=2,
            wall_clock_seconds=12.0,
            steps_per_second=682.5,
        )

        with (tmp_path / "updates.csv").open(
            newline="", encoding="utf-8"
        ) as handle:
            rows = list(csv.DictReader(handle))
        assert rows == [
            {
                "environment_steps": "8192",
                "update_index": "2",
                "policy_version": "2",
                "wall_clock_seconds": "12.0",
                "steps_per_second": "682.5",
                "transition_count": "8",
                "timing/response_wait_mean_seconds": "0.012",
                "timing/response_wait_max_seconds": "0.075",
                "timing/response_wait_count": "8",
                "timing/microbatch_wait_seconds": "0.004",
                "timing/optimizer_update_seconds": "0.5",
                "worker/0/transitions": "3",
                "worker/1/transitions": "5",
                "map/map1/transitions": "3",
                "map/map1/episode_starts": "1",
                "map/map1/natural_terminals": "1",
                "map/map1/truncated_terminals": "0",
                "map/map2/transitions": "5",
                "map/map2/episode_starts": "0",
                "map/map2/natural_terminals": "0",
                "map/map2/truncated_terminals": "0",
                "optimization/policy_loss": "-0.25",
                "optimization/value_loss": "1.5",
            }
        ]
        with (tmp_path / "episodes.csv").open(
            newline="", encoding="utf-8"
        ) as handle:
            episode_rows = list(csv.DictReader(handle))
        assert episode_rows == [
            {
                "environment_steps": "8192",
                "update_index": "2",
                "environment_id": "0",
                "process_generation": "1",
                "map_name": "map1",
                "agent_id": "17",
                "episode_index": "4",
                "episode_return": "11.25",
                "episode_length": "9",
                "terminated": "True",
                "truncated": "False",
                "terminal_reward": "10.5",
                "success": "True",
            }
        ]
        events = EventAccumulator(str(tmp_path / "tensorboard")).Reload()
        assert events.Scalars("optimization/policy_loss")[-1].value == pytest.approx(
            -0.25
        )
        assert events.Scalars("episode/success")[-1].value == pytest.approx(1.0)
        assert events.Scalars("episode/map1/return")[-1].value == pytest.approx(
            11.25
        )


def _episode(index, *, map_name="map1", success=False, truncated=False):
    return DinoParallelEpisodeSummary(
        environment_id=0, process_generation=0, map_name=map_name,
        agent_id=17, episode_index=index,
        episode_return=10.0 if success else -1.0, episode_length=10,
        terminated=not truncated, truncated=truncated,
        terminal_reward=10.0 if success else -1.0, success=success,
    )


def _update(episodes):
    return DinoParallelUpdateSummary(
        policy_version=0, transition_count=8,
        environment_contributions=((0, 8),),
        transitions_by_map=(("map1", 4), ("map2", 4)),
        episode_starts_by_map=(),
        natural_terminals_by_map=tuple(Counter(
            e.map_name for e in episodes if e.terminated
        ).items()),
        truncated_terminals_by_map=tuple(Counter(
            e.map_name for e in episodes if e.truncated
        ).items()),
        metrics=(("loss", 0.1),), completed_episodes=tuple(episodes),
    )


def _log(reporter, update, index=1):
    reporter.log_update(
        update, environment_steps=index * 8, update_index=index,
        wall_clock_seconds=float(index), steps_per_second=8.0,
    )


def test_reporter_aggregates_natural_success_once_per_update_and_map(tmp_path):
    episodes = (
        _episode(0, success=True), _episode(1), _episode(2, truncated=True),
        _episode(3, map_name="map2", success=True),
    )
    with _reporter_class()(tmp_path, environment_ids=(0,), map_names=("map1", "map2")) as reporter:
        _log(reporter, _update(episodes))
        events = EventAccumulator(str(tmp_path / "tensorboard")).Reload()
        overall = events.Scalars("rollout/success_rate")
        assert len(overall) == 1
        assert overall[0].step == 8
        assert overall[0].value == pytest.approx(2 / 3)
        assert events.Scalars("rollout/map1/success_rate")[0].value == 0.5
        assert events.Scalars("rollout/map2/success_rate")[0].value == 1.0
        assert events.Scalars("rollout/natural_episodes")[0].value == 3
        assert events.Scalars("rollout/truncated_episodes")[0].value == 1
        assert events.Scalars("rollout/mean_return")[0].value == pytest.approx(19 / 3)
        assert events.Scalars("rollout/mean_length")[0].value == 10
        with (tmp_path / "episodes.csv").open(newline="", encoding="utf-8") as handle:
            assert len(list(csv.DictReader(handle))) == 4


def test_reporter_omits_undefined_rates_without_natural_episodes(tmp_path):
    with _reporter_class()(tmp_path, environment_ids=(0,), map_names=("map1", "map2")) as reporter:
        _log(reporter, _update(()))
        _log(reporter, _update((_episode(0, truncated=True),)), index=2)
        events = EventAccumulator(str(tmp_path / "tensorboard")).Reload()
        assert "rollout/success_rate" not in events.Tags()["scalars"]
        assert "rollout/map1/success_rate" not in events.Tags()["scalars"]
        assert "rollout/mean_return" not in events.Tags()["scalars"]
        assert [e.value for e in events.Scalars("rollout/natural_episodes")] == [0, 0]
        assert [e.value for e in events.Scalars("rollout/truncated_episodes")] == [0, 1]


def test_reporter_rejects_duplicate_episodes_but_keeps_worker_generations_separate(tmp_path):
    episode = _episode(0, success=True)
    with _reporter_class()(tmp_path, environment_ids=(0, 1), map_names=("map1", "map2")) as reporter:
        with pytest.raises(ValueError, match="duplicate episode"):
            _log(reporter, _update((episode, episode)))
        assert not (tmp_path / "updates.csv").exists()
        distinct = (
            episode,
            replace(episode, process_generation=1),
            replace(episode, environment_id=1),
            replace(episode, agent_id=18),
        )
        update = replace(
            _update(distinct), environment_contributions=((0, 6), (1, 2)),
        )
        _log(reporter, update)
        events = EventAccumulator(str(tmp_path / "tensorboard")).Reload()
        assert events.Scalars("rollout/natural_episodes")[0].value == 4
        assert events.Scalars("rollout/success_rate")[0].value == 1.0


@pytest.mark.parametrize("mutation", [
    {"episode_return": float("nan")}, {"terminal_reward": float("inf")},
    {"map_name": "unknown"}, {"environment_id": 99},
    {"terminated": False, "truncated": False},
    {"terminated": True, "truncated": True},
    {"episode_length": -1}, {"success": True, "terminated": False, "truncated": True},
])
def test_reporter_rejects_bad_episodes_before_persisting_any_update(tmp_path, mutation):
    with _reporter_class()(tmp_path, environment_ids=(0,), map_names=("map1", "map2")) as reporter:
        invalid = replace(_episode(0), **mutation)
        with pytest.raises(ValueError, match="episode"):
            _log(reporter, _update((invalid,)))
        assert not (tmp_path / "updates.csv").exists()
        with (tmp_path / "episodes.csv").open(newline="", encoding="utf-8") as handle:
            assert list(csv.DictReader(handle)) == []
        assert EventAccumulator(str(tmp_path / "tensorboard")).Reload().Tags()["scalars"] == []
        # Rejection must not poison the reporter for the next valid update.
        _log(reporter, _update((_episode(0, success=True),)))
