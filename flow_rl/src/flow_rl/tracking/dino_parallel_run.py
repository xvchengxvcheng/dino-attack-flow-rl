from __future__ import annotations

import csv
import math
from collections.abc import Iterable
from numbers import Integral
from pathlib import Path
from typing import TYPE_CHECKING

from torch.utils.tensorboard import SummaryWriter

if TYPE_CHECKING:
    from flow_rl.training.dino_parallel_ppo import (
        DinoParallelEpisodeSummary,
        DinoParallelUpdateSummary,
    )


_EPISODE_FIELDS = (
    "environment_steps",
    "update_index",
    "environment_id",
    "process_generation",
    "map_name",
    "agent_id",
    "episode_index",
    "episode_return",
    "episode_length",
    "terminated",
    "truncated",
    "terminal_reward",
    "success",
)


class DinoParallelRunReporter:
    """Persist every structured PPO update and completed episode immediately."""

    def __init__(
        self,
        run_directory: Path,
        *,
        environment_ids: Iterable[int],
        map_names: Iterable[str],
    ) -> None:
        self.run_directory = Path(run_directory)
        self.run_directory.mkdir(parents=True, exist_ok=True)
        self._environment_ids = tuple(int(item) for item in environment_ids)
        self._map_names = tuple(str(item) for item in map_names)
        if not self._environment_ids or len(set(self._environment_ids)) != len(
            self._environment_ids
        ):
            raise ValueError("environment_ids must be non-empty and unique")
        if not self._map_names or len(set(self._map_names)) != len(self._map_names):
            raise ValueError("map_names must be non-empty and unique")
        targets = (
            self.run_directory / "updates.csv",
            self.run_directory / "episodes.csv",
            self.run_directory / "tensorboard",
        )
        if any(target.exists() for target in targets):
            raise FileExistsError("Dino parallel run logs already exist")

        self._episode_handle = (self.run_directory / "episodes.csv").open(
            "w", encoding="utf-8", newline=""
        )
        self._episode_csv = csv.DictWriter(
            self._episode_handle, fieldnames=_EPISODE_FIELDS
        )
        self._episode_csv.writeheader()
        self._episode_handle.flush()
        self._update_handle = None
        self._update_csv = None
        self._update_metric_names: tuple[str, ...] | None = None
        self._tensorboard = SummaryWriter(
            log_dir=str(self.run_directory / "tensorboard")
        )
        self._closed = False

    def log_update(
        self,
        update: DinoParallelUpdateSummary,
        *,
        environment_steps: int,
        update_index: int,
        wall_clock_seconds: float,
        steps_per_second: float,
    ) -> None:
        self._ensure_open()
        rollout_metrics = self._rollout_metrics(update.completed_episodes)
        if environment_steps < 0 or update_index < 0:
            raise ValueError("environment_steps and update_index cannot be negative")
        for name, value in (
            ("wall_clock_seconds", wall_clock_seconds),
            ("steps_per_second", steps_per_second),
            ("response_wait_mean_seconds", update.response_wait_mean_seconds),
            ("response_wait_max_seconds", update.response_wait_max_seconds),
            ("microbatch_wait_seconds", update.microbatch_wait_seconds),
            ("optimizer_update_seconds", update.optimizer_update_seconds),
        ):
            if not math.isfinite(value) or value < 0.0:
                raise ValueError(f"{name} must be finite and nonnegative")
        if update.response_wait_count < 0:
            raise ValueError("response_wait_count must be nonnegative")

        contributions = dict(update.environment_contributions)
        if set(contributions) != set(self._environment_ids):
            raise ValueError("update worker contributions do not match this run")
        map_counters = {
            "transitions": dict(update.transitions_by_map),
            "episode_starts": dict(update.episode_starts_by_map),
            "natural_terminals": dict(update.natural_terminals_by_map),
            "truncated_terminals": dict(update.truncated_terminals_by_map),
        }
        unexpected_maps = set().union(*(set(items) for items in map_counters.values())) - set(
            self._map_names
        )
        if unexpected_maps:
            raise ValueError(f"unexpected map names: {sorted(unexpected_maps)}")

        row: dict[str, float | int] = {
            "environment_steps": environment_steps,
            "update_index": update_index,
            "policy_version": update.policy_version,
            "wall_clock_seconds": wall_clock_seconds,
            "steps_per_second": steps_per_second,
            "transition_count": update.transition_count,
            "timing/response_wait_mean_seconds": update.response_wait_mean_seconds,
            "timing/response_wait_max_seconds": update.response_wait_max_seconds,
            "timing/response_wait_count": update.response_wait_count,
            "timing/microbatch_wait_seconds": update.microbatch_wait_seconds,
            "timing/optimizer_update_seconds": update.optimizer_update_seconds,
        }
        for environment_id in self._environment_ids:
            row[f"worker/{environment_id}/transitions"] = contributions[environment_id]
        for map_name in self._map_names:
            for counter_name, values in map_counters.items():
                row[f"map/{map_name}/{counter_name}"] = values.get(map_name, 0)
        for name, value in update.metrics:
            if not math.isfinite(value):
                raise ValueError(f"metric {name!r} must be finite")
            row[f"optimization/{name}"] = value

        names = tuple(row)
        if self._update_metric_names is None:
            self._update_metric_names = names
            self._update_handle = (self.run_directory / "updates.csv").open(
                "w", encoding="utf-8", newline=""
            )
            self._update_csv = csv.DictWriter(
                self._update_handle, fieldnames=names
            )
            self._update_csv.writeheader()
        elif names != self._update_metric_names:
            raise ValueError("update metric fields changed during the run")
        assert self._update_csv is not None and self._update_handle is not None
        self._update_csv.writerow(row)
        self._update_handle.flush()

        for name, value in row.items():
            if name in {"environment_steps", "update_index"}:
                continue
            self._tensorboard.add_scalar(name, float(value), environment_steps)
        for name, value in rollout_metrics.items():
            self._tensorboard.add_scalar(name, value, environment_steps)
        for episode in update.completed_episodes:
            self._episode_csv.writerow(
                {
                    "environment_steps": environment_steps,
                    "update_index": update_index,
                    "environment_id": episode.environment_id,
                    "process_generation": episode.process_generation,
                    "map_name": episode.map_name,
                    "agent_id": episode.agent_id,
                    "episode_index": episode.episode_index,
                    "episode_return": episode.episode_return,
                    "episode_length": episode.episode_length,
                    "terminated": episode.terminated,
                    "truncated": episode.truncated,
                    "terminal_reward": episode.terminal_reward,
                    "success": episode.success,
                }
            )
            self._tensorboard.add_scalar(
                "episode/return", episode.episode_return, environment_steps
            )
            self._tensorboard.add_scalar(
                "episode/length", episode.episode_length, environment_steps
            )
            self._tensorboard.add_scalar(
                "episode/success", float(episode.success), environment_steps
            )
            self._tensorboard.add_scalar(
                f"episode/{episode.map_name}/return",
                episode.episode_return,
                environment_steps,
            )
            self._tensorboard.add_scalar(
                f"episode/{episode.map_name}/success",
                float(episode.success),
                environment_steps,
            )
        self._episode_handle.flush()
        self._tensorboard.flush()

    def _rollout_metrics(
        self, episodes: tuple[DinoParallelEpisodeSummary, ...]
    ) -> dict[str, float]:
        """Validate before writing; rates describe this rollout's natural endings.

        Episodes can span policy versions. These are online rollout statistics,
        not a frozen evaluation of the parameters after the optimizer update.
        """
        seen = set()
        for episode in episodes:
            if episode.environment_id not in self._environment_ids:
                raise ValueError("episode environment_id is not part of this run")
            if episode.map_name not in self._map_names:
                raise ValueError("episode map_name is not part of this run")
            if not math.isfinite(episode.episode_return) or not math.isfinite(
                episode.terminal_reward
            ):
                raise ValueError("episode return and terminal reward must be finite")
            if (
                isinstance(episode.episode_length, bool)
                or not isinstance(episode.episode_length, Integral)
                or episode.episode_length <= 0
            ):
                raise ValueError("episode_length must be a positive integer")
            if bool(episode.terminated) == bool(episode.truncated):
                raise ValueError("episode must be either terminated or truncated")
            if episode.success and not episode.terminated:
                raise ValueError("a truncated episode cannot be successful")
            identity = (
                episode.environment_id, episode.process_generation,
                episode.agent_id, episode.episode_index,
            )
            if identity in seen:
                raise ValueError("duplicate episode in update")
            seen.add(identity)

        groups = {"rollout": episodes}
        groups.update({
            f"rollout/{name}": tuple(e for e in episodes if e.map_name == name)
            for name in self._map_names
        })
        metrics: dict[str, float] = {}
        for prefix, group in groups.items():
            natural = tuple(e for e in group if e.terminated)
            count = len(natural)
            successes = sum(bool(e.success) for e in natural)
            metrics[f"{prefix}/natural_episodes"] = float(count)
            metrics[f"{prefix}/truncated_episodes"] = float(len(group) - count)
            metrics[f"{prefix}/successful_episodes"] = float(successes)
            if count:
                metrics[f"{prefix}/success_rate"] = successes / count
                metrics[f"{prefix}/mean_return"] = math.fsum(
                    e.episode_return / count for e in natural
                )
                metrics[f"{prefix}/mean_length"] = math.fsum(
                    e.episode_length / count for e in natural
                )
        return metrics

    def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        self._episode_handle.flush()
        self._episode_handle.close()
        if self._update_handle is not None:
            self._update_handle.flush()
            self._update_handle.close()
        self._tensorboard.flush()
        self._tensorboard.close()

    def __enter__(self) -> "DinoParallelRunReporter":
        self._ensure_open()
        return self

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None:
        self.close()

    def _ensure_open(self) -> None:
        if self._closed:
            raise RuntimeError("DinoParallelRunReporter is closed")
