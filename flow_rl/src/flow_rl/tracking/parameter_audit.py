from __future__ import annotations

import math
from collections.abc import Mapping
from typing import Any

import torch
from torch import nn


def snapshot_named_parameters(module: nn.Module) -> dict[str, torch.Tensor]:
    """Copy trainable parameter values to detached CPU tensors."""
    return {
        name: parameter.detach().cpu().clone()
        for name, parameter in module.named_parameters()
    }


def _parameter_group(name: str) -> str:
    parts = name.split(".")
    if len(parts) >= 3 and parts[1] == "encoder":
        return ".".join(parts[:3])
    if len(parts) >= 2:
        return ".".join(parts[:2])
    return parts[0]


def audit_parameter_updates(
    before: Mapping[str, torch.Tensor],
    after: Mapping[str, torch.Tensor],
) -> dict[str, Any]:
    """Compare named parameters exactly and aggregate changes by subnetwork."""
    before_names = set(before)
    after_names = set(after)
    if before_names != after_names:
        raise ValueError(
            "parameter names differ; "
            f"missing_after={sorted(before_names - after_names)}, "
            f"missing_before={sorted(after_names - before_names)}"
        )

    parameters: dict[str, dict[str, Any]] = {}
    groups: dict[str, dict[str, Any]] = {}
    changed_tensors = 0
    total_elements = 0
    changed_elements = 0
    all_finite = True

    for name in sorted(before_names):
        initial = before[name].detach().cpu()
        final = after[name].detach().cpu()
        if initial.shape != final.shape or initial.dtype != final.dtype:
            raise ValueError(f"parameter metadata changed for {name}")
        initial64 = initial.to(dtype=torch.float64)
        final64 = final.to(dtype=torch.float64)
        delta = final64 - initial64
        finite = bool(
            torch.isfinite(initial64).all()
            and torch.isfinite(final64).all()
            and torch.isfinite(delta).all()
        )
        count = int(initial.numel())
        changed = int(torch.count_nonzero(delta).item())
        before_l2 = float(torch.linalg.vector_norm(initial64).item())
        after_l2 = float(torch.linalg.vector_norm(final64).item())
        delta_l2 = float(torch.linalg.vector_norm(delta).item())
        maximum = float(torch.max(torch.abs(delta)).item()) if count else 0.0
        relative = delta_l2 / max(before_l2, torch.finfo(torch.float64).eps)
        item = {
            "numel": count,
            "changed_elements": changed,
            "changed": changed > 0,
            "before_l2": before_l2,
            "after_l2": after_l2,
            "l2_delta": delta_l2,
            "relative_l2_delta": relative,
            "max_abs_delta": maximum,
            "all_finite": finite,
        }
        parameters[name] = item
        group_name = _parameter_group(name)
        group = groups.setdefault(
            group_name,
            {
                "parameter_tensors": 0,
                "changed_parameter_tensors": 0,
                "parameter_elements": 0,
                "changed_parameter_elements": 0,
                "max_abs_delta": 0.0,
                "l2_delta": 0.0,
                "all_finite": True,
                "_squared_l2_delta": 0.0,
            },
        )
        group["parameter_tensors"] += 1
        group["changed_parameter_tensors"] += int(changed > 0)
        group["parameter_elements"] += count
        group["changed_parameter_elements"] += changed
        group["max_abs_delta"] = max(group["max_abs_delta"], maximum)
        group["_squared_l2_delta"] += delta_l2 * delta_l2
        group["all_finite"] = bool(group["all_finite"] and finite)
        changed_tensors += int(changed > 0)
        total_elements += count
        changed_elements += changed
        all_finite = all_finite and finite

    for group in groups.values():
        group["l2_delta"] = math.sqrt(group.pop("_squared_l2_delta"))

    tensor_count = len(parameters)
    return {
        "summary": {
            "parameter_tensors": tensor_count,
            "changed_parameter_tensors": changed_tensors,
            "unchanged_parameter_tensors": tensor_count - changed_tensors,
            "parameter_elements": total_elements,
            "changed_parameter_elements": changed_elements,
            "all_finite": all_finite,
        },
        "groups": groups,
        "parameters": parameters,
    }


def render_parameter_update_audit_markdown(audit: Mapping[str, Any]) -> str:
    summary = audit["summary"]
    lines = [
        "# PPO parameter update audit",
        "",
        f"All finite: `{summary['all_finite']}`",
        "",
        "| Parameter group | Changed tensors | Changed elements | L2 delta | Max abs delta | Finite |",
        "|---|---:|---:|---:|---:|---:|",
    ]
    for name, group in sorted(audit["groups"].items()):
        lines.append(
            f"| `{name}` | {group['changed_parameter_tensors']} / {group['parameter_tensors']} | "
            f"{group['changed_parameter_elements']} / {group['parameter_elements']} | "
            f"{group['l2_delta']:.9g} | {group['max_abs_delta']:.9g} | "
            f"{group['all_finite']} |"
        )
    lines.extend(
        [
            "",
            "## Summary",
            "",
            f"- Changed parameter tensors: {summary['changed_parameter_tensors']} / {summary['parameter_tensors']}",
            f"- Changed parameter elements: {summary['changed_parameter_elements']} / {summary['parameter_elements']}",
            f"- Unchanged parameter tensors: {summary['unchanged_parameter_tensors']}",
            "",
        ]
    )
    return "\n".join(lines)
