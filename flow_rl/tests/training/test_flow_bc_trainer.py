from __future__ import annotations

import csv
import hashlib
from pathlib import Path

import numpy as np
import pytest
import torch
import yaml
from torch import nn

from flow_rl.data.demonstrations import DemonstrationBatch, save_demonstrations


class _ConstantVelocity(nn.Module):
    def __init__(self, value: float) -> None:
        super().__init__()
        self.value = nn.Parameter(torch.tensor(value, dtype=torch.float32))

    def forward(self, observations, latent_actions, time):
        return torch.ones_like(latent_actions) * self.value


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _config(tmp_path: Path, *, run_name: str, resume: str | None = None) -> Path:
    build = tmp_path / "3DBall.exe"
    if not build.exists():
        build.write_bytes(b"build")
    dataset = tmp_path / "dataset"
    if not dataset.exists():
        observations = np.linspace(-1.0, 1.0, 32, dtype=np.float32).reshape(16, 2)
        actions = (0.25 * observations[:, :1]).astype(np.float32)
        save_demonstrations(
            dataset,
            DemonstrationBatch(
                agent_ids=np.arange(16, dtype=np.int64),
                observations=(observations,),
                actions=actions,
                terminated=np.zeros(16, dtype=bool),
                truncated=np.zeros(16, dtype=bool),
            ),
            {
                "schema_version": 1,
                "sample_count": 16,
                "dataset_seed": 100,
                "checkpoint_sha256": "ppo-checkpoint",
                "build_sha256": _sha256(build),
                "observation_shapes": [[2]],
                "action_size": 1,
                "actions_are_bounded": True,
                "observations_are_raw": True,
                "observation_normalization": {
                    "count": 16,
                    "epsilon": 1e-8,
                    "clip": 10.0,
                    "mean": [[0.0, 0.0]],
                    "variance": [[1.0, 1.0]],
                },
            },
        )
    raw = {
        "dataset_directory": "dataset",
        "dataset_sha256": _sha256(dataset / "demonstrations.npz"),
        "metadata_sha256": _sha256(dataset / "metadata.json"),
        "build_path": "3DBall.exe",
        "run_directory": run_name,
        "epochs": 2,
        "batch_size": 4,
        "learning_rate": 1e-2,
        "weight_decay": 0.0,
        "validation_fraction": 0.25,
        "state_size": 4,
        "time_embedding_size": 4,
        "velocity_hidden_sizes": [8],
        "nfe": 1,
        "checkpoint_interval_epochs": 1,
        "device": "cpu",
        "seed": 600,
        "split_seed": 601,
        "resume_checkpoint": resume,
    }
    path = tmp_path / f"{run_name}.yaml"
    path.write_text(yaml.safe_dump(raw), encoding="utf-8")
    return path


def test_flow_matching_loss_matches_hand_derived_condot_value() -> None:
    from flow_rl.training.flow_bc_trainer import flow_matching_loss

    model = _ConstantVelocity(1.0)
    observations = (torch.tensor([[3.0]], dtype=torch.float32),)
    actions = torch.tensor([[2.0]], dtype=torch.float32)
    noise = torch.tensor([[0.0]], dtype=torch.float32)
    time = torch.tensor([[0.25]], dtype=torch.float32)

    loss = flow_matching_loss(model, observations, actions, noise=noise, time=time)

    torch.testing.assert_close(loss, torch.tensor(1.0))
    loss.backward()
    assert model.value.grad is not None
    assert torch.isfinite(model.value.grad)


@pytest.mark.parametrize(
    ("field", "replacement", "message"),
    [
        ("actions", torch.tensor([[2.0]], dtype=torch.float64), "actions"),
        ("noise", torch.tensor([0.0], dtype=torch.float32), "noise"),
        ("time", torch.tensor([0.25], dtype=torch.float32), "time"),
        ("actions", torch.tensor([[float("nan")]], dtype=torch.float32), "finite"),
    ],
)
def test_flow_matching_loss_rejects_invalid_inputs(
    field: str, replacement: torch.Tensor, message: str
) -> None:
    from flow_rl.training.flow_bc_trainer import flow_matching_loss

    values = {
        "actions": torch.tensor([[2.0]], dtype=torch.float32),
        "noise": torch.tensor([[0.0]], dtype=torch.float32),
        "time": torch.tensor([[0.25]], dtype=torch.float32),
    }
    values[field] = replacement

    with pytest.raises((TypeError, ValueError), match=message):
        flow_matching_loss(
            _ConstantVelocity(1.0),
            (torch.tensor([[3.0]], dtype=torch.float32),),
            values["actions"],
            noise=values["noise"],
            time=values["time"],
        )


def test_flow_bc_trainer_logs_and_saves_resumable_checkpoint(tmp_path: Path) -> None:
    from flow_rl.tracking.checkpoint import load_checkpoint, validate_flow_bc_checkpoint
    from flow_rl.training.flow_bc_config import FlowBCTrainingConfig
    from flow_rl.training.flow_bc_trainer import FlowBCTrainer

    config = FlowBCTrainingConfig.from_yaml(_config(tmp_path, run_name="initial"))
    torch.manual_seed(config.seed)
    from flow_rl.models.flow import ConditionalVelocityMLP

    initial_model = ConditionalVelocityMLP(
        observation_shapes=config.observation_shapes,
        action_size=config.action_size,
        state_size=config.state_size,
        time_embedding_size=config.time_embedding_size,
        hidden_sizes=config.velocity_hidden_sizes,
    )
    summary = FlowBCTrainer(config).train()

    assert summary.epochs == 2
    assert summary.train_samples == 12
    assert summary.validation_samples == 4
    assert np.isfinite(summary.final_train_loss)
    assert np.isfinite(summary.final_validation_loss)
    payload = load_checkpoint(Path(summary.final_checkpoint))
    validate_flow_bc_checkpoint(payload)
    assert payload["metadata"]["algorithm"] == "flow_bc"
    assert payload["metadata"]["dataset_sha256"] == config.dataset_sha256
    assert payload["metadata"]["solver"]["action_transform"] == "clamp"
    assert payload["normalizer_state"]["count"] == 16
    assert any(
        not torch.equal(value, initial_model.state_dict()[name])
        for name, value in payload["model_state"].items()
    )
    with (config.run_directory / "updates.csv").open(newline="", encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))
    assert len(rows) == 2
    assert all(np.isfinite(float(row["train_loss"])) for row in rows)

    resumed_config = FlowBCTrainingConfig.from_yaml(
        _config(
            tmp_path,
            run_name="resumed",
            resume="initial/checkpoints/epoch-1.pt",
        )
    )
    resumed = FlowBCTrainer(resumed_config).train()

    assert resumed.epochs == 2
    resumed_payload = load_checkpoint(Path(resumed.final_checkpoint))
    assert resumed_payload["metadata"]["epoch"] == 2

    replica_config = FlowBCTrainingConfig.from_yaml(
        _config(tmp_path, run_name="replica")
    )
    replica = FlowBCTrainer(replica_config).train()
    replica_payload = load_checkpoint(Path(replica.final_checkpoint))
    assert replica.final_train_loss == summary.final_train_loss
    assert replica.final_validation_loss == summary.final_validation_loss
    for name, value in payload["model_state"].items():
        torch.testing.assert_close(value, replica_payload["model_state"][name])
