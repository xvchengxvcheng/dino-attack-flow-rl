from __future__ import annotations

import argparse
import json
import math
import random
import time
from dataclasses import asdict
from pathlib import Path
from typing import Sequence

import numpy as np
import torch
from mlagents_envs.side_channel.engine_configuration_channel import (
    EngineConfigurationChannel,
)

from flow_rl.algorithms.ppo import PPOUpdater
from flow_rl.data.normalization import IdentityObservationNormalizer
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.envs.parallel_unity import AsyncUnityVectorEnv, EnvironmentHandle
from flow_rl.envs.types import EnvStep
from flow_rl.envs.unity import UnityEnvAdapter
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.models.policy import GaussianActorCritic
from flow_rl.training.collector import PPOAuxiliary
from flow_rl.training.dino_batched_policy import DinoBatchedPolicyDecider
from flow_rl.training.dino_parallel_ppo import (
    DinoParallelPPOTrainer,
    DinoParallelRestartState,
)
from flow_rl.training.dino_parallel_ppo_config import (
    DinoParallelCheckpointManager,
    DinoParallelPPOConfig,
    DinoParallelResumeState,
)
from flow_rl.training.versioned_collector import (
    BootstrapTarget,
    VersionedActionInfo,
    VersionedCollector,
)
from flow_rl.tracking.parameter_audit import (
    audit_parameter_updates,
    render_parameter_update_audit_markdown,
    snapshot_named_parameters,
)
from flow_rl.tracking.dino_parallel_run import DinoParallelRunReporter


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Train parallel Dino Attack Players with versioned PPO updates."
    )
    parser.add_argument("--config", type=Path, required=True)
    return parser


def _training_launch_args(
    *, base_seed: int, environment_id: int, generation: int
) -> tuple[str, ...]:
    return (
        "--dino-training",
        "--dino-base-seed",
        str(base_seed),
        "--dino-environment-index",
        str(environment_id),
        "--dino-process-generation",
        str(generation),
    )


def _seed_everything(seed: int) -> None:
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)


def _validate_step(step: EnvStep, protocol: DinoProtocol) -> None:
    if len(step.observations) != len(protocol.observation_shapes):
        raise ValueError("Player returned the wrong observation stream count")
    for index, (stream, shape) in enumerate(
        zip(step.observations, protocol.observation_shapes)
    ):
        if stream.dtype != np.float32 or stream.shape[1:] != shape:
            raise TypeError(
                f"observations[{index}] must be float32 with feature shape {shape}"
            )
        if not np.isfinite(stream).all():
            raise ValueError(f"observations[{index}] contains a non-finite value")
    if not np.isfinite(step.rewards).all():
        raise ValueError("Player returned a non-finite reward")


def _classify_map(step: EnvStep, decision_count: int) -> str | None:
    if decision_count <= 0 or len(step.observations) < 4:
        return None
    guard_rows = np.asarray(step.observations[3][0, :, 0], dtype=np.float32)
    valid_guards = int(np.count_nonzero(guard_rows > 0.5))
    if valid_guards == 8:
        return "map1"
    if valid_guards == 11:
        return "map2"
    return None


def _build_policy_and_state(
    config: DinoParallelPPOConfig,
    protocol: DinoProtocol,
    device: torch.device,
):
    policy = GaussianActorCritic(
        observation_shapes=protocol.observation_shapes,
        action_size=protocol.action_size,
        hidden_sizes=config.hidden_sizes,
        encoder_factory=lambda: SetTransformerDinoEncoder(
            protocol,
            d_model=config.encoder_d_model,
            heads=config.encoder_heads,
            inducing_points=config.encoder_inducing_points,
            layers=config.encoder_layers,
            dropout=config.encoder_dropout,
            output_size=config.encoder_output_size,
        ),
    ).to(device)
    action_generator = torch.Generator(device=device).manual_seed(config.seed + 1_000)
    update_generator = torch.Generator().manual_seed(config.seed + 2_000)
    updater = PPOUpdater(
        policy,
        learning_rate=config.learning_rate,
        final_learning_rate=config.final_learning_rate,
        clip_range=config.clip_range,
        final_clip_range=config.final_clip_range,
        entropy_coefficient=config.entropy_coefficient,
        final_entropy_coefficient=config.final_entropy_coefficient,
        value_coefficient=config.value_coefficient,
        max_gradient_norm=config.max_gradient_norm,
        total_environment_steps=config.schedule_environment_steps,
        batch_size=config.batch_size,
        epochs=config.epochs,
        generator=update_generator,
    )
    normalizer = IdentityObservationNormalizer(
        protocol.observation_shapes,
        protocol_manifest_sha256=protocol.manifest_sha256,
    )
    return policy, updater, normalizer, action_generator, update_generator


def run_training(config: DinoParallelPPOConfig) -> dict[str, object]:
    config.validate()
    protocol = DinoProtocol.from_yaml(config.protocol_path)
    _seed_everything(config.seed)
    device = torch.device(config.device)
    (
        policy,
        updater,
        normalizer,
        action_generator,
        update_generator,
    ) = _build_policy_and_state(config, protocol, device)
    checkpoint_manager = DinoParallelCheckpointManager(
        config=config,
        protocol=protocol,
        policy=policy,
        updater=updater,
        normalizer=normalizer,
        action_generator=action_generator,
        update_generator=update_generator,
    )

    resume = DinoParallelResumeState(0, 0, 0, 0.0)
    if config.resume_checkpoint is not None:
        resume = checkpoint_manager.restore(config.resume_checkpoint)
    initial_parameters = snapshot_named_parameters(policy)

    config.run_directory.mkdir(parents=True, exist_ok=False)
    (config.run_directory / "effective-config.json").write_text(
        json.dumps(config.to_dict(), indent=2, sort_keys=True),
        encoding="utf-8",
    )

    adapters: dict[int, UnityEnvAdapter] = {}
    initial_steps: dict[int, EnvStep] = {}

    def adapter_factory(handle: EnvironmentHandle) -> UnityEnvAdapter:
        channel = EngineConfigurationChannel()
        channel.set_configuration_parameters(
            width=84,
            height=84,
            quality_level=0,
            time_scale=config.time_scale,
            target_frame_rate=-1,
            capture_frame_rate=0,
        )
        adapter = UnityEnvAdapter(
            config.build_path,
            worker_id=handle.worker_id,
            seed=handle.environment_seed,
            no_graphics=True,
            timeout_wait=config.timeout_wait,
            behavior_name=config.behavior_name,
            log_folder=handle.log_directory,
            side_channels=[channel],
            additional_args=_training_launch_args(
                base_seed=config.seed,
                environment_id=handle.environment_id,
                generation=handle.generation,
            ),
            protocol=protocol,
        )
        try:
            step = adapter.reset()
            _validate_step(step, protocol)
        except BaseException:
            adapter.close()
            raise
        adapters[handle.environment_id] = adapter
        initial_steps[handle.environment_id] = step
        return adapter

    vector = AsyncUnityVectorEnv(
        adapter_factory,
        num_envs=config.num_envs,
        base_worker_id=config.worker_base,
        base_seed=config.seed,
        log_directory=config.run_directory / "unity",
        max_consecutive_failures=config.max_consecutive_failures,
    )
    try:
        reporter = DinoParallelRunReporter(
            config.run_directory,
            environment_ids=range(config.num_envs),
            map_names=("map1", "map2"),
        )
    except BaseException:
        vector.close()
        raise

    def decide(
        environment_id: int,
        generation: int,
        policy_version: int,
        step: EnvStep,
        adapter: UnityEnvAdapter,
    ) -> VersionedActionInfo[PPOAuxiliary]:
        agent_ids = adapter.pending_agent_ids
        observations = tuple(
            np.array(stream[: len(agent_ids)], dtype=np.float32, copy=True)
            for stream in step.observations
        )
        if len(agent_ids) == 0:
            return VersionedActionInfo(
                environment_id=environment_id,
                process_generation=generation,
                policy_version=policy_version,
                agent_ids=agent_ids,
                observations=observations,
                actions=np.empty((0, protocol.action_size), dtype=np.float32),
                values=np.empty(0, dtype=np.float32),
                auxiliaries=(),
            )
        tensors = tuple(torch.as_tensor(item, device=device) for item in observations)
        with torch.inference_mode():
            output = policy.act(
                tensors,
                deterministic=False,
                generator=action_generator,
            )
        actions = output.actions.detach().cpu().numpy().astype(np.float32, copy=True)
        values = output.values.detach().cpu().numpy().astype(np.float32, copy=True)
        if not np.isfinite(actions).all() or not np.isfinite(values).all():
            raise RuntimeError("policy produced a non-finite action or value")
        return VersionedActionInfo(
            environment_id=environment_id,
            process_generation=generation,
            policy_version=policy_version,
            agent_ids=agent_ids,
            observations=observations,
            actions=actions,
            values=values,
            auxiliaries=tuple(
                PPOAuxiliary(float(value))
                for value in output.log_probs.detach().cpu().numpy()
            ),
        )

    decide_batch = DinoBatchedPolicyDecider(
        policy=policy,
        device=device,
        action_size=protocol.action_size,
        generator=action_generator,
    )

    def bootstrap(
        collector: VersionedCollector[PPOAuxiliary],
    ) -> dict[BootstrapTarget, float]:
        values: dict[BootstrapTarget, float] = {}
        for target, observations in collector.bootstrap_observations.items():
            tensors = tuple(
                torch.as_tensor(observation[None, ...], device=device)
                for observation in observations
            )
            with torch.inference_mode():
                value = policy.critic(tensors)
            number = float(value.detach().cpu().item())
            if not math.isfinite(number):
                raise RuntimeError("policy produced a non-finite bootstrap value")
            values[target] = number
        return values

    def resolve_restart(
        environment_id: int, handle: EnvironmentHandle
    ) -> DinoParallelRestartState:
        del handle
        return DinoParallelRestartState(
            adapter=adapters[environment_id],
            initial_step=initial_steps[environment_id],
        )

    started = time.perf_counter()
    next_checkpoint = (
        (resume.environment_steps // config.checkpoint_interval) + 1
    ) * config.checkpoint_interval
    optimizer_updates = resume.optimizer_updates

    def checkpoint_update(update, absolute_steps: int) -> None:
        nonlocal next_checkpoint, optimizer_updates
        optimizer_updates += 1
        current_run_seconds = time.perf_counter() - started
        elapsed = resume.wall_clock_seconds + current_run_seconds
        reporter.log_update(
            update,
            environment_steps=absolute_steps,
            update_index=optimizer_updates,
            wall_clock_seconds=elapsed,
            steps_per_second=(
                (absolute_steps - resume.environment_steps)
                / max(current_run_seconds, 1e-9)
            ),
        )
        if absolute_steps < next_checkpoint:
            return
        checkpoint_manager.save(
            config.run_directory
            / "checkpoints"
            / f"step-{absolute_steps}.pt",
            environment_steps=absolute_steps,
            policy_version=update.policy_version + 1,
            optimizer_updates=optimizer_updates,
            wall_clock_seconds=elapsed,
        )
        while next_checkpoint <= absolute_steps:
            next_checkpoint += config.checkpoint_interval

    trainer = DinoParallelPPOTrainer(
        vector_env=vector,
        adapters=adapters,
        initial_steps=initial_steps,
        updater=updater,
        device=device,
        rollout_size=config.rollout_size,
        total_environment_steps=config.total_environment_steps,
        initial_environment_steps=resume.environment_steps,
        gamma=config.gamma,
        gae_lambda=config.gae_lambda,
        decide=decide,
        bootstrap=bootstrap,
        map_classifier=_classify_map,
        restart_state_resolver=resolve_restart,
        poll_timeout=config.poll_timeout,
        max_consecutive_no_progress=config.max_consecutive_no_progress,
        initial_policy_version=resume.policy_version,
        on_update_completed=checkpoint_update,
        sampling_mode=config.sampling_mode,
        decide_batch=decide_batch,
        inference_batch_size=config.inference_batch_size,
        inference_batch_wait_seconds=config.inference_batch_wait_seconds,
    )
    try:
        summary = trainer.train()
    finally:
        reporter.close()
    elapsed = resume.wall_clock_seconds + time.perf_counter() - started
    final_version = summary.updates[-1].policy_version + 1
    checkpoint_manager.save(
        config.run_directory / "checkpoints" / "final.pt",
        environment_steps=summary.final_environment_steps,
        policy_version=final_version,
        optimizer_updates=optimizer_updates,
        wall_clock_seconds=elapsed,
    )
    parameter_audit = audit_parameter_updates(
        initial_parameters,
        snapshot_named_parameters(policy),
    )
    audit_json_path = config.run_directory / "parameter-update-audit.json"
    audit_markdown_path = config.run_directory / "parameter-update-audit.md"
    audit_json_path.write_text(
        json.dumps(parameter_audit, indent=2, sort_keys=True, allow_nan=False),
        encoding="utf-8",
    )
    audit_markdown_path.write_text(
        render_parameter_update_audit_markdown(parameter_audit),
        encoding="utf-8",
    )
    payload: dict[str, object] = {
        "status": "PASS",
        "summary": asdict(summary),
        "optimizer_updates": optimizer_updates,
        "wall_clock_seconds": elapsed,
        "steps_per_second": (
            (summary.final_environment_steps - resume.environment_steps)
            / max(time.perf_counter() - started, 1e-9)
        ),
        "inference_batching": asdict(decide_batch.statistics()),
        "final_checkpoint": str(
            (config.run_directory / "checkpoints" / "final.pt").resolve()
        ),
        "parameter_update_audit": {
            "summary": parameter_audit["summary"],
            "json_path": str(audit_json_path.resolve()),
            "markdown_path": str(audit_markdown_path.resolve()),
        },
    }
    (config.run_directory / "summary.json").write_text(
        json.dumps(payload, indent=2, sort_keys=True),
        encoding="utf-8",
    )
    return payload


def main(argv: Sequence[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    config = DinoParallelPPOConfig.from_yaml(args.config)
    result = run_training(config)
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
