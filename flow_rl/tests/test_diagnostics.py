from __future__ import annotations

import importlib
from pathlib import Path

import pytest


@pytest.mark.integration
def test_collect_diagnostics_reports_runtime_and_resolved_build() -> None:
    try:
        diagnose = importlib.import_module("flow_rl.cli.diagnose")
    except ModuleNotFoundError as exc:
        pytest.fail(f"diagnostic module is missing: {exc}")
    build_path = Path(__file__).parents[2] / "Builds" / "3DBall" / "3DBall.exe"

    diagnostics = diagnose.collect_diagnostics(build_path)

    assert set(diagnostics) == {
        "python",
        "python_executable",
        "numpy",
        "torch",
        "cuda_available",
        "cuda_version",
        "mlagents_envs",
        "unity_build_path",
        "unity_build_exists",
    }
    assert diagnostics["python"] == "3.10.12"
    assert diagnostics["numpy"] == "1.23.5"
    assert diagnostics["torch"] == "2.2.1+cu121"
    assert diagnostics["cuda_available"] is True
    assert diagnostics["cuda_version"] == "12.1"
    assert diagnostics["mlagents_envs"] == "1.1.0"
    assert diagnostics["unity_build_path"] == str(build_path.resolve())
    assert diagnostics["unity_build_exists"] is True
