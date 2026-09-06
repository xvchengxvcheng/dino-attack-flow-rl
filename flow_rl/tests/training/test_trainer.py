from __future__ import annotations

import csv
import json
from dataclasses import replace
from pathlib import Path

import numpy as np
import pytest
import torch

from flow_rl.envs.types import EnvStep
from flow_rl.models.policy import GaussianActorCritic
from flow_rl.tracking.checkpoint import load_checkpoint
from flow_rl.training.config import PPOTrainingConfig
from flow_rl.training.trainer import PPOTrainer


class DeterministicTrainingAdapter:
    def __init__(self, *, fail_after: int | None = None) -> None:
        self.behavior_name = "test"
        self.continuous_action_size = 1
        self._pending = np.asarray([0, 1], dtype=np.int64)
        self._tick = 0
        self._fail_after = fail_after
        self.close_count = 0
        self.actions: list[np.ndarray] = []

    @property
    def pending_agent_ids(self) -> np.ndarray:
        return self._pending.copy()

    def __enter__(self) -> "DeterministicTrainingAdapter":
        return self

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None:
        self.close_count += 1

    def reset(self) -> EnvStep:
        self._tick = 0
        return self._decision_step(reordered=False)

    def step(self, actions: np.ndarray) -> EnvStep:
        self.actions.append(actions.copy())
        self._tick += 1
        if self._fail_after is not None and self._tick >= self._fail_after:
            raise RuntimeError("injected environment failure")
        if self._tick == 3:
            self._pending = np.asarray([0, 1], dtype=np.int64)
            return EnvStep(
                agent_ids=np.asarray([0, 1, 0], dtype=np.int64),
                observations=(
                    np.asarray([[3.0], [4.0], [9.0]], dtype=np.float32),
                ),
                rewards=np.asarray([0.0, 1.0, 2.0], dtype=np.float32),
                terminated=np.asarray([False, False, True], dtype=bool),
                truncated=np.zeros(3, dtype=bool),
            )
        return self._decision_step(reordered=self._tick % 2 == 1)

    def _decision_step(self, *, reordered: bool) -> EnvStep:
        ids = [1, 0] if reordered else [0, 1]
        self._pending = np.asarray(ids, dtype=np.int64)
        observations = [float(self._tick + agent_id + 1) for agent_id in ids]
        return EnvStep(
            agent_ids=self._pending,
            observations=(
                np.asarray(observations, dtype=np.float32).reshape(2, 1),
            ),
            rewards=np.ones(2, dtype=np.float32),
            terminated=np.zeros(2, dtype=bool),
            truncated=np.zeros(2, dtype=bool),
        )


def _config(tmp_path: Path, run_name: str) -> PPOTrainingConfig:
    build = tmp_path / "3DBall.exe"
    build.write_bytes(b"test-build")
    return PPOTrainingConfig(
        build_path=build,
        run_directory=tmp_path / run_name,
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
        entropy_coefficient=0.0,
        final_entropy_coefficient=0.0,
        value_coefficient=0.5,
        max_gradient_norm=0.5,
        hidden_sizes=(4,),
        checkpoint_interval=4,
        time_scale=1.0,
        device="cpu",
        worker_id=2,
        evaluation_worker_id=3,
        seed=7,
        timeout_wait=10,
        max_agent_absence_steps=2,
        behavior_name=None,
        resume_checkpoint=None,
    )


def test_trainer_runs_real_updates_logs_episode_and_saves_reloadable_checkpoint(
    tmp_path: Path,
) -> None:
    adapter = DeterministicTrainingAdapter()
    created: list[GaussianActorCritic] = []

    def policy_factory(observation_shapes, action_size, hidden_sizes, device):
        policy = GaussianActorCritic(
            observation_shapes=observation_shapes,
            action_size=action_size,
            hidden_sizes=hidden_sizes,
        ).to(device)
        created.append(policy)
        return policy

    trainer = PPOTrainer(
        _config(tmp_path, "run"),
        adapter_factory=lambda **_: adapter,
        policy_factory=policy_factory,
    )

    summary = trainer.train()

    assert summary.environment_steps == 8
    assert summary.optimizer_updates == 2
    assert summary.episodes == 1
    assert summary.all_finite is True
    assert adapter.close_count == 1
    assert len(adapter.actions) == 4
    checkpoint = tmp_path / "run" / "checkpoints" / "final.pt"
    payload = load_checkpoint(checkpoint)
    assert payload["metadata"]["environment_steps"] == 8
    assert payload["metadata"]["optimizer_updates"] == 2
    with (tmp_path / "run" / "updates.csv").open(
        newline="", encoding="utf-8"
    ) as handle:
        assert len(list(csv.DictReader(handle))) == 2
    with (tmp_path / "run" / "summary.json").open(encoding="utf-8") as handle:
        assert json.load(handle)["environment_steps"] == 8
    assert created[0] is trainer.policy


def test_trainer_closes_adapter_and_logger_when_environment_raises(
    tmp_path: Path,
) -> None:
    adapter = DeterministicTrainingAdapter(fail_after=1)
    trainer = PPOTrainer(
        _config(tmp_path, "failed"),
        adapter_factory=lambda **_: adapter,
    )

    with pytest.raises(RuntimeError, match="injected environment failure"):
        trainer.train()

    assert adapter.close_count == 1
    assert (tmp_path / "failed" / "episodes.csv").is_file()


def test_trainer_resumes_progress_optimizer_and_normalizer_into_fresh_directory(
    tmp_path: Path,
) -> None:
    initial_config = _config(tmp_path, "initial")
    initial = PPOTrainer(
        initial_config,
        adapter_factory=lambda **_: DeterministicTrainingAdapter(),
    )
    initial.train()
    midpoint = initial_config.run_directory / "checkpoints" / "step-4.pt"
    resumed_config = replace(
        initial_config,
        run_directory=tmp_path / "resumed",
        resume_checkpoint=midpoint,
    )

    resumed_adapter = DeterministicTrainingAdapter()
    summary = PPOTrainer(
        resumed_config,
        adapter_factory=lambda **_: resumed_adapter,
    ).train()

    assert summary.environment_steps == 8
    assert summary.optimizer_updates == 2
    assert len(resumed_adapter.actions) == 2
    payload = load_checkpoint(resumed_config.run_directory / "checkpoints" / "final.pt")
    assert payload["metadata"]["environment_steps"] == 8
    assert payload["normalizer_state"]["count"] > 0


@pytest.mark.skipif(not torch.cuda.is_available(), reason="CUDA is unavailable")
def test_trainer_resumes_cuda_rng_state_from_cuda_mapped_checkpoint(
    tmp_path: Path,
) -> None:
    initial_config = replace(_config(tmp_path, "cuda-initial"), device="cuda")
    PPOTrainer(
        initial_config,
        adapter_factory=lambda **_: DeterministicTrainingAdapter(),
    ).train()
    resumed_config = replace(
        initial_config,
        run_directory=tmp_path / "cuda-resumed",
        resume_checkpoint=initial_config.run_directory / "checkpoints" / "step-4.pt",
    )

    summary = PPOTrainer(
        resumed_config,
        adapter_factory=lambda **_: DeterministicTrainingAdapter(),
    ).train()

    assert summary.environment_steps == 8
    assert summary.optimizer_updates == 2
