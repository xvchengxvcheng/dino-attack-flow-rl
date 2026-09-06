from __future__ import annotations

import hashlib
import random
from pathlib import Path
from types import SimpleNamespace

import numpy as np
import torch

from flow_rl.algorithms.ppo import PPOUpdater
from flow_rl.data.normalization import IdentityObservationNormalizer
from flow_rl.envs.dino_protocol import DinoProtocol
from flow_rl.envs.parallel_unity import ParallelEnvEvent
from flow_rl.envs.types import EnvStep
from flow_rl.models.dino_set_transformer import SetTransformerDinoEncoder
from flow_rl.models.policy import GaussianActorCritic
from flow_rl.training.collector import PPOAuxiliary
from flow_rl.training.dino_parallel_ppo import DinoParallelPPOTrainer
from flow_rl.training.dino_parallel_ppo_config import (
    DinoParallelCheckpointManager,
    DinoParallelPPOConfig,
)
from flow_rl.training.versioned_collector import VersionedActionInfo


ROOT = Path(__file__).resolve().parents[3]
PROTOCOL_PATH = ROOT / "flow_rl" / "configs" / "dino_attack_structured_set_v2.yaml"


def _assert_nested_equal(actual, expected) -> None:
    if isinstance(expected, torch.Tensor):
        torch.testing.assert_close(actual.cpu(), expected.cpu(), rtol=0.0, atol=0.0)
    elif isinstance(expected, np.ndarray):
        np.testing.assert_array_equal(actual, expected)
    elif isinstance(expected, dict):
        assert set(actual) == set(expected)
        for key in expected:
            _assert_nested_equal(actual[key], expected[key])
    elif isinstance(expected, (tuple, list)):
        assert len(actual) == len(expected)
        for actual_item, expected_item in zip(actual, expected):
            _assert_nested_equal(actual_item, expected_item)
    else:
        assert actual == expected


class _Adapter:
    def __init__(self, environment_id: int) -> None:
        self.pending_agent_ids = np.asarray([environment_id + 1], dtype=np.int64)
        self.continuous_action_size = 4


def _observations(environment_id: int) -> tuple[np.ndarray, ...]:
    guards = np.zeros((1, 11, 7), dtype=np.float32)
    guards[:, : (8 if environment_id < 2 else 11), 0] = 1.0
    return (
        np.asarray([[1.0, 0.01, 1.0, 0.0, 0.0]], dtype=np.float32),
        np.zeros((1, 5, 8), dtype=np.float32),
        np.zeros((1, 6, 5), dtype=np.float32),
        guards,
        np.zeros((1, 8, 6), dtype=np.float32),
        np.zeros((1, 10, 7), dtype=np.float32),
    )


def _step(environment_id: int) -> EnvStep:
    return EnvStep(
        agent_ids=np.asarray([environment_id + 1], dtype=np.int64),
        observations=_observations(environment_id),
        rewards=np.asarray([1.0], dtype=np.float32),
        terminated=np.asarray([False], dtype=bool),
        truncated=np.asarray([False], dtype=bool),
    )


class _FakeVector:
    def __init__(self) -> None:
        self.handles = {
            environment_id: SimpleNamespace(generation=0)
            for environment_id in range(4)
        }
        self._ready: list[ParallelEnvEvent] = []
        self.closed = False

    def submit_actions(self, actions_by_environment) -> None:
        for environment_id in actions_by_environment:
            self._ready.append(
                ParallelEnvEvent(environment_id, 0, _step(environment_id), None)
            )

    def poll_ready(self, *, timeout=0.0, max_events=None):
        del timeout
        count = len(self._ready) if max_events is None else max_events
        events = tuple(self._ready[:count])
        del self._ready[:count]
        return events

    def restart_environment(self, environment_id):
        raise AssertionError(f"unexpected fake restart {environment_id}")

    def close(self) -> None:
        self.closed = True


def _config(
    tmp_path: Path,
    *,
    run_name: str,
    resume: Path | None = None,
    total_environment_steps: int = 4,
) -> DinoParallelPPOConfig:
    build = tmp_path / "DinoAttackDualMapTask8.exe"
    build.write_bytes(b"frozen-player")
    return DinoParallelPPOConfig(
        build_path=build,
        run_directory=tmp_path / run_name,
        protocol_path=PROTOCOL_PATH,
        total_environment_steps=total_environment_steps,
        schedule_environment_steps=32,
        rollout_size=4,
        batch_size=4,
        epochs=4,
        gamma=0.995,
        gae_lambda=0.95,
        learning_rate=3e-4,
        final_learning_rate=3e-4,
        clip_range=0.2,
        final_clip_range=0.2,
        entropy_coefficient=0.001,
        final_entropy_coefficient=0.001,
        value_coefficient=0.5,
        max_gradient_norm=0.5,
        hidden_sizes=(128, 128),
        checkpoint_interval=4,
        time_scale=20.0,
        device="cuda",
        num_envs=4,
        worker_base=160,
        seed=0,
        timeout_wait=120,
        behavior_name="DinoAttackPlanner",
        max_consecutive_failures=3,
        poll_timeout=0.1,
        max_consecutive_no_progress=3,
        resume_checkpoint=resume,
        encoder_type="set_transformer",
        encoder_d_model=48,
        encoder_heads=4,
        encoder_inducing_points=8,
        encoder_layers=1,
        encoder_dropout=0.0,
        encoder_output_size=128,
        normalize_observations=False,
    )


def _objects(config: DinoParallelPPOConfig, protocol: DinoProtocol):
    device = torch.device("cpu")
    policy = GaussianActorCritic(
        observation_shapes=protocol.observation_shapes,
        action_size=protocol.action_size,
        hidden_sizes=config.hidden_sizes,
        encoder_factory=lambda: SetTransformerDinoEncoder(
            protocol,
            d_model=48,
            heads=4,
            inducing_points=8,
            layers=1,
            dropout=0.0,
            output_size=128,
        ),
    ).to(device)
    action_generator = torch.Generator().manual_seed(config.seed + 1_000)
    update_generator = torch.Generator().manual_seed(config.seed + 2_000)
    updater = PPOUpdater(
        policy,
        learning_rate=config.learning_rate,
        final_learning_rate=config.final_learning_rate,
        clip_range=config.clip_range,
        final_clip_range=config.final_clip_range,
        entropy_coefficient=config.entropy_coefficient,
        final_entropy_coefficient=config.final_entropy_coefficient,
        value_coefficient=config.value_coefficient,
        max_gradient_norm=config.max_gradient_norm,
        # The schedule horizon remains frozen when a 40,960-step learning
        # check is resumed to 65,536 steps.
        total_environment_steps=config.schedule_environment_steps,
        batch_size=config.batch_size,
        epochs=config.epochs,
        generator=update_generator,
    )
    normalizer = IdentityObservationNormalizer(
        protocol.observation_shapes,
        protocol_manifest_sha256=protocol.manifest_sha256,
    )
    return device, policy, updater, normalizer, action_generator, update_generator


def _trainer(
    *,
    config,
    protocol,
    device,
    policy,
    updater,
    action_generator,
    initial_steps,
    initial_policy_version,
    initial_environment_steps,
    callback,
):
    vector = _FakeVector()
    adapters = {environment_id: _Adapter(environment_id) for environment_id in range(4)}

    def decide(environment_id, generation, policy_version, step, adapter):
        observations = tuple(
            torch.as_tensor(np.array(item, copy=True), device=device)
            for item in step.observations
        )
        with torch.inference_mode():
            output = policy.act(
                observations,
                deterministic=False,
                generator=action_generator,
            )
        return VersionedActionInfo(
            environment_id=environment_id,
            process_generation=generation,
            policy_version=policy_version,
            agent_ids=adapter.pending_agent_ids,
            observations=step.observations,
            actions=output.actions.cpu().numpy().astype(np.float32),
            values=output.values.cpu().numpy().astype(np.float32),
            auxiliaries=tuple(
                PPOAuxiliary(float(value))
                for value in output.log_probs.cpu().numpy()
            ),
        )

    trainer = DinoParallelPPOTrainer(
        vector_env=vector,
        adapters=adapters,
        initial_steps=initial_steps,
        updater=updater,
        device=device,
        rollout_size=config.rollout_size,
        total_environment_steps=config.total_environment_steps,
        initial_environment_steps=initial_environment_steps,
        gamma=config.gamma,
        gae_lambda=config.gae_lambda,
        decide=decide,
        bootstrap=lambda collector: {
            target: 0.0 for target in collector.bootstrap_targets
        },
        map_classifier=lambda step, count: (
            "map1" if int(np.count_nonzero(step.observations[3][0, :, 0])) == 8 else "map2"
        ),
        restart_state_resolver=lambda environment_id, handle: (_ for _ in ()).throw(
            AssertionError("unexpected restart")
        ),
        poll_timeout=config.poll_timeout,
        max_consecutive_no_progress=config.max_consecutive_no_progress,
        initial_policy_version=initial_policy_version,
        on_update_completed=callback,
    )
    return trainer, vector


def test_fake_four_environment_checkpoint_resume_restores_complete_boundary_state(
    tmp_path: Path,
) -> None:
    protocol = DinoProtocol.from_yaml(PROTOCOL_PATH)
    initial_config = _config(tmp_path, run_name="initial")
    initial_config.validate()
    initial_config.run_directory.mkdir()
    initial_objects = _objects(initial_config, protocol)
    device, policy, updater, normalizer, action_generator, update_generator = initial_objects
    manager = DinoParallelCheckpointManager(
        config=initial_config,
        protocol=protocol,
        policy=policy,
        updater=updater,
        normalizer=normalizer,
        action_generator=action_generator,
        update_generator=update_generator,
    )
    checkpoint = initial_config.run_directory / "checkpoints" / "boundary-initial.pt"

    def save_initial(update, absolute_steps):
        manager.save(
            checkpoint,
            environment_steps=absolute_steps,
            policy_version=update.policy_version + 1,
            optimizer_updates=1,
            wall_clock_seconds=1.25,
        )

    initial_steps = {environment_id: _step(environment_id) for environment_id in range(4)}
    first, first_vector = _trainer(
        config=initial_config,
        protocol=protocol,
        device=device,
        policy=policy,
        updater=updater,
        action_generator=action_generator,
        initial_steps=initial_steps,
        initial_policy_version=0,
        initial_environment_steps=0,
        callback=save_initial,
    )
    first_summary = first.train()

    first_steps = first_summary.final_environment_steps
    assert 4 <= first_steps <= 7
    assert first_vector.closed
    assert checkpoint.is_file()
    source_payload = torch.load(checkpoint, map_location="cpu", weights_only=False)

    resumed_target = first_steps + 4
    resumed_config = _config(
        tmp_path,
        run_name="resumed",
        resume=checkpoint,
        total_environment_steps=resumed_target,
    )
    resumed_config.validate()
    resumed_config.run_directory.mkdir()
    resumed_objects = _objects(resumed_config, protocol)
    (
        resumed_device,
        resumed_policy,
        resumed_updater,
        resumed_normalizer,
        resumed_action_generator,
        resumed_update_generator,
    ) = resumed_objects
    resumed_manager = DinoParallelCheckpointManager(
        config=resumed_config,
        protocol=protocol,
        policy=resumed_policy,
        updater=resumed_updater,
        normalizer=resumed_normalizer,
        action_generator=resumed_action_generator,
        update_generator=resumed_update_generator,
    )
    random.seed(999)
    np.random.seed(999)
    torch.manual_seed(999)
    resumed_action_generator.manual_seed(999)
    resumed_update_generator.manual_seed(999)
    state = resumed_manager.restore(checkpoint)

    assert state.environment_steps == first_steps
    assert state.policy_version == 1
    assert state.optimizer_updates == 1
    assert state.wall_clock_seconds == 1.25
    _assert_nested_equal(resumed_policy.state_dict(), source_payload["model_state"])
    _assert_nested_equal(
        resumed_updater.optimizer.state_dict(), source_payload["optimizer_state"]
    )
    _assert_nested_equal(
        resumed_normalizer.state_dict(), source_payload["normalizer_state"]
    )
    assert random.getstate() == source_payload["metadata"]["python_random_state"]
    _assert_nested_equal(
        np.random.get_state(), source_payload["metadata"]["numpy_random_state"]
    )
    torch.testing.assert_close(
        torch.get_rng_state(), source_payload["metadata"]["torch_rng_state"]
    )
    torch.testing.assert_close(
        resumed_action_generator.get_state(),
        source_payload["metadata"]["action_generator_state"],
    )
    torch.testing.assert_close(
        resumed_update_generator.get_state(),
        source_payload["metadata"]["update_generator_state"],
    )

    final_checkpoint = resumed_config.run_directory / "checkpoints" / "final.pt"

    def save_resumed(update, absolute_steps):
        resumed_manager.save(
            final_checkpoint,
            environment_steps=absolute_steps,
            policy_version=update.policy_version + 1,
            optimizer_updates=state.optimizer_updates + 1,
            wall_clock_seconds=state.wall_clock_seconds + 0.5,
        )

    second, second_vector = _trainer(
        config=resumed_config,
        protocol=protocol,
        device=resumed_device,
        policy=resumed_policy,
        updater=resumed_updater,
        action_generator=resumed_action_generator,
        initial_steps=initial_steps,
        initial_policy_version=state.policy_version,
        initial_environment_steps=state.environment_steps,
        callback=save_resumed,
    )
    second_summary = second.train()

    payload = torch.load(final_checkpoint, map_location="cpu", weights_only=False)
    metadata = payload["metadata"]
    assert resumed_target <= second_summary.final_environment_steps <= resumed_target + 3
    assert second_vector.closed
    assert metadata["environment_steps"] == second_summary.final_environment_steps
    assert metadata["policy_version"] == 2
    assert metadata["optimizer_updates"] == 2
    assert metadata["rollout_boundary"] is True
    assert metadata["in_flight_decisions"] == 0
    assert metadata["protocol"] == protocol.checkpoint_metadata()
    assert metadata["build_sha256"] == hashlib.sha256(
        resumed_config.build_path.read_bytes()
    ).hexdigest()
    assert metadata["build_path"] == str(resumed_config.build_path.resolve())
    assert metadata["resume_source"]["path"] == str(checkpoint.resolve())
    assert payload["optimizer_state"]["state"]
    assert payload["normalizer_state"] == resumed_normalizer.state_dict()
    assert "python_random_state" in metadata
    assert "numpy_random_state" in metadata
    assert "torch_rng_state" in metadata
    assert "action_generator_state" in metadata
    assert "update_generator_state" in metadata

    # The next random values are part of the resumed state, not a fresh seed.
    assert isinstance(random.random(), float)
    assert np.isfinite(np.random.random())
