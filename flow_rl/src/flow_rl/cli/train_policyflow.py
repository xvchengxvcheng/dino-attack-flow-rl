from __future__ import annotations

import argparse
from pathlib import Path

from flow_rl.training.policyflow_config import PolicyFlowTrainingConfig
from flow_rl.training.policyflow_trainer import PolicyFlowTrainer


def main() -> None:
    parser = argparse.ArgumentParser(description="Train PolicyFlow through Unity LLAPI")
    parser.add_argument("--config", type=Path, required=True)
    args = parser.parse_args()
    summary = PolicyFlowTrainer(PolicyFlowTrainingConfig.from_yaml(args.config)).train()
    print(summary.to_dict())


if __name__ == "__main__":
    main()
