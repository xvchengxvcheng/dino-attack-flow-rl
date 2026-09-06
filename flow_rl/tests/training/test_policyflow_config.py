from __future__ import annotations

from pathlib import Path

import pytest
import yaml

from flow_rl.training.policyflow_config import (
    PolicyFlowTrainingConfig,
    _normalizer_states_equal,
)


def _config() -> dict:
    return {
        "normalizer_checkpoint_path": "../../reports/phase3/ppo-seed-0/checkpoints/final.pt",
        "actor_initialization_checkpoint_path": "../../reports/phase6/flow-bc-seed-0/checkpoints/final.pt",
        "build_path": "../../Builds/3DBall/3DBall.exe",
        "run_directory": "../../reports/phase7/test-config-output",
        "total_environment_steps": 24000, "rollout_size": 12000,
        "batch_size": 64, "epochs": 2, "gamma": 0.99, "gae_lambda": 0.99,
        "actor_learning_rate": 0.0003, "critic_learning_rate": 0.0003,
        "clip_range": 0.2, "gaussian_entropy_coefficient": 0.001,
        "brownian_coefficient": 0.25, "value_clip": 0.2,
        "max_gradient_norm": 0.5, "state_size": 128,
        "time_embedding_size": 32, "velocity_hidden_sizes": [128, 128],
        "critic_hidden_sizes": [128, 128], "solver_steps": 2,
        "horizon_steps": 1, "checkpoint_interval": 12000,
        "time_scale": 20.0, "device": "cpu", "worker_id": 170,
        "evaluation_worker_id": 171, "seed": 0, "timeout_wait": 60,
        "max_agent_absence_steps": 10, "behavior_name": None,
        "resume_checkpoint": None,
    }


def test_yaml_loads_derived_official_nfe_and_normalizer_metadata(tmp_path: Path, flow_bc_fixture) -> None:
    path = tmp_path / "config.yaml"
    values = _config()
    build, checkpoint = flow_bc_fixture
    values["normalizer_checkpoint_path"] = str(checkpoint)
    values["actor_initialization_checkpoint_path"] = str(checkpoint)
    values["build_path"] = str(build)
    values["run_directory"] = str(tmp_path / "run")
    path.write_text(yaml.safe_dump(values), encoding="utf-8")
    config = PolicyFlowTrainingConfig.from_yaml(path)
    assert config.velocity_nfe == 4
    assert config.observation_shapes == ((8,),)
    assert config.action_size == 2
    assert len(config.actor_initialization_checkpoint_sha256) == 64


def test_yaml_is_strict_about_extra_keys(tmp_path: Path) -> None:
    path = tmp_path / "config.yaml"
    values = _config()
    values["unexpected"] = True
    path.write_text(yaml.safe_dump(values), encoding="utf-8")
    with pytest.raises(ValueError, match="extra"):
        PolicyFlowTrainingConfig.from_yaml(path)


def test_normalizer_provenance_comparison_rejects_compatible_but_different_state() -> None:
    assert _normalizer_states_equal({"count": 2}, {"count": 2})
    assert not _normalizer_states_equal({"count": 2}, {"count": 3})
