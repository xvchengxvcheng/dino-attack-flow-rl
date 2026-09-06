from __future__ import annotations

from typing import Any

import torch
from torch import nn

from flow_rl.models.state_encoder import (
    StateEncoder,
    extract_entity_masks,
    validate_observations,
)


_DINO_OBSERVATION_SHAPES = ((5,), (5, 8), (6, 5), (11, 7), (8, 6), (10, 7))
_REGION_COUNT = 5
_REGION_STATE_SIZE = 16


def _entity_mlp(input_size: int) -> nn.Sequential:
    return nn.Sequential(
        nn.Linear(input_size, 64),
        nn.SiLU(),
        nn.Linear(64, 64),
        nn.SiLU(),
    )


def _masked_mean_and_max(
    encoded: torch.Tensor,
    mask: torch.Tensor,
) -> torch.Tensor:
    weights = mask.unsqueeze(-1)
    total = torch.sum(encoded * weights, dim=1)
    count = torch.sum(weights, dim=1)
    mean = total / count.clamp_min(1.0)

    valid = mask.to(dtype=torch.bool).unsqueeze(-1)
    masked = torch.where(
        valid,
        encoded,
        torch.full_like(encoded, torch.finfo(encoded.dtype).min),
    )
    maximum = torch.amax(masked, dim=1)
    has_valid_row = torch.any(valid, dim=1)
    maximum = torch.where(has_valid_row, maximum, torch.zeros_like(maximum))
    return torch.cat((mean, maximum), dim=1)


class DeepSetsDinoEncoder(StateEncoder):
    def __init__(self, output_size: int = 128) -> None:
        super().__init__(
            observation_shapes=_DINO_OBSERVATION_SHAPES,
            output_size=output_size,
        )
        self.wall_encoder = _entity_mlp(5)
        self.guard_encoder = _entity_mlp(7)
        self.house_encoder = _entity_mlp(6)
        self.dino_encoder = _entity_mlp(7)
        self.region_encoder = nn.Sequential(
            nn.Linear(8, 32),
            nn.SiLU(),
            nn.Linear(32, _REGION_STATE_SIZE),
            nn.SiLU(),
        )
        self.global_encoder = nn.Sequential(
            nn.Linear(5, 32),
            nn.SiLU(),
            nn.Linear(32, 64),
            nn.SiLU(),
        )
        self.fusion = nn.Sequential(
            nn.Linear(656, 256),
            nn.SiLU(),
            nn.Linear(256, self.output_size),
            nn.SiLU(),
        )

    def forward(self, observations: tuple[torch.Tensor, ...]) -> torch.Tensor:
        (
            global_observation,
            regions,
            walls,
            guards,
            houses,
            dinos,
        ) = validate_observations(observations, self.observation_shapes)
        masks = extract_entity_masks(observations, stream_indices=(2, 3, 4, 5))
        category_states = (
            _masked_mean_and_max(self.wall_encoder(walls), masks[0]),
            _masked_mean_and_max(self.guard_encoder(guards), masks[1]),
            _masked_mean_and_max(self.house_encoder(houses), masks[2]),
            _masked_mean_and_max(self.dino_encoder(dinos), masks[3]),
        )
        region_state = self.encode_region_states(regions).reshape(
            regions.shape[0],
            _REGION_COUNT * _REGION_STATE_SIZE,
        )
        global_state = self.global_encoder(global_observation)
        return self.fusion(
            torch.cat((*category_states, region_state, global_state), dim=1)
        )

    def encode_region_states(self, regions: torch.Tensor) -> torch.Tensor:
        if regions.ndim != 3 or tuple(regions.shape[1:]) != (_REGION_COUNT, 8):
            raise ValueError("regions must have shape (batch, 5, 8)")
        if regions.dtype != torch.float32:
            raise TypeError("regions must use float32")
        if not torch.isfinite(regions).all():
            raise ValueError("regions must contain only finite values")
        return self.region_encoder(regions)

    def checkpoint_metadata(self) -> dict[str, Any]:
        metadata = super().checkpoint_metadata()
        metadata["encoder_type"] = "deep_sets"
        metadata["region_state_size"] = _REGION_STATE_SIZE
        metadata["region_query_mode"] = "dynamic_geometry_only"
        return metadata
