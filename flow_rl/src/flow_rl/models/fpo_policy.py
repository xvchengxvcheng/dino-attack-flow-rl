from __future__ import annotations

from collections.abc import Callable, Sequence
from dataclasses import dataclass

import torch
from torch import nn

from flow_rl.algorithms.fpo import conditional_flow_matching_loss
from flow_rl.models.critic import ValueCritic
from flow_rl.models.flow import ConditionalVelocityMLP, EulerFlowSampler
from flow_rl.models.state_encoder import StateEncoder


@dataclass(frozen=True)
class FPOPolicyOutput:
    actions: torch.Tensor
    latent_actions: torch.Tensor
    loss_eps: torch.Tensor
    loss_t: torch.Tensor
    old_cfm_losses: torch.Tensor
    values: torch.Tensor
    nfe: int


class FlowActorCritic(nn.Module):
    def __init__(
        self,
        *,
        observation_shapes: Sequence[tuple[int, ...]],
        action_size: int,
        state_size: int = 128,
        time_embedding_size: int = 32,
        velocity_hidden_sizes: Sequence[int] = (128, 128),
        critic_hidden_sizes: Sequence[int] = (128, 128),
        encoder_factory: Callable[[], StateEncoder] | None = None,
    ) -> None:
        super().__init__()
        self.actor = ConditionalVelocityMLP(
            observation_shapes=observation_shapes,
            action_size=action_size,
            state_size=state_size,
            time_embedding_size=time_embedding_size,
            hidden_sizes=velocity_hidden_sizes,
            state_encoder=None if encoder_factory is None else encoder_factory(),
        )
        self.critic = ValueCritic(
            observation_shapes=observation_shapes,
            hidden_sizes=critic_hidden_sizes,
            encoder_factory=encoder_factory,
        )
        self.sampler = EulerFlowSampler(self.actor)
        self.action_size = int(action_size)

    def act(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        nfe: int,
        num_fpo_samples: int,
        generator: torch.Generator,
    ) -> FPOPolicyOutput:
        if isinstance(num_fpo_samples, bool) or not isinstance(num_fpo_samples, int) or num_fpo_samples <= 0:
            raise ValueError("num_fpo_samples must be a positive integer")
        sample = self.sampler.sample(
            observations,
            nfe=nfe,
            generator=generator,
        )
        batch_size = sample.latent_actions.shape[0]
        loss_eps = torch.randn(
            (batch_size, num_fpo_samples, self.action_size),
            dtype=torch.float32,
            device=sample.latent_actions.device,
            generator=generator,
        )
        loss_t = torch.rand(
            (batch_size, num_fpo_samples, 1),
            dtype=torch.float32,
            device=sample.latent_actions.device,
            generator=generator,
        )
        old_cfm_losses = conditional_flow_matching_loss(
            self.actor,
            observations,
            sample.latent_actions,
            loss_eps,
            loss_t,
        ).detach()
        values = self.critic(observations)
        return FPOPolicyOutput(
            actions=sample.actions,
            latent_actions=sample.latent_actions,
            loss_eps=loss_eps,
            loss_t=loss_t,
            old_cfm_losses=old_cfm_losses,
            values=values,
            nfe=nfe,
        )
