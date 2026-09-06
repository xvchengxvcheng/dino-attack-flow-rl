from __future__ import annotations

import hashlib
from collections.abc import Mapping
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import torch
import yaml

from flow_rl.tracking.checkpoint import load_checkpoint, validate_flow_bc_checkpoint


@dataclass(frozen=True)
class ReinFlowTrainingConfig:
    bc_checkpoint_path: Path
    build_path: Path
    run_directory: Path
    total_environment_steps: int
    rollout_size: int
    batch_size: int
    epochs: int
    gamma: float
    gae_lambda: float
    actor_learning_rate: float
    critic_learning_rate: float
    clip_range: float
    entropy_coefficient: float
    max_gradient_norm: float
    critic_hidden_sizes: tuple[int, ...]
    noise_hidden_sizes: tuple[int, ...]
    nfe: int
    horizon_steps: int
    min_noise_std: float
    max_noise_std: float
    log_prob_min: float
    log_prob_max: float
    critic_warmup_environment_steps: int
    checkpoint_interval: int
    time_scale: float
    device: str
    worker_id: int
    evaluation_worker_id: int
    seed: int
    timeout_wait: int
    max_agent_absence_steps: int
    behavior_name: str | None
    resume_checkpoint: Path | None
    bc_checkpoint_sha256: str = field(init=False)
    observation_shapes: tuple[tuple[int, ...], ...] = field(init=False)
    action_size: int = field(init=False)
    state_size: int = field(init=False)
    time_embedding_size: int = field(init=False)
    velocity_hidden_sizes: tuple[int, ...] = field(init=False)

    @classmethod
    def from_yaml(cls, path: Path) -> "ReinFlowTrainingConfig":
        source = Path(path).resolve()
        with source.open(encoding="utf-8") as handle:
            raw = yaml.safe_load(handle)
        if not isinstance(raw, Mapping):
            raise TypeError("ReinFlow configuration must be a mapping")
        expected = {
            name for name, item in cls.__dataclass_fields__.items() if item.init
        }
        actual = set(raw)
        if actual != expected:
            raise ValueError(
                "ReinFlow configuration keys mismatch; "
                f"missing={sorted(expected - actual)}, extra={sorted(actual - expected)}"
            )
        values = dict(raw)
        base = source.parent
        for name in ("bc_checkpoint_path", "build_path", "run_directory"):
            values[name] = (base / Path(values[name])).resolve()
        if values["resume_checkpoint"] is not None:
            values["resume_checkpoint"] = (
                base / Path(values["resume_checkpoint"])
            ).resolve()
        for name in ("critic_hidden_sizes", "noise_hidden_sizes"):
            if not isinstance(values[name], list):
                raise TypeError(f"{name} must be a list")
            values[name] = tuple(values[name])
        config = cls(**values)
        if not config.bc_checkpoint_path.is_file():
            raise FileNotFoundError(
                f"Flow BC checkpoint does not exist: {config.bc_checkpoint_path}"
            )
        payload = load_checkpoint(config.bc_checkpoint_path, map_location="cpu")
        validate_flow_bc_checkpoint(payload)
        metadata = payload["metadata"]
        bc_config = payload["config"]
        object.__setattr__(config, "bc_checkpoint_sha256", _sha256(config.bc_checkpoint_path))
        object.__setattr__(
            config,
            "observation_shapes",
            tuple(tuple(int(value) for value in shape) for shape in metadata["observation_shapes"]),
        )
        object.__setattr__(config, "action_size", int(metadata["action_size"]))
        object.__setattr__(config, "state_size", int(bc_config["state_size"]))
        object.__setattr__(
            config, "time_embedding_size", int(bc_config["time_embedding_size"])
        )
        object.__setattr__(
            config,
            "velocity_hidden_sizes",
            tuple(int(size) for size in bc_config["velocity_hidden_sizes"]),
        )
        config.validate(bc_payload=payload)
        return config

    def validate(self, *, bc_payload: Mapping[str, Any] | None = None) -> None:
        if not self.build_path.is_file():
            raise FileNotFoundError(f"Unity build does not exist: {self.build_path}")
        if self.run_directory.is_dir() and any(self.run_directory.iterdir()):
            raise FileExistsError(f"run directory is not empty: {self.run_directory}")
        if self.resume_checkpoint is not None and not self.resume_checkpoint.is_file():
            raise FileNotFoundError(f"resume checkpoint does not exist: {self.resume_checkpoint}")
        if bc_payload is None:
            bc_payload = load_checkpoint(self.bc_checkpoint_path, map_location="cpu")
            validate_flow_bc_checkpoint(bc_payload)
        if bc_payload["metadata"]["build_sha256"] != _sha256(self.build_path):
            raise ValueError("Flow BC checkpoint Unity build hash does not match")
        if int(bc_payload["metadata"]["solver"]["nfe"]) != self.nfe:
            raise ValueError("ReinFlow NFE must match the Flow BC checkpoint NFE")
        counts = (
            "total_environment_steps", "rollout_size", "batch_size", "epochs",
            "checkpoint_interval", "timeout_wait",
        )
        for name in counts:
            value = getattr(self, name)
            if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
                raise ValueError(f"{name} must be a positive integer")
        if self.rollout_size < self.batch_size:
            raise ValueError("rollout_size must be at least batch_size")
        if self.horizon_steps != 1:
            raise ValueError("ReinFlow horizon_steps must remain 1 in phase 6")
        if self.nfe not in {1, 2, 4, 8}:
            raise ValueError("nfe must be one of 1, 2, 4, 8")
        if not 0.0 < self.min_noise_std < self.max_noise_std:
            raise ValueError("noise bounds must satisfy 0 < min < max")
        if not self.log_prob_min < self.log_prob_max:
            raise ValueError("log-prob bounds must satisfy min < max")
        if not 0.0 <= self.gamma <= 1.0 or not 0.0 <= self.gae_lambda <= 1.0:
            raise ValueError("gamma and gae_lambda must be within [0, 1]")
        if min(self.actor_learning_rate, self.critic_learning_rate) <= 0.0:
            raise ValueError("learning rates must be positive")
        if self.clip_range < 0.0 or self.entropy_coefficient < 0.0:
            raise ValueError("clip range and entropy coefficient must be nonnegative")
        if self.max_gradient_norm <= 0.0:
            raise ValueError("max_gradient_norm must be positive")
        if self.critic_warmup_environment_steps < 0:
            raise ValueError("critic warmup cannot be negative")
        for name in ("critic_hidden_sizes", "noise_hidden_sizes"):
            values = getattr(self, name)
            if not values or any(int(value) <= 0 for value in values):
                raise ValueError(f"{name} must contain positive integers")
        if self.time_scale <= 0.0:
            raise ValueError("time_scale must be positive")
        if self.device not in {"cpu", "cuda"}:
            raise ValueError("device must be 'cpu' or 'cuda'")
        if self.device == "cuda" and not torch.cuda.is_available():
            raise RuntimeError("device cuda requires torch.cuda.is_available()")
        if min(self.worker_id, self.evaluation_worker_id, self.seed) < 0:
            raise ValueError("worker IDs and seed cannot be negative")
        if self.worker_id == self.evaluation_worker_id:
            raise ValueError("training and evaluation worker IDs must differ")
        if self.max_agent_absence_steps < 0:
            raise ValueError("max_agent_absence_steps cannot be negative")
        if self.behavior_name is not None and not isinstance(self.behavior_name, str):
            raise TypeError("behavior_name must be a string or null")

    def to_dict(self) -> dict[str, Any]:
        result: dict[str, Any] = {
            name: getattr(self, name)
            for name, item in self.__dataclass_fields__.items()
            if item.init
        }
        for name in ("bc_checkpoint_path", "build_path", "run_directory"):
            result[name] = str(getattr(self, name))
        result["resume_checkpoint"] = (
            None if self.resume_checkpoint is None else str(self.resume_checkpoint)
        )
        result["critic_hidden_sizes"] = list(self.critic_hidden_sizes)
        result["noise_hidden_sizes"] = list(self.noise_hidden_sizes)
        result.update(
            {
                "bc_checkpoint_sha256": self.bc_checkpoint_sha256,
                "observation_shapes": [list(shape) for shape in self.observation_shapes],
                "action_size": self.action_size,
                "state_size": self.state_size,
                "time_embedding_size": self.time_embedding_size,
                "velocity_hidden_sizes": list(self.velocity_hidden_sizes),
            }
        )
        return result


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()
