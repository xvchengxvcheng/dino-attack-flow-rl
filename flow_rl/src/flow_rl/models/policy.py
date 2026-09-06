from __future__ import annotations

from collections.abc import Callable, Sequence
from dataclasses import dataclass

import torch
from torch import nn

from flow_rl.models.critic import ValueCritic
from flow_rl.models.gaussian import TanhGaussianActor
from flow_rl.models.state_encoder import StateEncoder


@dataclass(frozen=True)
class PolicyOutput:
    actions: torch.Tensor
    pre_tanh: torch.Tensor
    log_probs: torch.Tensor
    values: torch.Tensor
    entropy: torch.Tensor


class GaussianActorCritic(nn.Module):
    def __init__(
        self,
        *,
        observation_shapes: Sequence[tuple[int, ...]],
        action_size: int,
        hidden_sizes: Sequence[int] = (128, 128),
        encoder_factory: Callable[[], StateEncoder] | None = None,
    ) -> None:
        super().__init__()
        self.actor = TanhGaussianActor(
            observation_shapes=observation_shapes,
            action_size=action_size,
            hidden_sizes=hidden_sizes,
            encoder_factory=encoder_factory,
        )
        self.critic = ValueCritic(
            observation_shapes=observation_shapes,
            hidden_sizes=hidden_sizes,
            encoder_factory=encoder_factory,
        )

    def act(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        deterministic: bool,
        generator: torch.Generator | None = None,
    ) -> PolicyOutput:
        sample = self.actor.sample(
            observations,
            deterministic=deterministic,
            generator=generator,
        )
        values = self.critic(observations)
        return PolicyOutput(
            actions=sample.actions,
            pre_tanh=sample.pre_tanh,
            log_probs=sample.log_probs,
            values=values,
            entropy=sample.entropy,
        )

    def evaluate_actions(
        self,
        observations: tuple[torch.Tensor, ...],
        actions: torch.Tensor,
    ) -> tuple[torch.Tensor, torch.Tensor, torch.Tensor]:
        log_probs, entropy = self.actor.evaluate_actions(observations, actions)
        values = self.critic(observations)
        return log_probs, entropy, values
