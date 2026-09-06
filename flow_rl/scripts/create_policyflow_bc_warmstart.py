from __future__ import annotations

import argparse
import hashlib
from pathlib import Path

from flow_rl.tracking.checkpoint import (
    load_checkpoint,
    save_checkpoint,
    validate_flow_bc_checkpoint,
    validate_policyflow_checkpoint,
)


def main() -> None:
    parser = argparse.ArgumentParser(description="Create a diagnostic PolicyFlow BC warm start")
    parser.add_argument("--bc", type=Path, required=True)
    parser.add_argument("--template", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    args = parser.parse_args()
    bc = load_checkpoint(args.bc, map_location="cpu")
    template = load_checkpoint(args.template, map_location="cpu")
    validate_flow_bc_checkpoint(bc)
    validate_policyflow_checkpoint(template)
    state = dict(template["model_state"])
    for key, value in bc["model_state"].items():
        state[f"actor.{key}"] = value
        state[f"snapshot.{key}"] = value
    metadata = dict(template["metadata"])
    metadata.update(
        {
            "environment_steps": 0,
            "unity_steps": 0,
            "optimizer_updates": 0,
            "wall_clock_seconds": 0.0,
            "initialization": "phase6_flow_bc",
            "initialization_checkpoint_sha256": _sha256(args.bc),
        }
    )
    save_checkpoint(
        args.output,
        model_state=state,
        optimizer_state=template["optimizer_state"],
        normalizer_state=bc["normalizer_state"],
        config=template["config"],
        metadata=metadata,
    )


def _sha256(path: Path) -> str:
    digest = hashlib.sha256()
    with path.open("rb") as handle:
        for chunk in iter(lambda: handle.read(1024 * 1024), b""):
            digest.update(chunk)
    return digest.hexdigest()


if __name__ == "__main__":
    main()
