from __future__ import annotations

from pathlib import Path

import pytest
import torch

from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.models.state_encoder import StateEncoder
from .dino_fixtures import make_structured_batch, permute_valid_rows


MANIFEST_PATH = (
    Path(__file__).resolve().parents[2]
    / "configs"
    / "dino_attack_structured_set_v2.yaml"
)


@pytest.fixture
def protocol() -> DinoProtocol:
    return DinoProtocol.from_yaml(MANIFEST_PATH)


def test_set_transformer_returns_five_region_states_and_finite_output(
    protocol: DinoProtocol,
) -> None:
    encoder = SetTransformerDinoEncoder(protocol).eval()
    batch = make_structured_batch(batch_size=3)
    seen_time: list[torch.Tensor] = []
    hook = encoder.time_encoder.register_forward_pre_hook(
        lambda _module, inputs: seen_time.append(inputs[0].detach().clone())
    )

    try:
        region_states = encoder.encode_region_states(batch)
        state = encoder(batch)
    finally:
        hook.remove()

    assert isinstance(encoder, StateEncoder)
    assert encoder.observation_shapes == protocol.observation_shapes
    assert encoder.output_size == 128
    assert region_states.shape == (3, 5, 48)
    assert state.shape == (3, 128)
    assert state.dtype == torch.float32
    assert torch.isfinite(region_states).all()
    assert torch.isfinite(state).all()
    assert all(value.shape == (3, 1) for value in seen_time)
    assert all(torch.equal(value, batch[0][:, 0:1]) for value in seen_time)

    assert len(encoder.defense_isabs) == 1
    assert len(encoder.dinosaur_sabs) == 1
    assert encoder.defense_isabs[0].inducing_points.shape == (1, 8, 48)
    assert encoder.wall_type_embedding.shape == (1, 1, 48)
    assert encoder.guard_type_embedding.shape == (1, 1, 48)
    assert encoder.house_type_embedding.shape == (1, 1, 48)
    assert encoder.defense_cross_attention is not encoder.dinosaur_cross_attention
    assert encoder.region_fusion[0].in_features == 144
    assert encoder.region_fusion[0].out_features == 128
    assert encoder.region_fusion[2].out_features == 48
    assert encoder.output_fusion[0].in_features == 288
    assert encoder.output_fusion[0].out_features == 256
    assert encoder.output_fusion[2].out_features == 128
    assert encoder.dropout == 0.0
    assert torch.count_nonzero(encoder.defense_film.weight) == 0
    assert torch.count_nonzero(encoder.defense_film.bias) == 0
    assert torch.count_nonzero(encoder.dinosaur_film.weight) == 0
    assert torch.count_nonzero(encoder.dinosaur_film.bias) == 0


@pytest.mark.parametrize(
    ("stream_index", "permutation"),
    (
        (3, (1, 0, 2, 3, 4, 5, 6, 7, 8, 9, 10)),
        (5, (1, 0, 2, 3, 4, 5, 6, 7, 8, 9)),
    ),
)
def test_set_transformer_is_invariant_to_valid_entity_row_permutation(
    protocol: DinoProtocol,
    stream_index: int,
    permutation: tuple[int, ...],
) -> None:
    encoder = SetTransformerDinoEncoder(protocol).eval()
    batch = make_structured_batch(batch_size=3)
    permuted = permute_valid_rows(batch, stream_index, permutation)

    torch.testing.assert_close(
        encoder(batch),
        encoder(permuted),
        atol=1e-6,
        rtol=1e-6,
    )


def test_set_transformer_returns_finite_state_for_empty_guard_and_dino_sets(
    protocol: DinoProtocol,
) -> None:
    encoder = SetTransformerDinoEncoder(protocol).eval()
    batch = list(make_structured_batch(batch_size=2))
    batch[3] = batch[3].clone()
    batch[5] = batch[5].clone()
    batch[3][..., 0] = 0.0
    batch[5][..., 0] = 0.0

    region_states = encoder.encode_region_states(tuple(batch))
    state = encoder(tuple(batch))

    assert region_states.shape == (2, 5, 48)
    assert state.shape == (2, 128)
    assert torch.isfinite(region_states).all()
    assert torch.isfinite(state).all()


def test_set_transformer_reads_dynamic_regions_without_a_fixed_buffer(
    protocol: DinoProtocol,
) -> None:
    encoder = SetTransformerDinoEncoder(protocol).eval()
    batch = make_structured_batch(batch_size=2)
    changed = list(batch)
    changed[1] = changed[1].clone()
    changed[1][:, 0, 0] += 0.25

    region_states = encoder.encode_region_states(batch)
    changed_region_states = encoder.encode_region_states(tuple(changed))
    state = encoder(batch)
    changed_state = encoder(tuple(changed))

    assert "region_vertices" not in dict(encoder.named_buffers())
    assert region_states.shape == changed_region_states.shape == (2, 5, 48)
    assert state.shape == changed_state.shape == (2, 128)
    assert torch.isfinite(region_states).all()
    assert torch.isfinite(changed_region_states).all()
    assert torch.isfinite(state).all()
    assert torch.isfinite(changed_state).all()
    assert not torch.equal(region_states, changed_region_states)
    assert not torch.equal(state, changed_state)


def test_region_states_follow_the_order_of_dynamic_region_coordinates(
    protocol: DinoProtocol,
) -> None:
    encoder = SetTransformerDinoEncoder(protocol).eval()
    batch = make_structured_batch(batch_size=2)
    permutation = (4, 2, 0, 3, 1)
    permuted = list(batch)
    permuted[1] = batch[1][:, permutation, :].clone()

    region_states = encoder.encode_region_states(batch)
    permuted_region_states = encoder.encode_region_states(tuple(permuted))

    torch.testing.assert_close(
        permuted_region_states,
        region_states[:, permutation, :],
    )
