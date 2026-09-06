"""Episode-safe Dino demonstration data structures."""

from __future__ import annotations

import json
import os
import tempfile
from collections import Counter
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from pathlib import Path

import numpy as np

from flow_rl.data.demonstrations import DemonstrationBatch
from flow_rl.training.on_policy import CollectedTransition
from flow_rl.training.versioned_collector import VersionedBatch


MAP_NAMES = ("map1", "map2")
_MAP_TO_ID = {name: index for index, name in enumerate(MAP_NAMES)}


def _readonly(values: np.ndarray, *, dtype: np.dtype) -> np.ndarray:
    result = np.asarray(values, dtype=dtype).copy()
    result.setflags(write=False)
    return result


@dataclass(frozen=True)
class DinoDemonstrationBatch:
    demonstrations: DemonstrationBatch
    environment_ids: np.ndarray
    process_generations: np.ndarray
    policy_versions: np.ndarray
    episode_ids: np.ndarray
    map_ids: np.ndarray
    action_valid: np.ndarray

    def __post_init__(self) -> None:
        count = len(self.demonstrations)
        for name in (
            "environment_ids",
            "process_generations",
            "policy_versions",
            "episode_ids",
            "map_ids",
        ):
            values = np.asarray(getattr(self, name))
            if values.dtype != np.int64 or values.shape != (count,):
                raise TypeError(f"{name} must be an int64 vector matching the batch")
            if np.any(values < 0):
                raise ValueError(f"{name} cannot contain negative values")
            object.__setattr__(self, name, _readonly(values, dtype=np.dtype(np.int64)))
        if np.any(self.map_ids >= len(MAP_NAMES)):
            raise ValueError("map_ids contain an unknown map")
        action_valid = np.asarray(self.action_valid)
        if action_valid.dtype != np.bool_ or action_valid.shape != (count,):
            raise TypeError("action_valid must be a bool vector matching the batch")
        object.__setattr__(
            self, "action_valid", _readonly(action_valid, dtype=np.dtype(np.bool_))
        )

    def __len__(self) -> int:
        return len(self.demonstrations)

    @property
    def agent_ids(self) -> np.ndarray:
        return self.demonstrations.agent_ids

    @property
    def observations(self) -> tuple[np.ndarray, ...]:
        return self.demonstrations.observations

    @property
    def actions(self) -> np.ndarray:
        return self.demonstrations.actions


@dataclass(frozen=True)
class _AcceptedEpisode:
    environment_id: int
    process_generation: int
    policy_version: int
    episode_id: int
    map_name: str
    trajectory: tuple[CollectedTransition[object], ...]
    action_valid: tuple[bool, ...]


class DinoSuccessfulEpisodeBuffer:
    """Accept only complete naturally terminated Dino victory trajectories."""

    def __init__(
        self,
        *,
        target_episodes_per_map: int,
        victory_terminal_reward_threshold: float = 5.0,
    ) -> None:
        if target_episodes_per_map <= 0:
            raise ValueError("target_episodes_per_map must be positive")
        self.target_episodes_per_map = int(target_episodes_per_map)
        self.victory_terminal_reward_threshold = float(
            victory_terminal_reward_threshold
        )
        self._episodes: list[_AcceptedEpisode] = []
        self._accepted: Counter[str] = Counter()
        self._failed: Counter[str] = Counter()
        self._truncated: Counter[str] = Counter()
        self._partial: Counter[str] = Counter()

    @property
    def complete(self) -> bool:
        return all(
            self._accepted[name] >= self.target_episodes_per_map
            for name in MAP_NAMES
        )

    @property
    def accepted_episodes_by_map(self) -> Mapping[str, int]:
        return {name: self._accepted[name] for name in MAP_NAMES}

    def accept(
        self,
        *,
        environment_id: int,
        process_generation: int,
        policy_version: int,
        map_name: str,
        trajectory: Sequence[CollectedTransition[object]],
        action_valid: Sequence[bool],
    ) -> bool:
        if environment_id < 0 or process_generation < 0 or policy_version < 0:
            raise ValueError(
                "environment, process generation and policy version cannot be negative"
            )
        if map_name not in _MAP_TO_ID:
            raise ValueError(f"unknown Dino map: {map_name}")
        items = tuple(trajectory)
        valid_items = tuple(bool(value) for value in action_valid)
        if len(valid_items) != len(items):
            raise ValueError("action_valid must match the episode length")
        if not items or not (items[-1].terminated or items[-1].truncated):
            self._partial[map_name] += 1
            return False
        if items[-1].truncated:
            self._truncated[map_name] += 1
            return False
        if not items[-1].terminated:
            self._partial[map_name] += 1
            return False
        if items[-1].reward < self.victory_terminal_reward_threshold:
            self._failed[map_name] += 1
            return False
        if self._accepted[map_name] >= self.target_episodes_per_map:
            return False
        observation_count = len(items[0].observation)
        action_shape = items[0].action.shape
        for item in items:
            if len(item.observation) != observation_count:
                raise ValueError("episode observation stream count changed")
            if item.action.shape != action_shape:
                raise ValueError("episode action shape changed")
            if item.truncated:
                raise ValueError("successful episode contains an earlier truncation")
        episode_id = len(self._episodes)
        self._episodes.append(
            _AcceptedEpisode(
                environment_id=environment_id,
                process_generation=process_generation,
                policy_version=policy_version,
                episode_id=episode_id,
                map_name=map_name,
                trajectory=items,
                action_valid=valid_items,
            )
        )
        self._accepted[map_name] += 1
        return True

    def build(self) -> tuple[DinoDemonstrationBatch, dict[str, object]]:
        if not self._episodes:
            raise RuntimeError("no successful Dino episodes were accepted")
        rows = [
            (episode, transition)
            for episode in self._episodes
            for transition in episode.trajectory
        ]
        observation_count = len(rows[0][1].observation)
        demonstrations = DemonstrationBatch(
            agent_ids=np.asarray(
                [transition.agent_id for _, transition in rows], dtype=np.int64
            ),
            observations=tuple(
                np.stack(
                    [transition.observation[index] for _, transition in rows]
                ).astype(np.float32, copy=False)
                for index in range(observation_count)
            ),
            actions=np.stack([transition.action for _, transition in rows]).astype(
                np.float32, copy=False
            ),
            terminated=np.asarray(
                [transition.terminated for _, transition in rows], dtype=bool
            ),
            truncated=np.asarray(
                [transition.truncated for _, transition in rows], dtype=bool
            ),
        )
        batch = DinoDemonstrationBatch(
            demonstrations=demonstrations,
            environment_ids=np.asarray(
                [episode.environment_id for episode, _ in rows], dtype=np.int64
            ),
            process_generations=np.asarray(
                [episode.process_generation for episode, _ in rows], dtype=np.int64
            ),
            policy_versions=np.asarray(
                [episode.policy_version for episode, _ in rows], dtype=np.int64
            ),
            episode_ids=np.asarray(
                [episode.episode_id for episode, _ in rows], dtype=np.int64
            ),
            map_ids=np.asarray(
                [_MAP_TO_ID[episode.map_name] for episode, _ in rows], dtype=np.int64
            ),
            action_valid=np.asarray(
                [
                    episode.action_valid[index]
                    for episode in self._episodes
                    for index in range(len(episode.trajectory))
                ],
                dtype=bool,
            ),
        )
        metadata: dict[str, object] = {
            "schema_version": 2,
            "sample_count": len(batch),
            "accepted_episode_count": len(self._episodes),
            "target_episodes_per_map": self.target_episodes_per_map,
            "accepted_episodes_by_map": dict(self.accepted_episodes_by_map),
            "failed_episodes_by_map": {
                name: self._failed[name] for name in MAP_NAMES
            },
            "truncated_episodes_by_map": {
                name: self._truncated[name] for name in MAP_NAMES
            },
            "partial_episodes_by_map": {
                name: self._partial[name] for name in MAP_NAMES
            },
            "map_names": list(MAP_NAMES),
            "victory_terminal_reward_threshold": (
                self.victory_terminal_reward_threshold
            ),
            "complete": self.complete,
        }
        return batch, metadata


def save_dino_demonstrations(
    directory: Path,
    batch: DinoDemonstrationBatch,
    metadata: Mapping[str, object],
) -> None:
    """Atomically publish a structured Dino dataset and its provenance."""

    target = Path(directory)
    if target.is_dir() and any(target.iterdir()):
        raise FileExistsError(f"dataset directory is not empty: {target}")
    target.mkdir(parents=True, exist_ok=True)
    data_path = target / "demonstrations.npz"
    metadata_path = target / "metadata.json"
    if data_path.exists() or metadata_path.exists():
        raise FileExistsError(f"dataset target already exists: {target}")
    data_temporary: Path | None = None
    metadata_temporary: Path | None = None
    try:
        with tempfile.NamedTemporaryFile(
            dir=target, prefix=".dino-demonstrations.", suffix=".npz.tmp", delete=False
        ) as handle:
            data_temporary = Path(handle.name)
            base = batch.demonstrations
            np.savez_compressed(
                handle,
                agent_ids=base.agent_ids,
                actions=base.actions,
                terminated=base.terminated,
                truncated=base.truncated,
                environment_ids=batch.environment_ids,
                process_generations=batch.process_generations,
                policy_versions=batch.policy_versions,
                episode_ids=batch.episode_ids,
                map_ids=batch.map_ids,
                action_valid=batch.action_valid,
                **{
                    f"observations_{index}": observation
                    for index, observation in enumerate(base.observations)
                },
            )
        with tempfile.NamedTemporaryFile(
            mode="w", dir=target, prefix=".dino-metadata.", suffix=".json.tmp",
            encoding="utf-8", newline="\n", delete=False,
        ) as handle:
            metadata_temporary = Path(handle.name)
            json.dump(dict(metadata), handle, indent=2, sort_keys=True)
            handle.write("\n")
        os.replace(data_temporary, data_path)
        data_temporary = None
        os.replace(metadata_temporary, metadata_path)
        metadata_temporary = None
    finally:
        if data_temporary is not None:
            data_temporary.unlink(missing_ok=True)
        if metadata_temporary is not None:
            metadata_temporary.unlink(missing_ok=True)


def load_dino_demonstrations(
    directory: Path,
) -> tuple[DinoDemonstrationBatch, dict[str, object]]:
    target = Path(directory)
    with np.load(target / "demonstrations.npz", allow_pickle=False) as archive:
        observation_names = sorted(
            (name for name in archive.files if name.startswith("observations_")),
            key=lambda name: int(name.rsplit("_", 1)[1]),
        )
        base = DemonstrationBatch(
            agent_ids=archive["agent_ids"],
            observations=tuple(archive[name] for name in observation_names),
            actions=archive["actions"],
            terminated=archive["terminated"],
            truncated=archive["truncated"],
        )
        batch = DinoDemonstrationBatch(
            demonstrations=base,
            environment_ids=archive["environment_ids"],
            process_generations=archive["process_generations"],
            policy_versions=archive["policy_versions"],
            episode_ids=archive["episode_ids"],
            map_ids=archive["map_ids"],
            action_valid=archive["action_valid"],
        )
    with (target / "metadata.json").open(encoding="utf-8") as handle:
        metadata = json.load(handle)
    if not isinstance(metadata, dict):
        raise ValueError("Dino demonstration metadata must be a mapping")
    return batch, metadata


def summarize_dino_action_modes(
    batch: DinoDemonstrationBatch,
) -> dict[str, object]:
    """Summarize map/zone/choice and coarse coordinate coverage."""

    actions = batch.actions
    if actions.shape[1] != 4:
        raise ValueError("Dino action mode report requires four-dimensional actions")
    zones = np.digitize(actions[:, 0], (-0.6, -0.2, 0.2, 0.6), right=False)
    choices = np.digitize(actions[:, 3], (-0.5, 0.0, 0.5), right=False)
    coordinate_bins = np.clip(((actions[:, 1:3] + 1.0) * 2.0).astype(int), 0, 3)
    maps: dict[str, object] = {}
    for map_id, map_name in enumerate(MAP_NAMES):
        indices = np.flatnonzero(batch.map_ids == map_id)
        mode_counts: Counter[str] = Counter()
        coordinate_counts: Counter[str] = Counter()
        legal_mode_counts: Counter[str] = Counter()
        illegal_mode_counts: Counter[str] = Counter()
        legal_coordinate_counts: Counter[str] = Counter()
        wait_count = 0
        legal_deployment_count = 0
        illegal_deployment_count = 0
        for index in indices:
            choice = int(choices[index])
            if choice == 0:
                wait_count += 1
                continue
            zone = int(zones[index])
            mode_name = f"z{zone + 1}/choice{choice}"
            mode_counts[mode_name] += 1
            if batch.action_valid[index]:
                legal_deployment_count += 1
                legal_mode_counts[mode_name] += 1
            else:
                illegal_deployment_count += 1
                illegal_mode_counts[mode_name] += 1
            u_bin, v_bin = (int(item) for item in coordinate_bins[index])
            coordinate_name = f"z{zone + 1}/u{u_bin}/v{v_bin}"
            coordinate_counts[coordinate_name] += 1
            if batch.action_valid[index]:
                legal_coordinate_counts[coordinate_name] += 1
        maps[map_name] = {
            "sample_count": int(len(indices)),
            "episode_count": int(len(np.unique(batch.episode_ids[indices]))),
            "wait_count": wait_count,
            "deployment_count": int(len(indices) - wait_count),
            "legal_deployment_count": legal_deployment_count,
            "illegal_deployment_count": illegal_deployment_count,
            "zone_choice_counts": dict(sorted(mode_counts.items())),
            "legal_zone_choice_counts": dict(sorted(legal_mode_counts.items())),
            "illegal_zone_choice_counts": dict(sorted(illegal_mode_counts.items())),
            "coordinate_bin_counts": dict(sorted(coordinate_counts.items())),
            "legal_coordinate_bin_counts": dict(
                sorted(legal_coordinate_counts.items())
            ),
            "covered_zone_choice_modes": len(mode_counts),
            "covered_coordinate_bins": len(coordinate_counts),
            "covered_legal_zone_choice_modes": len(legal_mode_counts),
            "covered_legal_coordinate_bins": len(legal_coordinate_counts),
        }
    return {
        "schema_version": 1,
        "sample_count": len(batch),
        "map_names": list(MAP_NAMES),
        "coordinate_grid": "4x4 over normalized u/v in [-1,1]",
        "maps": maps,
    }


def accept_versioned_dino_trajectories(
    buffer: DinoSuccessfulEpisodeBuffer,
    batch: VersionedBatch[object],
) -> int:
    """Bridge one frozen-policy parallel batch into episode-safe BC data."""

    accepted = 0
    for target, trajectory in batch.trajectories().items():
        if not trajectory:
            continue
        first_observation = trajectory[0].observation
        if len(first_observation) < 4 or first_observation[3].shape != (11, 7):
            raise ValueError("versioned Dino trajectory has wrong Guards shape")
        initial_guard_count = int(
            np.count_nonzero(first_observation[3][:, 0] > 0.5)
        )
        if initial_guard_count == 8:
            map_name = "map1"
        elif initial_guard_count == 11:
            map_name = "map2"
        else:
            raise ValueError(
                "cannot classify Dino map from initial guard count "
                f"{initial_guard_count}"
            )
        converted: list[CollectedTransition[object]] = []
        action_valid: list[bool] = []
        for item in trajectory:
            if len(item.observation) < 4 or item.observation[3].shape != (11, 7):
                raise ValueError("versioned Dino trajectory has wrong Guards shape")
            converted.append(
                CollectedTransition(
                    agent_id=item.identity.agent_id,
                    observation=item.observation,
                    action=item.action,
                    reward=item.reward,
                    value=item.value,
                    next_value=item.next_value,
                    terminated=item.terminated,
                    truncated=item.truncated,
                    auxiliary=item.auxiliary,
                )
            )
            if not item.next_observation or item.next_observation[0].shape != (5,):
                raise ValueError("versioned Dino trajectory has wrong Global shape")
            action_valid.append(bool(item.next_observation[0][2] > 0.5))
        if buffer.accept(
            environment_id=target.identity.environment_id,
            process_generation=target.identity.process_generation,
            policy_version=batch.policy_version,
            map_name=map_name,
            trajectory=converted,
            action_valid=action_valid,
        ):
            accepted += 1
    return accepted
