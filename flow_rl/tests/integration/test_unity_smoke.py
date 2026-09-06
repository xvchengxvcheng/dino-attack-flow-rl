from __future__ import annotations

import importlib
import json
import os
from pathlib import Path

import numpy as np
import pytest


@pytest.mark.integration
def test_random_policy_launches_steps_and_closes_3dball(tmp_path: Path) -> None:
    build_value = os.environ.get("FLOW_RL_UNITY_BUILD")
    if not build_value:
        pytest.skip("set FLOW_RL_UNITY_BUILD to run the Unity integration smoke test")
    try:
        random_policy = importlib.import_module("flow_rl.cli.random_policy")
    except ModuleNotFoundError as exc:
        pytest.fail(f"random policy runner is missing: {exc}")

    summary = random_policy.run_random_policy(
        build_path=Path(build_value),
        run_directory=tmp_path,
        requested_environment_steps=1,
        worker_id=int(os.environ.get("FLOW_RL_SMOKE_WORKER_ID", "71")),
        seed=123,
        time_scale=10.0,
    )

    assert summary["environment_steps"] >= 1
    assert summary["unity_steps"] >= 1
    assert summary["unique_agent_ids"] > 0
    assert summary["all_finite"] is True
    assert np.isfinite(summary["steps_per_second"])
    assert (tmp_path / "episodes.csv").is_file()
    assert (tmp_path / "config.yaml").is_file()
    with (tmp_path / "summary.json").open(encoding="utf-8") as handle:
        assert json.load(handle) == summary
