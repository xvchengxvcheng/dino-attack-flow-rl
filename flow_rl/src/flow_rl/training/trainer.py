from __future__ import annotations

import hashlib
import json
import math
import random
import time
from collections.abc import Callable, Sequence
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Protocol

import numpy as np
import torch
from mlagents_envs.side_channel.engine_configuration_channel import (
    EngineConfigurationChannel,
)
from mlagents_envs.side_channel.side_channel import SideChannel

from flow_rl.algorithms.ppo import PPOBatch, PPOMetrics, PPOUpdater
from flow_rl.data.gae import compute_gae
from flow_rl.data.normalization import IdentityObservationNormalizer, ObservationNormalizer
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.envs.types import EnvStep
from flow_rl.envs.unity import UnityEnvAdapter
from flow_rl.models.policy import GaussianActorCritic
from flow_rl.models.state_encoder import StateEncoder, build_state_encoder
from flow_rl.tracking.agent_liveness import AgentLivenessTracker
from flow_rl.tracking.checkpoint import (
    STRUCTURED_CHECKPOINT_SCHEMA_VERSION,
    load_checkpoint,
    save_checkpoint,
    validate_checkpoint_compatibility,
)
from flow_rl.tracking.episodes import EpisodeTracker
from flow_rl.tracking.run import RunLogger
from flow_rl.training.collector import PPOAuxiliary, PPOCollector
from flow_rl.training.config import PPOTrainingConfig
from flow_rl.training.versioned_collector import VersionedBatch, VersionedCollector


class TrainingAdapter(Protocol):
    behavior_name: str
    continuous_action_size: int

    @property
    def pending_agent_ids(self) -> np.ndarray: ...

    def __enter__(self) -> "TrainingAdapter": ...

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None: ...

    def reset(self) -> EnvStep: ...

    def step(self, actions: np.ndarray) -> EnvStep: ...


AdapterFactory = Callable[..., TrainingAdapter]
PolicyFactory = Callable[
    [Sequence[tuple[int, ...]], int, Sequence[int], torch.device],
    GaussianActorCritic,
]
_ENCODER_CONFIG_DEFAULTS: dict[str, object] = {
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


@dataclass(frozen=True)
class TrainingSummary:
    environment_steps: int
    unity_steps: int
    optimizer_updates: int
    episodes: int
    terminated_episodes: int
    truncated_episodes: int
    wall_clock_seconds: float
    steps_per_second: float
    parameter_count: int
    initial_agent_ids: tuple[int, ...]
    final_drain_agent_ids: tuple[int, ...]
    maximum_agent_absence_steps_observed: int
    all_finite: bool
    final_checkpoint: str

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def _default_policy_factory(
    observation_shapes: Sequence[tuple[int, ...]],
    action_size: int,
    hidden_sizes: Sequence[int],
    device: torch.device,
) -> GaussianActorCritic:
    return GaussianActorCritic(
        observation_shapes=observation_shapes,
        action_size=action_size,
        hidden_sizes=hidden_sizes,
    ).to(device)


def _default_adapter_factory(
    *,
    config: PPOTrainingConfig,
    log_folder: Path,
    side_channels: Sequence[SideChannel],
) -> UnityEnvAdapter:
    return UnityEnvAdapter(
        config.build_path,
        worker_id=config.worker_id,
        seed=config.seed,
        no_graphics=True,
        timeout_wait=config.timeout_wait,
        behavior_name=config.behavior_name,
        log_folder=log_folder,
        side_channels=side_channels,
        protocol=_load_protocol(config.protocol_path),
    )


def _load_protocol(path: Path | None) -> DinoProtocol | None:
    return None if path is None else DinoProtocol.from_yaml(path)


def _encoder_factory(
    config: PPOTrainingConfig,
    protocol: DinoProtocol | None,
) -> Callable[[], StateEncoder] | None:
    if config.encoder_type == "flat":
        return None
    return lambda: build_state_encoder(
        config.encoder_type,
        protocol,
        output_size=config.encoder_output_size,
        d_model=config.encoder_d_model,
        heads=config.encoder_heads,
        inducing_points=config.encoder_inducing_points,
        layers=config.encoder_layers,
        dropout=config.encoder_dropout,
    )


def _structured_encoder_metadata(
    policy: GaussianActorCritic,
) -> dict[str, object] | None:
    encoder = policy.actor.encoder
    if not isinstance(encoder, StateEncoder):
        return None
    return encoder.checkpoint_metadata()


class VersionedPPOUpdater(Protocol):
    def update(self, batch: PPOBatch, *, environment_steps: int) -> object: ...


def _build_versioned_ppo_batch(
    versioned: VersionedBatch[PPOAuxiliary],
    *,
    gamma: float,
    gae_lambda: float,
    device: torch.device,
) -> PPOBatch:
    observation_streams: list[list[np.ndarray]] | None = None
    actions: list[np.ndarray] = []
    old_log_probs: list[float] = []
    old_values: list[float] = []
    advantages: list[np.ndarray] = []
    returns: list[np.ndarray] = []
    for trajectory in versioned.trajectories().values():
        if observation_streams is None:
            observation_streams = [[] for _ in trajectory[0].observation]
        rewards = np.asarray([item.reward for item in trajectory], dtype=np.float32)
        values = np.asarray([item.value for item in trajectory], dtype=np.float32)
        next_values = np.asarray(
            [item.next_value for item in trajectory], dtype=np.float32
        )
        terminated = np.asarray([item.terminated for item in trajectory], dtype=bool)
        truncated = np.asarray([item.truncated for item in trajectory], dtype=bool)
        item_advantages, item_returns = compute_gae(
            rewards,
            values,
            next_values,
            terminated,
            truncated,
            gamma=gamma,
            gae_lambda=gae_lambda,
        )
        advantages.append(item_advantages)
        returns.append(item_returns)
        for transition in trajectory:
            if not isinstance(transition.auxiliary, PPOAuxiliary):
                raise TypeError("PPO versioned transitions require PPOAuxiliary")
            for stream, observation in zip(
                observation_streams, transition.observation
            ):
                stream.append(observation)
            actions.append(transition.action)
            old_log_probs.append(transition.auxiliary.old_log_prob)
            old_values.append(transition.value)
    if observation_streams is None:
        raise RuntimeError("no versioned PPO transitions are available")
    return PPOBatch(
        observations=tuple(
            torch.as_tensor(np.stack(stream), device=device)
            for stream in observation_streams
        ),
        actions=torch.as_tensor(np.stack(actions), device=device),
        old_log_probs=torch.as_tensor(
            np.asarray(old_log_probs, dtype=np.float32), device=device
        ),
        old_values=torch.as_tensor(
            np.asarray(old_values, dtype=np.float32), device=device
        ),
        advantages=torch.as_tensor(np.concatenate(advantages), device=device),
        returns=torch.as_tensor(np.concatenate(returns), device=device),
    )


def update_versioned_ppo(
    collector: VersionedCollector[PPOAuxiliary],
    updater: VersionedPPOUpdater,
    *,
    gamma: float,
    gae_lambda: float,
    device: torch.device,
    environment_steps: int,
) -> object:
    """Build and update one drained PPO version, advancing only on success."""

    versioned = collector.build_batch()
    batch = _build_versioned_ppo_batch(
        versioned,
        gamma=gamma,
        gae_lambda=gae_lambda,
        device=device,
    )
    result = updater.update(batch, environment_steps=environment_steps)
    collector.complete_optimizer_update(versioned.policy_version, succeeded=True)
    return result


class PPOTrainer:
    def __init__(
        self,
        config: PPOTrainingConfig,
        *,
        adapter_factory: AdapterFactory = _default_adapter_factory,
        policy_factory: PolicyFactory = _default_policy_factory,
    ) -> None:
        self.config = config
        self._adapter_factory = adapter_factory
        self._policy_factory = policy_factory
        self.policy: GaussianActorCritic | None = None

    def train(self) -> TrainingSummary:
        self.config.validate()
        protocol = _load_protocol(self.config.protocol_path)
        _seed_everything(self.config.seed)
        device = torch.device(self.config.device)
        engine_channel = EngineConfigurationChannel()
        engine_channel.set_configuration_parameters(
            width=84,
            height=84,
            quality_level=0,
            time_scale=self.config.time_scale,
            target_frame_rate=-1,
            capture_frame_rate=0,
        )
        config_dict = self.config.to_dict()
        started = time.perf_counter()
        unity_log_directory = self.config.run_directory / "unity"
        with RunLogger(self.config.run_directory, config_dict) as logger:
            unity_log_directory.mkdir(parents=True, exist_ok=False)
            with self._adapter_factory(
                config=self.config,
                log_folder=unity_log_directory,
                side_channels=[engine_channel],
            ) as adapter:
                initial_step = adapter.reset()
                initial_ids = tuple(
                    int(agent_id) for agent_id in adapter.pending_agent_ids
                )
                if not initial_ids:
                    raise RuntimeError("Unity reset produced no decision Agents")
                observation_shapes = tuple(
                    tuple(observation.shape[1:])
                    for observation in initial_step.observations
                )
                if self._policy_factory is _default_policy_factory:
                    self.policy = GaussianActorCritic(
                        observation_shapes=observation_shapes,
                        action_size=adapter.continuous_action_size,
                        hidden_sizes=self.config.hidden_sizes,
                        encoder_factory=_encoder_factory(self.config, protocol),
                    ).to(device)
                else:
                    self.policy = self._policy_factory(
                        observation_shapes,
                        adapter.continuous_action_size,
                        self.config.hidden_sizes,
                        device,
                    )
                normalizer = (
                    ObservationNormalizer(observation_shapes)
                    if protocol is None
                    else IdentityObservationNormalizer(
                        observation_shapes,
                        protocol_manifest_sha256=protocol.manifest_sha256,
                    )
                )
                action_generator = torch.Generator(device=device).manual_seed(
                    self.config.seed + 1_000
                )
                update_generator = torch.Generator().manual_seed(
                    self.config.seed + 2_000
                )
                collector = PPOCollector(
                    policy=self.policy,
                    normalizer=normalizer,
                    device=device,
                    gamma=self.config.gamma,
                    gae_lambda=self.config.gae_lambda,
                    generator=action_generator,
                )
                updater = PPOUpdater(
                    self.policy,
                    learning_rate=self.config.learning_rate,
                    final_learning_rate=self.config.final_learning_rate,
                    clip_range=self.config.clip_range,
                    final_clip_range=self.config.final_clip_range,
                    entropy_coefficient=self.config.entropy_coefficient,
                    final_entropy_coefficient=self.config.final_entropy_coefficient,
                    value_coefficient=self.config.value_coefficient,
                    max_gradient_norm=self.config.max_gradient_norm,
                    total_environment_steps=self.config.total_environment_steps,
                    batch_size=self.config.batch_size,
                    epochs=self.config.epochs,
                    generator=update_generator,
                )
                environment_steps = 0
                unity_steps = 0
                optimizer_updates = 0
                previous_elapsed = 0.0
                if self.config.resume_checkpoint is not None:
                    (
                        environment_steps,
                        unity_steps,
                        optimizer_updates,
                        previous_elapsed,
                    ) = self._restore_training_checkpoint(
                        path=self.config.resume_checkpoint,
                        updater=updater,
                        normalizer=normalizer,
                        observation_shapes=observation_shapes,
                        action_size=adapter.continuous_action_size,
                        action_generator=action_generator,
                        update_generator=update_generator,
                    )
                    if environment_steps >= self.config.total_environment_steps:
                        raise ValueError(
                            "resume checkpoint already reached total_environment_steps"
                        )
                output = collector.reset(initial_step)
                actions = output.actions
                episode_tracker = EpisodeTracker()
                liveness = AgentLivenessTracker(
                    initial_ids,
                    max_absence_steps=self.config.max_agent_absence_steps,
                )
                session_unity_steps = 0
                episode_count = 0
                terminated_episodes = 0
                truncated_episodes = 0
                final_drain_ids: set[int] = set()
                next_checkpoint = (
                    environment_steps // self.config.checkpoint_interval + 1
                ) * self.config.checkpoint_interval
                while True:
                    submitted_count = actions.shape[0]
                    step = adapter.step(actions)
                    environment_steps += submitted_count
                    unity_steps += 1
                    session_unity_steps += 1
                    liveness.observe(step, unity_step=session_unity_steps)
                    if environment_steps >= self.config.total_environment_steps:
                        final_drain_ids.update(int(agent_id) for agent_id in step.agent_ids)
                    for episode in episode_tracker.record(step):
                        episode_count += 1
                        terminated_episodes += int(episode.terminated)
                        truncated_episodes += int(episode.truncated)
                        logger.log_episode(episode, environment_steps)
                    output = collector.step(step)
                    actions = output.actions
                    if collector.completed_transition_count >= self.config.rollout_size:
                        optimizer_updates += 1
                        metrics = updater.update(
                            collector.build_batch(),
                            environment_steps=environment_steps,
                        )
                        logger.log_update(
                            metrics.as_dict(),
                            environment_steps=environment_steps,
                            update_index=optimizer_updates,
                        )
                    while environment_steps >= next_checkpoint:
                        self._save_training_checkpoint(
                            path=self.config.run_directory
                            / "checkpoints"
                            / f"step-{next_checkpoint}.pt",
                            updater=updater,
                            normalizer=normalizer,
                            environment_steps=environment_steps,
                            unity_steps=unity_steps,
                            optimizer_updates=optimizer_updates,
                            elapsed=previous_elapsed + time.perf_counter() - started,
                            observation_shapes=observation_shapes,
                            action_size=adapter.continuous_action_size,
                            action_generator=action_generator,
                            update_generator=update_generator,
                        )
                        next_checkpoint += self.config.checkpoint_interval
                    if (
                        environment_steps >= self.config.total_environment_steps
                        and final_drain_ids >= set(initial_ids)
                    ):
                        break
                if collector.completed_transition_count > 0:
                    optimizer_updates += 1
                    metrics = updater.update(
                        collector.build_batch(),
                        environment_steps=environment_steps,
                    )
                    logger.log_update(
                        metrics.as_dict(),
                        environment_steps=environment_steps,
                        update_index=optimizer_updates,
                    )
                liveness.finalize(observed_agent_ids=final_drain_ids)
                final_checkpoint = self.config.run_directory / "checkpoints" / "final.pt"
                elapsed = previous_elapsed + time.perf_counter() - started
                self._save_training_checkpoint(
                    path=final_checkpoint,
                    updater=updater,
                    normalizer=normalizer,
                    environment_steps=environment_steps,
                    unity_steps=unity_steps,
                    optimizer_updates=optimizer_updates,
                    elapsed=elapsed,
                    observation_shapes=observation_shapes,
                    action_size=adapter.continuous_action_size,
                    action_generator=action_generator,
                    update_generator=update_generator,
                )
                steps_per_second = environment_steps / elapsed
                parameter_count = sum(
                    parameter.numel() for parameter in self.policy.parameters()
                )
                summary = TrainingSummary(
                    environment_steps=environment_steps,
                    unity_steps=unity_steps,
                    optimizer_updates=optimizer_updates,
                    episodes=episode_count,
                    terminated_episodes=terminated_episodes,
                    truncated_episodes=truncated_episodes,
                    wall_clock_seconds=elapsed,
                    steps_per_second=steps_per_second,
                    parameter_count=parameter_count,
                    initial_agent_ids=tuple(sorted(initial_ids)),
                    final_drain_agent_ids=tuple(sorted(final_drain_ids)),
                    maximum_agent_absence_steps_observed=(
                        liveness.maximum_absence_steps_observed
                    ),
                    all_finite=all(
                        math.isfinite(value)
                        for value in (elapsed, steps_per_second)
                    ),
                    final_checkpoint=str(final_checkpoint),
                )
                logger.log_metrics(
                    {
                        "runtime/steps_per_second": steps_per_second,
                        "runtime/unity_steps": float(unity_steps),
                        "runtime/parameter_count": float(parameter_count),
                    },
                    environment_steps,
                )
        with (self.config.run_directory / "summary.json").open(
            "w", encoding="utf-8", newline="\n"
        ) as handle:
            json.dump(summary.to_dict(), handle, indent=2, sort_keys=True)
            handle.write("\n")
        return summary

    def _restore_training_checkpoint(
        self,
        *,
        path: Path,
        updater: PPOUpdater,
        normalizer: ObservationNormalizer | IdentityObservationNormalizer,
        observation_shapes: Sequence[tuple[int, ...]],
        action_size: int,
        action_generator: torch.Generator,
        update_generator: torch.Generator,
    ) -> tuple[int, int, int, float]:
        if self.policy is None:
            raise RuntimeError("policy is not initialized")
        payload = load_checkpoint(path, map_location=self.config.device)
        protocol = _load_protocol(self.config.protocol_path)
        encoder_metadata = _structured_encoder_metadata(self.policy)
        validate_checkpoint_compatibility(
            payload,
            expected_observation_shapes=observation_shapes,
            expected_action_size=action_size,
            expected_protocol_metadata=(
                None if protocol is None else protocol.checkpoint_metadata()
            ),
            expected_encoder_metadata=encoder_metadata,
        )
        saved_config = payload["config"]
        current_config = self.config.to_dict()
        stable_keys = (
            "total_environment_steps",
            "rollout_size",
            "batch_size",
            "epochs",
            "gamma",
            "gae_lambda",
            "learning_rate",
            "final_learning_rate",
            "clip_range",
            "final_clip_range",
            "entropy_coefficient",
            "final_entropy_coefficient",
            "value_coefficient",
            "max_gradient_norm",
            "hidden_sizes",
            "encoder_type",
            "protocol_path",
            "encoder_d_model",
            "encoder_heads",
            "encoder_inducing_points",
            "encoder_layers",
            "encoder_dropout",
            "encoder_output_size",
            "normalize_observations",
            "seed",
        )
        mismatched = [
            key
            for key in stable_keys
            if saved_config.get(key, _ENCODER_CONFIG_DEFAULTS.get(key))
            != current_config.get(key)
        ]
        metadata = payload["metadata"]
        if tuple(tuple(shape) for shape in metadata["observation_shapes"]) != tuple(
            tuple(shape) for shape in observation_shapes
        ):
            mismatched.append("observation_shapes")
        if int(metadata["action_size"]) != action_size:
            mismatched.append("action_size")
        if metadata["build_sha256"] != _sha256(self.config.build_path):
            mismatched.append("build_sha256")
        if mismatched:
            raise ValueError(
                f"resume checkpoint configuration mismatch: {sorted(set(mismatched))}"
            )
        self.policy.load_state_dict(payload["model_state"], strict=True)
        updater.optimizer.load_state_dict(payload["optimizer_state"])
        normalizer.load_state_dict(payload["normalizer_state"])
        torch.set_rng_state(metadata["torch_rng_state"].cpu())
        if torch.cuda.is_available() and metadata["cuda_rng_state"] is not None:
            torch.cuda.set_rng_state_all(
                [state.cpu() for state in metadata["cuda_rng_state"]]
            )
        action_generator.set_state(metadata["action_generator_state"].cpu())
        update_generator.set_state(metadata["update_generator_state"].cpu())
        np.random.set_state(metadata["numpy_random_state"])
        return (
            int(metadata["environment_steps"]),
            int(metadata["unity_steps"]),
            int(metadata["optimizer_updates"]),
            float(metadata["wall_clock_seconds"]),
        )

    def _save_training_checkpoint(
        self,
        *,
        path: Path,
        updater: PPOUpdater,
        normalizer: ObservationNormalizer | IdentityObservationNormalizer,
        environment_steps: int,
        unity_steps: int,
        optimizer_updates: int,
        elapsed: float,
        observation_shapes: Sequence[tuple[int, ...]],
        action_size: int,
        action_generator: torch.Generator,
        update_generator: torch.Generator,
    ) -> None:
        if self.policy is None:
            raise RuntimeError("policy is not initialized")
        protocol = _load_protocol(self.config.protocol_path)
        encoder_metadata = _structured_encoder_metadata(self.policy)
        metadata = {
            "schema_version": (
                1
                if protocol is None
                else STRUCTURED_CHECKPOINT_SCHEMA_VERSION
            ),
            "environment_steps": environment_steps,
            "unity_steps": unity_steps,
            "optimizer_updates": optimizer_updates,
            "seed": self.config.seed,
            "worker_id": self.config.worker_id,
            "observation_shapes": tuple(tuple(shape) for shape in observation_shapes),
            "action_size": action_size,
            "build_sha256": _sha256(self.config.build_path),
            "source_identity": "workspace-without-head",
            "wall_clock_seconds": elapsed,
            "torch_rng_state": torch.get_rng_state(),
            "cuda_rng_state": (
                torch.cuda.get_rng_state_all() if torch.cuda.is_available() else None
            ),
            "action_generator_state": action_generator.get_state(),
            "update_generator_state": update_generator.get_state(),
            "numpy_random_state": np.random.get_state(),
        }
        if protocol is not None:
            metadata["protocol"] = protocol.checkpoint_metadata()
            metadata["encoder"] = encoder_metadata
        save_checkpoint(
            path,
            model_state=self.policy.state_dict(),
            optimizer_state=updater.optimizer.state_dict(),
            normalizer_state=normalizer.state_dict(),
            config=self.config.to_dict(),
            metadata=metadata,
        )


def _seed_everything(seed: int) -> None:
    random.seed(seed)
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
