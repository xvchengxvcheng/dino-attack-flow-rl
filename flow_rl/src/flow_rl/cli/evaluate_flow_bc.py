from __future__ import annotations

import argparse
import json
from collections.abc import Sequence
from pathlib import Path

from flow_rl.evaluation.flow_bc import evaluate_flow_bc_checkpoint


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Evaluate a Flow BC checkpoint")
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--episodes", type=int, default=100)
    parser.add_argument("--training-worker-id", type=int, default=92)
    parser.add_argument("--evaluation-worker-id", type=int, required=True)
    parser.add_argument("--evaluation-seed", type=int, required=True)
    parser.add_argument("--nfe", type=int, choices=(1, 2, 4, 8), default=4)
    parser.add_argument("--device", choices=("cpu", "cuda"), default="cuda")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    arguments = _parser().parse_args(argv)
    summary = evaluate_flow_bc_checkpoint(
        checkpoint_path=arguments.checkpoint,
        build_path=arguments.build,
        output_directory=arguments.output_dir,
        episodes=arguments.episodes,
        training_worker_id=arguments.training_worker_id,
        evaluation_worker_id=arguments.evaluation_worker_id,
        evaluation_seed=arguments.evaluation_seed,
        nfe=arguments.nfe,
        device=arguments.device,
    )
    print(json.dumps(summary.to_dict(), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
