from __future__ import annotations

import csv
from collections.abc import Mapping, Sequence
from pathlib import Path
from typing import Any

import numpy as np
import yaml
from torch.utils.tensorboard import SummaryWriter

from flow_rl.tracking.episodes import EpisodeSummary


_EPISODE_FIELDS = (
    "environment_steps",
    "agent_id",
    "episode_index",
    "episode_return",
    "episode_length",
    "terminated",
    "truncated",
)


class RunLogger:
    """Writes one consistent configuration, episode CSV, and TensorBoard stream."""

    def __init__(self, run_directory: Path, config: Mapping[str, Any]) -> None:
        self.run_directory = Path(run_directory)
        if self.run_directory.is_dir() and any(self.run_directory.iterdir()):
            raise FileExistsError(
                f"run directory is not empty: {self.run_directory}"
            )
        self.run_directory.mkdir(parents=True, exist_ok=True)
        config_path = self.run_directory / "config.yaml"
        with config_path.open("w", encoding="utf-8", newline="\n") as handle:
            yaml.safe_dump(_serializable(config), handle, sort_keys=True)
        self._csv_handle = (self.run_directory / "episodes.csv").open(
            "w", encoding="utf-8", newline=""
        )
        self._csv = csv.DictWriter(self._csv_handle, fieldnames=_EPISODE_FIELDS)
        self._csv.writeheader()
        self._csv_handle.flush()
        self._tensorboard = SummaryWriter(
            log_dir=str(self.run_directory / "tensorboard")
        )
        self._updates_handle = None
        self._updates_csv = None
        self._update_metric_names: tuple[str, ...] | None = None
        self._closed = False

    def log_episode(
        self, summary: EpisodeSummary, environment_steps: int
    ) -> None:
        self._ensure_open()
        if environment_steps < 0:
            raise ValueError("environment_steps cannot be negative")
        self._csv.writerow(
            {
                "environment_steps": environment_steps,
                "agent_id": summary.agent_id,
                "episode_index": summary.episode_index,
                "episode_return": summary.episode_return,
                "episode_length": summary.episode_length,
                "terminated": summary.terminated,
                "truncated": summary.truncated,
            }
        )
        self._csv_handle.flush()
        self._tensorboard.add_scalar(
            "episode/return", summary.episode_return, environment_steps
        )
        self._tensorboard.add_scalar(
            "episode/length", summary.episode_length, environment_steps
        )

    def log_metrics(self, metrics: Mapping[str, float], environment_steps: int) -> None:
        self._ensure_open()
        for name, value in metrics.items():
            if not np.isfinite(value):
                raise ValueError(f"metric {name!r} must be finite")
            self._tensorboard.add_scalar(name, float(value), environment_steps)

    def log_update(
        self,
        metrics: Mapping[str, float | int],
        *,
        environment_steps: int,
        update_index: int,
    ) -> None:
        self._ensure_open()
        if environment_steps < 0 or update_index < 0:
            raise ValueError("environment_steps and update_index cannot be negative")
        names = tuple(str(name) for name in metrics)
        if not names:
            raise ValueError("update metrics cannot be empty")
        if any(name in {"environment_steps", "update_index"} for name in names):
            raise ValueError("update metric names use a reserved field")
        for name, value in metrics.items():
            if not np.isfinite(value):
                raise ValueError(f"metric {name!r} must be finite")
        if self._update_metric_names is None:
            self._update_metric_names = names
            self._updates_handle = (self.run_directory / "updates.csv").open(
                "w", encoding="utf-8", newline=""
            )
            self._updates_csv = csv.DictWriter(
                self._updates_handle,
                fieldnames=("environment_steps", "update_index", *names),
            )
            self._updates_csv.writeheader()
        elif names != self._update_metric_names:
            raise ValueError("update metric fields changed during the run")
        row = {
            "environment_steps": environment_steps,
            "update_index": update_index,
            **metrics,
        }
        self._updates_csv.writerow(row)
        self._updates_handle.flush()
        for name, value in metrics.items():
            self._tensorboard.add_scalar(
                f"optimization/{name}",
                float(value),
                environment_steps,
            )

    def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        self._csv_handle.flush()
        self._csv_handle.close()
        if self._updates_handle is not None:
            self._updates_handle.flush()
            self._updates_handle.close()
        self._tensorboard.flush()
        self._tensorboard.close()

    def __enter__(self) -> "RunLogger":
        self._ensure_open()
        return self

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None:
        self.close()

    def _ensure_open(self) -> None:
        if self._closed:
            raise RuntimeError("RunLogger is closed")


def _serializable(value: Any) -> Any:
    if isinstance(value, Mapping):
        return {str(key): _serializable(item) for key, item in value.items()}
    if isinstance(value, Path):
        return str(value)
    if isinstance(value, np.generic):
        return value.item()
    if isinstance(value, Sequence) and not isinstance(value, (str, bytes, bytearray)):
        return [_serializable(item) for item in value]
    return value
