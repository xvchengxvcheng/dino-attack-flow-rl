from __future__ import annotations

import hashlib
import json
import time
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any

import numpy as np
import torch
from torch import nn

from flow_rl.data.normalization import (
    IdentityObservationNormalizer,
    ObservationNormalizer,
)
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.models.flow import ConditionalVelocityMLP
from flow_rl.tracking.checkpoint import (
    FlowSolverConfig,
    load_checkpoint,
    save_checkpoint,
    validate_flow_bc_checkpoint,
)
from flow_rl.tracking.run import RunLogger
from flow_rl.training.flow_bc_config import (
    FlowBCTrainingConfig,
    split_demonstrations,
)


@dataclass(frozen=True)
class FlowBCSummary:
    epochs: int
    train_samples: int
    validation_samples: int
    final_train_loss: float
    final_validation_loss: float
    wall_clock_seconds: float
    parameter_count: int
    final_checkpoint: str
    best_checkpoint: str
    all_finite: bool

    def to_dict(self) -> dict[str, Any]:
        return asdict(self)


def flow_matching_loss(
    model: nn.Module,
    observations: tuple[torch.Tensor, ...],
    actions: torch.Tensor,
    *,
    noise: torch.Tensor,
    time: torch.Tensor,
) -> torch.Tensor:
    """Conditional flow-matching loss for the linear noise-to-action path."""
    if actions.dtype != torch.float32 or actions.ndim != 2:
        raise TypeError("actions must be a two-dimensional float32 tensor")
    if noise.dtype != torch.float32 or noise.shape != actions.shape:
        raise TypeError("noise must be float32 and have the same shape as actions")
    if time.dtype != torch.float32 or time.shape != (actions.shape[0], 1):
        raise TypeError("time must be float32 with shape (batch, 1)")
    if actions.device != noise.device or actions.device != time.device:
        raise ValueError("actions, noise, and time must share a device")
    if not all(torch.isfinite(value).all() for value in (actions, noise, time)):
        raise ValueError("flow-matching inputs must be finite")
    interpolated = (1.0 - time) * noise + time * actions
    target_velocity = actions - noise
    predicted_velocity = model(observations, interpolated, time)
    if predicted_velocity.shape != actions.shape:
        raise ValueError("velocity prediction shape does not match actions")
    loss = torch.mean(torch.square(predicted_velocity - target_velocity))
    if not torch.isfinite(loss):
        raise RuntimeError("Flow BC loss is not finite")
    return loss


class FlowBCTrainer:
    def __init__(self, config: FlowBCTrainingConfig) -> None:
        self.config = config
        self.model: ConditionalVelocityMLP | None = None

    def train(self) -> FlowBCSummary:
        self.config.validate()
        _seed_everything(self.config.seed)
        device = torch.device(self.config.device)
        train_indices, validation_indices = split_demonstrations(
            self.config.demonstrations,
            validation_fraction=self.config.validation_fraction,
            seed=self.config.split_seed,
            episode_ids=self.config.episode_ids,
            strata=self.config.map_ids,
        )
        normalizer = _normalizer_from_metadata(self.config)
        normalized_observations = normalizer.normalize(
            self.config.demonstrations.observations
        )
        self.model = _build_velocity_model(self.config).to(device)
        optimizer = torch.optim.AdamW(
            self.model.parameters(),
            lr=self.config.learning_rate,
            weight_decay=self.config.weight_decay,
        )
        order_generator = torch.Generator(device="cpu").manual_seed(
            self.config.seed + 1_000
        )
        train_generator = torch.Generator(device=device).manual_seed(
            self.config.seed + 2_000
        )
        start_epoch = 0
        best_validation_loss = float("inf")
        elapsed_before_resume = 0.0
        if self.config.resume_checkpoint is not None:
            start_epoch, best_validation_loss, elapsed_before_resume = self._restore(
                path=self.config.resume_checkpoint,
                optimizer=optimizer,
                normalizer=normalizer,
                order_generator=order_generator,
                train_generator=train_generator,
            )
        started = time.perf_counter()
        final_train_loss = float("nan")
        final_validation_loss = float("nan")
        best_checkpoint = self.config.run_directory / "checkpoints" / "best.pt"
        with RunLogger(self.config.run_directory, self.config.to_dict()) as logger:
            for epoch in range(start_epoch + 1, self.config.epochs + 1):
                final_train_loss = self._train_epoch(
                    optimizer=optimizer,
                    normalized_observations=normalized_observations,
                    train_indices=train_indices,
                    order_generator=order_generator,
                    train_generator=train_generator,
                    device=device,
                )
                final_validation_loss = self._validate_epoch(
                    normalized_observations=normalized_observations,
                    validation_indices=validation_indices,
                    device=device,
                )
                logger.log_update(
                    {
                        "train_loss": final_train_loss,
                        "validation_loss": final_validation_loss,
                        "learning_rate": optimizer.param_groups[0]["lr"],
                    },
                    environment_steps=epoch,
                    update_index=epoch,
                )
                elapsed = elapsed_before_resume + time.perf_counter() - started
                if final_validation_loss < best_validation_loss:
                    best_validation_loss = final_validation_loss
                    self._save(
                        path=best_checkpoint,
                        epoch=epoch,
                        best_validation_loss=best_validation_loss,
                        elapsed=elapsed,
                        optimizer=optimizer,
                        normalizer=normalizer,
                        order_generator=order_generator,
                        train_generator=train_generator,
                    )
                if epoch % self.config.checkpoint_interval_epochs == 0:
                    self._save(
                        path=self.config.run_directory
                        / "checkpoints"
                        / f"epoch-{epoch}.pt",
                        epoch=epoch,
                        best_validation_loss=best_validation_loss,
                        elapsed=elapsed,
                        optimizer=optimizer,
                        normalizer=normalizer,
                        order_generator=order_generator,
                        train_generator=train_generator,
                    )
        if start_epoch >= self.config.epochs:
            raise ValueError("resume checkpoint already reached configured epochs")
        wall_clock_seconds = elapsed_before_resume + time.perf_counter() - started
        final_checkpoint = self.config.run_directory / "checkpoints" / "final.pt"
        self._save(
            path=final_checkpoint,
            epoch=self.config.epochs,
            best_validation_loss=best_validation_loss,
            elapsed=wall_clock_seconds,
            optimizer=optimizer,
            normalizer=normalizer,
            order_generator=order_generator,
            train_generator=train_generator,
        )
        summary = FlowBCSummary(
            epochs=self.config.epochs,
            train_samples=len(train_indices),
            validation_samples=len(validation_indices),
            final_train_loss=final_train_loss,
            final_validation_loss=final_validation_loss,
            wall_clock_seconds=wall_clock_seconds,
            parameter_count=sum(parameter.numel() for parameter in self.model.parameters()),
            final_checkpoint=str(final_checkpoint),
            best_checkpoint=str(best_checkpoint),
            all_finite=bool(
                np.isfinite(final_train_loss) and np.isfinite(final_validation_loss)
            ),
        )
        with (self.config.run_directory / "summary.json").open(
            "w", encoding="utf-8", newline="\n"
        ) as handle:
            json.dump(summary.to_dict(), handle, indent=2, sort_keys=True)
            handle.write("\n")
        return summary

    def _train_epoch(
        self,
        *,
        optimizer: torch.optim.Optimizer,
        normalized_observations: tuple[np.ndarray, ...],
        train_indices: np.ndarray,
        order_generator: torch.Generator,
        train_generator: torch.Generator,
        device: torch.device,
    ) -> float:
        assert self.model is not None
        self.model.train()
        order = torch.randperm(
            len(train_indices), generator=order_generator
        ).numpy()
        total_loss = 0.0
        sample_count = 0
        for offset in range(0, len(order), self.config.batch_size):
            indices = train_indices[order[offset : offset + self.config.batch_size]]
            observations = tuple(
                torch.as_tensor(array[indices], dtype=torch.float32, device=device)
                for array in normalized_observations
            )
            actions = torch.as_tensor(
                self.config.demonstrations.actions[indices],
                dtype=torch.float32,
                device=device,
            )
            noise = torch.randn(
                actions.shape,
                dtype=torch.float32,
                device=device,
                generator=train_generator,
            )
            sample_time = torch.rand(
                (len(indices), 1),
                dtype=torch.float32,
                device=device,
                generator=train_generator,
            )
            optimizer.zero_grad(set_to_none=True)
            loss = flow_matching_loss(
                self.model,
                observations,
                actions,
                noise=noise,
                time=sample_time,
            )
            loss.backward()
            for parameter in self.model.parameters():
                if parameter.grad is not None and not torch.isfinite(parameter.grad).all():
                    raise RuntimeError("Flow BC gradient is not finite")
            optimizer.step()
            total_loss += float(loss.detach()) * len(indices)
            sample_count += len(indices)
        return total_loss / sample_count

    def _validate_epoch(
        self,
        *,
        normalized_observations: tuple[np.ndarray, ...],
        validation_indices: np.ndarray,
        device: torch.device,
    ) -> float:
        assert self.model is not None
        self.model.eval()
        generator = torch.Generator(device=device).manual_seed(self.config.seed + 3_000)
        total_loss = 0.0
        sample_count = 0
        with torch.inference_mode():
            for offset in range(0, len(validation_indices), self.config.batch_size):
                indices = validation_indices[offset : offset + self.config.batch_size]
                observations = tuple(
                    torch.as_tensor(array[indices], dtype=torch.float32, device=device)
                    for array in normalized_observations
                )
                actions = torch.as_tensor(
                    self.config.demonstrations.actions[indices],
                    dtype=torch.float32,
                    device=device,
                )
                noise = torch.randn(
                    actions.shape,
                    dtype=torch.float32,
                    device=device,
                    generator=generator,
                )
                sample_time = torch.rand(
                    (len(indices), 1),
                    dtype=torch.float32,
                    device=device,
                    generator=generator,
                )
                loss = flow_matching_loss(
                    self.model,
                    observations,
                    actions,
                    noise=noise,
                    time=sample_time,
                )
                total_loss += float(loss) * len(indices)
                sample_count += len(indices)
        return total_loss / sample_count

    def _restore(
        self,
        *,
        path: Path,
        optimizer: torch.optim.Optimizer,
        normalizer: ObservationNormalizer | IdentityObservationNormalizer,
        order_generator: torch.Generator,
        train_generator: torch.Generator,
    ) -> tuple[int, float, float]:
        assert self.model is not None
        payload = load_checkpoint(path, map_location=self.config.device)
        validate_flow_bc_checkpoint(payload)
        stable_keys = (
            "dataset_sha256",
            "metadata_sha256",
            "batch_size",
            "learning_rate",
            "weight_decay",
            "validation_fraction",
            "state_size",
            "time_embedding_size",
            "velocity_hidden_sizes",
            "nfe",
            "seed",
            "split_seed",
            "encoder_type",
            "protocol_path",
            "encoder_d_model",
            "encoder_heads",
            "encoder_inducing_points",
            "encoder_layers",
            "encoder_dropout",
            "encoder_output_size",
        )
        current = self.config.to_dict()
        mismatched = [
            name
            for name in stable_keys
            if payload["config"].get(name) != current.get(name)
        ]
        if mismatched:
            raise ValueError(f"resume configuration mismatch: {mismatched}")
        metadata = payload["metadata"]
        if metadata["build_sha256"] != _sha256(self.config.build_path):
            raise ValueError("resume Unity build hash does not match")
        self.model.load_state_dict(payload["model_state"], strict=True)
        optimizer.load_state_dict(payload["optimizer_state"])
        normalizer.load_state_dict(payload["normalizer_state"])
        torch.set_rng_state(metadata["torch_rng_state"].cpu())
        if torch.cuda.is_available() and metadata["cuda_rng_state"] is not None:
            torch.cuda.set_rng_state_all(
                [state.cpu() for state in metadata["cuda_rng_state"]]
            )
        order_generator.set_state(metadata["order_generator_state"].cpu())
        train_generator.set_state(metadata["train_generator_state"].cpu())
        return (
            int(metadata["epoch"]),
            float(metadata["best_validation_loss"]),
            float(metadata["wall_clock_seconds"]),
        )

    def _save(
        self,
        *,
        path: Path,
        epoch: int,
        best_validation_loss: float,
        elapsed: float,
        optimizer: torch.optim.Optimizer,
        normalizer: ObservationNormalizer | IdentityObservationNormalizer,
        order_generator: torch.Generator,
        train_generator: torch.Generator,
    ) -> None:
        assert self.model is not None
        metadata = {
            "schema_version": 3 if self.config.encoder_type != "flat" else 2,
            "algorithm": "flow_bc",
            "epoch": epoch,
            "best_validation_loss": best_validation_loss,
            "dataset_sha256": self.config.dataset_sha256,
            "metadata_sha256": self.config.metadata_sha256,
            "build_sha256": _sha256(self.config.build_path),
            "observation_shapes": [list(shape) for shape in self.config.observation_shapes],
            "action_size": self.config.action_size,
            "solver": FlowSolverConfig(
                nfe=self.config.nfe, action_transform="clamp"
            ).to_dict(),
            "wall_clock_seconds": elapsed,
            "torch_rng_state": torch.get_rng_state(),
            "cuda_rng_state": (
                torch.cuda.get_rng_state_all() if torch.cuda.is_available() else None
            ),
            "order_generator_state": order_generator.get_state(),
            "train_generator_state": train_generator.get_state(),
        }
        if self.config.encoder_type == "set_transformer":
            assert self.config.protocol_path is not None
            protocol = DinoProtocol.from_yaml(self.config.protocol_path)
            metadata["protocol"] = protocol.checkpoint_metadata()
            metadata["encoder"] = self.model.state_encoder.checkpoint_metadata()
            metadata["dataset_provenance"] = {
                "checkpoint_sha256": self.config.dataset_metadata.get(
                    "checkpoint_sha256"
                ),
                "protocol_manifest_sha256": self.config.dataset_metadata.get(
                    "protocol_manifest_sha256"
                ),
                "policy_seed": self.config.dataset_metadata.get("policy_seed"),
                "layout_seed": self.config.dataset_metadata.get("layout_seed"),
                "accepted_episodes_by_map": self.config.dataset_metadata.get(
                    "accepted_episodes_by_map"
                ),
            }
        save_checkpoint(
            path,
            model_state=self.model.state_dict(),
            optimizer_state=optimizer.state_dict(),
            normalizer_state=normalizer.state_dict(),
            config=self.config.to_dict(),
            metadata=metadata,
        )


def _build_velocity_model(config: FlowBCTrainingConfig) -> ConditionalVelocityMLP:
    state_encoder = None
    if config.encoder_type == "set_transformer":
        assert config.protocol_path is not None
        protocol = DinoProtocol.from_yaml(config.protocol_path)
        state_encoder = SetTransformerDinoEncoder(
            protocol,
            d_model=config.encoder_d_model,
            heads=config.encoder_heads,
            inducing_points=config.encoder_inducing_points,
            layers=config.encoder_layers,
            dropout=config.encoder_dropout,
            output_size=config.state_size,
        )
    return ConditionalVelocityMLP(
        observation_shapes=config.observation_shapes,
        action_size=config.action_size,
        state_size=config.state_size,
        time_embedding_size=config.time_embedding_size,
        hidden_sizes=config.velocity_hidden_sizes,
        state_encoder=state_encoder,
    )


def _normalizer_from_metadata(
    config: FlowBCTrainingConfig,
) -> ObservationNormalizer | IdentityObservationNormalizer:
    if config.normalizer_type == "identity":
        assert config.protocol_path is not None
        protocol = DinoProtocol.from_yaml(config.protocol_path)
        return IdentityObservationNormalizer(
            config.observation_shapes,
            protocol_manifest_sha256=protocol.manifest_sha256,
        )
    raw = config.dataset_metadata["observation_normalization"]
    normalizer = ObservationNormalizer(
        config.observation_shapes,
        epsilon=float(raw["epsilon"]),
        clip=float(raw["clip"]),
    )
    normalizer.load_state_dict(
        {
            "shapes": config.observation_shapes,
            "epsilon": raw["epsilon"],
            "clip": raw["clip"],
            "count": raw["count"],
            "mean": raw["mean"],
            "variance": raw["variance"],
        }
    )
    return normalizer


def _seed_everything(seed: int) -> None:
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()
