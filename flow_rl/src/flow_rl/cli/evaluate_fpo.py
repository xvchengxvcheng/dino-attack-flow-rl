from __future__ import annotations

import argparse
import json
from collections.abc import Sequence
from pathlib import Path

from flow_rl.evaluation.fpo import evaluate_fpo_checkpoint
from flow_rl.tracking.checkpoint import load_checkpoint


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Evaluate an FPO checkpoint")
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--episodes", type=int, default=100)
    parser.add_argument("--evaluation-worker-id", type=int, required=True)
    parser.add_argument("--evaluation-seed", type=int, required=True)
    parser.add_argument("--nfe", type=int, choices=(1, 2, 4, 8), default=4)
    parser.add_argument("--device", choices=("cpu", "cuda"), default="cuda")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    arguments = _parser().parse_args(argv)
    payload = load_checkpoint(arguments.checkpoint, map_location="cpu")
    summary = evaluate_fpo_checkpoint(
        checkpoint_path=arguments.checkpoint,
        build_path=arguments.build,
        output_directory=arguments.output_dir,
        episodes=arguments.episodes,
        training_worker_id=int(payload["config"]["worker_id"]),
        evaluation_worker_id=arguments.evaluation_worker_id,
        evaluation_seed=arguments.evaluation_seed,
        nfe=arguments.nfe,
        device=arguments.device,
    )
    print(json.dumps(summary.to_dict(), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
