from __future__ import annotations

import copy
import math

import pytest
import torch

from flow_rl.models.reinflow_policy import ReinFlowActorCritic


def _policy() -> ReinFlowActorCritic:
    torch.manual_seed(10)
    return ReinFlowActorCritic(
        observation_shapes=((3,),),
        action_size=1,
        state_size=4,
        time_embedding_size=4,
        velocity_hidden_sizes=(8,),
        critic_hidden_sizes=(8,),
        noise_hidden_sizes=(8,),
        nfe=1,
        min_noise_std=0.1,
        max_noise_std=0.4,
    )


def test_chain_statistics_matches_literal_one_step_normal_values() -> None:
    from flow_rl.algorithms.reinflow import reinflow_chain_statistics

    policy = _policy()
    with torch.no_grad():
        for parameter in policy.actor.parameters():
            parameter.zero_()
        for parameter in policy.noise_head.noise_mlp.parameters():
            parameter.zero_()
    observations = (torch.zeros((1, 3), dtype=torch.float32),)
    chain = torch.tensor([[[0.0], [0.2]]], dtype=torch.float32)

    statistics = reinflow_chain_statistics(policy, observations, chain)

    std = 0.2
    initial_log_prob = -0.5 * math.log(2.0 * math.pi)
    transition_log_prob = -math.log(std) - 0.5 * math.log(2.0 * math.pi) - 0.5
    expected_log_prob = (initial_log_prob + transition_log_prob) / 2.0
    initial_entropy = 0.5 * math.log(2.0 * math.pi * math.e)
    transition_entropy = math.log(std) + 0.5 * math.log(2.0 * math.pi * math.e)
    assert statistics.log_probs.item() == pytest.approx(expected_log_prob)
    assert statistics.entropy_rate.item() == pytest.approx(
        (initial_entropy + transition_entropy) / 2.0
    )
    assert statistics.mean_noise_std.item() == pytest.approx(std)


@pytest.mark.parametrize(
    ("advantage", "expected_loss"),
    [(2.0, -2.02), (0.0, 0.0), (-2.0, 2.0 * math.e)],
)
def test_reinflow_ratio_clamp_and_signed_clipped_surrogate(
    advantage: float, expected_loss: float
) -> None:
    from flow_rl.algorithms.reinflow import reinflow_clipped_policy_loss

    result = reinflow_clipped_policy_loss(
        new_log_probs=torch.tensor([3.0]),
        old_log_probs=torch.tensor([0.0]),
        advantages=torch.tensor([advantage]),
        clip_range=0.01,
        log_prob_min=-1.0,
        log_prob_max=1.0,
    )

    assert result.policy_loss.item() == pytest.approx(expected_loss)
    assert result.ratios.item() == pytest.approx(math.e)
    assert result.approximate_kl.item() == pytest.approx(math.e - 2.0)


def _batch(policy: ReinFlowActorCritic):
    from flow_rl.algorithms.reinflow import ReinFlowBatch

    observations = (torch.randn((5, 3), generator=torch.Generator().manual_seed(11)),)
    output = policy.act(
        observations,
        evaluation=False,
        generator=torch.Generator().manual_seed(12),
    )
    return ReinFlowBatch(
        observations=observations,
        chains=output.chains,
        actions=output.actions,
        old_log_probs=output.old_log_probs,
        old_values=output.values,
        advantages=torch.tensor([2.0, -1.0, 1.0, -2.0, 0.5]),
        returns=output.values + torch.tensor([1.0, -0.5, 0.5, -1.0, 0.25]),
    )


def test_updater_warmup_changes_only_critic_then_actor_and_noise() -> None:
    from flow_rl.algorithms.reinflow import ReinFlowUpdater

    policy = _policy()
    batch = _batch(policy)
    updater = ReinFlowUpdater(
        policy,
        actor_learning_rate=1e-2,
        critic_learning_rate=1e-2,
        clip_range=0.01,
        entropy_coefficient=0.03,
        max_gradient_norm=1.0,
        batch_size=2,
        epochs=1,
        critic_warmup_environment_steps=10,
        log_prob_min=-1.0,
        log_prob_max=1.0,
        generator=torch.Generator().manual_seed(13),
    )
    actor_before = copy.deepcopy(policy.actor.state_dict())
    noise_before = copy.deepcopy(policy.noise_head.noise_mlp.state_dict())
    critic_before = copy.deepcopy(policy.critic.state_dict())

    warmup = updater.update(batch, environment_steps=9)

    assert all(torch.equal(actor_before[k], v) for k, v in policy.actor.state_dict().items())
    assert all(
        torch.equal(noise_before[k], v)
        for k, v in policy.noise_head.noise_mlp.state_dict().items()
    )
    assert any(
        not torch.equal(critic_before[k], v)
        for k, v in policy.critic.state_dict().items()
    )
    assert warmup.actor_updated is False

    actor_before = copy.deepcopy(policy.actor.state_dict())
    noise_before = copy.deepcopy(policy.noise_head.noise_mlp.state_dict())
    active = updater.update(batch, environment_steps=10)

    assert any(
        not torch.equal(actor_before[k], v) for k, v in policy.actor.state_dict().items()
    )
    assert any(
        not torch.equal(noise_before[k], v)
        for k, v in policy.noise_head.noise_mlp.state_dict().items()
    )
    assert active.actor_updated is True
    assert active.samples_processed == 5
    assert active.minibatches == 3
    assert all(
        math.isfinite(float(value))
        for name, value in active.as_dict().items()
        if name not in {"actor_updated"}
    )


def test_updater_repeats_with_identical_model_batch_and_generator() -> None:
    from flow_rl.algorithms.reinflow import ReinFlowUpdater

    first_policy = _policy()
    second_policy = copy.deepcopy(first_policy)
    batch = _batch(first_policy)
    kwargs = {
        "actor_learning_rate": 1e-3,
        "critic_learning_rate": 2e-3,
        "clip_range": 0.01,
        "entropy_coefficient": 0.03,
        "max_gradient_norm": 1.0,
        "batch_size": 2,
        "epochs": 2,
        "critic_warmup_environment_steps": 0,
        "log_prob_min": -1.0,
        "log_prob_max": 1.0,
    }
    first = ReinFlowUpdater(
        first_policy,
        **kwargs,
        generator=torch.Generator().manual_seed(31),
    )
    second = ReinFlowUpdater(
        second_policy,
        **kwargs,
        generator=torch.Generator().manual_seed(31),
    )

    first_metrics = first.update(batch, environment_steps=10)
    second_metrics = second.update(batch, environment_steps=10)

    assert first_metrics.as_dict() == pytest.approx(second_metrics.as_dict())
    for left, right in zip(first_policy.parameters(), second_policy.parameters()):
        torch.testing.assert_close(left, right, rtol=0.0, atol=0.0)
