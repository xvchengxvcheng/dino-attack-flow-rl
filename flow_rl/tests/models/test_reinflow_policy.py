from __future__ import annotations

import torch


def _policy():
    from flow_rl.models.reinflow_policy import ReinFlowActorCritic

    policy = ReinFlowActorCritic(
        observation_shapes=((2,),),
        action_size=1,
        state_size=4,
        time_embedding_size=4,
        velocity_hidden_sizes=(4,),
        critic_hidden_sizes=(4,),
        noise_hidden_sizes=(4,),
        nfe=2,
        min_noise_std=0.1,
        max_noise_std=0.4,
    )
    with torch.no_grad():
        for parameter in policy.actor.parameters():
            parameter.zero_()
        for parameter in policy.noise_head.noise_mlp.parameters():
            parameter.zero_()
    return policy


def test_learnable_noise_is_bounded_and_detaches_time_embedding() -> None:
    policy = _policy()
    observations = (torch.ones((3, 2), dtype=torch.float32),)
    sample_time = torch.full((3, 1), 0.25, dtype=torch.float32)

    std = policy.noise_head(observations, sample_time, step=0)

    assert std.shape == (3, 1)
    assert torch.all((0.1 <= std) & (std <= 0.4))
    std.sum().backward()
    assert any(
        parameter.grad is not None
        for parameter in policy.actor.state_encoder.parameters()
    )
    assert all(
        parameter.grad is None
        for parameter in policy.actor.time_embedding.parameters()
    )
    assert any(
        parameter.grad is not None
        for parameter in policy.noise_head.noise_mlp.parameters()
    )


def test_reinflow_chain_matches_injected_noise_and_eval_has_no_transition_noise() -> None:
    policy = _policy()
    observations = (torch.ones((1, 2), dtype=torch.float32),)
    initial = torch.tensor([[0.2]], dtype=torch.float32)
    transition = torch.tensor([[[1.0], [5.0]]], dtype=torch.float32)

    training = policy.act(
        observations,
        evaluation=False,
        generator=torch.Generator().manual_seed(1),
        initial_noise=initial,
        transition_noise=transition,
    )
    evaluation = policy.act(
        observations,
        evaluation=True,
        generator=torch.Generator().manual_seed(1),
        initial_noise=initial,
        transition_noise=transition,
    )

    torch.testing.assert_close(
        training.chains,
        torch.tensor([[[0.2], [0.4], [1.0]]], dtype=torch.float32),
    )
    torch.testing.assert_close(
        evaluation.chains,
        torch.tensor([[[0.2], [0.2], [0.2]]], dtype=torch.float32),
    )
    torch.testing.assert_close(training.actions, torch.tensor([[1.0]]))
    assert training.old_log_probs.shape == (1,)
    assert training.values.shape == (1,)
    assert training.nfe == 2
    assert torch.isfinite(training.old_log_probs).all()
    assert torch.isfinite(training.entropy_rate).all()


def test_reinflow_chain_repeats_with_explicit_generator_and_is_bounded() -> None:
    policy = _policy()
    observations = (torch.ones((5, 2), dtype=torch.float32),)

    left = policy.act(
        observations,
        evaluation=False,
        generator=torch.Generator().manual_seed(7),
    )
    right = policy.act(
        observations,
        evaluation=False,
        generator=torch.Generator().manual_seed(7),
    )

    torch.testing.assert_close(left.chains, right.chains)
    assert torch.all((-1.0 <= left.actions) & (left.actions <= 1.0))
