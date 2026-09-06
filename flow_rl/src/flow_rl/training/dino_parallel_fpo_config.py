"""Formal parallel Dino FPO configuration and checkpoint support."""

from __future__ import annotations

import hashlib
import math
import random
from collections.abc import Mapping
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

import torch
import yaml
import numpy as np

from flow_rl.data.normalization import IdentityObservationNormalizer
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.state_encoder import StateEncoder
from flow_rl.tracking.checkpoint import (
    STRUCTURED_CHECKPOINT_SCHEMA_VERSION,
    load_checkpoint,
    save_checkpoint,
    validate_checkpoint_compatibility,
)
from flow_rl.training.dino_parallel_ppo_config import (
    DinoParallelEnvironmentSpec,
    DinoParallelResumeState,
)
from flow_rl.training.fpo_trainer import FPO_SOURCE_COMMIT


_PROTOCOL_VERSION = "dino_attack_structured_set_v2"


@dataclass(frozen=True)
class DinoParallelFPOConfig:
    build_path: Path
    run_directory: Path
    protocol_path: Path
    total_environment_steps: int
    schedule_environment_steps: int
    rollout_size: int
    batch_size: int
    epochs: int
    gamma: float
    gae_lambda: float
    learning_rate: float
    final_learning_rate: float
    clip_range: float
    final_clip_range: float
    max_gradient_norm: float
    state_size: int
    time_embedding_size: int
    velocity_hidden_sizes: tuple[int, ...]
    critic_hidden_sizes: tuple[int, ...]
    nfe: int
    num_fpo_samples: int
    difference_clip: float
    positive_advantage: bool
    checkpoint_interval: int
    time_scale: float
    device: str
    num_envs: int
    worker_base: int
    seed: int
    timeout_wait: int
    behavior_name: str
    max_consecutive_failures: int
    poll_timeout: float
    max_consecutive_no_progress: int
    resume_checkpoint: Path | None
    encoder_type: str
    encoder_d_model: int
    encoder_heads: int
    encoder_inducing_points: int
    encoder_layers: int
    encoder_dropout: float
    encoder_output_size: int
    normalize_observations: bool
    sampling_mode: str = "asynchronous"
    inference_batch_size: int = 1
    inference_batch_wait_seconds: float = 0.0
    warm_start_checkpoint: Path | None = None

    @classmethod
    def from_yaml(cls, path: Path) -> "DinoParallelFPOConfig":
        source = Path(path).resolve()
        with source.open(encoding="utf-8") as handle:
            raw = yaml.safe_load(handle)
        if not isinstance(raw, Mapping):
            raise TypeError("Dino parallel FPO configuration must be a mapping")
        expected = set(cls.__dataclass_fields__)
        optional = {
            "sampling_mode",
            "inference_batch_size",
            "inference_batch_wait_seconds",
            "warm_start_checkpoint",
        }
        actual = set(raw)
        if not expected - optional <= actual or not actual <= expected:
            raise ValueError(
                "Dino parallel FPO configuration keys mismatch; "
                f"missing={sorted((expected - optional) - actual)}, "
                f"extra={sorted(actual - expected)}"
            )
        values = dict(raw)
        values.setdefault("sampling_mode", "asynchronous")
        values.setdefault("inference_batch_size", 1)
        values.setdefault("inference_batch_wait_seconds", 0.0)
        values.setdefault("warm_start_checkpoint", None)
        base = source.parent
        for name in ("build_path", "run_directory", "protocol_path"):
            values[name] = (base / Path(values[name])).resolve()
        for name in ("resume_checkpoint", "warm_start_checkpoint"):
            if values[name] is not None:
                values[name] = (base / Path(values[name])).resolve()
        for name in ("velocity_hidden_sizes", "critic_hidden_sizes"):
            sizes = values[name]
            if not isinstance(sizes, list):
                raise TypeError(f"{name} must be a list")
            values[name] = tuple(sizes)
        config = cls(**values)
        config.validate()
        return config

    @property
    def environment_specs(self) -> tuple[DinoParallelEnvironmentSpec, ...]:
        return tuple(
            DinoParallelEnvironmentSpec(
                environment_id=environment_id,
                worker_id=self.worker_base + environment_id,
                environment_seed=self.seed + environment_id,
            )
            for environment_id in range(self.num_envs)
        )

    def validate(self) -> None:
        if self.resume_checkpoint is not None and self.warm_start_checkpoint is not None:
            raise ValueError("resume_checkpoint and warm_start_checkpoint are mutually exclusive")
        if not self.build_path.is_file():
            raise FileNotFoundError(f"Unity build does not exist: {self.build_path}")
        if not self.protocol_path.is_file():
            raise FileNotFoundError(f"protocol does not exist: {self.protocol_path}")
        if self.run_directory.exists() and not self.run_directory.is_dir():
            raise FileExistsError(f"run directory is not a directory: {self.run_directory}")
        if self.run_directory.is_dir() and any(self.run_directory.iterdir()):
            detail = (
                "resume or warm start requires a new empty run directory"
                if self.resume_checkpoint is not None
                or self.warm_start_checkpoint is not None
                else "run directory is not empty"
            )
            raise FileExistsError(f"{detail}: {self.run_directory}")
        if self.resume_checkpoint is not None and not self.resume_checkpoint.is_file():
            raise FileNotFoundError(
                f"resume checkpoint does not exist: {self.resume_checkpoint}"
            )
        if (
            self.warm_start_checkpoint is not None
            and not self.warm_start_checkpoint.is_file()
        ):
            raise FileNotFoundError(
                "warm-start checkpoint does not exist: "
                f"{self.warm_start_checkpoint}"
            )

        counts = {
            "total_environment_steps": self.total_environment_steps,
            "schedule_environment_steps": self.schedule_environment_steps,
            "rollout_size": self.rollout_size,
            "batch_size": self.batch_size,
            "epochs": self.epochs,
            "state_size": self.state_size,
            "time_embedding_size": self.time_embedding_size,
            "num_fpo_samples": self.num_fpo_samples,
            "checkpoint_interval": self.checkpoint_interval,
            "timeout_wait": self.timeout_wait,
            "max_consecutive_failures": self.max_consecutive_failures,
            "max_consecutive_no_progress": self.max_consecutive_no_progress,
        }
        if any(
            isinstance(value, bool) or not isinstance(value, int) or value <= 0
            for value in counts.values()
        ):
            raise ValueError("step, batch, epoch, network and timeout counts must be positive")
        if self.schedule_environment_steps < self.total_environment_steps:
            raise ValueError("schedule_environment_steps must cover the stop target")
        if self.rollout_size < self.batch_size:
            raise ValueError("rollout_size must be at least batch_size")
        if self.batch_size not in (256, 512):
            raise ValueError("formal Dino FPO requires batch_size=512 or the approved 256 fallback")
        if self.epochs not in (2, 3):
            raise ValueError("formal Dino FPO requires epochs=2 or 3")
        if self.num_envs not in (4, 8, 12, 16, 24):
            raise ValueError("Dino parallel FPO requires 4, 8, 12, 16, or 24 environments")
        if self.worker_base < 0 or self.seed < 0:
            raise ValueError("worker_base and seed cannot be negative")
        if self.gamma != 0.995:
            raise ValueError("formal Dino FPO requires gamma=0.995")
        if not 0.0 <= self.gae_lambda <= 1.0:
            raise ValueError("gae_lambda must be within [0, 1]")
        for name in ("learning_rate", "clip_range", "max_gradient_norm"):
            if float(getattr(self, name)) <= 0.0:
                raise ValueError(f"{name} must be positive")
        for name in ("final_learning_rate", "final_clip_range"):
            if float(getattr(self, name)) < 0.0:
                raise ValueError(f"{name} cannot be negative")
        if self.state_size != 128:
            raise ValueError("formal Dino FPO requires state_size=128")
        if self.time_embedding_size != 32:
            raise ValueError("formal Dino FPO requires time_embedding_size=32")
        if self.velocity_hidden_sizes != (128, 128):
            raise ValueError("formal Dino FPO requires velocity_hidden_sizes=[128, 128]")
        if self.critic_hidden_sizes != (128, 128):
            raise ValueError("formal Dino FPO requires critic_hidden_sizes=[128, 128]")
        if self.nfe != 4:
            raise ValueError("formal Dino FPO requires nfe=4")
        if self.num_fpo_samples not in (8, 16):
            raise ValueError("formal Dino FPO requires num_fpo_samples=8 or 16")
        if self.difference_clip not in (1.0, 3.0):
            raise ValueError("formal Dino FPO requires difference_clip=1.0 or 3.0")
        if self.positive_advantage:
            raise ValueError("formal Dino FPO requires positive_advantage=false")
        if self.time_scale <= 0.0 or self.poll_timeout <= 0.0:
            raise ValueError("time_scale and poll_timeout must be positive")
        if self.device != "cuda":
            raise ValueError("formal Dino FPO requires device=cuda")
        if not torch.cuda.is_available():
            raise RuntimeError("formal Dino FPO requires CUDA")
        if not self.behavior_name:
            raise ValueError("behavior_name must be non-empty")
        if self.sampling_mode not in ("asynchronous", "synchronous"):
            raise ValueError("sampling_mode must be asynchronous or synchronous")
        if not 1 <= self.inference_batch_size <= self.num_envs:
            raise ValueError("inference_batch_size must be between 1 and num_envs")
        if not 0.0 <= self.inference_batch_wait_seconds <= 0.1:
            raise ValueError("inference_batch_wait_seconds must be within [0, 0.1]")

        if self.encoder_type != "set_transformer":
            raise ValueError("formal Dino FPO requires the Set Transformer encoder")
        if self.encoder_d_model != 48:
            raise ValueError("formal Dino FPO requires Set Transformer d_model=48")
        if self.encoder_heads != 4:
            raise ValueError("formal Dino FPO requires Set Transformer heads=4")
        if self.encoder_inducing_points != 8:
            raise ValueError("formal Dino FPO requires Set Transformer inducing_points=8")
        if self.encoder_layers != 1:
            raise ValueError("formal Dino FPO requires Set Transformer layers=1")
        if self.encoder_dropout != 0.0:
            raise ValueError("formal Dino FPO requires Set Transformer dropout=0.0")
        if self.encoder_output_size != 128:
            raise ValueError("formal Dino FPO requires Set Transformer output=128")
        if self.normalize_observations:
            raise ValueError("structured v2 requires an identity normalizer")
        protocol = DinoProtocol.from_yaml(self.protocol_path)
        if protocol.protocol_version != _PROTOCOL_VERSION:
            raise ValueError(
                f"formal protocol must be {_PROTOCOL_VERSION}, got {protocol.protocol_version}"
            )

    def to_dict(self) -> dict[str, Any]:
        result = asdict(self)
        for name in ("build_path", "run_directory", "protocol_path"):
            result[name] = str(getattr(self, name))
        result["resume_checkpoint"] = (
            None if self.resume_checkpoint is None else str(self.resume_checkpoint)
        )
        result["warm_start_checkpoint"] = (
            None
            if self.warm_start_checkpoint is None
            else str(self.warm_start_checkpoint)
        )
        result["velocity_hidden_sizes"] = list(self.velocity_hidden_sizes)
        result["critic_hidden_sizes"] = list(self.critic_hidden_sizes)
        return result


class DinoParallelFPOCheckpointManager:
    """Persist complete parallel FPO update boundaries for exact resumption."""

    _STABLE_CONFIG_KEYS = (
        "schedule_environment_steps", "rollout_size", "batch_size", "epochs",
        "gamma", "gae_lambda", "learning_rate", "final_learning_rate",
        "clip_range", "final_clip_range", "max_gradient_norm", "state_size",
        "time_embedding_size", "velocity_hidden_sizes", "critic_hidden_sizes",
        "nfe", "num_fpo_samples", "difference_clip", "positive_advantage",
        "time_scale", "device", "num_envs", "seed", "behavior_name",
        "encoder_type", "encoder_d_model", "encoder_heads",
        "encoder_inducing_points", "encoder_layers", "encoder_dropout",
        "encoder_output_size", "normalize_observations", "sampling_mode",
        "inference_batch_size", "inference_batch_wait_seconds",
    )

    def __init__(
        self,
        *,
        config: DinoParallelFPOConfig,
        protocol: DinoProtocol,
        policy: Any,
        updater: Any,
        normalizer: IdentityObservationNormalizer,
        action_generator: torch.Generator,
        update_generator: torch.Generator,
    ) -> None:
        self.config = config
        self.protocol = protocol
        self.policy = policy
        self.updater = updater
        self.normalizer = normalizer
        self.action_generator = action_generator
        self.update_generator = update_generator

    def save(
        self,
        path: Path,
        *,
        environment_steps: int,
        policy_version: int,
        optimizer_updates: int,
        wall_clock_seconds: float,
    ) -> None:
        destination = Path(path).resolve()
        if not destination.is_relative_to(self.config.run_directory.resolve()):
            raise ValueError("checkpoint destination must be inside the new run directory")
        if destination.exists():
            raise FileExistsError(f"checkpoint already exists: {destination}")
        if any(
            isinstance(value, bool) or not isinstance(value, int) or value <= 0
            for value in (environment_steps, policy_version, optimizer_updates)
        ):
            raise ValueError("a checkpoint requires at least one complete FPO update")
        if not math.isfinite(wall_clock_seconds) or wall_clock_seconds < 0.0:
            raise ValueError("wall_clock_seconds must be finite and nonnegative")
        resume_source = None
        if self.config.resume_checkpoint is not None:
            resume_source = {
                "path": str(self.config.resume_checkpoint.resolve()),
                "sha256": _sha256(self.config.resume_checkpoint),
            }
        warm_start_source = None
        if self.config.warm_start_checkpoint is not None:
            warm_start_source = {
                "path": str(self.config.warm_start_checkpoint.resolve()),
                "sha256": _sha256(self.config.warm_start_checkpoint),
            }
        metadata = {
            "schema_version": STRUCTURED_CHECKPOINT_SCHEMA_VERSION,
            "algorithm": "dino_parallel_fpo",
            "fpo_source_commit": FPO_SOURCE_COMMIT,
            "solver": {"name": "euler", "nfe": self.config.nfe},
            "rollout_boundary": True,
            "restart_boundary": True,
            "in_flight_decisions": 0,
            "environment_steps": environment_steps,
            "policy_version": policy_version,
            "optimizer_updates": optimizer_updates,
            "wall_clock_seconds": float(wall_clock_seconds),
            "seed": self.config.seed,
            "num_envs": self.config.num_envs,
            "worker_base": self.config.worker_base,
            "worker_ids": tuple(item.worker_id for item in self.config.environment_specs),
            "environment_seeds": tuple(
                item.environment_seed for item in self.config.environment_specs
            ),
            "observation_shapes": self.protocol.observation_shapes,
            "action_size": self.protocol.action_size,
            "build_path": str(self.config.build_path.resolve()),
            "build_sha256": _sha256(self.config.build_path),
            "source_identity": "workspace-without-head",
            "protocol": self.protocol.checkpoint_metadata(),
            "encoder": self._encoder_metadata(),
            "resume_source": resume_source,
            "warm_start_source": warm_start_source,
            "python_random_state": random.getstate(),
            "numpy_random_state": np.random.get_state(),
            "torch_rng_state": torch.get_rng_state(),
            "cuda_rng_state": torch.cuda.get_rng_state_all() if torch.cuda.is_available() else None,
            "action_generator_state": self.action_generator.get_state(),
            "update_generator_state": self.update_generator.get_state(),
        }
        save_checkpoint(
            destination,
            model_state=self.policy.state_dict(),
            optimizer_state={
                "actor": self.updater.actor_optimizer.state_dict(),
                "critic": self.updater.critic_optimizer.state_dict(),
            },
            normalizer_state=self.normalizer.state_dict(),
            config=self.config.to_dict(),
            metadata=metadata,
        )

    def restore(self, path: Path) -> DinoParallelResumeState:
        payload = load_checkpoint(Path(path).resolve(), map_location=self.config.device)
        validate_checkpoint_compatibility(
            payload,
            expected_observation_shapes=self.protocol.observation_shapes,
            expected_action_size=self.protocol.action_size,
            expected_protocol_metadata=self.protocol.checkpoint_metadata(),
            expected_encoder_metadata=self._encoder_metadata(),
        )
        metadata = payload["metadata"]
        checks = {
            "algorithm": metadata.get("algorithm") == "dino_parallel_fpo",
            "FPO source commit": metadata.get("fpo_source_commit") == FPO_SOURCE_COMMIT,
            "rollout boundary": metadata.get("rollout_boundary") is True,
            "restart boundary": metadata.get("restart_boundary") is True,
            "in-flight decisions": metadata.get("in_flight_decisions") == 0,
            "Unity build hash": metadata.get("build_sha256") == _sha256(self.config.build_path),
        }
        failed = [name for name, valid in checks.items() if not valid]
        if failed:
            raise ValueError(f"resume checkpoint mismatch: {sorted(failed)}")
        current = self.config.to_dict()
        mismatched = [
            key for key in self._STABLE_CONFIG_KEYS
            if payload["config"].get(key) != current.get(key)
        ]
        if mismatched:
            raise ValueError(f"resume checkpoint configuration mismatch: {sorted(mismatched)}")
        steps = _positive_checkpoint_int(metadata, "environment_steps")
        version = _positive_checkpoint_int(metadata, "policy_version")
        updates = _positive_checkpoint_int(metadata, "optimizer_updates")
        if self.config.total_environment_steps <= steps:
            raise ValueError("resume target must exceed checkpoint steps")
        elapsed = float(metadata.get("wall_clock_seconds", -1.0))
        if not math.isfinite(elapsed) or elapsed < 0.0:
            raise ValueError("resume checkpoint wall clock is invalid")
        self.policy.load_state_dict(payload["model_state"], strict=True)
        self.updater.actor_optimizer.load_state_dict(payload["optimizer_state"]["actor"])
        self.updater.critic_optimizer.load_state_dict(payload["optimizer_state"]["critic"])
        self.normalizer.load_state_dict(payload["normalizer_state"])
        random.setstate(metadata["python_random_state"])
        np.random.set_state(metadata["numpy_random_state"])
        torch.set_rng_state(metadata["torch_rng_state"].cpu())
        if torch.cuda.is_available() and metadata["cuda_rng_state"] is not None:
            torch.cuda.set_rng_state_all([item.cpu() for item in metadata["cuda_rng_state"]])
        self.action_generator.set_state(metadata["action_generator_state"].cpu())
        self.update_generator.set_state(metadata["update_generator_state"].cpu())
        return DinoParallelResumeState(steps, version, updates, elapsed)

    def warm_start(self, path: Path) -> dict[str, str]:
        """Load policy weights without restoring optimizer, RNG, or run counters."""

        source = Path(path).resolve()
        payload = load_checkpoint(source, map_location=self.config.device)
        validate_checkpoint_compatibility(
            payload,
            expected_observation_shapes=self.protocol.observation_shapes,
            expected_action_size=self.protocol.action_size,
            expected_protocol_metadata=self.protocol.checkpoint_metadata(),
            expected_encoder_metadata=self._encoder_metadata(),
        )
        metadata = payload["metadata"]
        checks = {
            "algorithm": metadata.get("algorithm") == "dino_parallel_fpo",
            "FPO source commit": metadata.get("fpo_source_commit") == FPO_SOURCE_COMMIT,
            "rollout boundary": metadata.get("rollout_boundary") is True,
            "restart boundary": metadata.get("restart_boundary") is True,
            "in-flight decisions": metadata.get("in_flight_decisions") == 0,
            "Unity build hash": metadata.get("build_sha256")
            == _sha256(self.config.build_path),
        }
        failed = [name for name, valid in checks.items() if not valid]
        if failed:
            raise ValueError(f"warm-start checkpoint mismatch: {sorted(failed)}")
        self.policy.load_state_dict(payload["model_state"], strict=True)
        return {"path": str(source), "sha256": _sha256(source)}

    def _encoder_metadata(self) -> dict[str, Any]:
        encoder = self.policy.actor.state_encoder
        if not isinstance(encoder, StateEncoder):
            raise TypeError("Dino parallel FPO requires a structured actor encoder")
        return encoder.checkpoint_metadata()


def _positive_checkpoint_int(metadata: Mapping[str, Any], name: str) -> int:
    value = metadata.get(name)
    if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
        raise ValueError(f"resume checkpoint {name} is invalid")
    return value


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()
