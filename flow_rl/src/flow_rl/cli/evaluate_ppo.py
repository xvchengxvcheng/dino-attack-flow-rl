from __future__ import annotations

import argparse
import json
from collections.abc import Sequence
from pathlib import Path

from flow_rl.evaluation.ppo import evaluate_ppo_checkpoint
from flow_rl.tracking.checkpoint import load_checkpoint


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--episodes", type=int, default=100)
    parser.add_argument("--evaluation-worker-id", type=int, required=True)
    parser.add_argument("--device", choices=("cpu", "cuda"), default="cuda")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    payload = load_checkpoint(args.checkpoint, map_location="cpu")
    training_worker_id = int(payload["config"]["worker_id"])
    summary = evaluate_ppo_checkpoint(
        checkpoint_path=args.checkpoint,
        build_path=args.build,
        output_directory=args.output_dir,
        episodes=args.episodes,
        training_worker_id=training_worker_id,
        evaluation_worker_id=args.evaluation_worker_id,
        device=args.device,
    )
    print(json.dumps(summary.to_dict(), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
