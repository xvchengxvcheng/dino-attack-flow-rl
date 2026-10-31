"""Scalar validation shared by training configuration boundaries."""

from __future__ import annotations

import math
from numbers import Real


def validate_finite_parameters(**parameters: float) -> None:
    """Reject nonnumeric/nonfinite YAML scalars before range comparisons.

    NaN passes ordinary lower-bound comparisons, and bool is an int subclass.
    Keep range constraints in each algorithm's configuration validator.
    """
    for name, value in parameters.items():
        if isinstance(value, bool) or not isinstance(value, Real):
            raise ValueError(f"{name} must be a finite real number")
        try:
            finite = math.isfinite(value)
        except OverflowError:
            finite = False
        if not finite:
            raise ValueError(f"{name} must be a finite real number")
