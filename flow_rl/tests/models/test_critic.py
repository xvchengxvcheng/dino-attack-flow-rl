from __future__ import annotations

import torch

from flow_rl.models.critic import ValueCritic


def test_critic_returns_one_finite_value_per_observation_and_backpropagates() -> None:
    critic = ValueCritic(
        observation_shapes=((2,), (1,)),
        hidden_sizes=(8, 8),
    )
    observations = (
        torch.tensor([[1.0, 2.0], [3.0, 4.0]], dtype=torch.float32),
        torch.tensor([[5.0], [6.0]], dtype=torch.float32),
    )

    values = critic(observations)
    values.sum().backward()

    assert values.shape == (2,)
    assert values.dtype == torch.float32
    assert torch.isfinite(values).all()
    assert all(parameter.grad is not None for parameter in critic.parameters())
    assert all(torch.isfinite(parameter.grad).all() for parameter in critic.parameters())

