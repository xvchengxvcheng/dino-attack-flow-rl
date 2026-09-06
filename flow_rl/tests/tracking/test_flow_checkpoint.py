from __future__ import annotations

from pathlib import Path

import pytest

from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.tracking.checkpoint import (
    FlowSolverConfig,
    POLICYFLOW_SOURCE_COMMIT,
    validate_flow_bc_checkpoint,
    validate_flow_checkpoint,
    validate_policyflow_checkpoint,
)


MANIFEST_PATH = (
    Path(__file__).resolve().parents[2]
    / "configs"
    / "dino_attack_structured_set_v2.yaml"
)


def _payload() -> dict[str, object]:
    return {
        "model_state": {"weight": 1},
        "optimizer_state": {"state": {}},
        "normalizer_state": {"count": 5},
        "config": {
            "state_size": 128,
            "time_embedding_size": 32,
            "velocity_hidden_sizes": [128, 128],
        },
        "metadata": {
            "schema_version": 2,
            "observation_shapes": [[8]],
            "action_size": 2,
            "solver": {
                "method": "euler",
                "direction": "0_to_1",
                "nfe": 4,
                "action_transform": "tanh",
            },
        },
    }


def test_flow_solver_config_round_trips_literal_schema() -> None:
    # Omitting a solver field would make resumed sampling semantically ambiguous.
    solver = FlowSolverConfig(
        method="euler",
        direction="0_to_1",
        nfe=4,
        action_transform="tanh",
    )

    assert solver.to_dict() == {
        "method": "euler",
        "direction": "0_to_1",
        "nfe": 4,
        "action_transform": "tanh",
    }
    assert FlowSolverConfig.from_mapping(solver.to_dict()) == solver


def test_midpoint_solver_records_actual_velocity_nfe() -> None:
    solver = FlowSolverConfig(method="midpoint", nfe=8, action_transform="tanh")
    assert FlowSolverConfig.from_mapping(solver.to_dict()) == solver


@pytest.mark.parametrize(
    ("field", "value", "message"),
    [
        ("method", "rk4", "method"),
        ("direction", "1_to_0", "direction"),
        ("nfe", 0, "nfe"),
        ("action_transform", "clip", "action_transform"),
    ],
)
def test_flow_solver_config_rejects_unsupported_values(
    field: str,
    value: object,
    message: str,
) -> None:
    raw = FlowSolverConfig(nfe=4).to_dict()
    raw[field] = value

    with pytest.raises((TypeError, ValueError), match=message):
        FlowSolverConfig.from_mapping(raw)


def test_validate_flow_checkpoint_accepts_complete_payload() -> None:
    validate_flow_checkpoint(_payload())


def test_validate_flow_checkpoint_rejects_missing_network_field() -> None:
    payload = _payload()
    del payload["config"]["time_embedding_size"]  # type: ignore[index]

    with pytest.raises(ValueError, match="time_embedding_size"):
        validate_flow_checkpoint(payload)


def test_validate_flow_checkpoint_rejects_missing_solver_field() -> None:
    payload = _payload()
    del payload["metadata"]["solver"]["direction"]  # type: ignore[index]

    with pytest.raises(ValueError, match="solver"):
        validate_flow_checkpoint(payload)


def test_structured_flow_checkpoint_requires_versioned_protocol_and_encoder() -> None:
    payload = _payload()
    payload["config"]["encoder_type"] = "set_transformer"  # type: ignore[index]
    payload["metadata"]["schema_version"] = 3  # type: ignore[index]

    with pytest.raises(ValueError, match="protocol"):
        validate_flow_checkpoint(payload)

    protocol = DinoProtocol.from_yaml(MANIFEST_PATH)
    payload["metadata"]["protocol"] = protocol.checkpoint_metadata()  # type: ignore[index]
    with pytest.raises(ValueError, match="encoder"):
        validate_flow_checkpoint(payload)

    payload["metadata"]["encoder"] = {  # type: ignore[index]
        "encoder_type": "set_transformer",
        "observation_shapes": protocol.observation_shapes,
        "output_size": 256,
    }
    payload["config"]["state_size"] = 256  # type: ignore[index]
    payload["metadata"]["observation_shapes"] = protocol.observation_shapes  # type: ignore[index]
    payload["metadata"]["action_size"] = 4  # type: ignore[index]
    payload["normalizer_state"] = {
        "normalizer_type": "identity",
        "shapes": protocol.observation_shapes,
        "protocol_manifest_sha256": protocol.manifest_sha256,
    }
    with pytest.raises(ValueError, match="encoder config"):
        validate_flow_checkpoint(payload)

    payload["metadata"]["encoder"] = SetTransformerDinoEncoder(  # type: ignore[index]
        protocol,
        dropout=0.0,
    ).checkpoint_metadata()
    payload["config"]["state_size"] = 128  # type: ignore[index]
    payload["config"].update(  # type: ignore[union-attr]
        {
            "encoder_output_size": 128,
            "encoder_d_model": 48,
            "encoder_heads": 4,
            "encoder_inducing_points": 8,
            "encoder_layers": 1,
            "encoder_dropout": 0.0,
        }
    )
    validate_flow_checkpoint(payload)


def test_validate_flow_bc_checkpoint_requires_source_and_clamp_solver() -> None:
    payload = _payload()
    payload["metadata"].update(  # type: ignore[union-attr]
        {
            "algorithm": "flow_bc",
            "dataset_sha256": "dataset",
            "metadata_sha256": "metadata",
            "build_sha256": "build",
        }
    )
    payload["metadata"]["solver"]["action_transform"] = "clamp"  # type: ignore[index]

    validate_flow_bc_checkpoint(payload)

    del payload["metadata"]["dataset_sha256"]  # type: ignore[index]
    with pytest.raises(ValueError, match="dataset_sha256"):
        validate_flow_bc_checkpoint(payload)


def _policyflow_payload() -> dict[str, object]:
    payload = _payload()
    payload["config"].update({"solver_steps": 2, "velocity_nfe": 4})  # type: ignore[union-attr]
    payload["metadata"].update(  # type: ignore[union-attr]
        {
            "algorithm": "policyflow",
            "policyflow_source_commit": POLICYFLOW_SOURCE_COMMIT,
            "normalizer_checkpoint_sha256": "normalizer",
            "build_sha256": "build",
            "environment_steps": 10,
            "optimizer_updates": 1,
            "solver_steps": 2,
            "rollout_boundary": True,
            "restart_boundary": True,
            "in_flight_decisions": 0,
        }
    )
    payload["metadata"]["solver"] = FlowSolverConfig(  # type: ignore[index]
        method="midpoint", nfe=4, action_transform="tanh"
    ).to_dict()
    return payload


def test_validate_policyflow_checkpoint_requires_pinned_source_and_clean_boundary() -> None:
    payload = _policyflow_payload()
    validate_policyflow_checkpoint(payload)
    payload["metadata"]["policyflow_source_commit"] = "other"  # type: ignore[index]
    with pytest.raises(ValueError, match="pinned official"):
        validate_policyflow_checkpoint(payload)
    payload = _policyflow_payload()
    payload["metadata"]["rollout_boundary"] = False  # type: ignore[index]
    with pytest.raises(ValueError, match="clean rollout boundary"):
        validate_policyflow_checkpoint(payload)
