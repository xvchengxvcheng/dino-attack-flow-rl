from __future__ import annotations

import argparse
import hashlib
import json
import math
import random
import time
import traceback
from collections import Counter
from collections.abc import Callable, Mapping, Sequence
from dataclasses import asdict, dataclass
from pathlib import Path
from typing import Any, Literal

import numpy as np
import torch
import yaml
from mlagents_envs.side_channel.engine_configuration_channel import (
    EngineConfigurationChannel,
)

from flow_rl.algorithms.fpo import FPOUpdater
from flow_rl.algorithms.ppo import PPOUpdater
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.envs.parallel_unity import (
    AsyncUnityVectorEnv,
    EnvironmentHandle,
)
from flow_rl.envs.types import EnvStep
from flow_rl.envs.unity import UnityEnvAdapter
from flow_rl.models.fpo_policy import FlowActorCritic
from flow_rl.models.policy import GaussianActorCritic
from flow_rl.models.state_encoder import build_state_encoder
from flow_rl.training.collector import PPOAuxiliary
from flow_rl.training.config import PPOTrainingConfig
from flow_rl.training.fpo_collector import FPOAuxiliary
from flow_rl.training.fpo_config import FPOTrainingConfig
from flow_rl.training.fpo_trainer import update_versioned_fpo
from flow_rl.training.trainer import update_versioned_ppo
from flow_rl.training.versioned_collector import (
    BootstrapTarget,
    VersionedActionInfo,
    VersionedCollector,
)


Algorithm = Literal["ppo", "fpo"]
Runner = Callable[["SmokeRequest"], dict[str, Any]]
_NUM_ENVIRONMENTS = 4


@dataclass(frozen=True)
class SmokeRequest:
    algorithm: Algorithm
    config: Path
    run_directory: Path
    worker_base: int
    seed: int
    time_scale: float
    timeout: int
    target: int


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description=(
            "Run one reproducible four-Player Dino v2 PPO or FPO update and "
            "write raw JSON evidence."
        )
    )
    parser.add_argument("--algorithm", choices=("ppo", "fpo"), required=True)
    parser.add_argument("--config", type=Path, required=True)
    parser.add_argument("--run-dir", type=Path, required=True)
    parser.add_argument("--worker-base", type=int, required=True)
    parser.add_argument("--seed", type=int, required=True)
    parser.add_argument("--time-scale", type=float, required=True)
    parser.add_argument("--timeout", type=int, required=True)
    parser.add_argument("--target", type=int, required=True)
    return parser


def _request(argv: Sequence[str] | None) -> SmokeRequest:
    args = _parser().parse_args(argv)
    request = SmokeRequest(
        algorithm=args.algorithm,
        config=args.config.resolve(),
        run_directory=args.run_dir.resolve(),
        worker_base=args.worker_base,
        seed=args.seed,
        time_scale=args.time_scale,
        timeout=args.timeout,
        target=args.target,
    )
    if not request.config.is_file():
        raise FileNotFoundError(f"configuration does not exist: {request.config}")
    if request.run_directory.is_dir() and any(request.run_directory.iterdir()):
        raise FileExistsError(
            f"run directory is not empty: {request.run_directory}"
        )
    if request.worker_base < 0 or request.seed < 0:
        raise ValueError("worker-base and seed cannot be negative")
    if request.time_scale <= 0.0 or request.timeout <= 0 or request.target <= 0:
        raise ValueError("time-scale, timeout and target must be positive")
    return request


def _write_json(path: Path, payload: Mapping[str, Any]) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    with path.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")


def main(
    argv: Sequence[str] | None = None,
    *,
    runner: Runner = lambda request: run_smoke(request),
) -> int:
    request = _request(argv)
    request.run_directory.mkdir(parents=True, exist_ok=True)
    try:
        result = runner(request)
        _write_json(request.run_directory / "result.json", result)
        metrics = result.get("metrics")
        if isinstance(metrics, Mapping):
            _write_json(request.run_directory / "metrics.json", metrics)
        _write_json(
            request.run_directory / "exit.json",
            {"exit_code": 0, "status": "PASS"},
        )
        print(json.dumps(result, indent=2, sort_keys=True))
        return 0
    except BaseException as error:
        trace = traceback.format_exc()
        traceback_path = request.run_directory / "traceback.txt"
        traceback_path.write_text(trace, encoding="utf-8", newline="\n")
        failure = {
            "status": "FAIL",
            "error_type": type(error).__name__,
            "error_message": str(error),
            "traceback": str(traceback_path),
        }
        _write_json(request.run_directory / "result.json", failure)
        _write_json(
            request.run_directory / "exit.json",
            {"exit_code": 1, "status": "FAIL"},
        )
        print(json.dumps(failure, indent=2, sort_keys=True))
        return 1


def run_smoke(request: SmokeRequest) -> dict[str, Any]:
    config = _effective_config(request)
    config.validate()
    protocol_path = config.protocol_path
    if protocol_path is None:
        raise ValueError("Dino v2 smoke requires protocol_path")
    protocol = DinoProtocol.from_yaml(protocol_path)
    if hasattr(protocol, "region_vertices") or hasattr(
        protocol, "region_vertices_normalized"
    ):
        raise RuntimeError("v1 map-specific region metadata is not allowed")
    _seed_everything(request.seed)
    device = torch.device(config.device)
    _write_json(request.run_directory / "effective-config.json", config.to_dict())

    initial_steps: dict[int, EnvStep] = {}
    adapters: dict[int, UnityEnvAdapter] = {}
    channels: dict[int, EngineConfigurationChannel] = {}
    current_map_by_environment: dict[int, str] = {}
    map_episode_starts: Counter[str] = Counter()
    map_natural_terminals: Counter[str] = Counter()
    map_truncated_terminals: Counter[str] = Counter()
    map_layout_fingerprints: dict[str, set[str]] = {
        "map1": set(),
        "map2": set(),
    }
    map_max_valid_guards: Counter[str] = Counter()
    awaiting_map_classification: dict[int, bool] = {}

    def record_map_start(
        environment_id: int,
        step: EnvStep,
        decision_count: int,
    ) -> bool:
        observed_map = _classify_decision_map(step, decision_count)
        if observed_map is None:
            return False
        current_map_by_environment[environment_id] = observed_map
        map_episode_starts[observed_map] += 1
        map_layout_fingerprints[observed_map].add(
            _layout_fingerprint(step, decision_count)
        )
        map_max_valid_guards[observed_map] = max(
            map_max_valid_guards[observed_map],
            _valid_guard_count(step, decision_count),
        )
        awaiting_map_classification[environment_id] = False
        return True

    def adapter_factory(handle: EnvironmentHandle) -> UnityEnvAdapter:
        channel = EngineConfigurationChannel()
        channel.set_configuration_parameters(
            width=84,
            height=84,
            quality_level=0,
            time_scale=request.time_scale,
            target_frame_rate=-1,
            capture_frame_rate=0,
        )
        channels[handle.environment_id] = channel
        adapter = UnityEnvAdapter(
            config.build_path,
            worker_id=handle.worker_id,
            seed=handle.environment_seed,
            no_graphics=True,
            timeout_wait=request.timeout,
            behavior_name=config.behavior_name,
            log_folder=handle.log_directory,
            side_channels=[channel],
            additional_args=_training_launch_args(
                base_seed=request.seed,
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
        awaiting_map_classification[handle.environment_id] = True
        record_map_start(
            handle.environment_id,
            step,
            len(adapter.pending_agent_ids),
        )
        return adapter

    started = time.perf_counter()
    vector_closed = False
    natural_terminals = 0
    truncated_terminals = 0
    max_action_abs = 0.0
    environment_times: list[float] = []
    flow_times: list[float] = []
    separate_time_inputs = True
    policy, updater, action_generator = _build_algorithm(config, protocol, device)
    collector: VersionedCollector[PPOAuxiliary | FPOAuxiliary] = (
        VersionedCollector(target_transitions=request.target)
    )
    collector.begin(0)
    pending: dict[int, VersionedActionInfo[PPOAuxiliary | FPOAuxiliary]] = {}
    vector = AsyncUnityVectorEnv(
        adapter_factory,
        num_envs=_NUM_ENVIRONMENTS,
        base_worker_id=request.worker_base,
        base_seed=request.seed,
        log_directory=request.run_directory / "unity",
    )
    try:
        submissions: dict[int, np.ndarray] = {}
        for environment_id, handle in vector.handles.items():
            info, diagnostics = _decide(
                algorithm=request.algorithm,
                environment_id=environment_id,
                generation=handle.generation,
                step=initial_steps[environment_id],
                adapter=adapters[environment_id],
                policy=policy,
                config=config,
                device=device,
                generator=action_generator,
            )
            pending[environment_id] = info
            submissions[environment_id] = info.actions
            max_action_abs = max(max_action_abs, diagnostics["max_action_abs"])
            environment_times.extend(diagnostics["environment_times"])
            flow_times.extend(diagnostics["flow_times"])
            separate_time_inputs &= diagnostics["separate_time_inputs"]
        vector.submit_actions(submissions)

        while pending:
            events = vector.poll_ready(timeout=None, max_events=1)
            if len(events) != 1:
                raise RuntimeError("parallel environment returned no completion event")
            event = events[0]
            info = pending.pop(event.environment_id)
            collector.record(event, info)
            if event.step is not None:
                _validate_step(event.step, protocol)
                natural_count = int(event.step.terminated.sum())
                truncated_count = int(event.step.truncated.sum())
                natural_terminals += natural_count
                truncated_terminals += truncated_count
                current_map = current_map_by_environment.get(event.environment_id)
                if natural_count or truncated_count:
                    if current_map is None:
                        raise RuntimeError(
                            "episode ended before its map could be classified"
                        )
                    map_natural_terminals[current_map] += natural_count
                    map_truncated_terminals[current_map] += truncated_count
                    awaiting_map_classification[event.environment_id] = True

                decision_count = len(adapters[event.environment_id].pending_agent_ids)
                if awaiting_map_classification[event.environment_id]:
                    record_map_start(
                        event.environment_id,
                        event.step,
                        decision_count,
                    )
            if not collector.accepting_submissions:
                continue
            if event.error is not None:
                handle = vector.restart_environment(event.environment_id)
                step = initial_steps[event.environment_id]
            else:
                handle = vector.handles[event.environment_id]
                assert event.step is not None
                step = event.step
            next_info, diagnostics = _decide(
                algorithm=request.algorithm,
                environment_id=event.environment_id,
                generation=handle.generation,
                step=step,
                adapter=adapters[event.environment_id],
                policy=policy,
                config=config,
                device=device,
                generator=action_generator,
            )
            pending[event.environment_id] = next_info
            max_action_abs = max(max_action_abs, diagnostics["max_action_abs"])
            environment_times.extend(diagnostics["environment_times"])
            flow_times.extend(diagnostics["flow_times"])
            separate_time_inputs &= diagnostics["separate_time_inputs"]
            vector.submit_actions({event.environment_id: next_info.actions})

        bootstrap = _bootstrap_values(collector, policy, device)
        collector.seal_and_bootstrap(bootstrap)
        versioned = collector.build_batch()
        contributions = Counter(
            transition.identity.environment_id
            for transition in versioned.transitions
        )
        if set(contributions) != set(range(_NUM_ENVIRONMENTS)):
            raise RuntimeError(
                f"not all environments contributed: {dict(contributions)}"
            )
        if request.algorithm == "ppo":
            metrics_object = update_versioned_ppo(
                collector,
                updater,
                gamma=config.gamma,
                gae_lambda=config.gae_lambda,
                device=device,
                environment_steps=len(versioned.transitions),
            )
        else:
            metrics_object = update_versioned_fpo(
                collector,
                updater,
                gamma=config.gamma,
                gae_lambda=config.gae_lambda,
                device=device,
                environment_steps=len(versioned.transitions),
            )
        metrics = metrics_object.as_dict()
        _require_finite(metrics, "metrics")
        if request.algorithm == "fpo" and (
            not flow_times or not separate_time_inputs
        ):
            raise RuntimeError("FPO environment-time and flow-time inputs were not distinct")
    finally:
        vector.close()
        vector_closed = True

    elapsed = time.perf_counter() - started
    result: dict[str, Any] = {
        "status": "PASS",
        "algorithm": request.algorithm,
        "request": {
            **asdict(request),
            "config": str(request.config),
            "run_directory": str(request.run_directory),
        },
        "observation_shapes": [list(shape) for shape in protocol.observation_shapes],
        "action_size": protocol.action_size,
        "v1_region_metadata_present": False,
        "accepted_transitions": len(versioned.transitions),
        "drained_transitions": len(versioned.transitions),
        "environment_contributions": {
            str(index): contributions[index] for index in range(_NUM_ENVIRONMENTS)
        },
        "natural_terminals": natural_terminals,
        "truncated_terminals": truncated_terminals,
        "map_episode_starts": dict(sorted(map_episode_starts.items())),
        "map_natural_terminals": dict(sorted(map_natural_terminals.items())),
        "map_truncated_terminals": dict(sorted(map_truncated_terminals.items())),
        "map_layout_fingerprints": {
            name: sorted(values)
            for name, values in sorted(map_layout_fingerprints.items())
        },
        "map_max_valid_guards": dict(sorted(map_max_valid_guards.items())),
        "map_classification_rule": {
            "map1": "8 valid Guard rows",
            "map2": "11 valid Guard rows",
            "network_map_one_hot": False,
        },
        "optimizer_updates": 1,
        "policy_version_before": 0,
        "policy_version_after": 1,
        "max_action_abs": max_action_abs,
        "all_finite": True,
        "metrics": metrics,
        "wall_clock_seconds": elapsed,
        "vector_closed": vector_closed,
        "player_log_paths": [
            str(
                request.run_directory
                / "unity"
                / f"environment-{index}"
                / "generation-0"
                / f"Player-{request.worker_base + index}.log"
            )
            for index in range(_NUM_ENVIRONMENTS)
        ],
    }
    if environment_times:
        result["environment_time_range"] = [
            min(environment_times),
            max(environment_times),
        ]
    if request.algorithm == "fpo":
        result["flow_time_range"] = [min(flow_times), max(flow_times)]
        result["separate_time_inputs"] = separate_time_inputs
    _require_finite(result, "result")
    return result


def _effective_config(
    request: SmokeRequest,
) -> PPOTrainingConfig | FPOTrainingConfig:
    with request.config.open(encoding="utf-8") as handle:
        loaded = yaml.safe_load(handle)
    if not isinstance(loaded, Mapping):
        raise TypeError("training configuration must be a mapping")
    raw = dict(loaded)
    raw.update(
        {
            "run_directory": request.run_directory,
            "total_environment_steps": request.target,
            "rollout_size": request.target,
            "worker_id": request.worker_base,
            "evaluation_worker_id": (
                request.worker_base + _NUM_ENVIRONMENTS + 100
            ),
            "seed": request.seed,
            "time_scale": request.time_scale,
            "timeout_wait": request.timeout,
            "resume_checkpoint": None,
        }
    )
    base = request.config.parent
    for name in ("build_path", "protocol_path"):
        if raw.get(name) is not None:
            raw[name] = (base / Path(raw[name])).resolve()
    if request.algorithm == "ppo":
        raw.setdefault("encoder_type", "flat")
        raw.setdefault("protocol_path", None)
        raw.setdefault("encoder_d_model", 64)
        raw.setdefault("encoder_heads", 4)
        raw.setdefault("encoder_inducing_points", 8)
        raw.setdefault("encoder_layers", 2)
        raw.setdefault("encoder_dropout", 0.05)
        raw.setdefault("encoder_output_size", 256)
        raw.setdefault("normalize_observations", True)
        raw["hidden_sizes"] = tuple(raw["hidden_sizes"])
        return PPOTrainingConfig(**raw)
    raw.setdefault("encoder_type", "flat")
    raw.setdefault("protocol_path", None)
    raw.setdefault("encoder_d_model", 64)
    raw.setdefault("encoder_heads", 4)
    raw.setdefault("encoder_inducing_points", 8)
    raw.setdefault("encoder_layers", 2)
    raw.setdefault("encoder_dropout", 0.05)
    raw.setdefault("encoder_output_size", 256)
    raw.setdefault("normalize_observations", True)
    raw["velocity_hidden_sizes"] = tuple(raw["velocity_hidden_sizes"])
    raw["critic_hidden_sizes"] = tuple(raw["critic_hidden_sizes"])
    return FPOTrainingConfig(**raw)


def _encoder_factory(config, protocol: DinoProtocol):
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


def _build_algorithm(config, protocol: DinoProtocol, device: torch.device):
    action_generator = torch.Generator(device=device).manual_seed(config.seed + 1_000)
    update_generator = torch.Generator().manual_seed(config.seed + 2_000)
    if isinstance(config, PPOTrainingConfig):
        policy = GaussianActorCritic(
            observation_shapes=protocol.observation_shapes,
            action_size=protocol.action_size,
            hidden_sizes=config.hidden_sizes,
            encoder_factory=_encoder_factory(config, protocol),
        ).to(device)
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
            total_environment_steps=config.total_environment_steps,
            batch_size=config.batch_size,
            epochs=config.epochs,
            generator=update_generator,
        )
    else:
        policy = FlowActorCritic(
            observation_shapes=protocol.observation_shapes,
            action_size=protocol.action_size,
            state_size=config.state_size,
            time_embedding_size=config.time_embedding_size,
            velocity_hidden_sizes=config.velocity_hidden_sizes,
            critic_hidden_sizes=config.critic_hidden_sizes,
            encoder_factory=_encoder_factory(config, protocol),
        ).to(device)
        updater = FPOUpdater(
            policy,
            learning_rate=config.learning_rate,
            final_learning_rate=config.final_learning_rate,
            clip_range=config.clip_range,
            final_clip_range=config.final_clip_range,
            max_gradient_norm=config.max_gradient_norm,
            total_environment_steps=config.total_environment_steps,
            batch_size=config.batch_size,
            epochs=config.epochs,
            difference_clip=config.difference_clip,
            positive_advantage=config.positive_advantage,
            generator=update_generator,
        )
    return policy, updater, action_generator


def _decision_observations(
    step: EnvStep,
    pending_agent_ids: np.ndarray,
) -> tuple[np.ndarray, ...]:
    count = len(pending_agent_ids)
    if not np.array_equal(step.agent_ids[:count], pending_agent_ids):
        raise RuntimeError("Decision Agent order does not match merged LLAPI step")
    return tuple(
        np.array(stream[:count], dtype=np.float32, copy=True)
        for stream in step.observations
    )


def _training_launch_args(
    *,
    base_seed: int,
    environment_id: int,
    generation: int,
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


def _valid_guard_count(step: EnvStep, decision_count: int) -> int:
    if decision_count <= 0:
        return 0
    guards = step.observations[3][:decision_count]
    counts = np.count_nonzero(guards[..., 0] > 0.5, axis=1)
    if len(counts) == 0 or np.any(counts != counts[0]):
        return 0
    return int(counts[0])


def _classify_decision_map(step: EnvStep, decision_count: int) -> str | None:
    guard_count = _valid_guard_count(step, decision_count)
    if guard_count == 8:
        return "map1"
    if guard_count == 11:
        return "map2"
    return None


def _layout_fingerprint(step: EnvStep, decision_count: int) -> str:
    if decision_count <= 0:
        raise ValueError("a decision observation is required for layout fingerprinting")
    digest = hashlib.sha256()
    digest.update(np.asarray(step.observations[0][0, 3:5], dtype=np.float32).tobytes())
    digest.update(np.asarray(step.observations[1][0], dtype=np.float32).tobytes())
    return digest.hexdigest().upper()


def _decide(
    *,
    algorithm: Algorithm,
    environment_id: int,
    generation: int,
    step: EnvStep,
    adapter: UnityEnvAdapter,
    policy,
    config,
    device: torch.device,
    generator: torch.Generator,
):
    agent_ids = adapter.pending_agent_ids
    observations = _decision_observations(step, agent_ids)
    if len(agent_ids) == 0:
        info = VersionedActionInfo(
            environment_id=environment_id,
            process_generation=generation,
            policy_version=0,
            agent_ids=agent_ids,
            observations=observations,
            actions=np.empty(
                (0, adapter.continuous_action_size), dtype=np.float32
            ),
            values=np.empty(0, dtype=np.float32),
            auxiliaries=(),
        )
        return info, {
            "max_action_abs": 0.0,
            "environment_times": [],
            "flow_times": [],
            "separate_time_inputs": True,
        }
    tensors = tuple(torch.as_tensor(stream, device=device) for stream in observations)
    with torch.inference_mode():
        if algorithm == "ppo":
            output = policy.act(
                tensors,
                deterministic=False,
                generator=generator,
            )
            auxiliaries = tuple(
                PPOAuxiliary(float(value))
                for value in output.log_probs.detach().cpu().numpy()
            )
            actions = output.actions.detach().cpu().numpy().astype(np.float32, copy=True)
            values = output.values.detach().cpu().numpy().astype(np.float32, copy=True)
            flow_times: list[float] = []
            separate = True
        else:
            output = policy.act(
                tensors,
                nfe=config.nfe,
                num_fpo_samples=config.num_fpo_samples,
                generator=generator,
            )
            latent = output.latent_actions.cpu().numpy()
            loss_eps = output.loss_eps.cpu().numpy()
            loss_t = output.loss_t.cpu().numpy()
            old_cfm_losses = output.old_cfm_losses.cpu().numpy()
            auxiliaries = tuple(
                FPOAuxiliary(
                    latent_action=np.array(latent[index], dtype=np.float32, copy=True),
                    loss_eps=np.array(loss_eps[index], dtype=np.float32, copy=True),
                    loss_t=np.array(loss_t[index], dtype=np.float32, copy=True),
                    old_cfm_losses=np.array(
                        old_cfm_losses[index], dtype=np.float32, copy=True
                    ),
                )
                for index in range(len(latent))
            )
            actions = output.actions.cpu().numpy().astype(np.float32, copy=True)
            values = output.values.cpu().numpy().astype(np.float32, copy=True)
            flow_times = [float(value) for value in loss_t.reshape(-1)]
            separate = not np.shares_memory(observations[0], loss_t)
    info = VersionedActionInfo(
        environment_id=environment_id,
        process_generation=generation,
        policy_version=0,
        agent_ids=agent_ids,
        observations=observations,
        actions=actions,
        values=values,
        auxiliaries=auxiliaries,
    )
    environment_times = [float(value) for value in observations[0][:, 0]]
    return info, {
        "max_action_abs": float(np.max(np.abs(actions))) if actions.size else 0.0,
        "environment_times": environment_times,
        "flow_times": flow_times,
        "separate_time_inputs": separate,
    }


def _bootstrap_values(
    collector: VersionedCollector,
    policy,
    device: torch.device,
) -> dict[BootstrapTarget, float]:
    values: dict[BootstrapTarget, float] = {}
    for target, observations in collector.bootstrap_observations.items():
        tensors = tuple(
            torch.as_tensor(observation[None, ...], device=device)
            for observation in observations
        )
        with torch.inference_mode():
            value = policy.critic(tensors)
        values[target] = float(value.detach().cpu().item())
    return values


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


def _require_finite(value: Any, name: str) -> None:
    if isinstance(value, bool) or value is None or isinstance(value, str):
        return
    if isinstance(value, (int, np.integer)):
        return
    if isinstance(value, (float, np.floating)):
        if not math.isfinite(float(value)):
            raise RuntimeError(f"{name} is not finite")
        return
    if isinstance(value, Mapping):
        for key, item in value.items():
            _require_finite(item, f"{name}.{key}")
        return
    if isinstance(value, Sequence):
        for index, item in enumerate(value):
            _require_finite(item, f"{name}[{index}]")


def _seed_everything(seed: int) -> None:
    random.seed(seed)
    np.random.seed(seed)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)


if __name__ == "__main__":
    raise SystemExit(main())
