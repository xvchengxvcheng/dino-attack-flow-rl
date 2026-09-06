from __future__ import annotations

import copy

import torch

from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.models.policyflow_policy import PolicyFlowActorCritic


ROOT = __import__("pathlib").Path(__file__).resolve().parents[3]
PROTOCOL_PATH = ROOT / "flow_rl" / "configs" / "dino_attack_structured_set_v2.yaml"


def _observations(batch: int = 3) -> tuple[torch.Tensor, ...]:
    return (torch.linspace(-1.0, 1.0, batch * 4).reshape(batch, 4),)


def _policy(*, solver_steps: int = 2) -> PolicyFlowActorCritic:
    torch.manual_seed(71)
    return PolicyFlowActorCritic(
        observation_shapes=((4,),),
        action_size=2,
        state_size=8,
        time_embedding_size=8,
        velocity_hidden_sizes=(8,),
        critic_hidden_sizes=(8,),
        solver_steps=solver_steps,
    )


def test_midpoint_sampler_reports_actual_velocity_nfe_and_is_reproducible() -> None:
    policy = _policy(solver_steps=2)
    observations = _observations()
    initial = torch.tensor([[0.1, -0.2], [0.3, 0.4], [-0.5, 0.6]])
    first = policy.sample_prior(observations, initial_noise=initial)
    second = policy.sample_prior(observations, initial_noise=initial)
    assert first.solver_steps == 2
    assert first.velocity_nfe == 4
    assert torch.equal(first.latent_actions, second.latent_actions)


def test_actions_are_bounded_and_training_saves_official_auxiliaries() -> None:
    policy = _policy()
    output = policy.act(
        _observations(),
        evaluation=False,
        generator=torch.Generator().manual_seed(72),
    )
    assert output.actions.shape == (3, 2)
    assert torch.all(output.actions >= -1.0)
    assert torch.all(output.actions <= 1.0)
    assert output.flow_x0.shape == output.actions_prior.shape == output.delta_actions.shape
    assert output.old_delta_std.shape == (3, 2)
    assert output.old_delta_log_probs.shape == (3,)
    assert torch.isfinite(output.old_delta_log_probs).all()


def test_evaluation_keeps_initial_flow_noise_but_omits_delta_action_noise() -> None:
    policy = _policy()
    initial = torch.full((3, 2), 0.25)
    output = policy.act(
        _observations(),
        evaluation=True,
        generator=torch.Generator().manual_seed(73),
        initial_noise=initial,
    )
    prior = policy.sample_prior(_observations(), initial_noise=initial)
    assert torch.allclose(output.actions, torch.tanh(prior.latent_actions))
    assert torch.count_nonzero(output.delta_actions) == 0


def test_snapshot_is_frozen_until_explicit_refresh() -> None:
    policy = _policy()
    original = copy.deepcopy(policy.snapshot.state_dict())
    with torch.no_grad():
        next(policy.actor.parameters()).add_(1.0)
    assert all(torch.equal(original[key], value) for key, value in policy.snapshot.state_dict().items())
    policy.refresh_snapshot()
    assert all(
        torch.equal(policy.actor.state_dict()[key], value)
        for key, value in policy.snapshot.state_dict().items()
    )
    assert not any(parameter.requires_grad for parameter in policy.snapshot.parameters())


def test_learnable_variance_respects_official_log_std_bounds() -> None:
    policy = _policy()
    with torch.no_grad():
        policy.log_std.fill_(100.0)
    assert torch.all(policy.delta_std() <= torch.exp(torch.tensor(4.0)))
    with torch.no_grad():
        policy.log_std.fill_(-100.0)
    assert torch.all(policy.delta_std() >= torch.exp(torch.tensor(-20.0)))


def test_structured_actor_snapshot_and_critic_own_independent_encoders() -> None:
    protocol = DinoProtocol.from_yaml(PROTOCOL_PATH)

    def encoder_factory() -> SetTransformerDinoEncoder:
        return SetTransformerDinoEncoder(
            protocol, d_model=48, heads=4, inducing_points=8, layers=1,
            dropout=0.0, output_size=128,
        )

    policy = PolicyFlowActorCritic(
        observation_shapes=protocol.observation_shapes,
        action_size=protocol.action_size,
        state_size=128,
        time_embedding_size=32,
        velocity_hidden_sizes=(128, 128),
        critic_hidden_sizes=(128, 128),
        solver_steps=2,
        encoder_factory=encoder_factory,
    )

    assert isinstance(policy.actor.state_encoder, SetTransformerDinoEncoder)
    assert isinstance(policy.snapshot.state_encoder, SetTransformerDinoEncoder)
    assert isinstance(policy.critic.encoder, SetTransformerDinoEncoder)
    assert policy.actor.state_encoder is not policy.snapshot.state_encoder
    assert policy.actor.state_encoder is not policy.critic.encoder
    assert policy.snapshot.state_encoder is not policy.critic.encoder
