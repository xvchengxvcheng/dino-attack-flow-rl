from __future__ import annotations

import importlib.util


def test_parallel_fpo_cli_module_is_available() -> None:
    assert importlib.util.find_spec("flow_rl.cli.train_dino_parallel_fpo") is not None
