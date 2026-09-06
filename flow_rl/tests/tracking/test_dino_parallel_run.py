from __future__ import annotations

import csv
import importlib

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
