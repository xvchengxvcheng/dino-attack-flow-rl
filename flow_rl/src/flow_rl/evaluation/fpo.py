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
from mlagents_envs.side_channel.engine_configuration_channel import EngineConfigurationChannel

from flow_rl.data.normalization import ObservationNormalizer
from flow_rl.envs.unity import UnityEnvAdapter
from flow_rl.evaluation.runner import EvaluationAdapter, evaluate_policy
from flow_rl.models.fpo_policy import FlowActorCritic
from flow_rl.tracking.checkpoint import load_checkpoint, validate_flow_checkpoint
from flow_rl.tracking.run import RunLogger


@dataclass(frozen=True)
class FPOEvaluationSummary:
    episodes: int
    mean_return: float
    standard_deviation: float
    minimum_return: float
    maximum_return: float
    environment_steps: int
    wall_clock_seconds: float
    steps_per_second: float
    mean_inference_latency_ms: float
    parameter_count: int
    normalizer_count: int
    nfe: int
    evaluation_seed: int
    checkpoint_path: str
    build_path: str
    training_worker_id: int
    evaluation_worker_id: int
    all_finite: bool

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def evaluate_fpo_checkpoint(
    *,
    checkpoint_path: Path,
    build_path: Path,
    output_directory: Path,
    episodes: int,
    training_worker_id: int,
    evaluation_worker_id: int,
    evaluation_seed: int,
    nfe: int,
    device: str,
    adapter_factory: Callable[[int], EvaluationAdapter] | None = None,
) -> FPOEvaluationSummary:
    if training_worker_id == evaluation_worker_id:
        raise ValueError("training and evaluation worker IDs must differ")
    if episodes <= 0:
        raise ValueError("episodes must be positive")
    if nfe not in {1, 2, 4, 8}:
        raise ValueError("nfe must be one of 1, 2, 4, 8")
    if device not in {"cpu", "cuda"}:
        raise ValueError("device must be 'cpu' or 'cuda'")
    if device == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("device cuda requires torch.cuda.is_available()")
    resolved_build = Path(build_path).resolve()
    if not resolved_build.is_file():
        raise FileNotFoundError(f"Unity build does not exist: {resolved_build}")
    checkpoint = Path(checkpoint_path).resolve()
    payload = load_checkpoint(checkpoint, map_location=device)
    validate_flow_checkpoint(payload)
    metadata = payload["metadata"]
    if metadata["build_sha256"] != _sha256(resolved_build):
        raise ValueError("Unity build hash does not match checkpoint")
    observation_shapes = tuple(tuple(shape) for shape in metadata["observation_shapes"])
    action_size = int(metadata["action_size"])
    config = payload["config"]
    torch_device = torch.device(device)
    policy = FlowActorCritic(
        observation_shapes=observation_shapes,
        action_size=action_size,
        state_size=int(config["state_size"]),
        time_embedding_size=int(config["time_embedding_size"]),
        velocity_hidden_sizes=tuple(int(size) for size in config["velocity_hidden_sizes"]),
        critic_hidden_sizes=tuple(int(size) for size in config["critic_hidden_sizes"]),
    ).to(torch_device)
    policy.load_state_dict(payload["model_state"], strict=True)
    policy.eval()
    normalizer = ObservationNormalizer(observation_shapes)
    normalizer.load_state_dict(payload["normalizer_state"])
    normalizer_count = int(normalizer.state_dict()["count"])
    output_directory = Path(output_directory).resolve()
    evaluation_config = {
        "checkpoint_path": str(checkpoint),
        "build_path": str(resolved_build),
        "episodes": episodes,
        "training_worker_id": training_worker_id,
        "evaluation_worker_id": evaluation_worker_id,
        "evaluation_seed": evaluation_seed,
        "nfe": nfe,
        "device": device,
        "stochastic_seeded": True,
    }
    generator = torch.Generator(device=torch_device).manual_seed(evaluation_seed)
    inference_seconds = 0.0
    inference_calls = 0
    started = time.perf_counter()
    with RunLogger(output_directory, evaluation_config) as logger:
        unity_directory = output_directory / "unity"
        unity_directory.mkdir(parents=True, exist_ok=False)
        if adapter_factory is None:
            engine_channel = EngineConfigurationChannel()
            engine_channel.set_configuration_parameters(
                width=84,
                height=84,
                quality_level=0,
                time_scale=float(config["time_scale"]),
                target_frame_rate=-1,
                capture_frame_rate=0,
            )

            def selected_adapter_factory(worker_id: int) -> EvaluationAdapter:
                return UnityEnvAdapter(
                    resolved_build,
                    worker_id=worker_id,
                    seed=int(config["seed"]),
                    no_graphics=True,
                    timeout_wait=int(config["timeout_wait"]),
                    behavior_name=config.get("behavior_name"),
                    log_folder=unity_directory,
                    side_channels=[engine_channel],
                )
        else:
            selected_adapter_factory = adapter_factory

        def seeded_policy(
            observations: tuple[np.ndarray, ...],
            agent_ids: np.ndarray,
        ) -> np.ndarray:
            nonlocal inference_seconds, inference_calls
            del agent_ids
            normalized = normalizer.normalize(observations)
            tensors = tuple(torch.as_tensor(item, device=torch_device) for item in normalized)
            if torch_device.type == "cuda":
                torch.cuda.synchronize(torch_device)
            call_started = time.perf_counter()
            with torch.inference_mode():
                sample = policy.sampler.sample(
                    tensors,
                    nfe=nfe,
                    generator=generator,
                )
            if torch_device.type == "cuda":
                torch.cuda.synchronize(torch_device)
            inference_seconds += time.perf_counter() - call_started
            inference_calls += 1
            return sample.actions.cpu().numpy().astype(np.float32, copy=True)

        result = evaluate_policy(
            adapter_factory=selected_adapter_factory,
            policy=seeded_policy,
            episodes=episodes,
            training_worker_id=training_worker_id,
            evaluation_worker_id=evaluation_worker_id,
        )
        for episode in result.episodes:
            logger.log_episode(episode, result.environment_steps)
        elapsed = time.perf_counter() - started
        steps_per_second = result.environment_steps / elapsed
        mean_inference_latency_ms = 1000.0 * inference_seconds / inference_calls
        logger.log_metrics(
            {
                "evaluation/mean_return": result.mean_return,
                "runtime/steps_per_second": steps_per_second,
                "runtime/inference_latency_ms": mean_inference_latency_ms,
                "runtime/nfe": float(nfe),
            },
            result.environment_steps,
        )
    returns = np.asarray(
        [episode.episode_return for episode in result.episodes],
        dtype=np.float64,
    )
    summary = FPOEvaluationSummary(
        episodes=len(result.episodes),
        mean_return=float(returns.mean()),
        standard_deviation=float(returns.std()),
        minimum_return=float(returns.min()),
        maximum_return=float(returns.max()),
        environment_steps=result.environment_steps,
        wall_clock_seconds=elapsed,
        steps_per_second=steps_per_second,
        mean_inference_latency_ms=mean_inference_latency_ms,
        parameter_count=sum(parameter.numel() for parameter in policy.parameters()),
        normalizer_count=normalizer_count,
        nfe=nfe,
        evaluation_seed=evaluation_seed,
        checkpoint_path=str(checkpoint),
        build_path=str(resolved_build),
        training_worker_id=training_worker_id,
        evaluation_worker_id=evaluation_worker_id,
        all_finite=all(
            math.isfinite(value)
            for value in (
                returns.mean(), returns.std(), elapsed, steps_per_second,
                mean_inference_latency_ms,
            )
        ),
    )
    with (output_directory / "evaluation.json").open(
        "w", encoding="utf-8", newline="\n"
    ) as handle:
        json.dump(summary.to_dict(), handle, indent=2, sort_keys=True)
        handle.write("\n")
    return summary


def ablate_fpo_nfe(
    *,
    checkpoint_path: Path,
    build_path: Path,
    output_directory: Path,
    nfes: Sequence[int],
    episodes: int,
    training_worker_id: int,
    first_evaluation_worker_id: int,
    evaluation_seed: int,
    device: str,
    adapter_factory: Callable[[int], EvaluationAdapter] | None = None,
) -> tuple[FPOEvaluationSummary, ...]:
    normalized_nfes = tuple(sorted(int(nfe) for nfe in nfes))
    if len(set(normalized_nfes)) != len(normalized_nfes):
        raise ValueError("nfes must be unique")
    if any(nfe not in {1, 2, 4, 8} for nfe in normalized_nfes):
        raise ValueError("nfes must be selected from 1, 2, 4, 8")
    destination = Path(output_directory).resolve()
    if destination.is_dir() and any(destination.iterdir()):
        raise FileExistsError(f"ablation directory is not empty: {destination}")
    results = tuple(
        evaluate_fpo_checkpoint(
            checkpoint_path=checkpoint_path,
            build_path=build_path,
            output_directory=destination / f"nfe-{nfe}",
            episodes=episodes,
            training_worker_id=training_worker_id,
            evaluation_worker_id=first_evaluation_worker_id + index,
            evaluation_seed=evaluation_seed,
            nfe=nfe,
            device=device,
            adapter_factory=adapter_factory,
        )
        for index, nfe in enumerate(normalized_nfes)
    )
    with (destination / "ablation.json").open(
        "w", encoding="utf-8", newline="\n"
    ) as handle:
        json.dump([result.to_dict() for result in results], handle, indent=2, sort_keys=True)
        handle.write("\n")
    return results


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()
