from __future__ import annotations

import copy

import pytest
import torch
from torch.distributions import Normal

from flow_rl.algorithms.policyflow import (
    PolicyFlowBatch,
    PolicyFlowUpdater,
    gaussian_kl_old_to_new,
    policyflow_clipped_surrogate,
    policyflow_variation,
)
from flow_rl.models.policyflow_policy import PolicyFlowActorCritic


def _policy() -> PolicyFlowActorCritic:
    torch.manual_seed(81)
    return PolicyFlowActorCritic(
        observation_shapes=((3,),), action_size=2, state_size=8,
        time_embedding_size=8, velocity_hidden_sizes=(8,),
        critic_hidden_sizes=(8,), solver_steps=2,
    )


def test_variation_and_brownian_loss_match_fixed_official_formula() -> None:
    policy = _policy()
    with torch.no_grad():
        next(policy.actor.parameters()).add_(0.1)
    observations = (torch.tensor([[0.1, 0.2, 0.3], [-0.3, 0.0, 0.5]]),)
    x0 = torch.tensor([[0.2, -0.4], [0.1, 0.3]])
    x1 = torch.tensor([[0.7, 0.5], [-0.2, 0.9]])
    time = torch.tensor([[0.25], [0.75]])
    result = policyflow_variation(policy, observations, x0=x0, x1=x1, time=time)
    xt = (1.0 - time) * x0 + time * x1
    current = policy.actor(observations, xt, time)
    with torch.inference_mode():
        old = policy.snapshot(observations, xt, time)
    expected_brownian = torch.nn.functional.mse_loss(
        (1.0 - time) * current, xt - time * old
    )
    assert torch.allclose(result.delta_velocity, current - old)
    assert torch.allclose(result.brownian_loss, expected_brownian)


def test_variation_accepts_transition_owned_behavior_velocity() -> None:
    policy = _policy()
    observations = (torch.zeros(2, 3),)
    x0 = torch.zeros(2, 2)
    x1 = torch.ones(2, 2)
    time = torch.full((2, 1), 0.5)
    old_velocity = torch.full((2, 2), 0.25)
    result = policyflow_variation(
        policy, observations, x0=x0, x1=x1, time=time,
        old_velocity=old_velocity,
    )
    current = policy.actor(observations, torch.full((2, 2), 0.5), time)
    assert torch.allclose(result.delta_velocity, current - old_velocity)


def test_ratio_and_signed_advantage_clip_match_hand_calculation() -> None:
    old = torch.tensor([-1.0, -1.0])
    new = old + torch.log(torch.tensor([1.5, 0.5]))
    advantages = torch.tensor([2.0, -2.0])
    result = policyflow_clipped_surrogate(
        new_log_probs=new, old_log_probs=old, advantages=advantages, clip_range=0.2
    )
    expected = -torch.minimum(
        torch.tensor([1.5, 0.5]) * advantages,
        torch.tensor([1.2, 0.8]) * advantages,
    ).mean()
    assert torch.allclose(result.policy_loss, expected)
    assert torch.allclose(result.ratios, torch.tensor([1.5, 0.5]))
    assert result.clip_fraction.item() == 1.0


def test_ratio_uses_unclamped_official_log_probability_difference() -> None:
    result = policyflow_clipped_surrogate(
        new_log_probs=torch.tensor([25.0]),
        old_log_probs=torch.zeros(1),
        advantages=torch.ones(1),
        clip_range=0.2,
    )
    assert result.log_ratios.item() == 25.0
    assert result.ratios.item() == pytest.approx(torch.exp(torch.tensor(25.0)).item())


def test_gaussian_kl_matches_old_to_new_distribution_formula() -> None:
    new_mean = torch.tensor([[0.2, -0.1]])
    new_std = torch.tensor([[0.8, 1.2]])
    old_mean = torch.zeros_like(new_mean)
    old_std = torch.tensor([[1.0, 0.7]])
    actual = gaussian_kl_old_to_new(new_mean, new_std, old_mean, old_std)
    expected = torch.distributions.kl_divergence(
        Normal(old_mean, old_std), Normal(new_mean, new_std)
    ).sum(dim=1)
    assert torch.allclose(actual, expected, atol=2e-5)


def test_brownian_objective_produces_finite_actor_gradient() -> None:
    policy = _policy()
    with torch.no_grad():
        next(policy.actor.parameters()).add_(0.05)
    result = policyflow_variation(
        policy,
        (torch.randn(4, 3),),
        x0=torch.randn(4, 2),
        x1=torch.randn(4, 2),
        time=torch.full((4, 1), 0.5),
    )
    (result.delta_velocity.square().mean() + result.brownian_loss).backward()
    gradients = [p.grad for p in policy.actor.parameters() if p.grad is not None]
    assert gradients
    assert all(torch.isfinite(gradient).all() for gradient in gradients)


def test_identical_policy_copies_are_numerically_reproducible() -> None:
    first = _policy()
    second = copy.deepcopy(first)
    inputs = dict(
        observations=(torch.randn(5, 3, generator=torch.Generator().manual_seed(90)),),
        x0=torch.randn(5, 2, generator=torch.Generator().manual_seed(91)),
        x1=torch.randn(5, 2, generator=torch.Generator().manual_seed(92)),
        time=torch.full((5, 1), 0.4),
    )
    one = policyflow_variation(first, **inputs)
    two = policyflow_variation(second, **inputs)
    assert torch.equal(one.delta_velocity, two.delta_velocity)
    assert torch.equal(one.brownian_loss, two.brownian_loss)


def _batch(policy: PolicyFlowActorCritic) -> PolicyFlowBatch:
    observations = (torch.randn(12, 3, generator=torch.Generator().manual_seed(93)),)
    output = policy.act(
        observations, evaluation=False,
        generator=torch.Generator().manual_seed(94),
    )
    return PolicyFlowBatch(
        observations=observations,
        flow_x0=output.flow_x0,
        actions_prior=output.actions_prior,
        delta_actions=output.delta_actions,
        actions=output.actions,
        old_delta_std=output.old_delta_std,
        old_delta_log_probs=output.old_delta_log_probs,
        old_velocity_grid=output.old_velocity_grid,
        old_values=output.values,
        advantages=torch.linspace(-1.0, 1.0, 12),
        returns=output.values + 0.5,
    )


def test_updater_is_finite_and_refreshes_snapshot_only_after_update() -> None:
    policy = _policy()
    batch = _batch(policy)
    before = copy.deepcopy(policy.snapshot.state_dict())
    updater = PolicyFlowUpdater(
        policy,
        actor_learning_rate=1e-3,
        critic_learning_rate=1e-3,
        clip_range=0.2,
        gaussian_entropy_coefficient=1e-3,
        brownian_coefficient=0.25,
        value_clip=0.2,
        max_gradient_norm=1.0,
        batch_size=6,
        epochs=2,
        generator=torch.Generator().manual_seed(95),
    )
    metrics = updater.update(batch)
    assert metrics.samples_processed == 24
    assert all(torch.isfinite(torch.tensor(value)) for value in metrics.as_dict().values())
    assert any(not torch.equal(before[key], value) for key, value in policy.snapshot.state_dict().items())
    assert all(
        torch.equal(policy.actor.state_dict()[key], value)
        for key, value in policy.snapshot.state_dict().items()
    )


def test_updater_reproduces_parameters_with_identical_inputs_and_generator() -> None:
    first = _policy()
    second = copy.deepcopy(first)
    first_batch = _batch(first)
    second_batch = copy.deepcopy(first_batch)
    kwargs = dict(
        actor_learning_rate=1e-3, critic_learning_rate=1e-3,
        clip_range=0.2, gaussian_entropy_coefficient=1e-3,
        brownian_coefficient=0.25, value_clip=0.2,
        max_gradient_norm=1.0, batch_size=6, epochs=1,
    )
    first_metrics = PolicyFlowUpdater(
        first, generator=torch.Generator().manual_seed(96), **kwargs
    ).update(first_batch)
    second_metrics = PolicyFlowUpdater(
        second, generator=torch.Generator().manual_seed(96), **kwargs
    ).update(second_batch)
    assert first_metrics.as_dict() == second_metrics.as_dict()
    assert all(
        torch.equal(left, right)
        for left, right in zip(first.state_dict().values(), second.state_dict().values())
    )
