from __future__ import annotations

import math

import pytest
import torch
from torch import nn

from flow_rl.algorithms.fpo import (
    FPOBatch,
    FPOUpdater,
    conditional_flow_matching_loss,
    fpo_clipped_policy_loss,
    fpo_ratio,
)
from flow_rl.models.critic import ValueCritic
from flow_rl.models.flow import ConditionalVelocityMLP


class _ConstantVelocity(nn.Module):
    action_size = 1

    def forward(
        self,
        observations: tuple[torch.Tensor, ...],
        latent_actions: torch.Tensor,
        time: torch.Tensor,
    ) -> torch.Tensor:
        del observations, time
        return torch.ones_like(latent_actions)


class _TinyPolicy(nn.Module):
    def __init__(self) -> None:
        super().__init__()
        self.actor = ConditionalVelocityMLP(
            observation_shapes=((3,),),
            action_size=2,
            state_size=4,
            time_embedding_size=4,
            hidden_sizes=(8,),
        )
        self.critic = ValueCritic(
            observation_shapes=((3,),),
            hidden_sizes=(8,),
        )


def test_conditional_flow_matching_loss_matches_literal_condot_case() -> None:
    # Reversing the CondOT interpolation or target changes this hand-derived loss.
    loss = conditional_flow_matching_loss(
        _ConstantVelocity(),
        observations=(torch.tensor([[3.0]], dtype=torch.float32),),
        latent_actions=torch.tensor([[2.0]], dtype=torch.float32),
        loss_eps=torch.tensor([[[0.0]]], dtype=torch.float32),
        loss_t=torch.tensor([[[0.25]]], dtype=torch.float32),
    )

    # x_t=0.5, target=2.0, prediction=1.0, so MSE=1.
    torch.testing.assert_close(loss, torch.tensor([[1.0]], dtype=torch.float32))


def test_fpo_ratio_clamps_each_mc_difference_before_mean() -> None:
    # Averaging first would produce exp(4), while the official order produces exp(2.5).
    ratio, log_ratio = fpo_ratio(
        old_losses=torch.tensor([[10.0, 2.0]], dtype=torch.float32),
        new_losses=torch.tensor([[0.0, 0.0]], dtype=torch.float32),
        difference_clip=3.0,
    )

    assert log_ratio.item() == pytest.approx(2.5)
    assert ratio.item() == pytest.approx(math.exp(2.5))


@pytest.mark.parametrize(
    ("advantage", "expected_loss"),
    [(2.0, -2.4), (0.0, 0.0), (-2.0, 3.0)],
)
def test_fpo_clipped_surrogate_handles_signed_advantage(
    advantage: float,
    expected_loss: float,
) -> None:
    # Applying a nonnegative shift would make the negative-advantage case impossible.
    loss, clip_fraction = fpo_clipped_policy_loss(
        ratio=torch.tensor([1.5], dtype=torch.float32),
        advantages=torch.tensor([advantage], dtype=torch.float32),
        clip_range=0.2,
    )

    assert loss.item() == pytest.approx(expected_loss)
    assert clip_fraction.item() == pytest.approx(1.0)


def _batch(policy: _TinyPolicy) -> FPOBatch:
    generator = torch.Generator().manual_seed(12)
    observations = (torch.randn((5, 3), generator=generator),)
    latent_actions = torch.randn((5, 2), generator=generator)
    bounded_actions = torch.tanh(latent_actions)
    loss_eps = torch.randn((5, 2, 2), generator=generator)
    loss_t = torch.rand((5, 2, 1), generator=generator)
    with torch.no_grad():
        old_losses = conditional_flow_matching_loss(
            policy.actor,
            observations,
            latent_actions,
            loss_eps,
            loss_t,
        )
        old_values = policy.critic(observations)
    return FPOBatch(
        observations=observations,
        latent_actions=latent_actions,
        bounded_actions=bounded_actions,
        loss_eps=loss_eps,
        loss_t=loss_t,
        old_cfm_losses=old_losses,
        old_values=old_values,
        advantages=torch.tensor([1.0, -1.0, 0.5, -0.5, 2.0]),
        returns=torch.tensor([0.5, -0.5, 1.0, 0.0, 1.5]),
    )


def test_fpo_updater_changes_actor_and_critic_with_finite_metrics() -> None:
    # A detached CFM path or critic loss would leave one parameter group unchanged.
    policy = _TinyPolicy()
    batch = _batch(policy)
    actor_before = [parameter.detach().clone() for parameter in policy.actor.parameters()]
    critic_before = [parameter.detach().clone() for parameter in policy.critic.parameters()]
    updater = FPOUpdater(
        policy,
        learning_rate=3e-4,
        final_learning_rate=0.0,
        clip_range=0.2,
        final_clip_range=0.1,
        max_gradient_norm=0.5,
        total_environment_steps=100,
        batch_size=2,
        epochs=2,
        difference_clip=3.0,
        positive_advantage=False,
        generator=torch.Generator().manual_seed(4),
    )

    metrics = updater.update(batch, environment_steps=10)

    assert any(
        not torch.equal(before, after)
        for before, after in zip(actor_before, policy.actor.parameters())
    )
    assert any(
        not torch.equal(before, after)
        for before, after in zip(critic_before, policy.critic.parameters())
    )
    assert metrics.samples_processed == 10
    assert metrics.minibatches == 6
    assert all(
        math.isfinite(float(value))
        for name, value in metrics.as_dict().items()
        if name not in {"samples_processed", "minibatches"}
    )
