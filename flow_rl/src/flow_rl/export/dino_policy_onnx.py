from __future__ import annotations

import hashlib
import json
from collections.abc import Mapping, Sequence
from dataclasses import dataclass
from pathlib import Path
from typing import Any

import torch
from torch import nn

from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.models.fpo_policy import FlowActorCritic
from flow_rl.models.policy import GaussianActorCritic
from flow_rl.models.policyflow_policy import PolicyFlowActorCritic
from flow_rl.tracking.checkpoint import load_checkpoint


INPUT_NAMES = ("global", "regions", "walls", "guards", "houses", "dinos")
OUTPUT_NAME = "actions"
ARTIFACT_KIND_FEASIBILITY = "feasibility_only"
ARTIFACT_KIND_TRAINED = "trained_checkpoint"
_EXPORT_SCHEMA_VERSION = 1
_ONNX_OPSET = 17


class _DeterministicActor(nn.Module):
    def __init__(self, policy: GaussianActorCritic) -> None:
        super().__init__()
        self.actor = policy.actor

    def forward(
        self,
        global_observation: torch.Tensor,
        regions: torch.Tensor,
        walls: torch.Tensor,
        guards: torch.Tensor,
        houses: torch.Tensor,
        dinos: torch.Tensor,
    ) -> torch.Tensor:
        observations = (
            global_observation,
            regions,
            walls,
            guards,
            houses,
            dinos,
        )
        mean, _ = self.actor._distribution_parameters(observations)
        return torch.tanh(mean)


class _DeterministicFPOActor(nn.Module):
    """Export the frozen zero-latent FPO path used by deterministic evaluation."""

    def __init__(self, policy: FlowActorCritic, *, nfe: int) -> None:
        super().__init__()
        self.actor = policy.actor
        self.nfe = int(nfe)
        self.action_size = int(policy.action_size)

    def forward(
        self,
        global_observation: torch.Tensor,
        regions: torch.Tensor,
        walls: torch.Tensor,
        guards: torch.Tensor,
        houses: torch.Tensor,
        dinos: torch.Tensor,
    ) -> torch.Tensor:
        observations = (
            global_observation,
            regions,
            walls,
            guards,
            houses,
            dinos,
        )
        latent_actions = torch.zeros(
            (global_observation.shape[0], self.action_size),
            dtype=global_observation.dtype,
            device=global_observation.device,
        )
        dt = 1.0 / float(self.nfe)
        for step in range(self.nfe):
            time = torch.full(
                (global_observation.shape[0], 1),
                step * dt,
                dtype=global_observation.dtype,
                device=global_observation.device,
            )
            latent_actions = latent_actions + dt * self.actor(
                observations, latent_actions, time
            )
        return torch.tanh(latent_actions)


@dataclass(frozen=True)
class DinoOnnxExportResult:
    model_path: Path
    manifest_path: Path
    model_sha256: str
    protocol_sha256: str
    artifact_kind: str
    module: nn.Module


class _DeterministicPolicyFlowActor(_DeterministicFPOActor):
    def forward(self, global_observation, regions, walls, guards, houses, dinos):
        observations = (global_observation, regions, walls, guards, houses, dinos)
        current = torch.zeros((global_observation.shape[0], self.action_size),
                              dtype=global_observation.dtype, device=global_observation.device)
        steps = self.nfe // 2
        dt = 1.0 / steps
        for step in range(steps):
            time = torch.full((global_observation.shape[0], 1), step * dt,
                              dtype=global_observation.dtype, device=global_observation.device)
            first = self.actor(observations, current, time)
            midpoint = current + 0.5 * dt * first
            current = current + dt * self.actor(observations, midpoint, time + 0.5 * dt)
        return torch.tanh(current)


def _sha256(path: Path) -> str:
    return hashlib.sha256(Path(path).read_bytes()).hexdigest()


def _canonical(value: Any) -> Any:
    if isinstance(value, Mapping):
        return {
            str(key): _canonical(item)
            for key, item in sorted(value.items(), key=lambda pair: str(pair[0]))
        }
    if isinstance(value, (list, tuple)):
        return [_canonical(item) for item in value]
    return value


def _checkpoint_provenance(metadata: Mapping[str, Any] | None) -> Any:
    if metadata is None:
        return None
    stable_keys = (
        "schema_version",
        "algorithm",
        "environment_steps",
        "policy_version",
        "unity_steps",
        "optimizer_updates",
        "seed",
        "worker_id",
        "observation_shapes",
        "action_size",
        "build_sha256",
        "source_identity",
        "protocol",
        "encoder",
        "solver",
        "fpo_source_commit",
        "policyflow_source_commit",
    )
    return _canonical({key: metadata[key] for key in stable_keys if key in metadata})


def _policy(
    protocol: DinoProtocol,
    *,
    hidden_sizes: Sequence[int],
    d_model: int,
    heads: int,
    inducing_points: int,
    layers: int,
    dropout: float,
    output_size: int,
) -> GaussianActorCritic:
    def encoder_factory() -> SetTransformerDinoEncoder:
        return SetTransformerDinoEncoder(
            protocol,
            d_model=d_model,
            heads=heads,
            inducing_points=inducing_points,
            layers=layers,
            dropout=dropout,
            output_size=output_size,
        )

    return GaussianActorCritic(
        observation_shapes=protocol.observation_shapes,
        action_size=protocol.action_size,
        hidden_sizes=tuple(int(size) for size in hidden_sizes),
        encoder_factory=encoder_factory,
    )


def _feasibility_policy(protocol: DinoProtocol, seed: int) -> GaussianActorCritic:
    with torch.random.fork_rng(devices=[]):
        torch.manual_seed(seed)
        return _policy(
            protocol,
            hidden_sizes=(32,),
            d_model=16,
            heads=2,
            inducing_points=4,
            layers=1,
            dropout=0.0,
            output_size=32,
        )


def _trained_module(
    protocol: DinoProtocol,
    checkpoint_path: Path,
) -> tuple[nn.Module, Mapping[str, Any], str, int | None]:
    payload = load_checkpoint(checkpoint_path, map_location="cpu")
    config = payload["config"]
    metadata = payload["metadata"]
    if not isinstance(config, Mapping) or not isinstance(metadata, Mapping):
        raise TypeError("checkpoint config and metadata must be mappings")
    if config.get("encoder_type") != "set_transformer":
        raise ValueError("checkpoint must use the set_transformer encoder")
    if _canonical(metadata.get("protocol")) != _canonical(
        protocol.checkpoint_metadata()
    ):
        raise ValueError("checkpoint protocol metadata does not match v2 manifest")
    if tuple(tuple(shape) for shape in metadata.get("observation_shapes", ())) != (
        protocol.observation_shapes
    ):
        raise ValueError("checkpoint observation shapes do not match v2")
    if int(metadata.get("action_size", -1)) != protocol.action_size:
        raise ValueError("checkpoint action size does not match v2")

    algorithm = metadata.get("algorithm")
    if algorithm in {"dino_parallel_fpo", "policyflow"}:
        def encoder_factory() -> SetTransformerDinoEncoder:
            return SetTransformerDinoEncoder(
                protocol,
                d_model=int(config["encoder_d_model"]),
                heads=int(config["encoder_heads"]),
                inducing_points=int(config["encoder_inducing_points"]),
                layers=int(config["encoder_layers"]),
                dropout=float(config["encoder_dropout"]),
                output_size=int(config["encoder_output_size"]),
            )

        policy_class = PolicyFlowActorCritic if algorithm == "policyflow" else FlowActorCritic
        extra = {"solver_steps": int(config["solver_steps"]), "std_init": float(config["std_init"])} if algorithm == "policyflow" else {}
        policy = policy_class(
            observation_shapes=protocol.observation_shapes,
            action_size=protocol.action_size,
            state_size=int(config["state_size"]),
            time_embedding_size=int(config["time_embedding_size"]),
            velocity_hidden_sizes=tuple(
                int(size) for size in config["velocity_hidden_sizes"]
            ),
            critic_hidden_sizes=tuple(
                int(size) for size in config["critic_hidden_sizes"]
            ),
            encoder_factory=encoder_factory,
            **extra,
        )
        policy.load_state_dict(payload["model_state"], strict=True)
        if algorithm == "policyflow":
            if payload["normalizer_state"].get("normalizer_type") != "identity":
                raise ValueError("PolicyFlow export requires identity normalization")
            nfe = 2 * int(config["solver_steps"])
            return _DeterministicPolicyFlowActor(policy, nfe=nfe), metadata, "zero_latent_midpoint_zero_delta", nfe
        nfe = int(metadata.get("solver", {}).get("nfe", config["nfe"]))
        return (
            _DeterministicFPOActor(policy, nfe=nfe),
            metadata,
            "zero_latent_euler_flow",
            nfe,
        )
    if algorithm not in {None, "dino_parallel_ppo"}:
        raise ValueError(f"unsupported trained checkpoint algorithm: {algorithm}")
    policy = _policy(
        protocol,
        hidden_sizes=tuple(int(size) for size in config["hidden_sizes"]),
        d_model=int(config["encoder_d_model"]),
        heads=int(config["encoder_heads"]),
        inducing_points=int(config["encoder_inducing_points"]),
        layers=int(config["encoder_layers"]),
        dropout=float(config["encoder_dropout"]),
        output_size=int(config["encoder_output_size"]),
    )
    policy.load_state_dict(payload["model_state"], strict=True)
    return _DeterministicActor(policy), metadata, "tanh_mean", None


def _export(
    *,
    output_path: Path,
    protocol_path: Path,
    module: nn.Module,
    deterministic_action: str,
    nfe: int | None,
    artifact_kind: str,
    seed: int | None,
    checkpoint_path: Path | None,
    checkpoint_metadata: Mapping[str, Any] | None,
) -> DinoOnnxExportResult:
    destination = Path(output_path).resolve()
    protocol_source = Path(protocol_path).resolve()
    destination.parent.mkdir(parents=True, exist_ok=True)
    protocol = DinoProtocol.from_yaml(protocol_source)
    if protocol.stream_names != tuple(name.capitalize() for name in INPUT_NAMES):
        raise ValueError("protocol stream order does not match the ONNX input contract")

    module = module.cpu().eval()
    dummy_inputs = tuple(
        torch.zeros((1, *shape), dtype=torch.float32)
        for shape in protocol.observation_shapes
    )
    dynamic_axes = {name: {0: "batch"} for name in INPUT_NAMES}
    dynamic_axes[OUTPUT_NAME] = {0: "batch"}
    with torch.inference_mode():
        torch.onnx.export(
            module,
            dummy_inputs,
            destination,
            export_params=True,
            do_constant_folding=True,
            input_names=list(INPUT_NAMES),
            output_names=[OUTPUT_NAME],
            dynamic_axes=dynamic_axes,
            opset_version=_ONNX_OPSET,
        )

    model_sha256 = _sha256(destination)
    manifest_path = destination.with_suffix(".manifest.json")
    manifest = {
        "schema_version": _EXPORT_SCHEMA_VERSION,
        "artifact_kind": artifact_kind,
        "protocol": {
            "version": protocol.protocol_version,
            "manifest_sha256": protocol.manifest_sha256,
            "metadata": _canonical(protocol.checkpoint_metadata()),
        },
        "model": {
            "file": destination.name,
            "sha256": model_sha256,
            "onnx_opset": _ONNX_OPSET,
            "deterministic_action": deterministic_action,
            **({} if nfe is None else {"nfe": nfe}),
        },
        "inputs": [
            {
                "name": name,
                "shape": list(shape),
                "runtime_shape": ["batch", *shape],
                "dtype": "float32",
            }
            for name, shape in zip(INPUT_NAMES, protocol.observation_shapes)
        ],
        "output": {
            "name": OUTPUT_NAME,
            "shape": ["batch", protocol.action_size],
            "dtype": "float32",
            "range": list(protocol.action_range),
        },
        "provenance": {
            "seed": seed,
            "checkpoint": None
            if checkpoint_path is None
            else {
                "file": str(checkpoint_path.resolve()),
                "sha256": _sha256(checkpoint_path),
                "metadata": _checkpoint_provenance(checkpoint_metadata),
            },
        },
    }
    manifest_path.write_text(
        json.dumps(manifest, indent=2, sort_keys=True) + "\n",
        encoding="utf-8",
    )
    return DinoOnnxExportResult(
        model_path=destination,
        manifest_path=manifest_path,
        model_sha256=model_sha256,
        protocol_sha256=protocol.manifest_sha256,
        artifact_kind=artifact_kind,
        module=module,
    )


def export_feasibility_fixture(
    *,
    output_path: Path,
    protocol_path: Path,
    seed: int = 20260830,
) -> DinoOnnxExportResult:
    protocol = DinoProtocol.from_yaml(protocol_path)
    return _export(
        output_path=output_path,
        protocol_path=protocol_path,
        module=_DeterministicActor(_feasibility_policy(protocol, seed)),
        deterministic_action="tanh_mean",
        nfe=None,
        artifact_kind=ARTIFACT_KIND_FEASIBILITY,
        seed=seed,
        checkpoint_path=None,
        checkpoint_metadata=None,
    )


def export_trained_checkpoint(
    *,
    output_path: Path,
    protocol_path: Path,
    checkpoint_path: Path,
) -> DinoOnnxExportResult:
    protocol = DinoProtocol.from_yaml(protocol_path)
    module, metadata, deterministic_action, nfe = _trained_module(
        protocol, Path(checkpoint_path)
    )
    return _export(
        output_path=output_path,
        protocol_path=protocol_path,
        module=module,
        deterministic_action=deterministic_action,
        nfe=nfe,
        artifact_kind=ARTIFACT_KIND_TRAINED,
        seed=None,
        checkpoint_path=Path(checkpoint_path),
        checkpoint_metadata=metadata,
    )
