from __future__ import annotations

from pathlib import Path

import yaml
import hashlib
import torch
import pytest
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.data.normalization import IdentityObservationNormalizer

from flow_rl.training.dino_parallel_policyflow_config import (
    DinoParallelPolicyFlowConfig,
)


ROOT = Path(__file__).resolve().parents[3]
CONFIG = (
    ROOT
    / "flow_rl"
    / "configs"
    / "phase8_dino_policyflow_fixedclock_v5_16env_seed0_smoke_20260905.yaml"
)
STD01_CONFIG = (
    ROOT / "flow_rl" / "configs"
    / "phase8_dino_policyflow_fixedclock_v5_16env_seed0_smoke_std01_20260905.yaml"
)
FORMAL_CONFIG = (
    ROOT / "flow_rl" / "configs"
    / "phase8_dino_policyflow_fixedclock_v5_16env_seed0_4718592_std03_actorlr5e6_20260905.yaml"
)


def _load_with_unused_run(source: Path, tmp_path: Path) -> DinoParallelPolicyFlowConfig:
    raw = yaml.safe_load(source.read_text(encoding="utf-8"))
    for name in ("build_path", "protocol_path", "actor_initialization_checkpoint_path"):
        raw[name] = str((source.parent / raw[name]).resolve())
    # Generated metadata fixtures test the real validator without a local training run.
    build = tmp_path / (source.stem + "-player.exe")
    build.write_bytes(b"config-test-build; not an executable")
    raw["build_path"] = str(build)
    protocol = DinoProtocol.from_yaml(Path(raw["protocol_path"]))
    encoder = SetTransformerDinoEncoder(protocol)
    checkpoint = tmp_path / (source.stem + "-bc.pt")
    torch.save({
        "model_state": {}, "optimizer_state": {},
        "normalizer_state": IdentityObservationNormalizer(
            protocol.observation_shapes,
            protocol_manifest_sha256=protocol.manifest_sha256,
        ).state_dict(),
        "config": raw,
        "metadata": {
            "schema_version": 3, "algorithm": "flow_bc",
            "dataset_sha256": "unit-test-dataset", "metadata_sha256": "unit-test-metadata",
            "build_sha256": hashlib.sha256(build.read_bytes()).hexdigest(),
            "observation_shapes": protocol.observation_shapes, "action_size": protocol.action_size,
            "protocol": protocol.checkpoint_metadata(), "encoder": encoder.checkpoint_metadata(),
            "solver": {"method": "euler", "direction": "0_to_1", "nfe": 4,
                       "action_transform": "clamp"},
        },
    }, checkpoint)
    raw["actor_initialization_checkpoint_path"] = str(checkpoint)
    raw["run_directory"] = str(tmp_path / source.stem)
    copied = tmp_path / f"{source.stem}.yaml"
    copied.write_text(yaml.safe_dump(raw, sort_keys=False), encoding="utf-8")
    return DinoParallelPolicyFlowConfig.from_yaml(copied)


def test_dino_policyflow_smoke_config_is_one_versioned_rollout(tmp_path: Path) -> None:
    config = _load_with_unused_run(CONFIG, tmp_path)

    assert config.total_environment_steps == config.rollout_size == 16_384
    assert config.num_envs == config.inference_batch_size == 16
    assert config.batch_size == 512
    assert config.epochs == config.solver_steps == 2
    assert config.velocity_nfe == 4
    assert config.time_scale == 1.0
    assert config.inference_batch_wait_seconds == 0.0
    assert config.encoder_type == "set_transformer"
    assert config.encoder_output_size == config.state_size == 128
    assert config.actor_initialization_checkpoint_sha256 == (
        hashlib.sha256(config.actor_initialization_checkpoint_path.read_bytes()).hexdigest()
    )
    assert len(config.environment_specs) == 16
    assert len({item.worker_id for item in config.environment_specs}) == 16


def test_dino_policyflow_low_noise_smoke_starts_at_std_point_one(
    tmp_path: Path,
) -> None:
    assert _load_with_unused_run(STD01_CONFIG, tmp_path).std_init == 0.1


def test_formal_policyflow_uses_unified_ppo_fpo_budget_and_smoke_parameters(
    tmp_path: Path,
) -> None:
    config = _load_with_unused_run(FORMAL_CONFIG, tmp_path)

    assert config.total_environment_steps == 4_718_592
    assert config.rollout_size == config.checkpoint_interval == 16_384
    assert config.std_init == 0.3
    assert config.actor_learning_rate == 5e-6
    assert config.critic_learning_rate == 1e-4
    assert config.num_envs == config.inference_batch_size == 16


def test_generated_bc_fixture_still_rejects_wrong_player_hash(tmp_path: Path) -> None:
    config = _load_with_unused_run(CONFIG, tmp_path)
    config.build_path.write_bytes(b"different-build")
    with pytest.raises(ValueError, match="build hash mismatch"):
        config.validate()
