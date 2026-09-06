from __future__ import annotations

from pathlib import Path

import pytest
import yaml

from flow_rl.training.config import PPOTrainingConfig


def _mapping(build_name: str = "3DBall.exe") -> dict[str, object]:
    return {
        "build_path": build_name,
        "run_directory": "run",
        "total_environment_steps": 500000,
        "rollout_size": 12000,
        "batch_size": 64,
        "epochs": 3,
        "gamma": 0.99,
        "gae_lambda": 0.99,
        "learning_rate": 0.0003,
        "final_learning_rate": 1e-10,
        "clip_range": 0.2,
        "final_clip_range": 0.1,
        "entropy_coefficient": 0.001,
        "final_entropy_coefficient": 1e-5,
        "value_coefficient": 0.5,
        "max_gradient_norm": 0.5,
        "hidden_sizes": [128, 128],
        "checkpoint_interval": 100000,
        "time_scale": 20.0,
        "device": "cpu",
        "worker_id": 80,
        "evaluation_worker_id": 90,
        "seed": 0,
        "timeout_wait": 60,
        "max_agent_absence_steps": 10,
        "behavior_name": None,
        "resume_checkpoint": None,
    }


def _write_config(tmp_path: Path, mapping: dict[str, object]) -> Path:
    path = tmp_path / "config.yaml"
    path.write_text(yaml.safe_dump(mapping), encoding="utf-8")
    return path


def test_config_resolves_paths_and_round_trips_all_values(tmp_path: Path) -> None:
    build = tmp_path / "3DBall.exe"
    build.write_bytes(b"build")
    path = _write_config(tmp_path, _mapping())

    config = PPOTrainingConfig.from_yaml(path)

    assert config.build_path == build.resolve()
    assert config.run_directory == (tmp_path / "run").resolve()
    assert config.hidden_sizes == (128, 128)
    assert config.total_environment_steps == 500000
    assert config.resume_checkpoint is None
    assert config.to_dict()["build_path"] == str(build.resolve())
    assert config.to_dict()["hidden_sizes"] == [128, 128]


@pytest.mark.parametrize(
    ("updates", "message"),
    [
        ({"total_environment_steps": 0}, "positive"),
        ({"rollout_size": 32, "batch_size": 64}, "rollout_size"),
        ({"gamma": 1.1}, "gamma"),
        ({"clip_range": -0.1}, "clip"),
        ({"worker_id": 80, "evaluation_worker_id": 80}, "differ"),
        ({"hidden_sizes": []}, "hidden"),
        ({"device": "tpu"}, "device"),
    ],
)
def test_config_rejects_invalid_values(
    tmp_path: Path,
    updates: dict[str, object],
    message: str,
) -> None:
    (tmp_path / "3DBall.exe").write_bytes(b"build")
    mapping = {**_mapping(), **updates}

    with pytest.raises((TypeError, ValueError), match=message):
        PPOTrainingConfig.from_yaml(_write_config(tmp_path, mapping))


def test_config_rejects_missing_build_and_nonempty_run_before_training(
    tmp_path: Path,
) -> None:
    with pytest.raises(FileNotFoundError, match="build"):
        PPOTrainingConfig.from_yaml(_write_config(tmp_path, _mapping("missing.exe")))

    (tmp_path / "3DBall.exe").write_bytes(b"build")
    run_directory = tmp_path / "run"
    run_directory.mkdir()
    (run_directory / "evidence.txt").write_text("keep", encoding="utf-8")
    with pytest.raises(FileExistsError, match="not empty"):
        PPOTrainingConfig.from_yaml(_write_config(tmp_path, _mapping()))
