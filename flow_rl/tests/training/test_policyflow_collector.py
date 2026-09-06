from __future__ import annotations

import numpy as np
import pytest
import torch

from flow_rl.envs.types import EnvStep
from flow_rl.models.policyflow_policy import PolicyFlowPolicyOutput
from flow_rl.training.policyflow_collector import PolicyFlowAuxiliary
from flow_rl.training.versioned_collector import (
    AgentIdentity,
    VersionedBatch,
    VersionedTransition,
)


class _FrozenNormalizer:
    def __init__(self) -> None:
        self.update_calls = 0

    def update(self, observations: tuple[np.ndarray, ...]) -> None:
        del observations
        self.update_calls += 1

    def normalize(self, observations: tuple[np.ndarray, ...]) -> tuple[np.ndarray, ...]:
        return tuple(item.copy() for item in observations)


class _Critic:
    def __call__(self, observations: tuple[torch.Tensor, ...]) -> torch.Tensor:
        return observations[0][:, 0] * 10.0


class _Policy:
    critic = _Critic()

    def act(self, observations, *, evaluation, generator):
        del evaluation, generator
        feature = observations[0][:, 0]
        prior = torch.stack((feature, -feature), dim=1)
        delta = torch.full_like(prior, 0.1)
        return PolicyFlowPolicyOutput(
            actions=torch.tanh(prior + delta),
            flow_x0=torch.stack((feature + 1.0, feature - 1.0), dim=1),
            actions_prior=prior,
            delta_actions=delta,
            old_delta_std=torch.full_like(prior, 0.5),
            old_delta_log_probs=feature * 0.01,
            old_velocity_grid=torch.zeros((feature.shape[0], 5, 2), dtype=torch.float32),
            values=feature * 10.0,
            solver_steps=2,
            velocity_nfe=4,
        )


def _step(ids, observations, *, rewards=None, terminated=None, truncated=None):
    count = len(ids)
    return EnvStep(
        agent_ids=np.asarray(ids, dtype=np.int64),
        observations=(np.asarray(observations, dtype=np.float32).reshape(count, 1),),
        rewards=np.asarray(rewards or [0.0] * count, dtype=np.float32),
        terminated=np.asarray(terminated or [False] * count, dtype=bool),
        truncated=np.asarray(truncated or [False] * count, dtype=bool),
    )


def _collector():
    from flow_rl.training.policyflow_collector import PolicyFlowCollector

    normalizer = _FrozenNormalizer()
    return PolicyFlowCollector(
        policy=_Policy(), normalizer=normalizer, device=torch.device("cpu"),
        gamma=0.99, gae_lambda=0.95, velocity_nfe=4,
        generator=torch.Generator().manual_seed(9),
    ), normalizer


def test_collector_preserves_auxiliaries_under_agent_id_reordering() -> None:
    collector, normalizer = _collector()
    collector.reset(_step([20, 10], [0.2, 0.1]))
    collector.step(_step([10, 20], [0.3, 0.4], rewards=[1.0, 2.0]))
    trajectories = collector.trajectories()
    np.testing.assert_allclose(trajectories[20][0].auxiliary.actions_prior, [0.2, -0.2])
    np.testing.assert_allclose(trajectories[20][0].auxiliary.flow_x0, [1.2, -0.8])
    assert trajectories[10][0].reward == pytest.approx(1.0)
    assert normalizer.update_calls == 0


def test_collector_bootstraps_truncation_and_accepts_terminal_only_tick() -> None:
    collector, _ = _collector()
    collector.reset(_step([1], [0.2]))
    output = collector.step(_step([1], [0.5], rewards=[3.0], truncated=[True]))
    assert output.actions.shape == (0, 2)
    batch = collector.build_batch()
    assert batch.flow_x0.shape == batch.actions_prior.shape == (1, 2)
    assert batch.old_delta_std.shape == (1, 2)
    assert batch.old_velocity_grid.shape == (1, 5, 2)
    assert batch.returns.item() == pytest.approx(3.0 + 0.99 * 5.0)


def test_replace_pending_refreshes_behavior_action_after_snapshot_update() -> None:
    collector, _ = _collector()
    collector.reset(_step([1], [0.2]))
    collector.replace_pending(_step([1], [0.5]))
    collector.step(_step([1], [0.6], rewards=[2.0], terminated=[True]))
    batch = collector.build_batch()
    assert batch.old_values.item() == pytest.approx(5.0)


def test_replace_pending_keeps_absent_agent_pending_until_it_returns() -> None:
    collector, _ = _collector()
    collector.reset(_step([1, 2], [0.2, 0.4]))
    collector.replace_pending(_step([1], [0.5]))
    assert collector.pending_agent_ids == (1, 2)


def test_versioned_bridge_copies_each_transition_behavior_snapshot() -> None:
    from flow_rl.training.policyflow_trainer import _build_versioned_policyflow_batch

    auxiliary = PolicyFlowAuxiliary(
        flow_x0=np.asarray([1.0, 2.0], dtype=np.float32),
        actions_prior=np.asarray([3.0, 4.0], dtype=np.float32),
        delta_actions=np.asarray([0.1, 0.2], dtype=np.float32),
        old_delta_std=np.asarray([0.5, 0.6], dtype=np.float32),
        old_delta_log_prob=-2.5,
        old_velocity_grid=np.arange(10, dtype=np.float32).reshape(5, 2),
    )
    transition = VersionedTransition(
        identity=AgentIdentity(3, 2, 41),
        trajectory_id=7,
        policy_version=11,
        observation=(np.asarray([0.25], dtype=np.float32),),
        action=np.asarray([0.3, -0.4], dtype=np.float32),
        reward=1.0,
        value=0.5,
        next_observation=(np.asarray([0.5], dtype=np.float32),),
        next_value=0.0,
        terminated=True,
        truncated=False,
        auxiliary=auxiliary,
    )

    batch = _build_versioned_policyflow_batch(
        VersionedBatch(policy_version=11, transitions=(transition,), diagnostics=()),
        gamma=0.995,
        gae_lambda=0.95,
        device=torch.device("cpu"),
    )

    assert batch.flow_x0.tolist() == [[1.0, 2.0]]
    assert batch.actions_prior.tolist() == [[3.0, 4.0]]
    np.testing.assert_allclose(batch.delta_actions.numpy(), [[0.1, 0.2]])
    np.testing.assert_allclose(batch.old_delta_std.numpy(), [[0.5, 0.6]])
    assert batch.old_delta_log_probs.tolist() == pytest.approx([-2.5])
    assert batch.old_velocity_grid.tolist() == [
        np.arange(10, dtype=np.float32).reshape(5, 2).tolist()
    ]
    assert batch.returns.tolist() == pytest.approx([1.0])
