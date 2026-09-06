from __future__ import annotations

from copy import deepcopy
from pathlib import Path

import pytest
import torch
import yaml

from flow_rl.training.dino_parallel_ppo_config import DinoParallelPPOConfig


ROOT = Path(__file__).resolve().parents[3]
V2_PROTOCOL = ROOT / "flow_rl" / "configs" / "dino_attack_structured_set_v2.yaml"
V1_PROTOCOL = ROOT / "flow_rl" / "configs" / "dino_attack_structured_set_v1.yaml"
FORMAL_8_ENV_CONFIG = (
    ROOT
    / "flow_rl"
    / "configs"
    / "phase8_dino_ppo_runtimefix_tuned_8env_seed0_fresh_20260902.yaml"
)
FORMAL_4_ENV_CONFIG = (
    ROOT
    / "flow_rl"
    / "configs"
    / "phase8_dino_ppo_runtimefix_tuned_4env_seed0_fresh_20260902.yaml"
)
FORMAL_20_ENV_CONFIG = (
    ROOT
    / "flow_rl"
    / "configs"
    / "phase8_dino_ppo_fixedclock_v5_20env_seed0_timescale5_rollout20480_20260903.yaml"
)


def _raw(tmp_path: Path) -> dict[str, object]:
    build = tmp_path / "DinoAttackDualMapTask8.exe"
    build.write_bytes(b"frozen-dual-map-player")
    return {
        "build_path": str(build),
        "run_directory": str(tmp_path / "run"),
        "protocol_path": str(V2_PROTOCOL),
        "total_environment_steps": 40_960,
        "schedule_environment_steps": 65_536,
        "rollout_size": 4_096,
        "batch_size": 256,
        "epochs": 4,
        "gamma": 0.995,
        "gae_lambda": 0.95,
        "learning_rate": 3e-4,
        "final_learning_rate": 3e-4,
        "clip_range": 0.2,
        "final_clip_range": 0.2,
        "entropy_coefficient": 0.001,
        "final_entropy_coefficient": 0.001,
        "value_coefficient": 0.5,
        "max_gradient_norm": 0.5,
        "hidden_sizes": [128, 128],
        "checkpoint_interval": 20_480,
        "time_scale": 20.0,
        "device": "cuda",
        "num_envs": 4,
        "worker_base": 160,
        "seed": 0,
        "timeout_wait": 120,
        "behavior_name": "DinoAttackPlanner",
        "max_consecutive_failures": 3,
        "poll_timeout": 120.0,
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
    }


def _write(path: Path, raw: dict[str, object]) -> Path:
    path.write_text(yaml.safe_dump(raw, sort_keys=False), encoding="utf-8")
    return path


def test_formal_post_victory_guard_seed0_uses_fresh_eight_environment_contract() -> None:
    raw = yaml.safe_load(FORMAL_8_ENV_CONFIG.read_text(encoding="utf-8"))

    assert raw["num_envs"] == 8
    assert raw["inference_batch_size"] == 8
    assert raw["inference_batch_wait_seconds"] == 0.0
    assert raw["total_environment_steps"] == 1_572_864
    assert raw["schedule_environment_steps"] == 1_572_864
    assert raw["rollout_size"] == 16_384
    assert raw["batch_size"] == 512
    assert raw["epochs"] == 5
    assert raw["seed"] == 0
    assert raw["resume_checkpoint"] is None
    assert "build-runtimefix-20260902" in raw["build_path"]
    assert "8env-seed0-fresh" in raw["run_directory"]


def test_replacement_post_victory_guard_seed0_uses_fresh_four_environment_contract() -> None:
    raw = yaml.safe_load(FORMAL_4_ENV_CONFIG.read_text(encoding="utf-8"))

    assert raw["num_envs"] == 4
    assert raw["inference_batch_size"] == 4
    assert raw["inference_batch_wait_seconds"] == 0.0
    assert raw["total_environment_steps"] == 1_572_864
    assert raw["schedule_environment_steps"] == 1_572_864
    assert raw["rollout_size"] == 16_384
    assert raw["batch_size"] == 512
    assert raw["epochs"] == 5
    assert raw["seed"] == 0
    assert raw["resume_checkpoint"] is None
    assert raw["worker_base"] == 1900
    assert "build-runtimefix-20260902" in raw["build_path"]
    assert "4env-seed0-fresh" in raw["run_directory"]


def test_fixed_clock_v5_seed0_uses_current_sixteen_environment_contract() -> None:
    raw = yaml.safe_load(FORMAL_20_ENV_CONFIG.read_text(encoding="utf-8"))

    assert raw["num_envs"] == 16
    assert raw["inference_batch_size"] == 16
    assert raw["inference_batch_wait_seconds"] == 0.0
    assert raw["time_scale"] == 1.0
    assert raw["total_environment_steps"] == 4_718_592
    assert raw["schedule_environment_steps"] == 4_718_592
    assert raw["rollout_size"] == 16_384
    assert raw["rollout_size"] // raw["num_envs"] == 1_024
    assert raw["batch_size"] == 512
    assert raw["epochs"] == 5
    assert raw["checkpoint_interval"] == 131_072
    assert raw["seed"] == 0
    assert raw["resume_checkpoint"] is None
    assert "build-fixed-clock-v5-20260903" in raw["build_path"]
    assert "16env-seed0-timescale5-rollout20480" in raw["run_directory"]


@pytest.mark.parametrize("environment_count", (8, 12, 16, 20, 24))
def test_config_supports_extended_environment_v2_set_transformer_runs(
    tmp_path: Path,
    environment_count: int,
) -> None:
    raw = _raw(tmp_path)
    raw["num_envs"] = environment_count
    config = DinoParallelPPOConfig.from_yaml(_write(tmp_path / "valid.yaml", raw))

    assert config.num_envs == environment_count
    assert config.schedule_environment_steps == 65_536
    assert config.gamma == 0.995
    assert config.encoder_type == "set_transformer"
    assert config.encoder_d_model == 48
    assert config.encoder_heads == 4
    assert config.encoder_inducing_points == 8
    assert config.encoder_layers == 1
    assert config.encoder_dropout == 0.0
    assert config.encoder_output_size == 128
    assert config.hidden_sizes == (128, 128)
    assert [item.worker_id for item in config.environment_specs] == list(
        range(160, 160 + environment_count)
    )
    assert [item.environment_seed for item in config.environment_specs] == list(
        range(environment_count)
    )
    assert len({item.worker_id for item in config.environment_specs}) == environment_count
    assert len({item.environment_seed for item in config.environment_specs}) == environment_count


def test_config_accepts_explicit_synchronous_sampling(tmp_path: Path) -> None:
    raw = _raw(tmp_path)
    raw["sampling_mode"] = "synchronous"

    config = DinoParallelPPOConfig.from_yaml(_write(tmp_path / "synchronous.yaml", raw))

    assert config.sampling_mode == "synchronous"


def test_config_accepts_dynamic_gpu_inference_batching(tmp_path: Path) -> None:
    raw = _raw(tmp_path)
    raw["num_envs"] = 24
    raw["inference_batch_size"] = 24
    raw["inference_batch_wait_seconds"] = 0.002

    config = DinoParallelPPOConfig.from_yaml(_write(tmp_path / "microbatch.yaml", raw))

    assert config.inference_batch_size == 24
    assert config.inference_batch_wait_seconds == pytest.approx(0.002)


@pytest.mark.parametrize(
    ("field", "value"),
    (
        ("inference_batch_size", 0),
        ("inference_batch_size", 25),
        ("inference_batch_wait_seconds", -0.001),
        ("inference_batch_wait_seconds", 0.5),
    ),
)
def test_config_rejects_invalid_dynamic_inference_batching(
    tmp_path: Path, field: str, value: object
) -> None:
    raw = _raw(tmp_path)
    raw[field] = value

    with pytest.raises(ValueError, match="inference_batch"):
        DinoParallelPPOConfig.from_yaml(_write(tmp_path / "bad-microbatch.yaml", raw))


def test_config_rejects_unknown_sampling_mode(tmp_path: Path) -> None:
    raw = _raw(tmp_path)
    raw["sampling_mode"] = "sometimes"

    with pytest.raises(ValueError, match="sampling_mode"):
        DinoParallelPPOConfig.from_yaml(_write(tmp_path / "bad-sampling.yaml", raw))


@pytest.mark.parametrize(
    ("field", "value", "message"),
    (
        ("num_envs", 3, "four, eight, twelve, sixteen, twenty, or twenty-four"),
        ("gamma", 0.99, "gamma=0.995"),
        ("encoder_type", "deep_sets", "Set Transformer"),
        ("hidden_sizes", [256, 256], r"hidden_sizes=\[128, 128\]"),
        ("encoder_d_model", 64, "d_model=48"),
        ("encoder_heads", 2, "heads=4"),
        ("encoder_inducing_points", 4, "inducing_points=8"),
        ("encoder_layers", 2, "layers=1"),
        ("encoder_dropout", 0.05, "dropout=0.0"),
        ("encoder_output_size", 256, "output=128"),
        ("normalize_observations", True, "identity normalizer"),
    ),
)
def test_config_fails_closed_on_formal_contract_drift(
    tmp_path: Path,
    field: str,
    value: object,
    message: str,
) -> None:
    raw = _raw(tmp_path)
    raw[field] = value

    with pytest.raises(ValueError, match=message):
        DinoParallelPPOConfig.from_yaml(_write(tmp_path / f"{field}.yaml", raw))


def test_config_rejects_v1_protocol_and_nonempty_run_directory(tmp_path: Path) -> None:
    wrong_protocol = _raw(tmp_path)
    wrong_protocol["protocol_path"] = str(V1_PROTOCOL)
    with pytest.raises(ValueError, match="structured_set_v2"):
        DinoParallelPPOConfig.from_yaml(
            _write(tmp_path / "wrong-protocol.yaml", wrong_protocol)
        )

    nonempty = _raw(tmp_path)
    run_directory = Path(str(nonempty["run_directory"]))
    run_directory.mkdir()
    (run_directory / "existing.txt").write_text("preserve", encoding="utf-8")
    with pytest.raises(FileExistsError, match="not empty"):
        DinoParallelPPOConfig.from_yaml(_write(tmp_path / "nonempty.yaml", nonempty))


def test_resume_requires_new_directory_and_target_above_absolute_checkpoint_steps(
    tmp_path: Path,
) -> None:
    raw = _raw(tmp_path)
    source_run = tmp_path / "source"
    source_run.mkdir()
    checkpoint = source_run / "step-4096.pt"
    torch.save(
        {
            "model_state": {},
            "optimizer_state": {},
            "normalizer_state": {},
            "config": {},
            "metadata": {
                "environment_steps": 4_096,
                "protocol": {"protocol_version": "dino_attack_structured_set_v2"},
            },
        },
        checkpoint,
    )
    raw["resume_checkpoint"] = str(checkpoint)
    raw["total_environment_steps"] = 4_096

    with pytest.raises(ValueError, match="must exceed checkpoint steps"):
        DinoParallelPPOConfig.from_yaml(_write(tmp_path / "resume-equal.yaml", raw))

    changed = deepcopy(raw)
    changed["total_environment_steps"] = 8_192
    changed["run_directory"] = str(source_run)
    with pytest.raises(FileExistsError, match="new empty run directory"):
        DinoParallelPPOConfig.from_yaml(_write(tmp_path / "resume-source.yaml", changed))
