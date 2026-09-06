from __future__ import annotations

import numpy as np
import pytest

from flow_rl.envs.types import EnvStep
from flow_rl.training.on_policy import DecisionBatch, OnPolicyCollectorCore


def _step(
    agent_ids: list[int],
    observations: list[float],
    *,
    rewards: list[float] | None = None,
    terminated: list[bool] | None = None,
    truncated: list[bool] | None = None,
) -> EnvStep:
    count = len(agent_ids)
    return EnvStep(
        agent_ids=np.asarray(agent_ids, dtype=np.int64),
        observations=(
            np.asarray(observations, dtype=np.float32).reshape(count, 1),
        ),
        rewards=np.asarray(rewards or [0.0] * count, dtype=np.float32),
        terminated=np.asarray(terminated or [False] * count, dtype=bool),
        truncated=np.asarray(truncated or [False] * count, dtype=bool),
    )


def _decide(observations: tuple[np.ndarray, ...]) -> DecisionBatch[str]:
    features = observations[0][:, 0]
    return DecisionBatch(
        observations=tuple(stream.copy() for stream in observations),
        actions=np.stack((features, -features), axis=1).astype(np.float32),
        values=(features * 10.0).astype(np.float32),
        auxiliaries=tuple(f"feature-{value:g}" for value in features),
    )


def _bootstrap(observations: tuple[np.ndarray, ...]) -> np.ndarray:
    return (observations[0][:, 0] * 10.0).astype(np.float32)


def test_core_keeps_opaque_auxiliary_payload_with_reordered_agent_id() -> None:
    # Index-based ownership would attach Agent 20's auxiliary record to Agent 10.
    core: OnPolicyCollectorCore[str] = OnPolicyCollectorCore()
    first = core.reset(
        _step([20, 10], [2.0, 1.0]),
        decide=_decide,
        bootstrap=_bootstrap,
    )

    second = core.step(
        _step([10, 20], [3.0, 4.0], rewards=[1.0, 2.0]),
        decide=_decide,
        bootstrap=_bootstrap,
    )

    np.testing.assert_allclose(first.actions, [[2.0, -2.0], [1.0, -1.0]])
    np.testing.assert_allclose(second.actions, [[3.0, -3.0], [4.0, -4.0]])
    assert core.trajectories()[20][0].auxiliary == "feature-2"
    assert core.trajectories()[20][0].reward == pytest.approx(2.0)
    assert core.trajectories()[20][0].next_value == pytest.approx(40.0)
    assert core.trajectories()[10][0].auxiliary == "feature-1"
    assert core.trajectories()[10][0].reward == pytest.approx(1.0)


def test_core_closes_terminal_before_same_id_new_decision() -> None:
    core: OnPolicyCollectorCore[str] = OnPolicyCollectorCore()
    core.reset(_step([2], [1.0]), decide=_decide, bootstrap=_bootstrap)

    output = core.step(
        _step(
            [2, 2],
            [7.0, 9.0],
            rewards=[0.0, 2.0],
            terminated=[False, True],
        ),
        decide=_decide,
        bootstrap=_bootstrap,
    )

    assert core.trajectories()[2][0].terminated is True
    assert core.trajectories()[2][0].auxiliary == "feature-1"
    assert core.pending_agent_ids == (2,)
    np.testing.assert_allclose(output.actions, [[7.0, -7.0]])


def test_core_uses_bootstrap_only_for_interrupted_terminal() -> None:
    core: OnPolicyCollectorCore[str] = OnPolicyCollectorCore()
    core.reset(_step([1, 2], [1.0, 2.0]), decide=_decide, bootstrap=_bootstrap)

    core.step(
        _step(
            [1, 2],
            [8.0, 9.0],
            rewards=[3.0, 4.0],
            terminated=[True, False],
            truncated=[False, True],
        ),
        decide=_decide,
        bootstrap=_bootstrap,
    )

    assert core.trajectories()[1][0].next_value == 0.0
    assert core.trajectories()[2][0].next_value == pytest.approx(90.0)
