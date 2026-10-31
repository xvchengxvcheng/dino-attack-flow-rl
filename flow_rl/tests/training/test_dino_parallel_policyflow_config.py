from __future__ import annotations

from pathlib import Path

import yaml

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
        "667fb23bcaeeba65db904cfcae42aafb6e68a8f2c4781ebf224edf95a6401d38"
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
