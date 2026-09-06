from __future__ import annotations

from collections.abc import Sequence

import torch


def make_structured_batch(batch_size: int) -> tuple[torch.Tensor, ...]:
    if isinstance(batch_size, bool) or not isinstance(batch_size, int) or batch_size < 0:
        raise ValueError("batch_size must be a non-negative integer")

    global_rows = torch.tensor(
        [[0.75, 0.40, 1.0, -0.25, 0.50]],
        dtype=torch.float32,
    ).repeat(batch_size, 1)
    regions = torch.tensor(
        [
            (-0.90, -0.80, -0.60, -0.80, -0.55, -0.50, -0.85, -0.45),
            (-0.50, -0.75, -0.15, -0.65, -0.10, -0.30, -0.45, -0.25),
            (-0.10, -0.60, 0.20, -0.55, 0.25, -0.15, -0.05, -0.10),
            (0.25, -0.45, 0.55, -0.40, 0.60, -0.05, 0.30, 0.00),
            (0.60, -0.25, 0.90, -0.20, 0.85, 0.25, 0.55, 0.20),
        ],
        dtype=torch.float32,
    ).unsqueeze(0).repeat(batch_size, 1, 1)
    walls = _padded_stream(
        batch_size,
        row_count=6,
        rows=(
            (1.0, 1.0, -0.8, -0.4, 1.0),
            (1.0, 0.0, -0.2, 0.1, 0.0),
            (1.0, 1.0, 0.6, -0.1, 0.5),
            (1.0, 0.0, 0.8, 0.4, 0.0),
            (1.0, 1.0, 0.1, 0.7, 0.9),
            (1.0, 0.0, -0.5, 0.6, 0.0),
        ),
    )
    guards = _padded_stream(
        batch_size,
        row_count=11,
        rows=(
            (1.0, 1.0, 0.0, -0.4, 0.2, 0.8, 1.0),
            (1.0, 0.0, 1.0, 0.1, 0.4, 0.6, 0.0),
            (1.0, 1.0, 0.0, 0.5, -0.2, 1.0, 0.0),
        ),
    )
    houses = _padded_stream(
        batch_size,
        row_count=8,
        rows=(
            (1.0, 1.0, -0.3, 0.2, 1.0, 100.0 / 325.0),
            (1.0, 0.0, 0.2, 0.3, 0.0, 150.0 / 325.0),
            (1.0, 1.0, 0.4, 0.5, 0.7, 225.0 / 325.0),
            (1.0, 1.0, -0.1, 0.7, 0.9, 1.0),
            (1.0, 0.0, 0.6, 0.8, 0.0, 100.0 / 325.0),
            (1.0, 1.0, -0.7, 0.1, 0.5, 150.0 / 325.0),
            (1.0, 1.0, 0.8, -0.1, 0.4, 225.0 / 325.0),
            (1.0, 1.0, 0.0, -0.5, 0.8, 1.0),
        ),
    )
    dinos = _padded_stream(
        batch_size,
        row_count=10,
        rows=(
            (1.0, 1.0, 0.0, 0.0, -0.7, -0.6, 1.0),
            (1.0, 0.0, 1.0, 0.0, 0.0, -0.4, 0.75),
            (1.0, 0.0, 0.0, 1.0, 0.6, 0.2, 0.5),
        ),
    )
    return global_rows, regions, walls, guards, houses, dinos


def permute_valid_rows(
    observations: tuple[torch.Tensor, ...],
    stream_index: int,
    permutation: Sequence[int],
) -> tuple[torch.Tensor, ...]:
    if stream_index not in (2, 3, 4, 5):
        raise ValueError("stream_index must select an entity stream")
    stream = observations[stream_index]
    order = tuple(int(index) for index in permutation)
    if len(order) != stream.shape[1] or set(order) != set(range(stream.shape[1])):
        raise ValueError("permutation must contain every row index exactly once")

    permuted = stream[:, order, :].clone()
    if not torch.equal(permuted[..., 0], stream[..., 0]):
        raise ValueError("permutation must not move valid rows into padding positions")
    result = list(observations)
    result[stream_index] = permuted
    return tuple(result)


def _padded_stream(
    batch_size: int,
    *,
    row_count: int,
    rows: tuple[tuple[float, ...], ...],
) -> torch.Tensor:
    width = len(rows[0])
    stream = torch.zeros((batch_size, row_count, width), dtype=torch.float32)
    if rows:
        values = torch.tensor(rows, dtype=torch.float32)
        stream[:, : values.shape[0], :] = values
    return stream
