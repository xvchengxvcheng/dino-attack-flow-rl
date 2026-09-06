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
from flow_rl.models.policy import GaussianActorCritic
from flow_rl.tracking.checkpoint import load_checkpoint
from flow_rl.tracking.run import RunLogger


@dataclass(frozen=True)
class PPOEvaluationSummary:
    episodes: int
    mean_return: float
    standard_deviation: float
    minimum_return: float
    maximum_return: float
    environment_steps: int
    wall_clock_seconds: float
    steps_per_second: float
    parameter_count: int
    normalizer_count: int
    checkpoint_path: str
    build_path: str
    training_worker_id: int
    evaluation_worker_id: int
    all_finite: bool

    def to_dict(self) -> dict[str, object]:
        return asdict(self)


def evaluate_ppo_checkpoint(
    *,
    checkpoint_path: Path,
    build_path: Path,
    output_directory: Path,
    episodes: int,
    training_worker_id: int,
    evaluation_worker_id: int,
    device: str,
    adapter_factory: Callable[[int], EvaluationAdapter] | None = None,
) -> PPOEvaluationSummary:
    if training_worker_id == evaluation_worker_id:
        raise ValueError("training and evaluation worker IDs must differ")
    if episodes <= 0:
        raise ValueError("episodes must be positive")
    resolved_build = Path(build_path).resolve()
    if not resolved_build.is_file():
        raise FileNotFoundError(f"Unity build does not exist: {resolved_build}")
    if device not in {"cpu", "cuda"}:
        raise ValueError("device must be 'cpu' or 'cuda'")
    if device == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("device cuda requires torch.cuda.is_available()")
    checkpoint = Path(checkpoint_path).resolve()
    payload = load_checkpoint(checkpoint, map_location=device)
    metadata = payload["metadata"]
    if metadata["build_sha256"] != _sha256(resolved_build):
        raise ValueError("Unity build hash does not match checkpoint")
    observation_shapes = tuple(
        tuple(shape) for shape in metadata["observation_shapes"]
    )
    action_size = int(metadata["action_size"])
    config = payload["config"]
    hidden_sizes = tuple(int(size) for size in config["hidden_sizes"])
    torch_device = torch.device(device)
    policy = GaussianActorCritic(
        observation_shapes=observation_shapes,
        action_size=action_size,
        hidden_sizes=hidden_sizes,
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
        "device": device,
        "deterministic": True,
    }
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

        def deterministic_policy(
            observations: tuple[np.ndarray, ...],
            agent_ids: np.ndarray,
        ) -> np.ndarray:
            del agent_ids
            normalized = normalizer.normalize(observations)
            tensor_observations = tuple(
                torch.as_tensor(observation, device=torch_device)
                for observation in normalized
            )
            with torch.inference_mode():
                output = policy.act(tensor_observations, deterministic=True)
            return output.actions.cpu().numpy().astype(np.float32, copy=True)

        result = evaluate_policy(
            adapter_factory=selected_adapter_factory,
            policy=deterministic_policy,
            episodes=episodes,
            training_worker_id=training_worker_id,
            evaluation_worker_id=evaluation_worker_id,
        )
        for episode in result.episodes:
            logger.log_episode(episode, result.environment_steps)
        elapsed = time.perf_counter() - started
        steps_per_second = result.environment_steps / elapsed
        logger.log_metrics(
            {
                "evaluation/mean_return": result.mean_return,
                "runtime/steps_per_second": steps_per_second,
            },
            result.environment_steps,
        )
    returns = np.asarray(
        [episode.episode_return for episode in result.episodes],
        dtype=np.float64,
    )
    summary = PPOEvaluationSummary(
        episodes=len(result.episodes),
        mean_return=float(returns.mean()),
        standard_deviation=float(returns.std()),
        minimum_return=float(returns.min()),
        maximum_return=float(returns.max()),
        environment_steps=result.environment_steps,
        wall_clock_seconds=elapsed,
        steps_per_second=steps_per_second,
        parameter_count=sum(parameter.numel() for parameter in policy.parameters()),
        normalizer_count=normalizer_count,
        checkpoint_path=str(checkpoint),
        build_path=str(resolved_build),
        training_worker_id=training_worker_id,
        evaluation_worker_id=evaluation_worker_id,
        all_finite=all(
            math.isfinite(value)
            for value in (
                returns.mean(),
                returns.std(),
                elapsed,
                steps_per_second,
            )
        ),
    )
    with (output_directory / "evaluation.json").open(
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
