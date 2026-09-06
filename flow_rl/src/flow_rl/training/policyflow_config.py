from __future__ import annotations

import hashlib
from collections.abc import Mapping
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import torch
import yaml

from flow_rl.tracking.checkpoint import (
    load_checkpoint,
    validate_flow_bc_checkpoint,
)


@dataclass(frozen=True)
class PolicyFlowTrainingConfig:
    normalizer_checkpoint_path: Path
    actor_initialization_checkpoint_path: Path | None
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
    worker_id: int
    evaluation_worker_id: int
    seed: int
    timeout_wait: int
    max_agent_absence_steps: int
    behavior_name: str | None
    resume_checkpoint: Path | None
    normalizer_checkpoint_sha256: str = field(init=False)
    actor_initialization_checkpoint_sha256: str | None = field(init=False)
    observation_shapes: tuple[tuple[int, ...], ...] = field(init=False)
    action_size: int = field(init=False)

    @property
    def velocity_nfe(self) -> int:
        return 2 * self.solver_steps

    @classmethod
    def from_yaml(cls, path: Path) -> "PolicyFlowTrainingConfig":
        source = Path(path).resolve()
        with source.open(encoding="utf-8") as handle:
            raw = yaml.safe_load(handle)
        if not isinstance(raw, Mapping):
            raise TypeError("PolicyFlow configuration must be a mapping")
        expected = {name for name, item in cls.__dataclass_fields__.items() if item.init}
        actual = set(raw)
        if actual != expected:
            raise ValueError(
                "PolicyFlow configuration keys mismatch; "
                f"missing={sorted(expected - actual)}, extra={sorted(actual - expected)}"
            )
        values = dict(raw)
        base = source.parent
        for name in ("normalizer_checkpoint_path", "build_path", "run_directory"):
            values[name] = (base / Path(values[name])).resolve()
        if values["actor_initialization_checkpoint_path"] is not None:
            values["actor_initialization_checkpoint_path"] = (
                base / Path(values["actor_initialization_checkpoint_path"])
            ).resolve()
        if values["resume_checkpoint"] is not None:
            values["resume_checkpoint"] = (base / Path(values["resume_checkpoint"])).resolve()
        for name in ("velocity_hidden_sizes", "critic_hidden_sizes"):
            if not isinstance(values[name], list):
                raise TypeError(f"{name} must be a list")
            values[name] = tuple(values[name])
        config = cls(**values)
        if not config.normalizer_checkpoint_path.is_file():
            raise FileNotFoundError(
                f"normalizer checkpoint does not exist: {config.normalizer_checkpoint_path}"
            )
        payload = load_checkpoint(config.normalizer_checkpoint_path, map_location="cpu")
        metadata = payload.get("metadata", {})
        if not {"observation_shapes", "action_size", "build_sha256"} <= set(metadata):
            raise ValueError("normalizer checkpoint metadata is incomplete")
        if "normalizer_state" not in payload:
            raise ValueError("normalizer checkpoint is missing normalizer_state")
        object.__setattr__(config, "normalizer_checkpoint_sha256", _sha256(config.normalizer_checkpoint_path))
        object.__setattr__(config, "observation_shapes", tuple(tuple(shape) for shape in metadata["observation_shapes"]))
        object.__setattr__(config, "action_size", int(metadata["action_size"]))
        actor_payload = None
        if config.actor_initialization_checkpoint_path is not None:
            if not config.actor_initialization_checkpoint_path.is_file():
                raise FileNotFoundError(
                    "actor initialization checkpoint does not exist: "
                    f"{config.actor_initialization_checkpoint_path}"
                )
            actor_payload = load_checkpoint(
                config.actor_initialization_checkpoint_path, map_location="cpu"
            )
            validate_flow_bc_checkpoint(actor_payload)
            actor_hash = _sha256(config.actor_initialization_checkpoint_path)
        else:
            actor_hash = None
        object.__setattr__(
            config, "actor_initialization_checkpoint_sha256", actor_hash
        )
        config.validate(normalizer_payload=payload, actor_payload=actor_payload)
        return config

    def validate(
        self,
        *,
        normalizer_payload: Mapping[str, Any] | None = None,
        actor_payload: Mapping[str, Any] | None = None,
    ) -> None:
        if not self.build_path.is_file():
            raise FileNotFoundError(f"Unity build does not exist: {self.build_path}")
        if self.run_directory.is_dir() and any(self.run_directory.iterdir()):
            raise FileExistsError(f"run directory is not empty: {self.run_directory}")
        if self.resume_checkpoint is not None and not self.resume_checkpoint.is_file():
            raise FileNotFoundError(f"resume checkpoint does not exist: {self.resume_checkpoint}")
        if normalizer_payload is None:
            normalizer_payload = load_checkpoint(self.normalizer_checkpoint_path, map_location="cpu")
        if normalizer_payload["metadata"]["build_sha256"] != _sha256(self.build_path):
            raise ValueError("normalizer checkpoint Unity build hash does not match")
        if self.actor_initialization_checkpoint_path is not None:
            if actor_payload is None:
                actor_payload = load_checkpoint(
                    self.actor_initialization_checkpoint_path, map_location="cpu"
                )
                validate_flow_bc_checkpoint(actor_payload)
            actor_metadata = actor_payload["metadata"]
            actor_config = actor_payload["config"]
            if actor_metadata["build_sha256"] != _sha256(self.build_path):
                raise ValueError("actor initialization Unity build hash does not match")
            if not _normalizer_states_equal(
                actor_payload["normalizer_state"], normalizer_payload["normalizer_state"]
            ):
                raise ValueError(
                    "actor initialization normalizer does not match configured normalizer"
                )
            if tuple(tuple(shape) for shape in actor_metadata["observation_shapes"]) != self.observation_shapes:
                raise ValueError("actor initialization observation shapes do not match")
            if int(actor_metadata["action_size"]) != self.action_size:
                raise ValueError("actor initialization action size does not match")
            expected_network = {
                "state_size": self.state_size,
                "time_embedding_size": self.time_embedding_size,
                "velocity_hidden_sizes": list(self.velocity_hidden_sizes),
            }
            mismatched = [
                name for name, value in expected_network.items()
                if actor_config.get(name) != value
            ]
            if mismatched:
                raise ValueError(
                    f"actor initialization network mismatch: {sorted(mismatched)}"
                )
        for name in (
            "total_environment_steps", "rollout_size", "batch_size", "epochs",
            "state_size", "time_embedding_size", "checkpoint_interval", "timeout_wait",
        ):
            value = getattr(self, name)
            if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
                raise ValueError(f"{name} must be a positive integer")
        if self.rollout_size < self.batch_size:
            raise ValueError("rollout_size must be at least batch_size")
        if self.solver_steps not in {1, 2, 4}:
            raise ValueError("solver_steps must be one of 1, 2, 4")
        if self.horizon_steps != 1:
            raise ValueError("PolicyFlow horizon_steps must remain 1 in phase 7")
        if not 0.0 <= self.gamma <= 1.0 or not 0.0 <= self.gae_lambda <= 1.0:
            raise ValueError("gamma and gae_lambda must be within [0, 1]")
        if min(self.actor_learning_rate, self.critic_learning_rate, self.max_gradient_norm) <= 0.0:
            raise ValueError("learning rates and gradient norm must be positive")
        if not 0.0 <= self.clip_range < 1.0:
            raise ValueError("clip_range must be within [0, 1)")
        if min(self.gaussian_entropy_coefficient, self.brownian_coefficient, self.value_clip) < 0.0:
            raise ValueError("loss coefficients and value_clip must be nonnegative")
        for name in ("velocity_hidden_sizes", "critic_hidden_sizes"):
            sizes = getattr(self, name)
            if not sizes or any(int(size) <= 0 for size in sizes):
                raise ValueError(f"{name} must contain positive integers")
        if self.time_scale <= 0.0:
            raise ValueError("time_scale must be positive")
        if self.device not in {"cpu", "cuda"}:
            raise ValueError("device must be 'cpu' or 'cuda'")
        if self.device == "cuda" and not torch.cuda.is_available():
            raise RuntimeError("device cuda requires torch.cuda.is_available()")
        if min(self.worker_id, self.evaluation_worker_id, self.seed, self.max_agent_absence_steps) < 0:
            raise ValueError("worker IDs, seed and absence limit cannot be negative")
        if self.worker_id == self.evaluation_worker_id:
            raise ValueError("training and evaluation worker IDs must differ")
        if self.behavior_name is not None and not isinstance(self.behavior_name, str):
            raise TypeError("behavior_name must be a string or null")

    def to_dict(self) -> dict[str, Any]:
        result = {name: getattr(self, name) for name, item in self.__dataclass_fields__.items() if item.init}
        for name in ("normalizer_checkpoint_path", "build_path", "run_directory"):
            result[name] = str(getattr(self, name))
        result["actor_initialization_checkpoint_path"] = (
            None
            if self.actor_initialization_checkpoint_path is None
            else str(self.actor_initialization_checkpoint_path)
        )
        result["resume_checkpoint"] = None if self.resume_checkpoint is None else str(self.resume_checkpoint)
        result["velocity_hidden_sizes"] = list(self.velocity_hidden_sizes)
        result["critic_hidden_sizes"] = list(self.critic_hidden_sizes)
        result.update({
            "normalizer_checkpoint_sha256": self.normalizer_checkpoint_sha256,
            "actor_initialization_checkpoint_sha256": (
                self.actor_initialization_checkpoint_sha256
            ),
            "observation_shapes": [list(shape) for shape in self.observation_shapes],
            "action_size": self.action_size,
            "velocity_nfe": self.velocity_nfe,
        })
        return result


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _normalizer_states_equal(left: Any, right: Any) -> bool:
    if isinstance(left, torch.Tensor) and isinstance(right, torch.Tensor):
        return left.dtype == right.dtype and left.shape == right.shape and torch.equal(left, right)
    if hasattr(left, "shape") and hasattr(right, "shape"):
        return left.dtype == right.dtype and left.shape == right.shape and bool((left == right).all())
    if isinstance(left, Mapping) and isinstance(right, Mapping):
        return set(left) == set(right) and all(
            _normalizer_states_equal(left[key], right[key]) for key in left
        )
    if isinstance(left, (tuple, list)) and isinstance(right, (tuple, list)):
        return len(left) == len(right) and all(
            _normalizer_states_equal(a, b) for a, b in zip(left, right)
        )
    return left == right
