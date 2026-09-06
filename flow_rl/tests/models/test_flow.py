from __future__ import annotations

import pytest
import torch
from torch import nn

from flow_rl.models.flow import (
    ConditionalVelocityMLP,
    EulerFlowSampler,
    FlowStateEncoder,
    FlowTimeEmbedding,
)


class _ConstantVelocity(nn.Module):
    def __init__(self, velocity: tuple[float, ...]) -> None:
        super().__init__()
        self.action_size = len(velocity)
        self.register_buffer(
            "velocity",
            torch.tensor(velocity, dtype=torch.float32),
        )
        self.calls = 0

    def forward(
        self,
        observations: tuple[torch.Tensor, ...],
        latent_actions: torch.Tensor,
        time: torch.Tensor,
    ) -> torch.Tensor:
        del observations, time
        self.calls += 1
        return self.velocity.expand_as(latent_actions)


def _observations(batch_size: int = 2) -> tuple[torch.Tensor, ...]:
    return (
        torch.arange(batch_size * 2, dtype=torch.float32).reshape(batch_size, 2),
        torch.arange(batch_size * 3, dtype=torch.float32).reshape(batch_size, 1, 3),
    )


def test_state_encoder_preserves_batch_and_rejects_wrong_feature_shape() -> None:
    # A feature-shape check catches silent flattening of an incompatible Unity stream.
    encoder = FlowStateEncoder(
        observation_shapes=((2,), (1, 3)),
        state_size=7,
    )

    encoded = encoder(_observations())

    assert encoded.shape == (2, 7)
    assert encoded.dtype == torch.float32
    assert torch.isfinite(encoded).all()
    with pytest.raises(ValueError, match="feature shape"):
        encoder((torch.zeros(2, 3), torch.zeros(2, 1, 3)))


def test_time_embedding_has_literal_shape_and_finite_gradient() -> None:
    # A detached embedding would prevent the learned time projection from updating.
    embedding = FlowTimeEmbedding(embedding_size=8)
    time = torch.tensor([[0.0], [0.5], [1.0]], dtype=torch.float32)

    encoded = embedding(time)
    encoded.square().mean().backward()

    assert encoded.shape == (3, 8)
    assert encoded.dtype == torch.float32
    assert all(
        parameter.grad is not None and torch.isfinite(parameter.grad).all()
        for parameter in embedding.parameters()
    )


def test_time_embedding_rejects_non_column_time() -> None:
    embedding = FlowTimeEmbedding(embedding_size=8)

    with pytest.raises(ValueError, match=r"\(batch, 1\)"):
        embedding(torch.zeros(2, dtype=torch.float32))


def test_conditional_velocity_matches_action_shape_and_backpropagates() -> None:
    # A wrong output head or detached condition would break CFM optimization.
    velocity = ConditionalVelocityMLP(
        observation_shapes=((2,), (1, 3)),
        action_size=2,
        state_size=7,
        time_embedding_size=8,
        hidden_sizes=(11, 11),
    )
    latent_actions = torch.tensor(
        [[0.25, -0.5], [1.0, 0.0]],
        dtype=torch.float32,
        requires_grad=True,
    )
    time = torch.tensor([[0.25], [0.75]], dtype=torch.float32)

    output = velocity(_observations(), latent_actions, time)
    output.square().mean().backward()

    assert output.shape == (2, 2)
    assert output.dtype == torch.float32
    assert torch.isfinite(output).all()
    assert latent_actions.grad is not None
    assert torch.isfinite(latent_actions.grad).all()
    assert all(
        parameter.grad is not None and torch.isfinite(parameter.grad).all()
        for parameter in velocity.parameters()
    )


def test_euler_sampler_matches_hand_computed_constant_velocity_path() -> None:
    # Wrong integration direction or dt changes the literal terminal latent action.
    velocity = _ConstantVelocity((0.5, 0.5))
    sampler = EulerFlowSampler(velocity)
    initial_noise = torch.tensor([[1.0, -1.0]], dtype=torch.float32)

    sample = sampler.sample(
        (torch.zeros(1, 2, dtype=torch.float32),),
        nfe=4,
        initial_noise=initial_noise,
        return_path=True,
    )

    torch.testing.assert_close(
        sample.latent_actions,
        torch.tensor([[1.5, -0.5]], dtype=torch.float32),
    )
    torch.testing.assert_close(sample.actions, torch.tanh(sample.latent_actions))
    assert sample.nfe == 4
    assert sample.path is not None
    assert sample.path.shape == (1, 5, 2)
    assert velocity.calls == 4


@pytest.mark.parametrize("nfe", [1, 2, 4, 8])
def test_euler_sampler_calls_velocity_exactly_nfe_times(nfe: int) -> None:
    velocity = _ConstantVelocity((0.0, 0.0))
    sampler = EulerFlowSampler(velocity)

    sample = sampler.sample(
        (torch.zeros(3, 2, dtype=torch.float32),),
        nfe=nfe,
        initial_noise=torch.zeros(3, 2, dtype=torch.float32),
    )

    assert sample.nfe == nfe
    assert velocity.calls == nfe
    assert sample.actions.shape == (3, 2)
    assert torch.all(sample.actions >= -1.0)
    assert torch.all(sample.actions <= 1.0)


def test_euler_sampler_reproduces_noise_from_explicit_generator() -> None:
    # Reading global RNG would make two same-seed experiment generators diverge.
    first_sampler = EulerFlowSampler(_ConstantVelocity((0.0, 0.0)))
    second_sampler = EulerFlowSampler(_ConstantVelocity((0.0, 0.0)))

    first = first_sampler.sample(
        _observations(),
        nfe=2,
        generator=torch.Generator().manual_seed(123),
    )
    second = second_sampler.sample(
        _observations(),
        nfe=2,
        generator=torch.Generator().manual_seed(123),
    )

    torch.testing.assert_close(first.latent_actions, second.latent_actions)
    torch.testing.assert_close(first.actions, second.actions)


def test_euler_sampler_rejects_invalid_nfe() -> None:
    sampler = EulerFlowSampler(_ConstantVelocity((0.0,)))

    with pytest.raises(ValueError, match="nfe"):
        sampler.sample(
            (torch.zeros(1, 2, dtype=torch.float32),),
            nfe=0,
            initial_noise=torch.zeros(1, 1, dtype=torch.float32),
        )
