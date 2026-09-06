from __future__ import annotations

import importlib
from pathlib import Path

import numpy as np
import pytest

from .fakes import (
    ScriptedUnityEnvironment,
    behavior_spec,
    decision_steps,
    terminal_steps,
)


def _adapter_type():
    try:
        module = importlib.import_module("flow_rl.envs.unity")
    except ModuleNotFoundError as exc:
        pytest.fail(f"Unity adapter module is missing: {exc}")
    return module.UnityEnvAdapter


def _scripted_environment() -> ScriptedUnityEnvironment:
    spec = behavior_spec(observation_shapes=((1,), (2,)), action_size=2)
    states = [
        (
            decision_steps(
                spec,
                [20, 10],
                [
                    np.asarray([[2.0], [1.0]]),
                    np.asarray([[20.0, 21.0], [10.0, 11.0]]),
                ],
                [0.2, 0.1],
            ),
            terminal_steps(
                spec,
                [30, 40],
                [
                    np.asarray([[3.0], [4.0]]),
                    np.asarray([[30.0, 31.0], [40.0, 41.0]]),
                ],
                [3.0, 4.0],
                [False, True],
            ),
        ),
        (
            decision_steps(
                spec,
                [10],
                [np.asarray([[1.5]]), np.asarray([[15.0, 16.0]])],
                [0.5],
            ),
            terminal_steps(
                spec,
                [],
                [np.empty((0, 1)), np.empty((0, 2))],
            ),
        ),
        (
            decision_steps(
                spec,
                [20, 10],
                [
                    np.asarray([[2.5], [1.6]]),
                    np.asarray([[25.0, 26.0], [16.0, 17.0]]),
                ],
            ),
            terminal_steps(
                spec,
                [],
                [np.empty((0, 1)), np.empty((0, 2))],
            ),
        ),
    ]
    return ScriptedUnityEnvironment({"3DBall?team=0": spec}, {"3DBall?team=0": states})


def _adapter(fake: ScriptedUnityEnvironment, behavior_name: str | None = None):
    UnityEnvAdapter = _adapter_type()
    return UnityEnvAdapter(
        file_name="unused.exe",
        behavior_name=behavior_name,
        environment_factory=lambda **_: fake,
    )


def _dino_protocol():
    module = importlib.import_module("flow_rl.envs.dino_protocol")
    manifest = (
        Path(__file__).resolve().parents[2]
        / "configs"
        / "dino_attack_structured_set_v2.yaml"
    )
    return module.DinoProtocol.from_yaml(manifest)


def _dino_environment(
    observation_shapes=((5,), (5, 8), (6, 5), (11, 7), (8, 6), (10, 7)),
    action_size: int = 4,
) -> ScriptedUnityEnvironment:
    spec = behavior_spec(
        observation_shapes=observation_shapes,
        action_size=action_size,
    )
    observations = [
        np.arange(np.prod(shape), dtype=np.float32).reshape((1, *shape))
        for shape in observation_shapes
    ]
    empty_observations = [
        np.empty((0, *shape), dtype=np.float32) for shape in observation_shapes
    ]
    state = (
        decision_steps(spec, [7], observations),
        terminal_steps(spec, [], empty_observations),
    )
    return ScriptedUnityEnvironment(
        {"DinoAttackPlanner?team=0": spec},
        {"DinoAttackPlanner?team=0": [state]},
    )


def test_reset_preserves_sensor_tuple_order_and_maps_terminal_flags() -> None:
    fake = _scripted_environment()
    adapter = _adapter(fake)

    step = adapter.reset()

    assert adapter.behavior_name == "3DBall?team=0"
    assert adapter.continuous_action_size == 2
    np.testing.assert_array_equal(adapter.pending_agent_ids, [20, 10])
    np.testing.assert_array_equal(step.agent_ids, [20, 10, 30, 40])
    np.testing.assert_allclose(step.observations[0], [[2.0], [1.0], [3.0], [4.0]])
    np.testing.assert_allclose(
        step.observations[1],
        [[20.0, 21.0], [10.0, 11.0], [30.0, 31.0], [40.0, 41.0]],
    )
    np.testing.assert_array_equal(step.terminated, [False, False, True, False])
    np.testing.assert_array_equal(step.truncated, [False, False, False, True])


def test_dino_protocol_validates_llapi_shape_order_and_action_size_on_reset() -> None:
    protocol = _dino_protocol()
    valid = _dino_environment()
    adapter = _adapter_type()(
        file_name="unused.exe",
        behavior_name="DinoAttackPlanner",
        protocol=protocol,
        environment_factory=lambda **_: valid,
    )

    step = adapter.reset()

    assert adapter.observation_shapes == protocol.observation_shapes
    assert tuple(observation.shape for observation in step.observations) == (
        (1, 5),
        (1, 5, 8),
        (1, 6, 5),
        (1, 11, 7),
        (1, 8, 6),
        (1, 10, 7),
    )

    v1 = _dino_environment(
        observation_shapes=((3,), (6, 5), (8, 7), (8, 6), (10, 7))
    )
    with pytest.raises(ValueError, match="observation shapes"):
        _adapter_type()(
            file_name="unused.exe",
            protocol=protocol,
            environment_factory=lambda **_: v1,
        ).reset()

    reordered = _dino_environment(
        observation_shapes=((5,), (6, 5), (5, 8), (11, 7), (8, 6), (10, 7))
    )
    with pytest.raises(ValueError, match="observation shapes"):
        _adapter_type()(
            file_name="unused.exe",
            protocol=protocol,
            environment_factory=lambda **_: reordered,
        ).reset()

    wrong_shape = _dino_environment(
        observation_shapes=((5,), (5, 8), (6, 5), (8, 7), (8, 6), (10, 7))
    )
    with pytest.raises(ValueError, match="observation shapes"):
        _adapter_type()(
            file_name="unused.exe",
            protocol=protocol,
            environment_factory=lambda **_: wrong_shape,
        ).reset()

    wrong_action = _dino_environment(action_size=6)
    with pytest.raises(ValueError, match="action size"):
        _adapter_type()(
            file_name="unused.exe",
            protocol=protocol,
            environment_factory=lambda **_: wrong_action,
        ).reset()


def test_step_submits_actions_in_pending_order_and_tracks_reappearance() -> None:
    fake = _scripted_environment()
    adapter = _adapter(fake)
    adapter.reset()

    adapter.step(np.asarray([[0.2, -0.2], [0.1, -0.1]], dtype=np.float32))
    np.testing.assert_array_equal(adapter.pending_agent_ids, [10])
    adapter.step(np.asarray([[0.5, -0.5]], dtype=np.float32))

    assert fake.actions[0][0] == "3DBall?team=0"
    np.testing.assert_allclose(
        fake.actions[0][1].continuous,
        [[0.2, -0.2], [0.1, -0.1]],
    )
    np.testing.assert_array_equal(adapter.pending_agent_ids, [20, 10])


@pytest.mark.parametrize(
    ("actions", "message"),
    [
        (np.zeros((2, 3), dtype=np.float32), "shape"),
        (np.zeros((2, 2), dtype=np.float64), "float32"),
        (np.asarray([[0.0, np.nan], [0.0, 0.0]], dtype=np.float32), "finite"),
        (np.asarray([[0.0, 1.01], [0.0, 0.0]], dtype=np.float32), r"\[-1, 1\]"),
    ],
)
def test_step_rejects_invalid_continuous_actions(
    actions: np.ndarray, message: str
) -> None:
    adapter = _adapter(_scripted_environment())
    adapter.reset()

    with pytest.raises((TypeError, ValueError), match=message):
        adapter.step(actions)


def test_behavior_selection_requires_explicit_name_when_multiple_exist() -> None:
    spec = behavior_spec()
    state = (
        decision_steps(spec, [1], [np.asarray([[0.0]])]),
        terminal_steps(spec, [], [np.empty((0, 1))]),
    )
    fake = ScriptedUnityEnvironment(
        {"alpha": spec, "beta": spec},
        {"alpha": [state], "beta": [state]},
    )

    with _adapter(fake) as adapter:
        with pytest.raises(ValueError, match="multiple behaviors"):
            adapter.reset()

    explicit = _adapter(fake, behavior_name="beta")
    explicit.reset()
    assert explicit.behavior_name == "beta"


def test_behavior_selection_accepts_base_name_without_team_suffix() -> None:
    spec = behavior_spec()
    qualified_name = "DinoAttackPlanner?team=0"
    state = (
        decision_steps(spec, [1], [np.asarray([[0.0]])]),
        terminal_steps(spec, [], [np.empty((0, 1))]),
    )
    fake = ScriptedUnityEnvironment(
        {qualified_name: spec},
        {qualified_name: [state]},
    )

    adapter = _adapter(fake, behavior_name="DinoAttackPlanner")
    adapter.reset()

    assert adapter.behavior_name == qualified_name


def test_context_manager_closes_once_when_body_raises() -> None:
    fake = _scripted_environment()

    with pytest.raises(RuntimeError, match="policy failed"):
        with _adapter(fake) as adapter:
            adapter.reset()
            raise RuntimeError("policy failed")

    adapter.close()
    assert fake.close_count == 1


def test_reset_rejects_non_float32_observations_from_llapi() -> None:
    spec = behavior_spec()
    decisions = decision_steps(spec, [1], [np.asarray([[0.0]])])
    decisions.obs[0] = decisions.obs[0].astype(np.float64)
    state = (
        decisions,
        terminal_steps(spec, [], [np.empty((0, 1))]),
    )
    fake = ScriptedUnityEnvironment({"test": spec}, {"test": [state]})

    with pytest.raises(TypeError, match="float32"):
        _adapter(fake).reset()


def test_adapter_passes_side_channels_and_additional_args_to_llapi_factory() -> None:
    fake = _scripted_environment()
    captured: dict[str, object] = {}
    side_channel = object()

    def factory(**kwargs):
        captured.update(kwargs)
        return fake

    UnityEnvAdapter = _adapter_type()
    adapter = UnityEnvAdapter(
        file_name="unused.exe",
        side_channels=[side_channel],
        additional_args=["--test-argument"],
        environment_factory=factory,
    )
    adapter.close()

    assert captured["side_channels"] == [side_channel]
    assert captured["additional_args"] == ["--test-argument"]
