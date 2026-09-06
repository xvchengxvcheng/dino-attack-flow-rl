from __future__ import annotations

import csv
import json
from dataclasses import replace
from pathlib import Path

import numpy as np
import pytest
import torch

from flow_rl.envs.types import EnvStep
from flow_rl.tracking.checkpoint import load_checkpoint, validate_flow_checkpoint
from flow_rl.training.fpo_config import FPOTrainingConfig
from flow_rl.training.fpo_trainer import FPOTrainer


class _Adapter:
    behavior_name = "test"
    continuous_action_size = 1

    def __init__(self) -> None:
        self._pending = np.asarray([0, 1], dtype=np.int64)
        self._tick = 0
        self.close_count = 0
        self.actions: list[np.ndarray] = []

    @property
    def pending_agent_ids(self) -> np.ndarray:
        return self._pending.copy()

    def __enter__(self) -> "_Adapter":
        return self

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None:
        self.close_count += 1

    def reset(self) -> EnvStep:
        self._tick = 0
        return self._decisions([0, 1])

    def step(self, actions: np.ndarray) -> EnvStep:
        self.actions.append(actions.copy())
        self._tick += 1
        if self._tick == 3:
            self._pending = np.asarray([0, 1], dtype=np.int64)
            return EnvStep(
                agent_ids=np.asarray([0, 1, 0], dtype=np.int64),
                observations=(np.asarray([[3.0], [4.0], [9.0]], dtype=np.float32),),
                rewards=np.asarray([0.0, 1.0, 2.0], dtype=np.float32),
                terminated=np.asarray([False, False, True], dtype=bool),
                truncated=np.zeros(3, dtype=bool),
            )
        return self._decisions([1, 0] if self._tick % 2 else [0, 1])

    def _decisions(self, ids: list[int]) -> EnvStep:
        self._pending = np.asarray(ids, dtype=np.int64)
        observations = [float(self._tick + agent_id + 1) for agent_id in ids]
        return EnvStep(
            agent_ids=self._pending,
            observations=(np.asarray(observations, dtype=np.float32).reshape(2, 1),),
            rewards=np.ones(2, dtype=np.float32),
            terminated=np.zeros(2, dtype=bool),
            truncated=np.zeros(2, dtype=bool),
        )


def _config(tmp_path: Path) -> FPOTrainingConfig:
    build = tmp_path / "3DBall.exe"
    build.write_bytes(b"test-build")
    return FPOTrainingConfig(
        build_path=build,
        run_directory=tmp_path / "fpo-run",
        total_environment_steps=8,
        rollout_size=4,
        batch_size=2,
        epochs=1,
        gamma=0.9,
        gae_lambda=0.8,
        learning_rate=1e-3,
        final_learning_rate=1e-3,
        clip_range=0.2,
        final_clip_range=0.2,
        max_gradient_norm=0.5,
        state_size=4,
        time_embedding_size=4,
        velocity_hidden_sizes=(8,),
        critic_hidden_sizes=(8,),
        nfe=1,
        num_fpo_samples=2,
        difference_clip=3.0,
        positive_advantage=False,
        checkpoint_interval=4,
        time_scale=1.0,
        device="cpu",
        worker_id=20,
        evaluation_worker_id=21,
        seed=7,
        timeout_wait=10,
        max_agent_absence_steps=2,
        behavior_name=None,
        resume_checkpoint=None,
    )


def test_fpo_trainer_updates_logs_and_saves_complete_flow_checkpoint(
    tmp_path: Path,
) -> None:
    # Missing auxiliary data or solver metadata would fail before a resumable run exists.
    adapter = _Adapter()
    config = _config(tmp_path)
    trainer = FPOTrainer(config, adapter_factory=lambda **_: adapter)

    summary = trainer.train()

    assert summary.environment_steps == 8
    assert summary.optimizer_updates == 2
    assert summary.episodes == 1
    assert summary.all_finite is True
    assert adapter.close_count == 1
    payload = load_checkpoint(Path(summary.final_checkpoint))
    validate_flow_checkpoint(payload)
    assert payload["metadata"]["solver"] == {
        "method": "euler",
        "direction": "0_to_1",
        "nfe": 1,
        "action_transform": "tanh",
    }
    assert payload["metadata"]["fpo_source_commit"] == (
        "418c2554f7cd22d52e14c07d951280929d73bf2f"
    )
    assert set(payload["optimizer_state"]) == {"actor", "critic"}
    with (config.run_directory / "updates.csv").open(newline="", encoding="utf-8") as handle:
        rows = list(csv.DictReader(handle))
    assert len(rows) == 2
    assert all(float(row["flow_loss"]) >= 0.0 for row in rows)
    with (config.run_directory / "summary.json").open(encoding="utf-8") as handle:
        assert json.load(handle)["environment_steps"] == 8


def test_fpo_trainer_resumes_both_optimizers_and_rng_into_new_directory(
    tmp_path: Path,
) -> None:
    initial_config = _config(tmp_path)
    FPOTrainer(initial_config, adapter_factory=lambda **_: _Adapter()).train()
    resumed_config = replace(
        initial_config,
        run_directory=tmp_path / "fpo-resumed",
        resume_checkpoint=initial_config.run_directory / "checkpoints" / "step-4.pt",
    )
    adapter = _Adapter()

    summary = FPOTrainer(
        resumed_config,
        adapter_factory=lambda **_: adapter,
    ).train()

    assert summary.environment_steps == 8
    assert summary.optimizer_updates == 2
    assert len(adapter.actions) == 2
    payload = load_checkpoint(Path(summary.final_checkpoint))
    assert payload["normalizer_state"]["count"] > 0
    assert payload["optimizer_state"]["actor"]["state"]
    assert payload["optimizer_state"]["critic"]["state"]


@pytest.mark.skipif(not torch.cuda.is_available(), reason="CUDA is unavailable")
def test_fpo_trainer_resumes_cuda_rng_state_from_cuda_mapped_checkpoint(
    tmp_path: Path,
) -> None:
    # Mapping the checkpoint to CUDA must not pass a CUDA tensor to
    # torch.cuda.Generator.set_state(), which requires a CPU ByteTensor.
    initial_config = replace(_config(tmp_path), device="cuda")
    FPOTrainer(initial_config, adapter_factory=lambda **_: _Adapter()).train()
    resumed_config = replace(
        initial_config,
        run_directory=tmp_path / "fpo-cuda-resumed",
        resume_checkpoint=initial_config.run_directory / "checkpoints" / "step-4.pt",
    )

    summary = FPOTrainer(
        resumed_config,
        adapter_factory=lambda **_: _Adapter(),
    ).train()

    assert summary.environment_steps == 8
    assert summary.optimizer_updates == 2
