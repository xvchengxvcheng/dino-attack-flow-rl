from __future__ import annotations

import argparse
import json
from collections.abc import Sequence
from pathlib import Path

from flow_rl.training.fpo_config import FPOTrainingConfig
from flow_rl.training.fpo_trainer import FPOTrainer


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description="Train FPO through the Unity LLAPI")
    parser.add_argument("--config", type=Path, required=True)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    arguments = _parser().parse_args(argv)
    summary = FPOTrainer(FPOTrainingConfig.from_yaml(arguments.config)).train()
    print(json.dumps(summary.to_dict(), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
