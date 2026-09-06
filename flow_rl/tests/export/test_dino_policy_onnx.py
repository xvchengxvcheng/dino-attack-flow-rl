from __future__ import annotations

import hashlib
import json
from copy import deepcopy
from pathlib import Path

import onnx
import numpy as np
import pytest
import torch

from flow_rl.data.normalization import IdentityObservationNormalizer
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.export.dino_policy_onnx import (
    ARTIFACT_KIND_FEASIBILITY,
    ARTIFACT_KIND_TRAINED,
    INPUT_NAMES,
    OUTPUT_NAME,
    export_feasibility_fixture,
    export_trained_checkpoint,
)
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.models.fpo_policy import FlowActorCritic
from flow_rl.models.policy import GaussianActorCritic
from flow_rl.tracking.checkpoint import (
    STRUCTURED_CHECKPOINT_SCHEMA_VERSION,
    save_checkpoint,
)
from flow_rl.training.config import PPOTrainingConfig


PROTOCOL_PATH = (
    Path(__file__).resolve().parents[2]
    / "configs"
    / "dino_attack_structured_set_v2.yaml"
)
EXPECTED_SHAPES = ((5,), (5, 8), (6, 5), (11, 7), (8, 6), (10, 7))


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _write_v2_checkpoint(
    path: Path,
    *,
    protocol_override: dict[str, object] | None = None,
    encoder_type: str = "set_transformer",
) -> None:
    protocol = DinoProtocol.from_yaml(PROTOCOL_PATH)

    def encoder_factory() -> SetTransformerDinoEncoder:
        return SetTransformerDinoEncoder(
            protocol,
            d_model=16,
            heads=2,
            inducing_points=4,
            layers=1,
            dropout=0.0,
            output_size=32,
        )

    policy = GaussianActorCritic(
        observation_shapes=protocol.observation_shapes,
        action_size=protocol.action_size,
        hidden_sizes=(32,),
        encoder_factory=encoder_factory,
    )
    protocol_metadata = (
        protocol.checkpoint_metadata()
        if protocol_override is None
        else protocol_override
    )
    metadata = {
        "schema_version": STRUCTURED_CHECKPOINT_SCHEMA_VERSION,
        "environment_steps": 128,
        "unity_steps": 132,
        "optimizer_updates": 1,
        "seed": 20260830,
        "worker_id": 0,
        "observation_shapes": protocol.observation_shapes,
        "action_size": protocol.action_size,
        "build_sha256": "1" * 64,
        "source_identity": "synthetic-v2-export-contract",
        "wall_clock_seconds": 1.25,
        "torch_rng_state": torch.get_rng_state(),
        "cuda_rng_state": None,
        "action_generator_state": torch.Generator().manual_seed(11).get_state(),
        "update_generator_state": torch.Generator().manual_seed(12).get_state(),
        "numpy_random_state": np.random.get_state(),
        "protocol": protocol_metadata,
        "encoder": policy.actor.encoder.checkpoint_metadata(),
    }
    normalizer = IdentityObservationNormalizer(
        protocol.observation_shapes,
        protocol_manifest_sha256=protocol.manifest_sha256,
    )
    config = PPOTrainingConfig(
        build_path=path.parent / "DinoAttack.exe",
        run_directory=path.parent / "run",
        total_environment_steps=4096,
        rollout_size=4096,
        batch_size=256,
        epochs=1,
        gamma=0.995,
        gae_lambda=0.95,
        learning_rate=0.0003,
        final_learning_rate=0.0003,
        clip_range=0.2,
        final_clip_range=0.2,
        entropy_coefficient=0.001,
        final_entropy_coefficient=0.001,
        value_coefficient=0.5,
        max_gradient_norm=0.5,
        hidden_sizes=(32,),
        checkpoint_interval=4096,
        time_scale=20.0,
        device="cpu",
        worker_id=0,
        evaluation_worker_id=1,
        seed=20260830,
        timeout_wait=120,
        max_agent_absence_steps=10,
        behavior_name="DinoAttackPlanner",
        resume_checkpoint=None,
        encoder_type=encoder_type,
        protocol_path=PROTOCOL_PATH,
        encoder_d_model=16,
        encoder_heads=2,
        encoder_inducing_points=4,
        encoder_layers=1,
        encoder_dropout=0.0,
        encoder_output_size=32,
        normalize_observations=False,
    )
    save_checkpoint(
        path,
        model_state=policy.state_dict(),
        optimizer_state={},
        normalizer_state=normalizer.state_dict(),
        config=config.to_dict(),
        metadata=metadata,
    )


def test_feasibility_fixture_exports_reproducible_six_input_action4_contract(
    tmp_path: Path,
) -> None:
    first = export_feasibility_fixture(
        output_path=tmp_path / "first.onnx",
        protocol_path=PROTOCOL_PATH,
        seed=20260830,
    )
    second = export_feasibility_fixture(
        output_path=tmp_path / "second.onnx",
        protocol_path=PROTOCOL_PATH,
        seed=20260830,
    )

    assert first.artifact_kind == ARTIFACT_KIND_FEASIBILITY
    assert first.model_sha256 == _sha256(first.model_path)
    assert first.model_sha256 == second.model_sha256

    model = onnx.load(first.model_path)
    onnx.checker.check_model(model)
    assert tuple(value.name for value in model.graph.input) == INPUT_NAMES
    assert tuple(value.name for value in model.graph.output) == (OUTPUT_NAME,)
    for value, expected_shape in zip(model.graph.input, EXPECTED_SHAPES):
        dimensions = value.type.tensor_type.shape.dim
        assert dimensions[0].dim_param == "batch"
        assert tuple(dimension.dim_value for dimension in dimensions[1:]) == expected_shape
    output_dimensions = model.graph.output[0].type.tensor_type.shape.dim
    assert output_dimensions[0].dim_param == "batch"
    assert output_dimensions[1].dim_value == 4

    zero_observations = tuple(
        torch.zeros((1, *shape), dtype=torch.float32)
        for shape in EXPECTED_SHAPES
    )
    with torch.inference_mode():
        actions = first.module(*zero_observations)
    assert tuple(actions.shape) == (1, 4)
    assert torch.isfinite(actions).all()
    assert torch.all(actions.abs() <= 1.0)

    manifest = json.loads(first.manifest_path.read_text(encoding="utf-8"))
    assert manifest["artifact_kind"] == ARTIFACT_KIND_FEASIBILITY
    assert manifest["protocol"]["version"] == "dino_attack_structured_set_v2"
    assert manifest["protocol"]["manifest_sha256"] == first.protocol_sha256
    assert manifest["model"]["sha256"] == first.model_sha256
    assert tuple(item["name"] for item in manifest["inputs"]) == INPUT_NAMES
    assert tuple(tuple(item["shape"]) for item in manifest["inputs"]) == EXPECTED_SHAPES
    assert manifest["output"] == {
        "name": OUTPUT_NAME,
        "shape": ["batch", 4],
        "dtype": "float32",
        "range": [-1.0, 1.0],
    }


def test_trained_checkpoint_exports_manifest_and_provenance(tmp_path: Path) -> None:
    checkpoint_path = tmp_path / "trained.pt"
    _write_v2_checkpoint(checkpoint_path)

    result = export_trained_checkpoint(
        output_path=tmp_path / "trained.onnx",
        protocol_path=PROTOCOL_PATH,
        checkpoint_path=checkpoint_path,
    )

    assert result.artifact_kind == ARTIFACT_KIND_TRAINED
    manifest = json.loads(result.manifest_path.read_text(encoding="utf-8"))
    assert manifest["artifact_kind"] == ARTIFACT_KIND_TRAINED
    assert manifest["provenance"]["seed"] is None
    assert manifest["provenance"]["checkpoint"]["file"] == str(
        checkpoint_path.resolve()
    )
    assert manifest["provenance"]["checkpoint"]["sha256"] == _sha256(
        checkpoint_path
    )
    assert (
        manifest["provenance"]["checkpoint"]["metadata"]["schema_version"]
        == STRUCTURED_CHECKPOINT_SCHEMA_VERSION
    )
    onnx.checker.check_model(onnx.load(result.model_path))


def test_fpo_checkpoint_exports_zero_latent_euler_ai_strategy(tmp_path: Path) -> None:
    protocol = DinoProtocol.from_yaml(PROTOCOL_PATH)

    def encoder_factory() -> SetTransformerDinoEncoder:
        return SetTransformerDinoEncoder(
            protocol,
            d_model=48,
            heads=4,
            inducing_points=8,
            layers=1,
            dropout=0.0,
            output_size=128,
        )

    policy = FlowActorCritic(
        observation_shapes=protocol.observation_shapes,
        action_size=protocol.action_size,
        state_size=128,
        time_embedding_size=32,
        velocity_hidden_sizes=(128, 128),
        critic_hidden_sizes=(128, 128),
        encoder_factory=encoder_factory,
    )
    checkpoint_path = tmp_path / "fpo.pt"
    save_checkpoint(
        checkpoint_path,
        model_state=policy.state_dict(),
        optimizer_state={"actor": {}, "critic": {}},
        normalizer_state=IdentityObservationNormalizer(
            protocol.observation_shapes,
            protocol_manifest_sha256=protocol.manifest_sha256,
        ).state_dict(),
        config={
            "protocol_path": str(PROTOCOL_PATH),
            "encoder_type": "set_transformer",
            "encoder_d_model": 48,
            "encoder_heads": 4,
            "encoder_inducing_points": 8,
            "encoder_layers": 1,
            "encoder_dropout": 0.0,
            "encoder_output_size": 128,
            "state_size": 128,
            "time_embedding_size": 32,
            "velocity_hidden_sizes": [128, 128],
            "critic_hidden_sizes": [128, 128],
            "nfe": 4,
        },
        metadata={
            "schema_version": STRUCTURED_CHECKPOINT_SCHEMA_VERSION,
            "algorithm": "dino_parallel_fpo",
            "environment_steps": 2_606_819,
            "policy_version": 159,
            "optimizer_updates": 159,
            "observation_shapes": protocol.observation_shapes,
            "action_size": protocol.action_size,
            "build_sha256": "2" * 64,
            "protocol": protocol.checkpoint_metadata(),
            "encoder": policy.actor.state_encoder.checkpoint_metadata(),
            "solver": {"name": "euler", "nfe": 4},
        },
    )

    result = export_trained_checkpoint(
        output_path=tmp_path / "fpo.onnx",
        protocol_path=PROTOCOL_PATH,
        checkpoint_path=checkpoint_path,
    )

    manifest = json.loads(result.manifest_path.read_text(encoding="utf-8"))
    assert manifest["model"]["deterministic_action"] == "zero_latent_euler_flow"
    assert manifest["model"]["nfe"] == 4
    assert manifest["provenance"]["checkpoint"]["metadata"]["algorithm"] == (
        "dino_parallel_fpo"
    )
    assert tuple(value.name for value in onnx.load(result.model_path).graph.input) == (
        INPUT_NAMES
    )
    zero_observations = tuple(
        torch.zeros((1, *shape), dtype=torch.float32) for shape in EXPECTED_SHAPES
    )
    with torch.inference_mode():
        actions = result.module(*zero_observations)
    assert actions.shape == (1, 4)
    assert torch.isfinite(actions).all()
    assert torch.all(actions.abs() <= 1.0)


def test_trained_checkpoint_rejects_wrong_protocol(tmp_path: Path) -> None:
    protocol = DinoProtocol.from_yaml(PROTOCOL_PATH)
    wrong_protocol = deepcopy(protocol.checkpoint_metadata())
    wrong_protocol["protocol_version"] = "dino_attack_structured_set_v1"
    checkpoint_path = tmp_path / "wrong-protocol.pt"
    _write_v2_checkpoint(checkpoint_path, protocol_override=wrong_protocol)

    with pytest.raises(ValueError, match="protocol metadata does not match"):
        export_trained_checkpoint(
            output_path=tmp_path / "wrong-protocol.onnx",
            protocol_path=PROTOCOL_PATH,
            checkpoint_path=checkpoint_path,
        )


def test_trained_checkpoint_rejects_non_set_transformer(tmp_path: Path) -> None:
    checkpoint_path = tmp_path / "wrong-encoder.pt"
    _write_v2_checkpoint(checkpoint_path, encoder_type="deep_sets")

    with pytest.raises(ValueError, match="must use the set_transformer encoder"):
        export_trained_checkpoint(
            output_path=tmp_path / "wrong-encoder.onnx",
            protocol_path=PROTOCOL_PATH,
            checkpoint_path=checkpoint_path,
        )
