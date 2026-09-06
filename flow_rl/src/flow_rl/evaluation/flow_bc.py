from __future__ import annotations

import hashlib
import json
import math
import time
from collections.abc import Callable
from dataclasses import asdict, dataclass
from pathlib import Path

import numpy as np
import torch
from mlagents_envs.side_channel.engine_configuration_channel import (
    EngineConfigurationChannel,
)

from flow_rl.data.normalization import ObservationNormalizer
from flow_rl.envs.unity import UnityEnvAdapter
from flow_rl.evaluation.runner import EvaluationAdapter, evaluate_policy
from flow_rl.models.flow import ConditionalVelocityMLP
from flow_rl.tracking.checkpoint import load_checkpoint, validate_flow_bc_checkpoint
from flow_rl.tracking.run import RunLogger


@dataclass(frozen=True)
class FlowBCEvaluationSummary:
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


def sample_bounded_flow(
    model: ConditionalVelocityMLP,
    observations: tuple[torch.Tensor, ...],
    *,
    nfe: int,
    generator: torch.Generator,
    initial_noise: torch.Tensor | None = None,
) -> torch.Tensor:
    if nfe not in {1, 2, 4, 8}:
        raise ValueError("nfe must be one of 1, 2, 4, 8")
    batch_size = observations[0].shape[0]
    expected = (batch_size, model.action_size)
    if initial_noise is None:
        current = torch.randn(
            expected,
            dtype=torch.float32,
            device=observations[0].device,
            generator=generator,
        )
    else:
        if initial_noise.dtype != torch.float32 or initial_noise.shape != expected:
            raise TypeError("initial_noise must be float32 with shape (batch, action_size)")
        current = initial_noise.clone()
    dt = 1.0 / nfe
    for step in range(nfe):
        sample_time = torch.full(
            (batch_size, 1),
            step * dt,
            dtype=torch.float32,
            device=current.device,
        )
        current = torch.clamp(
            current + dt * model(observations, current, sample_time),
            -1.0,
            1.0,
        )
    return current


def evaluate_flow_bc_checkpoint(
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
) -> FlowBCEvaluationSummary:
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
    build = Path(build_path).resolve()
    checkpoint = Path(checkpoint_path).resolve()
    if not build.is_file():
        raise FileNotFoundError(f"Unity build does not exist: {build}")
    payload = load_checkpoint(checkpoint, map_location=device)
    validate_flow_bc_checkpoint(payload)
    metadata = payload["metadata"]
    if metadata["build_sha256"] != _sha256(build):
        raise ValueError("Unity build hash does not match checkpoint")
    observation_shapes = tuple(tuple(shape) for shape in metadata["observation_shapes"])
    config = payload["config"]
    torch_device = torch.device(device)
    model = ConditionalVelocityMLP(
        observation_shapes=observation_shapes,
        action_size=int(metadata["action_size"]),
        state_size=int(config["state_size"]),
        time_embedding_size=int(config["time_embedding_size"]),
        hidden_sizes=tuple(int(size) for size in config["velocity_hidden_sizes"]),
    ).to(torch_device)
    model.load_state_dict(payload["model_state"], strict=True)
    model.eval()
    normalizer = ObservationNormalizer(observation_shapes)
    normalizer.load_state_dict(payload["normalizer_state"])
    normalizer_count = int(normalizer.state_dict()["count"])
    destination = Path(output_directory).resolve()
    evaluation_config = {
        "checkpoint_path": str(checkpoint),
        "build_path": str(build),
        "episodes": episodes,
        "training_worker_id": training_worker_id,
        "evaluation_worker_id": evaluation_worker_id,
        "evaluation_seed": evaluation_seed,
        "nfe": nfe,
        "device": device,
    }
    generator = torch.Generator(device=torch_device).manual_seed(evaluation_seed)
    inference_seconds = 0.0
    inference_calls = 0
    started = time.perf_counter()
    with RunLogger(destination, evaluation_config) as logger:
        unity_directory = destination / "unity"
        unity_directory.mkdir(parents=True, exist_ok=False)
        if adapter_factory is None:
            engine_channel = EngineConfigurationChannel()
            engine_channel.set_configuration_parameters(
                width=84,
                height=84,
                quality_level=0,
                time_scale=20.0,
                target_frame_rate=-1,
                capture_frame_rate=0,
            )

            def selected_factory(worker_id: int) -> EvaluationAdapter:
                return UnityEnvAdapter(
                    build,
                    worker_id=worker_id,
                    seed=evaluation_seed,
                    no_graphics=True,
                    timeout_wait=120,
                    behavior_name=None,
                    log_folder=unity_directory,
                    side_channels=[engine_channel],
                )
        else:
            selected_factory = adapter_factory

        def policy(observations: tuple[np.ndarray, ...], agent_ids: np.ndarray) -> np.ndarray:
            nonlocal inference_seconds, inference_calls
            del agent_ids
            normalized = normalizer.normalize(observations)
            tensors = tuple(torch.as_tensor(item, device=torch_device) for item in normalized)
            if torch_device.type == "cuda":
                torch.cuda.synchronize(torch_device)
            call_started = time.perf_counter()
            with torch.inference_mode():
                actions = sample_bounded_flow(
                    model, tensors, nfe=nfe, generator=generator
                )
            if torch_device.type == "cuda":
                torch.cuda.synchronize(torch_device)
            inference_seconds += time.perf_counter() - call_started
            inference_calls += 1
            return actions.cpu().numpy().astype(np.float32, copy=True)

        result = evaluate_policy(
            adapter_factory=selected_factory,
            policy=policy,
            episodes=episodes,
            training_worker_id=training_worker_id,
            evaluation_worker_id=evaluation_worker_id,
        )
        for episode in result.episodes:
            logger.log_episode(episode, result.environment_steps)
        elapsed = time.perf_counter() - started
        steps_per_second = result.environment_steps / elapsed
        latency = 1_000.0 * inference_seconds / inference_calls
        logger.log_metrics(
            {
                "evaluation/mean_return": result.mean_return,
                "runtime/steps_per_second": steps_per_second,
                "runtime/inference_latency_ms": latency,
                "runtime/nfe": float(nfe),
            },
            result.environment_steps,
        )
    returns = np.asarray([item.episode_return for item in result.episodes], dtype=np.float64)
    summary = FlowBCEvaluationSummary(
        episodes=len(result.episodes),
        mean_return=float(returns.mean()),
        standard_deviation=float(returns.std()),
        minimum_return=float(returns.min()),
        maximum_return=float(returns.max()),
        environment_steps=result.environment_steps,
        wall_clock_seconds=elapsed,
        steps_per_second=steps_per_second,
        mean_inference_latency_ms=latency,
        parameter_count=sum(parameter.numel() for parameter in model.parameters()),
        normalizer_count=normalizer_count,
        nfe=nfe,
        evaluation_seed=evaluation_seed,
        checkpoint_path=str(checkpoint),
        build_path=str(build),
        training_worker_id=training_worker_id,
        evaluation_worker_id=evaluation_worker_id,
        all_finite=all(
            math.isfinite(value)
            for value in (returns.mean(), returns.std(), elapsed, steps_per_second, latency)
        ),
    )
    with (destination / "evaluation.json").open(
        "w", encoding="utf-8", newline="\n"
    ) as handle:
        json.dump(summary.to_dict(), handle, indent=2, sort_keys=True)
        handle.write("\n")
    return summary


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()
