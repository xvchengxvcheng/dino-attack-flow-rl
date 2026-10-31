from __future__ import annotations

import hashlib
from copy import deepcopy
from pathlib import Path
from typing import get_type_hints

import pytest
import torch
import yaml

from flow_rl.data.normalization import IdentityObservationNormalizer
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.models.flow import ConditionalVelocityMLP
from flow_rl.tracking.checkpoint import FlowSolverConfig, save_checkpoint
from flow_rl.training.config import PPOTrainingConfig
from flow_rl.training.dino_parallel_fpo_config import DinoParallelFPOConfig
from flow_rl.training.dino_parallel_policyflow_config import DinoParallelPolicyFlowConfig
from flow_rl.training.dino_parallel_ppo_config import DinoParallelPPOConfig
from flow_rl.training.flow_bc_config import FlowBCTrainingConfig
from flow_rl.training.fpo_config import FPOTrainingConfig
from flow_rl.training.policyflow_config import PolicyFlowTrainingConfig
from flow_rl.training.reinflow_config import ReinFlowTrainingConfig

from .test_config import _mapping as ppo_raw
from .test_dino_parallel_fpo_config import _raw as dino_fpo_raw
from .test_dino_parallel_ppo_config import V2_PROTOCOL, _raw as dino_ppo_raw
from .test_flow_bc_config import _raw as bc_raw
from .test_fpo_config import _raw as fpo_raw
from .test_policyflow_config import _config as policyflow_raw
from .test_reinflow_config import _bc_checkpoint, _raw as reinflow_raw


def _dino_policyflow_raw(directory: Path) -> dict:
    source = V2_PROTOCOL.parent / (
        "phase8_dino_policyflow_fixedclock_v5_16env_seed0_smoke_20260905.yaml"
    )
    raw = yaml.safe_load(source.read_text(encoding="utf-8"))
    build = directory / "Dino.exe"
    build.write_bytes(b"numeric-validation-fixture")
    raw.update(
        build_path=str(build), protocol_path=str(V2_PROTOCOL),
        run_directory=str(directory / "run"),
        actor_initialization_checkpoint_path=str(directory / "bc.pt"),
    )
    protocol = DinoProtocol.from_yaml(V2_PROTOCOL)
    encoder = SetTransformerDinoEncoder(protocol)
    model = ConditionalVelocityMLP(
        observation_shapes=protocol.observation_shapes,
        action_size=protocol.action_size,
        state_size=128, time_embedding_size=32, hidden_sizes=(128, 128),
        state_encoder=encoder,
    )
    normalizer = IdentityObservationNormalizer(
        protocol.observation_shapes,
        protocol_manifest_sha256=protocol.manifest_sha256,
    )
    save_checkpoint(
        Path(raw["actor_initialization_checkpoint_path"]),
        model_state=model.state_dict(), optimizer_state={},
        normalizer_state=normalizer.state_dict(), config=raw,
        metadata={
            "schema_version": 3, "algorithm": "flow_bc",
            "dataset_sha256": "synthetic-dataset", "metadata_sha256": "synthetic-metadata",
            "build_sha256": hashlib.sha256(build.read_bytes()).hexdigest(),
            "observation_shapes": protocol.observation_shapes,
            "action_size": protocol.action_size,
            "protocol": protocol.checkpoint_metadata(),
            "encoder": encoder.checkpoint_metadata(),
            "solver": FlowSolverConfig(action_transform="clamp").to_dict(),
        },
    )
    return raw


@pytest.fixture(scope="module", params=(
    "ppo", "fpo", "reinflow", "policyflow", "bc",
    "dino_ppo", "dino_fpo", "dino_policyflow",
))
def config_case(request, tmp_path_factory):
    directory = tmp_path_factory.mktemp(request.param)
    if request.param == "ppo":
        (directory / "3DBall.exe").write_bytes(b"build")
        cls, raw = PPOTrainingConfig, ppo_raw()
    elif request.param == "fpo":
        cls, raw = FPOTrainingConfig, fpo_raw(directory)
    elif request.param == "reinflow":
        cls, raw = ReinFlowTrainingConfig, reinflow_raw(directory)
    elif request.param == "policyflow":
        checkpoint, build = _bc_checkpoint(directory)
        cls, raw = PolicyFlowTrainingConfig, policyflow_raw()
        raw.update(
            normalizer_checkpoint_path=str(checkpoint),
            actor_initialization_checkpoint_path=str(checkpoint),
            build_path=str(build), run_directory=str(directory / "run"),
            state_size=4, time_embedding_size=4, velocity_hidden_sizes=[8],
        )
    elif request.param == "bc":
        cls, raw = FlowBCTrainingConfig, bc_raw(directory)
    elif request.param == "dino_ppo":
        cls, raw = DinoParallelPPOConfig, dino_ppo_raw(directory)
    elif request.param == "dino_fpo":
        cls, raw = DinoParallelFPOConfig, dino_fpo_raw(directory)
    else:
        cls, raw = DinoParallelPolicyFlowConfig, _dino_policyflow_raw(directory)
    source = directory / "config.yaml"
    source.write_text(yaml.safe_dump(raw), encoding="utf-8")
    # Configuration checks do not need a GPU or a real Player/checkpoint.
    with pytest.MonkeyPatch.context() as patch:
        patch.setattr(torch.cuda, "is_available", lambda: True)
        config = cls.from_yaml(source)
    for name, hint in get_type_hints(cls).items():
        if hint is float:
            raw.setdefault(name, getattr(config, name))
    return cls, raw, source


@pytest.mark.parametrize("invalid", [float("nan"), float("inf"), -float("inf"), True, "0.1"])
def test_all_training_float_parameters_reject_invalid_yaml_scalars(
    config_case, monkeypatch, invalid,
) -> None:
    cls, raw, source = config_case
    monkeypatch.setattr(torch.cuda, "is_available", lambda: True)
    for name, hint in get_type_hints(cls).items():
        if hint is not float:
            continue
        changed = deepcopy(raw)
        changed[name] = invalid
        source.write_text(yaml.safe_dump(changed), encoding="utf-8")
        with pytest.raises(ValueError, match=name):
            cls.from_yaml(source)
    assert not (source.parent / raw["run_directory"]).exists()


def test_valid_training_configuration_round_trips_without_parameter_changes(
    config_case, monkeypatch,
) -> None:
    cls, raw, source = config_case
    monkeypatch.setattr(torch.cuda, "is_available", lambda: True)
    source.write_text(yaml.safe_dump(raw), encoding="utf-8")
    config = cls.from_yaml(source)
    for name, hint in get_type_hints(cls).items():
        if hint is float:
            assert getattr(config, name) == raw[name]
