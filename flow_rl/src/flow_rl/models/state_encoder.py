from __future__ import annotations

import math
from abc import ABC, abstractmethod
from collections.abc import Sequence
from typing import TYPE_CHECKING, Any

import torch
from torch import nn

if TYPE_CHECKING:
    from flow_rl.envs.dino_protocol import DinoProtocol


def normalize_observation_shapes(
    observation_shapes: Sequence[tuple[int, ...]],
) -> tuple[tuple[int, ...], ...]:
    shapes = tuple(
        tuple(int(dimension) for dimension in shape)
        for shape in observation_shapes
    )
    if not shapes or any(not shape for shape in shapes):
        raise ValueError("observation_shapes must contain non-empty shapes")
    if any(dimension <= 0 for shape in shapes for dimension in shape):
        raise ValueError("observation dimensions must be positive")
    return shapes


def validate_observations(
    observations: tuple[torch.Tensor, ...],
    observation_shapes: Sequence[tuple[int, ...]],
) -> tuple[torch.Tensor, ...]:
    expected_shapes = tuple(observation_shapes)
    if not isinstance(observations, tuple):
        raise TypeError("observations must be a tuple")
    if len(observations) != len(expected_shapes):
        raise ValueError(
            f"observation stream count must be {len(expected_shapes)}"
        )

    batch_size: int | None = None
    device: torch.device | None = None
    for index, (observation, expected_shape) in enumerate(
        zip(observations, expected_shapes)
    ):
        if not isinstance(observation, torch.Tensor):
            raise TypeError(f"observations[{index}] must be a torch.Tensor")
        if observation.dtype != torch.float32:
            raise TypeError(f"observations[{index}] must have dtype float32")
        if observation.shape[1:] != expected_shape:
            raise ValueError(
                f"observations[{index}] feature shape must be {expected_shape}"
            )
        if batch_size is None:
            batch_size = observation.shape[0]
            device = observation.device
        elif observation.shape[0] != batch_size:
            raise ValueError("observation streams must share a batch dimension")
        elif observation.device != device:
            raise ValueError("observation streams must share a device")
        if not torch.isfinite(observation).all():
            raise ValueError(
                f"observations[{index}] must contain only finite values"
            )
    return observations


def extract_entity_masks(
    observations: tuple[torch.Tensor, ...],
    stream_indices: Sequence[int] | None = None,
) -> tuple[torch.Tensor, ...]:
    indices = (
        tuple(range(1, len(observations)))
        if stream_indices is None
        else tuple(stream_indices)
    )
    masks: list[torch.Tensor] = []
    for index in indices:
        if isinstance(index, bool) or not isinstance(index, int):
            raise TypeError("stream_indices must contain integers")
        if index < 0 or index >= len(observations):
            raise ValueError(f"stream index {index} is out of range")
        observation = observations[index]
        if observation.ndim != 3:
            raise ValueError(f"observations[{index}] must be an entity matrix")
        mask = observation[..., 0]
        if not torch.all((mask == 0.0) | (mask == 1.0)):
            raise ValueError(
                f"observations[{index}] valid_mask must contain only 0 or 1"
            )
        masks.append(mask)
    return tuple(masks)


class StateEncoder(nn.Module, ABC):
    def __init__(
        self,
        *,
        observation_shapes: Sequence[tuple[int, ...]],
        output_size: int,
    ) -> None:
        super().__init__()
        self.observation_shapes = normalize_observation_shapes(observation_shapes)
        if isinstance(output_size, bool) or not isinstance(output_size, int):
            raise TypeError("output_size must be an integer")
        if output_size <= 0:
            raise ValueError("output_size must be positive")
        self.output_size = output_size

    def checkpoint_metadata(self) -> dict[str, Any]:
        return {
            "encoder_type": type(self).__name__,
            "observation_shapes": self.observation_shapes,
            "output_size": self.output_size,
        }

    @abstractmethod
    def forward(self, observations: tuple[torch.Tensor, ...]) -> torch.Tensor:
        raise NotImplementedError


class FlatMLPStateEncoder(StateEncoder):
    def __init__(
        self,
        *,
        observation_shapes: Sequence[tuple[int, ...]],
        output_size: int = 128,
        hidden_sizes: Sequence[int] = (128,),
    ) -> None:
        super().__init__(
            observation_shapes=observation_shapes,
            output_size=output_size,
        )
        widths = tuple(int(size) for size in hidden_sizes)
        if any(size <= 0 for size in widths):
            raise ValueError("hidden_sizes must contain positive integers")

        input_size = sum(math.prod(shape) for shape in self.observation_shapes)
        layers: list[nn.Module] = []
        for width in (*widths, self.output_size):
            layers.extend((nn.Linear(input_size, width), nn.SiLU()))
            input_size = width
        self.network = nn.Sequential(*layers)

    def forward(self, observations: tuple[torch.Tensor, ...]) -> torch.Tensor:
        validated = validate_observations(observations, self.observation_shapes)
        flattened = tuple(
            observation.reshape(observation.shape[0], -1)
            for observation in validated
        )
        return self.network(torch.cat(flattened, dim=1))


def build_state_encoder(
    encoder_type: str,
    protocol: DinoProtocol | None = None,
    *,
    observation_shapes: Sequence[tuple[int, ...]] | None = None,
    output_size: int = 256,
    d_model: int = 64,
    heads: int = 4,
    inducing_points: int = 8,
    layers: int = 2,
    dropout: float = 0.05,
) -> StateEncoder:
    if encoder_type == "set_transformer":
        if protocol is None:
            raise ValueError("set_transformer encoder requires a DinoProtocol")
        from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder

        return SetTransformerDinoEncoder(
            protocol,
            d_model=d_model,
            heads=heads,
            inducing_points=inducing_points,
            layers=layers,
            dropout=dropout,
            output_size=output_size,
        )
    if encoder_type == "deep_sets":
        if protocol is None:
            raise ValueError("deep_sets encoder requires a DinoProtocol")
        protocol.validate_shapes(((5,), (5, 8), (6, 5), (11, 7), (8, 6), (10, 7)))
        from flow_rl.models.dino_deep_sets import DeepSetsDinoEncoder

        return DeepSetsDinoEncoder(output_size=output_size)
    if encoder_type == "flat":
        if observation_shapes is None:
            raise ValueError("flat encoder requires observation_shapes")
        return FlatMLPStateEncoder(
            observation_shapes=observation_shapes,
            output_size=output_size,
            hidden_sizes=(),
        )
    raise ValueError(f"unsupported encoder_type: {encoder_type!r}")
