from __future__ import annotations

import importlib

import numpy as np
import pytest


def _compute_gae(*args, **kwargs):
    try:
        module = importlib.import_module("flow_rl.data.gae")
    except ModuleNotFoundError as exc:
        pytest.fail(f"GAE module is missing: {exc}")
    return module.compute_gae(*args, **kwargs)


def test_natural_termination_blocks_bootstrap_but_prior_trace_continues() -> None:
    advantages, returns = _compute_gae(
        rewards=np.asarray([1.0, 2.0], dtype=np.float32),
        values=np.asarray([0.5, 0.25], dtype=np.float32),
        next_values=np.asarray([0.25, 10.0], dtype=np.float32),
        terminated=np.asarray([False, True]),
        truncated=np.asarray([False, False]),
        gamma=0.9,
        gae_lambda=0.8,
    )

    np.testing.assert_allclose(advantages, [1.985, 1.75], rtol=1e-6)
    np.testing.assert_allclose(returns, [2.485, 2.0], rtol=1e-6)


def test_truncation_bootstraps_next_value_and_cuts_recursive_trace() -> None:
    advantages, returns = _compute_gae(
        rewards=np.asarray([1.0, 100.0], dtype=np.float32),
        values=np.asarray([0.0, 0.0], dtype=np.float32),
        next_values=np.asarray([2.0, 50.0], dtype=np.float32),
        terminated=np.asarray([False, True]),
        truncated=np.asarray([True, False]),
        gamma=0.9,
        gae_lambda=0.95,
    )

    np.testing.assert_allclose(advantages, [2.8, 100.0], rtol=1e-6)
    np.testing.assert_allclose(returns, [2.8, 100.0], rtol=1e-6)


def test_gae_rejects_overlapping_terminal_flags() -> None:
    with pytest.raises(ValueError, match="both terminated and truncated"):
        _compute_gae(
            rewards=np.asarray([1.0], dtype=np.float32),
            values=np.asarray([0.0], dtype=np.float32),
            next_values=np.asarray([0.0], dtype=np.float32),
            terminated=np.asarray([True]),
            truncated=np.asarray([True]),
            gamma=0.99,
            gae_lambda=0.95,
        )

