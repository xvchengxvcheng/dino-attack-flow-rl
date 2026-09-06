from __future__ import annotations

import csv
import importlib

import pytest
import yaml


def _tracking_modules():
    try:
        episodes = importlib.import_module("flow_rl.tracking.episodes")
        run = importlib.import_module("flow_rl.tracking.run")
    except ModuleNotFoundError as exc:
        pytest.fail(f"run tracking module is missing: {exc}")
    return episodes, run


def test_run_logger_writes_parseable_episode_csv_config_and_tensorboard(tmp_path) -> None:
    episodes, run = _tracking_modules()
    summary = episodes.EpisodeSummary(
        agent_id=11,
        episode_index=2,
        episode_return=7.25,
        episode_length=13,
        terminated=False,
        truncated=True,
    )

    with run.RunLogger(tmp_path, {"seed": 3, "nested": {"worker_id": 17}}) as logger:
        logger.log_episode(summary, environment_steps=123)

    with (tmp_path / "episodes.csv").open(newline="", encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))
    assert rows == [
        {
            "environment_steps": "123",
            "agent_id": "11",
            "episode_index": "2",
            "episode_return": "7.25",
            "episode_length": "13",
            "terminated": "False",
            "truncated": "True",
        }
    ]
    with (tmp_path / "config.yaml").open(encoding="utf-8") as handle:
        assert yaml.safe_load(handle) == {"seed": 3, "nested": {"worker_id": 17}}
    assert list((tmp_path / "tensorboard").glob("events.out.tfevents.*"))


def test_run_logger_rejects_a_nonempty_run_directory_without_modifying_it(
    tmp_path,
) -> None:
    _, run = _tracking_modules()
    sentinel = tmp_path / "existing-evidence.txt"
    sentinel.write_text("keep me", encoding="utf-8")

    with pytest.raises(FileExistsError, match="not empty"):
        run.RunLogger(tmp_path, {"seed": 99})

    assert sentinel.read_text(encoding="utf-8") == "keep me"
    assert sorted(path.name for path in tmp_path.iterdir()) == [sentinel.name]


def test_run_logger_writes_parseable_update_metrics_csv(tmp_path) -> None:
    _, run = _tracking_modules()
    metrics = {
        "policy_loss": -0.25,
        "value_loss": 1.5,
        "entropy": 0.75,
    }

    with run.RunLogger(tmp_path, {"seed": 4}) as logger:
        logger.log_update(
            metrics,
            environment_steps=12000,
            update_index=3,
        )

    with (tmp_path / "updates.csv").open(newline="", encoding="utf-8") as handle:
        assert list(csv.DictReader(handle)) == [
            {
                "environment_steps": "12000",
                "update_index": "3",
                "policy_loss": "-0.25",
                "value_loss": "1.5",
                "entropy": "0.75",
            }
        ]
