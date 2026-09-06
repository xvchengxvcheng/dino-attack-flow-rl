from __future__ import annotations

import hashlib
import json
from pathlib import Path

import numpy as np
import pytest
import torch

from flow_rl.data.normalization import ObservationNormalizer
from flow_rl.envs.types import EnvStep
from flow_rl.evaluation.ppo import evaluate_ppo_checkpoint
from flow_rl.models.policy import GaussianActorCritic
from flow_rl.tracking.checkpoint import save_checkpoint


class OneStepEpisodeAdapter:
    def __init__(self) -> None:
        self.pending_agent_ids = np.asarray([0], dtype=np.int64)
        self.close_count = 0
        self.actions: list[np.ndarray] = []

    def __enter__(self) -> "OneStepEpisodeAdapter":
        return self

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None:
        self.close_count += 1

    def reset(self) -> EnvStep:
        return _step([0], [1.0], [0.0], [False])

    def step(self, actions: np.ndarray) -> EnvStep:
        self.actions.append(actions.copy())
        return _step([0, 0], [1.0, 2.0], [0.0, 5.0], [False, True])


def _step(
    agent_ids: list[int],
    observations: list[float],
    rewards: list[float],
    terminated: list[bool],
) -> EnvStep:
    count = len(agent_ids)
    return EnvStep(
        agent_ids=np.asarray(agent_ids, dtype=np.int64),
        observations=(
            np.asarray(observations, dtype=np.float32).reshape(count, 1),
        ),
        rewards=np.asarray(rewards, dtype=np.float32),
        terminated=np.asarray(terminated, dtype=bool),
        truncated=np.zeros(count, dtype=bool),
    )


def _checkpoint(tmp_path: Path) -> tuple[Path, Path, int]:
    build = tmp_path / "3DBall.exe"
    build.write_bytes(b"evaluation-build")
    policy = GaussianActorCritic(
        observation_shapes=((1,),),
        action_size=1,
        hidden_sizes=(4,),
    )
    normalizer = ObservationNormalizer(((1,),))
    normalizer.update((np.asarray([[1.0], [3.0]], dtype=np.float32),))
    checkpoint = tmp_path / "policy.pt"
    build_hash = hashlib.sha256(build.read_bytes()).hexdigest()
    save_checkpoint(
        checkpoint,
        model_state=policy.state_dict(),
        optimizer_state=None,
        normalizer_state=normalizer.state_dict(),
        config={
            "build_path": str(build),
            "hidden_sizes": [4],
            "device": "cpu",
            "worker_id": 80,
            "seed": 7,
            "behavior_name": None,
            "timeout_wait": 10,
            "time_scale": 1.0,
        },
        metadata={
            "observation_shapes": ((1,),),
            "action_size": 1,
            "build_sha256": build_hash,
        },
    )
    return checkpoint, build, normalizer.state_dict()["count"]


def test_checkpoint_evaluation_is_deterministic_frozen_and_writes_summary(
    tmp_path: Path,
) -> None:
    checkpoint, build, normalizer_count = _checkpoint(tmp_path)
    adapter = OneStepEpisodeAdapter()

    summary = evaluate_ppo_checkpoint(
        checkpoint_path=checkpoint,
        build_path=build,
        output_directory=tmp_path / "evaluation",
        episodes=3,
        training_worker_id=80,
        evaluation_worker_id=90,
        device="cpu",
        adapter_factory=lambda _: adapter,
    )

    assert summary.episodes == 3
    assert summary.mean_return == pytest.approx(5.0)
    assert summary.standard_deviation == pytest.approx(0.0)
    assert summary.minimum_return == pytest.approx(5.0)
    assert summary.maximum_return == pytest.approx(5.0)
    assert summary.environment_steps == 3
    assert summary.normalizer_count == normalizer_count
    assert adapter.close_count == 1
    assert len(adapter.actions) == 3
    np.testing.assert_array_equal(adapter.actions[0], adapter.actions[1])
    with (tmp_path / "evaluation" / "evaluation.json").open(
        encoding="utf-8"
    ) as handle:
        written = json.load(handle)
    assert written["episodes"] == 3
    assert written["mean_return"] == pytest.approx(5.0)


def test_checkpoint_evaluation_rejects_equal_workers_and_nonempty_output(
    tmp_path: Path,
) -> None:
    checkpoint, build, _ = _checkpoint(tmp_path)
    with pytest.raises(ValueError, match="differ"):
        evaluate_ppo_checkpoint(
            checkpoint_path=checkpoint,
            build_path=build,
            output_directory=tmp_path / "unused",
            episodes=1,
            training_worker_id=80,
            evaluation_worker_id=80,
            device="cpu",
            adapter_factory=lambda _: OneStepEpisodeAdapter(),
        )
    output = tmp_path / "existing"
    output.mkdir()
    (output / "evidence.txt").write_text("keep", encoding="utf-8")
    with pytest.raises(FileExistsError, match="not empty"):
        evaluate_ppo_checkpoint(
            checkpoint_path=checkpoint,
            build_path=build,
            output_directory=output,
            episodes=1,
            training_worker_id=80,
            evaluation_worker_id=90,
            device="cpu",
            adapter_factory=lambda _: OneStepEpisodeAdapter(),
        )
