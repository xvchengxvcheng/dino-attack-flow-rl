from __future__ import annotations

import argparse
import json
from collections.abc import Sequence
from pathlib import Path

from flow_rl.evaluation.fpo import ablate_fpo_nfe
from flow_rl.tracking.checkpoint import load_checkpoint


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Run a seeded FPO NFE ablation")
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--episodes", type=int, default=100)
    parser.add_argument("--first-evaluation-worker-id", type=int, required=True)
    parser.add_argument("--evaluation-seed", type=int, required=True)
    parser.add_argument("--device", choices=("cpu", "cuda"), default="cuda")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    arguments = _parser().parse_args(argv)
    payload = load_checkpoint(arguments.checkpoint, map_location="cpu")
    results = ablate_fpo_nfe(
        checkpoint_path=arguments.checkpoint,
        build_path=arguments.build,
        output_directory=arguments.output_dir,
        nfes=(1, 2, 4, 8),
        episodes=arguments.episodes,
        training_worker_id=int(payload["config"]["worker_id"]),
        first_evaluation_worker_id=arguments.first_evaluation_worker_id,
        evaluation_seed=arguments.evaluation_seed,
        device=arguments.device,
    )
    print(json.dumps([result.to_dict() for result in results], indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
