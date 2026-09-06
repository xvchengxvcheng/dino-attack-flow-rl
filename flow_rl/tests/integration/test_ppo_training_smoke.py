from __future__ import annotations

import os
from pathlib import Path

import pytest

from flow_rl.tracking.checkpoint import load_checkpoint
from flow_rl.training.config import PPOTrainingConfig
from flow_rl.training.trainer import PPOTrainer


@pytest.mark.integration
def test_real_3dball_ppo_performs_updates_and_closes(tmp_path: Path) -> None:
    build_value = os.environ.get("FLOW_RL_UNITY_BUILD")
    if not build_value:
        pytest.skip("FLOW_RL_UNITY_BUILD is not configured")
    build = Path(build_value).resolve()
    worker_id = int(os.environ.get("FLOW_RL_PPO_SMOKE_WORKER_ID", "74"))
    config = PPOTrainingConfig(
        build_path=build,
        run_directory=tmp_path / "ppo-smoke",
        total_environment_steps=24,
        rollout_size=12,
        batch_size=4,
        epochs=1,
        gamma=0.99,
        gae_lambda=0.99,
        learning_rate=3e-4,
        final_learning_rate=3e-4,
        clip_range=0.2,
        final_clip_range=0.2,
        entropy_coefficient=1e-3,
        final_entropy_coefficient=1e-3,
        value_coefficient=0.5,
        max_gradient_norm=0.5,
        hidden_sizes=(8,),
        checkpoint_interval=24,
        time_scale=20.0,
        device="cpu",
        worker_id=worker_id,
        evaluation_worker_id=worker_id + 1,
        seed=0,
        timeout_wait=60,
        max_agent_absence_steps=10,
        behavior_name=None,
        resume_checkpoint=None,
    )

    summary = PPOTrainer(config).train()

    assert summary.environment_steps >= 24
    assert summary.optimizer_updates >= 1
    assert summary.all_finite is True
    checkpoint = load_checkpoint(Path(summary.final_checkpoint))
    assert checkpoint["metadata"]["environment_steps"] >= 24
    assert (config.run_directory / "updates.csv").is_file()
