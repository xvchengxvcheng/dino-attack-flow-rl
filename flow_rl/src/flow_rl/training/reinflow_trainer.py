from __future__ import annotations

import hashlib
import json
import math
import random
import time
from collections.abc import Callable, Sequence
from pathlib import Path

import numpy as np
import torch
from mlagents_envs.side_channel.engine_configuration_channel import (
    EngineConfigurationChannel,
)
from mlagents_envs.side_channel.side_channel import SideChannel

from flow_rl.algorithms.reinflow import ReinFlowUpdater
from flow_rl.data.normalization import ObservationNormalizer
from flow_rl.envs.unity import UnityEnvAdapter
from flow_rl.models.reinflow_policy import ReinFlowActorCritic
from flow_rl.tracking.agent_liveness import AgentLivenessTracker
from flow_rl.tracking.checkpoint import (
    FlowSolverConfig,
    load_checkpoint,
    save_checkpoint,
    validate_flow_bc_checkpoint,
    validate_reinflow_checkpoint,
)
from flow_rl.tracking.episodes import EpisodeTracker
from flow_rl.tracking.run import RunLogger
from flow_rl.training.reinflow_collector import ReinFlowCollector
from flow_rl.training.reinflow_config import ReinFlowTrainingConfig
from flow_rl.training.trainer import TrainingAdapter, TrainingSummary


REINFLOW_SOURCE_COMMIT = "e722e151bed767f3ffef47527cf697f2358af55d"
AdapterFactory = Callable[..., TrainingAdapter]


def _default_adapter_factory(
    *,
    config: ReinFlowTrainingConfig,
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
    )


class ReinFlowTrainer:
    def __init__(
        self,
        config: ReinFlowTrainingConfig,
        *,
        adapter_factory: AdapterFactory = _default_adapter_factory,
    ) -> None:
        self.config = config
        self._adapter_factory = adapter_factory
        self.policy: ReinFlowActorCritic | None = None

    def train(self) -> TrainingSummary:
        self.config.validate()
        _seed_everything(self.config.seed)
        device = torch.device(self.config.device)
        bc_payload = load_checkpoint(self.config.bc_checkpoint_path, map_location=device)
        validate_flow_bc_checkpoint(bc_payload)
        engine_channel = EngineConfigurationChannel()
        engine_channel.set_configuration_parameters(
            width=84,
            height=84,
            quality_level=0,
            time_scale=self.config.time_scale,
            target_frame_rate=-1,
            capture_frame_rate=0,
        )
        started = time.perf_counter()
        unity_log_directory = self.config.run_directory / "unity"
        with RunLogger(self.config.run_directory, self.config.to_dict()) as logger:
            unity_log_directory.mkdir(parents=True, exist_ok=False)
            with self._adapter_factory(
                config=self.config,
                log_folder=unity_log_directory,
                side_channels=[engine_channel],
            ) as adapter:
                initial_step = adapter.reset()
                initial_ids = tuple(int(value) for value in adapter.pending_agent_ids)
                if not initial_ids:
                    raise RuntimeError("Unity reset produced no decision Agents")
                observation_shapes = tuple(
                    tuple(observation.shape[1:])
                    for observation in initial_step.observations
                )
                if observation_shapes != self.config.observation_shapes:
                    raise ValueError("Unity observation shapes do not match Flow BC")
                if adapter.continuous_action_size != self.config.action_size:
                    raise ValueError("Unity action size does not match Flow BC")
                self.policy = ReinFlowActorCritic(
                    observation_shapes=observation_shapes,
                    action_size=self.config.action_size,
                    state_size=self.config.state_size,
                    time_embedding_size=self.config.time_embedding_size,
                    velocity_hidden_sizes=self.config.velocity_hidden_sizes,
                    critic_hidden_sizes=self.config.critic_hidden_sizes,
                    noise_hidden_sizes=self.config.noise_hidden_sizes,
                    nfe=self.config.nfe,
                    min_noise_std=self.config.min_noise_std,
                    max_noise_std=self.config.max_noise_std,
                ).to(device)
                self.policy.actor.load_state_dict(bc_payload["model_state"], strict=True)
                normalizer = ObservationNormalizer(observation_shapes)
                normalizer.load_state_dict(bc_payload["normalizer_state"])
                frozen_normalizer_count = int(normalizer.state_dict()["count"])
                action_generator = torch.Generator(device=device).manual_seed(
                    self.config.seed + 1_000
                )
                update_generator = torch.Generator().manual_seed(
                    self.config.seed + 2_000
                )
                collector = ReinFlowCollector(
                    policy=self.policy,
                    normalizer=normalizer,
                    device=device,
                    gamma=self.config.gamma,
                    gae_lambda=self.config.gae_lambda,
                    nfe=self.config.nfe,
                    generator=action_generator,
                )
                updater = ReinFlowUpdater(
                    self.policy,
                    actor_learning_rate=self.config.actor_learning_rate,
                    critic_learning_rate=self.config.critic_learning_rate,
                    clip_range=self.config.clip_range,
                    entropy_coefficient=self.config.entropy_coefficient,
                    max_gradient_norm=self.config.max_gradient_norm,
                    batch_size=self.config.batch_size,
                    epochs=self.config.epochs,
                    critic_warmup_environment_steps=(
                        self.config.critic_warmup_environment_steps
                    ),
                    log_prob_min=self.config.log_prob_min,
                    log_prob_max=self.config.log_prob_max,
                    generator=update_generator,
                )
                environment_steps = unity_steps = optimizer_updates = 0
                previous_elapsed = 0.0
                if self.config.resume_checkpoint is not None:
                    (
                        environment_steps,
                        unity_steps,
                        optimizer_updates,
                        previous_elapsed,
                    ) = self._restore(
                        path=self.config.resume_checkpoint,
                        updater=updater,
                        normalizer=normalizer,
                        action_generator=action_generator,
                        update_generator=update_generator,
                    )
                    if environment_steps >= self.config.total_environment_steps:
                        raise ValueError("resume checkpoint already reached total steps")
                output = collector.reset(initial_step)
                actions = output.actions
                episode_tracker = EpisodeTracker()
                liveness = AgentLivenessTracker(
                    initial_ids,
                    max_absence_steps=self.config.max_agent_absence_steps,
                )
                session_unity_steps = episode_count = 0
                terminated_episodes = truncated_episodes = 0
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
                        final_drain_ids.update(int(value) for value in step.agent_ids)
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
                        self._save(
                            path=self.config.run_directory
                            / "checkpoints"
                            / f"step-{next_checkpoint}.pt",
                            updater=updater,
                            normalizer=normalizer,
                            environment_steps=environment_steps,
                            unity_steps=unity_steps,
                            optimizer_updates=optimizer_updates,
                            elapsed=previous_elapsed + time.perf_counter() - started,
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
                        collector.build_batch(), environment_steps=environment_steps
                    )
                    logger.log_update(
                        metrics.as_dict(),
                        environment_steps=environment_steps,
                        update_index=optimizer_updates,
                    )
                if int(normalizer.state_dict()["count"]) != frozen_normalizer_count:
                    raise RuntimeError("ReinFlow normalizer changed during online training")
                liveness.finalize(observed_agent_ids=final_drain_ids)
                elapsed = previous_elapsed + time.perf_counter() - started
                final_checkpoint = self.config.run_directory / "checkpoints" / "final.pt"
                self._save(
                    path=final_checkpoint,
                    updater=updater,
                    normalizer=normalizer,
                    environment_steps=environment_steps,
                    unity_steps=unity_steps,
                    optimizer_updates=optimizer_updates,
                    elapsed=elapsed,
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
                        math.isfinite(value) for value in (elapsed, steps_per_second)
                    ),
                    final_checkpoint=str(final_checkpoint),
                )
                logger.log_metrics(
                    {
                        "runtime/steps_per_second": steps_per_second,
                        "runtime/unity_steps": float(unity_steps),
                        "runtime/parameter_count": float(parameter_count),
                        "runtime/nfe": float(self.config.nfe),
                        "runtime/normalizer_count": float(frozen_normalizer_count),
                    },
                    environment_steps,
                )
        with (self.config.run_directory / "summary.json").open(
            "w", encoding="utf-8", newline="\n"
        ) as handle:
            json.dump(summary.to_dict(), handle, indent=2, sort_keys=True)
            handle.write("\n")
        return summary

    def _restore(
        self,
        *,
        path: Path,
        updater: ReinFlowUpdater,
        normalizer: ObservationNormalizer,
        action_generator: torch.Generator,
        update_generator: torch.Generator,
    ) -> tuple[int, int, int, float]:
        assert self.policy is not None
        payload = load_checkpoint(path, map_location=self.config.device)
        validate_reinflow_checkpoint(payload)
        stable_keys = (
            "total_environment_steps", "rollout_size", "batch_size", "epochs",
            "gamma", "gae_lambda", "actor_learning_rate", "critic_learning_rate",
            "clip_range", "entropy_coefficient", "max_gradient_norm",
            "critic_hidden_sizes", "noise_hidden_sizes", "nfe", "horizon_steps",
            "min_noise_std", "max_noise_std", "log_prob_min", "log_prob_max",
            "critic_warmup_environment_steps", "seed", "bc_checkpoint_sha256",
        )
        current = self.config.to_dict()
        mismatched = [
            key for key in stable_keys
            if payload["config"].get(key) != current.get(key)
        ]
        metadata = payload["metadata"]
        if metadata["build_sha256"] != _sha256(self.config.build_path):
            mismatched.append("build_sha256")
        if metadata["reinflow_source_commit"] != REINFLOW_SOURCE_COMMIT:
            mismatched.append("reinflow_source_commit")
        if mismatched:
            raise ValueError(
                f"resume checkpoint configuration mismatch: {sorted(set(mismatched))}"
            )
        self.policy.load_state_dict(payload["model_state"], strict=True)
        updater.actor_optimizer.load_state_dict(payload["optimizer_state"]["actor"])
        updater.critic_optimizer.load_state_dict(payload["optimizer_state"]["critic"])
        normalizer.load_state_dict(payload["normalizer_state"])
        torch.set_rng_state(metadata["torch_rng_state"].cpu())
        if torch.cuda.is_available() and metadata["cuda_rng_state"] is not None:
            torch.cuda.set_rng_state_all(
                [state.cpu() for state in metadata["cuda_rng_state"]]
            )
        action_generator.set_state(metadata["action_generator_state"].cpu())
        update_generator.set_state(metadata["update_generator_state"].cpu())
        np.random.set_state(metadata["numpy_random_state"])
        random.setstate(metadata["python_random_state"])
        return (
            int(metadata["environment_steps"]),
            int(metadata["unity_steps"]),
            int(metadata["optimizer_updates"]),
            float(metadata["wall_clock_seconds"]),
        )

    def _save(
        self,
        *,
        path: Path,
        updater: ReinFlowUpdater,
        normalizer: ObservationNormalizer,
        environment_steps: int,
        unity_steps: int,
        optimizer_updates: int,
        elapsed: float,
        action_generator: torch.Generator,
        update_generator: torch.Generator,
    ) -> None:
        assert self.policy is not None
        metadata = {
            "schema_version": 2,
            "algorithm": "reinflow",
            "environment_steps": environment_steps,
            "unity_steps": unity_steps,
            "optimizer_updates": optimizer_updates,
            "seed": self.config.seed,
            "worker_id": self.config.worker_id,
            "observation_shapes": self.config.observation_shapes,
            "action_size": self.config.action_size,
            "build_sha256": _sha256(self.config.build_path),
            "bc_checkpoint_sha256": self.config.bc_checkpoint_sha256,
            "reinflow_source_commit": REINFLOW_SOURCE_COMMIT,
            "solver": FlowSolverConfig(
                nfe=self.config.nfe, action_transform="clamp"
            ).to_dict(),
            "wall_clock_seconds": elapsed,
            "torch_rng_state": torch.get_rng_state(),
            "cuda_rng_state": (
                torch.cuda.get_rng_state_all() if torch.cuda.is_available() else None
            ),
            "action_generator_state": action_generator.get_state(),
            "update_generator_state": update_generator.get_state(),
            "numpy_random_state": np.random.get_state(),
            "python_random_state": random.getstate(),
        }
        save_checkpoint(
            path,
            model_state=self.policy.state_dict(),
            optimizer_state={
                "actor": updater.actor_optimizer.state_dict(),
                "critic": updater.critic_optimizer.state_dict(),
            },
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
