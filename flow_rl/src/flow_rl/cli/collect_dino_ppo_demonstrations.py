"""Collect map-balanced successful Dino episodes from a frozen PPO teacher."""

from __future__ import annotations

import argparse
import hashlib
import json
import math
import re
import time
from dataclasses import asdict
from pathlib import Path
from typing import Sequence

import numpy as np
import torch
from mlagents_envs.side_channel.engine_configuration_channel import (
    EngineConfigurationChannel,
)

from flow_rl.cli.train_dino_parallel_ppo import (
    _classify_map,
    _training_launch_args,
    _validate_step,
)
from flow_rl.data.dino_demonstrations import (
    DinoSuccessfulEpisodeBuffer,
    accept_versioned_dino_trajectories,
    save_dino_demonstrations,
    summarize_dino_action_modes,
)
from flow_rl.data.normalization import IdentityObservationNormalizer
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.envs.parallel_unity import AsyncUnityVectorEnv, EnvironmentHandle
from flow_rl.envs.types import EnvStep
from flow_rl.envs.unity import UnityEnvAdapter
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.models.policy import GaussianActorCritic
from flow_rl.tracking.checkpoint import (
    load_checkpoint,
    validate_checkpoint_compatibility,
)
from flow_rl.training.collector import PPOAuxiliary
from flow_rl.training.dino_batched_policy import DinoBatchedPolicyDecider
from flow_rl.training.dino_parallel_ppo import (
    DinoParallelDecisionRequest,
    DinoParallelPPOTrainer,
    DinoParallelRestartState,
)
from flow_rl.training.versioned_collector import BootstrapTarget, VersionedCollector


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Collect complete natural-success Dino episodes from a frozen "
            "structured PPO checkpoint."
        )
    )
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--num-envs", type=int, default=16)
    parser.add_argument("--worker-base", type=int, default=3400)
    parser.add_argument("--layout-seed", type=int, default=20260905)
    parser.add_argument("--policy-seed", type=int, default=20260905)
    parser.add_argument("--rollout-size", type=int, default=131_072)
    parser.add_argument("--target-episodes-per-map", type=int, default=500)
    parser.add_argument("--device", choices=("cpu", "cuda"), default="cuda")
    return parser


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _canonical_json_sha256(payload: object) -> str:
    encoded = json.dumps(
        payload,
        sort_keys=True,
        separators=(",", ":"),
        ensure_ascii=True,
        allow_nan=False,
    ).encode("utf-8")
    return hashlib.sha256(encoded).hexdigest()


def _write_json(path: Path, payload: object) -> None:
    path.write_text(
        json.dumps(payload, indent=2, sort_keys=True, allow_nan=False) + "\n",
        encoding="utf-8",
    )


def _scan_unity_logs(directory: Path) -> dict[str, object]:
    exception_header = re.compile(r"^[A-Za-z][A-Za-z0-9.]*Exception:")
    critical_line = re.compile(r"\b(crash|fatal|nan)\b|out of memory", re.IGNORECASE)
    critical_matches: list[dict[str, object]] = []
    known_benign_matches: list[dict[str, object]] = []
    files = sorted(path for path in directory.rglob("*") if path.is_file())
    for path in files:
        try:
            lines = path.read_text(encoding="utf-8", errors="replace").splitlines()
        except OSError:
            continue
        index = 0
        while index < len(lines):
            line = lines[index]
            if exception_header.match(line):
                stop = index + 1
                while stop < len(lines) and not exception_header.match(lines[stop]):
                    if lines[stop] and not lines[stop].startswith((" ", "\t")):
                        break
                    stop += 1
                block = "\n".join(lines[index:stop])
                record = {
                    "path": str(path.resolve()),
                    "line": index + 1,
                    "text": block[:2000],
                }
                benign_health_bar = (
                    line.startswith("NullReferenceException:")
                    and "LlamAcademy.Dinos.Utility.HealthBarCanvas.Update" in block
                )
                benign_shutdown_sensor = (
                    line.startswith("InvalidOperationException:")
                    and "DinoStructuredObservationBuilder" in block
                    and "Unity.MLAgents.Agent.OnDisable" in block
                )
                if benign_health_bar or benign_shutdown_sensor:
                    known_benign_matches.append(record)
                else:
                    critical_matches.append(record)
                index = stop
                continue
            if critical_line.search(line):
                critical_matches.append(
                    {
                        "path": str(path.resolve()),
                        "line": index + 1,
                        "text": line[:500],
                    }
                )
            index += 1
    return {
        "file_count": len(files),
        "known_benign_match_count": len(known_benign_matches),
        "known_benign_matches": known_benign_matches,
        "critical_match_count": len(critical_matches),
        "critical_matches": critical_matches,
    }


def collect(args: argparse.Namespace) -> dict[str, object]:
    if args.num_envs != 16:
        raise ValueError("formal Dino dataset collection requires exactly 16 envs")
    if args.rollout_size <= 0 or args.target_episodes_per_map <= 0:
        raise ValueError("rollout and episode target must be positive")
    if args.worker_base < 0 or args.layout_seed < 0 or args.policy_seed < 0:
        raise ValueError("worker and seeds cannot be negative")
    if args.device == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("device cuda requires torch.cuda.is_available()")

    output = args.output.resolve()
    checkpoint = args.checkpoint.resolve()
    build = args.build.resolve()
    if output.exists() and (not output.is_dir() or any(output.iterdir())):
        raise FileExistsError(f"output directory is not empty: {output}")
    if not checkpoint.is_file() or not build.is_file():
        raise FileNotFoundError("checkpoint and Unity build must both exist")

    payload = load_checkpoint(checkpoint, map_location=args.device)
    metadata = payload["metadata"]
    config = payload["config"]
    if metadata.get("algorithm") != "dino_parallel_ppo":
        raise ValueError("checkpoint is not a Dino parallel PPO policy")
    build_hash = _sha256(build)
    if metadata.get("build_sha256") != build_hash:
        raise ValueError("Unity build hash does not match checkpoint")
    if float(config["time_scale"]) != 1.0:
        raise ValueError("PolicyFlow data gate requires checkpoint time_scale=1")

    protocol_path = Path(config["protocol_path"]).resolve()
    protocol = DinoProtocol.from_yaml(protocol_path)
    device = torch.device(args.device)
    policy = GaussianActorCritic(
        observation_shapes=protocol.observation_shapes,
        action_size=protocol.action_size,
        hidden_sizes=tuple(int(item) for item in config["hidden_sizes"]),
        encoder_factory=lambda: SetTransformerDinoEncoder(
            protocol,
            d_model=int(config["encoder_d_model"]),
            heads=int(config["encoder_heads"]),
            inducing_points=int(config["encoder_inducing_points"]),
            layers=int(config["encoder_layers"]),
            dropout=float(config["encoder_dropout"]),
            output_size=int(config["encoder_output_size"]),
        ),
    ).to(device)
    encoder_metadata = policy.actor.encoder.checkpoint_metadata()
    validate_checkpoint_compatibility(
        payload,
        expected_observation_shapes=protocol.observation_shapes,
        expected_action_size=protocol.action_size,
        expected_protocol_metadata=protocol.checkpoint_metadata(),
        expected_encoder_metadata=encoder_metadata,
    )
    policy.load_state_dict(payload["model_state"], strict=True)
    policy.eval()
    normalizer = IdentityObservationNormalizer(
        protocol.observation_shapes,
        protocol_manifest_sha256=protocol.manifest_sha256,
    )
    normalizer.load_state_dict(payload["normalizer_state"])
    normalizer_state = normalizer.state_dict()
    if normalizer_state["normalizer_type"] != "identity":
        raise ValueError("Dino PPO teacher must use Identity normalization")

    output.mkdir(parents=True, exist_ok=False)
    unity_directory = output / "unity"
    unity_directory.mkdir()
    _write_json(
        output / "effective-config.json",
        {
            "checkpoint": str(checkpoint),
            "build": str(build),
            "num_envs": args.num_envs,
            "worker_base": args.worker_base,
            "worker_ids": [args.worker_base + i for i in range(args.num_envs)],
            "layout_seed": args.layout_seed,
            "environment_seeds": [args.layout_seed + i for i in range(args.num_envs)],
            "policy_seed": args.policy_seed,
            "action_mode": "stochastic",
            "rollout_size": args.rollout_size,
            "target_episodes_per_map": args.target_episodes_per_map,
            "time_scale": float(config["time_scale"]),
            "protocol_path": str(protocol_path),
            "device": args.device,
        },
    )

    adapters: dict[int, UnityEnvAdapter] = {}
    initial_steps: dict[int, EnvStep] = {}

    def adapter_factory(handle: EnvironmentHandle) -> UnityEnvAdapter:
        channel = EngineConfigurationChannel()
        channel.set_configuration_parameters(
            width=84,
            height=84,
            quality_level=0,
            time_scale=float(config["time_scale"]),
            target_frame_rate=-1,
            capture_frame_rate=0,
        )
        adapter = UnityEnvAdapter(
            build,
            worker_id=handle.worker_id,
            seed=handle.environment_seed,
            no_graphics=True,
            timeout_wait=int(config["timeout_wait"]),
            behavior_name=str(config["behavior_name"]),
            log_folder=handle.log_directory,
            side_channels=[channel],
            additional_args=_training_launch_args(
                base_seed=args.layout_seed,
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
        num_envs=args.num_envs,
        base_worker_id=args.worker_base,
        base_seed=args.layout_seed,
        log_directory=unity_directory,
        max_consecutive_failures=int(config["max_consecutive_failures"]),
    )
    generator = torch.Generator(device=device).manual_seed(args.policy_seed)
    decide_batch = DinoBatchedPolicyDecider(
        policy=policy,
        device=device,
        action_size=protocol.action_size,
        generator=generator,
    )

    def decide(environment_id, generation, policy_version, step, adapter):
        request = DinoParallelDecisionRequest(
            environment_id=environment_id,
            process_generation=generation,
            policy_version=policy_version,
            step=step,
            adapter=adapter,
        )
        return decide_batch((request,))[environment_id]

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
                raise RuntimeError("teacher produced a non-finite bootstrap value")
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

    buffer = DinoSuccessfulEpisodeBuffer(
        target_episodes_per_map=args.target_episodes_per_map
    )

    def collect_update(collector, updater, **kwargs):
        del updater, kwargs
        batch = collector.build_batch()
        accepted = accept_versioned_dino_trajectories(buffer, batch)
        collector.complete_optimizer_update(batch.policy_version, succeeded=True)
        return {"accepted_success_episodes": accepted}

    started = time.perf_counter()
    trainer = DinoParallelPPOTrainer(
        vector_env=vector,
        adapters=adapters,
        initial_steps=initial_steps,
        updater=object(),
        device=device,
        rollout_size=args.rollout_size,
        total_environment_steps=args.rollout_size,
        initial_environment_steps=0,
        gamma=float(config["gamma"]),
        gae_lambda=float(config["gae_lambda"]),
        decide=decide,
        bootstrap=bootstrap,
        map_classifier=_classify_map,
        restart_state_resolver=resolve_restart,
        poll_timeout=float(config["poll_timeout"]),
        max_consecutive_no_progress=int(config["max_consecutive_no_progress"]),
        initial_policy_version=int(metadata["policy_version"]),
        sampling_mode="asynchronous",
        decide_batch=decide_batch,
        inference_batch_size=16,
        inference_batch_wait_seconds=0.0,
        update_function=collect_update,
    )
    summary = trainer.train()
    elapsed = time.perf_counter() - started
    if not buffer.complete:
        raise RuntimeError(
            "one-rollout collection did not meet balanced episode target: "
            f"{dict(buffer.accepted_episodes_by_map)}"
        )
    batch, dataset_metadata = buffer.build()
    mode_report = summarize_dino_action_modes(batch)
    unity_audit = _scan_unity_logs(unity_directory)
    if unity_audit["critical_match_count"]:
        raise RuntimeError("Unity logs contain critical-error patterns")
    contributions = dict(summary.updates[0].environment_contributions)
    if set(contributions) != set(range(args.num_envs)):
        raise RuntimeError("not all 16 environments contributed to collection")

    dataset_metadata.update(
        {
            "checkpoint_path": str(checkpoint),
            "checkpoint_sha256": _sha256(checkpoint),
            "checkpoint_algorithm": metadata["algorithm"],
            "checkpoint_environment_steps": int(metadata["environment_steps"]),
            "checkpoint_policy_version": int(metadata["policy_version"]),
            "build_path": str(build),
            "build_sha256": build_hash,
            "protocol_path": str(protocol_path),
            "protocol_version": protocol.protocol_version,
            "protocol_manifest_sha256": protocol.manifest_sha256,
            "protocol_file_sha256": _sha256(protocol_path),
            "encoder": encoder_metadata,
            "encoder_metadata_sha256": _canonical_json_sha256(encoder_metadata),
            "normalizer_type": "identity",
            "normalizer_state": normalizer_state,
            "normalizer_state_sha256": _canonical_json_sha256(normalizer_state),
            "action_mode": "stochastic",
            "policy_seed": args.policy_seed,
            "layout_seed": args.layout_seed,
            "worker_ids": [args.worker_base + i for i in range(args.num_envs)],
            "environment_seeds": [args.layout_seed + i for i in range(args.num_envs)],
            "rollout_transition_count": summary.total_transitions,
            "environment_contributions": contributions,
            "wall_clock_seconds": elapsed,
            "steps_per_second": summary.total_transitions / max(elapsed, 1e-9),
            "unity_log_audit": unity_audit,
        }
    )
    save_dino_demonstrations(output / "dataset", batch, dataset_metadata)
    _write_json(output / "action-mode-report.json", mode_report)
    result = {
        "status": "PASS",
        "dataset_directory": str((output / "dataset").resolve()),
        "dataset": dataset_metadata,
        "mode_report": mode_report,
        "trainer_summary": asdict(summary),
        "inference_batching": asdict(decide_batch.statistics()),
    }
    _write_json(output / "summary.json", result)
    return result


def main(argv: Sequence[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    result = collect(args)
    print(json.dumps(result, indent=2, sort_keys=True, allow_nan=False))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
