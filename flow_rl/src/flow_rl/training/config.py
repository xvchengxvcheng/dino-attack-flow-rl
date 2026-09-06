from __future__ import annotations

from collections.abc import Mapping
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

import torch
import yaml


@dataclass(frozen=True)
class PPOTrainingConfig:
    build_path: Path
    run_directory: Path
    total_environment_steps: int
    rollout_size: int
    batch_size: int
    epochs: int
    gamma: float
    gae_lambda: float
    learning_rate: float
    final_learning_rate: float
    clip_range: float
    final_clip_range: float
    entropy_coefficient: float
    final_entropy_coefficient: float
    value_coefficient: float
    max_gradient_norm: float
    hidden_sizes: tuple[int, ...]
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
    encoder_type: str = "flat"
    protocol_path: Path | None = None
    encoder_d_model: int = 64
    encoder_heads: int = 4
    encoder_inducing_points: int = 8
    encoder_layers: int = 2
    encoder_dropout: float = 0.05
    encoder_output_size: int = 256
    normalize_observations: bool = True

    @classmethod
    def from_yaml(cls, path: Path) -> "PPOTrainingConfig":
        source_path = Path(path).resolve()
        with source_path.open(encoding="utf-8") as handle:
            raw = yaml.safe_load(handle)
        if not isinstance(raw, Mapping):
            raise TypeError("PPO configuration must be a mapping")
        optional_defaults = {
            "encoder_type": "flat",
            "protocol_path": None,
            "encoder_d_model": 64,
            "encoder_heads": 4,
            "encoder_inducing_points": 8,
            "encoder_layers": 2,
            "encoder_dropout": 0.05,
            "encoder_output_size": 256,
            "normalize_observations": True,
        }
        expected = set(cls.__dataclass_fields__)
        required = expected - set(optional_defaults)
        actual = set(raw)
        if not required <= actual or not actual <= expected:
            missing = sorted(required - actual)
            extra = sorted(actual - expected)
            raise ValueError(
                f"PPO configuration keys mismatch; missing={missing}, extra={extra}"
            )
        values = {**optional_defaults, **dict(raw)}
        base = source_path.parent
        values["build_path"] = (base / Path(values["build_path"])).resolve()
        values["run_directory"] = (
            base / Path(values["run_directory"])
        ).resolve()
        if values["resume_checkpoint"] is not None:
            values["resume_checkpoint"] = (
                base / Path(values["resume_checkpoint"])
            ).resolve()
        if values["protocol_path"] is not None:
            values["protocol_path"] = (
                base / Path(values["protocol_path"])
            ).resolve()
        hidden_sizes = values["hidden_sizes"]
        if not isinstance(hidden_sizes, list):
            raise TypeError("hidden_sizes must be a list")
        values["hidden_sizes"] = tuple(hidden_sizes)
        config = cls(**values)
        config.validate()
        return config

    def validate(self) -> None:
        if not self.build_path.is_file():
            raise FileNotFoundError(f"Unity build does not exist: {self.build_path}")
        if self.run_directory.is_dir() and any(self.run_directory.iterdir()):
            raise FileExistsError(
                f"run directory is not empty: {self.run_directory}"
            )
        if self.resume_checkpoint is not None and not self.resume_checkpoint.is_file():
            raise FileNotFoundError(
                f"resume checkpoint does not exist: {self.resume_checkpoint}"
            )
        positive_counts = {
            "total_environment_steps": self.total_environment_steps,
            "rollout_size": self.rollout_size,
            "batch_size": self.batch_size,
            "epochs": self.epochs,
            "checkpoint_interval": self.checkpoint_interval,
            "timeout_wait": self.timeout_wait,
        }
        if any(
            isinstance(value, bool) or not isinstance(value, int) or value <= 0
            for value in positive_counts.values()
        ):
            raise ValueError("step, rollout, batch, epoch, checkpoint, and timeout counts must be positive integers")
        if self.rollout_size < self.batch_size:
            raise ValueError("rollout_size must be at least batch_size")
        if not 0.0 <= self.gamma <= 1.0:
            raise ValueError("gamma must be within [0, 1]")
        if not 0.0 <= self.gae_lambda <= 1.0:
            raise ValueError("gae_lambda must be within [0, 1]")
        if self.learning_rate <= 0.0 or self.final_learning_rate < 0.0:
            raise ValueError("learning rates must be positive/nonnegative")
        if self.clip_range < 0.0 or self.final_clip_range < 0.0:
            raise ValueError("clip ranges cannot be negative")
        if (
            self.entropy_coefficient < 0.0
            or self.final_entropy_coefficient < 0.0
        ):
            raise ValueError("entropy coefficients cannot be negative")
        if self.value_coefficient < 0.0 or self.max_gradient_norm <= 0.0:
            raise ValueError("value coefficient and max gradient norm are invalid")
        if not self.hidden_sizes or any(
            isinstance(size, bool) or not isinstance(size, int) or size <= 0
            for size in self.hidden_sizes
        ):
            raise ValueError("hidden sizes must be positive integers")
        if self.time_scale <= 0.0:
            raise ValueError("time_scale must be positive")
        if self.device not in {"cpu", "cuda"}:
            raise ValueError("device must be 'cpu' or 'cuda'")
        if self.device == "cuda" and not torch.cuda.is_available():
            raise RuntimeError("device cuda requires torch.cuda.is_available()")
        if self.worker_id < 0 or self.evaluation_worker_id < 0:
            raise ValueError("worker IDs cannot be negative")
        if self.worker_id == self.evaluation_worker_id:
            raise ValueError("training and evaluation worker IDs must differ")
        if self.max_agent_absence_steps < 0:
            raise ValueError("max_agent_absence_steps cannot be negative")
        if self.behavior_name is not None and not isinstance(
            self.behavior_name, str
        ):
            raise TypeError("behavior_name must be a string or null")
        if self.encoder_type not in {"flat", "deep_sets", "set_transformer"}:
            raise ValueError("encoder_type must be flat, deep_sets, or set_transformer")
        encoder_counts = {
            "encoder_d_model": self.encoder_d_model,
            "encoder_heads": self.encoder_heads,
            "encoder_inducing_points": self.encoder_inducing_points,
            "encoder_layers": self.encoder_layers,
            "encoder_output_size": self.encoder_output_size,
        }
        if any(
            isinstance(value, bool) or not isinstance(value, int) or value <= 0
            for value in encoder_counts.values()
        ):
            raise ValueError("encoder sizes must be positive integers")
        if self.encoder_d_model % self.encoder_heads != 0:
            raise ValueError("encoder_d_model must be divisible by encoder_heads")
        if not 0.0 <= float(self.encoder_dropout) < 1.0:
            raise ValueError("encoder_dropout must be in [0, 1)")
        if not isinstance(self.normalize_observations, bool):
            raise TypeError("normalize_observations must be boolean")
        if self.encoder_type == "flat":
            if self.protocol_path is not None:
                raise ValueError("flat encoder must not set protocol_path")
            if not self.normalize_observations:
                raise ValueError("flat encoder requires normalize_observations=true")
        else:
            if self.protocol_path is None or not self.protocol_path.is_file():
                raise FileNotFoundError("structured encoder requires an existing protocol_path")
            if self.normalize_observations:
                raise ValueError("structured observations require normalize_observations=false")

    def to_dict(self) -> dict[str, Any]:
        serialized = asdict(self)
        serialized["build_path"] = str(self.build_path)
        serialized["run_directory"] = str(self.run_directory)
        serialized["resume_checkpoint"] = (
            None if self.resume_checkpoint is None else str(self.resume_checkpoint)
        )
        serialized["protocol_path"] = (
            None if self.protocol_path is None else str(self.protocol_path)
        )
        serialized["hidden_sizes"] = list(self.hidden_sizes)
        return serialized
