from __future__ import annotations

import hashlib
from pathlib import Path

import numpy as np
import pytest
import torch

from flow_rl.data.normalization import IdentityObservationNormalizer
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.envs.types import EnvStep
from flow_rl.evaluation.dino_parallel_ppo import evaluate_dino_parallel_ppo
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.models.flow import ConditionalVelocityMLP
from flow_rl.models.fpo_policy import FlowActorCritic
from flow_rl.models.policy import GaussianActorCritic
from flow_rl.tracking.checkpoint import FlowSolverConfig, save_checkpoint


ROOT = Path(__file__).resolve().parents[3]
PROTOCOL_PATH = ROOT / "flow_rl" / "configs" / "dino_attack_structured_set_v2.yaml"


def _streams(map_name: str, *, food: float = 0.01, remaining: float = 1.0):
    guards = np.zeros((1, 11, 7), dtype=np.float32)
    guards[:, : (8 if map_name == "map1" else 11), 0] = 1.0
    return (
        np.asarray([[remaining, food, 1.0, 0.0, 0.0]], dtype=np.float32),
        np.zeros((1, 5, 8), dtype=np.float32),
        np.zeros((1, 6, 5), dtype=np.float32),
        guards,
        np.zeros((1, 8, 6), dtype=np.float32),
        np.zeros((1, 10, 7), dtype=np.float32),
    )


def _merge_rows(first, second):
    return tuple(np.concatenate((a, b), axis=0) for a, b in zip(first, second))


class _AlternatingMapAdapter:
    def __init__(self) -> None:
        self.pending_agent_ids = np.asarray([7], dtype=np.int64)
        self.maps = ("map1", "map2", "map1", "map2")
        self.returns = (12.0, -1.0, 11.0, 10.0)
        self.food = (0.04, 0.01, 0.02, 0.03)
        self.remaining = (0.75, 0.0, 0.5, 0.25)
        self.index = 0
        self.actions: list[np.ndarray] = []
        self.close_count = 0

    def __enter__(self):
        return self

    def __exit__(self, exc_type, exc, traceback) -> None:
        self.close_count += 1

    def reset(self) -> EnvStep:
        return EnvStep(
            agent_ids=np.asarray([7], dtype=np.int64),
            observations=_streams(self.maps[0]),
            rewards=np.asarray([0.0], dtype=np.float32),
            terminated=np.asarray([False]),
            truncated=np.asarray([False]),
        )

    def step(self, actions: np.ndarray) -> EnvStep:
        self.actions.append(actions.copy())
        ended = self.index
        next_index = min(ended + 1, len(self.maps) - 1)
        decision = _streams(self.maps[next_index])
        terminal = _streams(
            self.maps[ended],
            food=self.food[ended],
            remaining=self.remaining[ended],
        )
        self.index = next_index
        return EnvStep(
            # New-episode decision comes first, old terminal comes second.
            agent_ids=np.asarray([7, 7], dtype=np.int64),
            observations=_merge_rows(decision, terminal),
            rewards=np.asarray([0.0, self.returns[ended]], dtype=np.float32),
            terminated=np.asarray([False, True]),
            truncated=np.asarray([False, False]),
        )


class _DelayedMapEvaluationAdapter(_AlternatingMapAdapter):
    def __init__(self) -> None:
        super().__init__()
        self.warmed_up = False

    def reset(self) -> EnvStep:
        streams = list(_streams("map1"))
        streams[3] = np.zeros((1, 11, 7), dtype=np.float32)
        return EnvStep(
            agent_ids=np.asarray([7], dtype=np.int64),
            observations=tuple(streams),
            rewards=np.asarray([0.0], dtype=np.float32),
            terminated=np.asarray([False]),
            truncated=np.asarray([False]),
        )

    def step(self, actions: np.ndarray) -> EnvStep:
        if not self.warmed_up:
            self.warmed_up = True
            self.actions.append(actions.copy())
            return EnvStep(
                agent_ids=np.asarray([7], dtype=np.int64),
                observations=_streams("map1"),
                rewards=np.asarray([0.0], dtype=np.float32),
                terminated=np.asarray([False]),
                truncated=np.asarray([False]),
            )
        return super().step(actions)


class _TerminalOnlyGapAdapter(_AlternatingMapAdapter):
    def __init__(self) -> None:
        super().__init__()
        self.awaiting_next_decision = False
        self.empty_action_steps = 0

    def step(self, actions: np.ndarray) -> EnvStep:
        if self.awaiting_next_decision:
            assert actions.shape == (0, 4)
            self.empty_action_steps += 1
            self.awaiting_next_decision = False
            self.pending_agent_ids = np.asarray([7], dtype=np.int64)
            return EnvStep(
                agent_ids=np.asarray([7], dtype=np.int64),
                observations=_streams(self.maps[self.index]),
                rewards=np.asarray([0.0], dtype=np.float32),
                terminated=np.asarray([False]),
                truncated=np.asarray([False]),
            )

        self.actions.append(actions.copy())
        ended = self.index
        self.index = min(ended + 1, len(self.maps) - 1)
        self.awaiting_next_decision = True
        self.pending_agent_ids = np.empty(0, dtype=np.int64)
        return EnvStep(
            agent_ids=np.asarray([7], dtype=np.int64),
            observations=_streams(
                self.maps[ended],
                food=self.food[ended],
                remaining=self.remaining[ended],
            ),
            rewards=np.asarray([self.returns[ended]], dtype=np.float32),
            terminated=np.asarray([True]),
            truncated=np.asarray([False]),
        )


class _DamagedGuardTerminalAdapter(_AlternatingMapAdapter):
    def step(self, actions: np.ndarray) -> EnvStep:
        self.actions.append(actions.copy())
        ended = self.index
        next_index = min(ended + 1, len(self.maps) - 1)
        decision = _streams(self.maps[next_index])
        terminal = list(
            _streams(
                self.maps[ended],
                food=self.food[ended],
                remaining=self.remaining[ended],
            )
        )
        terminal[3] = np.zeros((1, 11, 7), dtype=np.float32)
        terminal[3][:, :6, 0] = 1.0
        self.index = next_index
        return EnvStep(
            agent_ids=np.asarray([7, 7], dtype=np.int64),
            observations=_merge_rows(decision, tuple(terminal)),
            rewards=np.asarray([0.0, self.returns[ended]], dtype=np.float32),
            terminated=np.asarray([False, True]),
            truncated=np.asarray([False, False]),
        )


def _checkpoint(tmp_path: Path):
    protocol = DinoProtocol.from_yaml(PROTOCOL_PATH)
    build = tmp_path / "DinoAttackDualMapPPOV2.exe"
    build.write_bytes(b"updated-two-map-player")
    policy = GaussianActorCritic(
        observation_shapes=protocol.observation_shapes,
        action_size=protocol.action_size,
        hidden_sizes=(16,),
        encoder_factory=lambda: SetTransformerDinoEncoder(
            protocol,
            d_model=64,
            heads=4,
            inducing_points=8,
            layers=2,
            dropout=0.0,
            output_size=256,
        ),
    )
    encoder = policy.actor.encoder.checkpoint_metadata()
    checkpoint = tmp_path / "trained.pt"
    save_checkpoint(
        checkpoint,
        model_state=policy.state_dict(),
        optimizer_state={"state": {}},
        normalizer_state=IdentityObservationNormalizer(
            protocol.observation_shapes,
            protocol_manifest_sha256=protocol.manifest_sha256,
        ).state_dict(),
        config={
            "protocol_path": str(PROTOCOL_PATH),
            "hidden_sizes": [16],
            "encoder_type": "set_transformer",
            "encoder_d_model": 64,
            "encoder_heads": 4,
            "encoder_inducing_points": 8,
            "encoder_layers": 2,
            "encoder_dropout": 0.0,
            "encoder_output_size": 256,
            "time_scale": 20.0,
            "timeout_wait": 120,
            "behavior_name": "DinoAttackPlanner",
        },
        metadata={
            "schema_version": 3,
            "algorithm": "dino_parallel_ppo",
            "observation_shapes": protocol.observation_shapes,
            "action_size": protocol.action_size,
            "build_sha256": hashlib.sha256(build.read_bytes()).hexdigest(),
            "protocol": protocol.checkpoint_metadata(),
            "encoder": encoder,
            "environment_steps": 40_960,
        },
    )
    return checkpoint, build


def _flow_bc_checkpoint(tmp_path: Path):
    protocol = DinoProtocol.from_yaml(PROTOCOL_PATH)
    build = tmp_path / "DinoAttackDualMapFlowBC.exe"
    build.write_bytes(b"structured-flow-bc-player")

    def encoder_factory() -> SetTransformerDinoEncoder:
        return SetTransformerDinoEncoder(
            protocol,
            d_model=48,
            heads=4,
            inducing_points=8,
            layers=1,
            dropout=0.0,
            output_size=128,
        )

    model = ConditionalVelocityMLP(
        observation_shapes=protocol.observation_shapes,
        action_size=protocol.action_size,
        state_size=128,
        time_embedding_size=32,
        hidden_sizes=(128, 128),
        state_encoder=encoder_factory(),
    )
    for parameter in model.parameters():
        torch.nn.init.zeros_(parameter)
    encoder = model.state_encoder.checkpoint_metadata()
    checkpoint = tmp_path / "flow-bc.pt"
    save_checkpoint(
        checkpoint,
        model_state=model.state_dict(),
        optimizer_state={"state": {}},
        normalizer_state=IdentityObservationNormalizer(
            protocol.observation_shapes,
            protocol_manifest_sha256=protocol.manifest_sha256,
        ).state_dict(),
        config={
            "protocol_path": str(PROTOCOL_PATH),
            "encoder_type": "set_transformer",
            "encoder_d_model": 48,
            "encoder_heads": 4,
            "encoder_inducing_points": 8,
            "encoder_layers": 1,
            "encoder_dropout": 0.0,
            "encoder_output_size": 128,
            "state_size": 128,
            "time_embedding_size": 32,
            "velocity_hidden_sizes": [128, 128],
            "nfe": 4,
        },
        metadata={
            "schema_version": 3,
            "algorithm": "flow_bc",
            "dataset_sha256": "synthetic-dataset",
            "metadata_sha256": "synthetic-metadata",
            "build_sha256": hashlib.sha256(build.read_bytes()).hexdigest(),
            "observation_shapes": protocol.observation_shapes,
            "action_size": protocol.action_size,
            "solver": FlowSolverConfig(nfe=4, action_transform="clamp").to_dict(),
            "protocol": protocol.checkpoint_metadata(),
            "encoder": encoder,
        },
    )
    return checkpoint, build


def _fpo_checkpoint(tmp_path: Path):
    protocol = DinoProtocol.from_yaml(PROTOCOL_PATH)
    build = tmp_path / "DinoAttackDualMapFPOV2.exe"
    build.write_bytes(b"structured-fpo-player")

    def encoder_factory() -> SetTransformerDinoEncoder:
        return SetTransformerDinoEncoder(
            protocol,
            d_model=48,
            heads=4,
            inducing_points=8,
            layers=1,
            dropout=0.0,
            output_size=128,
        )

    policy = FlowActorCritic(
        observation_shapes=protocol.observation_shapes,
        action_size=protocol.action_size,
        state_size=128,
        time_embedding_size=32,
        velocity_hidden_sizes=(128, 128),
        critic_hidden_sizes=(128, 128),
        encoder_factory=encoder_factory,
    )
    checkpoint = tmp_path / "fpo.pt"
    save_checkpoint(
        checkpoint,
        model_state=policy.state_dict(),
        optimizer_state={"actor": {}, "critic": {}},
        normalizer_state=IdentityObservationNormalizer(
            protocol.observation_shapes,
            protocol_manifest_sha256=protocol.manifest_sha256,
        ).state_dict(),
        config={
            "protocol_path": str(PROTOCOL_PATH),
            "encoder_type": "set_transformer",
            "encoder_d_model": 48,
            "encoder_heads": 4,
            "encoder_inducing_points": 8,
            "encoder_layers": 1,
            "encoder_dropout": 0.0,
            "encoder_output_size": 128,
            "state_size": 128,
            "time_embedding_size": 32,
            "velocity_hidden_sizes": [128, 128],
            "critic_hidden_sizes": [128, 128],
            "nfe": 4,
            "time_scale": 1.0,
            "timeout_wait": 120,
            "behavior_name": "DinoAttackPlanner",
        },
        metadata={
            "schema_version": 3,
            "algorithm": "dino_parallel_fpo",
            "observation_shapes": protocol.observation_shapes,
            "action_size": protocol.action_size,
            "build_sha256": hashlib.sha256(build.read_bytes()).hexdigest(),
            "protocol": protocol.checkpoint_metadata(),
            "encoder": policy.actor.state_encoder.checkpoint_metadata(),
            "environment_steps": 2_606_819,
            "solver": {"name": "euler", "nfe": 4},
        },
    )
    return checkpoint, build


def test_structured_fpo_checkpoint_uses_seeded_flow_evaluator(
    tmp_path: Path,
) -> None:
    checkpoint, build = _fpo_checkpoint(tmp_path)
    adapters = [_AlternatingMapAdapter(), _AlternatingMapAdapter()]

    summaries = [
        evaluate_dino_parallel_ppo(
            checkpoint_path=checkpoint,
            build_path=build,
            output_directory=tmp_path / f"fpo-evaluation-{index}",
            episodes_per_map=2,
            training_worker_ids=(3000, 3001),
            evaluation_worker_id=370,
            evaluation_seed=101,
            device="cpu",
            action_mode="stochastic",
            policy_seed=160_000,
            adapter_factory=lambda _, adapter=adapter: adapter,
        )
        for index, adapter in enumerate(adapters)
    ]

    for first, second in zip(adapters[0].actions, adapters[1].actions):
        np.testing.assert_array_equal(first, second)
    assert summaries[0].checkpoint_algorithm == "dino_parallel_fpo"
    assert summaries[0].velocity_nfe == 4
    assert summaries[0].solver_steps is None
    assert summaries[0].all_finite


def test_structured_flow_bc_checkpoint_uses_frozen_balanced_dino_evaluator(
    tmp_path: Path,
) -> None:
    checkpoint, build = _flow_bc_checkpoint(tmp_path)
    adapter = _AlternatingMapAdapter()

    summary = evaluate_dino_parallel_ppo(
        checkpoint_path=checkpoint,
        build_path=build,
        output_directory=tmp_path / "flow-bc-evaluation",
        episodes_per_map=2,
        training_worker_ids=(350, 351, 352, 353),
        evaluation_worker_id=370,
        evaluation_seed=101,
        device="cpu",
        adapter_factory=lambda _: adapter,
    )

    assert summary.episodes == 4
    assert summary.truncated_episodes == 0
    assert summary.all_finite
    assert {item.map_name: item.episodes for item in summary.maps} == {
        "map1": 2,
        "map2": 2,
    }
    assert all(np.isfinite(action).all() for action in adapter.actions)
    assert all(np.max(np.abs(action)) <= 1.0 for action in adapter.actions)
    np.testing.assert_array_equal(adapter.actions[0], adapter.actions[2])


def test_structured_evaluation_is_deterministic_frozen_and_balanced_by_terminal_map(
    tmp_path: Path,
) -> None:
    checkpoint, build = _checkpoint(tmp_path)
    adapter = _AlternatingMapAdapter()

    summary = evaluate_dino_parallel_ppo(
        checkpoint_path=checkpoint,
        build_path=build,
        output_directory=tmp_path / "evaluation",
        episodes_per_map=2,
        training_worker_ids=(350, 351, 352, 353),
        evaluation_worker_id=370,
        evaluation_seed=101,
        device="cpu",
        adapter_factory=lambda _: adapter,
    )

    assert summary.episodes == 4
    assert summary.truncated_episodes == 0
    assert summary.overall_wins == 3
    assert summary.overall_win_rate == pytest.approx(0.75)
    by_map = {item.map_name: item for item in summary.maps}
    assert by_map["map1"].episodes == 2
    assert by_map["map1"].wins == 2
    assert by_map["map1"].mean_final_meat == pytest.approx(150.0)
    assert by_map["map1"].mean_elapsed_seconds == pytest.approx(18.75)
    assert by_map["map2"].episodes == 2
    assert by_map["map2"].wins == 1
    assert adapter.close_count == 1
    assert len(adapter.actions) == 4
    np.testing.assert_array_equal(adapter.actions[0], adapter.actions[2])
    assert summary.normalizer_type == "identity"
    assert summary.protocol_version == "dino_attack_structured_set_v2"
    assert summary.all_finite
    assert (tmp_path / "evaluation" / "evaluation.json").is_file()


def test_structured_evaluation_injects_and_records_python_response_delay(
    tmp_path: Path, monkeypatch: pytest.MonkeyPatch
) -> None:
    checkpoint, build = _checkpoint(tmp_path)
    adapter = _AlternatingMapAdapter()
    sleep_calls: list[float] = []
    monkeypatch.setattr(
        "flow_rl.evaluation.dino_parallel_ppo.time.sleep", sleep_calls.append
    )

    summary = evaluate_dino_parallel_ppo(
        checkpoint_path=checkpoint,
        build_path=build,
        output_directory=tmp_path / "delayed-response-evaluation",
        episodes_per_map=2,
        training_worker_ids=(350, 351, 352, 353),
        evaluation_worker_id=370,
        evaluation_seed=101,
        device="cpu",
        python_response_delay_seconds=0.075,
        adapter_factory=lambda _: adapter,
    )

    assert sleep_calls == [0.075] * 4
    assert summary.python_response_delay_seconds == pytest.approx(0.075)


@pytest.mark.parametrize("delay", [-0.001, float("nan"), float("inf")])
def test_structured_evaluation_rejects_invalid_python_response_delay(
    tmp_path: Path, delay: float
) -> None:
    checkpoint, build = _checkpoint(tmp_path)

    with pytest.raises(ValueError, match="response delay"):
        evaluate_dino_parallel_ppo(
            checkpoint_path=checkpoint,
            build_path=build,
            output_directory=tmp_path / "invalid-delay-evaluation",
            episodes_per_map=1,
            training_worker_ids=(350, 351, 352, 353),
            evaluation_worker_id=370,
            evaluation_seed=101,
            device="cpu",
            python_response_delay_seconds=delay,
            adapter_factory=lambda _: _AlternatingMapAdapter(),
        )


def test_structured_stochastic_evaluation_is_seeded_and_records_action_mode(
    tmp_path: Path,
) -> None:
    checkpoint, build = _checkpoint(tmp_path)
    adapters = [_AlternatingMapAdapter() for _ in range(3)]

    summaries = [
        evaluate_dino_parallel_ppo(
            checkpoint_path=checkpoint,
            build_path=build,
            output_directory=tmp_path / f"stochastic-{index}",
            episodes_per_map=2,
            training_worker_ids=(350, 351, 352, 353),
            evaluation_worker_id=370,
            evaluation_seed=101,
            device="cpu",
            action_mode="stochastic",
            policy_seed=seed,
            adapter_factory=lambda _, adapter=adapter: adapter,
        )
        for index, (seed, adapter) in enumerate(
            zip((7001, 7001, 7002), adapters)
        )
    ]

    for first, second in zip(adapters[0].actions, adapters[1].actions):
        np.testing.assert_array_equal(first, second)
    assert any(
        not np.array_equal(first, different)
        for first, different in zip(adapters[0].actions, adapters[2].actions)
    )
    assert summaries[0].action_mode == "stochastic"
    assert summaries[0].policy_seed == 7001
    assert summaries[0].deterministic is False


def test_structured_evaluation_rejects_training_worker_collision_and_nonempty_output(
    tmp_path: Path,
) -> None:
    checkpoint, build = _checkpoint(tmp_path)
    with pytest.raises(ValueError, match="worker"):
        evaluate_dino_parallel_ppo(
            checkpoint_path=checkpoint,
            build_path=build,
            output_directory=tmp_path / "unused",
            episodes_per_map=1,
            training_worker_ids=(350, 351, 352, 353),
            evaluation_worker_id=352,
            evaluation_seed=1,
            device="cpu",
            adapter_factory=lambda _: _AlternatingMapAdapter(),
        )
    output = tmp_path / "existing"
    output.mkdir()
    (output / "keep.txt").write_text("keep", encoding="utf-8")
    with pytest.raises(FileExistsError, match="not empty"):
        evaluate_dino_parallel_ppo(
            checkpoint_path=checkpoint,
            build_path=build,
            output_directory=output,
            episodes_per_map=1,
            training_worker_ids=(350, 351, 352, 353),
            evaluation_worker_id=370,
            evaluation_seed=1,
            device="cpu",
            adapter_factory=lambda _: _AlternatingMapAdapter(),
        )


def test_structured_evaluation_waits_for_map_layout_before_counting_policy_steps(
    tmp_path: Path,
) -> None:
    checkpoint, build = _checkpoint(tmp_path)
    adapter = _DelayedMapEvaluationAdapter()

    summary = evaluate_dino_parallel_ppo(
        checkpoint_path=checkpoint,
        build_path=build,
        output_directory=tmp_path / "delayed-evaluation",
        episodes_per_map=2,
        training_worker_ids=(360, 361, 362, 363),
        evaluation_worker_id=370,
        evaluation_seed=101,
        device="cpu",
        adapter_factory=lambda _: adapter,
    )

    np.testing.assert_array_equal(
        adapter.actions[0], np.asarray([[0.0, 0.0, 0.0, -1.0]], dtype=np.float32)
    )
    assert summary.environment_steps == 4
    assert summary.episodes == 4
    assert adapter.close_count == 1


def test_structured_evaluation_advances_terminal_only_ticks_with_empty_actions(
    tmp_path: Path,
) -> None:
    checkpoint, build = _checkpoint(tmp_path)
    adapter = _TerminalOnlyGapAdapter()

    summary = evaluate_dino_parallel_ppo(
        checkpoint_path=checkpoint,
        build_path=build,
        output_directory=tmp_path / "terminal-gap-evaluation",
        episodes_per_map=2,
        training_worker_ids=(360, 361, 362, 363),
        evaluation_worker_id=370,
        evaluation_seed=101,
        device="cpu",
        adapter_factory=lambda _: adapter,
    )

    assert summary.environment_steps == 4
    assert summary.episodes == 4
    assert adapter.empty_action_steps == 3
    assert adapter.close_count == 1


def test_structured_evaluation_keeps_episode_map_when_terminal_guards_have_died(
    tmp_path: Path,
) -> None:
    checkpoint, build = _checkpoint(tmp_path)
    adapter = _DamagedGuardTerminalAdapter()

    summary = evaluate_dino_parallel_ppo(
        checkpoint_path=checkpoint,
        build_path=build,
        output_directory=tmp_path / "damaged-guard-evaluation",
        episodes_per_map=2,
        training_worker_ids=(360, 361, 362, 363),
        evaluation_worker_id=370,
        evaluation_seed=101,
        device="cpu",
        adapter_factory=lambda _: adapter,
    )

    assert {item.map_name: item.episodes for item in summary.maps} == {
        "map1": 2,
        "map2": 2,
    }
    assert summary.episodes == 4
    assert adapter.close_count == 1
