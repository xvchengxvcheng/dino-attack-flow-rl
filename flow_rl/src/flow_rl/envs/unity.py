from __future__ import annotations

from collections.abc import Callable, Sequence
from os import PathLike
from pathlib import Path
from typing import TYPE_CHECKING, Any

import numpy as np
from mlagents_envs.base_env import (
    ActionTuple,
    BaseEnv,
    BehaviorSpec,
    DecisionSteps,
    TerminalSteps,
)
from mlagents_envs.environment import UnityEnvironment
from mlagents_envs.side_channel.side_channel import SideChannel

from flow_rl.envs.types import EnvStep

if TYPE_CHECKING:
    from flow_rl.envs.dino_protocol import DinoProtocol


EnvironmentFactory = Callable[..., BaseEnv]


class UnityEnvAdapter:
    """Strict continuous-action adapter over the ML-Agents Low-Level API."""

    def __init__(
        self,
        file_name: str | PathLike[str],
        *,
        worker_id: int = 0,
        seed: int = 0,
        no_graphics: bool = True,
        timeout_wait: int = 60,
        behavior_name: str | None = None,
        log_folder: str | PathLike[str] | None = None,
        side_channels: Sequence[SideChannel] | None = None,
        additional_args: Sequence[str] | None = None,
        protocol: DinoProtocol | None = None,
        environment_factory: EnvironmentFactory = UnityEnvironment,
    ) -> None:
        kwargs: dict[str, Any] = {
            "file_name": str(Path(file_name)),
            "worker_id": worker_id,
            "seed": seed,
            "no_graphics": no_graphics,
            "timeout_wait": timeout_wait,
            "side_channels": list(side_channels) if side_channels is not None else [],
            "additional_args": list(additional_args)
            if additional_args is not None
            else [],
        }
        if log_folder is not None:
            kwargs["log_folder"] = str(Path(log_folder).resolve())
        self._environment = environment_factory(**kwargs)
        self._requested_behavior_name = behavior_name
        self._protocol = protocol
        self._behavior_name: str | None = None
        self._behavior_spec: BehaviorSpec | None = None
        self._pending_agent_ids = np.empty(0, dtype=np.int64)
        self._closed = False

    @property
    def behavior_name(self) -> str:
        if self._behavior_name is None:
            raise RuntimeError("reset() must select a behavior before it is available")
        return self._behavior_name

    @property
    def continuous_action_size(self) -> int:
        if self._behavior_spec is None:
            raise RuntimeError("reset() must select a behavior before actions are available")
        return self._behavior_spec.action_spec.continuous_size

    @property
    def observation_shapes(self) -> tuple[tuple[int, ...], ...]:
        if self._behavior_spec is None:
            raise RuntimeError(
                "reset() must select a behavior before observations are available"
            )
        return tuple(tuple(spec.shape) for spec in self._behavior_spec.observation_specs)

    @property
    def pending_agent_ids(self) -> np.ndarray:
        pending = self._pending_agent_ids.copy()
        pending.setflags(write=False)
        return pending

    def reset(self) -> EnvStep:
        self._ensure_open()
        self._environment.reset()
        self._select_behavior()
        decision_steps, terminal_steps = self._environment.get_steps(self.behavior_name)
        return self._accept_steps(decision_steps, terminal_steps)

    def step(self, actions: np.ndarray) -> EnvStep:
        self._ensure_open()
        if self._behavior_spec is None:
            raise RuntimeError("reset() must be called before step()")
        current_decisions, _ = self._environment.get_steps(self.behavior_name)
        current_ids = np.asarray(current_decisions.agent_id, dtype=np.int64)
        if not np.array_equal(current_ids, self._pending_agent_ids):
            raise RuntimeError("DecisionSteps agent order changed before actions were submitted")

        validated_actions = self._validate_actions(actions)
        self._environment.set_actions(
            self.behavior_name,
            ActionTuple(continuous=validated_actions.copy()),
        )
        self._environment.step()
        decision_steps, terminal_steps = self._environment.get_steps(self.behavior_name)
        return self._accept_steps(decision_steps, terminal_steps)

    def close(self) -> None:
        if self._closed:
            return
        self._closed = True
        self._environment.close()

    def __enter__(self) -> "UnityEnvAdapter":
        self._ensure_open()
        return self

    def __exit__(self, exc_type: object, exc: object, traceback: object) -> None:
        self.close()

    def _ensure_open(self) -> None:
        if self._closed:
            raise RuntimeError("UnityEnvAdapter is closed")

    def _select_behavior(self) -> None:
        names = tuple(self._environment.behavior_specs.keys())
        requested = self._requested_behavior_name
        if requested is None:
            if len(names) != 1:
                raise ValueError(
                    "multiple behaviors require an explicit behavior_name"
                    if names
                    else "Unity environment did not expose a behavior"
                )
            selected = names[0]
        else:
            if requested in self._environment.behavior_specs:
                selected = requested
            else:
                qualified_matches = tuple(
                    name for name in names if name.partition("?")[0] == requested
                )
                if len(qualified_matches) == 1:
                    selected = qualified_matches[0]
                elif len(qualified_matches) > 1:
                    raise ValueError(
                        f"behavior base name {requested!r} matches multiple behaviors: "
                        f"{qualified_matches!r}"
                    )
                else:
                    raise ValueError(f"behavior {requested!r} is not available")
        spec = self._environment.behavior_specs[selected]
        action_spec = spec.action_spec
        if action_spec.continuous_size <= 0 or action_spec.discrete_size != 0:
            raise ValueError("UnityEnvAdapter requires a continuous-only action space")
        if self._protocol is not None:
            shapes = tuple(
                tuple(observation.shape) for observation in spec.observation_specs
            )
            self._protocol.validate_shapes(shapes)
            self._protocol.validate_action_size(action_spec.continuous_size)
        self._behavior_name = selected
        self._behavior_spec = spec

    def _accept_steps(
        self, decision_steps: DecisionSteps, terminal_steps: TerminalSteps
    ) -> EnvStep:
        self._pending_agent_ids = np.asarray(
            decision_steps.agent_id, dtype=np.int64
        ).copy()
        return _merge_steps(decision_steps, terminal_steps)

    def _validate_actions(self, actions: np.ndarray) -> np.ndarray:
        if not isinstance(actions, np.ndarray):
            raise TypeError("actions must be a numpy array")
        expected_shape = (
            self._pending_agent_ids.shape[0],
            self.continuous_action_size,
        )
        if actions.shape != expected_shape:
            raise ValueError(f"actions shape must be {expected_shape}, got {actions.shape}")
        if actions.dtype != np.float32:
            raise TypeError("continuous actions must have dtype float32")
        if not np.isfinite(actions).all():
            raise ValueError("continuous actions must contain only finite values")
        if np.any(actions < -1.0) or np.any(actions > 1.0):
            raise ValueError("continuous actions must be within [-1, 1]")
        return actions


def _merge_steps(
    decision_steps: DecisionSteps, terminal_steps: TerminalSteps
) -> EnvStep:
    if len(decision_steps.obs) != len(terminal_steps.obs):
        raise ValueError("decision and terminal steps expose different sensor counts")
    observations = tuple(
        np.concatenate((decision_obs, terminal_obs), axis=0)
        for decision_obs, terminal_obs in zip(
            decision_steps.obs, terminal_steps.obs
        )
    )
    decision_count = len(decision_steps)
    interrupted = np.asarray(terminal_steps.interrupted, dtype=bool)
    return EnvStep(
        agent_ids=np.concatenate(
            (decision_steps.agent_id, terminal_steps.agent_id), axis=0
        ),
        observations=observations,
        rewards=np.concatenate(
            (decision_steps.reward, terminal_steps.reward), axis=0
        ),
        terminated=np.concatenate(
            (np.zeros(decision_count, dtype=bool), np.logical_not(interrupted)),
            axis=0,
        ),
        truncated=np.concatenate(
            (np.zeros(decision_count, dtype=bool), interrupted), axis=0
        ),
    )
