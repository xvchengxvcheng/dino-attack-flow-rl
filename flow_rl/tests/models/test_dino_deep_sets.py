from __future__ import annotations

import pytest
import torch

from flow_rl.models.dino_deep_sets import DeepSetsDinoEncoder
from flow_rl.models.state_encoder import FlatMLPStateEncoder, StateEncoder
from .dino_fixtures import (
    make_structured_batch,
    permute_valid_rows,
)


def test_deep_sets_returns_finite_default_state_shape() -> None:
    encoder = DeepSetsDinoEncoder()
    batch = make_structured_batch(batch_size=3)

    state = encoder(batch)

    assert isinstance(encoder, StateEncoder)
    assert encoder.output_size == 128
    assert encoder.observation_shapes == (
        (5,), (5, 8), (6, 5), (11, 7), (8, 6), (10, 7)
    )
    assert encoder.region_encoder[0].in_features == 8
    assert encoder.region_encoder[0].out_features == 32
    assert encoder.region_encoder[2].out_features == 16
    assert encoder.global_encoder[0].in_features == 5
    assert encoder.fusion[0].in_features == 656
    assert encoder.fusion[0].out_features == 256
    assert state.shape == (3, 128)
    assert state.dtype == torch.float32
    assert torch.isfinite(state).all()


@pytest.mark.parametrize(
    ("stream_index", "permutation"),
    (
        (3, (1, 0, 2, 3, 4, 5, 6, 7, 8, 9, 10)),
        (5, (1, 0, 2, 3, 4, 5, 6, 7, 8, 9)),
    ),
)
def test_deep_sets_is_invariant_to_valid_entity_row_permutation(
    stream_index: int,
    permutation: tuple[int, ...],
) -> None:
    encoder = DeepSetsDinoEncoder()
    batch = make_structured_batch(batch_size=3)
    permuted = permute_valid_rows(batch, stream_index, permutation)

    torch.testing.assert_close(
        encoder(batch),
        encoder(permuted),
        atol=1e-6,
        rtol=1e-6,
    )


def test_deep_sets_returns_finite_state_for_empty_guard_and_dino_sets() -> None:
    encoder = DeepSetsDinoEncoder()
    batch = list(make_structured_batch(batch_size=2))
    batch[3] = batch[3].clone()
    batch[5] = batch[5].clone()
    batch[3][..., 0] = 0.0
    batch[5][..., 0] = 0.0

    state = encoder(tuple(batch))

    assert state.shape == (2, 128)
    assert torch.isfinite(state).all()


@pytest.mark.parametrize(
    "mutation",
    ("stream_count", "wrong_shape", "wrong_dtype", "non_finite", "batch_mismatch"),
)
def test_deep_sets_rejects_invalid_structured_observations(mutation: str) -> None:
    encoder = DeepSetsDinoEncoder()
    batch = list(make_structured_batch(batch_size=2))
    if mutation == "stream_count":
        observations = tuple(batch[:-1])
    elif mutation == "wrong_shape":
        batch[1] = torch.zeros((2, 5, 7), dtype=torch.float32)
        observations = tuple(batch)
    elif mutation == "wrong_dtype":
        batch[3] = batch[3].to(torch.float64)
        observations = tuple(batch)
    elif mutation == "non_finite":
        batch[4] = batch[4].clone()
        batch[4][0, 0, 1] = torch.nan
        observations = tuple(batch)
    else:
        batch[5] = torch.zeros((3, 10, 7), dtype=torch.float32)
        observations = tuple(batch)

    with pytest.raises((TypeError, ValueError)):
        encoder(observations)


def test_deep_sets_changes_when_only_regions_change() -> None:
    encoder = DeepSetsDinoEncoder().eval()
    batch = make_structured_batch(batch_size=2)
    changed = list(batch)
    changed[1] = changed[1].clone()
    changed[1][:, 0, 0] += 0.25

    assert not torch.equal(encoder(batch), encoder(tuple(changed)))


def test_deep_sets_region_states_depend_only_on_dynamic_coordinates() -> None:
    encoder = DeepSetsDinoEncoder().eval()
    regions = torch.tensor(
        [[[0.1, 0.2, 0.3, 0.4, 0.5, 0.6, 0.7, 0.8]] * 5],
        dtype=torch.float32,
    )

    region_states = encoder.encode_region_states(regions)

    assert region_states.shape == (1, 5, 16)
    torch.testing.assert_close(
        region_states,
        region_states[:, 0:1, :].expand_as(region_states),
    )


def test_flat_mlp_state_encoder_keeps_generic_flat_observations_compatible() -> None:
    encoder = FlatMLPStateEncoder(
        observation_shapes=((8,),),
        output_size=128,
        hidden_sizes=(128,),
    )
    observations = (torch.arange(24, dtype=torch.float32).reshape(3, 8),)

    state = encoder(observations)

    assert isinstance(encoder, StateEncoder)
    assert encoder.observation_shapes == ((8,),)
    assert encoder.output_size == 128
    assert state.shape == (3, 128)
    assert torch.isfinite(state).all()
