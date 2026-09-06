from __future__ import annotations

import argparse
from pathlib import Path

from flow_rl.evaluation.policyflow import evaluate_policyflow_checkpoint


def main() -> None:
    parser = argparse.ArgumentParser(description="Evaluate a PolicyFlow checkpoint")
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--episodes", type=int, default=100)
    parser.add_argument("--training-worker-id", type=int, required=True)
    parser.add_argument("--evaluation-worker-id", type=int, required=True)
    parser.add_argument("--evaluation-seed", type=int, required=True)
    parser.add_argument("--device", choices=("cpu", "cuda"), default="cuda")
    args = parser.parse_args()
    summary = evaluate_policyflow_checkpoint(
        checkpoint_path=args.checkpoint, build_path=args.build,
        output_directory=args.output, episodes=args.episodes,
        training_worker_id=args.training_worker_id,
        evaluation_worker_id=args.evaluation_worker_id,
        evaluation_seed=args.evaluation_seed, device=args.device,
    )
    print(summary.to_dict())


if __name__ == "__main__":
    main()
