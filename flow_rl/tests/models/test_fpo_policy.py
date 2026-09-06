from __future__ import annotations

import torch

from flow_rl.models.fpo_policy import FlowActorCritic


def test_flow_actor_critic_returns_bounded_and_training_auxiliary_shapes() -> None:
    # Losing the latent target or MC axis makes the official CFM ratio impossible.
    policy = FlowActorCritic(
        observation_shapes=((3,),),
        action_size=2,
        state_size=4,
        time_embedding_size=4,
        velocity_hidden_sizes=(8,),
        critic_hidden_sizes=(8,),
    )
    observations = (torch.tensor([[1.0, 2.0, 3.0], [4.0, 5.0, 6.0]]),)

    output = policy.act(
        observations,
        nfe=4,
        num_fpo_samples=3,
        generator=torch.Generator().manual_seed(7),
    )

    assert output.actions.shape == (2, 2)
    assert output.latent_actions.shape == (2, 2)
    assert output.loss_eps.shape == (2, 3, 2)
    assert output.loss_t.shape == (2, 3, 1)
    assert output.old_cfm_losses.shape == (2, 3)
    assert output.values.shape == (2,)
    assert output.nfe == 4
    assert torch.all(output.actions >= -1.0)
    assert torch.all(output.actions <= 1.0)
    torch.testing.assert_close(output.actions, torch.tanh(output.latent_actions))
    assert output.old_cfm_losses.requires_grad is False


def test_flow_actor_critic_is_reproducible_with_same_generator_seed() -> None:
    # An implicit global RNG read would change rollout targets across resumed runs.
    policy = FlowActorCritic(
        observation_shapes=((3,),),
        action_size=2,
        state_size=4,
        time_embedding_size=4,
        velocity_hidden_sizes=(8,),
        critic_hidden_sizes=(8,),
    )
    observations = (torch.zeros(2, 3),)

    first = policy.act(
        observations,
        nfe=2,
        num_fpo_samples=2,
        generator=torch.Generator().manual_seed(11),
    )
    second = policy.act(
        observations,
        nfe=2,
        num_fpo_samples=2,
        generator=torch.Generator().manual_seed(11),
    )

    torch.testing.assert_close(first.actions, second.actions)
    torch.testing.assert_close(first.loss_eps, second.loss_eps)
    torch.testing.assert_close(first.loss_t, second.loss_t)
    torch.testing.assert_close(first.old_cfm_losses, second.old_cfm_losses)
