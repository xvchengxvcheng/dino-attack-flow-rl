from __future__ import annotations

import torch

from flow_rl.models.policy import GaussianActorCritic


def test_policy_combines_action_statistics_and_values_without_shared_parameters() -> None:
    policy = GaussianActorCritic(
        observation_shapes=((3,),),
        action_size=2,
        hidden_sizes=(8, 8),
    )
    observations = (torch.tensor([[1.0, 2.0, 3.0]], dtype=torch.float32),)

    output = policy.act(observations, deterministic=True)
    log_probs, entropy, values = policy.evaluate_actions(observations, output.actions)

    assert output.actions.shape == (1, 2)
    assert output.pre_tanh.shape == (1, 2)
    assert output.log_probs.shape == (1,)
    assert output.values.shape == (1,)
    assert output.entropy.shape == (1,)
    torch.testing.assert_close(log_probs, output.log_probs)
    torch.testing.assert_close(entropy, output.entropy)
    torch.testing.assert_close(values, output.values)
    actor_parameters = {id(parameter) for parameter in policy.actor.parameters()}
    critic_parameters = {id(parameter) for parameter in policy.critic.parameters()}
    assert actor_parameters.isdisjoint(critic_parameters)
