from __future__ import annotations

import importlib

import numpy as np
import pytest


def _rollout_module():
    try:
        return importlib.import_module("flow_rl.data.rollout")
    except ModuleNotFoundError as exc:
        pytest.fail(f"rollout module is missing: {exc}")


def _transition(module, agent_id: int, reward: float):
    return module.Transition(
        agent_id=agent_id,
        observation=(np.asarray([reward], dtype=np.float32),),
        action=np.asarray([reward, -reward], dtype=np.float32),
        reward=reward,
        value=reward / 2.0,
        next_value=reward / 4.0,
        terminated=False,
        truncated=False,
    )


def test_rollout_tracks_interleaved_agents_without_batch_index_assumptions() -> None:
    rollout = _rollout_module()
    buffer = rollout.AgentRolloutBuffer()

    for transition in (
        _transition(rollout, 20, 2.0),
        _transition(rollout, 10, 1.0),
        _transition(rollout, 10, 1.5),
        _transition(rollout, 20, 2.5),
    ):
        buffer.append(transition)

    trajectories = buffer.trajectories()
    assert tuple(trajectories) == (20, 10)
    assert [item.reward for item in trajectories[20]] == [2.0, 2.5]
    assert [item.reward for item in trajectories[10]] == [1.0, 1.5]


def test_transition_copies_arrays_and_rejects_nonfinite_data() -> None:
    rollout = _rollout_module()
    observation = np.asarray([1.0], dtype=np.float32)
    transition = _transition(rollout, 5, 1.0)
    copied = rollout.Transition(
        agent_id=transition.agent_id,
        observation=(observation,),
        action=transition.action,
        reward=transition.reward,
        value=transition.value,
        next_value=transition.next_value,
        terminated=False,
        truncated=False,
    )
    observation[0] = 99.0
    np.testing.assert_array_equal(copied.observation[0], [1.0])
    assert not copied.observation[0].flags.writeable

    with pytest.raises(ValueError, match="finite"):
        rollout.Transition(
            agent_id=5,
            observation=(np.asarray([np.nan], dtype=np.float32),),
            action=np.asarray([0.0], dtype=np.float32),
            reward=0.0,
            value=0.0,
            next_value=0.0,
            terminated=False,
            truncated=False,
        )

