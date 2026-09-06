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
from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel
from mlagents_envs.side_channel.side_channel import SideChannel

from flow_rl.algorithms.policyflow import PolicyFlowBatch, PolicyFlowUpdater
from flow_rl.data.gae import compute_gae
from flow_rl.data.normalization import ObservationNormalizer
from flow_rl.envs.unity import UnityEnvAdapter
from flow_rl.models.policyflow_policy import PolicyFlowActorCritic
from flow_rl.tracking.agent_liveness import AgentLivenessTracker
from flow_rl.tracking.checkpoint import (
    FlowSolverConfig,
    POLICYFLOW_SOURCE_COMMIT,
    load_checkpoint,
    save_checkpoint,
    validate_flow_bc_checkpoint,
    validate_policyflow_checkpoint,
)
from flow_rl.tracking.episodes import EpisodeTracker
from flow_rl.tracking.run import RunLogger
from flow_rl.training.policyflow_collector import PolicyFlowAuxiliary, PolicyFlowCollector
from flow_rl.training.policyflow_config import PolicyFlowTrainingConfig
from flow_rl.training.trainer import TrainingAdapter, TrainingSummary
from flow_rl.training.versioned_collector import VersionedBatch, VersionedCollector


AdapterFactory = Callable[..., TrainingAdapter]
PolicyFactory = Callable[
    [Sequence[tuple[int, ...]], int, PolicyFlowTrainingConfig, torch.device],
    PolicyFlowActorCritic,
]


def _build_versioned_policyflow_batch(
    versioned: VersionedBatch[PolicyFlowAuxiliary],
    *,
    gamma: float,
    gae_lambda: float,
    device: torch.device,
) -> PolicyFlowBatch:
    observation_streams: list[list[np.ndarray]] | None = None
    flow_x0: list[np.ndarray] = []
    actions_prior: list[np.ndarray] = []
    delta_actions: list[np.ndarray] = []
    actions: list[np.ndarray] = []
    old_delta_std: list[np.ndarray] = []
    old_delta_log_probs: list[float] = []
    old_velocity_grid: list[np.ndarray] = []
    old_values: list[float] = []
    advantages: list[np.ndarray] = []
    returns: list[np.ndarray] = []
    for trajectory in versioned.trajectories().values():
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
            if observation_streams is None:
                observation_streams = [[] for _ in transition.observation]
            for stream, observation in zip(
                observation_streams, transition.observation
            ):
                stream.append(observation)
            auxiliary = transition.auxiliary
            flow_x0.append(auxiliary.flow_x0)
            actions_prior.append(auxiliary.actions_prior)
            delta_actions.append(auxiliary.delta_actions)
            actions.append(transition.action)
            old_delta_std.append(auxiliary.old_delta_std)
            old_delta_log_probs.append(auxiliary.old_delta_log_prob)
            old_velocity_grid.append(auxiliary.old_velocity_grid)
            old_values.append(transition.value)
    if observation_streams is None:
        raise RuntimeError("no versioned PolicyFlow transitions are available")
    tensor = lambda values: torch.as_tensor(np.stack(values), device=device)
    return PolicyFlowBatch(
        observations=tuple(tensor(stream) for stream in observation_streams),
        flow_x0=tensor(flow_x0),
        actions_prior=tensor(actions_prior),
        delta_actions=tensor(delta_actions),
        actions=tensor(actions),
        old_delta_std=tensor(old_delta_std),
        old_delta_log_probs=torch.as_tensor(
            np.asarray(old_delta_log_probs, dtype=np.float32), device=device
        ),
        old_velocity_grid=tensor(old_velocity_grid),
        old_values=torch.as_tensor(
            np.asarray(old_values, dtype=np.float32), device=device
        ),
        advantages=torch.as_tensor(np.concatenate(advantages), device=device),
        returns=torch.as_tensor(np.concatenate(returns), device=device),
    )


def update_versioned_policyflow(
    collector: VersionedCollector[PolicyFlowAuxiliary],
    updater: PolicyFlowUpdater,
    *,
    gamma: float,
    gae_lambda: float,
    device: torch.device,
    environment_steps: int,
) -> object:
    del environment_steps
    versioned = collector.build_batch()
    batch = _build_versioned_policyflow_batch(
        versioned, gamma=gamma, gae_lambda=gae_lambda, device=device
    )
    result = updater.update(batch)
    collector.complete_optimizer_update(versioned.policy_version, succeeded=True)
    metrics = result.as_dict()
    valid = np.asarray(
        [transition.next_observation[0][2] for transition in versioned.transitions],
        dtype=np.float32,
    )
    metrics["action_valid_rate"] = float(np.mean(valid >= 0.5))
    metrics["illegal_action_rate"] = float(np.mean(valid < 0.5))
    return metrics


def initialize_actor_from_flow_bc(
    policy: PolicyFlowActorCritic,
    checkpoint_path: Path,
) -> None:
    payload = load_checkpoint(checkpoint_path, map_location="cpu")
    validate_flow_bc_checkpoint(payload)
    policy.actor.load_state_dict(payload["model_state"], strict=True)
    policy.refresh_snapshot()


def _default_policy_factory(
    observation_shapes: Sequence[tuple[int, ...]],
    action_size: int,
    config: PolicyFlowTrainingConfig,
    device: torch.device,
) -> PolicyFlowActorCritic:
    return PolicyFlowActorCritic(
        observation_shapes=observation_shapes,
        action_size=action_size,
        state_size=config.state_size,
        time_embedding_size=config.time_embedding_size,
        velocity_hidden_sizes=config.velocity_hidden_sizes,
        critic_hidden_sizes=config.critic_hidden_sizes,
        solver_steps=config.solver_steps,
    ).to(device)


def _default_adapter_factory(
    *,
    config: PolicyFlowTrainingConfig,
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


class PolicyFlowTrainer:
    def __init__(
        self,
        config: PolicyFlowTrainingConfig,
        *,
        adapter_factory: AdapterFactory = _default_adapter_factory,
        policy_factory: PolicyFactory = _default_policy_factory,
    ) -> None:
        self.config = config
        self._adapter_factory = adapter_factory
        self._policy_factory = policy_factory
        self.policy: PolicyFlowActorCritic | None = None

    def train(self) -> TrainingSummary:
        self.config.validate()
        _seed_everything(self.config.seed)
        device = torch.device(self.config.device)
        engine = EngineConfigurationChannel()
        engine.set_configuration_parameters(
            width=84, height=84, quality_level=0,
            time_scale=self.config.time_scale,
            target_frame_rate=-1, capture_frame_rate=0,
        )
        started = time.perf_counter()
        unity_directory = self.config.run_directory / "unity"
        with RunLogger(self.config.run_directory, self.config.to_dict()) as logger:
            unity_directory.mkdir(parents=True, exist_ok=False)
            with self._adapter_factory(
                config=self.config,
                log_folder=unity_directory,
                side_channels=[engine],
            ) as adapter:
                initial_step = adapter.reset()
                initial_ids = tuple(int(value) for value in adapter.pending_agent_ids)
                if not initial_ids:
                    raise RuntimeError("Unity reset produced no decision Agents")
                observation_shapes = tuple(
                    tuple(observation.shape[1:]) for observation in initial_step.observations
                )
                action_size = adapter.continuous_action_size
                if observation_shapes != self.config.observation_shapes:
                    raise ValueError("Unity observations do not match normalizer checkpoint")
                if action_size != self.config.action_size:
                    raise ValueError("Unity action size does not match normalizer checkpoint")
                self.policy = self._policy_factory(
                    observation_shapes, action_size, self.config, device
                )
                if self.config.actor_initialization_checkpoint_path is not None:
                    initialize_actor_from_flow_bc(
                        self.policy,
                        self.config.actor_initialization_checkpoint_path,
                    )
                normalizer = ObservationNormalizer(observation_shapes)
                source = load_checkpoint(
                    self.config.normalizer_checkpoint_path, map_location="cpu"
                )
                normalizer.load_state_dict(source["normalizer_state"])
                frozen_count = int(normalizer.state_dict()["count"])
                action_generator = torch.Generator(device=device).manual_seed(
                    self.config.seed + 7_000
                )
                update_generator = torch.Generator(device=device).manual_seed(
                    self.config.seed + 8_000
                )
                collector = PolicyFlowCollector(
                    policy=self.policy,
                    normalizer=normalizer,
                    device=device,
                    gamma=self.config.gamma,
                    gae_lambda=self.config.gae_lambda,
                    velocity_nfe=self.config.velocity_nfe,
                    generator=action_generator,
                )
                updater = PolicyFlowUpdater(
                    self.policy,
                    actor_learning_rate=self.config.actor_learning_rate,
                    critic_learning_rate=self.config.critic_learning_rate,
                    clip_range=self.config.clip_range,
                    gaussian_entropy_coefficient=self.config.gaussian_entropy_coefficient,
                    brownian_coefficient=self.config.brownian_coefficient,
                    value_clip=self.config.value_clip,
                    max_gradient_norm=self.config.max_gradient_norm,
                    batch_size=self.config.batch_size,
                    epochs=self.config.epochs,
                    generator=update_generator,
                )
                environment_steps = unity_steps = optimizer_updates = 0
                previous_elapsed = 0.0
                if self.config.resume_checkpoint is not None:
                    environment_steps, unity_steps, optimizer_updates, previous_elapsed = self._restore(
                        path=self.config.resume_checkpoint,
                        updater=updater,
                        normalizer=normalizer,
                        action_generator=action_generator,
                        update_generator=update_generator,
                    )
                    if environment_steps >= self.config.total_environment_steps:
                        raise ValueError("resume checkpoint already reached total_environment_steps")
                output = collector.reset(initial_step)
                actions = output.actions
                episodes = EpisodeTracker()
                liveness = AgentLivenessTracker(
                    initial_ids, max_absence_steps=self.config.max_agent_absence_steps
                )
                session_unity_steps = episode_count = terminated_count = truncated_count = 0
                final_drain_ids: set[int] = set()
                next_checkpoint = (
                    environment_steps // self.config.checkpoint_interval + 1
                ) * self.config.checkpoint_interval
                while True:
                    submitted = actions.shape[0]
                    step = adapter.step(actions)
                    environment_steps += submitted
                    unity_steps += 1
                    session_unity_steps += 1
                    liveness.observe(step, unity_step=session_unity_steps)
                    if environment_steps >= self.config.total_environment_steps:
                        final_drain_ids.update(int(value) for value in step.agent_ids)
                    for episode in episodes.record(step):
                        episode_count += 1
                        terminated_count += int(episode.terminated)
                        truncated_count += int(episode.truncated)
                        logger.log_episode(episode, environment_steps)
                    output = collector.step(step)
                    actions = output.actions
                    if collector.completed_transition_count >= self.config.rollout_size:
                        optimizer_updates += 1
                        metrics = updater.update(collector.build_batch())
                        logger.log_update(
                            metrics.as_dict(),
                            environment_steps=environment_steps,
                            update_index=optimizer_updates,
                        )
                        # The update refreshes the frozen behavior snapshot. The
                        # current decision batch was sampled before that refresh;
                        # replace its pending actions before they are submitted.
                        actions = collector.replace_pending(step).actions
                        # Periodic checkpoints are written only after a complete
                        # rollout update, so resume never silently drops partial
                        # trajectories or pending decisions.
                        while environment_steps >= next_checkpoint:
                            self._save(
                                path=self.config.run_directory / "checkpoints" / f"step-{environment_steps}.pt",
                                updater=updater, normalizer=normalizer,
                                environment_steps=environment_steps, unity_steps=unity_steps,
                                optimizer_updates=optimizer_updates,
                                in_flight_decisions=len(collector.pending_agent_ids),
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
                    metrics = updater.update(collector.build_batch())
                    logger.log_update(
                        metrics.as_dict(), environment_steps=environment_steps,
                        update_index=optimizer_updates,
                    )
                if int(normalizer.state_dict()["count"]) != frozen_count:
                    raise RuntimeError("PolicyFlow normalizer changed during training")
                liveness.finalize(observed_agent_ids=final_drain_ids)
                elapsed = previous_elapsed + time.perf_counter() - started
                final_checkpoint = self.config.run_directory / "checkpoints" / "final.pt"
                self._save(
                    path=final_checkpoint, updater=updater, normalizer=normalizer,
                    environment_steps=environment_steps, unity_steps=unity_steps,
                    optimizer_updates=optimizer_updates, elapsed=elapsed,
                    in_flight_decisions=len(collector.pending_agent_ids),
                    action_generator=action_generator, update_generator=update_generator,
                )
                throughput = environment_steps / elapsed
                summary = TrainingSummary(
                    environment_steps=environment_steps,
                    unity_steps=unity_steps,
                    optimizer_updates=optimizer_updates,
                    episodes=episode_count,
                    terminated_episodes=terminated_count,
                    truncated_episodes=truncated_count,
                    wall_clock_seconds=elapsed,
                    steps_per_second=throughput,
                    parameter_count=sum(
                        parameter.numel()
                        for parameter in self.policy.parameters()
                        if parameter.requires_grad
                    ),
                    initial_agent_ids=tuple(sorted(initial_ids)),
                    final_drain_agent_ids=tuple(sorted(final_drain_ids)),
                    maximum_agent_absence_steps_observed=liveness.maximum_absence_steps_observed,
                    all_finite=all(math.isfinite(value) for value in (elapsed, throughput)),
                    final_checkpoint=str(final_checkpoint),
                )
                logger.log_metrics(
                    {
                        "runtime/steps_per_second": throughput,
                        "runtime/unity_steps": float(unity_steps),
                        "runtime/parameter_count": float(summary.parameter_count),
                        "runtime/solver_steps": float(self.config.solver_steps),
                        "runtime/velocity_nfe": float(self.config.velocity_nfe),
                        "runtime/normalizer_count": float(frozen_count),
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
        updater: PolicyFlowUpdater,
        normalizer: ObservationNormalizer,
        action_generator: torch.Generator,
        update_generator: torch.Generator,
    ) -> tuple[int, int, int, float]:
        assert self.policy is not None
        payload = load_checkpoint(path, map_location=self.config.device)
        validate_policyflow_checkpoint(payload)
        stable = (
            "rollout_size", "batch_size", "epochs", "gamma", "gae_lambda",
            "actor_learning_rate", "critic_learning_rate", "clip_range",
            "gaussian_entropy_coefficient", "brownian_coefficient", "value_clip",
            "max_gradient_norm", "state_size", "time_embedding_size",
            "velocity_hidden_sizes", "critic_hidden_sizes", "solver_steps",
            "horizon_steps", "seed", "normalizer_checkpoint_sha256",
            "actor_initialization_checkpoint_sha256",
        )
        current = self.config.to_dict()
        mismatched = [key for key in stable if payload["config"].get(key) != current.get(key)]
        metadata = payload["metadata"]
        if metadata["build_sha256"] != _sha256(self.config.build_path):
            mismatched.append("build_sha256")
        if metadata["policyflow_source_commit"] != POLICYFLOW_SOURCE_COMMIT:
            mismatched.append("policyflow_source_commit")
        if mismatched:
            raise ValueError(f"resume checkpoint configuration mismatch: {sorted(set(mismatched))}")
        self.policy.load_state_dict(payload["model_state"], strict=True)
        updater.actor_optimizer.load_state_dict(payload["optimizer_state"]["actor"])
        updater.critic_optimizer.load_state_dict(payload["optimizer_state"]["critic"])
        normalizer.load_state_dict(payload["normalizer_state"])
        torch.set_rng_state(metadata["torch_rng_state"].cpu())
        if torch.cuda.is_available() and metadata["cuda_rng_state"] is not None:
            torch.cuda.set_rng_state_all([state.cpu() for state in metadata["cuda_rng_state"]])
        action_generator.set_state(metadata["action_generator_state"].cpu())
        update_generator.set_state(metadata["update_generator_state"].cpu())
        np.random.set_state(metadata["numpy_random_state"])
        random.setstate(metadata["python_random_state"])
        return (
            int(metadata["environment_steps"]), int(metadata["unity_steps"]),
            int(metadata["optimizer_updates"]), float(metadata["wall_clock_seconds"]),
        )

    def _save(
        self,
        *,
        path: Path,
        updater: PolicyFlowUpdater,
        normalizer: ObservationNormalizer,
        environment_steps: int,
        unity_steps: int,
        optimizer_updates: int,
        in_flight_decisions: int,
        elapsed: float,
        action_generator: torch.Generator,
        update_generator: torch.Generator,
    ) -> None:
        assert self.policy is not None
        metadata = {
            "schema_version": 3,
            "rollout_boundary": True,
            "restart_boundary": True,
            "in_flight_decisions": int(in_flight_decisions),
            "algorithm": "policyflow",
            "environment_steps": environment_steps,
            "unity_steps": unity_steps,
            "optimizer_updates": optimizer_updates,
            "seed": self.config.seed,
            "worker_id": self.config.worker_id,
            "observation_shapes": self.config.observation_shapes,
            "action_size": self.config.action_size,
            "build_sha256": _sha256(self.config.build_path),
            "normalizer_checkpoint_sha256": self.config.normalizer_checkpoint_sha256,
            "actor_initialization_checkpoint_sha256": (
                self.config.actor_initialization_checkpoint_sha256
            ),
            "policyflow_source_commit": POLICYFLOW_SOURCE_COMMIT,
            "solver_steps": self.config.solver_steps,
            "solver": FlowSolverConfig(
                method="midpoint", nfe=self.config.velocity_nfe, action_transform="tanh"
            ).to_dict(),
            "wall_clock_seconds": elapsed,
            "torch_rng_state": torch.get_rng_state(),
            "cuda_rng_state": torch.cuda.get_rng_state_all() if torch.cuda.is_available() else None,
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
