from __future__ import annotations

import hashlib
from pathlib import Path

import numpy as np
import torch

from flow_rl.data.normalization import ObservationNormalizer
from flow_rl.envs.types import EnvStep
from flow_rl.models.flow import ConditionalVelocityMLP
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


def _checkpoint(tmp_path: Path) -> tuple[Path, Path, int]:
    build = tmp_path / "3DBall.exe"
    build.write_bytes(b"flow-bc-build")
    model = ConditionalVelocityMLP(
        observation_shapes=((1,),),
        action_size=1,
        state_size=4,
        time_embedding_size=4,
        hidden_sizes=(4,),
    )
    with torch.no_grad():
        for parameter in model.parameters():
            parameter.zero_()
    normalizer = ObservationNormalizer(((1,),))
    normalizer.update((np.asarray([[1.0], [3.0]], dtype=np.float32),))
    checkpoint = tmp_path / "flow-bc.pt"
    save_checkpoint(
        checkpoint,
        model_state=model.state_dict(),
        optimizer_state={"state": {}},
        normalizer_state=normalizer.state_dict(),
        config={
            "state_size": 4,
            "time_embedding_size": 4,
            "velocity_hidden_sizes": [4],
            "nfe": 4,
            "seed": 600,
        },
        metadata={
            "schema_version": 2,
            "algorithm": "flow_bc",
            "dataset_sha256": "dataset",
            "metadata_sha256": "metadata",
            "build_sha256": hashlib.sha256(build.read_bytes()).hexdigest(),
            "observation_shapes": [[1]],
            "action_size": 1,
            "solver": FlowSolverConfig(nfe=4, action_transform="clamp").to_dict(),
        },
    )
    return checkpoint, build, normalizer.state_dict()["count"]


def test_flow_bc_evaluation_is_seeded_bounded_and_frozen(tmp_path: Path) -> None:
    from flow_rl.evaluation.flow_bc import evaluate_flow_bc_checkpoint

    checkpoint, build, normalizer_count = _checkpoint(tmp_path)
    adapters = [_Adapter(), _Adapter(), _Adapter()]
    summaries = [
        evaluate_flow_bc_checkpoint(
            checkpoint_path=checkpoint,
            build_path=build,
            output_directory=tmp_path / f"eval-{index}",
            episodes=3,
            training_worker_id=92,
            evaluation_worker_id=100 + index,
            evaluation_seed=900 if index < 2 else 901,
            nfe=4,
            device="cpu",
            adapter_factory=lambda _, adapter=adapters[index]: adapter,
        )
        for index in range(3)
    ]

    assert all(summary.mean_return == 5.0 for summary in summaries)
    assert all(summary.normalizer_count == normalizer_count for summary in summaries)
    assert all(adapter.close_count == 1 for adapter in adapters)
    for left, right in zip(adapters[0].actions, adapters[1].actions):
        np.testing.assert_array_equal(left, right)
    assert any(
        not np.array_equal(left, right)
        for left, right in zip(adapters[0].actions, adapters[2].actions)
    )
    assert all(
        np.all((-1.0 <= action) & (action <= 1.0))
        for adapter in adapters
        for action in adapter.actions
    )
