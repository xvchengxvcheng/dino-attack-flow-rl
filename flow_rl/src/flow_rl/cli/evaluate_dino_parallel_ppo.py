from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Sequence

from flow_rl.evaluation.dino_parallel_ppo import evaluate_dino_parallel_ppo


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="Evaluate a structured Dino PPO checkpoint on both maps."
    )
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--episodes-per-map", type=int, default=20)
    parser.add_argument("--training-worker-base", type=int, default=350)
    parser.add_argument("--evaluation-worker-id", type=int, default=370)
    parser.add_argument("--evaluation-seed", type=int, default=101)
    parser.add_argument(
        "--action-mode",
        choices=("deterministic", "stochastic"),
        default="deterministic",
    )
    parser.add_argument(
        "--policy-seed",
        type=int,
        help="Required seed for reproducible stochastic policy sampling.",
    )
    parser.add_argument("--environment-index", type=int, default=0)
    parser.add_argument(
        "--python-response-delay-seconds",
        type=float,
        default=0.0,
        help=(
            "Diagnostic delay inserted before each action response is sent to Unity."
        ),
    )
    parser.add_argument("--device", choices=("cpu", "cuda"), default="cuda")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    summary = evaluate_dino_parallel_ppo(
        checkpoint_path=args.checkpoint,
        build_path=args.build,
        output_directory=args.output,
        episodes_per_map=args.episodes_per_map,
        training_worker_ids=tuple(
            args.training_worker_base + index for index in range(4)
        ),
        evaluation_worker_id=args.evaluation_worker_id,
        evaluation_seed=args.evaluation_seed,
        action_mode=args.action_mode,
        policy_seed=args.policy_seed,
        environment_index=args.environment_index,
        python_response_delay_seconds=args.python_response_delay_seconds,
        device=args.device,
    )
    print(json.dumps(summary.to_dict(), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
