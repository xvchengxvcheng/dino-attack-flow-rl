from __future__ import annotations

from pathlib import Path

import pytest
import yaml

from flow_rl.training.fpo_config import FPOTrainingConfig


def _raw(tmp_path: Path) -> dict[str, object]:
    build = tmp_path / "3DBall.exe"
    build.write_bytes(b"build")
    return {
        "build_path": "3DBall.exe",
        "run_directory": "run",
        "total_environment_steps": 100,
        "rollout_size": 20,
        "batch_size": 10,
        "epochs": 2,
        "gamma": 0.99,
        "gae_lambda": 0.95,
        "learning_rate": 0.0003,
        "final_learning_rate": 0.0,
        "clip_range": 0.2,
        "final_clip_range": 0.1,
        "max_gradient_norm": 0.5,
        "state_size": 128,
        "time_embedding_size": 32,
        "velocity_hidden_sizes": [128, 128],
        "critic_hidden_sizes": [128, 128],
        "nfe": 4,
        "num_fpo_samples": 50,
        "difference_clip": 3.0,
        "positive_advantage": False,
        "checkpoint_interval": 50,
        "time_scale": 20.0,
        "device": "cpu",
        "worker_id": 80,
        "evaluation_worker_id": 81,
        "seed": 0,
        "timeout_wait": 60,
        "max_agent_absence_steps": 10,
        "behavior_name": None,
        "resume_checkpoint": None,
    }


def test_fpo_config_parses_strict_complete_yaml(tmp_path: Path) -> None:
    # Silently dropping FPO solver fields would make runs unreproducible.
    source = tmp_path / "fpo.yaml"
    source.write_text(yaml.safe_dump(_raw(tmp_path)), encoding="utf-8")

    config = FPOTrainingConfig.from_yaml(source)

    assert config.build_path == (tmp_path / "3DBall.exe").resolve()
    assert config.run_directory == (tmp_path / "run").resolve()
    assert config.velocity_hidden_sizes == (128, 128)
    assert config.critic_hidden_sizes == (128, 128)
    assert config.nfe == 4
    assert config.num_fpo_samples == 50
    assert config.positive_advantage is False


@pytest.mark.parametrize("nfe", [0, 3, 16])
def test_fpo_config_rejects_nfe_outside_experiment_set(
    tmp_path: Path,
    nfe: int,
) -> None:
    raw = _raw(tmp_path)
    raw["nfe"] = nfe
    source = tmp_path / f"bad-{nfe}.yaml"
    source.write_text(yaml.safe_dump(raw), encoding="utf-8")

    with pytest.raises(ValueError, match="nfe"):
        FPOTrainingConfig.from_yaml(source)


def test_fpo_config_rejects_unknown_key(tmp_path: Path) -> None:
    raw = _raw(tmp_path)
    raw["typo"] = 1
    source = tmp_path / "bad.yaml"
    source.write_text(yaml.safe_dump(raw), encoding="utf-8")

    with pytest.raises(ValueError, match="keys mismatch"):
        FPOTrainingConfig.from_yaml(source)
