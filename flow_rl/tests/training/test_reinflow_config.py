from __future__ import annotations

import hashlib
from pathlib import Path

import numpy as np
import pytest
import torch
import yaml

from flow_rl.data.normalization import ObservationNormalizer
from flow_rl.models.flow import ConditionalVelocityMLP
from flow_rl.tracking.checkpoint import FlowSolverConfig, save_checkpoint


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _bc_checkpoint(tmp_path: Path) -> tuple[Path, Path]:
    build = tmp_path / "3DBall.exe"
    build.write_bytes(b"reinflow-config-build")
    actor = ConditionalVelocityMLP(
        observation_shapes=((3,),),
        action_size=2,
        state_size=4,
        time_embedding_size=4,
        hidden_sizes=(8,),
    )
    normalizer = ObservationNormalizer(((3,),))
    normalizer.update((np.zeros((5, 3), dtype=np.float32),))
    checkpoint = tmp_path / "bc.pt"
    save_checkpoint(
        checkpoint,
        model_state=actor.state_dict(),
        optimizer_state={"state": {}},
        normalizer_state=normalizer.state_dict(),
        config={
            "state_size": 4,
            "time_embedding_size": 4,
            "velocity_hidden_sizes": [8],
            "nfe": 4,
        },
        metadata={
            "schema_version": 2,
            "algorithm": "flow_bc",
            "dataset_sha256": "dataset",
            "metadata_sha256": "metadata",
            "build_sha256": _sha256(build),
            "observation_shapes": [[3]],
            "action_size": 2,
            "solver": FlowSolverConfig(nfe=4, action_transform="clamp").to_dict(),
        },
    )
    return checkpoint, build


def _raw(tmp_path: Path) -> dict[str, object]:
    checkpoint, build = _bc_checkpoint(tmp_path)
    return {
        "bc_checkpoint_path": checkpoint.name,
        "build_path": build.name,
        "run_directory": "run",
        "total_environment_steps": 24000,
        "rollout_size": 12000,
        "batch_size": 256,
        "epochs": 3,
        "gamma": 0.99,
        "gae_lambda": 0.95,
        "actor_learning_rate": 4.5e-5,
        "critic_learning_rate": 6.5e-4,
        "clip_range": 0.01,
        "entropy_coefficient": 0.03,
        "max_gradient_norm": 1.0,
        "critic_hidden_sizes": [8],
        "noise_hidden_sizes": [8],
        "nfe": 4,
        "horizon_steps": 1,
        "min_noise_std": 0.1,
        "max_noise_std": 0.24,
        "log_prob_min": -1.0,
        "log_prob_max": 1.0,
        "critic_warmup_environment_steps": 12000,
        "checkpoint_interval": 12000,
        "time_scale": 20.0,
        "device": "cpu",
        "worker_id": 140,
        "evaluation_worker_id": 141,
        "seed": 700,
        "timeout_wait": 120,
        "max_agent_absence_steps": 10,
        "behavior_name": None,
        "resume_checkpoint": None,
    }


def _write(tmp_path: Path, raw: dict[str, object], name: str = "config.yaml") -> Path:
    path = tmp_path / name
    path.write_text(yaml.safe_dump(raw), encoding="utf-8")
    return path


def test_reinflow_config_validates_bc_source_and_network(tmp_path: Path) -> None:
    from flow_rl.training.reinflow_config import ReinFlowTrainingConfig

    config = ReinFlowTrainingConfig.from_yaml(_write(tmp_path, _raw(tmp_path)))

    assert config.action_size == 2
    assert config.observation_shapes == ((3,),)
    assert config.state_size == 4
    assert config.time_embedding_size == 4
    assert config.velocity_hidden_sizes == (8,)
    assert config.bc_checkpoint_sha256 == _sha256(tmp_path / "bc.pt")


@pytest.mark.parametrize(
    ("field", "value", "message"),
    [
        ("nfe", 2, "NFE"),
        ("horizon_steps", 2, "horizon"),
        ("min_noise_std", 0.3, "noise"),
        ("log_prob_min", 2.0, "log-prob"),
        ("worker_id", 141, "worker"),
    ],
)
def test_reinflow_config_rejects_incompatible_values(
    tmp_path: Path, field: str, value: object, message: str
) -> None:
    from flow_rl.training.reinflow_config import ReinFlowTrainingConfig

    raw = _raw(tmp_path)
    raw[field] = value

    with pytest.raises((TypeError, ValueError), match=message):
        ReinFlowTrainingConfig.from_yaml(_write(tmp_path, raw))
