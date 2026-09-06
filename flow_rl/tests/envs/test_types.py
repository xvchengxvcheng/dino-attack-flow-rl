from __future__ import annotations

import importlib

import numpy as np
import pytest


def _env_step_type():
    try:
        module = importlib.import_module("flow_rl.envs.types")
    except ModuleNotFoundError as exc:
        pytest.fail(f"EnvStep module is missing: {exc}")
    return module.EnvStep


def _valid_kwargs() -> dict[str, object]:
    return {
        "agent_ids": np.asarray([7, 3], dtype=np.int32),
        "observations": (
            np.asarray([[1.0], [2.0]], dtype=np.float32),
            np.asarray([[3.0, 4.0], [5.0, 6.0]], dtype=np.float32),
        ),
        "rewards": np.asarray([0.25, -0.5], dtype=np.float32),
        "terminated": np.asarray([False, True], dtype=bool),
        "truncated": np.asarray([False, False], dtype=bool),
    }


def test_env_step_canonicalizes_ids_and_owns_immutable_arrays() -> None:
    EnvStep = _env_step_type()
    kwargs = _valid_kwargs()
    source_ids = kwargs["agent_ids"]

    step = EnvStep(**kwargs)
    source_ids[0] = 99

    assert step.agent_ids.dtype == np.int64
    np.testing.assert_array_equal(step.agent_ids, [7, 3])
    assert all(not array.flags.writeable for array in step.observations)
    assert not step.agent_ids.flags.writeable
    with pytest.raises(ValueError, match="read-only"):
        step.rewards[0] = 3.0


@pytest.mark.parametrize(
    ("field", "value", "message"),
    [
        ("agent_ids", np.asarray([[1]], dtype=np.int32), "agent_ids"),
        ("rewards", np.asarray([1.0], dtype=np.float32), "batch"),
        ("rewards", np.asarray([0.0, np.nan], dtype=np.float32), "finite"),
        ("terminated", np.asarray([0, 1], dtype=np.int32), "bool"),
        (
            "observations",
            (np.asarray([[1.0], [2.0]], dtype=np.float64),),
            "float32",
        ),
    ],
)
def test_env_step_rejects_invalid_shape_dtype_or_values(
    field: str, value: object, message: str
) -> None:
    EnvStep = _env_step_type()
    kwargs = _valid_kwargs()
    kwargs[field] = value

    with pytest.raises((TypeError, ValueError), match=message):
        EnvStep(**kwargs)


def test_env_step_rejects_simultaneous_termination_and_truncation() -> None:
    EnvStep = _env_step_type()
    kwargs = _valid_kwargs()
    kwargs["terminated"] = np.asarray([False, True], dtype=bool)
    kwargs["truncated"] = np.asarray([False, True], dtype=bool)

    with pytest.raises(ValueError, match="both terminated and truncated"):
        EnvStep(**kwargs)

