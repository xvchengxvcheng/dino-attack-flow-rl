from __future__ import annotations

import argparse
import json
from collections.abc import Sequence
from pathlib import Path

from flow_rl.training.reinflow_config import ReinFlowTrainingConfig
from flow_rl.training.reinflow_trainer import ReinFlowTrainer


def main(argv: Sequence[str] | None = None) -> int:
    parser = argparse.ArgumentParser(description="Fine-tune ReinFlow on Unity")
    parser.add_argument("--config", type=Path, required=True)
    arguments = parser.parse_args(argv)
    summary = ReinFlowTrainer(
        ReinFlowTrainingConfig.from_yaml(arguments.config)
    ).train()
    print(json.dumps(summary.to_dict(), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
