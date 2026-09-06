from __future__ import annotations

import importlib.util
import json
import sys
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import torch

from flow_rl.envs.types import EnvStep


def _load_script():
    script = (
        Path(__file__).resolve().parents[2]
        / "scripts"
        / "dino_v2_single_update_smoke.py"
    )
    spec = importlib.util.spec_from_file_location(
        "dino_v2_single_update_smoke", script
    )
    assert spec is not None and spec.loader is not None
    module = importlib.util.module_from_spec(spec)
    sys.modules[spec.name] = module
    spec.loader.exec_module(module)
    return module


def test_cli_records_raw_result_and_exit_artifacts(tmp_path, capsys) -> None:
    module = _load_script()
    config = tmp_path / "ppo.yaml"
    config.write_text("placeholder: true\n", encoding="utf-8")
    run_directory = tmp_path / "run"

    def fake_runner(request):
        assert request.algorithm == "ppo"
        assert request.config == config.resolve()
        assert request.run_directory == run_directory.resolve()
        assert request.worker_base == 200
        assert request.seed == 17
        assert request.time_scale == 20.0
        assert request.timeout == 120
        assert request.target == 4096
        return {"status": "PASS", "optimizer_updates": 1}

    exit_code = module.main(
        [
            "--algorithm",
            "ppo",
            "--config",
            str(config),
            "--run-dir",
            str(run_directory),
            "--worker-base",
            "200",
            "--seed",
            "17",
            "--time-scale",
            "20",
            "--timeout",
            "120",
            "--target",
            "4096",
        ],
        runner=fake_runner,
    )

    assert exit_code == 0
    assert json.loads(capsys.readouterr().out) == {
        "optimizer_updates": 1,
        "status": "PASS",
    }
    assert json.loads((run_directory / "result.json").read_text(encoding="utf-8")) == {
        "optimizer_updates": 1,
        "status": "PASS",
    }
    assert json.loads((run_directory / "exit.json").read_text(encoding="utf-8")) == {
        "exit_code": 0,
        "status": "PASS",
    }


def test_zero_decision_tick_submits_empty_actions_without_calling_policy() -> None:
    module = _load_script()
    shapes = ((5,), (5, 8), (6, 5), (11, 7), (8, 6), (10, 7))
    step = EnvStep(
        agent_ids=np.empty(0, dtype=np.int64),
        observations=tuple(
            np.empty((0, *shape), dtype=np.float32) for shape in shapes
        ),
        rewards=np.empty(0, dtype=np.float32),
        terminated=np.empty(0, dtype=bool),
        truncated=np.empty(0, dtype=bool),
    )

    class EmptyAdapter:
        pending_agent_ids = np.empty(0, dtype=np.int64)
        continuous_action_size = 4

    class PolicyMustNotRun:
        def act(self, *args, **kwargs):
            raise AssertionError("policy must not receive a zero Decision batch")

    info, diagnostics = module._decide(
        algorithm="ppo",
        environment_id=2,
        generation=0,
        step=step,
        adapter=EmptyAdapter(),
        policy=PolicyMustNotRun(),
        config=SimpleNamespace(),
        device=torch.device("cpu"),
        generator=torch.Generator(),
    )

    assert info.agent_ids.shape == (0,)
    assert info.actions.shape == (0, 4)
    assert info.values.shape == (0,)
    assert info.auxiliaries == ()
    assert diagnostics == {
        "max_action_abs": 0.0,
        "environment_times": [],
        "flow_times": [],
        "separate_time_inputs": True,
    }


def test_dual_map_probe_classifies_only_complete_initial_guard_sets() -> None:
    module = _load_script()
    shapes = ((5,), (5, 8), (6, 5), (11, 7), (8, 6), (10, 7))

    def step_with_guards(count: int) -> EnvStep:
        streams = [np.zeros((1, *shape), dtype=np.float32) for shape in shapes]
        streams[0][0, 0] = 1.0
        streams[3][0, :count, 0] = 1.0
        return EnvStep(
            agent_ids=np.array([1], dtype=np.int64),
            observations=tuple(streams),
            rewards=np.zeros(1, dtype=np.float32),
            terminated=np.zeros(1, dtype=bool),
            truncated=np.zeros(1, dtype=bool),
        )

    assert module._classify_decision_map(step_with_guards(8), 1) == "map1"
    assert module._classify_decision_map(step_with_guards(11), 1) == "map2"
    assert module._classify_decision_map(step_with_guards(7), 1) is None
    assert module._classify_decision_map(step_with_guards(8), 0) is None


def test_training_launch_args_include_map_selection_seed_inputs() -> None:
    module = _load_script()

    assert module._training_launch_args(
        base_seed=17,
        environment_id=3,
        generation=2,
    ) == (
        "--dino-training",
        "--dino-base-seed",
        "17",
        "--dino-environment-index",
        "3",
        "--dino-process-generation",
        "2",
    )
