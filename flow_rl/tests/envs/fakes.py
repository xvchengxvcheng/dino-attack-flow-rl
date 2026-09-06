from __future__ import annotations

from collections.abc import Mapping, Sequence

import numpy as np
from mlagents_envs.base_env import (
    ActionSpec,
    ActionTuple,
    BehaviorSpec,
    DecisionSteps,
    DimensionProperty,
    ObservationSpec,
    ObservationType,
    TerminalSteps,
)


def behavior_spec(
    observation_shapes: Sequence[tuple[int, ...]] = ((1,),),
    action_size: int = 2,
) -> BehaviorSpec:
    observations = [
        ObservationSpec(
            shape=shape,
            dimension_property=(DimensionProperty.NONE,) * len(shape),
            observation_type=ObservationType.DEFAULT,
            name=f"sensor_{index}",
        )
        for index, shape in enumerate(observation_shapes)
    ]
    return BehaviorSpec(observations, ActionSpec.create_continuous(action_size))


def decision_steps(
    spec: BehaviorSpec,
    agent_ids: Sequence[int],
    observations: Sequence[np.ndarray],
    rewards: Sequence[float] | None = None,
) -> DecisionSteps:
    count = len(agent_ids)
    return DecisionSteps(
        obs=[np.asarray(obs, dtype=np.float32) for obs in observations],
        reward=np.asarray(rewards if rewards is not None else [0.0] * count, dtype=np.float32),
        agent_id=np.asarray(agent_ids, dtype=np.int32),
        action_mask=None,
        group_id=np.zeros(count, dtype=np.int32),
        group_reward=np.zeros(count, dtype=np.float32),
    )


def terminal_steps(
    spec: BehaviorSpec,
    agent_ids: Sequence[int],
    observations: Sequence[np.ndarray],
    rewards: Sequence[float] | None = None,
    interrupted: Sequence[bool] | None = None,
) -> TerminalSteps:
    count = len(agent_ids)
    return TerminalSteps(
        obs=[np.asarray(obs, dtype=np.float32) for obs in observations],
        reward=np.asarray(rewards if rewards is not None else [0.0] * count, dtype=np.float32),
        interrupted=np.asarray(
            interrupted if interrupted is not None else [False] * count,
            dtype=bool,
        ),
        agent_id=np.asarray(agent_ids, dtype=np.int32),
        group_id=np.zeros(count, dtype=np.int32),
        group_reward=np.zeros(count, dtype=np.float32),
    )


class ScriptedUnityEnvironment:
    def __init__(
        self,
        specs: Mapping[str, BehaviorSpec],
        states: Mapping[str, Sequence[tuple[DecisionSteps, TerminalSteps]]],
    ) -> None:
        self.behavior_specs = dict(specs)
        self._states = {name: list(sequence) for name, sequence in states.items()}
        self._index = 0
        self.actions: list[tuple[str, ActionTuple]] = []
        self.reset_count = 0
        self.step_count = 0
        self.close_count = 0

    def reset(self) -> None:
        self.reset_count += 1
        self._index = 0

    def get_steps(self, behavior_name: str) -> tuple[DecisionSteps, TerminalSteps]:
        return self._states[behavior_name][self._index]

    def set_actions(self, behavior_name: str, actions: ActionTuple) -> None:
        self.actions.append(behavior_name_and_action(behavior_name, actions))

    def step(self) -> None:
        self.step_count += 1
        next_index = self._index + 1
        if next_index >= len(next(iter(self._states.values()))):
            raise RuntimeError("script exhausted")
        self._index = next_index

    def close(self) -> None:
        self.close_count += 1


def behavior_name_and_action(
    behavior_name: str, actions: ActionTuple
) -> tuple[str, ActionTuple]:
    return behavior_name, actions

