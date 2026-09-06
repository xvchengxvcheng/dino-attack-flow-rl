from __future__ import annotations

from collections.abc import Callable, Sequence

import torch
from torch import nn

from flow_rl.models.gaussian import _build_encoder, _feature_size, _validate_shapes, flatten_observations
from flow_rl.models.state_encoder import StateEncoder


class ValueCritic(nn.Module):
    def __init__(
        self,
        *,
        observation_shapes: Sequence[tuple[int, ...]],
        hidden_sizes: Sequence[int] = (128, 128),
        encoder_factory: Callable[[], StateEncoder] | None = None,
    ) -> None:
        super().__init__()
        self.observation_shapes = _validate_shapes(observation_shapes)
        widths = tuple(int(size) for size in hidden_sizes)
        self._structured_encoder = encoder_factory is not None
        if encoder_factory is None:
            self.encoder, encoded_size = _build_encoder(
                _feature_size(self.observation_shapes),
                widths,
            )
            self.value_head = nn.Linear(encoded_size, 1)
        else:
            encoder = encoder_factory()
            if not isinstance(encoder, StateEncoder):
                raise TypeError("encoder_factory must return a StateEncoder")
            if encoder.observation_shapes != self.observation_shapes:
                raise ValueError("encoder observation_shapes do not match critic")
            self.encoder = encoder
            layers: list[nn.Module] = []
            input_size = encoder.output_size
            for width in widths:
                if width <= 0:
                    raise ValueError("hidden sizes must be positive")
                layers.extend((nn.Linear(input_size, width), nn.SiLU()))
                input_size = width
            layers.append(nn.Linear(input_size, 1))
            self.value_head = nn.Sequential(*layers)

    def forward(self, observations: tuple[torch.Tensor, ...]) -> torch.Tensor:
        if len(observations) != len(self.observation_shapes):
            raise ValueError("observation stream count does not match critic")
        for index, (observation, shape) in enumerate(
            zip(observations, self.observation_shapes)
        ):
            if observation.shape[1:] != shape:
                raise ValueError(
                    f"observations[{index}] feature shape must be {shape}"
                )
        if self._structured_encoder:
            encoded = self.encoder(observations)
        else:
            encoded = self.encoder(flatten_observations(observations))
        return self.value_head(encoded).squeeze(-1)
