from __future__ import annotations

import hashlib
from pathlib import Path

import numpy as np
import pytest
import torch
import yaml

from flow_rl.data.demonstrations import DemonstrationBatch, save_demonstrations
from flow_rl.data.dino_demonstrations import (
    DinoDemonstrationBatch,
    save_dino_demonstrations,
)
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _raw(tmp_path: Path) -> dict[str, object]:
    build = tmp_path / "3DBall.exe"
    build.write_bytes(b"build")
    dataset = tmp_path / "dataset"
    batch = DemonstrationBatch(
        agent_ids=np.arange(6, dtype=np.int64),
        observations=(np.arange(12, dtype=np.float32).reshape(6, 2),),
        actions=np.linspace(-0.5, 0.5, 6, dtype=np.float32).reshape(6, 1),
        terminated=np.zeros(6, dtype=bool),
        truncated=np.zeros(6, dtype=bool),
    )
    save_demonstrations(
        dataset,
        batch,
        {
            "schema_version": 1,
            "sample_count": 6,
            "dataset_seed": 10,
            "checkpoint_sha256": "checkpoint-hash",
            "build_sha256": _sha256(build),
            "observation_shapes": [[2]],
            "action_size": 1,
            "actions_are_bounded": True,
            "observations_are_raw": True,
            "observation_normalization": {
                "count": 6,
                "epsilon": 1e-8,
                "clip": 10.0,
                "mean": [[5.0, 6.0]],
                "variance": [[2.0, 3.0]],
            },
        },
    )
    return {
        "dataset_directory": "dataset",
        "dataset_sha256": _sha256(dataset / "demonstrations.npz"),
        "metadata_sha256": _sha256(dataset / "metadata.json"),
        "build_path": "3DBall.exe",
        "run_directory": "run",
        "epochs": 3,
        "batch_size": 2,
        "learning_rate": 1e-3,
        "weight_decay": 1e-6,
        "validation_fraction": 1 / 3,
        "state_size": 8,
        "time_embedding_size": 4,
        "velocity_hidden_sizes": [8],
        "nfe": 4,
        "checkpoint_interval_epochs": 1,
        "device": "cpu",
        "seed": 600,
        "split_seed": 601,
        "resume_checkpoint": None,
    }


def _write(tmp_path: Path, raw: dict[str, object], name: str = "bc.yaml") -> Path:
    source = tmp_path / name
    source.write_text(yaml.safe_dump(raw), encoding="utf-8")
    return source


def test_flow_bc_config_validates_dataset_source_and_reproducible_split(
    tmp_path: Path,
) -> None:
    # A changed dataset or overlapping split would invalidate all BC evidence.
    from flow_rl.training.flow_bc_config import (
        FlowBCTrainingConfig,
        split_demonstrations,
    )

    config = FlowBCTrainingConfig.from_yaml(_write(tmp_path, _raw(tmp_path)))
    first = split_demonstrations(
        config.demonstrations,
        validation_fraction=config.validation_fraction,
        seed=config.split_seed,
    )
    second = split_demonstrations(
        config.demonstrations,
        validation_fraction=config.validation_fraction,
        seed=config.split_seed,
    )

    assert config.observation_shapes == ((2,),)
    assert config.action_size == 1
    np.testing.assert_array_equal(first[0], second[0])
    np.testing.assert_array_equal(first[1], second[1])
    assert len(first[0]) == 4
    assert len(first[1]) == 2
    assert set(first[0]).isdisjoint(first[1])
    assert sorted(np.concatenate(first).tolist()) == list(range(6))


@pytest.mark.parametrize(
    ("mutation", "message"),
    [
        ({"validation_fraction": 0.0}, "validation_fraction"),
        ({"nfe": 3}, "nfe"),
        ({"dataset_sha256": "wrong"}, "dataset SHA-256"),
    ],
)
def test_flow_bc_config_rejects_invalid_or_changed_inputs(
    tmp_path: Path,
    mutation: dict[str, object],
    message: str,
) -> None:
    from flow_rl.training.flow_bc_config import FlowBCTrainingConfig

    raw = _raw(tmp_path)
    raw.update(mutation)

    with pytest.raises((ValueError, FileNotFoundError), match=message):
        FlowBCTrainingConfig.from_yaml(_write(tmp_path, raw))


def test_flow_bc_config_rejects_unbounded_metadata_and_unknown_keys(
    tmp_path: Path,
) -> None:
    from flow_rl.training.flow_bc_config import FlowBCTrainingConfig

    raw = _raw(tmp_path)
    raw["typo"] = True
    with pytest.raises(ValueError, match="keys mismatch"):
        FlowBCTrainingConfig.from_yaml(_write(tmp_path, raw, "unknown.yaml"))

    raw.pop("typo")
    metadata_path = tmp_path / "dataset" / "metadata.json"
    metadata = metadata_path.read_text(encoding="utf-8").replace(
        '"actions_are_bounded": true', '"actions_are_bounded": false'
    )
    metadata_path.write_text(metadata, encoding="utf-8")
    raw["metadata_sha256"] = _sha256(metadata_path)
    with pytest.raises(ValueError, match="bounded"):
        FlowBCTrainingConfig.from_yaml(_write(tmp_path, raw, "unbounded.yaml"))


def test_dino_split_keeps_episodes_whole_and_stratifies_maps() -> None:
    from flow_rl.training.flow_bc_config import split_demonstrations

    batch = DemonstrationBatch(
        agent_ids=np.arange(12, dtype=np.int64),
        observations=(np.zeros((12, 2), dtype=np.float32),),
        actions=np.zeros((12, 1), dtype=np.float32),
        terminated=np.zeros(12, dtype=bool),
        truncated=np.zeros(12, dtype=bool),
    )
    episode_ids = np.repeat(np.arange(6, dtype=np.int64), 2)
    map_ids = np.repeat(np.asarray([0, 0, 0, 1, 1, 1], dtype=np.int64), 2)

    train, validation = split_demonstrations(
        batch,
        validation_fraction=1 / 3,
        seed=99,
        episode_ids=episode_ids,
        strata=map_ids,
    )

    assert set(episode_ids[train]).isdisjoint(set(episode_ids[validation]))
    assert set(map_ids[validation]) == {0, 1}
    assert len(set(episode_ids[validation])) == 2


def test_flow_bc_config_loads_structured_dino_dataset_with_identity_normalizer(
    tmp_path: Path,
) -> None:
    from flow_rl.training.flow_bc_config import FlowBCTrainingConfig

    protocol_path = (Path(__file__).resolve().parents[3] / "flow_rl/configs/dino_attack_structured_set_v2.yaml").resolve()
    protocol = DinoProtocol.from_yaml(protocol_path)
    build = tmp_path / "Dino.exe"
    build.write_bytes(b"dino-build")
    count = 8
    base = DemonstrationBatch(
        agent_ids=np.arange(count, dtype=np.int64),
        observations=tuple(
            np.zeros((count, *shape), dtype=np.float32)
            for shape in protocol.observation_shapes
        ),
        actions=np.zeros((count, protocol.action_size), dtype=np.float32),
        terminated=np.asarray([False, True] * 4, dtype=bool),
        truncated=np.zeros(count, dtype=bool),
    )
    dataset = tmp_path / "dino-dataset"
    encoder = SetTransformerDinoEncoder(protocol)
    save_dino_demonstrations(
        dataset,
        DinoDemonstrationBatch(
            demonstrations=base,
            environment_ids=np.repeat(np.arange(4, dtype=np.int64), 2),
            process_generations=np.zeros(count, dtype=np.int64),
            policy_versions=np.full(count, 8, dtype=np.int64),
            episode_ids=np.repeat(np.arange(4, dtype=np.int64), 2),
            map_ids=np.repeat(np.asarray([0, 0, 1, 1], dtype=np.int64), 2),
            action_valid=np.ones(count, dtype=bool),
        ),
        {
            "schema_version": 2,
            "sample_count": count,
            "build_sha256": _sha256(build),
            "normalizer_type": "identity",
            "protocol_version": protocol.protocol_version,
            "protocol_manifest_sha256": protocol.manifest_sha256,
            "encoder": encoder.checkpoint_metadata(),
        },
    )
    raw = _raw(tmp_path)
    raw.update(
        {
            "dataset_directory": str(dataset),
            "dataset_sha256": _sha256(dataset / "demonstrations.npz"),
            "metadata_sha256": _sha256(dataset / "metadata.json"),
            "build_path": str(build),
            "encoder_type": "set_transformer",
            "protocol_path": str(protocol_path),
            "encoder_d_model": 48,
            "encoder_heads": 4,
            "encoder_inducing_points": 8,
            "encoder_layers": 1,
            "encoder_dropout": 0.0,
            "encoder_output_size": 128,
            "state_size": 128,
        }
    )

    config = FlowBCTrainingConfig.from_yaml(_write(tmp_path, raw, "dino.yaml"))

    assert config.observation_shapes == protocol.observation_shapes
    assert config.action_size == 4
    assert config.normalizer_type == "identity"
    assert config.episode_ids is not None
    assert config.map_ids is not None
    from flow_rl.training.flow_bc_trainer import (
        _build_velocity_model,
        _normalizer_from_metadata,
        flow_matching_loss,
    )

    model = _build_velocity_model(config)
    normalizer = _normalizer_from_metadata(config)
    normalized = normalizer.normalize(config.demonstrations.observations)
    observations = tuple(
        torch.as_tensor(np.array(item[:2], copy=True)) for item in normalized
    )
    actions = torch.as_tensor(np.array(config.demonstrations.actions[:2], copy=True))
    loss = flow_matching_loss(
        model,
        observations,
        actions,
        noise=torch.zeros_like(actions),
        time=torch.full((2, 1), 0.5, dtype=torch.float32),
    )

    assert model.state_encoder.checkpoint_metadata()["encoder_type"] == (
        "set_transformer"
    )
    assert torch.isfinite(loss)
