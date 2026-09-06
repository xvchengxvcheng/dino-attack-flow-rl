from __future__ import annotations

import json
import os
import tempfile
from collections.abc import Mapping
from dataclasses import dataclass
from pathlib import Path
from typing import Any, Callable, Protocol

import numpy as np
import torch
from mlagents_envs.side_channel.engine_configuration_channel import (
    EngineConfigurationChannel,
)

from flow_rl.data.normalization import ObservationNormalizer
from flow_rl.envs.types import EnvStep
from flow_rl.envs.unity import UnityEnvAdapter
from flow_rl.models.policy import GaussianActorCritic
from flow_rl.tracking.checkpoint import load_checkpoint


def _readonly(array: np.ndarray) -> np.ndarray:
    owned = np.array(array, copy=True)
    owned.setflags(write=False)
    return owned


@dataclass(frozen=True)
class DemonstrationBatch:
    agent_ids: np.ndarray
    observations: tuple[np.ndarray, ...]
    actions: np.ndarray
    terminated: np.ndarray
    truncated: np.ndarray

    def __post_init__(self) -> None:
        agent_ids = np.asarray(self.agent_ids)
        if agent_ids.dtype != np.int64 or agent_ids.ndim != 1:
            raise TypeError("agent_ids must be a one-dimensional int64 array")
        batch_size = len(agent_ids)
        if not isinstance(self.observations, tuple) or not self.observations:
            raise TypeError("observations must be a non-empty tuple")
        observations: list[np.ndarray] = []
        for index, observation in enumerate(self.observations):
            array = np.asarray(observation)
            if array.dtype != np.float32:
                raise TypeError(f"observations[{index}] must have dtype float32")
            if array.ndim < 2 or array.shape[0] != batch_size:
                raise ValueError(f"observations[{index}] batch dimension is invalid")
            if not np.isfinite(array).all():
                raise ValueError(f"observations[{index}] must be finite")
            observations.append(_readonly(array))
        actions = np.asarray(self.actions)
        if actions.dtype != np.float32:
            raise TypeError("actions must have dtype float32")
        if actions.ndim != 2 or actions.shape[0] != batch_size:
            raise ValueError("actions batch dimension is invalid")
        if not np.isfinite(actions).all():
            raise ValueError("actions must be finite")
        if np.any(actions < -1.0) or np.any(actions > 1.0):
            raise ValueError("actions must be within [-1, 1]")
        flags: dict[str, np.ndarray] = {}
        for name in ("terminated", "truncated"):
            flag = np.asarray(getattr(self, name))
            if flag.dtype != np.bool_:
                raise TypeError(f"{name} must have dtype bool")
            if flag.shape != (batch_size,):
                raise ValueError(f"{name} batch dimension is invalid")
            flags[name] = flag
        if np.logical_and(flags["terminated"], flags["truncated"]).any():
            raise ValueError("a demonstration row cannot be both terminated and truncated")
        object.__setattr__(self, "agent_ids", _readonly(agent_ids))
        object.__setattr__(self, "observations", tuple(observations))
        object.__setattr__(self, "actions", _readonly(actions))
        object.__setattr__(self, "terminated", _readonly(flags["terminated"]))
        object.__setattr__(self, "truncated", _readonly(flags["truncated"]))

    def __len__(self) -> int:
        return len(self.agent_ids)


class DemonstrationAdapter(Protocol):
    continuous_action_size: int

    @property
    def pending_agent_ids(self) -> np.ndarray: ...

    def __enter__(self) -> "DemonstrationAdapter": ...

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None: ...

    def reset(self) -> EnvStep: ...

    def step(self, actions: np.ndarray) -> EnvStep: ...


@dataclass(frozen=True)
class _PendingDemonstration:
    observations: tuple[np.ndarray, ...]
    action: np.ndarray


def collect_ppo_demonstrations(
    *,
    checkpoint_path: Path,
    build_path: Path,
    output_directory: Path,
    sample_count: int,
    worker_id: int,
    dataset_seed: int,
    device: str,
    adapter_factory: Callable[..., DemonstrationAdapter] | None = None,
) -> DemonstrationBatch:
    """Collect raw observations and bounded actions from a frozen PPO checkpoint."""
    if sample_count <= 0:
        raise ValueError("sample_count must be positive")
    if worker_id < 0:
        raise ValueError("worker_id cannot be negative")
    if dataset_seed < 0:
        raise ValueError("dataset_seed cannot be negative")
    if device not in {"cpu", "cuda"}:
        raise ValueError("device must be 'cpu' or 'cuda'")
    if device == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("device cuda requires torch.cuda.is_available()")
    checkpoint = Path(checkpoint_path).resolve()
    build = Path(build_path).resolve()
    output = Path(output_directory).resolve()
    if not checkpoint.is_file():
        raise FileNotFoundError(f"checkpoint does not exist: {checkpoint}")
    if not build.is_file():
        raise FileNotFoundError(f"Unity build does not exist: {build}")
    if output.is_dir() and any(output.iterdir()):
        raise FileExistsError(f"dataset directory is not empty: {output}")

    payload = load_checkpoint(checkpoint, map_location=device)
    metadata = payload["metadata"]
    build_sha256 = _sha256(build)
    if metadata["build_sha256"] != build_sha256:
        raise ValueError("Unity build hash does not match checkpoint")
    observation_shapes = tuple(
        tuple(int(dimension) for dimension in shape)
        for shape in metadata["observation_shapes"]
    )
    action_size = int(metadata["action_size"])
    config = payload["config"]
    torch_device = torch.device(device)
    policy = GaussianActorCritic(
        observation_shapes=observation_shapes,
        action_size=action_size,
        hidden_sizes=tuple(int(size) for size in config["hidden_sizes"]),
    ).to(torch_device)
    policy.load_state_dict(payload["model_state"], strict=True)
    policy.eval()
    normalizer = ObservationNormalizer(observation_shapes)
    normalizer.load_state_dict(payload["normalizer_state"])
    normalizer_state = normalizer.state_dict()
    generator = torch.Generator(device=torch_device).manual_seed(dataset_seed)

    engine_channel = EngineConfigurationChannel()
    engine_channel.set_configuration_parameters(
        width=84,
        height=84,
        quality_level=0,
        time_scale=float(config["time_scale"]),
        target_frame_rate=-1,
        capture_frame_rate=0,
    )
    unity_log_directory = output.with_name(f"{output.name}-unity")
    if adapter_factory is None:
        selected_adapter_factory: Callable[..., DemonstrationAdapter] = (
            lambda **kwargs: UnityEnvAdapter(
                kwargs["build_path"],
                worker_id=kwargs["worker_id"],
                seed=kwargs["seed"],
                no_graphics=True,
                timeout_wait=kwargs["timeout_wait"],
                behavior_name=kwargs["behavior_name"],
                log_folder=kwargs["log_folder"],
                side_channels=kwargs["side_channels"],
            )
        )
    else:
        selected_adapter_factory = adapter_factory

    pending: dict[int, _PendingDemonstration] = {}
    completed: list[tuple[int, _PendingDemonstration, bool, bool]] = []
    with selected_adapter_factory(
        build_path=build,
        worker_id=worker_id,
        seed=dataset_seed,
        timeout_wait=int(config["timeout_wait"]),
        behavior_name=config.get("behavior_name"),
        log_folder=unity_log_directory,
        side_channels=[engine_channel],
    ) as adapter:
        step = adapter.reset()
        if adapter.continuous_action_size != action_size:
            raise ValueError("checkpoint action size does not match Unity behavior")
        while len(completed) < sample_count:
            decision_ids = adapter.pending_agent_ids
            decision_count = len(decision_ids)
            if not np.array_equal(step.agent_ids[:decision_count], decision_ids):
                raise RuntimeError("EnvStep decision order does not match adapter")
            decision_observations = tuple(
                observation[:decision_count] for observation in step.observations
            )
            normalized = normalizer.normalize(decision_observations)
            tensor_observations = tuple(
                torch.as_tensor(observation, device=torch_device)
                for observation in normalized
            )
            with torch.inference_mode():
                actions = policy.act(
                    tensor_observations,
                    deterministic=False,
                    generator=generator,
                ).actions.cpu().numpy().astype(np.float32, copy=True)
            for row, agent_id_value in enumerate(decision_ids):
                agent_id = int(agent_id_value)
                if agent_id in pending:
                    raise RuntimeError(
                        f"Agent {agent_id} requested a new action before its prior outcome"
                    )
                pending[agent_id] = _PendingDemonstration(
                    observations=tuple(
                        np.array(observation[row], dtype=np.float32, copy=True)
                        for observation in decision_observations
                    ),
                    action=np.array(actions[row], dtype=np.float32, copy=True),
                )
            step = adapter.step(actions)
            returned_decision_count = len(adapter.pending_agent_ids)
            event_indices = tuple(range(returned_decision_count, len(step.agent_ids))) + tuple(
                range(returned_decision_count)
            )
            for row in event_indices:
                agent_id = int(step.agent_ids[row])
                submitted = pending.pop(agent_id, None)
                if submitted is None:
                    if step.terminated[row] or step.truncated[row]:
                        raise RuntimeError(
                            f"Agent {agent_id} terminated without a submitted action"
                        )
                    continue
                completed.append(
                    (
                        agent_id,
                        submitted,
                        bool(step.terminated[row]),
                        bool(step.truncated[row]),
                    )
                )

    selected = completed[:sample_count]
    batch = DemonstrationBatch(
        agent_ids=np.asarray([row[0] for row in selected], dtype=np.int64),
        observations=tuple(
            np.stack([row[1].observations[index] for row in selected]).astype(
                np.float32, copy=False
            )
            for index in range(len(observation_shapes))
        ),
        actions=np.stack([row[1].action for row in selected]).astype(
            np.float32, copy=False
        ),
        terminated=np.asarray([row[2] for row in selected], dtype=bool),
        truncated=np.asarray([row[3] for row in selected], dtype=bool),
    )
    dataset_metadata = {
        "schema_version": 1,
        "sample_count": len(batch),
        "dataset_seed": dataset_seed,
        "worker_id": worker_id,
        "checkpoint_path": str(checkpoint),
        "checkpoint_sha256": _sha256(checkpoint),
        "build_path": str(build),
        "build_sha256": build_sha256,
        "observation_shapes": [list(shape) for shape in observation_shapes],
        "action_size": action_size,
        "actions_are_bounded": True,
        "observations_are_raw": True,
        "observation_normalization": {
            "count": int(normalizer_state["count"]),
            "epsilon": float(normalizer_state["epsilon"]),
            "clip": float(normalizer_state["clip"]),
            "mean": [array.tolist() for array in normalizer_state["mean"]],
            "variance": [array.tolist() for array in normalizer_state["variance"]],
        },
        "unity_log_directory": str(unity_log_directory),
    }
    save_demonstrations(output, batch, dataset_metadata)
    return batch


def save_demonstrations(
    directory: Path,
    batch: DemonstrationBatch,
    metadata: Mapping[str, Any],
) -> None:
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
            dir=target,
            prefix=".demonstrations.",
            suffix=".npz.tmp",
            delete=False,
        ) as handle:
            data_temporary = Path(handle.name)
            arrays = {
                "agent_ids": batch.agent_ids,
                "actions": batch.actions,
                "terminated": batch.terminated,
                "truncated": batch.truncated,
                **{
                    f"observations_{index}": observation
                    for index, observation in enumerate(batch.observations)
                },
            }
            np.savez_compressed(handle, **arrays)
        with tempfile.NamedTemporaryFile(
            mode="w",
            dir=target,
            prefix=".metadata.",
            suffix=".json.tmp",
            encoding="utf-8",
            newline="\n",
            delete=False,
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


def load_demonstrations(
    directory: Path,
) -> tuple[DemonstrationBatch, dict[str, Any]]:
    target = Path(directory)
    with np.load(target / "demonstrations.npz", allow_pickle=False) as archive:
        observation_names = sorted(
            (name for name in archive.files if name.startswith("observations_")),
            key=lambda name: int(name.rsplit("_", 1)[1]),
        )
        batch = DemonstrationBatch(
            agent_ids=archive["agent_ids"],
            observations=tuple(archive[name] for name in observation_names),
            actions=archive["actions"],
            terminated=archive["terminated"],
            truncated=archive["truncated"],
        )
    with (target / "metadata.json").open(encoding="utf-8") as handle:
        metadata = json.load(handle)
    if not isinstance(metadata, dict):
        raise ValueError("demonstration metadata must be a mapping")
    return batch, metadata


def _sha256(path: Path) -> str:
    import hashlib

    digest = hashlib.sha256()
    with Path(path).open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()
