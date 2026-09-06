from __future__ import annotations

import argparse
import json
from collections.abc import Sequence
from pathlib import Path

from flow_rl.data.demonstrations import collect_ppo_demonstrations


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--checkpoint", type=Path, required=True)
    parser.add_argument("--build", type=Path, required=True)
    parser.add_argument("--output-dir", type=Path, required=True)
    parser.add_argument("--sample-count", type=int, default=100_000)
    parser.add_argument("--worker-id", type=int, required=True)
    parser.add_argument("--dataset-seed", type=int, default=10_000)
    parser.add_argument("--device", choices=("cpu", "cuda"), default="cuda")
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    batch = collect_ppo_demonstrations(
        checkpoint_path=args.checkpoint,
        build_path=args.build,
        output_directory=args.output_dir,
        sample_count=args.sample_count,
        worker_id=args.worker_id,
        dataset_seed=args.dataset_seed,
        device=args.device,
    )
    print(
        json.dumps(
            {
                "output_directory": str(args.output_dir.resolve()),
                "sample_count": len(batch),
            },
            indent=2,
            sort_keys=True,
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
