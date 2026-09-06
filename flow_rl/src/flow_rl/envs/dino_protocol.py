from __future__ import annotations

import hashlib
import math
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from numbers import Integral, Real
from os import PathLike
from pathlib import Path
from typing import Any

import numpy as np
import yaml


_PROTOCOL_VERSION = "dino_attack_structured_set_v2"
_STREAM_NAMES = ("Global", "Regions", "Walls", "Guards", "Houses", "Dinos")
_STREAM_SHAPES = ((5,), (5, 8), (6, 5), (11, 7), (8, 6), (10, 7))
_STREAM_FIELDS = (
    (
        "remaining_time",
        "food_normalized",
        "last_action_valid",
        "dino_target_x_norm",
        "dino_target_z_norm",
    ),
    ("Ax", "Az", "Bx", "Bz", "Cx", "Cz", "Dx", "Dz"),
    ("valid_mask", "active", "x_norm", "z_norm", "health_ratio"),
    (
        "valid_mask",
        "type_archer",
        "type_mage",
        "x_norm",
        "z_norm",
        "health_ratio",
        "is_wall_guard",
    ),
    (
        "valid_mask",
        "alive",
        "x_norm",
        "z_norm",
        "health_ratio",
        "max_health_normalized",
    ),
    (
        "valid_mask",
        "type_velociraptor",
        "type_pachy",
        "type_trex",
        "x_norm",
        "z_norm",
        "health_ratio",
    ),
)
_MAXIMA = (("walls", 6), ("guards", 11), ("houses", 8), ("dinos", 10))
_MAP_BOUNDS = (("x", (-55.5, 48.5)), ("z", (-53.0, 29.0)))
_FOOD_SCALE = 5000.0
_HOUSE_HEALTH_SCALE = 325.0
_REGION_IDS = (
    "Z1RuinsForecourt",
    "Z2OpenMeadow",
    "Z3RiverTerrace",
    "Z4PalmGrove",
    "Z5RockyShelf",
)
_ACTION_VERSION = "dino_attack_structured_set_action_v1"
_ACTION_DIMENSIONS = ("zone", "u", "v", "choice")
_ZONE_THRESHOLDS = (-0.6, -0.2, 0.2, 0.6)
_CHOICE_ORDER = ("Wait", "Velociraptor", "Pachycephalosaurus", "T-Rex")
_CHOICE_THRESHOLDS = (-0.5, 0.0, 0.5)
_BOUNDARY_RULE = "equality_enters_higher_bin"
_WAIT_IGNORES = ("zone", "u", "v")


def _expect_keys(mapping: Mapping[str, Any], expected: Sequence[str], label: str) -> None:
    actual = set(mapping)
    expected_set = set(expected)
    if actual != expected_set:
        raise ValueError(
            f"{label} keys mismatch; missing={sorted(expected_set - actual)}, "
            f"extra={sorted(actual - expected_set)}"
        )


def _mapping(value: Any, label: str) -> Mapping[str, Any]:
    if not isinstance(value, Mapping):
        raise TypeError(f"{label} must be a mapping")
    if not all(isinstance(key, str) for key in value):
        raise TypeError(f"{label} keys must be strings")
    return value


def _list(value: Any, label: str) -> list[Any]:
    if not isinstance(value, list):
        raise TypeError(f"{label} must be a list")
    return value


def _strings(value: Any, label: str) -> tuple[str, ...]:
    items = _list(value, label)
    if not all(isinstance(item, str) for item in items):
        raise TypeError(f"{label} must contain only strings")
    return tuple(items)


def _integer(value: Any, label: str) -> int:
    if isinstance(value, bool) or not isinstance(value, Integral):
        raise TypeError(f"{label} must be an integer")
    return int(value)


def _number(value: Any, label: str) -> float:
    if isinstance(value, bool) or not isinstance(value, Real):
        raise TypeError(f"{label} must be a number")
    result = float(value)
    if not math.isfinite(result):
        raise ValueError(f"{label} must be finite")
    return result


def _numbers(value: Any, length: int, label: str) -> tuple[float, ...]:
    items = _list(value, label)
    if len(items) != length:
        raise ValueError(f"{label} must contain exactly {length} values")
    return tuple(_number(item, f"{label}[{index}]") for index, item in enumerate(items))


def _require_equal(actual: Any, expected: Any, label: str) -> None:
    if actual != expected:
        raise ValueError(f"{label} mismatch; expected={expected!r}, got={actual!r}")


@dataclass(frozen=True)
class DinoProtocol:
    """Frozen structured Dino Attack observation/action protocol."""

    protocol_version: str
    stream_names: tuple[str, ...]
    observation_shapes: tuple[tuple[int, ...], ...]
    observation_fields: tuple[tuple[str, ...], ...]
    _maxima: tuple[tuple[str, int], ...]
    _map_bounds: tuple[tuple[str, tuple[float, float]], ...]
    food_scale: float
    house_health_scale: float
    region_ids: tuple[str, ...]
    action_version: str
    action_size: int
    action_dtype: str
    action_range: tuple[float, float]
    action_dimensions: tuple[str, ...]
    zone_thresholds: tuple[float, ...]
    choice_order: tuple[str, ...]
    choice_thresholds: tuple[float, ...]
    boundary_rule: str
    wait_ignores: tuple[str, ...]
    manifest_sha256: str

    @property
    def maxima(self) -> dict[str, int]:
        return dict(self._maxima)

    @property
    def map_bounds(self) -> dict[str, tuple[float, float]]:
        return dict(self._map_bounds)

    @classmethod
    def from_yaml(cls, path: str | PathLike[str]) -> "DinoProtocol":
        source = Path(path).resolve()
        encoded = source.read_bytes()
        raw = yaml.safe_load(encoded.decode("utf-8"))
        root = _mapping(raw, "Dino protocol manifest")
        _require_equal(root.get("protocol_version"), _PROTOCOL_VERSION, "protocol version")
        _expect_keys(
            root,
            (
                "protocol_version",
                "streams",
                "maxima",
                "normalization",
                "action",
            ),
            "Dino protocol manifest",
        )
        streams = _list(root["streams"], "streams")
        if len(streams) != len(_STREAM_NAMES):
            raise ValueError(f"stream order mismatch; expected {_STREAM_NAMES!r}")
        names: list[str] = []
        shapes: list[tuple[int, ...]] = []
        fields: list[tuple[str, ...]] = []
        for index, item in enumerate(streams):
            stream = _mapping(item, f"streams[{index}]")
            _expect_keys(stream, ("name", "shape", "fields"), f"streams[{index}]")
            name = stream["name"]
            if not isinstance(name, str):
                raise TypeError(f"streams[{index}].name must be a string")
            shape = tuple(
                _integer(size, f"streams[{index}].shape[{dimension}]")
                for dimension, size in enumerate(_list(stream["shape"], f"streams[{index}].shape"))
            )
            names.append(name)
            shapes.append(shape)
            fields.append(_strings(stream["fields"], f"streams[{index}].fields"))
        if tuple(names) != _STREAM_NAMES:
            raise ValueError(
                f"stream order mismatch; expected {_STREAM_NAMES!r}, got={tuple(names)!r}"
            )
        _require_equal(tuple(shapes), _STREAM_SHAPES, "observation shapes")
        _require_equal(tuple(fields), _STREAM_FIELDS, "observation fields")

        maxima = _mapping(root["maxima"], "maxima")
        _expect_keys(maxima, tuple(name for name, _ in _MAXIMA), "maxima")
        parsed_maxima = tuple(
            (name, _integer(maxima[name], f"maxima.{name}")) for name, _ in _MAXIMA
        )
        _require_equal(parsed_maxima, _MAXIMA, "maxima")

        normalization = _mapping(root["normalization"], "normalization")
        _expect_keys(
            normalization,
            ("map_bounds", "food_scale", "house_health_scale"),
            "normalization",
        )
        bounds = _mapping(normalization["map_bounds"], "normalization.map_bounds")
        _expect_keys(bounds, ("x", "z"), "normalization.map_bounds")
        parsed_bounds = tuple(
            (axis, _numbers(bounds[axis], 2, f"normalization.map_bounds.{axis}"))
            for axis, _ in _MAP_BOUNDS
        )
        _require_equal(parsed_bounds, _MAP_BOUNDS, "map bounds")
        food_scale = _number(normalization["food_scale"], "normalization.food_scale")
        house_scale = _number(
            normalization["house_health_scale"],
            "normalization.house_health_scale",
        )
        _require_equal(food_scale, _FOOD_SCALE, "food scale")
        _require_equal(house_scale, _HOUSE_HEALTH_SCALE, "house health scale")

        action = _mapping(root["action"], "action")
        _expect_keys(
            action,
            (
                "version",
                "size",
                "dtype",
                "range",
                "dimensions",
                "zone_order",
                "zone_thresholds",
                "choice_order",
                "choice_thresholds",
                "boundary_rule",
                "wait_ignores",
            ),
            "action",
        )
        _require_equal(action["version"], _ACTION_VERSION, "action version")
        action_size = _integer(action["size"], "action.size")
        _require_equal(action_size, 4, "action size")
        _require_equal(action["dtype"], "float32", "action dtype")
        action_range = _numbers(action["range"], 2, "action.range")
        _require_equal(action_range, (-1.0, 1.0), "action range")
        dimensions = _strings(action["dimensions"], "action.dimensions")
        _require_equal(dimensions, _ACTION_DIMENSIONS, "action dimension order")
        zone_order = _strings(action["zone_order"], "action.zone_order")
        _require_equal(zone_order, _REGION_IDS, "action zone order")
        zone_thresholds = _numbers(
            action["zone_thresholds"], 4, "action.zone_thresholds"
        )
        _require_equal(zone_thresholds, _ZONE_THRESHOLDS, "action zone thresholds")
        choice_order = _strings(action["choice_order"], "action.choice_order")
        _require_equal(choice_order, _CHOICE_ORDER, "action choice order")
        choice_thresholds = _numbers(
            action["choice_thresholds"], 3, "action.choice_thresholds"
        )
        _require_equal(
            choice_thresholds, _CHOICE_THRESHOLDS, "action choice thresholds"
        )
        _require_equal(action["boundary_rule"], _BOUNDARY_RULE, "action boundary rule")
        wait_ignores = _strings(action["wait_ignores"], "action.wait_ignores")
        _require_equal(wait_ignores, _WAIT_IGNORES, "action Wait semantics")

        return cls(
            protocol_version=_PROTOCOL_VERSION,
            stream_names=_STREAM_NAMES,
            observation_shapes=_STREAM_SHAPES,
            observation_fields=_STREAM_FIELDS,
            _maxima=_MAXIMA,
            _map_bounds=_MAP_BOUNDS,
            food_scale=food_scale,
            house_health_scale=house_scale,
            region_ids=_REGION_IDS,
            action_version=_ACTION_VERSION,
            action_size=action_size,
            action_dtype="float32",
            action_range=action_range,
            action_dimensions=_ACTION_DIMENSIONS,
            zone_thresholds=tuple(float(np.float32(value)) for value in zone_thresholds),
            choice_order=_CHOICE_ORDER,
            choice_thresholds=tuple(
                float(np.float32(value)) for value in choice_thresholds
            ),
            boundary_rule=_BOUNDARY_RULE,
            wait_ignores=_WAIT_IGNORES,
            manifest_sha256=hashlib.sha256(encoded).hexdigest(),
        )

    def validate_shapes(self, shapes: Sequence[Sequence[int]]) -> None:
        try:
            actual = tuple(
                tuple(_integer(size, f"observation shapes[{index}]") for size in shape)
                for index, shape in enumerate(shapes)
            )
        except TypeError as exc:
            raise ValueError(f"observation shapes are invalid: {exc}") from exc
        if actual != self.observation_shapes:
            raise ValueError(
                f"observation shapes mismatch; expected={self.observation_shapes!r}, "
                f"got={actual!r}"
            )

    def validate_action_size(self, size: int) -> None:
        actual = _integer(size, "action size")
        if actual != self.action_size:
            raise ValueError(
                f"action size mismatch; expected={self.action_size}, got={actual}"
            )

    def checkpoint_metadata(self) -> dict[str, Any]:
        return {
            "protocol_version": self.protocol_version,
            "manifest_sha256": self.manifest_sha256,
            "observation_streams": tuple(
                {"name": name, "shape": shape, "fields": fields}
                for name, shape, fields in zip(
                    self.stream_names,
                    self.observation_shapes,
                    self.observation_fields,
                )
            ),
            "observation_shapes": self.observation_shapes,
            "observation_fields": self.observation_fields,
            "maxima": self.maxima,
            "normalization": {
                "map_bounds": self.map_bounds,
                "food_scale": self.food_scale,
                "house_health_scale": self.house_health_scale,
            },
            "region_order": self.region_ids,
            "action": {
                "version": self.action_version,
                "size": self.action_size,
                "dtype": self.action_dtype,
                "range": self.action_range,
                "dimensions": self.action_dimensions,
                "zone_order": self.region_ids,
                "zone_thresholds": self.zone_thresholds,
                "choice_order": self.choice_order,
                "choice_thresholds": self.choice_thresholds,
                "boundary_rule": self.boundary_rule,
                "wait_ignores": self.wait_ignores,
            },
        }
