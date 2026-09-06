from __future__ import annotations

from pathlib import Path

import torch

from flow_rl.models.policyflow_policy import PolicyFlowActorCritic


def test_bc_initialization_loads_actor_and_refreshes_frozen_snapshot(flow_bc_fixture) -> None:
    from flow_rl.training.policyflow_trainer import initialize_actor_from_flow_bc

    _, checkpoint = flow_bc_fixture
    policy = PolicyFlowActorCritic(
        observation_shapes=((8,),), action_size=2, state_size=128,
        time_embedding_size=32, velocity_hidden_sizes=(128, 128),
        critic_hidden_sizes=(128, 128), solver_steps=2,
    )
    before = {key: value.clone() for key, value in policy.actor.state_dict().items()}
    initialize_actor_from_flow_bc(policy, checkpoint)
    assert any(not torch.equal(before[key], value) for key, value in policy.actor.state_dict().items())
    payload = torch.load(checkpoint, map_location="cpu", weights_only=False)
    assert all(
        torch.equal(policy.actor.state_dict()[key], value)
        for key, value in payload["model_state"].items()
    )
    assert all(
        torch.equal(policy.actor.state_dict()[key], value)
        for key, value in policy.snapshot.state_dict().items()
    )
