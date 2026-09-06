from __future__ import annotations

import hashlib
from pathlib import Path

import numpy as np
import torch

from flow_rl.data.normalization import ObservationNormalizer
from flow_rl.envs.types import EnvStep
from flow_rl.evaluation.fpo import ablate_fpo_nfe, evaluate_fpo_checkpoint
from flow_rl.models.fpo_policy import FlowActorCritic
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


def _step(
    ids: list[int],
    observations: list[float],
    rewards: list[float],
    terminated: list[bool],
) -> EnvStep:
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
    build.write_bytes(b"fpo-evaluation-build")
    policy = FlowActorCritic(
        observation_shapes=((1,),),
        action_size=1,
        state_size=4,
        time_embedding_size=4,
        velocity_hidden_sizes=(4,),
        critic_hidden_sizes=(4,),
    )
    with torch.no_grad():
        for parameter in policy.actor.parameters():
            parameter.zero_()
    normalizer = ObservationNormalizer(((1,),))
    normalizer.update((np.asarray([[1.0], [3.0]], dtype=np.float32),))
    checkpoint = tmp_path / "fpo.pt"
    save_checkpoint(
        checkpoint,
        model_state=policy.state_dict(),
        optimizer_state=None,
        normalizer_state=normalizer.state_dict(),
        config={
            "state_size": 4,
            "time_embedding_size": 4,
            "velocity_hidden_sizes": [4],
            "critic_hidden_sizes": [4],
            "nfe": 4,
            "time_scale": 1.0,
            "worker_id": 100,
            "seed": 7,
            "behavior_name": None,
            "timeout_wait": 10,
        },
        metadata={
            "schema_version": 2,
            "observation_shapes": ((1,),),
            "action_size": 1,
            "build_sha256": hashlib.sha256(build.read_bytes()).hexdigest(),
            "solver": FlowSolverConfig(nfe=4).to_dict(),
        },
    )
    return checkpoint, build


def test_fpo_evaluation_repeats_action_sequence_for_same_explicit_seed(
    tmp_path: Path,
) -> None:
    # Reusing global RNG would make two independent evaluations disagree.
    checkpoint, build = _checkpoint(tmp_path)
    first_adapter = _Adapter()
    second_adapter = _Adapter()

    first = evaluate_fpo_checkpoint(
        checkpoint_path=checkpoint,
        build_path=build,
        output_directory=tmp_path / "eval-1",
        episodes=3,
        training_worker_id=100,
        evaluation_worker_id=110,
        evaluation_seed=900,
        nfe=4,
        device="cpu",
        adapter_factory=lambda _: first_adapter,
    )
    second = evaluate_fpo_checkpoint(
        checkpoint_path=checkpoint,
        build_path=build,
        output_directory=tmp_path / "eval-2",
        episodes=3,
        training_worker_id=100,
        evaluation_worker_id=111,
        evaluation_seed=900,
        nfe=4,
        device="cpu",
        adapter_factory=lambda _: second_adapter,
    )

    assert first.mean_return == 5.0
    assert first.evaluation_seed == second.evaluation_seed == 900
    assert first.nfe == second.nfe == 4
    assert len(first_adapter.actions) == len(second_adapter.actions) == 3
    for left, right in zip(first_adapter.actions, second_adapter.actions):
        np.testing.assert_array_equal(left, right)


def test_nfe_ablation_reuses_seeded_initial_noise_and_orders_rows(tmp_path: Path) -> None:
    # With a zero velocity field, same initial noise must yield identical actions for every NFE.
    checkpoint, build = _checkpoint(tmp_path)
    adapters: list[_Adapter] = []

    def factory(_: int) -> _Adapter:
        adapter = _Adapter()
        adapters.append(adapter)
        return adapter

    results = ablate_fpo_nfe(
        checkpoint_path=checkpoint,
        build_path=build,
        output_directory=tmp_path / "ablation",
        nfes=(8, 1, 4, 2),
        episodes=2,
        training_worker_id=100,
        first_evaluation_worker_id=120,
        evaluation_seed=901,
        device="cpu",
        adapter_factory=factory,
    )

    assert [result.nfe for result in results] == [1, 2, 4, 8]
    for adapter in adapters[1:]:
        for left, right in zip(adapters[0].actions, adapter.actions):
            np.testing.assert_array_equal(left, right)
