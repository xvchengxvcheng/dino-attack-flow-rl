from __future__ import annotations

from collections.abc import Mapping, Sequence
from typing import Any

import numpy as np


class ObservationNormalizer:
    """Independent running mean and variance for each observation stream."""

    def __init__(
        self,
        shapes: Sequence[tuple[int, ...]],
        *,
        epsilon: float = 1e-8,
        clip: float = 10.0,
    ) -> None:
        self._shapes = tuple(tuple(shape) for shape in shapes)
        if not self._shapes:
            raise ValueError("at least one observation shape is required")
        if epsilon <= 0.0 or clip <= 0.0:
            raise ValueError("epsilon and clip must be positive")
        self._epsilon = float(epsilon)
        self._clip = float(clip)
        self._count = 0
        self._mean = [np.zeros(shape, dtype=np.float64) for shape in self._shapes]
        self._variance = [np.ones(shape, dtype=np.float64) for shape in self._shapes]

    def update(self, observations: tuple[np.ndarray, ...]) -> None:
        arrays = self._validate_observations(observations)
        batch_count = arrays[0].shape[0]
        if batch_count == 0:
            return
        previous_count = self._count
        total_count = previous_count + batch_count
        for index, array in enumerate(arrays):
            batch = array.astype(np.float64, copy=False)
            batch_mean = batch.mean(axis=0)
            batch_variance = batch.var(axis=0)
            if previous_count == 0:
                self._mean[index] = batch_mean
                self._variance[index] = batch_variance
                continue
            delta = batch_mean - self._mean[index]
            previous_m2 = self._variance[index] * previous_count
            batch_m2 = batch_variance * batch_count
            self._mean[index] = self._mean[index] + delta * (
                batch_count / total_count
            )
            combined_m2 = (
                previous_m2
                + batch_m2
                + np.square(delta) * previous_count * batch_count / total_count
            )
            self._variance[index] = combined_m2 / total_count
        self._count = total_count

    def normalize(
        self, observations: tuple[np.ndarray, ...]
    ) -> tuple[np.ndarray, ...]:
        arrays = self._validate_observations(observations)
        normalized = []
        for array, mean, variance in zip(arrays, self._mean, self._variance):
            values = (array.astype(np.float64) - mean) / np.sqrt(
                variance + self._epsilon
            )
            normalized.append(
                np.clip(values, -self._clip, self._clip).astype(np.float32)
            )
        return tuple(normalized)

    def state_dict(self) -> dict[str, Any]:
        return {
            "shapes": self._shapes,
            "epsilon": self._epsilon,
            "clip": self._clip,
            "count": self._count,
            "mean": tuple(array.copy() for array in self._mean),
            "variance": tuple(array.copy() for array in self._variance),
        }

    def load_state_dict(self, state: Mapping[str, Any]) -> None:
        if tuple(tuple(shape) for shape in state["shapes"]) != self._shapes:
            raise ValueError("normalizer state shapes do not match")
        if float(state["epsilon"]) != self._epsilon or float(state["clip"]) != self._clip:
            raise ValueError("normalizer state configuration does not match")
        count = int(state["count"])
        if count < 0:
            raise ValueError("normalizer count cannot be negative")
        means = tuple(np.asarray(array, dtype=np.float64) for array in state["mean"])
        variances = tuple(
            np.asarray(array, dtype=np.float64) for array in state["variance"]
        )
        if len(means) != len(self._shapes) or len(variances) != len(self._shapes):
            raise ValueError("normalizer state sensor count does not match")
        for shape, mean, variance in zip(self._shapes, means, variances):
            if mean.shape != shape or variance.shape != shape:
                raise ValueError("normalizer state shape does not match")
            if not np.isfinite(mean).all() or not np.isfinite(variance).all():
                raise ValueError("normalizer state must be finite")
            if np.any(variance < 0.0):
                raise ValueError("normalizer variance cannot be negative")
        self._count = count
        self._mean = [array.copy() for array in means]
        self._variance = [array.copy() for array in variances]

    def _validate_observations(
        self, observations: tuple[np.ndarray, ...]
    ) -> tuple[np.ndarray, ...]:
        if not isinstance(observations, tuple) or len(observations) != len(
            self._shapes
        ):
            raise ValueError("observation sensor count does not match")
        arrays = tuple(np.asarray(array) for array in observations)
        batch_size: int | None = None
        for index, (array, shape) in enumerate(zip(arrays, self._shapes)):
            if array.dtype != np.float32:
                raise TypeError(f"observations[{index}] must have dtype float32")
            if array.ndim < 1 or array.shape[1:] != shape:
                raise ValueError(f"observations[{index}] shape must be (batch, {shape})")
            if batch_size is None:
                batch_size = array.shape[0]
            elif array.shape[0] != batch_size:
                raise ValueError("observation streams must share a batch dimension")
            if not np.isfinite(array).all():
                raise ValueError(f"observations[{index}] must contain only finite values")
        return arrays


class IdentityObservationNormalizer:
    """Shape-check structured observations without changing masks or fields."""

    def __init__(
        self,
        shapes: Sequence[tuple[int, ...]],
        *,
        protocol_manifest_sha256: str,
    ) -> None:
        self._shapes = tuple(tuple(int(size) for size in shape) for shape in shapes)
        if not self._shapes or any(
            not shape or any(size <= 0 for size in shape)
            for shape in self._shapes
        ):
            raise ValueError("identity normalizer shapes must be positive")
        if not isinstance(protocol_manifest_sha256, str) or not protocol_manifest_sha256:
            raise ValueError("protocol_manifest_sha256 must be a non-empty string")
        self._protocol_manifest_sha256 = protocol_manifest_sha256

    def update(self, observations: tuple[np.ndarray, ...]) -> None:
        self._validate_observations(observations)

    def normalize(
        self,
        observations: tuple[np.ndarray, ...],
    ) -> tuple[np.ndarray, ...]:
        return self._validate_observations(observations)

    def state_dict(self) -> dict[str, Any]:
        return {
            "normalizer_type": "identity",
            "shapes": self._shapes,
            "protocol_manifest_sha256": self._protocol_manifest_sha256,
        }

    def load_state_dict(self, state: Mapping[str, Any]) -> None:
        expected = self.state_dict()
        actual = {
            "normalizer_type": state.get("normalizer_type"),
            "shapes": tuple(tuple(shape) for shape in state.get("shapes", ())),
            "protocol_manifest_sha256": state.get("protocol_manifest_sha256"),
        }
        if actual["normalizer_type"] != expected["normalizer_type"]:
            raise ValueError("identity normalizer type does not match")
        if actual["shapes"] != expected["shapes"]:
            raise ValueError("identity normalizer shapes do not match")
        if (
            actual["protocol_manifest_sha256"]
            != expected["protocol_manifest_sha256"]
        ):
            raise ValueError("identity normalizer protocol hash does not match")
        if set(state) != set(expected):
            raise ValueError("identity normalizer state fields do not match")

    def _validate_observations(
        self,
        observations: tuple[np.ndarray, ...],
    ) -> tuple[np.ndarray, ...]:
        if not isinstance(observations, tuple) or len(observations) != len(
            self._shapes
        ):
            raise ValueError("observation sensor count does not match")
        arrays = tuple(np.asarray(array) for array in observations)
        batch_size: int | None = None
        for index, (array, shape) in enumerate(zip(arrays, self._shapes)):
            if array.dtype != np.float32:
                raise TypeError(f"observations[{index}] must have dtype float32")
            if array.shape[1:] != shape:
                raise ValueError(f"observations[{index}] shape must be (batch, {shape})")
            if batch_size is None:
                batch_size = array.shape[0]
            elif array.shape[0] != batch_size:
                raise ValueError("observation streams must share a batch dimension")
            if not np.isfinite(array).all():
                raise ValueError(f"observations[{index}] must contain only finite values")
        return arrays
