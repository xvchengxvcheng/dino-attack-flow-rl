from __future__ import annotations

import numpy as np
import pytest
import torch

from flow_rl.envs.parallel_unity import ParallelEnvEvent
from flow_rl.envs.types import EnvStep
from flow_rl.training.collector import PPOAuxiliary
from flow_rl.training.fpo_collector import FPOAuxiliary
from flow_rl.training.fpo_trainer import update_versioned_fpo
from flow_rl.training.trainer import update_versioned_ppo
from flow_rl.training.versioned_collector import (
    AgentIdentity,
    VersionedActionInfo,
    VersionedCollector,
)


def _step(
    agent_id: int,
    observation: float,
    *,
    reward: float = 1.0,
    terminated: bool = False,
    truncated: bool = False,
) -> EnvStep:
    return EnvStep(
        agent_ids=np.asarray([agent_id], dtype=np.int64),
        observations=(np.asarray([[observation]], dtype=np.float32),),
        rewards=np.asarray([reward], dtype=np.float32),
        terminated=np.asarray([terminated], dtype=bool),
        truncated=np.asarray([truncated], dtype=bool),
    )


def _action(
    environment_id: int,
    generation: int,
    version: int,
    agent_id: int,
    observation: float,
    value: float,
    auxiliary: str,
) -> VersionedActionInfo[str]:
    return VersionedActionInfo(
        environment_id=environment_id,
        process_generation=generation,
        policy_version=version,
        agent_ids=np.asarray([agent_id], dtype=np.int64),
        observations=(np.asarray([[observation]], dtype=np.float32),),
        actions=np.asarray([[observation / 10.0]], dtype=np.float32),
        values=np.asarray([value], dtype=np.float32),
        auxiliaries=(auxiliary,),
    )


def _event(
    environment_id: int,
    generation: int,
    step: EnvStep | None = None,
    error: BaseException | None = None,
) -> ParallelEnvEvent:
    return ParallelEnvEvent(environment_id, generation, step, error)


def test_composite_environment_agent_identity_prevents_agent_id_collision() -> None:
    collector: VersionedCollector[str] = VersionedCollector(target_transitions=2)
    collector.begin(4)
    collector.record(_event(0, 0, _step(7, 1.0)), _action(0, 0, 4, 7, 0.0, 10.0, "env0"))
    collector.record(_event(1, 0, _step(7, 2.0)), _action(1, 0, 4, 7, 0.0, 20.0, "env1"))

    assert not collector.accepting_submissions
    collector.seal_and_bootstrap(
        {
            AgentIdentity(0, 0, 7): 11.0,
            AgentIdentity(1, 0, 7): 22.0,
        }
    )
    batch = collector.build_batch()

    assert batch.policy_version == 4
    assert [transition.identity.environment_id for transition in batch.transitions] == [0, 1]
    assert [transition.next_value for transition in batch.transitions] == [11.0, 22.0]
    assert [transition.auxiliary for transition in batch.transitions] == ["env0", "env1"]


def test_barrier_rejects_mixed_versions_bootstraps_only_nonterminal_fragments() -> None:
    collector: VersionedCollector[str] = VersionedCollector(target_transitions=2)
    collector.begin(8)
    collector.record(
        _event(0, 0, _step(3, 1.0, terminated=True)),
        _action(0, 0, 8, 3, 0.0, 4.0, "terminal"),
    )
    with pytest.raises(ValueError, match="mixed policy version"):
        collector.record(
            _event(1, 0, _step(4, 2.0)),
            _action(1, 0, 9, 4, 0.0, 5.0, "mixed"),
        )
    collector.record(
        _event(1, 0, _step(4, 2.0)),
        _action(1, 0, 8, 4, 0.0, 5.0, "open"),
    )

    collector.seal_and_bootstrap({AgentIdentity(1, 0, 4): 9.0})
    batch = collector.build_batch()
    terminal, open_fragment = batch.transitions
    assert terminal.terminated and terminal.next_value == 0.0
    assert not open_fragment.terminated and open_fragment.next_value == 9.0


def test_infrastructure_failure_discards_fragment_without_fabricating_observation() -> None:
    collector: VersionedCollector[str] = VersionedCollector(target_transitions=3)
    collector.begin(2)
    collector.record(_event(0, 0, _step(7, 1.0)), _action(0, 0, 2, 7, 0.0, 1.0, "lost"))
    collector.record(
        _event(0, 0, error=ConnectionError("player exited")),
        _action(0, 0, 2, 7, 1.0, 2.0, "in-flight"),
    )
    collector.record(_event(1, 0, _step(7, 3.0)), _action(1, 0, 2, 7, 2.0, 3.0, "kept"))

    assert collector.accepted_transition_count == 1
    collector.seal_and_bootstrap({AgentIdentity(1, 0, 7): 4.0})
    batch = collector.build_batch()
    assert [transition.auxiliary for transition in batch.transitions] == ["kept"]
    assert len(batch.diagnostics) == 1
    diagnostic = batch.diagnostics[0]
    assert diagnostic.environment_id == 0
    assert diagnostic.process_generation == 0
    assert diagnostic.infrastructure_truncated
    assert not diagnostic.final_observation_available
    assert diagnostic.discarded_transitions == 1


def test_reaching_target_latches_submission_barrier_even_if_failure_discards_fragment() -> None:
    collector: VersionedCollector[str] = VersionedCollector(target_transitions=1)
    collector.begin(6)
    collector.record(
        _event(0, 0, _step(7, 1.0)),
        _action(0, 0, 6, 7, 0.0, 1.0, "accepted"),
    )
    assert not collector.accepting_submissions

    collector.record(
        _event(0, 0, error=ConnectionError("failed during drain")),
        _action(0, 0, 6, 7, 1.0, 2.0, "in-flight"),
    )

    assert collector.accepted_transition_count == 0
    assert not collector.accepting_submissions


def test_policy_version_cannot_advance_until_optimizer_update_succeeds() -> None:
    collector: VersionedCollector[str] = VersionedCollector(target_transitions=1)
    collector.begin(0)
    collector.record(_event(0, 0, _step(1, 1.0)), _action(0, 0, 0, 1, 0.0, 1.0, "a"))
    collector.seal_and_bootstrap({AgentIdentity(0, 0, 1): 2.0})
    collector.build_batch()

    with pytest.raises(RuntimeError, match="optimizer update"):
        collector.begin(1)
    with pytest.raises(RuntimeError, match="optimizer update failed"):
        collector.complete_optimizer_update(0, succeeded=False)
    with pytest.raises(RuntimeError, match="optimizer update"):
        collector.begin(1)

    collector.complete_optimizer_update(0, succeeded=True)
    collector.begin(1)
    assert collector.policy_version == 1


def test_intermediate_next_value_comes_from_next_action_snapshot() -> None:
    collector: VersionedCollector[str] = VersionedCollector(target_transitions=2)
    collector.begin(5)
    collector.record(_event(0, 0, _step(9, 1.0)), _action(0, 0, 5, 9, 0.0, 3.0, "first"))
    collector.record(_event(0, 0, _step(9, 2.0)), _action(0, 0, 5, 9, 1.0, 7.0, "second"))
    collector.seal_and_bootstrap({AgentIdentity(0, 0, 9): 11.0})

    first, second = collector.build_batch().transitions
    assert first.next_value == 7.0
    assert second.next_value == 11.0


class _CapturingUpdater:
    def __init__(self, result: str = "updated") -> None:
        self.result = result
        self.batch = None
        self.environment_steps = None

    def update(self, batch: object, *, environment_steps: int) -> str:
        self.batch = batch
        self.environment_steps = environment_steps
        return self.result


def test_ppo_integration_builds_algorithm_batch_then_advances_version() -> None:
    collector: VersionedCollector[PPOAuxiliary] = VersionedCollector(target_transitions=1)
    collector.begin(12)
    collector.record(
        _event(0, 0, _step(1, 1.0)),
        _action(0, 0, 12, 1, 0.0, 1.0, PPOAuxiliary(old_log_prob=-0.25)),
    )
    collector.seal_and_bootstrap({AgentIdentity(0, 0, 1): 2.0})
    updater = _CapturingUpdater()

    result = update_versioned_ppo(
        collector,
        updater,
        gamma=1.0,
        gae_lambda=1.0,
        device=torch.device("cpu"),
        environment_steps=17,
    )

    assert result == "updated"
    assert updater.environment_steps == 17
    assert updater.batch.observations[0].tolist() == [[0.0]]
    assert updater.batch.old_log_probs.tolist() == pytest.approx([-0.25])
    assert updater.batch.advantages.tolist() == pytest.approx([2.0])
    assert updater.batch.returns.tolist() == pytest.approx([3.0])
    collector.begin(13)


def test_fpo_integration_preserves_auxiliary_snapshot_and_failed_update_does_not_advance() -> None:
    auxiliary = FPOAuxiliary(
        latent_action=np.asarray([0.4], dtype=np.float32),
        loss_eps=np.asarray([[0.1]], dtype=np.float32),
        loss_t=np.asarray([[0.2]], dtype=np.float32),
        old_cfm_losses=np.asarray([0.3], dtype=np.float32),
    )
    collector: VersionedCollector[FPOAuxiliary] = VersionedCollector(target_transitions=1)
    collector.begin(3)
    collector.record(
        _event(2, 1, _step(7, 4.0, terminated=True)),
        _action(2, 1, 3, 7, 3.0, 0.5, auxiliary),
    )
    collector.seal_and_bootstrap({})

    class FailingUpdater(_CapturingUpdater):
        def update(self, batch: object, *, environment_steps: int) -> str:
            super().update(batch, environment_steps=environment_steps)
            raise ArithmeticError("non-finite update")

    failing = FailingUpdater()
    with pytest.raises(ArithmeticError, match="non-finite"):
        update_versioned_fpo(
            collector,
            failing,
            gamma=0.9,
            gae_lambda=0.8,
            device=torch.device("cpu"),
            environment_steps=9,
        )
    np.testing.assert_allclose(
        failing.batch.latent_actions.numpy(),
        np.asarray([[0.4]], dtype=np.float32),
    )
    np.testing.assert_allclose(
        failing.batch.old_cfm_losses.numpy(),
        np.asarray([[0.3]], dtype=np.float32),
    )
    with pytest.raises(RuntimeError, match="optimizer update"):
        collector.begin(4)
