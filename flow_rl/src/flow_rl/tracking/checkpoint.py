from __future__ import annotations

import os
import tempfile
from collections.abc import Mapping
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

import torch


POLICYFLOW_SOURCE_COMMIT = "7f304b96e93b2804bdcc943710db885adddd06b8"
STRUCTURED_CHECKPOINT_SCHEMA_VERSION = 3


def _canonical(value: Any) -> Any:
    if isinstance(value, Mapping):
        return tuple(
            (str(key), _canonical(item))
            for key, item in sorted(value.items(), key=lambda pair: str(pair[0]))
        )
    if isinstance(value, (list, tuple)):
        return tuple(_canonical(item) for item in value)
    return value


def _validate_structured_metadata(
    payload: Mapping[str, Any],
) -> None:
    config = payload["config"]
    metadata = payload["metadata"]
    encoder_type = config.get("encoder_type", "flat")
    if encoder_type == "flat":
        return
    if encoder_type not in {"set_transformer", "deep_sets"}:
        raise ValueError(f"unsupported structured encoder_type: {encoder_type!r}")
    if metadata.get("schema_version") != STRUCTURED_CHECKPOINT_SCHEMA_VERSION:
        raise ValueError(
            "structured checkpoint schema_version must be "
            f"{STRUCTURED_CHECKPOINT_SCHEMA_VERSION}"
        )
    for name in ("protocol", "encoder"):
        if name not in metadata:
            raise ValueError(f"structured checkpoint is missing {name} metadata")
        if not isinstance(metadata[name], Mapping):
            raise TypeError(f"structured checkpoint {name} metadata must be a mapping")
    protocol = metadata["protocol"]
    required_protocol = {
        "protocol_version",
        "manifest_sha256",
        "observation_shapes",
        "observation_streams",
        "observation_fields",
        "maxima",
        "normalization",
        "region_order",
        "action",
    }
    if protocol.get("protocol_version") == "dino_attack_structured_set_v1":
        required_protocol.update(
            {"region_vertex_order", "region_vertices_normalized"}
        )
    missing_protocol = sorted(required_protocol - set(protocol))
    if missing_protocol:
        raise ValueError(
            f"structured checkpoint protocol metadata is missing {missing_protocol}"
        )
    if not isinstance(protocol["manifest_sha256"], str) or not protocol[
        "manifest_sha256"
    ]:
        raise ValueError("structured checkpoint protocol manifest hash is invalid")
    action = protocol["action"]
    required_action = {
        "version",
        "size",
        "dtype",
        "range",
        "dimensions",
        "zone_order",
        "zone_thresholds",
        "choice_order",
        "choice_thresholds",
        "boundary_rule",
        "wait_ignores",
    }
    if not isinstance(action, Mapping) or required_action - set(action):
        raise ValueError("structured checkpoint protocol action metadata is incomplete")
    encoder = metadata["encoder"]
    if encoder.get("encoder_type") != encoder_type:
        raise ValueError("structured checkpoint encoder_type metadata mismatch")
    required_encoder = {"encoder_type", "observation_shapes", "output_size"}
    if encoder_type == "set_transformer":
        required_encoder.update(
            {
                "d_model",
                "heads",
                "inducing_points",
                "layers",
                "dropout",
                "protocol",
            }
        )
    missing_encoder = sorted(required_encoder - set(encoder))
    if missing_encoder:
        raise ValueError(
            f"structured checkpoint encoder config is missing {missing_encoder}"
        )
    if encoder_type == "set_transformer" and _canonical(encoder["protocol"]) != _canonical(
        protocol
    ):
        raise ValueError("structured checkpoint encoder protocol metadata mismatch")
    encoder_config_keys = {
        "encoder_output_size": "output_size",
    }
    if encoder_type == "set_transformer":
        encoder_config_keys.update(
            {
                "encoder_d_model": "d_model",
                "encoder_heads": "heads",
                "encoder_inducing_points": "inducing_points",
                "encoder_layers": "layers",
                "encoder_dropout": "dropout",
            }
        )
    for config_key, metadata_key in encoder_config_keys.items():
        if config_key not in config:
            raise ValueError(
                f"structured checkpoint encoder config is missing {config_key}"
            )
        if config[config_key] != encoder[metadata_key]:
            raise ValueError(
                f"structured checkpoint encoder config mismatch: {config_key}"
            )
    if "state_size" in config and int(config["state_size"]) != int(
        encoder.get("output_size", -1)
    ):
        raise ValueError("structured checkpoint encoder output_size/state_size mismatch")
    if _canonical(encoder.get("observation_shapes")) != _canonical(
        metadata.get("observation_shapes")
    ):
        raise ValueError("structured checkpoint encoder observation_shapes mismatch")
    if _canonical(protocol["observation_shapes"]) != _canonical(
        metadata.get("observation_shapes")
    ):
        raise ValueError("structured checkpoint protocol observation_shapes mismatch")
    if int(action["size"]) != int(metadata.get("action_size", -1)):
        raise ValueError("structured checkpoint protocol action_size mismatch")
    normalizer = payload["normalizer_state"]
    if not isinstance(normalizer, Mapping):
        raise TypeError("structured checkpoint normalizer_state must be a mapping")
    if normalizer.get("normalizer_type") != "identity":
        raise ValueError("structured checkpoint must use an identity normalizer")
    if normalizer.get("protocol_manifest_sha256") != protocol["manifest_sha256"]:
        raise ValueError("structured checkpoint identity normalizer protocol hash mismatch")


def validate_checkpoint_compatibility(
    payload: Mapping[str, Any],
    *,
    expected_observation_shapes: Any,
    expected_action_size: int,
    expected_protocol_metadata: Mapping[str, Any] | None,
    expected_encoder_metadata: Mapping[str, Any] | None,
) -> None:
    """Reject incompatible runtime contracts before state_dict loading."""
    if not isinstance(payload, Mapping):
        raise TypeError("checkpoint payload must be a mapping")
    required_payload = {
        "model_state",
        "optimizer_state",
        "normalizer_state",
        "config",
        "metadata",
    }
    if set(payload) != required_payload:
        raise ValueError("checkpoint payload fields do not match the schema")
    if not isinstance(payload.get("config"), Mapping):
        raise TypeError("checkpoint config must be a mapping")
    metadata = payload.get("metadata")
    if not isinstance(metadata, Mapping):
        raise TypeError("checkpoint metadata must be a mapping")
    if _canonical(metadata.get("observation_shapes")) != _canonical(
        expected_observation_shapes
    ):
        raise ValueError("checkpoint observation_shapes mismatch")
    if int(metadata.get("action_size", -1)) != int(expected_action_size):
        raise ValueError("checkpoint action_size mismatch")
    if expected_protocol_metadata is None:
        if metadata.get("protocol") is not None:
            raise ValueError("flat checkpoint unexpectedly contains protocol metadata")
    elif _canonical(metadata.get("protocol")) != _canonical(
        expected_protocol_metadata
    ):
        raise ValueError("checkpoint protocol metadata mismatch")
    if expected_encoder_metadata is None:
        return
    expected_encoder_type = expected_encoder_metadata.get("encoder_type")
    if payload["config"].get("encoder_type") != expected_encoder_type:
        raise ValueError("checkpoint encoder_type configuration mismatch")
    actual_encoder = metadata.get("encoder")
    if not isinstance(actual_encoder, Mapping):
        raise ValueError("checkpoint encoder metadata is missing")
    if actual_encoder.get("encoder_type") != expected_encoder_metadata.get(
        "encoder_type"
    ):
        raise ValueError("checkpoint encoder_type mismatch")
    if _canonical(actual_encoder) != _canonical(expected_encoder_metadata):
        raise ValueError("checkpoint encoder metadata mismatch")
    _validate_structured_metadata(payload)


@dataclass(frozen=True)
class FlowSolverConfig:
    method: str = "euler"
    direction: str = "0_to_1"
    nfe: int = 4
    action_transform: str = "tanh"

    def __post_init__(self) -> None:
        if self.method not in {"euler", "midpoint"}:
            raise ValueError("flow solver method must be 'euler' or 'midpoint'")
        if self.direction != "0_to_1":
            raise ValueError("flow solver direction must be '0_to_1'")
        if isinstance(self.nfe, bool) or not isinstance(self.nfe, int) or self.nfe <= 0:
            raise ValueError("flow solver nfe must be a positive integer")
        if self.action_transform not in {"tanh", "clamp"}:
            raise ValueError(
                "flow solver action_transform must be 'tanh' or 'clamp'"
            )

    def to_dict(self) -> dict[str, str | int]:
        return asdict(self)

    @classmethod
    def from_mapping(cls, raw: Mapping[str, Any]) -> "FlowSolverConfig":
        if not isinstance(raw, Mapping):
            raise TypeError("flow solver must be a mapping")
        expected = {"method", "direction", "nfe", "action_transform"}
        if set(raw) != expected:
            raise ValueError("flow solver fields do not match the required schema")
        return cls(
            method=raw["method"],
            direction=raw["direction"],
            nfe=raw["nfe"],
            action_transform=raw["action_transform"],
        )


def validate_flow_checkpoint(payload: Mapping[str, Any]) -> None:
    if not isinstance(payload, Mapping):
        raise TypeError("flow checkpoint payload must be a mapping")
    required_payload = {
        "model_state",
        "optimizer_state",
        "normalizer_state",
        "config",
        "metadata",
    }
    if set(payload) != required_payload:
        raise ValueError("flow checkpoint payload fields do not match the schema")
    config = payload["config"]
    metadata = payload["metadata"]
    if not isinstance(config, Mapping) or not isinstance(metadata, Mapping):
        raise TypeError("flow checkpoint config and metadata must be mappings")
    required_network = {
        "state_size",
        "time_embedding_size",
        "velocity_hidden_sizes",
    }
    missing_network = sorted(required_network - set(config))
    if missing_network:
        raise ValueError(
            f"flow checkpoint is missing network fields: {missing_network}"
        )
    for name in ("state_size", "time_embedding_size"):
        value = config[name]
        if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
            raise ValueError(f"flow checkpoint {name} must be a positive integer")
    hidden_sizes = config["velocity_hidden_sizes"]
    if not isinstance(hidden_sizes, (list, tuple)) or not hidden_sizes:
        raise ValueError("flow checkpoint velocity_hidden_sizes must be non-empty")
    if any(
        isinstance(size, bool) or not isinstance(size, int) or size <= 0
        for size in hidden_sizes
    ):
        raise ValueError("flow checkpoint velocity_hidden_sizes must be positive")
    required_metadata = {
        "schema_version",
        "observation_shapes",
        "action_size",
        "solver",
    }
    missing_metadata = sorted(required_metadata - set(metadata))
    if missing_metadata:
        raise ValueError(
            f"flow checkpoint is missing metadata fields: {missing_metadata}"
        )
    FlowSolverConfig.from_mapping(metadata["solver"])
    _validate_structured_metadata(payload)


def validate_flow_bc_checkpoint(payload: Mapping[str, Any]) -> None:
    """Validate a Flow BC checkpoint and its immutable data provenance."""
    validate_flow_checkpoint(payload)
    metadata = payload["metadata"]
    required = {
        "algorithm",
        "dataset_sha256",
        "metadata_sha256",
        "build_sha256",
    }
    missing = sorted(required - set(metadata))
    if missing:
        raise ValueError(f"Flow BC checkpoint is missing metadata fields: {missing}")
    if metadata["algorithm"] != "flow_bc":
        raise ValueError("Flow BC checkpoint algorithm must be 'flow_bc'")
    for name in ("dataset_sha256", "metadata_sha256", "build_sha256"):
        value = metadata[name]
        if not isinstance(value, str) or not value:
            raise ValueError(f"Flow BC checkpoint {name} must be a non-empty string")
    solver = FlowSolverConfig.from_mapping(metadata["solver"])
    if solver.action_transform != "clamp":
        raise ValueError("Flow BC checkpoint solver action_transform must be 'clamp'")


def validate_reinflow_checkpoint(payload: Mapping[str, Any]) -> None:
    """Validate an online ReinFlow checkpoint and its BC/source provenance."""
    validate_flow_checkpoint(payload)
    metadata = payload["metadata"]
    required = {
        "algorithm",
        "reinflow_source_commit",
        "bc_checkpoint_sha256",
        "build_sha256",
        "environment_steps",
        "optimizer_updates",
    }
    missing = sorted(required - set(metadata))
    if missing:
        raise ValueError(f"ReinFlow checkpoint is missing metadata fields: {missing}")
    if metadata["algorithm"] != "reinflow":
        raise ValueError("ReinFlow checkpoint algorithm must be 'reinflow'")
    for name in ("reinflow_source_commit", "bc_checkpoint_sha256", "build_sha256"):
        if not isinstance(metadata[name], str) or not metadata[name]:
            raise ValueError(f"ReinFlow checkpoint {name} must be a non-empty string")
    if FlowSolverConfig.from_mapping(metadata["solver"]).action_transform != "clamp":
        raise ValueError("ReinFlow checkpoint action_transform must be 'clamp'")


def validate_policyflow_checkpoint(payload: Mapping[str, Any]) -> None:
    """Validate PolicyFlow state and its fixed official source provenance."""
    validate_flow_checkpoint(payload)
    metadata = payload["metadata"]
    required = {
        "algorithm", "policyflow_source_commit",
        "normalizer_checkpoint_sha256", "build_sha256",
        "environment_steps", "optimizer_updates", "solver_steps",
        "rollout_boundary", "restart_boundary", "in_flight_decisions",
    }
    missing = sorted(required - set(metadata))
    if missing:
        raise ValueError(f"PolicyFlow checkpoint is missing metadata fields: {missing}")
    if metadata["algorithm"] != "policyflow":
        raise ValueError("PolicyFlow checkpoint algorithm must be 'policyflow'")
    if metadata["policyflow_source_commit"] != POLICYFLOW_SOURCE_COMMIT:
        raise ValueError("PolicyFlow checkpoint source commit is not the pinned official clone")
    for name in ("normalizer_checkpoint_sha256", "build_sha256"):
        if not isinstance(metadata[name], str) or not metadata[name]:
            raise ValueError(f"PolicyFlow checkpoint {name} must be a non-empty string")
    if metadata["rollout_boundary"] is not True:
        raise ValueError("PolicyFlow checkpoints must be saved at a clean rollout boundary")
    if metadata["restart_boundary"] is not True:
        raise ValueError("PolicyFlow checkpoints must declare restart-boundary semantics")
    if not isinstance(metadata["in_flight_decisions"], int) or metadata["in_flight_decisions"] < 0:
        raise ValueError("PolicyFlow checkpoint in_flight_decisions must be a nonnegative integer")
    solver = FlowSolverConfig.from_mapping(metadata["solver"])
    if solver.method != "midpoint" or solver.action_transform != "tanh":
        raise ValueError("PolicyFlow checkpoint must use midpoint/tanh solver metadata")
    if metadata["solver_steps"] * 2 != solver.nfe:
        raise ValueError("PolicyFlow solver_steps and velocity NFE are inconsistent")
    config = payload["config"]
    if int(config.get("solver_steps", -1)) != int(metadata["solver_steps"]):
        raise ValueError("PolicyFlow config and metadata solver_steps are inconsistent")
    if int(config.get("velocity_nfe", -1)) != int(solver.nfe):
        raise ValueError("PolicyFlow config and metadata velocity NFE are inconsistent")


def save_checkpoint(
    path: Path,
    *,
    model_state: Mapping[str, Any],
    optimizer_state: Mapping[str, Any] | None,
    normalizer_state: Mapping[str, Any] | None,
    config: Mapping[str, Any],
    metadata: Mapping[str, Any],
) -> None:
    """Atomically save all state required to resume a training run."""
    destination = Path(path)
    destination.parent.mkdir(parents=True, exist_ok=True)
    payload = {
        "model_state": dict(model_state),
        "optimizer_state": None if optimizer_state is None else dict(optimizer_state),
        "normalizer_state": None
        if normalizer_state is None
        else dict(normalizer_state),
        "config": dict(config),
        "metadata": dict(metadata),
    }
    temporary_path: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            dir=destination.parent,
            prefix=f".{destination.name}.",
            suffix=".tmp",
            delete=False,
        ) as handle:
            temporary_path = Path(handle.name)
        torch.save(payload, temporary_path)
        os.replace(temporary_path, destination)
        temporary_path = None
    finally:
        if temporary_path is not None:
            temporary_path.unlink(missing_ok=True)


def load_checkpoint(
    path: Path, *, map_location: str | torch.device = "cpu"
) -> dict[str, Any]:
    payload = torch.load(Path(path), map_location=map_location, weights_only=False)
    if not isinstance(payload, dict):
        raise ValueError("checkpoint payload must be a mapping")
    required = {
        "model_state",
        "optimizer_state",
        "normalizer_state",
        "config",
        "metadata",
    }
    if set(payload) != required:
        raise ValueError("checkpoint payload has unexpected keys")
    return payload
