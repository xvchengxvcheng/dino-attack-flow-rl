from __future__ import annotations

import json

import pytest
import torch
from torch import nn

from flow_rl.tracking.parameter_audit import (
    audit_parameter_updates,
    render_parameter_update_audit_markdown,
    snapshot_named_parameters,
)


class _TinyActorCritic(nn.Module):
    def __init__(self) -> None:
        super().__init__()
        self.actor = nn.Module()
        self.actor.encoder = nn.Module()
        self.actor.encoder.wall_encoder = nn.Linear(2, 2, bias=False)
        self.actor.mean_head = nn.Linear(2, 1, bias=False)
        self.actor.log_std = nn.Parameter(torch.zeros(1))
        self.critic = nn.Module()
        self.critic.encoder = nn.Module()
        self.critic.encoder.global_encoder = nn.Linear(2, 2, bias=False)
        self.critic.value_head = nn.Linear(2, 1, bias=False)


def test_parameter_audit_reports_changed_and_unchanged_networks_independently() -> None:
    model = _TinyActorCritic()
    before = snapshot_named_parameters(model)
    with torch.no_grad():
        model.actor.encoder.wall_encoder.weight[0, 0].add_(0.5)
        model.actor.log_std.add_(0.25)
        model.critic.value_head.weight.add_(1.0)

    audit = audit_parameter_updates(before, snapshot_named_parameters(model))

    assert audit["summary"] == {
        "parameter_tensors": 5,
        "changed_parameter_tensors": 3,
        "unchanged_parameter_tensors": 2,
        "parameter_elements": 13,
        "changed_parameter_elements": 4,
        "all_finite": True,
    }
    assert audit["groups"]["actor.encoder.wall_encoder"]["changed_parameter_tensors"] == 1
    assert audit["groups"]["actor.mean_head"]["changed_parameter_tensors"] == 0
    assert audit["groups"]["actor.log_std"]["changed_parameter_tensors"] == 1
    assert audit["groups"]["critic.encoder.global_encoder"]["changed_parameter_tensors"] == 0
    assert audit["groups"]["critic.value_head"]["changed_parameter_tensors"] == 1
    assert audit["parameters"]["actor.encoder.wall_encoder.weight"]["changed_elements"] == 1
    assert audit["parameters"]["actor.encoder.wall_encoder.weight"]["max_abs_delta"] == pytest.approx(0.5)
    json.dumps(audit, allow_nan=False)


def test_parameter_snapshot_is_detached_and_markdown_lists_every_group() -> None:
    model = _TinyActorCritic()
    before = snapshot_named_parameters(model)
    with torch.no_grad():
        model.actor.mean_head.weight.add_(0.125)
    after = snapshot_named_parameters(model)

    assert torch.equal(before["actor.log_std"], torch.zeros(1))
    audit = audit_parameter_updates(before, after)
    markdown = render_parameter_update_audit_markdown(audit)

    assert "| `actor.encoder.wall_encoder` | 0 / 1 |" in markdown
    assert "| `actor.mean_head` | 1 / 1 |" in markdown
    assert "| `critic.value_head` | 0 / 1 |" in markdown
    assert "All finite: `True`" in markdown
