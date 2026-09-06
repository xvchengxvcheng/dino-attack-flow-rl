"""Formal restartable multi-Player FPO training for Dino Attack."""

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
from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel

from flow_rl.algorithms.fpo import FPOUpdater
from flow_rl.cli.train_dino_parallel_ppo import (
    _classify_map,
    _training_launch_args,
    _validate_step,
)
from flow_rl.data.normalization import IdentityObservationNormalizer
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.envs.parallel_unity import AsyncUnityVectorEnv, EnvironmentHandle
from flow_rl.envs.types import EnvStep
from flow_rl.envs.unity import UnityEnvAdapter
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.models.fpo_policy import FlowActorCritic
from flow_rl.tracking.dino_parallel_run import DinoParallelRunReporter
from flow_rl.tracking.parameter_audit import (
    audit_parameter_updates,
    render_parameter_update_audit_markdown,
    snapshot_named_parameters,
)
from flow_rl.training.dino_batched_policy import DinoBatchedFPOPolicyDecider
from flow_rl.training.dino_parallel_fpo_config import (
    DinoParallelFPOCheckpointManager,
    DinoParallelFPOConfig,
)
from flow_rl.training.dino_parallel_ppo import (
    DinoParallelDecisionRequest,
    DinoParallelPPOTrainer,
    DinoParallelRestartState,
)
from flow_rl.training.dino_parallel_ppo_config import DinoParallelResumeState
from flow_rl.training.fpo_collector import FPOAuxiliary
from flow_rl.training.fpo_trainer import update_versioned_fpo
from flow_rl.training.versioned_collector import (
    BootstrapTarget,
    VersionedActionInfo,
    VersionedCollector,
)


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Train parallel Dino Players with FPO.")
    parser.add_argument("--config", type=Path, required=True)
    return parser


def _seed_everything(seed: int) -> None:
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)


def _build_policy_and_state(
    config: DinoParallelFPOConfig,
    protocol: DinoProtocol,
    device: torch.device,
):
    encoder_factory = lambda: SetTransformerDinoEncoder(
        protocol,
        d_model=config.encoder_d_model,
        heads=config.encoder_heads,
        inducing_points=config.encoder_inducing_points,
        layers=config.encoder_layers,
        dropout=config.encoder_dropout,
        output_size=config.encoder_output_size,
    )
    policy = FlowActorCritic(
        observation_shapes=protocol.observation_shapes,
        action_size=protocol.action_size,
        state_size=config.state_size,
        time_embedding_size=config.time_embedding_size,
        velocity_hidden_sizes=config.velocity_hidden_sizes,
        critic_hidden_sizes=config.critic_hidden_sizes,
        encoder_factory=encoder_factory,
    ).to(device)
    action_generator = torch.Generator(device=device).manual_seed(config.seed + 1_000)
    update_generator = torch.Generator().manual_seed(config.seed + 2_000)
    updater = FPOUpdater(
        policy,
        learning_rate=config.learning_rate,
        final_learning_rate=config.final_learning_rate,
        clip_range=config.clip_range,
        final_clip_range=config.final_clip_range,
        max_gradient_norm=config.max_gradient_norm,
        total_environment_steps=config.schedule_environment_steps,
        batch_size=config.batch_size,
        epochs=config.epochs,
        difference_clip=config.difference_clip,
        positive_advantage=config.positive_advantage,
        generator=update_generator,
    )
    normalizer = IdentityObservationNormalizer(
        protocol.observation_shapes,
        protocol_manifest_sha256=protocol.manifest_sha256,
    )
    return policy, updater, normalizer, action_generator, update_generator


def run_training(config: DinoParallelFPOConfig) -> dict[str, object]:
    config.validate()
    protocol = DinoProtocol.from_yaml(config.protocol_path)
    _seed_everything(config.seed)
    device = torch.device(config.device)
    policy, updater, normalizer, action_generator, update_generator = (
        _build_policy_and_state(config, protocol, device)
    )
    checkpoint_manager = DinoParallelFPOCheckpointManager(
        config=config,
        protocol=protocol,
        policy=policy,
        updater=updater,
        normalizer=normalizer,
        action_generator=action_generator,
        update_generator=update_generator,
    )
    resume = DinoParallelResumeState(0, 0, 0, 0.0)
    warm_start_source = None
    if config.resume_checkpoint is not None:
        resume = checkpoint_manager.restore(config.resume_checkpoint)
    elif config.warm_start_checkpoint is not None:
        warm_start_source = checkpoint_manager.warm_start(
            config.warm_start_checkpoint
        )
    initial_parameters = snapshot_named_parameters(policy)
    config.run_directory.mkdir(parents=True, exist_ok=False)
    (config.run_directory / "effective-config.json").write_text(
        json.dumps(config.to_dict(), indent=2, sort_keys=True), encoding="utf-8"
    )

    adapters: dict[int, UnityEnvAdapter] = {}
    initial_steps: dict[int, EnvStep] = {}

    def adapter_factory(handle: EnvironmentHandle) -> UnityEnvAdapter:
        channel = EngineConfigurationChannel()
        channel.set_configuration_parameters(
            width=84, height=84, quality_level=0, time_scale=config.time_scale,
            target_frame_rate=-1, capture_frame_rate=0,
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

    def decide(environment_id, generation, policy_version, step, adapter):
        return decide_batch((DinoParallelDecisionRequest(
            environment_id=environment_id,
            process_generation=generation,
            policy_version=policy_version,
            step=step,
            adapter=adapter,
        ),))[environment_id]

    decide_batch = DinoBatchedFPOPolicyDecider(
        policy=policy,
        device=device,
        action_size=protocol.action_size,
        nfe=config.nfe,
        num_fpo_samples=config.num_fpo_samples,
        generator=action_generator,
    )

    def bootstrap(collector: VersionedCollector[FPOAuxiliary]) -> dict[BootstrapTarget, float]:
        values: dict[BootstrapTarget, float] = {}
        for target, observations in collector.bootstrap_observations.items():
            tensors = tuple(
                torch.as_tensor(observation[None, ...], device=device)
                for observation in observations
            )
            with torch.inference_mode():
                number = float(policy.critic(tensors).detach().cpu().item())
            if not math.isfinite(number):
                raise RuntimeError("policy produced a non-finite bootstrap value")
            values[target] = number
        return values

    def resolve_restart(environment_id: int, handle: EnvironmentHandle):
        del handle
        return DinoParallelRestartState(
            adapter=adapters[environment_id], initial_step=initial_steps[environment_id]
        )

    started = time.perf_counter()
    next_checkpoint = (
        (resume.environment_steps // config.checkpoint_interval) + 1
    ) * config.checkpoint_interval
    optimizer_updates = resume.optimizer_updates
    peak_allocated = 0
    peak_reserved = 0

    def checkpoint_update(update, absolute_steps: int) -> None:
        nonlocal next_checkpoint, optimizer_updates, peak_allocated, peak_reserved
        optimizer_updates += 1
        if device.type == "cuda":
            peak_allocated = max(peak_allocated, torch.cuda.max_memory_allocated(device))
            peak_reserved = max(peak_reserved, torch.cuda.max_memory_reserved(device))
            torch.cuda.reset_peak_memory_stats(device)
        current_seconds = time.perf_counter() - started
        elapsed = resume.wall_clock_seconds + current_seconds
        reporter.log_update(
            update,
            environment_steps=absolute_steps,
            update_index=optimizer_updates,
            wall_clock_seconds=elapsed,
            steps_per_second=(absolute_steps - resume.environment_steps) / max(current_seconds, 1e-9),
        )
        if absolute_steps >= next_checkpoint:
            checkpoint_manager.save(
                config.run_directory / "checkpoints" / f"step-{absolute_steps}.pt",
                environment_steps=absolute_steps,
                policy_version=update.policy_version + 1,
                optimizer_updates=optimizer_updates,
                wall_clock_seconds=elapsed,
            )
            while next_checkpoint <= absolute_steps:
                next_checkpoint += config.checkpoint_interval

    if device.type == "cuda":
        torch.cuda.reset_peak_memory_stats(device)
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
        update_function=update_versioned_fpo,
    )
    try:
        summary = trainer.train()
    finally:
        reporter.close()
    elapsed = resume.wall_clock_seconds + time.perf_counter() - started
    checkpoint_manager.save(
        config.run_directory / "checkpoints" / "final.pt",
        environment_steps=summary.final_environment_steps,
        policy_version=summary.updates[-1].policy_version + 1,
        optimizer_updates=optimizer_updates,
        wall_clock_seconds=elapsed,
    )
    parameter_audit = audit_parameter_updates(initial_parameters, snapshot_named_parameters(policy))
    audit_json = config.run_directory / "parameter-update-audit.json"
    audit_md = config.run_directory / "parameter-update-audit.md"
    audit_json.write_text(json.dumps(parameter_audit, indent=2, sort_keys=True), encoding="utf-8")
    audit_md.write_text(render_parameter_update_audit_markdown(parameter_audit), encoding="utf-8")
    result: dict[str, object] = {
        "status": "PASS",
        "summary": asdict(summary),
        "optimizer_updates": optimizer_updates,
        "warm_start_source": warm_start_source,
        "wall_clock_seconds": elapsed,
        "steps_per_second": (summary.final_environment_steps - resume.environment_steps)
        / max(time.perf_counter() - started, 1e-9),
        "inference_batching": asdict(decide_batch.statistics()),
        "cuda_peak_allocated_bytes": peak_allocated,
        "cuda_peak_reserved_bytes": peak_reserved,
        "cuda_peak_reserved_gib": peak_reserved / 1024**3,
        "memory_gate_passed": peak_reserved <= 14 * 1024**3,
        "final_checkpoint": str((config.run_directory / "checkpoints" / "final.pt").resolve()),
        "parameter_update_audit": {
            "summary": parameter_audit["summary"],
            "json_path": str(audit_json.resolve()),
            "markdown_path": str(audit_md.resolve()),
        },
    }
    (config.run_directory / "summary.json").write_text(
        json.dumps(result, indent=2, sort_keys=True), encoding="utf-8"
    )
    return result


def main(argv: Sequence[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    result = run_training(DinoParallelFPOConfig.from_yaml(args.config))
    print(json.dumps(result, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
