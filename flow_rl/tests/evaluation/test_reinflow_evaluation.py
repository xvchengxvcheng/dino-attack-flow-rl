from __future__ import annotations

import hashlib
from pathlib import Path

import numpy as np
import torch

from flow_rl.data.normalization import ObservationNormalizer
from flow_rl.envs.types import EnvStep
from flow_rl.models.reinflow_policy import ReinFlowActorCritic
from flow_rl.tracking.checkpoint import FlowSolverConfig, save_checkpoint


class _Adapter:
    def __init__(self) -> None:
        self.pending_agent_ids = np.asarray([0], dtype=np.int64)
        self.actions: list[np.ndarray] = []
        self.close_count = 0

    def __enter__(self) -> "_Adapter":
        return self

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None:
        self.close_count += 1

    def reset(self) -> EnvStep:
        return _step([0], [1.0], [0.0], [False])

    def step(self, actions: np.ndarray) -> EnvStep:
        self.actions.append(actions.copy())
        return _step([0, 0], [1.0, 2.0], [0.0, 5.0], [False, True])


def _step(ids, observations, rewards, terminated) -> EnvStep:
    count = len(ids)
    return EnvStep(
        agent_ids=np.asarray(ids, dtype=np.int64),
        observations=(np.asarray(observations, dtype=np.float32).reshape(count, 1),),
        rewards=np.asarray(rewards, dtype=np.float32),
        terminated=np.asarray(terminated, dtype=bool),
        truncated=np.zeros(count, dtype=bool),
    )


def _checkpoint(tmp_path: Path) -> tuple[Path, Path]:
    build = tmp_path / "3DBall.exe"
    build.write_bytes(b"reinflow-eval-build")
    policy = ReinFlowActorCritic(
        observation_shapes=((1,),),
        action_size=1,
        state_size=4,
        time_embedding_size=4,
        velocity_hidden_sizes=(4,),
        critic_hidden_sizes=(4,),
        noise_hidden_sizes=(4,),
        nfe=2,
        min_noise_std=0.1,
        max_noise_std=0.24,
    )
    with torch.no_grad():
        for parameter in policy.actor.parameters():
            parameter.zero_()
    normalizer = ObservationNormalizer(((1,),))
    normalizer.update((np.asarray([[1.0], [3.0]], dtype=np.float32),))
    checkpoint = tmp_path / "reinflow.pt"
    save_checkpoint(
        checkpoint,
        model_state=policy.state_dict(),
        optimizer_state={"actor": {}, "critic": {}},
        normalizer_state=normalizer.state_dict(),
        config={
            "state_size": 4,
            "time_embedding_size": 4,
            "velocity_hidden_sizes": [4],
            "critic_hidden_sizes": [4],
            "noise_hidden_sizes": [4],
            "nfe": 2,
            "min_noise_std": 0.1,
            "max_noise_std": 0.24,
            "seed": 700,
            "time_scale": 1.0,
            "timeout_wait": 10,
            "behavior_name": None,
        },
        metadata={
            "schema_version": 2,
            "algorithm": "reinflow",
            "reinflow_source_commit": "source",
            "bc_checkpoint_sha256": "bc",
            "build_sha256": hashlib.sha256(build.read_bytes()).hexdigest(),
            "environment_steps": 10,
            "optimizer_updates": 1,
            "observation_shapes": [[1]],
            "action_size": 1,
            "solver": FlowSolverConfig(nfe=2, action_transform="clamp").to_dict(),
        },
    )
    return checkpoint, build


def test_reinflow_evaluation_is_seeded_bounded_and_disables_transition_noise(
    tmp_path: Path,
) -> None:
    from flow_rl.evaluation.reinflow import evaluate_reinflow_checkpoint

    checkpoint, build = _checkpoint(tmp_path)
    adapters = [_Adapter(), _Adapter()]
    summaries = [
        evaluate_reinflow_checkpoint(
            checkpoint_path=checkpoint,
            build_path=build,
            output_directory=tmp_path / f"eval-{index}",
            episodes=3,
            training_worker_id=140,
            evaluation_worker_id=150 + index,
            evaluation_seed=7100,
            device="cpu",
            adapter_factory=lambda _, adapter=adapters[index]: adapter,
        )
        for index in range(2)
    ]

    assert all(summary.mean_return == 5.0 for summary in summaries)
    assert all(summary.nfe == 2 for summary in summaries)
    assert all(summary.normalizer_count == 2 for summary in summaries)
    for left, right in zip(adapters[0].actions, adapters[1].actions):
        np.testing.assert_array_equal(left, right)
    assert all(
        np.all((-1.0 <= action) & (action <= 1.0))
        for adapter in adapters
        for action in adapter.actions
    )
