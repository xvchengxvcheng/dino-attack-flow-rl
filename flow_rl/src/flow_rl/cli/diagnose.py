from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path
from typing import Sequence

import mlagents_envs
import numpy as np
import torch


def collect_diagnostics(build_path: Path) -> dict[str, object]:
    """Collect runtime versions and resolve a Unity build without changing state."""
    resolved_build = build_path.expanduser().resolve()
    return {
        "python": ".".join(str(part) for part in sys.version_info[:3]),
        "python_executable": str(Path(sys.executable).resolve()),
        "numpy": np.__version__,
        "torch": torch.__version__,
        "cuda_available": torch.cuda.is_available(),
        "cuda_version": torch.version.cuda,
        "mlagents_envs": mlagents_envs.__version__,
        "unity_build_path": str(resolved_build),
        "unity_build_exists": resolved_build.is_file(),
    }


def _parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--build", type=Path, required=True)
    return parser


def main(argv: Sequence[str] | None = None) -> int:
    args = _parser().parse_args(argv)
    print(json.dumps(collect_diagnostics(args.build), indent=2, sort_keys=True))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
