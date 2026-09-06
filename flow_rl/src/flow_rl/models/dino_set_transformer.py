from __future__ import annotations

from typing import Any

import torch
from torch import nn

from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.state_encoder import (
    StateEncoder,
    extract_entity_masks,
    validate_observations,
)


_DINO_OBSERVATION_SHAPES = ((5,), (5, 8), (6, 5), (11, 7), (8, 6), (10, 7))
_REGION_COUNT = 5
_GEOMETRY_FIELDS = 8


def _entity_mlp(input_size: int, d_model: int) -> nn.Sequential:
    return nn.Sequential(
        nn.Linear(input_size, d_model),
        nn.SiLU(),
        nn.Linear(d_model, d_model),
        nn.SiLU(),
    )


class _MultiheadAttentionBlock(nn.Module):
    def __init__(
        self,
        *,
        d_model: int,
        heads: int,
        dropout: float,
    ) -> None:
        super().__init__()
        self.attention = nn.MultiheadAttention(
            embed_dim=d_model,
            num_heads=heads,
            dropout=dropout,
            batch_first=True,
        )
        self.attention_dropout = nn.Dropout(dropout)
        self.attention_norm = nn.LayerNorm(d_model)
        self.feed_forward = nn.Sequential(
            nn.Linear(d_model, d_model),
            nn.SiLU(),
            nn.Dropout(dropout),
            nn.Linear(d_model, d_model),
        )
        self.feed_forward_dropout = nn.Dropout(dropout)
        self.feed_forward_norm = nn.LayerNorm(d_model)

    def forward(
        self,
        queries: torch.Tensor,
        keys: torch.Tensor,
        *,
        key_padding_mask: torch.Tensor | None = None,
    ) -> torch.Tensor:
        attended, _ = self.attention(
            queries,
            keys,
            keys,
            key_padding_mask=key_padding_mask,
            need_weights=False,
        )
        state = self.attention_norm(
            queries + self.attention_dropout(attended)
        )
        return self.feed_forward_norm(
            state + self.feed_forward_dropout(self.feed_forward(state))
        )


class _InducedSetAttentionBlock(nn.Module):
    def __init__(
        self,
        *,
        d_model: int,
        heads: int,
        inducing_points: int,
        dropout: float,
    ) -> None:
        super().__init__()
        self.inducing_points = nn.Parameter(
            torch.empty(1, inducing_points, d_model)
        )
        nn.init.normal_(self.inducing_points, mean=0.0, std=0.02)
        self.induce = _MultiheadAttentionBlock(
            d_model=d_model,
            heads=heads,
            dropout=dropout,
        )
        self.distribute = _MultiheadAttentionBlock(
            d_model=d_model,
            heads=heads,
            dropout=dropout,
        )

    def forward(
        self,
        tokens: torch.Tensor,
        key_padding_mask: torch.Tensor,
    ) -> torch.Tensor:
        inducing = self.inducing_points.expand(tokens.shape[0], -1, -1)
        induced_state = self.induce(
            inducing,
            tokens,
            key_padding_mask=key_padding_mask,
        )
        return self.distribute(tokens, induced_state)


class SetTransformerDinoEncoder(StateEncoder):
    def __init__(
        self,
        protocol: DinoProtocol,
        d_model: int = 48,
        heads: int = 4,
        inducing_points: int = 8,
        layers: int = 1,
        dropout: float = 0.0,
        output_size: int = 128,
    ) -> None:
        if not isinstance(protocol, DinoProtocol):
            raise TypeError("protocol must be a DinoProtocol")
        protocol.validate_shapes(_DINO_OBSERVATION_SHAPES)
        self._validate_hyperparameters(
            d_model=d_model,
            heads=heads,
            inducing_points=inducing_points,
            layers=layers,
            dropout=dropout,
        )
        super().__init__(
            observation_shapes=protocol.observation_shapes,
            output_size=output_size,
        )
        self._protocol = protocol
        self.d_model = d_model
        self.heads = heads
        self.inducing_points = inducing_points
        self.layers = layers
        self.dropout = float(dropout)

        self.wall_encoder = _entity_mlp(5, self.d_model)
        self.guard_encoder = _entity_mlp(7, self.d_model)
        self.house_encoder = _entity_mlp(6, self.d_model)
        self.dino_encoder = _entity_mlp(7, self.d_model)

        self.wall_type_embedding = nn.Parameter(
            torch.empty(1, 1, self.d_model)
        )
        self.guard_type_embedding = nn.Parameter(
            torch.empty(1, 1, self.d_model)
        )
        self.house_type_embedding = nn.Parameter(
            torch.empty(1, 1, self.d_model)
        )
        for embedding in (
            self.wall_type_embedding,
            self.guard_type_embedding,
            self.house_type_embedding,
        ):
            nn.init.normal_(embedding, mean=0.0, std=0.02)

        self.defense_empty_token = nn.Parameter(
            torch.zeros(1, 1, self.d_model)
        )
        self.dinosaur_empty_token = nn.Parameter(
            torch.zeros(1, 1, self.d_model)
        )
        self.defense_isabs = nn.ModuleList(
            self._make_isab() for _ in range(self.layers)
        )
        self.dinosaur_sabs = nn.ModuleList(
            self._make_sab() for _ in range(self.layers)
        )

        self.time_encoder = nn.Sequential(
            nn.Linear(1, 32),
            nn.SiLU(),
            nn.Linear(32, self.d_model),
            nn.SiLU(),
        )
        self.defense_film = nn.Linear(self.d_model, 2 * self.d_model)
        self.dinosaur_film = nn.Linear(self.d_model, 2 * self.d_model)
        nn.init.zeros_(self.defense_film.weight)
        nn.init.zeros_(self.defense_film.bias)
        nn.init.zeros_(self.dinosaur_film.weight)
        nn.init.zeros_(self.dinosaur_film.bias)

        self.geometry_encoder = nn.Sequential(
            nn.Linear(_GEOMETRY_FIELDS, 32),
            nn.SiLU(),
            nn.Linear(32, self.d_model),
            nn.SiLU(),
        )
        self.defense_cross_attention = nn.MultiheadAttention(
            embed_dim=self.d_model,
            num_heads=self.heads,
            dropout=self.dropout,
            batch_first=True,
        )
        self.dinosaur_cross_attention = nn.MultiheadAttention(
            embed_dim=self.d_model,
            num_heads=self.heads,
            dropout=self.dropout,
            batch_first=True,
        )
        self.region_fusion = nn.Sequential(
            nn.Linear(3 * self.d_model, 128),
            nn.SiLU(),
            nn.Linear(128, self.d_model),
            nn.SiLU(),
        )
        self.global_encoder = nn.Sequential(
            nn.Linear(5, 32),
            nn.SiLU(),
            nn.Linear(32, self.d_model),
            nn.SiLU(),
        )
        self.output_fusion = nn.Sequential(
            nn.Linear(_REGION_COUNT * self.d_model + self.d_model, 256),
            nn.SiLU(),
            nn.Linear(256, self.output_size),
            nn.SiLU(),
        )

    @staticmethod
    def _validate_hyperparameters(
        *,
        d_model: int,
        heads: int,
        inducing_points: int,
        layers: int,
        dropout: float,
    ) -> None:
        for name, value in (
            ("d_model", d_model),
            ("heads", heads),
            ("inducing_points", inducing_points),
            ("layers", layers),
        ):
            if isinstance(value, bool) or not isinstance(value, int):
                raise TypeError(f"{name} must be an integer")
            if value <= 0:
                raise ValueError(f"{name} must be positive")
        if d_model % heads != 0:
            raise ValueError("d_model must be divisible by heads")
        if isinstance(dropout, bool) or not isinstance(dropout, (int, float)):
            raise TypeError("dropout must be a number")
        if not 0.0 <= float(dropout) < 1.0:
            raise ValueError("dropout must be in [0, 1)")

    def _make_isab(self) -> _InducedSetAttentionBlock:
        return _InducedSetAttentionBlock(
            d_model=self.d_model,
            heads=self.heads,
            inducing_points=self.inducing_points,
            dropout=self.dropout,
        )

    def _make_sab(self) -> _MultiheadAttentionBlock:
        return _MultiheadAttentionBlock(
            d_model=self.d_model,
            heads=self.heads,
            dropout=self.dropout,
        )

    @staticmethod
    def _ensure_nonempty(
        tokens: torch.Tensor,
        valid_mask: torch.Tensor,
        empty_token: torch.Tensor,
    ) -> tuple[torch.Tensor, torch.Tensor]:
        key_padding_mask = ~valid_mask.to(dtype=torch.bool)
        fully_masked = torch.all(key_padding_mask, dim=1)
        if torch.any(fully_masked):
            empty_positions = torch.zeros_like(key_padding_mask)
            empty_positions[:, 0] = fully_masked
            tokens = torch.where(
                empty_positions.unsqueeze(-1),
                empty_token.expand(tokens.shape[0], tokens.shape[1], -1),
                tokens,
            )
            key_padding_mask = key_padding_mask.clone()
            key_padding_mask[fully_masked, 0] = False
        return tokens, key_padding_mask

    @staticmethod
    def _apply_film(
        tokens: torch.Tensor,
        film_parameters: torch.Tensor,
    ) -> torch.Tensor:
        gamma, beta = film_parameters.chunk(2, dim=-1)
        return (1.0 + gamma.unsqueeze(1)) * tokens + beta.unsqueeze(1)

    def _encode_region_and_global_states(
        self,
        observations: tuple[torch.Tensor, ...],
    ) -> tuple[torch.Tensor, torch.Tensor]:
        (
            global_observation,
            regions,
            walls,
            guards,
            houses,
            dinos,
        ) = validate_observations(observations, self.observation_shapes)
        wall_mask, guard_mask, house_mask, dino_mask = extract_entity_masks(
            observations,
            stream_indices=(2, 3, 4, 5),
        )

        defense_tokens = torch.cat(
            (
                self.wall_encoder(walls) + self.wall_type_embedding,
                self.guard_encoder(guards) + self.guard_type_embedding,
                self.house_encoder(houses) + self.house_type_embedding,
            ),
            dim=1,
        )
        defense_mask = torch.cat((wall_mask, guard_mask, house_mask), dim=1)
        defense_tokens, defense_padding = self._ensure_nonempty(
            defense_tokens,
            defense_mask,
            self.defense_empty_token,
        )

        dinosaur_tokens = self.dino_encoder(dinos)
        dinosaur_tokens, dinosaur_padding = self._ensure_nonempty(
            dinosaur_tokens,
            dino_mask,
            self.dinosaur_empty_token,
        )

        for layer in self.defense_isabs:
            defense_tokens = layer(defense_tokens, defense_padding)
        for layer in self.dinosaur_sabs:
            dinosaur_tokens = layer(
                dinosaur_tokens,
                dinosaur_tokens.clone(),
                key_padding_mask=dinosaur_padding,
            )

        time_state = self.time_encoder(global_observation[:, 0:1])
        defense_tokens = self._apply_film(
            defense_tokens,
            self.defense_film(time_state),
        )
        dinosaur_tokens = self._apply_film(
            dinosaur_tokens,
            self.dinosaur_film(time_state),
        )

        region_queries = self.geometry_encoder(regions)
        defense_region_state, _ = self.defense_cross_attention(
            region_queries,
            defense_tokens,
            defense_tokens,
            key_padding_mask=defense_padding,
            need_weights=False,
        )
        dinosaur_region_state, _ = self.dinosaur_cross_attention(
            region_queries,
            dinosaur_tokens,
            dinosaur_tokens,
            key_padding_mask=dinosaur_padding,
            need_weights=False,
        )
        region_states = self.region_fusion(
            torch.cat(
                (region_queries, defense_region_state, dinosaur_region_state),
                dim=-1,
            )
        )
        return region_states, self.global_encoder(global_observation)

    def encode_region_states(
        self,
        observations: tuple[torch.Tensor, ...],
    ) -> torch.Tensor:
        region_states, _ = self._encode_region_and_global_states(observations)
        return region_states

    def forward(self, observations: tuple[torch.Tensor, ...]) -> torch.Tensor:
        region_states, global_state = self._encode_region_and_global_states(
            observations
        )
        batch_size = region_states.shape[0]
        flattened_regions = region_states.reshape(
            batch_size,
            _REGION_COUNT * self.d_model,
        )
        return self.output_fusion(
            torch.cat((flattened_regions, global_state), dim=1)
        )

    def checkpoint_metadata(self) -> dict[str, Any]:
        metadata = super().checkpoint_metadata()
        metadata.update(
            {
                "encoder_type": "set_transformer",
                "d_model": self.d_model,
                "heads": self.heads,
                "inducing_points": self.inducing_points,
                "layers": self.layers,
                "dropout": self.dropout,
                "region_query_mode": "dynamic_geometry_only",
                "defense_block": "isab",
                "dinosaur_block": "sab",
                "region_fusion": "concat_query_defense_dinosaur",
                "output_fusion_hidden_size": 256,
                "protocol": self._protocol.checkpoint_metadata(),
            }
        )
        return metadata
