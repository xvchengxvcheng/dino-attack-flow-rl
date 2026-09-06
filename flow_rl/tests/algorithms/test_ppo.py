from __future__ import annotations

import copy
import math

import pytest
import torch

from flow_rl.algorithms.ppo import (
    PPOBatch,
    PPOUpdater,
    clipped_policy_loss,
    clipped_value_loss,
    linear_schedule,
    normalize_advantages,
)
from flow_rl.models.policy import GaussianActorCritic


def test_clipped_policy_loss_uses_lower_positive_advantage_surrogate() -> None:
    loss, ratio, clip_fraction = clipped_policy_loss(
        new_log_probs=torch.tensor([math.log(1.5)], dtype=torch.float32),
        old_log_probs=torch.tensor([0.0], dtype=torch.float32),
        advantages=torch.tensor([2.0], dtype=torch.float32),
        clip_range=0.2,
    )

    assert loss.item() == pytest.approx(-2.4)
    assert ratio.item() == pytest.approx(1.5)
    assert clip_fraction.item() == pytest.approx(1.0)


def test_clipped_policy_loss_uses_more_negative_clipped_surrogate() -> None:
    loss, ratio, clip_fraction = clipped_policy_loss(
        new_log_probs=torch.tensor([math.log(0.5)], dtype=torch.float32),
        old_log_probs=torch.tensor([0.0], dtype=torch.float32),
        advantages=torch.tensor([-2.0], dtype=torch.float32),
        clip_range=0.2,
    )

    assert loss.item() == pytest.approx(1.6)
    assert ratio.item() == pytest.approx(0.5)
    assert clip_fraction.item() == pytest.approx(1.0)


def test_clipped_value_loss_uses_larger_error() -> None:
    loss = clipped_value_loss(
        values=torch.tensor([3.0], dtype=torch.float32),
        old_values=torch.tensor([0.0], dtype=torch.float32),
        returns=torch.tensor([2.0], dtype=torch.float32),
        clip_range=0.2,
    )

    assert loss.item() == pytest.approx(1.62)


def test_constant_advantages_normalize_to_finite_zeros() -> None:
    normalized = normalize_advantages(torch.full((4,), 7.0, dtype=torch.float32))

    torch.testing.assert_close(normalized, torch.zeros(4))
    assert torch.isfinite(normalized).all()


@pytest.mark.parametrize(
    ("step", "expected"),
    [(0, 3.0), (25, 2.5), (100, 1.0), (200, 1.0)],
)
def test_linear_schedule_clamps_progress(step: int, expected: float) -> None:
    assert linear_schedule(3.0, 1.0, step, 100) == pytest.approx(expected)


def test_ppo_batch_rejects_non_float32_or_non_finite_data() -> None:
    valid = {
        "observations": (torch.zeros(2, 3, dtype=torch.float32),),
        "actions": torch.zeros(2, 1, dtype=torch.float32),
        "old_log_probs": torch.zeros(2, dtype=torch.float32),
        "old_values": torch.zeros(2, dtype=torch.float32),
        "advantages": torch.ones(2, dtype=torch.float32),
        "returns": torch.ones(2, dtype=torch.float32),
    }
    with pytest.raises(TypeError, match="float32"):
        PPOBatch(**{**valid, "actions": valid["actions"].double()})
    invalid_returns = valid["returns"].clone()
    invalid_returns[0] = torch.nan
    with pytest.raises(ValueError, match="finite"):
        PPOBatch(**{**valid, "returns": invalid_returns})


def _make_policy_and_batch() -> tuple[GaussianActorCritic, PPOBatch]:
    torch.manual_seed(7)
    policy = GaussianActorCritic(
        observation_shapes=((3,),),
        action_size=2,
        hidden_sizes=(8, 8),
    )
    observations = (
        torch.tensor(
            [
                [1.0, 0.0, -1.0],
                [0.5, 1.0, 0.0],
                [-1.0, 0.5, 1.0],
                [2.0, -1.0, 0.5],
                [0.0, 0.25, -0.25],
            ],
            dtype=torch.float32,
        ),
    )
    with torch.no_grad():
        old = policy.act(
            observations,
            deterministic=False,
            generator=torch.Generator().manual_seed(19),
        )
    batch = PPOBatch(
        observations=observations,
        actions=old.actions,
        old_log_probs=old.log_probs,
        old_values=old.values,
        advantages=torch.tensor([2.0, -1.0, 1.0, -2.0, 0.5], dtype=torch.float32),
        returns=old.values + torch.tensor(
            [1.0, -0.5, 0.75, -1.0, 0.25], dtype=torch.float32
        ),
    )
    return policy, batch


def test_updater_changes_actor_and_critic_and_consumes_final_partial_minibatch() -> None:
    policy, batch = _make_policy_and_batch()
    actor_before = [parameter.detach().clone() for parameter in policy.actor.parameters()]
    critic_before = [parameter.detach().clone() for parameter in policy.critic.parameters()]
    updater = PPOUpdater(
        policy,
        learning_rate=1e-2,
        final_learning_rate=1e-2,
        clip_range=0.2,
        final_clip_range=0.2,
        entropy_coefficient=0.0,
        final_entropy_coefficient=0.0,
        value_coefficient=0.5,
        max_gradient_norm=0.5,
        total_environment_steps=100,
        batch_size=2,
        epochs=2,
        generator=torch.Generator().manual_seed(23),
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
    for value in metrics.as_dict().values():
        assert math.isfinite(float(value))


def test_updater_is_reproducible_with_identical_model_batch_and_generator_state() -> None:
    first_policy, batch = _make_policy_and_batch()
    second_policy = copy.deepcopy(first_policy)
    kwargs = {
        "learning_rate": 1e-3,
        "final_learning_rate": 1e-3,
        "clip_range": 0.2,
        "final_clip_range": 0.2,
        "entropy_coefficient": 1e-3,
        "final_entropy_coefficient": 1e-3,
        "value_coefficient": 0.5,
        "max_gradient_norm": 0.5,
        "total_environment_steps": 100,
        "batch_size": 2,
        "epochs": 2,
    }
    first = PPOUpdater(
        first_policy,
        **kwargs,
        generator=torch.Generator().manual_seed(29),
    )
    second = PPOUpdater(
        second_policy,
        **kwargs,
        generator=torch.Generator().manual_seed(29),
    )

    first_metrics = first.update(batch, environment_steps=25)
    second_metrics = second.update(batch, environment_steps=25)

    assert first_metrics.as_dict() == pytest.approx(second_metrics.as_dict())
    for first_parameter, second_parameter in zip(
        first_policy.parameters(), second_policy.parameters()
    ):
        torch.testing.assert_close(
            first_parameter,
            second_parameter,
            rtol=0.0,
            atol=0.0,
        )
