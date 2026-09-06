from __future__ import annotations

import math
import time
from collections.abc import Callable, Sequence
from dataclasses import asdict, dataclass

import numpy as np
import torch

from flow_rl.models.flow import EulerFlowSampler
from flow_rl.models.gaussian import flatten_observations


@dataclass(frozen=True)
class FlowBenchmarkResult:
    nfe: int
    batch_size: int
    measured_runs: int
    mean_latency_ms: float
    p50_latency_ms: float
    p95_latency_ms: float
    peak_gpu_memory_bytes: int | None
    parameter_count: int

    def to_dict(self) -> dict[str, float | int | None]:
        return asdict(self)


def benchmark_flow_sampler(
    sampler: EulerFlowSampler,
    observations: tuple[torch.Tensor, ...],
    *,
    nfes: Sequence[int] = (1, 2, 4, 8),
    warmup_runs: int = 2,
    measured_runs: int = 10,
    seed: int = 0,
    clock: Callable[[], float] = time.perf_counter,
) -> tuple[FlowBenchmarkResult, ...]:
    normalized_nfes = tuple(int(nfe) for nfe in nfes)
    if not normalized_nfes or any(nfe <= 0 for nfe in normalized_nfes):
        raise ValueError("nfes must contain positive integers")
    if len(set(normalized_nfes)) != len(normalized_nfes):
        raise ValueError("nfes must be unique")
    if warmup_runs < 0 or measured_runs <= 0:
        raise ValueError("warmup_runs must be nonnegative and measured_runs positive")
    flattened = flatten_observations(observations)
    batch_size = flattened.shape[0]
    generator = torch.Generator(device=flattened.device).manual_seed(seed)
    initial_noise = torch.randn(
        (batch_size, sampler.action_size),
        dtype=torch.float32,
        device=flattened.device,
        generator=generator,
    )
    is_cuda = flattened.device.type == "cuda"
    parameter_count = sum(
        parameter.numel() for parameter in sampler.velocity_model.parameters()
    )
    results: list[FlowBenchmarkResult] = []
    for nfe in sorted(normalized_nfes):
        for _ in range(warmup_runs):
            sampler.sample(
                observations,
                nfe=nfe,
                initial_noise=initial_noise,
            )
        if is_cuda:
            torch.cuda.synchronize(flattened.device)
            torch.cuda.reset_peak_memory_stats(flattened.device)
        latencies_ms: list[float] = []
        for _ in range(measured_runs):
            if is_cuda:
                torch.cuda.synchronize(flattened.device)
            started = clock()
            sampler.sample(
                observations,
                nfe=nfe,
                initial_noise=initial_noise,
            )
            if is_cuda:
                torch.cuda.synchronize(flattened.device)
            latency_ms = (clock() - started) * 1000.0
            if not math.isfinite(latency_ms) or latency_ms < 0.0:
                raise RuntimeError("flow benchmark latency must be finite and nonnegative")
            latencies_ms.append(latency_ms)
        values = np.asarray(latencies_ms, dtype=np.float64)
        results.append(
            FlowBenchmarkResult(
                nfe=nfe,
                batch_size=batch_size,
                measured_runs=measured_runs,
                mean_latency_ms=float(values.mean()),
                p50_latency_ms=float(np.percentile(values, 50)),
                p95_latency_ms=float(np.percentile(values, 95)),
                peak_gpu_memory_bytes=(
                    int(torch.cuda.max_memory_allocated(flattened.device))
                    if is_cuda
                    else None
                ),
                parameter_count=parameter_count,
            )
        )
    return tuple(results)
