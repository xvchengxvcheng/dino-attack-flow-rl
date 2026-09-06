"""Fail-closed configuration and checkpoints for parallel Dino PolicyFlow."""

from __future__ import annotations

import hashlib
import math
import random
from collections.abc import Mapping
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

import numpy as np
import torch
import yaml

from flow_rl.data.normalization import IdentityObservationNormalizer
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.state_encoder import StateEncoder
from flow_rl.tracking.checkpoint import (
    FlowSolverConfig,
    POLICYFLOW_SOURCE_COMMIT,
    STRUCTURED_CHECKPOINT_SCHEMA_VERSION,
    load_checkpoint,
    save_checkpoint,
    validate_checkpoint_compatibility,
    validate_flow_bc_checkpoint,
    validate_policyflow_checkpoint,
)
from flow_rl.training.dino_parallel_ppo_config import (
    DinoParallelEnvironmentSpec,
    DinoParallelResumeState,
)


@dataclass(frozen=True)
class DinoParallelPolicyFlowConfig:
    build_path: Path
    run_directory: Path
    protocol_path: Path
    actor_initialization_checkpoint_path: Path
    total_environment_steps: int
    rollout_size: int
    batch_size: int
    epochs: int
    gamma: float
    gae_lambda: float
    actor_learning_rate: float
    critic_learning_rate: float
    clip_range: float
    gaussian_entropy_coefficient: float
    brownian_coefficient: float
    value_clip: float
    max_gradient_norm: float
    state_size: int
    time_embedding_size: int
    velocity_hidden_sizes: tuple[int, ...]
    critic_hidden_sizes: tuple[int, ...]
    solver_steps: int
    horizon_steps: int
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
    inference_batch_size: int = 16
    inference_batch_wait_seconds: float = 0.0
    std_init: float = 1.0

    @classmethod
    def from_yaml(cls, path: Path) -> "DinoParallelPolicyFlowConfig":
        source = Path(path).resolve()
        raw = yaml.safe_load(source.read_text(encoding="utf-8"))
        if not isinstance(raw, Mapping):
            raise TypeError("Dino PolicyFlow configuration must be a mapping")
        expected = set(cls.__dataclass_fields__)
        optional = {
            "sampling_mode", "inference_batch_size",
            "inference_batch_wait_seconds",
            "std_init",
        }
        actual = set(raw)
        if not expected - optional <= actual or not actual <= expected:
            raise ValueError(
                "Dino PolicyFlow configuration keys mismatch; "
                f"missing={sorted((expected - optional) - actual)}, "
                f"extra={sorted(actual - expected)}"
            )
        values = dict(raw)
        values.setdefault("sampling_mode", "asynchronous")
        values.setdefault("inference_batch_size", 16)
        values.setdefault("inference_batch_wait_seconds", 0.0)
        values.setdefault("std_init", 1.0)
        base = source.parent
        for name in (
            "build_path", "run_directory", "protocol_path",
            "actor_initialization_checkpoint_path",
        ):
            values[name] = (base / Path(values[name])).resolve()
        if values["resume_checkpoint"] is not None:
            values["resume_checkpoint"] = (
                base / Path(values["resume_checkpoint"])
            ).resolve()
        for name in ("velocity_hidden_sizes", "critic_hidden_sizes"):
            if not isinstance(values[name], list):
                raise TypeError(f"{name} must be a list")
            values[name] = tuple(values[name])
        result = cls(**values)
        result.validate()
        return result

    @property
    def velocity_nfe(self) -> int:
        return 2 * self.solver_steps

    @property
    def environment_specs(self) -> tuple[DinoParallelEnvironmentSpec, ...]:
        return tuple(
            DinoParallelEnvironmentSpec(i, self.worker_base + i, self.seed + i)
            for i in range(self.num_envs)
        )

    @property
    def actor_initialization_checkpoint_sha256(self) -> str:
        return _sha256(self.actor_initialization_checkpoint_path)

    def validate(self) -> None:
        for path, label in (
            (self.build_path, "Unity build"),
            (self.protocol_path, "protocol"),
            (self.actor_initialization_checkpoint_path, "Flow BC checkpoint"),
        ):
            if not path.is_file():
                raise FileNotFoundError(f"{label} does not exist: {path}")
        if self.run_directory.exists() and not self.run_directory.is_dir():
            raise FileExistsError(f"run directory is not a directory: {self.run_directory}")
        if self.run_directory.is_dir() and any(self.run_directory.iterdir()):
            raise FileExistsError(f"run directory is not empty: {self.run_directory}")
        if self.resume_checkpoint is not None and not self.resume_checkpoint.is_file():
            raise FileNotFoundError(f"resume checkpoint does not exist: {self.resume_checkpoint}")
        counts = (
            self.total_environment_steps, self.rollout_size, self.batch_size,
            self.epochs, self.checkpoint_interval, self.timeout_wait,
            self.max_consecutive_failures, self.max_consecutive_no_progress,
        )
        if any(isinstance(v, bool) or not isinstance(v, int) or v <= 0 for v in counts):
            raise ValueError("step, batch, epoch and timeout counts must be positive")
        if self.total_environment_steps < self.rollout_size:
            raise ValueError("total_environment_steps must cover one rollout")
        if self.rollout_size < self.batch_size or self.batch_size != 512:
            raise ValueError("Dino PolicyFlow requires batch_size=512 within its rollout")
        if self.epochs != 2 or self.gamma != 0.995:
            raise ValueError("Dino PolicyFlow requires epochs=2 and gamma=0.995")
        if self.num_envs != 16 or self.inference_batch_size != 16:
            raise ValueError("Dino PolicyFlow requires 16 environments and batch size 16")
        if self.solver_steps != 2 or self.velocity_nfe != 4 or self.horizon_steps != 1:
            raise ValueError("Dino PolicyFlow requires midpoint solver_steps=2, NFE4, horizon=1")
        if not 0 <= self.gae_lambda <= 1:
            raise ValueError("gae_lambda must be within [0, 1]")
        if min(self.actor_learning_rate, self.critic_learning_rate, self.max_gradient_norm) <= 0:
            raise ValueError("learning rates and gradient norm must be positive")
        if not 0 <= self.clip_range < 1 or min(
            self.gaussian_entropy_coefficient,
            self.brownian_coefficient,
            self.value_clip,
        ) < 0:
            raise ValueError("PolicyFlow clip and loss coefficients are invalid")
        if not 0.0 < self.std_init <= 1.0:
            raise ValueError("Dino PolicyFlow std_init must be within (0, 1]")
        if (self.state_size, self.time_embedding_size) != (128, 32):
            raise ValueError("Dino PolicyFlow requires state_size=128 and time size=32")
        if self.velocity_hidden_sizes != (128, 128) or self.critic_hidden_sizes != (128, 128):
            raise ValueError("Dino PolicyFlow requires [128,128] actor/critic heads")
        if self.encoder_type != "set_transformer" or (
            self.encoder_d_model, self.encoder_heads, self.encoder_inducing_points,
            self.encoder_layers, self.encoder_dropout, self.encoder_output_size,
        ) != (48, 4, 8, 1, 0.0, 128):
            raise ValueError("Dino PolicyFlow requires Set Transformer 48/4/8/1/0/128")
        if self.normalize_observations:
            raise ValueError("structured Dino PolicyFlow requires identity normalization")
        if self.device != "cuda" or not torch.cuda.is_available():
            raise RuntimeError("Dino PolicyFlow requires CUDA")
        if self.time_scale != 1.0 or self.inference_batch_wait_seconds != 0.0:
            raise ValueError("FixedClockV5 PolicyFlow requires time_scale=1 and wait=0")
        if self.sampling_mode != "asynchronous":
            raise ValueError("Dino PolicyFlow requires asynchronous sampling")
        if self.worker_base < 0 or self.seed < 0 or self.poll_timeout <= 0:
            raise ValueError("worker IDs, seed and poll timeout are invalid")
        protocol = DinoProtocol.from_yaml(self.protocol_path)
        if protocol.protocol_version != "dino_attack_structured_set_v2":
            raise ValueError("Dino PolicyFlow requires structured protocol v2")
        payload = load_checkpoint(
            self.actor_initialization_checkpoint_path, map_location="cpu"
        )
        validate_flow_bc_checkpoint(payload)
        metadata = payload["metadata"]
        if metadata["build_sha256"] != _sha256(self.build_path):
            raise ValueError("Flow BC checkpoint build hash mismatch")
        if metadata["protocol"] != protocol.checkpoint_metadata():
            raise ValueError("Flow BC checkpoint protocol mismatch")
        expected = {
            "state_size": self.state_size,
            "time_embedding_size": self.time_embedding_size,
            "velocity_hidden_sizes": list(self.velocity_hidden_sizes),
            "encoder_type": self.encoder_type,
            "encoder_d_model": self.encoder_d_model,
            "encoder_heads": self.encoder_heads,
            "encoder_inducing_points": self.encoder_inducing_points,
            "encoder_layers": self.encoder_layers,
            "encoder_dropout": self.encoder_dropout,
            "encoder_output_size": self.encoder_output_size,
        }
        mismatch = [k for k, v in expected.items() if payload["config"].get(k) != v]
        if mismatch:
            raise ValueError(f"Flow BC network mismatch: {mismatch}")

    def to_dict(self) -> dict[str, Any]:
        result = asdict(self)
        for name in (
            "build_path", "run_directory", "protocol_path",
            "actor_initialization_checkpoint_path",
        ):
            result[name] = str(getattr(self, name))
        result["resume_checkpoint"] = (
            None if self.resume_checkpoint is None else str(self.resume_checkpoint)
        )
        result["velocity_hidden_sizes"] = list(self.velocity_hidden_sizes)
        result["critic_hidden_sizes"] = list(self.critic_hidden_sizes)
        result["velocity_nfe"] = self.velocity_nfe
        result["actor_initialization_checkpoint_sha256"] = (
            self.actor_initialization_checkpoint_sha256
        )
        return result


class DinoParallelPolicyFlowCheckpointManager:
    _STABLE = tuple(
        name for name in DinoParallelPolicyFlowConfig.__dataclass_fields__
        if name not in {"total_environment_steps", "run_directory", "resume_checkpoint"}
    )

    def __init__(self, *, config, protocol, policy, updater, normalizer,
                 action_generator, update_generator) -> None:
        self.config = config
        self.protocol = protocol
        self.policy = policy
        self.updater = updater
        self.normalizer = normalizer
        self.action_generator = action_generator
        self.update_generator = update_generator

    def _encoder_metadata(self) -> dict[str, Any]:
        encoder = self.policy.actor.state_encoder
        if not isinstance(encoder, StateEncoder):
            raise TypeError("Dino PolicyFlow actor requires a structured encoder")
        return encoder.checkpoint_metadata()

    def save(self, path: Path, *, environment_steps: int, policy_version: int,
             optimizer_updates: int, wall_clock_seconds: float) -> None:
        destination = Path(path).resolve()
        if not destination.is_relative_to(self.config.run_directory.resolve()):
            raise ValueError("checkpoint must be inside the run directory")
        if destination.exists():
            raise FileExistsError(f"checkpoint already exists: {destination}")
        if min(environment_steps, policy_version, optimizer_updates) <= 0:
            raise ValueError("checkpoint requires a completed update")
        if not math.isfinite(wall_clock_seconds) or wall_clock_seconds < 0:
            raise ValueError("wall clock must be finite and nonnegative")
        bc_sha = self.config.actor_initialization_checkpoint_sha256
        metadata = {
            "schema_version": STRUCTURED_CHECKPOINT_SCHEMA_VERSION,
            "algorithm": "policyflow",
            "policyflow_source_commit": POLICYFLOW_SOURCE_COMMIT,
            "normalizer_checkpoint_sha256": bc_sha,
            "actor_initialization_checkpoint_sha256": bc_sha,
            "build_sha256": _sha256(self.config.build_path),
            "environment_steps": environment_steps,
            "optimizer_updates": optimizer_updates,
            "policy_version": policy_version,
            "solver_steps": self.config.solver_steps,
            "solver": FlowSolverConfig(
                method="midpoint", nfe=self.config.velocity_nfe,
                action_transform="tanh",
            ).to_dict(),
            "rollout_boundary": True,
            "restart_boundary": True,
            "in_flight_decisions": 0,
            "wall_clock_seconds": float(wall_clock_seconds),
            "seed": self.config.seed,
            "num_envs": self.config.num_envs,
            "worker_base": self.config.worker_base,
            "observation_shapes": self.protocol.observation_shapes,
            "action_size": self.protocol.action_size,
            "protocol": self.protocol.checkpoint_metadata(),
            "encoder": self._encoder_metadata(),
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
        payload = load_checkpoint(path, map_location=self.config.device)
        validate_policyflow_checkpoint(payload)
        validate_checkpoint_compatibility(
            payload,
            expected_observation_shapes=self.protocol.observation_shapes,
            expected_action_size=self.protocol.action_size,
            expected_protocol_metadata=self.protocol.checkpoint_metadata(),
            expected_encoder_metadata=self._encoder_metadata(),
        )
        current = self.config.to_dict()
        mismatch = [k for k in self._STABLE if payload["config"].get(k) != current.get(k)]
        if payload["metadata"].get("build_sha256") != _sha256(self.config.build_path):
            mismatch.append("build_sha256")
        if mismatch:
            raise ValueError(f"resume checkpoint configuration mismatch: {mismatch}")
        self.policy.load_state_dict(payload["model_state"], strict=True)
        self.updater.actor_optimizer.load_state_dict(payload["optimizer_state"]["actor"])
        self.updater.critic_optimizer.load_state_dict(payload["optimizer_state"]["critic"])
        self.normalizer.load_state_dict(payload["normalizer_state"])
        metadata = payload["metadata"]
        random.setstate(metadata["python_random_state"])
        np.random.set_state(metadata["numpy_random_state"])
        torch.set_rng_state(metadata["torch_rng_state"].cpu())
        if torch.cuda.is_available() and metadata["cuda_rng_state"] is not None:
            torch.cuda.set_rng_state_all([x.cpu() for x in metadata["cuda_rng_state"]])
        self.action_generator.set_state(metadata["action_generator_state"].cpu())
        self.update_generator.set_state(metadata["update_generator_state"].cpu())
        return DinoParallelResumeState(
            int(metadata["environment_steps"]), int(metadata["policy_version"]),
            int(metadata["optimizer_updates"]), float(metadata["wall_clock_seconds"]),
        )


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()
