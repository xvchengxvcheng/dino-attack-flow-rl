from __future__ import annotations

import argparse
import json
import time
from pathlib import Path
from typing import Sequence

import numpy as np
from mlagents_envs.side_channel.engine_configuration_channel import (
    EngineConfigurationChannel,
)

from flow_rl.envs.unity import UnityEnvAdapter
from flow_rl.tracking.agent_liveness import AgentLivenessTracker
from flow_rl.tracking.episodes import EpisodeTracker
from flow_rl.tracking.run import RunLogger


def sample_uniform_actions(
    generator: np.random.Generator, *, agent_count: int, action_size: int
) -> np.ndarray:
    if agent_count < 0 or action_size <= 0:
        raise ValueError("agent_count must be nonnegative and action_size positive")
    return generator.uniform(
        low=-1.0,
        high=1.0,
        size=(agent_count, action_size),
    ).astype(np.float32)


def run_random_policy(
    *,
    build_path: Path,
    run_directory: Path,
    requested_environment_steps: int,
    worker_id: int,
    seed: int,
    time_scale: float = 20.0,
    behavior_name: str | None = None,
    timeout_wait: int = 60,
    strict_agent_set: bool = True,
    max_agent_absence_steps: int = 10,
) -> dict[str, object]:
    if requested_environment_steps <= 0:
        raise ValueError("requested_environment_steps must be positive")
    if worker_id < 0:
        raise ValueError("worker_id cannot be negative")
    if time_scale <= 0.0:
        raise ValueError("time_scale must be positive")
    if max_agent_absence_steps < 0:
        raise ValueError("max_agent_absence_steps cannot be negative")
    resolved_build = Path(build_path).resolve()
    if not resolved_build.is_file():
        raise FileNotFoundError(f"Unity build does not exist: {resolved_build}")
    run_directory = Path(run_directory).resolve()
    unity_log_directory = run_directory / "unity"
    config = {
        "build_path": str(resolved_build),
        "requested_environment_steps": requested_environment_steps,
        "worker_id": worker_id,
        "seed": seed,
        "time_scale": time_scale,
        "behavior_name": behavior_name,
        "timeout_wait": timeout_wait,
        "strict_agent_set": strict_agent_set,
        "max_agent_absence_steps": max_agent_absence_steps,
    }
    engine_channel = EngineConfigurationChannel()
    engine_channel.set_configuration_parameters(
        width=84,
        height=84,
        quality_level=0,
        time_scale=time_scale,
        target_frame_rate=-1,
        capture_frame_rate=0,
    )
    generator = np.random.default_rng(seed)
    tracker = EpisodeTracker()
    environment_steps = 0
    unity_steps = 0
    idle_steps = 0
    episode_count = 0
    terminated_episodes = 0
    truncated_episodes = 0
    unique_agent_ids: set[int] = set()
    started = time.perf_counter()
    selected_behavior = ""
    action_size = 0

    with RunLogger(run_directory, config) as logger:
        unity_log_directory.mkdir(parents=True, exist_ok=False)
        with UnityEnvAdapter(
            resolved_build,
            worker_id=worker_id,
            seed=seed,
            no_graphics=True,
            timeout_wait=timeout_wait,
            behavior_name=behavior_name,
            log_folder=unity_log_directory,
            side_channels=[engine_channel],
        ) as adapter:
            current_step = adapter.reset()
            initial_agent_ids = {int(agent_id) for agent_id in adapter.pending_agent_ids}
            if not initial_agent_ids:
                raise RuntimeError("Unity reset produced no Agents requesting decisions")
            selected_behavior = adapter.behavior_name
            action_size = adapter.continuous_action_size
            unique_agent_ids.update(initial_agent_ids)
            liveness = AgentLivenessTracker(
                initial_agent_ids,
                max_absence_steps=max_agent_absence_steps,
                allow_new_agent_ids=not strict_agent_set,
            )
            final_drain_agent_ids: set[int] = set()
            final_drain_start_unity_step: int | None = None
            while (
                environment_steps < requested_environment_steps
                or final_drain_agent_ids != set(liveness.known_agent_ids)
            ):
                pending_count = len(adapter.pending_agent_ids)
                actions = sample_uniform_actions(
                    generator,
                    agent_count=pending_count,
                    action_size=action_size,
                )
                current_step = adapter.step(actions)
                environment_steps += pending_count
                unity_steps += 1
                idle_steps = idle_steps + 1 if pending_count == 0 else 0
                if idle_steps > 1_000:
                    raise RuntimeError("Unity produced no Agent decisions for 1,000 steps")

                event_ids = [int(agent_id) for agent_id in current_step.agent_ids]
                event_id_set = set(event_ids)
                unique_agent_ids.update(event_id_set)
                liveness.observe(current_step, unity_step=unity_steps)
                if environment_steps >= requested_environment_steps:
                    if final_drain_start_unity_step is None:
                        final_drain_start_unity_step = unity_steps
                    final_drain_agent_ids.update(event_id_set)
                for summary in tracker.record(current_step):
                    episode_count += 1
                    terminated_episodes += int(summary.terminated)
                    truncated_episodes += int(summary.truncated)
                    logger.log_episode(summary, environment_steps)
            liveness.finalize(observed_agent_ids=final_drain_agent_ids)

        elapsed = time.perf_counter() - started
        steps_per_second = environment_steps / elapsed
        logger.log_metrics(
            {
                "runtime/steps_per_second": steps_per_second,
                "runtime/unity_steps": float(unity_steps),
            },
            environment_steps,
        )

    summary = {
        "requested_environment_steps": requested_environment_steps,
        "environment_steps": environment_steps,
        "unity_steps": unity_steps,
        "episodes": episode_count,
        "terminated_episodes": terminated_episodes,
        "truncated_episodes": truncated_episodes,
        "unique_agent_ids": len(unique_agent_ids),
        "initial_agent_ids": sorted(initial_agent_ids),
        "behavior_name": selected_behavior,
        "continuous_action_size": action_size,
        "worker_id": worker_id,
        "seed": seed,
        "wall_clock_seconds": elapsed,
        "steps_per_second": steps_per_second,
        "all_finite": bool(
            np.isfinite(elapsed) and np.isfinite(steps_per_second)
        ),
        "strict_agent_set": strict_agent_set,
        "max_agent_absence_steps": max_agent_absence_steps,
        "maximum_agent_absence_steps_observed": (
            liveness.maximum_absence_steps_observed
        ),
        "final_drain_agent_ids": sorted(final_drain_agent_ids),
        "final_drain_unity_steps": (
            unity_steps - final_drain_start_unity_step + 1
            if final_drain_start_unity_step is not None
            else 0
        ),
        "build_path": str(resolved_build),
    }
    with (run_directory / "summary.json").open(
        "w", encoding="utf-8", newline="\n"
    ) as handle:
        json.dump(summary, handle, indent=2, sort_keys=True)
        handle.write("\n")
    return summary


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--run-dir", type=Path, required=True)
    parser.add_argument("--environment-steps", type=int, required=True)
    parser.add_argument("--worker-id", type=int, required=True)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--time-scale", type=float, default=20.0)
    parser.add_argument("--behavior-name")
    parser.add_argument("--timeout-wait", type=int, default=60)
    parser.add_argument("--max-agent-absence-steps", type=int, default=10)
    parser.add_argument(
        "--allow-agent-set-changes",
        action="store_true",
        help="Disable the strict fixed-Agent-set assertion used by the 3DBall check.",
    )
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    summary = run_random_policy(
        build_path=args.build,
        run_directory=args.run_dir,
        requested_environment_steps=args.environment_steps,
        worker_id=args.worker_id,
        seed=args.seed,
        time_scale=args.time_scale,
        behavior_name=args.behavior_name,
        timeout_wait=args.timeout_wait,
        strict_agent_set=not args.allow_agent_set_changes,
        max_agent_absence_steps=args.max_agent_absence_steps,
    )
    print(json.dumps(summary, indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
