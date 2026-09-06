from __future__ import annotations

import numpy as np


def compute_gae(
    rewards: np.ndarray,
    values: np.ndarray,
    next_values: np.ndarray,
    terminated: np.ndarray,
    truncated: np.ndarray,
    gamma: float,
    gae_lambda: float,
) -> tuple[np.ndarray, np.ndarray]:
    """Compute GAE with time-limit bootstrap and episode-boundary trace cuts."""
    numeric = {
        "rewards": np.asarray(rewards),
        "values": np.asarray(values),
        "next_values": np.asarray(next_values),
    }
    flags = {
        "terminated": np.asarray(terminated),
        "truncated": np.asarray(truncated),
    }
    shape = numeric["rewards"].shape
    if len(shape) != 1:
        raise ValueError("GAE inputs must be one-dimensional")
    for name, array in numeric.items():
        if array.shape != shape:
            raise ValueError(f"{name} shape must match rewards")
        if array.dtype != np.float32:
            raise TypeError(f"{name} must have dtype float32")
        if not np.isfinite(array).all():
            raise ValueError(f"{name} must contain only finite values")
    for name, flag in flags.items():
        if flag.shape != shape:
            raise ValueError(f"{name} shape must match rewards")
        if flag.dtype != np.bool_:
            raise TypeError(f"{name} must have dtype bool")
    if np.logical_and(flags["terminated"], flags["truncated"]).any():
        raise ValueError("a sample cannot be both terminated and truncated")
    if not 0.0 <= gamma <= 1.0:
        raise ValueError("gamma must be within [0, 1]")
    if not 0.0 <= gae_lambda <= 1.0:
        raise ValueError("gae_lambda must be within [0, 1]")

    advantages = np.empty(shape, dtype=np.float32)
    next_advantage = 0.0
    for index in range(shape[0] - 1, -1, -1):
        bootstrap = 0.0 if flags["terminated"][index] else 1.0
        continue_trace = 0.0 if (
            flags["terminated"][index] or flags["truncated"][index]
        ) else 1.0
        delta = (
            float(numeric["rewards"][index])
            + gamma * float(numeric["next_values"][index]) * bootstrap
            - float(numeric["values"][index])
        )
        next_advantage = (
            delta + gamma * gae_lambda * continue_trace * next_advantage
        )
        advantages[index] = next_advantage
    returns = advantages + numeric["values"]
    return advantages, returns.astype(np.float32, copy=False)

