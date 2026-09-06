from __future__ import annotations

import numpy as np

from flow_rl.data.dino_demonstrations import (
    DinoSuccessfulEpisodeBuffer,
    accept_versioned_dino_trajectories,
    load_dino_demonstrations,
    save_dino_demonstrations,
    summarize_dino_action_modes,
)
from flow_rl.training.collector import PPOAuxiliary
from flow_rl.training.on_policy import CollectedTransition
from flow_rl.training.versioned_collector import (
    AgentIdentity,
    VersionedBatch,
    VersionedTransition,
)


def _transition(
    marker: float, *, terminated=False, truncated=False, reward=0.0, action=None
):
    return CollectedTransition(
        agent_id=7,
        observation=(np.asarray([marker], dtype=np.float32),),
        action=np.asarray(
            [marker / 10] if action is None else action, dtype=np.float32
        ),
        reward=reward,
        value=0.0,
        next_value=0.0,
        terminated=terminated,
        truncated=truncated,
        auxiliary=PPOAuxiliary(0.0),
    )


def test_buffer_keeps_only_complete_natural_success_episodes_by_map() -> None:
    buffer = DinoSuccessfulEpisodeBuffer(target_episodes_per_map=1)

    assert not buffer.accept(
        environment_id=0,
        process_generation=2,
        policy_version=11,
        map_name="map1",
        trajectory=(_transition(1), _transition(2, terminated=True, reward=-1)),
        action_valid=(True, False),
    )
    assert not buffer.accept(
        environment_id=1,
        process_generation=3,
        policy_version=11,
        map_name="map2",
        trajectory=(_transition(3), _transition(4, truncated=True, reward=10)),
        action_valid=(True, True),
    )
    assert buffer.accept(
        environment_id=0,
        process_generation=2,
        policy_version=11,
        map_name="map1",
        trajectory=(_transition(5), _transition(6, terminated=True, reward=10)),
        action_valid=(True, False),
    )
    assert buffer.accept(
        environment_id=1,
        process_generation=3,
        policy_version=11,
        map_name="map2",
        trajectory=(_transition(7), _transition(8, terminated=True, reward=10)),
        action_valid=(True, True),
    )

    batch, metadata = buffer.build()
    np.testing.assert_array_equal(batch.environment_ids, [0, 0, 1, 1])
    np.testing.assert_array_equal(batch.process_generations, [2, 2, 3, 3])
    np.testing.assert_array_equal(batch.episode_ids, [0, 0, 1, 1])
    np.testing.assert_array_equal(batch.map_ids, [0, 0, 1, 1])
    np.testing.assert_array_equal(batch.action_valid, [True, False, True, True])
    np.testing.assert_array_equal(batch.policy_versions, [11, 11, 11, 11])
    np.testing.assert_allclose(batch.observations[0].reshape(-1), [5, 6, 7, 8])
    assert metadata["accepted_episodes_by_map"] == {"map1": 1, "map2": 1}
    assert metadata["failed_episodes_by_map"] == {"map1": 1, "map2": 0}
    assert metadata["truncated_episodes_by_map"] == {"map1": 0, "map2": 1}


def test_buffer_rejects_partial_and_over_target_episodes() -> None:
    buffer = DinoSuccessfulEpisodeBuffer(target_episodes_per_map=1)
    assert not buffer.accept(
        environment_id=0,
        process_generation=0,
        policy_version=0,
        map_name="map1",
        trajectory=(_transition(1),),
        action_valid=(True,),
    )
    assert buffer.accept(
        environment_id=0,
        process_generation=0,
        policy_version=0,
        map_name="map1",
        trajectory=(_transition(2, terminated=True, reward=10),),
        action_valid=(True,),
    )
    assert not buffer.accept(
        environment_id=0,
        process_generation=0,
        policy_version=0,
        map_name="map1",
        trajectory=(_transition(3, terminated=True, reward=10),),
        action_valid=(True,),
    )


def test_dino_dataset_round_trip_preserves_row_provenance_atomically(tmp_path) -> None:
    buffer = DinoSuccessfulEpisodeBuffer(target_episodes_per_map=1)
    for environment_id, generation, map_name, marker in (
        (0, 2, "map1", 1.0),
        (1, 3, "map2", 2.0),
    ):
        assert buffer.accept(
            environment_id=environment_id,
            process_generation=generation,
            policy_version=5,
            map_name=map_name,
            trajectory=(_transition(marker, terminated=True, reward=10),),
            action_valid=(True,),
        )
    batch, metadata = buffer.build()
    metadata["checkpoint_sha256"] = "a" * 64

    save_dino_demonstrations(tmp_path / "dataset", batch, metadata)
    loaded, loaded_metadata = load_dino_demonstrations(tmp_path / "dataset")

    np.testing.assert_array_equal(loaded.actions, batch.actions)
    np.testing.assert_array_equal(loaded.environment_ids, [0, 1])
    np.testing.assert_array_equal(loaded.process_generations, [2, 3])
    np.testing.assert_array_equal(loaded.episode_ids, [0, 1])
    np.testing.assert_array_equal(loaded.map_ids, [0, 1])
    np.testing.assert_array_equal(loaded.action_valid, [True, True])
    np.testing.assert_array_equal(loaded.policy_versions, [5, 5])
    assert loaded_metadata == metadata


def test_action_mode_report_counts_wait_zone_choice_and_episode_coverage() -> None:
    buffer = DinoSuccessfulEpisodeBuffer(target_episodes_per_map=1)
    map1 = (
        _transition(-1.0, action=[-0.8, -0.5, 0.5, -0.25]),
        _transition(1.0, terminated=True, reward=10, action=[0.8, 0.5, -0.5, 0.75]),
    )
    map2 = (
        _transition(0.0, terminated=True, reward=10, action=[0.0, 0.0, 0.0, 0.25]),
    )
    assert buffer.accept(
        environment_id=0,
        process_generation=0,
        policy_version=0,
        map_name="map1",
        trajectory=map1,
        action_valid=(True, False),
    )
    assert buffer.accept(
        environment_id=1,
        process_generation=0,
        policy_version=0,
        map_name="map2",
        trajectory=map2,
        action_valid=(True,),
    )
    batch, _ = buffer.build()

    report = summarize_dino_action_modes(batch)

    assert report["sample_count"] == 3
    assert report["maps"]["map1"]["sample_count"] == 2
    assert sum(report["maps"]["map1"]["zone_choice_counts"].values()) == 2
    assert report["maps"]["map1"]["legal_deployment_count"] == 1
    assert report["maps"]["map1"]["illegal_deployment_count"] == 1
    assert sum(report["maps"]["map1"]["legal_zone_choice_counts"].values()) == 1
    assert sum(report["maps"]["map1"]["illegal_zone_choice_counts"].values()) == 1
    assert report["maps"]["map1"]["covered_legal_coordinate_bins"] == 1
    assert report["maps"]["map1"]["episode_count"] == 1


def test_versioned_bridge_classifies_map_from_guard_mask_and_keeps_success() -> None:
    observations = (
        np.zeros(5, dtype=np.float32),
        np.zeros((5, 8), dtype=np.float32),
        np.zeros((6, 5), dtype=np.float32),
        np.zeros((11, 7), dtype=np.float32),
        np.zeros((8, 6), dtype=np.float32),
        np.zeros((10, 7), dtype=np.float32),
    )
    observations[3][:8, 0] = 1.0
    first = VersionedTransition(
        identity=AgentIdentity(4, 2, 9),
        trajectory_id=3,
        policy_version=0,
        observation=observations,
        action=np.zeros(4, dtype=np.float32),
        reward=0.0,
        value=0.0,
        next_observation=observations,
        next_value=0.0,
        terminated=False,
        truncated=False,
        auxiliary=PPOAuxiliary(0.0),
    )
    damaged_observations = tuple(np.array(item, copy=True) for item in observations)
    damaged_observations[3][7, 0] = 0.0
    terminal = VersionedTransition(
        identity=AgentIdentity(4, 2, 9),
        trajectory_id=3,
        policy_version=0,
        observation=damaged_observations,
        action=np.zeros(4, dtype=np.float32),
        reward=10.0,
        value=0.0,
        next_observation=damaged_observations,
        next_value=0.0,
        terminated=True,
        truncated=False,
        auxiliary=PPOAuxiliary(0.0),
    )
    batch = VersionedBatch(
        policy_version=0, transitions=(first, terminal), diagnostics=()
    )
    buffer = DinoSuccessfulEpisodeBuffer(target_episodes_per_map=1)

    accepted = accept_versioned_dino_trajectories(buffer, batch)

    assert accepted == 1
    built, metadata = buffer.build()
    np.testing.assert_array_equal(built.environment_ids, [4, 4])
    np.testing.assert_array_equal(built.process_generations, [2, 2])
    np.testing.assert_array_equal(built.map_ids, [0, 0])
    np.testing.assert_array_equal(built.action_valid, [False, False])
    np.testing.assert_array_equal(built.policy_versions, [0, 0])
    assert metadata["accepted_episodes_by_map"]["map1"] == 1
