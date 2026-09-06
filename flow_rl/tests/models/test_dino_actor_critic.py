from __future__ import annotations

from pathlib import Path

import torch

from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.models.fpo_policy import FlowActorCritic
from flow_rl.models.policy import GaussianActorCritic
from .dino_fixtures import make_structured_batch


MANIFEST_PATH = (
    Path(__file__).resolve().parents[2]
    / "configs"
    / "dino_attack_structured_set_v2.yaml"
)


def _protocol() -> DinoProtocol:
    return DinoProtocol.from_yaml(MANIFEST_PATH)


def _factory(protocol: DinoProtocol):
    return lambda: SetTransformerDinoEncoder(
        protocol,
        d_model=48,
        heads=4,
        inducing_points=8,
        layers=1,
        dropout=0.0,
        output_size=128,
    )


def test_structured_ppo_uses_independent_encoders_and_exact_action4_head() -> None:
    protocol = _protocol()
    policy = GaussianActorCritic(
        observation_shapes=protocol.observation_shapes,
        action_size=4,
        hidden_sizes=(128, 128),
        encoder_factory=_factory(protocol),
    )
    observations = make_structured_batch(batch_size=2)

    output = policy.act(
        observations,
        deterministic=False,
        generator=torch.Generator().manual_seed(81),
    )

    assert policy.actor.encoder is not policy.critic.encoder
    assert policy.actor.policy_head[0].in_features == 128
    assert policy.actor.policy_head[0].out_features == 128
    assert policy.actor.policy_head[2].out_features == 128
    assert policy.actor.mean_head.in_features == 128
    assert policy.actor.mean_head.out_features == 4
    assert policy.critic.value_head[0].in_features == 128
    assert policy.critic.value_head[0].out_features == 128
    assert policy.critic.value_head[2].out_features == 128
    assert policy.critic.value_head[4].out_features == 1
    assert output.actions.shape == (2, 4)
    assert output.values.shape == (2,)
    assert torch.isfinite(output.actions).all()
    assert torch.isfinite(output.values).all()
    parameter_count = sum(parameter.numel() for parameter in policy.parameters())
    assert 500_000 <= parameter_count <= 520_000


def test_structured_ppo_has_finite_actor_and_critic_gradients() -> None:
    protocol = _protocol()
    policy = GaussianActorCritic(
        observation_shapes=protocol.observation_shapes,
        action_size=4,
        hidden_sizes=(128, 128),
        encoder_factory=_factory(protocol),
    )
    observations = make_structured_batch(batch_size=2)
    sampled = policy.act(
        observations,
        deterministic=True,
    )
    log_probs, _entropy, values = policy.evaluate_actions(
        observations,
        sampled.actions.detach(),
    )

    (-log_probs.mean() + values.square().mean()).backward()

    actor_gradients = [
        parameter.grad for parameter in policy.actor.parameters() if parameter.grad is not None
    ]
    critic_gradients = [
        parameter.grad for parameter in policy.critic.parameters() if parameter.grad is not None
    ]
    assert actor_gradients and all(torch.isfinite(gradient).all() for gradient in actor_gradients)
    assert critic_gradients and all(torch.isfinite(gradient).all() for gradient in critic_gradients)


def test_structured_fpo_uses_independent_encoders_and_exact_292_input() -> None:
    protocol = _protocol()
    policy = FlowActorCritic(
        observation_shapes=protocol.observation_shapes,
        action_size=4,
        state_size=128,
        time_embedding_size=32,
        velocity_hidden_sizes=(128, 128),
        critic_hidden_sizes=(128, 128),
        encoder_factory=_factory(protocol),
    )
    observations = make_structured_batch(batch_size=2)

    velocity = policy.actor(
        observations,
        torch.zeros((2, 4), dtype=torch.float32),
        torch.full((2, 1), 0.5, dtype=torch.float32),
    )

    assert policy.actor.state_encoder is not policy.critic.encoder
    assert policy.actor.velocity_head[0].in_features == 164
    assert policy.actor.velocity_head[0].out_features == 128
    assert policy.actor.velocity_head[2].out_features == 128
    assert policy.actor.velocity_head[4].out_features == 4
    assert velocity.shape == (2, 4)
    assert torch.isfinite(velocity).all()


def test_fpo_environment_film_time_is_separate_from_flow_time_tau() -> None:
    protocol = _protocol()
    policy = FlowActorCritic(
        observation_shapes=protocol.observation_shapes,
        action_size=4,
        state_size=128,
        time_embedding_size=32,
        velocity_hidden_sizes=(128, 128),
        critic_hidden_sizes=(128, 128),
        encoder_factory=_factory(protocol),
    )
    observations = list(make_structured_batch(batch_size=2))
    observations[0] = torch.tensor(
        [[0.25, 0.4, 1.0, -0.2, 0.5], [0.75, 0.4, 1.0, 0.3, -0.5]],
        dtype=torch.float32,
    )
    tau = torch.tensor([[0.1], [0.9]], dtype=torch.float32)
    seen_environment_time: list[torch.Tensor] = []
    seen_flow_time: list[torch.Tensor] = []
    environment_hook = policy.actor.state_encoder.time_encoder.register_forward_pre_hook(
        lambda _module, args: seen_environment_time.append(args[0].detach().clone())
    )
    flow_hook = policy.actor.time_embedding.register_forward_pre_hook(
        lambda _module, args: seen_flow_time.append(args[0].detach().clone())
    )
    try:
        policy.actor(
            tuple(observations),
            torch.zeros((2, 4), dtype=torch.float32),
            tau,
        )
    finally:
        environment_hook.remove()
        flow_hook.remove()

    assert len(seen_environment_time) == 1
    assert len(seen_flow_time) == 1
    torch.testing.assert_close(
        seen_environment_time[0], observations[0][:, 0:1]
    )
    torch.testing.assert_close(seen_flow_time[0], tau)
    assert not torch.equal(seen_environment_time[0], seen_flow_time[0])


def test_flat_3dball_model_keys_keep_the_existing_layout() -> None:
    ppo = GaussianActorCritic(
        observation_shapes=((8,),),
        action_size=2,
        hidden_sizes=(128, 128),
    )
    fpo = FlowActorCritic(
        observation_shapes=((8,),),
        action_size=2,
        state_size=128,
        time_embedding_size=32,
        velocity_hidden_sizes=(128, 128),
        critic_hidden_sizes=(128, 128),
    )

    ppo_keys = set(ppo.state_dict())
    fpo_keys = set(fpo.state_dict())
    assert "actor.encoder.0.weight" in ppo_keys
    assert "actor.encoder.2.weight" in ppo_keys
    assert "actor.mean_head.weight" in ppo_keys
    assert "critic.encoder.0.weight" in ppo_keys
    assert "critic.value_head.weight" in ppo_keys
    assert not any("policy_head" in key for key in ppo_keys)
    assert "actor.state_encoder.network.0.weight" in fpo_keys
    assert "actor.velocity_head.0.weight" in fpo_keys
    assert "critic.encoder.0.weight" in fpo_keys
