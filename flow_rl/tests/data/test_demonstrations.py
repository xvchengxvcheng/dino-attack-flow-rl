from __future__ import annotations

from pathlib import Path
import hashlib

import numpy as np
import pytest
import torch

from flow_rl.data.normalization import ObservationNormalizer
from flow_rl.data.demonstrations import (
    DemonstrationBatch,
    collect_ppo_demonstrations,
    load_demonstrations,
    save_demonstrations,
)
from flow_rl.envs.types import EnvStep
from flow_rl.models.policy import GaussianActorCritic
from flow_rl.tracking.checkpoint import save_checkpoint


class ReorderedDatasetAdapter:
    def __init__(self) -> None:
        self.continuous_action_size = 1
        self._pending = np.asarray([10, 20], dtype=np.int64)
        self._tick = 0
        self.close_count = 0

    @property
    def pending_agent_ids(self) -> np.ndarray:
        return self._pending.copy()

    def __enter__(self) -> "ReorderedDatasetAdapter":
        return self

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None:
        self.close_count += 1

    def reset(self) -> EnvStep:
        return _env_step([10, 20], [1.0, 2.0])

    def step(self, actions: np.ndarray) -> EnvStep:
        assert actions.shape == (len(self._pending), 1)
        self._tick += 1
        if self._tick == 1:
            self._pending = np.asarray([20, 10], dtype=np.int64)
            return _env_step([20, 10], [3.0, 4.0])
        self._pending = np.asarray([10], dtype=np.int64)
        return _env_step(
            [10, 20],
            [5.0, 9.0],
            terminated=[False, True],
        )


def _env_step(
    agent_ids: list[int],
    observations: list[float],
    *,
    terminated: list[bool] | None = None,
) -> EnvStep:
    count = len(agent_ids)
    return EnvStep(
        agent_ids=np.asarray(agent_ids, dtype=np.int64),
        observations=(np.asarray(observations, dtype=np.float32).reshape(count, 1),),
        rewards=np.zeros(count, dtype=np.float32),
        terminated=np.asarray(terminated or [False] * count, dtype=bool),
        truncated=np.zeros(count, dtype=bool),
    )


def _dataset_checkpoint(tmp_path: Path) -> tuple[Path, Path]:
    build = tmp_path / "3DBall.exe"
    build.write_bytes(b"dataset-build")
    policy = GaussianActorCritic(
        observation_shapes=((1,),), action_size=1, hidden_sizes=(4,)
    )
    normalizer = ObservationNormalizer(((1,),))
    normalizer.update((np.asarray([[1.0], [2.0]], dtype=np.float32),))
    checkpoint = tmp_path / "policy.pt"
    save_checkpoint(
        checkpoint,
        model_state=policy.state_dict(),
        optimizer_state=None,
        normalizer_state=normalizer.state_dict(),
        config={
            "hidden_sizes": [4],
            "seed": 0,
            "timeout_wait": 10,
            "behavior_name": None,
            "time_scale": 1.0,
            "worker_id": 80,
        },
        metadata={
            "observation_shapes": ((1,),),
            "action_size": 1,
            "build_sha256": hashlib.sha256(build.read_bytes()).hexdigest(),
        },
    )
    return checkpoint, build


def _batch() -> DemonstrationBatch:
    return DemonstrationBatch(
        agent_ids=np.asarray([10, 20], dtype=np.int64),
        observations=(
            np.asarray([[1.0, 2.0], [3.0, 4.0]], dtype=np.float32),
            np.asarray([[[5.0]], [[6.0]]], dtype=np.float32),
        ),
        actions=np.asarray([[0.25, -0.5], [1.0, -1.0]], dtype=np.float32),
        terminated=np.asarray([False, True], dtype=bool),
        truncated=np.asarray([True, False], dtype=bool),
    )


def test_demonstration_artifacts_round_trip_exact_arrays_and_metadata(
    tmp_path: Path,
) -> None:
    metadata = {
        "checkpoint_sha256": "a" * 64,
        "build_sha256": "b" * 64,
        "sample_count": 2,
    }

    save_demonstrations(tmp_path / "dataset", _batch(), metadata)
    loaded, loaded_metadata = load_demonstrations(tmp_path / "dataset")

    np.testing.assert_array_equal(loaded.agent_ids, [10, 20])
    np.testing.assert_array_equal(loaded.observations[0], [[1.0, 2.0], [3.0, 4.0]])
    np.testing.assert_array_equal(loaded.observations[1], [[[5.0]], [[6.0]]])
    np.testing.assert_array_equal(loaded.actions, [[0.25, -0.5], [1.0, -1.0]])
    np.testing.assert_array_equal(loaded.terminated, [False, True])
    np.testing.assert_array_equal(loaded.truncated, [True, False])
    assert loaded.agent_ids.dtype == np.int64
    assert loaded.actions.dtype == np.float32
    assert loaded.agent_ids.flags.writeable is False
    assert loaded_metadata == metadata


def test_demonstration_batch_rejects_invalid_rows_values_and_flags() -> None:
    valid = _batch()
    with pytest.raises(ValueError, match="batch"):
        DemonstrationBatch(
            agent_ids=valid.agent_ids,
            observations=(valid.observations[0][:1],),
            actions=valid.actions,
            terminated=valid.terminated,
            truncated=valid.truncated,
        )
    invalid_actions = valid.actions.copy()
    invalid_actions[0, 0] = np.nan
    with pytest.raises(ValueError, match="finite"):
        DemonstrationBatch(
            agent_ids=valid.agent_ids,
            observations=valid.observations,
            actions=invalid_actions,
            terminated=valid.terminated,
            truncated=valid.truncated,
        )
    with pytest.raises(ValueError, match="both"):
        DemonstrationBatch(
            agent_ids=valid.agent_ids,
            observations=valid.observations,
            actions=valid.actions,
            terminated=np.asarray([True, True], dtype=bool),
            truncated=np.asarray([True, False], dtype=bool),
        )


def test_demonstration_save_rejects_existing_target_without_modifying_it(
    tmp_path: Path,
) -> None:
    target = tmp_path / "dataset"
    target.mkdir()
    sentinel = target / "evidence.txt"
    sentinel.write_text("keep", encoding="utf-8")

    with pytest.raises(FileExistsError, match="not empty"):
        save_demonstrations(target, _batch(), {"sample_count": 2})

    assert sentinel.read_text(encoding="utf-8") == "keep"


def test_collect_ppo_demonstrations_maps_reordered_agents_and_is_reproducible(
    tmp_path: Path,
) -> None:
    checkpoint, build = _dataset_checkpoint(tmp_path)
    adapters: list[ReorderedDatasetAdapter] = []

    def adapter_factory(**_: object) -> ReorderedDatasetAdapter:
        adapter = ReorderedDatasetAdapter()
        adapters.append(adapter)
        return adapter

    first = collect_ppo_demonstrations(
        checkpoint_path=checkpoint,
        build_path=build,
        output_directory=tmp_path / "first",
        sample_count=3,
        worker_id=91,
        dataset_seed=123,
        device="cpu",
        adapter_factory=adapter_factory,
    )
    second = collect_ppo_demonstrations(
        checkpoint_path=checkpoint,
        build_path=build,
        output_directory=tmp_path / "second",
        sample_count=3,
        worker_id=92,
        dataset_seed=123,
        device="cpu",
        adapter_factory=adapter_factory,
    )

    np.testing.assert_array_equal(first.agent_ids, [20, 10, 20])
    np.testing.assert_array_equal(first.observations[0], [[2.0], [1.0], [3.0]])
    np.testing.assert_array_equal(first.terminated, [False, False, True])
    np.testing.assert_array_equal(first.truncated, [False, False, False])
    np.testing.assert_array_equal(first.actions, second.actions)
    assert all(adapter.close_count == 1 for adapter in adapters)
    loaded, metadata = load_demonstrations(tmp_path / "first")
    np.testing.assert_array_equal(loaded.actions, first.actions)
    assert metadata["sample_count"] == 3
    assert metadata["dataset_seed"] == 123
    assert metadata["checkpoint_sha256"] == hashlib.sha256(
        checkpoint.read_bytes()
    ).hexdigest()
    assert metadata["observation_normalization"]["count"] == 2
