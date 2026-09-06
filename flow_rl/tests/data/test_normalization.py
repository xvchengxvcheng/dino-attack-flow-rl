from __future__ import annotations

import importlib

import numpy as np
import pytest


def _normalizer_type():
    try:
        module = importlib.import_module("flow_rl.data.normalization")
    except ModuleNotFoundError as exc:
        pytest.fail(f"normalization module is missing: {exc}")
    return module.ObservationNormalizer


def test_normalizer_updates_each_sensor_independently() -> None:
    ObservationNormalizer = _normalizer_type()
    normalizer = ObservationNormalizer(((1,), (2,)), epsilon=1e-8, clip=10.0)
    observations = (
        np.asarray([[1.0], [3.0]], dtype=np.float32),
        np.asarray([[2.0, 4.0], [6.0, 8.0]], dtype=np.float32),
    )

    normalizer.update(observations)
    normalized = normalizer.normalize(observations)

    np.testing.assert_allclose(normalized[0], [[-1.0], [1.0]], atol=1e-6)
    np.testing.assert_allclose(normalized[1], [[-1.0, -1.0], [1.0, 1.0]], atol=1e-6)
    assert all(array.dtype == np.float32 for array in normalized)


def test_normalizer_is_finite_for_zero_variance_and_restores_exact_state() -> None:
    ObservationNormalizer = _normalizer_type()
    normalizer = ObservationNormalizer(((1,),), epsilon=1e-6, clip=5.0)
    observations = np.asarray([[5.0], [5.0]], dtype=np.float32)
    normalizer.update((observations,))
    state = normalizer.state_dict()

    restored = ObservationNormalizer(((1,),), epsilon=1e-6, clip=5.0)
    restored.load_state_dict(state)

    np.testing.assert_allclose(restored.normalize((observations,))[0], [[0.0], [0.0]])
    restored_state = restored.state_dict()
    assert restored_state["count"] == state["count"]
    np.testing.assert_array_equal(restored_state["mean"][0], state["mean"][0])
    np.testing.assert_array_equal(restored_state["variance"][0], state["variance"][0])


def test_normalizer_rejects_sensor_shape_mismatch() -> None:
    ObservationNormalizer = _normalizer_type()
    normalizer = ObservationNormalizer(((2,),))

    with pytest.raises(ValueError, match="shape"):
        normalizer.update((np.ones((3, 1), dtype=np.float32),))
