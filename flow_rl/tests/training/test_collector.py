from __future__ import annotations

from dataclasses import dataclass

import numpy as np
import pytest
import torch

from flow_rl.envs.types import EnvStep
from flow_rl.models.policy import PolicyOutput
from flow_rl.training.collector import PPOCollector


class IdentityNormalizer:
    def __init__(self) -> None:
        self.update_calls: list[tuple[np.ndarray, ...]] = []

    def update(self, observations: tuple[np.ndarray, ...]) -> None:
        self.update_calls.append(tuple(array.copy() for array in observations))

    def normalize(
        self, observations: tuple[np.ndarray, ...]
    ) -> tuple[np.ndarray, ...]:
        return tuple(array.copy() for array in observations)


class LiteralCritic:
    def __call__(self, observations: tuple[torch.Tensor, ...]) -> torch.Tensor:
        return observations[0][:, 0] * 10.0


class LiteralPolicy:
    def __init__(self) -> None:
        self.critic = LiteralCritic()

    def act(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        deterministic: bool,
        generator: torch.Generator | None = None,
    ) -> PolicyOutput:
        feature = observations[0][:, 0]
        pre_tanh = (feature / 10.0).unsqueeze(1)
        return PolicyOutput(
            actions=torch.tanh(pre_tanh),
            pre_tanh=pre_tanh,
            log_probs=feature / 100.0,
            values=feature * 10.0,
            entropy=torch.zeros_like(feature),
        )


def _step(
    agent_ids: list[int],
    observations: list[float],
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


def _collector(*, gamma: float = 0.99, gae_lambda: float = 0.95) -> PPOCollector:
    return PPOCollector(
        policy=LiteralPolicy(),
        normalizer=IdentityNormalizer(),
        device=torch.device("cpu"),
        gamma=gamma,
        gae_lambda=gae_lambda,
        generator=torch.Generator().manual_seed(5),
    )


def test_reordered_decisions_keep_actions_values_and_rewards_with_agent_ids() -> None:
    collector = _collector()
    initial = collector.reset(_step([20, 10], [2.0, 1.0]))
    next_output = collector.step(
        _step([10, 20], [3.0, 4.0], rewards=[1.0, 2.0])
    )

    np.testing.assert_allclose(
        initial.actions[:, 0],
        np.tanh(np.asarray([0.2, 0.1], dtype=np.float32)),
    )
    np.testing.assert_allclose(
        next_output.actions[:, 0],
        np.tanh(np.asarray([0.3, 0.4], dtype=np.float32)),
    )
    trajectories = collector.trajectories()
    assert trajectories[20][0].reward == pytest.approx(2.0)
    assert trajectories[20][0].value == pytest.approx(20.0)
    assert trajectories[20][0].next_value == pytest.approx(40.0)
    assert trajectories[20][0].old_log_prob == pytest.approx(0.02)
    assert trajectories[10][0].reward == pytest.approx(1.0)
    assert trajectories[10][0].value == pytest.approx(10.0)
    assert trajectories[10][0].next_value == pytest.approx(30.0)
    assert trajectories[10][0].old_log_prob == pytest.approx(0.01)


def test_natural_terminal_disables_bootstrap() -> None:
    collector = _collector()
    collector.reset(_step([1], [2.0]))

    output = collector.step(
        _step([1], [9.0], rewards=[3.0], terminated=[True])
    )

    transition = collector.trajectories()[1][0]
    assert output.actions.shape == (0, 1)
    assert transition.next_value == 0.0
    assert transition.terminated is True
    assert transition.truncated is False


def test_interrupted_terminal_bootstraps_from_terminal_observation() -> None:
    collector = _collector()
    collector.reset(_step([1], [2.0]))

    collector.step(_step([1], [5.0], rewards=[3.0], truncated=[True]))

    transition = collector.trajectories()[1][0]
    assert transition.next_value == pytest.approx(50.0)
    assert transition.terminated is False
    assert transition.truncated is True


def test_same_batch_terminal_is_closed_before_new_decision_for_same_id() -> None:
    collector = _collector()
    collector.reset(_step([2], [1.0]))

    output = collector.step(
        _step(
            [2, 2],
            [7.0, 9.0],
            rewards=[0.0, 2.0],
            terminated=[False, True],
        )
    )

    trajectories = collector.trajectories()
    assert len(trajectories[2]) == 1
    assert trajectories[2][0].reward == pytest.approx(2.0)
    assert trajectories[2][0].terminated is True
    assert collector.pending_agent_ids == (2,)
    np.testing.assert_allclose(output.actions[:, 0], [np.tanh(0.7)])


def test_delayed_reset_decision_is_not_counted_as_a_transition() -> None:
    collector = _collector()
    collector.reset(_step([3], [1.0]))
    collector.step(_step([3], [4.0], rewards=[2.0], terminated=[True]))

    collector.step(_step([3], [5.0], rewards=[99.0]))
    collector.step(_step([3], [6.0], rewards=[4.0], terminated=[True]))

    transitions = collector.trajectories()[3]
    assert len(transitions) == 2
    assert [transition.reward for transition in transitions] == pytest.approx([2.0, 4.0])


def test_build_batch_computes_literal_per_agent_gae_and_retains_pending() -> None:
    collector = _collector(gamma=0.9, gae_lambda=0.8)
    collector.reset(_step([1], [0.1]))
    collector.step(_step([1], [0.2], rewards=[1.0]))
    collector.step(_step([1], [0.3], rewards=[2.0], terminated=[True]))
    collector.step(_step([1], [0.4]))

    batch = collector.build_batch()

    torch.testing.assert_close(
        batch.advantages,
        torch.tensor([1.8, 0.0], dtype=torch.float32),
    )
    torch.testing.assert_close(
        batch.returns,
        torch.tensor([2.8, 2.0], dtype=torch.float32),
    )
    torch.testing.assert_close(
        batch.observations[0],
        torch.tensor([[0.1], [0.2]], dtype=torch.float32),
    )
    assert collector.completed_transition_count == 0
    assert collector.pending_agent_ids == (1,)
