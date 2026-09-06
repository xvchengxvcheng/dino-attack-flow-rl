from __future__ import annotations

import importlib

import numpy as np
import pytest


def _random_policy_module():
    try:
        return importlib.import_module("flow_rl.cli.random_policy")
    except ModuleNotFoundError as exc:
        pytest.fail(f"random policy module is missing: {exc}")


def test_uniform_actions_are_seeded_float32_and_bounded() -> None:
    random_policy = _random_policy_module()

    first = random_policy.sample_uniform_actions(
        np.random.default_rng(123), agent_count=4, action_size=2
    )
    second = random_policy.sample_uniform_actions(
        np.random.default_rng(123), agent_count=4, action_size=2
    )

    np.testing.assert_array_equal(first, second)
    assert first.shape == (4, 2)
    assert first.dtype == np.float32
    assert np.all(first >= -1.0)
    assert np.all(first <= 1.0)

