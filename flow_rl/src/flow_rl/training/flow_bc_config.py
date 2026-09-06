from __future__ import annotations

import hashlib
import json
from collections.abc import Mapping
from dataclasses import dataclass, field
from pathlib import Path
from typing import Any

import numpy as np
import torch
import yaml

from flow_rl.data.demonstrations import DemonstrationBatch, load_demonstrations
from flow_rl.data.dino_demonstrations import (
    DinoDemonstrationBatch,
    load_dino_demonstrations,
)
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder


@dataclass(frozen=True)
class FlowBCTrainingConfig:
    dataset_directory: Path
    dataset_sha256: str
    metadata_sha256: str
    build_path: Path
    run_directory: Path
    epochs: int
    batch_size: int
    learning_rate: float
    weight_decay: float
    validation_fraction: float
    state_size: int
    time_embedding_size: int
    velocity_hidden_sizes: tuple[int, ...]
    nfe: int
    checkpoint_interval_epochs: int
    device: str
    seed: int
    split_seed: int
    resume_checkpoint: Path | None
    encoder_type: str = "flat"
    protocol_path: Path | None = None
    encoder_d_model: int = 48
    encoder_heads: int = 4
    encoder_inducing_points: int = 8
    encoder_layers: int = 1
    encoder_dropout: float = 0.0
    encoder_output_size: int = 128
    demonstrations: DemonstrationBatch = field(init=False, repr=False, compare=False)
    dataset_metadata: dict[str, Any] = field(init=False, repr=False, compare=False)
    dino_demonstrations: DinoDemonstrationBatch | None = field(
        init=False, repr=False, compare=False, default=None
    )

    @classmethod
    def from_yaml(cls, path: Path) -> "FlowBCTrainingConfig":
        source = Path(path).resolve()
        with source.open(encoding="utf-8") as handle:
            raw = yaml.safe_load(handle)
        if not isinstance(raw, Mapping):
            raise TypeError("Flow BC configuration must be a mapping")
        allowed = {
            name for name, item in cls.__dataclass_fields__.items() if item.init
        }
        optional = {
            "encoder_type",
            "protocol_path",
            "encoder_d_model",
            "encoder_heads",
            "encoder_inducing_points",
            "encoder_layers",
            "encoder_dropout",
            "encoder_output_size",
        }
        required = allowed - optional
        actual = set(raw)
        if not required.issubset(actual) or not actual.issubset(allowed):
            raise ValueError(
                "Flow BC configuration keys mismatch; "
                f"missing={sorted(required - actual)}, extra={sorted(actual - allowed)}"
            )
        if raw.get("encoder_type", "flat") == "set_transformer" and not optional.issubset(actual):
            raise ValueError(
                "structured Flow BC configuration keys mismatch; "
                f"missing={sorted(optional - actual)}"
            )
        values = dict(raw)
        base = source.parent
        for name in ("dataset_directory", "build_path", "run_directory"):
            values[name] = (base / Path(values[name])).resolve()
        if values.get("protocol_path") is not None:
            values["protocol_path"] = (base / Path(values["protocol_path"])).resolve()
        if values["resume_checkpoint"] is not None:
            values["resume_checkpoint"] = (
                base / Path(values["resume_checkpoint"])
            ).resolve()
        if not isinstance(values["velocity_hidden_sizes"], list):
            raise TypeError("velocity_hidden_sizes must be a list")
        values["velocity_hidden_sizes"] = tuple(values["velocity_hidden_sizes"])
        config = cls(**values)
        metadata_path = config.dataset_directory / "metadata.json"
        with metadata_path.open(encoding="utf-8") as handle:
            metadata_hint = json.load(handle)
        is_dino = int(metadata_hint.get("schema_version", 0)) == 2
        dino_batch: DinoDemonstrationBatch | None = None
        if is_dino:
            dino_batch, metadata = load_dino_demonstrations(config.dataset_directory)
            demonstrations = dino_batch.demonstrations
        else:
            demonstrations, metadata = load_demonstrations(config.dataset_directory)
        object.__setattr__(config, "demonstrations", demonstrations)
        object.__setattr__(config, "dataset_metadata", metadata)
        object.__setattr__(config, "dino_demonstrations", dino_batch)
        config.validate()
        return config

    @property
    def observation_shapes(self) -> tuple[tuple[int, ...], ...]:
        raw = self.dataset_metadata.get("observation_shapes")
        if raw is None and self.dino_demonstrations is not None:
            raw = tuple(array.shape[1:] for array in self.demonstrations.observations)
        return tuple(tuple(int(value) for value in shape) for shape in raw)

    @property
    def action_size(self) -> int:
        return int(
            self.dataset_metadata.get(
                "action_size", self.demonstrations.actions.shape[1]
            )
        )

    @property
    def normalizer_type(self) -> str:
        return str(self.dataset_metadata.get("normalizer_type", "running"))

    @property
    def episode_ids(self) -> np.ndarray | None:
        return (
            None
            if self.dino_demonstrations is None
            else self.dino_demonstrations.episode_ids
        )

    @property
    def map_ids(self) -> np.ndarray | None:
        return (
            None if self.dino_demonstrations is None else self.dino_demonstrations.map_ids
        )

    def validate(self) -> None:
        data_path = self.dataset_directory / "demonstrations.npz"
        metadata_path = self.dataset_directory / "metadata.json"
        if not data_path.is_file() or not metadata_path.is_file():
            raise FileNotFoundError("Flow BC dataset files do not exist")
        if _sha256(data_path) != self.dataset_sha256.lower():
            raise ValueError("dataset SHA-256 does not match configuration")
        if _sha256(metadata_path) != self.metadata_sha256.lower():
            raise ValueError("metadata SHA-256 does not match configuration")
        if not self.build_path.is_file():
            raise FileNotFoundError(f"Unity build does not exist: {self.build_path}")
        if self.dataset_metadata.get("build_sha256") != _sha256(self.build_path):
            raise ValueError("dataset Unity build hash does not match build_path")
        if self.dino_demonstrations is None:
            if self.dataset_metadata.get("actions_are_bounded") is not True:
                raise ValueError("Flow BC requires bounded dataset actions")
            if self.dataset_metadata.get("observations_are_raw") is not True:
                raise ValueError("Flow BC requires raw dataset observations")
        else:
            if not np.isfinite(self.demonstrations.actions).all() or np.any(
                np.abs(self.demonstrations.actions) > 1.0
            ):
                raise ValueError("Dino Flow BC requires finite bounded actions")
            if self.normalizer_type != "identity":
                raise ValueError("Dino Flow BC requires Identity normalization")
        if int(self.dataset_metadata.get("sample_count", -1)) != len(self.demonstrations):
            raise ValueError("dataset sample_count does not match arrays")
        if self.observation_shapes != tuple(
            tuple(array.shape[1:]) for array in self.demonstrations.observations
        ):
            raise ValueError("dataset observation shapes do not match metadata")
        if self.demonstrations.actions.shape[1] != self.action_size:
            raise ValueError("dataset action size does not match metadata")
        if self.encoder_type not in {"flat", "set_transformer"}:
            raise ValueError("encoder_type must be 'flat' or 'set_transformer'")
        if self.encoder_type == "set_transformer":
            if self.dino_demonstrations is None or self.protocol_path is None:
                raise ValueError("Set Transformer Flow BC requires a Dino dataset and protocol")
            if self.encoder_output_size != self.state_size:
                raise ValueError("encoder_output_size must match state_size")
            protocol = DinoProtocol.from_yaml(self.protocol_path)
            encoder = SetTransformerDinoEncoder(
                protocol,
                d_model=self.encoder_d_model,
                heads=self.encoder_heads,
                inducing_points=self.encoder_inducing_points,
                layers=self.encoder_layers,
                dropout=self.encoder_dropout,
                output_size=self.state_size,
            )
            if protocol.observation_shapes != self.observation_shapes:
                raise ValueError("Dino protocol observation shapes do not match dataset")
            if protocol.action_size != self.action_size:
                raise ValueError("Dino protocol action size does not match dataset")
            if self.dataset_metadata.get("protocol_manifest_sha256") != protocol.manifest_sha256:
                raise ValueError("Dino dataset protocol hash does not match protocol_path")
            expected_encoder = json.loads(json.dumps(encoder.checkpoint_metadata()))
            if self.dataset_metadata.get("encoder") != expected_encoder:
                raise ValueError("Dino dataset encoder metadata does not match config")
        if self.run_directory.is_dir() and any(self.run_directory.iterdir()):
            raise FileExistsError(f"run directory is not empty: {self.run_directory}")
        if self.resume_checkpoint is not None and not self.resume_checkpoint.is_file():
            raise FileNotFoundError(f"resume checkpoint does not exist: {self.resume_checkpoint}")
        for name in (
            "epochs",
            "batch_size",
            "state_size",
            "time_embedding_size",
            "checkpoint_interval_epochs",
            "encoder_d_model",
            "encoder_heads",
            "encoder_inducing_points",
            "encoder_layers",
            "encoder_output_size",
        ):
            value = getattr(self, name)
            if isinstance(value, bool) or not isinstance(value, int) or value <= 0:
                raise ValueError(f"{name} must be a positive integer")
        if self.batch_size > len(self.demonstrations):
            raise ValueError("batch_size cannot exceed dataset size")
        validation_count = int(round(len(self.demonstrations) * self.validation_fraction))
        if not 0.0 < self.validation_fraction < 1.0 or not 0 < validation_count < len(self.demonstrations):
            raise ValueError("validation_fraction must produce non-empty train and validation splits")
        if self.learning_rate <= 0.0 or self.weight_decay < 0.0:
            raise ValueError("learning_rate must be positive and weight_decay nonnegative")
        if self.nfe not in {1, 2, 4, 8}:
            raise ValueError("nfe must be one of 1, 2, 4, 8")
        if self.time_embedding_size < 4 or self.time_embedding_size % 2 != 0:
            raise ValueError("time_embedding_size must be even and at least 4")
        if not self.velocity_hidden_sizes or any(
            isinstance(size, bool) or not isinstance(size, int) or size <= 0
            for size in self.velocity_hidden_sizes
        ):
            raise ValueError("velocity_hidden_sizes must contain positive integers")
        if self.device not in {"cpu", "cuda"}:
            raise ValueError("device must be 'cpu' or 'cuda'")
        if self.device == "cuda" and not torch.cuda.is_available():
            raise RuntimeError("device cuda requires torch.cuda.is_available()")
        if self.seed < 0 or self.split_seed < 0:
            raise ValueError("seeds cannot be negative")

    def to_dict(self) -> dict[str, Any]:
        serialized = {
            name: getattr(self, name)
            for name, item in self.__dataclass_fields__.items()
            if item.init
        }
        for name in ("dataset_directory", "build_path", "run_directory"):
            serialized[name] = str(getattr(self, name))
        serialized["resume_checkpoint"] = (
            None if self.resume_checkpoint is None else str(self.resume_checkpoint)
        )
        serialized["velocity_hidden_sizes"] = list(self.velocity_hidden_sizes)
        serialized["protocol_path"] = (
            None if self.protocol_path is None else str(self.protocol_path)
        )
        return serialized


def split_demonstrations(
    batch: DemonstrationBatch,
    *,
    validation_fraction: float,
    seed: int,
    episode_ids: np.ndarray | None = None,
    strata: np.ndarray | None = None,
) -> tuple[np.ndarray, np.ndarray]:
    if episode_ids is not None:
        groups = np.asarray(episode_ids)
        if groups.dtype != np.int64 or groups.shape != (len(batch),):
            raise TypeError("episode_ids must be an int64 vector matching the batch")
        labels = (
            np.zeros(len(batch), dtype=np.int64)
            if strata is None
            else np.asarray(strata)
        )
        if labels.dtype != np.int64 or labels.shape != (len(batch),):
            raise TypeError("strata must be an int64 vector matching the batch")
        if not 0.0 < validation_fraction < 1.0:
            raise ValueError("validation_fraction must be within (0, 1)")
        generator = np.random.default_rng(seed)
        validation_groups: list[int] = []
        for label in np.unique(labels):
            indices = np.flatnonzero(labels == label)
            label_groups = np.unique(groups[indices])
            for group in label_groups:
                if not np.all(labels[groups == group] == label):
                    raise ValueError("one episode cannot cross split strata")
            validation_count = int(round(len(label_groups) * validation_fraction))
            if not 0 < validation_count < len(label_groups):
                raise ValueError(
                    "validation_fraction must leave train and validation episodes "
                    "in every stratum"
                )
            shuffled = generator.permutation(label_groups)
            validation_groups.extend(int(item) for item in shuffled[:validation_count])
        is_validation = np.isin(groups, np.asarray(validation_groups, dtype=np.int64))
        return np.flatnonzero(~is_validation), np.flatnonzero(is_validation)
    validation_count = int(round(len(batch) * validation_fraction))
    if not 0.0 < validation_fraction < 1.0 or not 0 < validation_count < len(batch):
        raise ValueError("validation_fraction must produce non-empty splits")
    indices = np.random.default_rng(seed).permutation(len(batch)).astype(np.int64)
    return indices[validation_count:], indices[:validation_count]


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()
