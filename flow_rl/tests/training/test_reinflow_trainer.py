from __future__ import annotations

import csv
import hashlib
from pathlib import Path

import numpy as np
import torch
import yaml

from flow_rl.data.normalization import ObservationNormalizer
from flow_rl.envs.types import EnvStep
from flow_rl.models.flow import ConditionalVelocityMLP
from flow_rl.tracking.checkpoint import FlowSolverConfig, save_checkpoint


class _Adapter:
    behavior_name = "test"
    continuous_action_size = 2

    def __init__(self) -> None:
        self._pending = np.asarray([0, 1], dtype=np.int64)
        self._tick = 0
        self.close_count = 0
        self.actions: list[np.ndarray] = []

    @property
    def pending_agent_ids(self) -> np.ndarray:
        return self._pending.copy()

    def __enter__(self) -> "_Adapter":
        return self

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None:
        self.close_count += 1

    def reset(self) -> EnvStep:
        self._tick = 0
        return self._decisions([0, 1])

    def step(self, actions: np.ndarray) -> EnvStep:
        self.actions.append(actions.copy())
        self._tick += 1
        if self._tick == 3:
            self._pending = np.asarray([0, 1], dtype=np.int64)
            return EnvStep(
                agent_ids=np.asarray([0, 1, 0], dtype=np.int64),
                observations=(
                    np.asarray(
                        [[3.0, 0.0, 1.0], [4.0, 1.0, 0.0], [9.0, 0.0, 0.0]],
                        dtype=np.float32,
                    ),
                ),
                rewards=np.asarray([0.0, 1.0, 2.0], dtype=np.float32),
                terminated=np.asarray([False, False, True], dtype=bool),
                truncated=np.zeros(3, dtype=bool),
            )
        return self._decisions([1, 0] if self._tick % 2 else [0, 1])

    def _decisions(self, ids: list[int]) -> EnvStep:
        self._pending = np.asarray(ids, dtype=np.int64)
        observations = [
            [float(self._tick + agent_id + 1), float(agent_id), 1.0]
            for agent_id in ids
        ]
        return EnvStep(
            agent_ids=self._pending,
            observations=(np.asarray(observations, dtype=np.float32),),
            rewards=np.ones(2, dtype=np.float32),
            terminated=np.zeros(2, dtype=bool),
            truncated=np.zeros(2, dtype=bool),
        )


def _sha256(path: Path) -> str:
    return hashlib.sha256(path.read_bytes()).hexdigest()


def _config(tmp_path: Path, *, run_name: str, resume: str | None = None):
    from flow_rl.training.reinflow_config import ReinFlowTrainingConfig

    build = tmp_path / "3DBall.exe"
    if not build.exists():
        build.write_bytes(b"reinflow-trainer-build")
    bc = tmp_path / "bc.pt"
    if not bc.exists():
        actor = ConditionalVelocityMLP(
            observation_shapes=((3,),),
            action_size=2,
            state_size=4,
            time_embedding_size=4,
            hidden_sizes=(8,),
        )
        normalizer = ObservationNormalizer(((3,),))
        normalizer.update((np.zeros((5, 3), dtype=np.float32),))
        save_checkpoint(
            bc,
            model_state=actor.state_dict(),
            optimizer_state={"state": {}},
            normalizer_state=normalizer.state_dict(),
            config={
                "state_size": 4,
                "time_embedding_size": 4,
                "velocity_hidden_sizes": [8],
                "nfe": 1,
            },
            metadata={
                "schema_version": 2,
                "algorithm": "flow_bc",
                "dataset_sha256": "dataset",
                "metadata_sha256": "metadata",
                "build_sha256": _sha256(build),
                "observation_shapes": [[3]],
                "action_size": 2,
                "solver": FlowSolverConfig(nfe=1, action_transform="clamp").to_dict(),
            },
        )
    raw = {
        "bc_checkpoint_path": "bc.pt",
        "build_path": "3DBall.exe",
        "run_directory": run_name,
        "total_environment_steps": 8,
        "rollout_size": 4,
        "batch_size": 2,
        "epochs": 1,
        "gamma": 0.9,
        "gae_lambda": 0.8,
        "actor_learning_rate": 1e-3,
        "critic_learning_rate": 2e-3,
        "clip_range": 0.01,
        "entropy_coefficient": 0.03,
        "max_gradient_norm": 1.0,
        "critic_hidden_sizes": [8],
        "noise_hidden_sizes": [8],
        "nfe": 1,
        "horizon_steps": 1,
        "min_noise_std": 0.1,
        "max_noise_std": 0.24,
        "log_prob_min": -1.0,
        "log_prob_max": 1.0,
        "critic_warmup_environment_steps": 5,
        "checkpoint_interval": 4,
        "time_scale": 1.0,
        "device": "cpu",
        "worker_id": 20,
        "evaluation_worker_id": 21,
        "seed": 7,
        "timeout_wait": 10,
        "max_agent_absence_steps": 2,
        "behavior_name": None,
        "resume_checkpoint": resume,
    }
    path = tmp_path / f"{run_name}.yaml"
    path.write_text(yaml.safe_dump(raw), encoding="utf-8")
    return ReinFlowTrainingConfig.from_yaml(path)


def test_reinflow_trainer_warmup_logs_and_saves_complete_checkpoint(
    tmp_path: Path,
) -> None:
    from flow_rl.tracking.checkpoint import (
        load_checkpoint,
        validate_reinflow_checkpoint,
    )
    from flow_rl.training.reinflow_trainer import ReinFlowTrainer

    adapter = _Adapter()
    config = _config(tmp_path, run_name="run")
    summary = ReinFlowTrainer(
        config, adapter_factory=lambda **_: adapter
    ).train()

    assert summary.environment_steps == 8
    assert summary.optimizer_updates == 2
    assert adapter.close_count == 1
    payload = load_checkpoint(Path(summary.final_checkpoint))
    validate_reinflow_checkpoint(payload)
    assert payload["metadata"]["reinflow_source_commit"] == (
        "e722e151bed767f3ffef47527cf697f2358af55d"
    )
    assert payload["metadata"]["bc_checkpoint_sha256"] == config.bc_checkpoint_sha256
    assert payload["normalizer_state"]["count"] == 5
    assert set(payload["optimizer_state"]) == {"actor", "critic"}
    with (config.run_directory / "updates.csv").open(
        newline="", encoding="utf-8"
    ) as handle:
        rows = list(csv.DictReader(handle))
    assert [row["actor_updated"] for row in rows] == ["False", "True"]


def test_reinflow_trainer_resumes_optimizers_rng_and_frozen_normalizer(
    tmp_path: Path,
) -> None:
    from flow_rl.tracking.checkpoint import load_checkpoint
    from flow_rl.training.reinflow_trainer import ReinFlowTrainer

    initial = _config(tmp_path, run_name="initial")
    ReinFlowTrainer(initial, adapter_factory=lambda **_: _Adapter()).train()
    resumed = _config(
        tmp_path,
        run_name="resumed",
        resume="initial/checkpoints/step-4.pt",
    )
    adapter = _Adapter()

    summary = ReinFlowTrainer(
        resumed, adapter_factory=lambda **_: adapter
    ).train()

    assert summary.environment_steps == 8
    assert summary.optimizer_updates == 2
    assert len(adapter.actions) == 2
    payload = load_checkpoint(Path(summary.final_checkpoint))
    assert payload["normalizer_state"]["count"] == 5
    assert payload["optimizer_state"]["actor"]["state"]
    assert payload["optimizer_state"]["critic"]["state"]
