from __future__ import annotations

import importlib.util
from pathlib import Path

import pytest
import torch
import yaml

from flow_rl.algorithms.fpo import FPOUpdater
from flow_rl.data.normalization import IdentityObservationNormalizer
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.models.fpo_policy import FlowActorCritic
from flow_rl.training import dino_parallel_fpo_config


def test_parallel_fpo_config_module_is_available() -> None:
    assert importlib.util.find_spec(
        "flow_rl.training.dino_parallel_fpo_config"
    ) is not None


def test_parallel_fpo_config_type_is_available() -> None:
    assert hasattr(dino_parallel_fpo_config, "DinoParallelFPOConfig")


def test_parallel_fpo_checkpoint_manager_type_is_available() -> None:
    assert hasattr(dino_parallel_fpo_config, "DinoParallelFPOCheckpointManager")


ROOT = Path(__file__).resolve().parents[3]
PROTOCOL = ROOT / "flow_rl" / "configs" / "dino_attack_structured_set_v2.yaml"
PPO_FIXEDCLOCK_V5_REFERENCE = (
    ROOT
    / "flow_rl"
    / "configs"
    / "phase8_dino_ppo_fixedclock_v5_20env_seed0_timescale5_rollout20480_20260903.yaml"
)
FPO_FIXEDCLOCK_V5_SEED0 = (
    ROOT
    / "flow_rl"
    / "configs"
    / "phase8_dino_fpo_fixedclock_v5_16env_seed0_timescale1_rollout16384_20260904.yaml"
)
FPO_FIXEDCLOCK_V5_SMOKE = (
    ROOT
    / "flow_rl"
    / "configs"
    / "phase8_dino_fpo_fixedclock_v5_16env_seed0_smoke_20260904.yaml"
)
FPO_FIXEDCLOCK_V5_WARMSTART_25 = (
    ROOT
    / "flow_rl"
    / "configs"
    / "phase8_dino_fpo_fixedclock_v5_16env_seed0_warmstart_step786964_25updates_20260904.yaml"
)
FPO_FIXEDCLOCK_V5_WARMSTART_CONTINUE = (
    ROOT
    / "flow_rl"
    / "configs"
    / "phase8_dino_fpo_fixedclock_v5_16env_seed0_warmstart_step377087_continue_4718592_20260904.yaml"
)


def _raw(tmp_path: Path) -> dict[str, object]:
    build = tmp_path / "DinoAttackDualMapPostFix.exe"
    build.write_bytes(b"post-fix-player")
    return {
        "build_path": str(build),
        "run_directory": str(tmp_path / "run"),
        "protocol_path": str(PROTOCOL),
        "total_environment_steps": 1_572_864,
        "schedule_environment_steps": 1_572_864,
        "rollout_size": 16_384,
        "batch_size": 512,
        "epochs": 3,
        "gamma": 0.995,
        "gae_lambda": 0.95,
        "learning_rate": 3e-4,
        "final_learning_rate": 7.5e-5,
        "clip_range": 0.2,
        "final_clip_range": 0.15,
        "max_gradient_norm": 0.5,
        "state_size": 128,
        "time_embedding_size": 32,
        "velocity_hidden_sizes": [128, 128],
        "critic_hidden_sizes": [128, 128],
        "nfe": 4,
        "num_fpo_samples": 8,
        "difference_clip": 3.0,
        "positive_advantage": False,
        "checkpoint_interval": 131_072,
        "time_scale": 20.0,
        "device": "cuda",
        "num_envs": 24,
        "worker_base": 1400,
        "seed": 0,
        "timeout_wait": 120,
        "behavior_name": "DinoAttackPlanner",
        "max_consecutive_failures": 3,
        "poll_timeout": 180.0,
        "max_consecutive_no_progress": 3,
        "resume_checkpoint": None,
        "encoder_type": "set_transformer",
        "encoder_d_model": 48,
        "encoder_heads": 4,
        "encoder_inducing_points": 8,
        "encoder_layers": 1,
        "encoder_dropout": 0.0,
        "encoder_output_size": 128,
        "normalize_observations": False,
        "sampling_mode": "asynchronous",
        "inference_batch_size": 24,
        "inference_batch_wait_seconds": 0.002,
    }


def _write(tmp_path: Path, raw: dict[str, object]) -> Path:
    path = tmp_path / "config.yaml"
    path.write_text(yaml.safe_dump(raw, sort_keys=False), encoding="utf-8")
    return path


def test_formal_parallel_fpo_config_uses_approved_16gb_contract(tmp_path: Path) -> None:
    config = dino_parallel_fpo_config.DinoParallelFPOConfig.from_yaml(
        _write(tmp_path, _raw(tmp_path))
    )

    assert config.batch_size == 512
    assert config.num_fpo_samples == 8
    assert config.nfe == 4
    assert config.epochs == 3
    assert config.velocity_hidden_sizes == (128, 128)
    assert config.critic_hidden_sizes == (128, 128)
    assert config.encoder_d_model == 48
    assert config.encoder_layers == 1
    assert config.encoder_dropout == 0.0
    assert config.encoder_output_size == config.state_size == 128
    assert config.inference_batch_size == 24
    assert config.inference_batch_wait_seconds == pytest.approx(0.002)
    assert [item.worker_id for item in config.environment_specs] == list(range(1400, 1424))
    assert [item.environment_seed for item in config.environment_specs] == list(range(24))


def test_fixedclock_v5_fpo_seed0_matches_current_ppo_environment_contract() -> None:
    ppo = yaml.safe_load(PPO_FIXEDCLOCK_V5_REFERENCE.read_text(encoding="utf-8"))
    fpo = yaml.safe_load(FPO_FIXEDCLOCK_V5_SEED0.read_text(encoding="utf-8"))

    shared_fields = (
        "build_path",
        "protocol_path",
        "total_environment_steps",
        "schedule_environment_steps",
        "rollout_size",
        "batch_size",
        "gamma",
        "gae_lambda",
        "learning_rate",
        "final_learning_rate",
        "clip_range",
        "final_clip_range",
        "max_gradient_norm",
        "checkpoint_interval",
        "time_scale",
        "device",
        "num_envs",
        "seed",
        "timeout_wait",
        "behavior_name",
        "max_consecutive_failures",
        "poll_timeout",
        "max_consecutive_no_progress",
        "resume_checkpoint",
        "encoder_type",
        "encoder_d_model",
        "encoder_heads",
        "encoder_inducing_points",
        "encoder_layers",
        "encoder_dropout",
        "encoder_output_size",
        "normalize_observations",
        "sampling_mode",
        "inference_batch_size",
        "inference_batch_wait_seconds",
    )
    assert {name: fpo[name] for name in shared_fields} == {
        name: ppo[name] for name in shared_fields
    }
    assert fpo["epochs"] == 3
    assert fpo["nfe"] == 4
    assert fpo["num_fpo_samples"] == 8
    assert fpo["positive_advantage"] is False
    assert fpo["run_directory"] != ppo["run_directory"]
    assert fpo["worker_base"] != ppo["worker_base"]

    assert fpo["num_envs"] == 16
    assert fpo["time_scale"] == 1.0
    assert fpo["rollout_size"] == 16_384
    assert fpo["resume_checkpoint"] is None


def test_fixedclock_v5_fpo_smoke_is_one_formal_rollout() -> None:
    formal = yaml.safe_load(FPO_FIXEDCLOCK_V5_SEED0.read_text(encoding="utf-8"))
    smoke = yaml.safe_load(FPO_FIXEDCLOCK_V5_SMOKE.read_text(encoding="utf-8"))

    allowed_differences = {
        "run_directory",
        "total_environment_steps",
        "checkpoint_interval",
        "worker_base",
    }
    shared_fields = set(formal) - allowed_differences
    assert {name: smoke[name] for name in shared_fields} == {
        name: formal[name] for name in shared_fields
    }
    assert smoke["total_environment_steps"] == formal["rollout_size"] == 16_384
    assert smoke["checkpoint_interval"] == 16_384
    assert smoke["run_directory"] != formal["run_directory"]
    assert smoke["worker_base"] != formal["worker_base"]

    assert smoke["total_environment_steps"] == smoke["rollout_size"]


def test_fixedclock_v5_fpo_warmstart_uses_approved_stability_probe() -> None:
    raw = yaml.safe_load(FPO_FIXEDCLOCK_V5_WARMSTART_25.read_text(encoding="utf-8"))

    assert raw["total_environment_steps"] == 25 * raw["rollout_size"] == 409_600
    assert raw["schedule_environment_steps"] == 4_718_592
    assert raw["learning_rate"] == pytest.approx(1e-4)
    assert raw["final_learning_rate"] == pytest.approx(2.5e-5)
    assert raw["clip_range"] == pytest.approx(0.2)
    assert raw["final_clip_range"] == pytest.approx(0.15)
    assert raw["epochs"] == 2
    assert raw["num_fpo_samples"] == 16
    assert raw["difference_clip"] == pytest.approx(1.0)
    assert raw["checkpoint_interval"] == raw["rollout_size"]
    assert raw["resume_checkpoint"] is None
    assert raw["warm_start_checkpoint"].endswith("step-786964.pt")
    assert "target_proxy_kl" not in raw


def test_fixedclock_v5_fpo_continue_uses_curve_selected_weights_and_full_budget() -> None:
    raw = yaml.safe_load(
        FPO_FIXEDCLOCK_V5_WARMSTART_CONTINUE.read_text(encoding="utf-8")
    )

    assert raw["total_environment_steps"] == 4_718_592
    assert raw["schedule_environment_steps"] == 4_718_592
    assert raw["rollout_size"] == raw["checkpoint_interval"] == 16_384
    assert raw["num_envs"] == raw["inference_batch_size"] == 16
    assert raw["time_scale"] == pytest.approx(1.0)
    assert raw["learning_rate"] == pytest.approx(1e-4)
    assert raw["final_learning_rate"] == pytest.approx(2.5e-5)
    assert raw["clip_range"] == pytest.approx(0.2)
    assert raw["final_clip_range"] == pytest.approx(0.15)
    assert raw["epochs"] == 2
    assert raw["num_fpo_samples"] == 16
    assert raw["difference_clip"] == pytest.approx(1.0)
    assert raw["resume_checkpoint"] is None
    assert raw["warm_start_checkpoint"].endswith("step-377087.pt")
    assert "target_proxy_kl" not in raw



def test_parallel_fpo_config_allows_only_approved_memory_fallback(tmp_path: Path) -> None:
    fallback = _raw(tmp_path)
    fallback["batch_size"] = 256
    assert dino_parallel_fpo_config.DinoParallelFPOConfig.from_yaml(
        _write(tmp_path, fallback)
    ).batch_size == 256

    wrong = _raw(tmp_path)
    wrong["batch_size"] = 128
    with pytest.raises(ValueError, match="batch_size=512 or the approved 256 fallback"):
        dino_parallel_fpo_config.DinoParallelFPOConfig.from_yaml(
            _write(tmp_path, wrong)
        )


@pytest.mark.parametrize(
    ("field", "value", "message"),
    (
        ("num_fpo_samples", 32, "num_fpo_samples=8 or 16"),
        ("nfe", 2, "nfe=4"),
        ("epochs", 5, "epochs=2 or 3"),
        ("positive_advantage", True, "positive_advantage=false"),
        ("difference_clip", 2.0, "difference_clip=1.0 or 3.0"),
        ("encoder_d_model", 64, "d_model=48"),
        ("encoder_layers", 2, "layers=1"),
        ("encoder_dropout", 0.05, "dropout=0.0"),
        ("state_size", 256, "state_size=128"),
        ("velocity_hidden_sizes", [256, 256], "velocity_hidden_sizes"),
    ),
)
def test_parallel_fpo_config_fails_closed_on_algorithm_or_capacity_drift(
    tmp_path: Path, field: str, value: object, message: str
) -> None:
    raw = _raw(tmp_path)
    raw[field] = value

    with pytest.raises(ValueError, match=message):
        dino_parallel_fpo_config.DinoParallelFPOConfig.from_yaml(
            _write(tmp_path, raw)
        )


def test_fpo_checkpoint_manager_reads_velocity_state_encoder(tmp_path: Path) -> None:
    config = dino_parallel_fpo_config.DinoParallelFPOConfig.from_yaml(
        _write(tmp_path, _raw(tmp_path))
    )
    protocol = DinoProtocol.from_yaml(PROTOCOL)
    encoder_factory = lambda: SetTransformerDinoEncoder(
        protocol, d_model=48, heads=4, inducing_points=8, layers=1,
        dropout=0.0, output_size=128,
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
    generator = torch.Generator().manual_seed(1)
    updater = FPOUpdater(
        policy, learning_rate=3e-4, final_learning_rate=7.5e-5,
        clip_range=0.2, final_clip_range=0.15, max_gradient_norm=0.5,
        total_environment_steps=1_572_864, batch_size=512, epochs=3,
        difference_clip=3.0, positive_advantage=False, generator=generator,
    )
    manager = dino_parallel_fpo_config.DinoParallelFPOCheckpointManager(
        config=config,
        protocol=protocol,
        policy=policy,
        updater=updater,
        normalizer=IdentityObservationNormalizer(
            protocol.observation_shapes,
            protocol_manifest_sha256=protocol.manifest_sha256,
        ),
        action_generator=torch.Generator().manual_seed(2),
        update_generator=generator,
    )

    assert manager._encoder_metadata()["encoder_type"] == "set_transformer"


def test_fpo_checkpoint_manager_warm_starts_model_only(tmp_path: Path) -> None:
    source_raw = _raw(tmp_path)
    source_raw["run_directory"] = str(tmp_path / "source-run")
    source = dino_parallel_fpo_config.DinoParallelFPOConfig.from_yaml(
        _write(tmp_path, source_raw)
    )
    protocol = DinoProtocol.from_yaml(PROTOCOL)
    encoder_factory = lambda: SetTransformerDinoEncoder(
        protocol, d_model=48, heads=4, inducing_points=8, layers=1,
        dropout=0.0, output_size=128,
    )

    def build(config: dino_parallel_fpo_config.DinoParallelFPOConfig):
        policy = FlowActorCritic(
            observation_shapes=protocol.observation_shapes,
            action_size=protocol.action_size,
            state_size=128,
            time_embedding_size=32,
            velocity_hidden_sizes=(128, 128),
            critic_hidden_sizes=(128, 128),
            encoder_factory=encoder_factory,
        )
        generator = torch.Generator().manual_seed(1)
        updater = FPOUpdater(
            policy,
            learning_rate=config.learning_rate,
            final_learning_rate=config.final_learning_rate,
            clip_range=config.clip_range,
            final_clip_range=config.final_clip_range,
            max_gradient_norm=0.5,
            total_environment_steps=config.schedule_environment_steps,
            batch_size=512,
            epochs=config.epochs,
            difference_clip=config.difference_clip,
            positive_advantage=False,
            generator=generator,
        )
        manager = dino_parallel_fpo_config.DinoParallelFPOCheckpointManager(
            config=config,
            protocol=protocol,
            policy=policy,
            updater=updater,
            normalizer=IdentityObservationNormalizer(
                protocol.observation_shapes,
                protocol_manifest_sha256=protocol.manifest_sha256,
            ),
            action_generator=torch.Generator().manual_seed(2),
            update_generator=generator,
        )
        return policy, updater, manager

    source_policy, source_updater, source_manager = build(source)
    for parameter in source_policy.actor.parameters():
        parameter.grad = torch.ones_like(parameter)
    source_updater.actor_optimizer.step()
    for parameter in source_policy.critic.parameters():
        parameter.grad = torch.ones_like(parameter)
    source_updater.critic_optimizer.step()
    source.run_directory.mkdir()
    checkpoint = source.run_directory / "source.pt"
    source_manager.save(
        checkpoint,
        environment_steps=100,
        policy_version=2,
        optimizer_updates=2,
        wall_clock_seconds=1.0,
    )

    target_raw = _raw(tmp_path)
    target_raw.update(
        run_directory=str(tmp_path / "target-run"),
        epochs=2,
        learning_rate=1e-4,
        final_learning_rate=2.5e-5,
        num_fpo_samples=16,
        difference_clip=1.0,
        resume_checkpoint=None,
        warm_start_checkpoint=str(checkpoint),
    )
    target = dino_parallel_fpo_config.DinoParallelFPOConfig.from_yaml(
        _write(tmp_path, target_raw)
    )
    target_policy, target_updater, target_manager = build(target)
    assert not target_updater.actor_optimizer.state
    assert not target_updater.critic_optimizer.state

    source_identity = target_manager.warm_start(checkpoint)

    for name, value in source_policy.state_dict().items():
        assert torch.equal(target_policy.state_dict()[name], value)
    assert not target_updater.actor_optimizer.state
    assert not target_updater.critic_optimizer.state
    assert source_identity["path"] == str(checkpoint.resolve())
    assert len(source_identity["sha256"]) == 64


def test_parallel_fpo_config_rejects_resume_and_warm_start_together(
    tmp_path: Path,
) -> None:
    checkpoint = tmp_path / "checkpoint.pt"
    checkpoint.write_bytes(b"checkpoint")
    raw = _raw(tmp_path)
    raw["resume_checkpoint"] = str(checkpoint)
    raw["warm_start_checkpoint"] = str(checkpoint)

    with pytest.raises(ValueError, match="mutually exclusive"):
        dino_parallel_fpo_config.DinoParallelFPOConfig.from_yaml(
            _write(tmp_path, raw)
        )
