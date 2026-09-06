from __future__ import annotations

import argparse
import json
from collections.abc import Sequence
from pathlib import Path

import torch

from flow_rl.evaluation.flow_benchmark import benchmark_flow_sampler
from flow_rl.models.flow import ConditionalVelocityMLP, EulerFlowSampler


def run_synthetic_flow_benchmark(
    *,
    output_path: Path,
    observation_shapes: Sequence[tuple[int, ...]],
    action_size: int,
    batch_size: int,
    nfes: Sequence[int],
    warmup_runs: int,
    measured_runs: int,
    seed: int,
    device: str,
) -> dict[str, object]:
    if device not in {"cpu", "cuda"}:
        raise ValueError("device must be 'cpu' or 'cuda'")
    if device == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("device cuda requires torch.cuda.is_available()")
    if batch_size <= 0 or action_size <= 0:
        raise ValueError("batch_size and action_size must be positive")
    normalized_shapes = tuple(tuple(shape) for shape in observation_shapes)
    torch_device = torch.device(device)
    torch.manual_seed(seed)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(seed)
    velocity = ConditionalVelocityMLP(
        observation_shapes=normalized_shapes,
        action_size=action_size,
    ).to(torch_device)
    sampler = EulerFlowSampler(velocity)
    observation_generator = torch.Generator(device=torch_device).manual_seed(seed + 1)
    observations = tuple(
        torch.randn(
            (batch_size, *shape),
            dtype=torch.float32,
            device=torch_device,
            generator=observation_generator,
        )
        for shape in normalized_shapes
    )
    results = benchmark_flow_sampler(
        sampler,
        observations,
        nfes=nfes,
        warmup_runs=warmup_runs,
        measured_runs=measured_runs,
        seed=seed + 2,
    )
    payload: dict[str, object] = {
        "schema_version": 1,
        "device": device,
        "seed": seed,
        "observation_shapes": [list(shape) for shape in normalized_shapes],
        "action_size": action_size,
        "batch_size": batch_size,
        "warmup_runs": warmup_runs,
        "measured_runs": measured_runs,
        "results": [result.to_dict() for result in results],
    }
    destination = Path(output_path).resolve()
    destination.parent.mkdir(parents=True, exist_ok=True)
    if destination.exists():
        raise FileExistsError(f"benchmark output already exists: {destination}")
    with destination.open("w", encoding="utf-8", newline="\n") as handle:
        json.dump(payload, handle, indent=2, sort_keys=True)
        handle.write("\n")
    return payload


def _parse_shape(raw: str) -> tuple[int, ...]:
    dimensions = tuple(int(value) for value in raw.split("x"))
    if not dimensions or any(value <= 0 for value in dimensions):
        raise argparse.ArgumentTypeError("shape must contain positive dimensions")
    return dimensions


def main() -> None:
    parser = argparse.ArgumentParser(description="Benchmark the shared Flow sampler")
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--observation-shape", type=_parse_shape, action="append", required=True)
    parser.add_argument("--action-size", type=int, required=True)
    parser.add_argument("--batch-size", type=int, default=1024)
    parser.add_argument("--nfe", type=int, action="append", default=None)
    parser.add_argument("--warmup-runs", type=int, default=10)
    parser.add_argument("--measured-runs", type=int, default=100)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--device", choices=("cpu", "cuda"), default="cpu")
    arguments = parser.parse_args()
    run_synthetic_flow_benchmark(
        output_path=arguments.output,
        observation_shapes=arguments.observation_shape,
        action_size=arguments.action_size,
        batch_size=arguments.batch_size,
        nfes=(1, 2, 4, 8) if arguments.nfe is None else arguments.nfe,
        warmup_runs=arguments.warmup_runs,
        measured_runs=arguments.measured_runs,
        seed=arguments.seed,
        device=arguments.device,
    )


if __name__ == "__main__":
    main()
