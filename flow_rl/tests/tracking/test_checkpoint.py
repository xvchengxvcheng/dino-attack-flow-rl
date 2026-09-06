from __future__ import annotations

import importlib
from copy import deepcopy
from pathlib import Path

import numpy as np
import pytest
import torch
import yaml

from flow_rl.data import normalization as normalization_module
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.tracking import checkpoint as checkpoint_module
from flow_rl.training.config import PPOTrainingConfig
from flow_rl.training.fpo_config import FPOTrainingConfig
from flow_rl.training.fpo_trainer import FPOTrainer
from flow_rl.training.trainer import PPOTrainer


MANIFEST_PATH = (
    Path(__file__).resolve().parents[2]
    / "configs"
    / "dino_attack_structured_set_v2.yaml"
)
V2_MANIFEST_PATH = MANIFEST_PATH


def _checkpoint_module():
    try:
        return importlib.import_module("flow_rl.tracking.checkpoint")
    except ModuleNotFoundError as exc:
        pytest.fail(f"checkpoint module is missing: {exc}")


def test_checkpoint_atomically_round_trips_shared_training_state(tmp_path) -> None:
    checkpoint = _checkpoint_module()
    path = tmp_path / "checkpoints" / "latest.pt"
    normalizer_state = {
        "count": 2,
        "mean": (np.asarray([1.5], dtype=np.float64),),
    }

    checkpoint.save_checkpoint(
        path,
        model_state={"weight": torch.tensor([1.0, 2.0])},
        optimizer_state={"step": 9},
        normalizer_state=normalizer_state,
        config={"gamma": 0.99},
        metadata={"environment_steps": 1234},
    )
    restored = checkpoint.load_checkpoint(path)

    torch.testing.assert_close(restored["model_state"]["weight"], torch.tensor([1.0, 2.0]))
    assert restored["optimizer_state"] == {"step": 9}
    np.testing.assert_array_equal(restored["normalizer_state"]["mean"][0], [1.5])
    assert restored["config"] == {"gamma": 0.99}
    assert restored["metadata"] == {"environment_steps": 1234}
    assert not list(path.parent.glob("*.tmp"))


def test_structured_checkpoint_integration_modules_import() -> None:
    assert PPOTrainer is not None
    assert FPOTrainer is not None


def _structured_payload() -> tuple[dict[str, object], DinoProtocol, dict[str, object]]:
    protocol = DinoProtocol.from_yaml(MANIFEST_PATH)
    IdentityObservationNormalizer = getattr(
        normalization_module,
        "IdentityObservationNormalizer",
        None,
    )
    assert IdentityObservationNormalizer is not None, (
        "structured identity normalizer is missing"
    )
    encoder_metadata = SetTransformerDinoEncoder(
        protocol,
        dropout=0.0,
    ).checkpoint_metadata()
    payload: dict[str, object] = {
        "model_state": {"weight": torch.tensor([1.0])},
        "optimizer_state": {"state": {}},
        "normalizer_state": IdentityObservationNormalizer(
            protocol.observation_shapes,
            protocol_manifest_sha256=protocol.manifest_sha256,
        ).state_dict(),
        "config": {
            "encoder_type": "set_transformer",
            "encoder_output_size": 128,
            "encoder_d_model": 48,
            "encoder_heads": 4,
            "encoder_inducing_points": 8,
            "encoder_layers": 1,
            "encoder_dropout": 0.0,
        },
        "metadata": {
            "schema_version": 3,
            "observation_shapes": protocol.observation_shapes,
            "action_size": 4,
            "protocol": protocol.checkpoint_metadata(),
            "encoder": deepcopy(encoder_metadata),
        },
    }
    return payload, protocol, encoder_metadata


@pytest.mark.parametrize(
    "mutation",
    ("observation_shapes", "action_size", "protocol", "encoder_type"),
)
def test_structured_checkpoint_rejects_contract_mismatch_before_weight_load(
    mutation: str,
) -> None:
    payload, protocol, encoder_metadata = _structured_payload()
    if mutation == "observation_shapes":
        payload["metadata"]["observation_shapes"] = ((504,),)  # type: ignore[index]
    elif mutation == "action_size":
        payload["metadata"]["action_size"] = 6  # type: ignore[index]
    elif mutation == "protocol":
        changed_protocol = deepcopy(payload["metadata"]["protocol"])  # type: ignore[index]
        changed_normalization = deepcopy(changed_protocol["normalization"])
        changed_bounds = deepcopy(changed_normalization["map_bounds"])
        changed_bounds["x"] = (-999.0, 999.0)
        changed_normalization["map_bounds"] = changed_bounds
        changed_protocol["normalization"] = changed_normalization
        payload["metadata"]["protocol"] = changed_protocol  # type: ignore[index]
    else:
        payload["metadata"]["encoder"]["encoder_type"] = "deep_sets"  # type: ignore[index]

    with pytest.raises(ValueError, match=mutation):
        validator = getattr(
            checkpoint_module,
            "validate_checkpoint_compatibility",
            None,
        )
        assert validator is not None, "strict compatibility validator is missing"
        validator(
            payload,
            expected_observation_shapes=protocol.observation_shapes,
            expected_action_size=4,
            expected_protocol_metadata=protocol.checkpoint_metadata(),
            expected_encoder_metadata=encoder_metadata,
        )


def test_structured_checkpoint_rejects_legacy_fixed_region_identity_topology() -> None:
    payload, protocol, encoder_metadata = _structured_payload()
    payload["metadata"]["encoder"].pop("region_query_mode", None)  # type: ignore[index]

    with pytest.raises(ValueError, match="encoder metadata mismatch"):
        checkpoint_module.validate_checkpoint_compatibility(
            payload,
            expected_observation_shapes=protocol.observation_shapes,
            expected_action_size=4,
            expected_protocol_metadata=protocol.checkpoint_metadata(),
            expected_encoder_metadata=encoder_metadata,
        )


@pytest.mark.parametrize("field", ("defense_block", "dinosaur_block", "region_fusion"))
def test_structured_checkpoint_rejects_legacy_attention_topology(field: str) -> None:
    payload, protocol, encoder_metadata = _structured_payload()
    payload["metadata"]["encoder"].pop(field, None)  # type: ignore[index]

    with pytest.raises(ValueError, match="encoder metadata mismatch"):
        checkpoint_module.validate_checkpoint_compatibility(
            payload,
            expected_observation_shapes=protocol.observation_shapes,
            expected_action_size=4,
            expected_protocol_metadata=protocol.checkpoint_metadata(),
            expected_encoder_metadata=encoder_metadata,
        )


def test_identity_structured_normalizer_preserves_masks_and_binds_protocol() -> None:
    protocol = DinoProtocol.from_yaml(MANIFEST_PATH)
    IdentityObservationNormalizer = getattr(
        normalization_module,
        "IdentityObservationNormalizer",
        None,
    )
    assert IdentityObservationNormalizer is not None, (
        "structured identity normalizer is missing"
    )
    normalizer = IdentityObservationNormalizer(
        protocol.observation_shapes,
        protocol_manifest_sha256=protocol.manifest_sha256,
    )
    observations = tuple(
        np.zeros((2, *shape), dtype=np.float32)
        for shape in protocol.observation_shapes
    )
    observations[1][:, :, 0] = 1.0

    normalizer.update(observations)
    normalized = normalizer.normalize(observations)

    for actual, expected in zip(normalized, observations):
        np.testing.assert_array_equal(actual, expected)
    assert normalizer.state_dict() == {
        "normalizer_type": "identity",
        "shapes": protocol.observation_shapes,
        "protocol_manifest_sha256": protocol.manifest_sha256,
    }
    changed = dict(normalizer.state_dict())
    changed["protocol_manifest_sha256"] = "changed"
    with pytest.raises(ValueError, match="protocol"):
        normalizer.load_state_dict(changed)


def test_structured_checkpoint_accepts_v2_dynamic_regions_without_v1_vertices() -> None:
    protocol = DinoProtocol.from_yaml(V2_MANIFEST_PATH)
    encoder_metadata = SetTransformerDinoEncoder(
        protocol,
        dropout=0.0,
    ).checkpoint_metadata()
    payload = {
        "model_state": {"weight": torch.tensor([1.0])},
        "optimizer_state": {"state": {}},
        "normalizer_state": normalization_module.IdentityObservationNormalizer(
            protocol.observation_shapes,
            protocol_manifest_sha256=protocol.manifest_sha256,
        ).state_dict(),
        "config": {
            "encoder_type": "set_transformer",
            "encoder_output_size": 128,
            "encoder_d_model": 48,
            "encoder_heads": 4,
            "encoder_inducing_points": 8,
            "encoder_layers": 1,
            "encoder_dropout": 0.0,
        },
        "metadata": {
            "schema_version": 3,
            "observation_shapes": protocol.observation_shapes,
            "action_size": protocol.action_size,
            "protocol": protocol.checkpoint_metadata(),
            "encoder": encoder_metadata,
        },
    }

    checkpoint_module.validate_checkpoint_compatibility(
        payload,
        expected_observation_shapes=protocol.observation_shapes,
        expected_action_size=protocol.action_size,
        expected_protocol_metadata=protocol.checkpoint_metadata(),
        expected_encoder_metadata=encoder_metadata,
    )


@pytest.mark.parametrize(
    ("config_name", "config_type"),
    (
        ("phase3_ppo_3dball.yaml", PPOTrainingConfig),
        ("phase5_fpo_3dball.yaml", FPOTrainingConfig),
    ),
)
def test_existing_flat_yaml_receives_backward_compatible_encoder_defaults(
    tmp_path, config_name: str, config_type: type
) -> None:
    source = MANIFEST_PATH.parent / config_name
    raw = yaml.safe_load(source.read_text(encoding="utf-8"))
    build = tmp_path / "3DBall.exe"
    build.write_bytes(b"build")
    raw["build_path"] = str(build)
    raw["run_directory"] = str(tmp_path / "run")
    raw["resume_checkpoint"] = None
    config_path = tmp_path / config_name
    config_path.write_text(yaml.safe_dump(raw), encoding="utf-8")

    config = config_type.from_yaml(config_path)

    assert config.encoder_type == "flat"
    assert config.protocol_path is None
    assert config.normalize_observations is True
    assert config.encoder_d_model == 64
    assert config.encoder_heads == 4
    assert config.encoder_inducing_points == 8
    assert config.encoder_layers == 2
    assert config.encoder_dropout == pytest.approx(0.05)
    assert config.encoder_output_size == 256
