from __future__ import annotations

import hashlib
import importlib
import json
from pathlib import Path

import pytest
import yaml


MANIFEST_PATH = (
    Path(__file__).resolve().parents[2]
    / "configs"
    / "dino_attack_structured_set_v2.yaml"
)
V2_MANIFEST_PATH = (
    Path(__file__).resolve().parents[2]
    / "configs"
    / "dino_attack_structured_set_v2.yaml"
)
V1_MANIFEST_PATH = (
    Path(__file__).resolve().parents[2]
    / "configs"
    / "dino_attack_structured_set_v1.yaml"
)
EXPECTED_SHAPES = ((5,), (5, 8), (6, 5), (11, 7), (8, 6), (10, 7))
EXPECTED_FIELDS = (
    (
        "remaining_time",
        "food_normalized",
        "last_action_valid",
        "dino_target_x_norm",
        "dino_target_z_norm",
    ),
    ("Ax", "Az", "Bx", "Bz", "Cx", "Cz", "Dx", "Dz"),
    ("valid_mask", "active", "x_norm", "z_norm", "health_ratio"),
    (
        "valid_mask",
        "type_archer",
        "type_mage",
        "x_norm",
        "z_norm",
        "health_ratio",
        "is_wall_guard",
    ),
    (
        "valid_mask",
        "alive",
        "x_norm",
        "z_norm",
        "health_ratio",
        "max_health_normalized",
    ),
    (
        "valid_mask",
        "type_velociraptor",
        "type_pachy",
        "type_trex",
        "x_norm",
        "z_norm",
        "health_ratio",
    ),
)


def _protocol_type():
    try:
        module = importlib.import_module("flow_rl.envs.dino_protocol")
    except ModuleNotFoundError as exc:
        pytest.fail(f"Dino protocol module is missing: {exc}")
    return module.DinoProtocol


@pytest.fixture
def protocol():
    return _protocol_type().from_yaml(MANIFEST_PATH)


def test_frozen_manifest_produces_strict_checkpoint_metadata(protocol) -> None:
    assert protocol.protocol_version == "dino_attack_structured_set_v2"
    assert protocol.observation_shapes == EXPECTED_SHAPES
    assert protocol.observation_fields == EXPECTED_FIELDS
    assert protocol.maxima == {
        "walls": 6,
        "guards": 11,
        "houses": 8,
        "dinos": 10,
    }
    assert protocol.map_bounds == {"x": (-55.5, 48.5), "z": (-53.0, 29.0)}
    assert protocol.food_scale == 5000.0
    assert protocol.house_health_scale == 325.0
    assert not hasattr(protocol, "region_vertices")
    assert not hasattr(protocol, "region_vertices_normalized")

    metadata = protocol.checkpoint_metadata()
    assert metadata["manifest_sha256"] == hashlib.sha256(
        MANIFEST_PATH.read_bytes()
    ).hexdigest()
    assert "region_vertices" not in metadata
    assert "region_vertices_normalized" not in metadata
    assert metadata["action"] == {
        "version": "dino_attack_structured_set_action_v1",
        "size": 4,
        "dtype": "float32",
        "range": (-1.0, 1.0),
        "dimensions": ("zone", "u", "v", "choice"),
        "zone_order": (
            "Z1RuinsForecourt",
            "Z2OpenMeadow",
            "Z3RiverTerrace",
            "Z4PalmGrove",
            "Z5RockyShelf",
        ),
        "zone_thresholds": (
            -0.6000000238418579,
            -0.20000000298023224,
            0.20000000298023224,
            0.6000000238418579,
        ),
        "choice_order": (
            "Wait",
            "Velociraptor",
            "Pachycephalosaurus",
            "T-Rex",
        ),
        "choice_thresholds": (-0.5, 0.0, 0.5),
        "boundary_rule": "equality_enters_higher_bin",
        "wait_ignores": ("zone", "u", "v"),
    }
    json.dumps(metadata)


def test_protocol_rejects_legacy_interface(protocol) -> None:
    with pytest.raises(ValueError, match="observation shapes"):
        protocol.validate_shapes(((504,),))
    with pytest.raises(ValueError, match="action size"):
        protocol.validate_action_size(6)


def test_protocol_rejects_v1_manifest(tmp_path: Path) -> None:
    with pytest.raises(ValueError, match="protocol version"):
        _protocol_type().from_yaml(V1_MANIFEST_PATH)


def test_protocol_requires_exact_keys_and_stream_order(tmp_path: Path) -> None:
    raw = yaml.safe_load(V2_MANIFEST_PATH.read_text(encoding="utf-8"))

    raw["unexpected"] = True
    extra_path = tmp_path / "extra.yaml"
    extra_path.write_text(yaml.safe_dump(raw, sort_keys=False), encoding="utf-8")
    with pytest.raises(ValueError, match="keys mismatch"):
        _protocol_type().from_yaml(extra_path)

    del raw["unexpected"]
    raw["streams"][0], raw["streams"][1] = raw["streams"][1], raw["streams"][0]
    reordered_path = tmp_path / "reordered.yaml"
    reordered_path.write_text(
        yaml.safe_dump(raw, sort_keys=False), encoding="utf-8"
    )
    with pytest.raises(ValueError, match="stream order"):
        _protocol_type().from_yaml(reordered_path)
