from __future__ import annotations

from collections.abc import Iterator

import pytest
import torch
from torch import nn

from flow_rl.cli.benchmark_flow import run_synthetic_flow_benchmark
from flow_rl.evaluation.flow_benchmark import benchmark_flow_sampler
from flow_rl.models.flow import EulerFlowSampler


class _RecordingVelocity(nn.Module):
    action_size = 2

    def __init__(self) -> None:
        super().__init__()
        self.initial_latents: list[torch.Tensor] = []
        self.calls = 0

    def forward(
        self,
        observations: tuple[torch.Tensor, ...],
        latent_actions: torch.Tensor,
        time: torch.Tensor,
    ) -> torch.Tensor:
        del observations
        if torch.all(time == 0.0):
            self.initial_latents.append(latent_actions.detach().clone())
        self.calls += 1
        return torch.zeros_like(latent_actions)


def _clock(values: list[float]):
    iterator: Iterator[float] = iter(values)
    return lambda: next(iterator)


def test_benchmark_orders_nfe_excludes_warmup_and_reuses_initial_noise() -> None:
    # Counting warmups or regenerating noise per NFE makes latency comparisons invalid.
    velocity = _RecordingVelocity()
    sampler = EulerFlowSampler(velocity)
    observations = (torch.zeros(3, 4, dtype=torch.float32),)

    results = benchmark_flow_sampler(
        sampler,
        observations,
        nfes=(4, 1),
        warmup_runs=1,
        measured_runs=2,
        seed=123,
        clock=_clock([0.0, 0.002, 1.0, 1.006, 2.0, 2.004, 3.0, 3.012]),
    )

    assert [result.nfe for result in results] == [1, 4]
    assert results[0].batch_size == 3
    assert results[0].measured_runs == 2
    assert results[0].mean_latency_ms == pytest.approx(4.0)
    assert results[0].p50_latency_ms == pytest.approx(4.0)
    assert results[0].p95_latency_ms == pytest.approx(5.8)
    assert results[1].mean_latency_ms == pytest.approx(8.0)
    assert results[0].peak_gpu_memory_bytes is None
    assert results[1].peak_gpu_memory_bytes is None
    assert len(velocity.initial_latents) == 6
    for latent in velocity.initial_latents[1:]:
        torch.testing.assert_close(latent, velocity.initial_latents[0])


def test_benchmark_rejects_duplicate_nfe() -> None:
    sampler = EulerFlowSampler(_RecordingVelocity())

    with pytest.raises(ValueError, match="unique"):
        benchmark_flow_sampler(
            sampler,
            (torch.zeros(1, 4, dtype=torch.float32),),
            nfes=(1, 1),
            warmup_runs=0,
            measured_runs=1,
            seed=0,
        )


def test_synthetic_benchmark_writes_versioned_json(tmp_path) -> None:
    # A CLI that omits configuration cannot support repeatable NFE evidence.
    destination = tmp_path / "benchmark.json"

    payload = run_synthetic_flow_benchmark(
        output_path=destination,
        observation_shapes=((4,),),
        action_size=2,
        batch_size=3,
        nfes=(1, 2),
        warmup_runs=0,
        measured_runs=1,
        seed=9,
        device="cpu",
    )

    assert destination.is_file()
    assert payload["schema_version"] == 1
    assert payload["seed"] == 9
    assert payload["observation_shapes"] == [[4]]
    assert [row["nfe"] for row in payload["results"]] == [1, 2]
