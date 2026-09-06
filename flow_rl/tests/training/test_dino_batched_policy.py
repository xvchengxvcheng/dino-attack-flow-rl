from __future__ import annotations

from types import SimpleNamespace

import numpy as np
import torch

from flow_rl.envs.types import EnvStep
from flow_rl.models.fpo_policy import FPOPolicyOutput
from flow_rl.models.policy import PolicyOutput
from flow_rl.models.policyflow_policy import PolicyFlowPolicyOutput
from flow_rl.training.dino_batched_policy import (
    DinoBatchedFPOPolicyDecider,
    DinoBatchedPolicyFlowDecider,
    DinoBatchedPolicyDecider,
)
from flow_rl.training.dino_parallel_ppo import DinoParallelDecisionRequest


class _RecordingPolicy:
    def __init__(self) -> None:
        self.batch_shapes: list[tuple[tuple[int, ...], ...]] = []

    def act(self, observations, *, deterministic, generator):
        assert deterministic is False
        assert generator is not None
        self.batch_shapes.append(tuple(tuple(item.shape) for item in observations))
        markers = observations[0][:, 0]
        actions = torch.stack((markers, -markers), dim=1)
        return PolicyOutput(
            actions=actions,
            pre_tanh=actions,
            log_probs=markers + 100.0,
            values=markers + 200.0,
            entropy=torch.zeros_like(markers),
        )


class _FailIfCalledPolicy:
    def act(self, observations, *, deterministic, generator):
        raise AssertionError("policy.act must not run for an empty decision batch")


class _RecordingFPOPolicy:
    def __init__(self) -> None:
        self.batch_shapes: list[tuple[tuple[int, ...], ...]] = []

    def act(self, observations, *, nfe, num_fpo_samples, generator):
        assert nfe == 4
        assert num_fpo_samples == 8
        assert generator is not None
        self.batch_shapes.append(tuple(tuple(item.shape) for item in observations))
        markers = observations[0][:, 0]
        latent = torch.stack((markers, -markers), dim=1)
        batch_size = len(markers)
        loss_eps = torch.ones((batch_size, 8, 2)) * markers[:, None, None]
        loss_t = torch.ones((batch_size, 8, 1)) * 0.5
        old_losses = torch.ones((batch_size, 8)) * markers[:, None]
        return FPOPolicyOutput(
            actions=torch.tanh(latent),
            latent_actions=latent,
            loss_eps=loss_eps,
            loss_t=loss_t,
            old_cfm_losses=old_losses,
            values=markers + 200.0,
            nfe=nfe,
        )


class _RecordingPolicyFlowPolicy:
    def __init__(self) -> None:
        self.batch_shapes: list[tuple[tuple[int, ...], ...]] = []

    def act(self, observations, *, evaluation, generator):
        assert evaluation is False
        assert generator is not None
        self.batch_shapes.append(tuple(tuple(item.shape) for item in observations))
        markers = observations[0][:, 0]
        prior = torch.stack((markers, -markers), dim=1)
        batch_size = len(markers)
        return PolicyFlowPolicyOutput(
            actions=torch.tanh(prior),
            flow_x0=prior + 1.0,
            actions_prior=prior,
            delta_actions=torch.zeros_like(prior),
            old_delta_std=torch.full_like(prior, 0.5),
            old_delta_log_probs=markers + 10.0,
            old_velocity_grid=torch.ones((batch_size, 5, 2)) * markers[:, None, None],
            values=markers + 200.0,
            solver_steps=2,
            velocity_nfe=4,
        )


def _request(environment_id: int) -> DinoParallelDecisionRequest:
    marker = float(environment_id + 1)
    agent_ids = np.asarray([100 + environment_id], dtype=np.int64)
    observations = (
        np.asarray([[marker, marker + 0.5]], dtype=np.float32),
        np.asarray([[[marker], [marker + 1.0]]], dtype=np.float32),
    )
    step = EnvStep(
        agent_ids=agent_ids,
        observations=observations,
        rewards=np.zeros(1, dtype=np.float32),
        terminated=np.zeros(1, dtype=bool),
        truncated=np.zeros(1, dtype=bool),
    )
    return DinoParallelDecisionRequest(
        environment_id=environment_id,
        process_generation=3,
        policy_version=7,
        step=step,
        adapter=SimpleNamespace(pending_agent_ids=agent_ids),
    )


def test_sixteen_environments_use_one_policy_act_with_batch_size_sixteen() -> None:
    policy = _RecordingPolicy()
    decider = DinoBatchedPolicyDecider(
        policy=policy,
        device=torch.device("cpu"),
        action_size=2,
        generator=torch.Generator().manual_seed(123),
    )

    results = decider(tuple(_request(environment_id) for environment_id in range(16)))

    assert policy.batch_shapes == [((16, 2), (16, 2, 1))]
    statistics = decider.statistics()
    assert statistics.batch_count == 1
    assert statistics.environment_count == 16
    assert statistics.agent_count == 16
    assert statistics.mean_environment_batch_size == 16.0
    assert statistics.max_environment_batch_size == 16
    assert statistics.inference_seconds >= 0.0
    assert sorted(results) == list(range(16))
    for environment_id, info in results.items():
        marker = float(environment_id + 1)
        np.testing.assert_array_equal(info.agent_ids, [100 + environment_id])
        np.testing.assert_allclose(info.actions, [[marker, -marker]])
        np.testing.assert_allclose(info.values, [marker + 200.0])
        assert info.auxiliaries[0].old_log_prob == marker + 100.0
        assert info.process_generation == 3
        assert info.policy_version == 7


def test_empty_agent_requests_do_not_call_policy() -> None:
    request = _request(0)
    empty = DinoParallelDecisionRequest(
        environment_id=request.environment_id,
        process_generation=request.process_generation,
        policy_version=request.policy_version,
        step=EnvStep(
            agent_ids=np.empty(0, dtype=np.int64),
            observations=(
                np.empty((0, 2), dtype=np.float32),
                np.empty((0, 2, 1), dtype=np.float32),
            ),
            rewards=np.empty(0, dtype=np.float32),
            terminated=np.empty(0, dtype=bool),
            truncated=np.empty(0, dtype=bool),
        ),
        adapter=SimpleNamespace(pending_agent_ids=np.empty(0, dtype=np.int64)),
    )
    decider = DinoBatchedPolicyDecider(
        policy=_FailIfCalledPolicy(),
        device=torch.device("cpu"),
        action_size=2,
        generator=torch.Generator().manual_seed(123),
    )

    results = decider((empty,))

    assert results[0].actions.shape == (0, 2)
    assert results[0].values.shape == (0,)
    assert results[0].auxiliaries == ()


def test_fpo_decider_batches_requests_and_preserves_per_agent_samples() -> None:
    policy = _RecordingFPOPolicy()
    decider = DinoBatchedFPOPolicyDecider(
        policy=policy,
        device=torch.device("cpu"),
        action_size=2,
        nfe=4,
        num_fpo_samples=8,
        generator=torch.Generator().manual_seed(123),
    )

    results = decider(tuple(_request(environment_id) for environment_id in range(16)))

    assert policy.batch_shapes == [((16, 2), (16, 2, 1))]
    assert decider.statistics().agent_count == 16
    for environment_id, info in results.items():
        marker = float(environment_id + 1)
        auxiliary = info.auxiliaries[0]
        np.testing.assert_allclose(info.actions, [[np.tanh(marker), -np.tanh(marker)]])
        np.testing.assert_allclose(auxiliary.latent_action, [marker, -marker])
        assert auxiliary.loss_eps.shape == (8, 2)
        assert auxiliary.loss_t.shape == (8, 1)
        assert auxiliary.old_cfm_losses.shape == (8,)
        np.testing.assert_allclose(auxiliary.old_cfm_losses, marker)


def test_policyflow_decider_preserves_transition_owned_behavior_snapshot() -> None:
    policy = _RecordingPolicyFlowPolicy()
    decider = DinoBatchedPolicyFlowDecider(
        policy=policy,
        device=torch.device("cpu"),
        action_size=2,
        velocity_nfe=4,
        generator=torch.Generator().manual_seed(123),
    )

    results = decider(tuple(_request(environment_id) for environment_id in range(16)))

    assert policy.batch_shapes == [((16, 2), (16, 2, 1))]
    assert decider.statistics().agent_count == 16
    for environment_id, info in results.items():
        marker = float(environment_id + 1)
        auxiliary = info.auxiliaries[0]
        np.testing.assert_allclose(auxiliary.flow_x0, [marker + 1.0, -marker + 1.0])
        np.testing.assert_allclose(auxiliary.actions_prior, [marker, -marker])
        assert auxiliary.old_delta_log_prob == marker + 10.0
        assert auxiliary.old_velocity_grid.shape == (5, 2)
        np.testing.assert_allclose(auxiliary.old_velocity_grid, marker)
