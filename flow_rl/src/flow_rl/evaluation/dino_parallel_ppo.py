from __future__ import annotations

import hashlib
import json
import math
import time
from collections.abc import Callable, Sequence
from dataclasses import asdict, dataclass
from pathlib import Path

import numpy as np
import torch
from mlagents_envs.side_channel.engine_configuration_channel import (
    EngineConfigurationChannel,
)

from flow_rl.data.normalization import IdentityObservationNormalizer
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.envs.types import EnvStep
from flow_rl.envs.unity import UnityEnvAdapter
from flow_rl.evaluation.runner import EvaluationAdapter
from flow_rl.evaluation.flow_bc import sample_bounded_flow
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.models.flow import ConditionalVelocityMLP
from flow_rl.models.fpo_policy import FlowActorCritic
from flow_rl.models.policy import GaussianActorCritic
from flow_rl.models.policyflow_policy import PolicyFlowActorCritic
from flow_rl.tracking.checkpoint import (
    load_checkpoint,
    validate_flow_bc_checkpoint,
    validate_checkpoint_compatibility,
    validate_policyflow_checkpoint,
)
from flow_rl.tracking.episodes import EpisodeTracker


_VICTORY_TERMINAL_REWARD_THRESHOLD = 5.0
_FOOD_SCALE = 5_000.0
_ROUND_SECONDS = 50.0


@dataclass(frozen=True)
class DinoMapEvaluationSummary:
    map_name: str
    episodes: int
    wins: int
    win_rate: float
    mean_return: float
    mean_final_meat: float
    mean_elapsed_seconds: float


@dataclass(frozen=True)
class DinoParallelPPOEvaluationSummary:
    episodes: int
    episodes_per_map: int
    overall_wins: int
    overall_win_rate: float
    mean_return: float
    truncated_episodes: int
    environment_steps: int
    wall_clock_seconds: float
    steps_per_second: float
    parameter_count: int
    checkpoint_algorithm: str
    mean_inference_latency_ms: float
    solver_steps: int | None
    velocity_nfe: int | None
    maps: tuple[DinoMapEvaluationSummary, ...]
    checkpoint_path: str
    checkpoint_sha256: str
    build_path: str
    build_sha256: str
    protocol_version: str
    protocol_manifest_sha256: str
    normalizer_type: str
    action_mode: str
    policy_seed: int | None
    deterministic: bool
    victory_terminal_reward_threshold: float
    evaluation_seed: int
    evaluation_worker_id: int
    python_response_delay_seconds: float
    all_finite: bool

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


@dataclass(frozen=True)
class _EpisodeRecord:
    map_name: str
    episode_return: float
    win: bool
    final_meat: float
    elapsed_seconds: float
    truncated: bool


@dataclass(frozen=True)
class _ActionOutput:
    actions: torch.Tensor


class _FlowBCPolicyAdapter(torch.nn.Module):
    def __init__(self, model: ConditionalVelocityMLP, *, nfe: int) -> None:
        super().__init__()
        self.model = model
        self.nfe = int(nfe)

    def act(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        deterministic: bool,
        generator: torch.Generator | None,
    ) -> _ActionOutput:
        initial_noise = (
            torch.zeros(
                (observations[0].shape[0], self.model.action_size),
                dtype=torch.float32,
                device=observations[0].device,
            )
            if deterministic
            else None
        )
        if not deterministic and generator is None:
            raise ValueError("stochastic Flow BC evaluation requires a generator")
        return _ActionOutput(
            sample_bounded_flow(
                self.model,
                observations,
                nfe=self.nfe,
                generator=generator,
                initial_noise=initial_noise,
            )
        )


class _FPOPolicyAdapter(torch.nn.Module):
    def __init__(self, policy: FlowActorCritic, *, nfe: int) -> None:
        super().__init__()
        self.policy = policy
        self.nfe = int(nfe)

    def act(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        deterministic: bool,
        generator: torch.Generator | None,
    ) -> _ActionOutput:
        initial_noise = (
            torch.zeros(
                (observations[0].shape[0], self.policy.action_size),
                dtype=torch.float32,
                device=observations[0].device,
            )
            if deterministic
            else None
        )
        if not deterministic and generator is None:
            raise ValueError("stochastic FPO evaluation requires a generator")
        sample = self.policy.sampler.sample(
            observations,
            nfe=self.nfe,
            generator=generator,
            initial_noise=initial_noise,
        )
        return _ActionOutput(sample.actions)


class _PolicyFlowPolicyAdapter(torch.nn.Module):
    def __init__(self, policy: PolicyFlowActorCritic) -> None:
        super().__init__()
        self.policy = policy

    def act(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        deterministic: bool,
        generator: torch.Generator | None,
    ) -> _ActionOutput:
        if generator is None:
            raise ValueError("PolicyFlow evaluation requires a seeded generator")
        return _ActionOutput(
            self.policy.act(
                observations,
                evaluation=deterministic,
                generator=generator,
            ).actions
        )


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


def _launch_args(*, seed: int, environment_index: int) -> tuple[str, ...]:
    return (
        "--dino-training",
        "--dino-base-seed",
        str(seed),
        "--dino-environment-index",
        str(environment_index),
        "--dino-process-generation",
        "0",
    )


def _classify_decision_map(step: EnvStep, decision_count: int) -> str | None:
    if decision_count <= 0 or len(step.observations) < 4:
        return None
    guard_masks = np.asarray(
        step.observations[3][:decision_count, :, 0], dtype=np.float32
    )
    valid_guards = np.count_nonzero(guard_masks > 0.5, axis=1)
    if np.all(valid_guards == 8):
        return "map1"
    if np.all(valid_guards == 11):
        return "map2"
    return None


def _decision_map_is_ready(step: EnvStep, decision_count: int) -> bool:
    return _classify_decision_map(step, decision_count) is not None


def _summarize_map(
    map_name: str, records: Sequence[_EpisodeRecord]
) -> DinoMapEvaluationSummary:
    returns = np.asarray([item.episode_return for item in records], dtype=np.float64)
    meat = np.asarray([item.final_meat for item in records], dtype=np.float64)
    elapsed = np.asarray([item.elapsed_seconds for item in records], dtype=np.float64)
    wins = sum(int(item.win) for item in records)
    return DinoMapEvaluationSummary(
        map_name=map_name,
        episodes=len(records),
        wins=wins,
        win_rate=wins / len(records),
        mean_return=float(returns.mean()),
        mean_final_meat=float(meat.mean()),
        mean_elapsed_seconds=float(elapsed.mean()),
    )


def evaluate_dino_parallel_ppo(
    *,
    checkpoint_path: Path,
    build_path: Path,
    output_directory: Path,
    episodes_per_map: int,
    training_worker_ids: Sequence[int],
    evaluation_worker_id: int,
    evaluation_seed: int,
    device: str,
    action_mode: str = "deterministic",
    policy_seed: int | None = None,
    environment_index: int = 0,
    max_environment_steps: int = 1_000_000,
    python_response_delay_seconds: float = 0.0,
    adapter_factory: Callable[[int], EvaluationAdapter] | None = None,
) -> DinoParallelPPOEvaluationSummary:
    if episodes_per_map <= 0:
        raise ValueError("episodes_per_map must be positive")
    if evaluation_worker_id < 0 or evaluation_seed < 0 or environment_index < 0:
        raise ValueError("worker, seed and environment index cannot be negative")
    if evaluation_worker_id in set(int(item) for item in training_worker_ids):
        raise ValueError("evaluation worker must differ from every training worker")
    if max_environment_steps <= 0:
        raise ValueError("max_environment_steps must be positive")
    if (
        not math.isfinite(python_response_delay_seconds)
        or python_response_delay_seconds < 0.0
    ):
        raise ValueError("Python response delay must be finite and non-negative")
    if device not in {"cpu", "cuda"}:
        raise ValueError("device must be 'cpu' or 'cuda'")
    if device == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("device cuda requires torch.cuda.is_available()")
    if action_mode not in {"deterministic", "stochastic"}:
        raise ValueError("action_mode must be 'deterministic' or 'stochastic'")
    if policy_seed is not None and policy_seed < 0:
        raise ValueError("policy_seed cannot be negative")
    if action_mode == "stochastic" and policy_seed is None:
        raise ValueError("stochastic evaluation requires an explicit policy_seed")

    output = Path(output_directory).resolve()
    if output.exists() and not output.is_dir():
        raise FileExistsError(f"output directory is not a directory: {output}")
    if output.is_dir() and any(output.iterdir()):
        raise FileExistsError(f"output directory is not empty: {output}")
    build = Path(build_path).resolve()
    checkpoint = Path(checkpoint_path).resolve()
    if not build.is_file():
        raise FileNotFoundError(f"Unity build does not exist: {build}")
    if not checkpoint.is_file():
        raise FileNotFoundError(f"checkpoint does not exist: {checkpoint}")

    payload = load_checkpoint(checkpoint, map_location=device)
    metadata = payload["metadata"]
    algorithm = metadata.get("algorithm")
    if algorithm not in {
        "dino_parallel_ppo", "dino_parallel_fpo", "flow_bc", "policyflow"
    }:
        raise ValueError("checkpoint is not a supported structured Dino policy")
    build_hash = _sha256(build)
    if metadata.get("build_sha256") != build_hash:
        raise ValueError("Unity build hash does not match checkpoint")
    config = payload["config"]
    protocol_path = Path(config["protocol_path"]).resolve()
    protocol = DinoProtocol.from_yaml(protocol_path)
    encoder_factory = lambda: SetTransformerDinoEncoder(
        protocol,
        d_model=int(config["encoder_d_model"]),
        heads=int(config["encoder_heads"]),
        inducing_points=int(config["encoder_inducing_points"]),
        layers=int(config["encoder_layers"]),
        dropout=float(config["encoder_dropout"]),
        output_size=int(config["encoder_output_size"]),
    )
    torch_device = torch.device(device)
    policy_generator = torch.Generator(device=torch_device).manual_seed(
        evaluation_seed if policy_seed is None else policy_seed
    )
    if algorithm == "dino_parallel_ppo":
        policy = GaussianActorCritic(
            observation_shapes=protocol.observation_shapes,
            action_size=protocol.action_size,
            hidden_sizes=tuple(int(item) for item in config["hidden_sizes"]),
            encoder_factory=encoder_factory,
        ).to(torch_device)
        encoder_metadata = policy.actor.encoder.checkpoint_metadata()
        validate_checkpoint_compatibility(
            payload,
            expected_observation_shapes=protocol.observation_shapes,
            expected_action_size=protocol.action_size,
            expected_protocol_metadata=protocol.checkpoint_metadata(),
            expected_encoder_metadata=encoder_metadata,
        )
        policy.load_state_dict(payload["model_state"], strict=True)
        time_scale = float(config["time_scale"])
        timeout_wait = int(config["timeout_wait"])
        behavior_name = str(config["behavior_name"])
        solver_steps = velocity_nfe = None
    elif algorithm == "dino_parallel_fpo":
        structured_policy = FlowActorCritic(
            observation_shapes=protocol.observation_shapes,
            action_size=protocol.action_size,
            state_size=int(config["state_size"]),
            time_embedding_size=int(config["time_embedding_size"]),
            velocity_hidden_sizes=tuple(
                int(item) for item in config["velocity_hidden_sizes"]
            ),
            critic_hidden_sizes=tuple(
                int(item) for item in config["critic_hidden_sizes"]
            ),
            encoder_factory=encoder_factory,
        ).to(torch_device)
        validate_checkpoint_compatibility(
            payload,
            expected_observation_shapes=protocol.observation_shapes,
            expected_action_size=protocol.action_size,
            expected_protocol_metadata=protocol.checkpoint_metadata(),
            expected_encoder_metadata=(
                structured_policy.actor.state_encoder.checkpoint_metadata()
            ),
        )
        structured_policy.load_state_dict(payload["model_state"], strict=True)
        policy = _FPOPolicyAdapter(
            structured_policy, nfe=int(metadata["solver"]["nfe"])
        ).to(torch_device)
        time_scale = float(config["time_scale"])
        timeout_wait = int(config["timeout_wait"])
        behavior_name = str(config["behavior_name"])
        solver_steps = None
        velocity_nfe = int(metadata["solver"]["nfe"])
    elif algorithm == "flow_bc":
        validate_flow_bc_checkpoint(payload)
        velocity = ConditionalVelocityMLP(
            observation_shapes=protocol.observation_shapes,
            action_size=protocol.action_size,
            state_size=int(config["state_size"]),
            time_embedding_size=int(config["time_embedding_size"]),
            hidden_sizes=tuple(int(item) for item in config["velocity_hidden_sizes"]),
            state_encoder=encoder_factory(),
        ).to(torch_device)
        velocity.load_state_dict(payload["model_state"], strict=True)
        policy = _FlowBCPolicyAdapter(
            velocity, nfe=int(metadata["solver"]["nfe"])
        ).to(torch_device)
        time_scale = 1.0
        timeout_wait = 120
        behavior_name = "DinoAttackPlanner"
        solver_steps = None
        velocity_nfe = int(metadata["solver"]["nfe"])
    else:
        validate_policyflow_checkpoint(payload)
        structured_policy = PolicyFlowActorCritic(
            observation_shapes=protocol.observation_shapes,
            action_size=protocol.action_size,
            state_size=int(config["state_size"]),
            time_embedding_size=int(config["time_embedding_size"]),
            velocity_hidden_sizes=tuple(config["velocity_hidden_sizes"]),
            critic_hidden_sizes=tuple(config["critic_hidden_sizes"]),
            solver_steps=int(config["solver_steps"]),
            encoder_factory=encoder_factory,
        ).to(torch_device)
        validate_checkpoint_compatibility(
            payload,
            expected_observation_shapes=protocol.observation_shapes,
            expected_action_size=protocol.action_size,
            expected_protocol_metadata=protocol.checkpoint_metadata(),
            expected_encoder_metadata=(
                structured_policy.actor.state_encoder.checkpoint_metadata()
            ),
        )
        structured_policy.load_state_dict(payload["model_state"], strict=True)
        policy = _PolicyFlowPolicyAdapter(structured_policy).to(torch_device)
        time_scale = float(config["time_scale"])
        timeout_wait = int(config["timeout_wait"])
        behavior_name = str(config["behavior_name"])
        solver_steps = structured_policy.solver_steps
        velocity_nfe = structured_policy.velocity_nfe
    policy.eval()
    normalizer = IdentityObservationNormalizer(
        protocol.observation_shapes,
        protocol_manifest_sha256=protocol.manifest_sha256,
    )
    normalizer.load_state_dict(payload["normalizer_state"])

    output.mkdir(parents=True, exist_ok=True)
    unity_directory = output / "unity"
    unity_directory.mkdir()
    if adapter_factory is None:
        channel = EngineConfigurationChannel()
        channel.set_configuration_parameters(
            width=84,
            height=84,
            quality_level=0,
            time_scale=time_scale,
            target_frame_rate=-1,
            capture_frame_rate=0,
        )

        def selected_adapter_factory(worker_id: int) -> EvaluationAdapter:
            return UnityEnvAdapter(
                build,
                worker_id=worker_id,
                seed=evaluation_seed,
                no_graphics=True,
                timeout_wait=timeout_wait,
                behavior_name=behavior_name,
                log_folder=unity_directory,
                side_channels=[channel],
                additional_args=_launch_args(
                    seed=evaluation_seed,
                    environment_index=environment_index,
                ),
                protocol=protocol,
            )

    else:
        selected_adapter_factory = adapter_factory

    tracker = EpisodeTracker()
    accepted: dict[str, list[_EpisodeRecord]] = {"map1": [], "map2": []}
    environment_steps = 0
    idle_steps = 0
    layout_warmup_steps = 0
    current_episode_map: str | None = None
    started = time.perf_counter()
    inference_seconds = 0.0
    inference_calls = 0
    with selected_adapter_factory(evaluation_worker_id) as adapter:
        current_step = adapter.reset()
        while any(len(items) < episodes_per_map for items in accepted.values()):
            pending_ids = np.asarray(adapter.pending_agent_ids, dtype=np.int64)
            decision_count = len(pending_ids)
            count_policy_step = False
            if decision_count == 0:
                actions = np.empty((0, protocol.action_size), dtype=np.float32)
                idle_steps += 1
                layout_warmup_steps = 0
            elif (
                current_episode_map is None
                and not _decision_map_is_ready(current_step, decision_count)
            ):
                actions = np.zeros(
                    (decision_count, protocol.action_size), dtype=np.float32
                )
                actions[:, -1] = -1.0
                layout_warmup_steps += 1
                if layout_warmup_steps > 1_000:
                    raise RuntimeError("evaluation map layout did not become ready")
                idle_steps = 0
            else:
                if current_episode_map is None:
                    current_episode_map = _classify_decision_map(
                        current_step, decision_count
                    )
                    if current_episode_map is None:
                        raise RuntimeError(
                            "evaluation map layout was ready but could not be classified"
                        )
                layout_warmup_steps = 0
                idle_steps = 0
                if environment_steps + decision_count > max_environment_steps:
                    raise RuntimeError("evaluation exceeded max_environment_steps")
                observations = tuple(
                    np.array(stream[:decision_count], dtype=np.float32, copy=True)
                    for stream in current_step.observations
                )
                normalized = normalizer.normalize(observations)
                tensors = tuple(
                    torch.as_tensor(item, device=torch_device) for item in normalized
                )
                if torch_device.type == "cuda":
                    torch.cuda.synchronize(torch_device)
                inference_started = time.perf_counter()
                with torch.inference_mode():
                    policy_output = policy.act(
                        tensors,
                        deterministic=action_mode == "deterministic",
                        generator=policy_generator,
                    )
                if torch_device.type == "cuda":
                    torch.cuda.synchronize(torch_device)
                inference_seconds += time.perf_counter() - inference_started
                inference_calls += 1
                actions = policy_output.actions.detach().cpu().numpy().astype(
                    np.float32, copy=True
                )
                if not np.isfinite(actions).all():
                    raise RuntimeError("evaluation policy produced non-finite actions")
                count_policy_step = True
            # Unity is synchronously blocked inside the ML-Agents gRPC exchange until
            # this call sends the action batch.  An explicit delay here therefore
            # isolates Python response latency without adding Unity-side CPU load.
            if decision_count > 0 and python_response_delay_seconds > 0.0:
                time.sleep(python_response_delay_seconds)
            current_step = adapter.step(actions)
            if count_policy_step:
                environment_steps += decision_count
            if idle_steps > 1_000:
                raise RuntimeError("evaluation made no Agent decision progress")

            episode_summaries = tracker.record(current_step)
            terminal_indices = np.flatnonzero(
                current_step.terminated | current_step.truncated
            )
            if len(episode_summaries) != len(terminal_indices):
                raise RuntimeError("episode summaries and terminal observations diverged")
            if len(terminal_indices) > 0 and current_episode_map is None:
                raise RuntimeError("terminal episode has no classified map provenance")
            for episode, raw_index in zip(episode_summaries, terminal_indices):
                index = int(raw_index)
                map_name = current_episode_map
                if map_name is None:
                    raise RuntimeError("terminal episode has no classified map provenance")
                if len(accepted[map_name]) >= episodes_per_map:
                    continue
                global_row = current_step.observations[0][index]
                final_meat = float(global_row[1]) * _FOOD_SCALE
                elapsed_seconds = (1.0 - float(global_row[0])) * _ROUND_SECONDS
                record = _EpisodeRecord(
                    map_name=map_name,
                    episode_return=episode.episode_return,
                    win=(
                        episode.terminated
                        and float(current_step.rewards[index])
                        >= _VICTORY_TERMINAL_REWARD_THRESHOLD
                    ),
                    final_meat=final_meat,
                    elapsed_seconds=elapsed_seconds,
                    truncated=episode.truncated,
                )
                if not all(
                    math.isfinite(value)
                    for value in (
                        record.episode_return,
                        record.final_meat,
                        record.elapsed_seconds,
                    )
                ):
                    raise RuntimeError("evaluation episode contains non-finite metrics")
                accepted[map_name].append(record)
            if len(terminal_indices) > 0:
                current_episode_map = None

    elapsed = time.perf_counter() - started
    records = accepted["map1"] + accepted["map2"]
    returns = np.asarray([item.episode_return for item in records], dtype=np.float64)
    wins = sum(int(item.win) for item in records)
    maps = tuple(
        _summarize_map(map_name, accepted[map_name])
        for map_name in ("map1", "map2")
    )
    steps_per_second = environment_steps / max(elapsed, 1e-9)
    summary = DinoParallelPPOEvaluationSummary(
        episodes=len(records),
        episodes_per_map=episodes_per_map,
        overall_wins=wins,
        overall_win_rate=wins / len(records),
        mean_return=float(returns.mean()),
        truncated_episodes=sum(int(item.truncated) for item in records),
        environment_steps=environment_steps,
        wall_clock_seconds=elapsed,
        steps_per_second=steps_per_second,
        parameter_count=sum(item.numel() for item in policy.parameters()),
        checkpoint_algorithm=str(algorithm),
        mean_inference_latency_ms=(
            1_000.0 * inference_seconds / max(inference_calls, 1)
        ),
        solver_steps=solver_steps,
        velocity_nfe=velocity_nfe,
        maps=maps,
        checkpoint_path=str(checkpoint),
        checkpoint_sha256=_sha256(checkpoint),
        build_path=str(build),
        build_sha256=build_hash,
        protocol_version=protocol.protocol_version,
        protocol_manifest_sha256=protocol.manifest_sha256,
        normalizer_type=str(normalizer.state_dict()["normalizer_type"]),
        action_mode=action_mode,
        policy_seed=policy_seed,
        deterministic=action_mode == "deterministic",
        victory_terminal_reward_threshold=_VICTORY_TERMINAL_REWARD_THRESHOLD,
        evaluation_seed=evaluation_seed,
        evaluation_worker_id=evaluation_worker_id,
        python_response_delay_seconds=python_response_delay_seconds,
        all_finite=all(
            math.isfinite(value)
            for value in (
                returns.mean(),
                elapsed,
                steps_per_second,
                *(item.win_rate for item in maps),
            )
        ),
    )
    with (output / "evaluation.json").open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(summary.to_dict(), handle, indent=2, sort_keys=True)
        handle.write("\n")
    return summary
