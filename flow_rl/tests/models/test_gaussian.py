from __future__ import annotations

import math

import pytest
import torch

from flow_rl.models.gaussian import TanhGaussianActor, flatten_observations


def _fixed_actor(*, mean: float, log_std: float) -> TanhGaussianActor:
    actor = TanhGaussianActor(
        observation_shapes=((1,),),
        action_size=1,
        hidden_sizes=(),
    )
    with torch.no_grad():
        actor.mean_head.weight.zero_()
        actor.mean_head.bias.fill_(mean)
        actor.log_std.fill_(log_std)
    return actor


def test_tanh_gaussian_matches_hand_derived_scalar_log_probability() -> None:
    actor = _fixed_actor(mean=0.25, log_std=-0.5)
    action = torch.tensor([[0.2]], dtype=torch.float32)

    log_prob, _ = actor.evaluate_actions((torch.zeros(1, 1),), action)

    z = 0.2027325540540822
    base = -0.5 * ((z - 0.25) / math.exp(-0.5)) ** 2
    base -= -0.5 + 0.5 * math.log(2.0 * math.pi)
    expected = base - math.log(1.0 - 0.2**2)
    assert log_prob.item() == pytest.approx(expected, abs=1e-6)


@pytest.mark.parametrize("action", [-1.0, -0.999999, 0.999999, 1.0])
def test_log_probability_is_finite_at_action_boundaries(action: float) -> None:
    actor = _fixed_actor(mean=0.0, log_std=0.0)

    log_prob, entropy = actor.evaluate_actions(
        (torch.zeros(1, 1),),
        torch.tensor([[action]], dtype=torch.float32),
    )

    assert torch.isfinite(log_prob).all()
    assert torch.isfinite(entropy).all()


def test_sample_is_seeded_float32_bounded_and_has_literal_shapes() -> None:
    actor = TanhGaussianActor(
        observation_shapes=((2,), (1, 2)),
        action_size=3,
        hidden_sizes=(4,),
    )
    observations = (
        torch.tensor([[1.0, 2.0], [3.0, 4.0]], dtype=torch.float32),
        torch.tensor([[[5.0, 6.0]], [[7.0, 8.0]]], dtype=torch.float32),
    )
    first_generator = torch.Generator().manual_seed(123)
    second_generator = torch.Generator().manual_seed(123)

    first = actor.sample(observations, deterministic=False, generator=first_generator)
    second = actor.sample(observations, deterministic=False, generator=second_generator)

    assert first.actions.shape == (2, 3)
    assert first.pre_tanh.shape == (2, 3)
    assert first.log_probs.shape == (2,)
    assert first.entropy.shape == (2,)
    assert first.actions.dtype == torch.float32
    assert torch.all(first.actions >= -1.0)
    assert torch.all(first.actions <= 1.0)
    torch.testing.assert_close(first.actions, second.actions, rtol=0.0, atol=0.0)
    torch.testing.assert_close(first.log_probs, second.log_probs, rtol=0.0, atol=0.0)


def test_deterministic_sample_is_tanh_of_mean() -> None:
    actor = _fixed_actor(mean=0.5, log_std=0.0)

    sample = actor.sample((torch.tensor([[3.0]], dtype=torch.float32),), deterministic=True)

    assert sample.actions.item() == pytest.approx(math.tanh(0.5), abs=1e-7)
    assert sample.pre_tanh.item() == pytest.approx(0.5, abs=1e-7)


def test_flatten_observations_preserves_tuple_then_feature_order() -> None:
    flattened = flatten_observations(
        (
            torch.tensor([[1.0, 2.0]], dtype=torch.float32),
            torch.tensor([[[3.0, 4.0]]], dtype=torch.float32),
        )
    )

    torch.testing.assert_close(
        flattened,
        torch.tensor([[1.0, 2.0, 3.0, 4.0]], dtype=torch.float32),
    )


def test_flatten_observations_preserves_feature_width_for_empty_batch() -> None:
    flattened = flatten_observations(
        (
            torch.empty((0, 2, 3), dtype=torch.float32),
            torch.empty((0, 4), dtype=torch.float32),
        )
    )

    assert flattened.shape == (0, 10)


def test_flatten_observations_rejects_different_batch_sizes() -> None:
    with pytest.raises(ValueError, match="batch"):
        flatten_observations(
            (
                torch.zeros(2, 1, dtype=torch.float32),
                torch.zeros(3, 1, dtype=torch.float32),
            )
        )


def test_distribution_clamps_log_std_without_mutating_parameter() -> None:
    actor = _fixed_actor(mean=0.0, log_std=99.0)

    _, entropy = actor.evaluate_actions(
        (torch.zeros(1, 1),),
        torch.zeros(1, 1, dtype=torch.float32),
    )

    expected_entropy = 0.5 * math.log(2.0 * math.pi * math.e) + 2.0
    assert entropy.item() == pytest.approx(expected_entropy, abs=1e-6)
    assert actor.log_std.item() == pytest.approx(99.0)
