from __future__ import annotations

import importlib

import numpy as np
import pytest

from flow_rl.envs.types import EnvStep


def _runner_module():
    try:
        return importlib.import_module("flow_rl.evaluation.runner")
    except ModuleNotFoundError as exc:
        pytest.fail(f"evaluation runner module is missing: {exc}")


def _env_step(
    agent_ids: list[int],
    rewards: list[float],
    terminated: list[bool] | None = None,
) -> EnvStep:
    count = len(agent_ids)
    return EnvStep(
        agent_ids=np.asarray(agent_ids, dtype=np.int64),
        observations=(np.asarray(agent_ids, dtype=np.float32).reshape(count, 1),),
        rewards=np.asarray(rewards, dtype=np.float32),
        terminated=np.asarray(terminated or [False] * count, dtype=bool),
        truncated=np.zeros(count, dtype=bool),
    )


class EvaluationAdapter:
    def __init__(self) -> None:
        self._steps = [
            _env_step([2, 1], [0.0, 0.0]),
            _env_step([1, 2], [1.0, 2.0]),
            _env_step([1, 2], [3.0, 4.0], [True, True]),
        ]
        self._index = 0
        self.pending_agent_ids = np.asarray([2, 1], dtype=np.int64)
        self.closed = False
        self.actions: list[np.ndarray] = []

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, traceback) -> None:
        self.closed = True

    def reset(self) -> EnvStep:
        return self._steps[0]

    def step(self, actions: np.ndarray) -> EnvStep:
        self.actions.append(actions.copy())
        self._index += 1
        step = self._steps[self._index]
        terminal_count = int(step.terminated.sum() + step.truncated.sum())
        decision_count = len(step.agent_ids) - terminal_count
        self.pending_agent_ids = step.agent_ids[:decision_count]
        return step


def test_evaluator_uses_isolated_worker_and_preserves_policy_agent_order() -> None:
    runner = _runner_module()
    adapter = EvaluationAdapter()
    factory_calls: list[int] = []
    policy_calls: list[list[int]] = []

    def factory(worker_id: int) -> EvaluationAdapter:
        factory_calls.append(worker_id)
        return adapter

    def policy(observations, agent_ids):
        policy_calls.append(agent_ids.tolist())
        return np.column_stack((agent_ids, -agent_ids)).astype(np.float32) / 10.0

    result = runner.evaluate_policy(
        adapter_factory=factory,
        policy=policy,
        episodes=2,
        training_worker_id=4,
        evaluation_worker_id=9,
        max_environment_steps=10,
    )

    assert factory_calls == [9]
    assert policy_calls == [[2, 1], [1, 2]]
    assert result.environment_steps == 4
    assert [summary.episode_return for summary in result.episodes] == [4.0, 6.0]
    assert adapter.closed is True


def test_evaluator_rejects_training_worker_reuse() -> None:
    runner = _runner_module()
    with pytest.raises(ValueError, match="worker IDs must differ"):
        runner.evaluate_policy(
            adapter_factory=lambda _: EvaluationAdapter(),
            policy=lambda observations, ids: np.zeros((len(ids), 2), dtype=np.float32),
            episodes=1,
            training_worker_id=5,
            evaluation_worker_id=5,
        )


def test_evaluator_closes_adapter_when_policy_raises() -> None:
    runner = _runner_module()
    adapter = EvaluationAdapter()

    with pytest.raises(RuntimeError, match="policy failed"):
        runner.evaluate_policy(
            adapter_factory=lambda _: adapter,
            policy=lambda observations, ids: (_ for _ in ()).throw(RuntimeError("policy failed")),
            episodes=1,
            training_worker_id=1,
            evaluation_worker_id=2,
        )

    assert adapter.closed is True
