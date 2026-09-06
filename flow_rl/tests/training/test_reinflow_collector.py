from __future__ import annotations

import numpy as np
import pytest
import torch

from flow_rl.envs.types import EnvStep
from flow_rl.models.reinflow_policy import ReinFlowPolicyOutput


class _FrozenNormalizer:
    def __init__(self) -> None:
        self.update_calls = 0

    def update(self, observations: tuple[np.ndarray, ...]) -> None:
        del observations
        self.update_calls += 1

    def normalize(self, observations: tuple[np.ndarray, ...]) -> tuple[np.ndarray, ...]:
        return tuple(item.copy() for item in observations)


class _Critic:
    def __call__(self, observations: tuple[torch.Tensor, ...]) -> torch.Tensor:
        return observations[0][:, 0] * 10.0


class _Policy:
    critic = _Critic()

    def act(
        self,
        observations: tuple[torch.Tensor, ...],
        *,
        evaluation: bool,
        generator: torch.Generator,
    ) -> ReinFlowPolicyOutput:
        del evaluation, generator
        feature = observations[0][:, 0]
        chains = torch.stack(
            (
                torch.stack((feature, -feature), dim=1),
                torch.stack((feature + 0.1, -feature - 0.1), dim=1),
                torch.stack((feature + 0.2, -feature - 0.2), dim=1),
            ),
            dim=1,
        )
        return ReinFlowPolicyOutput(
            actions=torch.clamp(chains[:, -1], -1.0, 1.0),
            chains=chains,
            old_log_probs=feature * 0.01,
            values=feature * 10.0,
            entropy_rate=feature * 0.0 + 0.5,
            mean_noise_std=feature * 0.0 + 0.2,
            nfe=2,
        )


def _step(
    ids: list[int],
    observations: list[float],
    *,
    rewards: list[float] | None = None,
    terminated: list[bool] | None = None,
    truncated: list[bool] | None = None,
) -> EnvStep:
    count = len(ids)
    return EnvStep(
        agent_ids=np.asarray(ids, dtype=np.int64),
        observations=(np.asarray(observations, dtype=np.float32).reshape(count, 1),),
        rewards=np.asarray(rewards or [0.0] * count, dtype=np.float32),
        terminated=np.asarray(terminated or [False] * count, dtype=bool),
        truncated=np.asarray(truncated or [False] * count, dtype=bool),
    )


def _collector():
    from flow_rl.training.reinflow_collector import ReinFlowCollector

    normalizer = _FrozenNormalizer()
    collector = ReinFlowCollector(
        policy=_Policy(),
        normalizer=normalizer,
        device=torch.device("cpu"),
        gamma=0.99,
        gae_lambda=0.95,
        nfe=2,
        generator=torch.Generator().manual_seed(5),
    )
    return collector, normalizer


def test_collector_keeps_full_chain_with_reordered_agent_ids_and_frozen_normalizer() -> None:
    collector, normalizer = _collector()
    collector.reset(_step([20, 10], [0.2, 0.1]))

    collector.step(_step([10, 20], [0.3, 0.4], rewards=[1.0, 2.0]))

    trajectories = collector.trajectories()
    np.testing.assert_allclose(
        trajectories[20][0].auxiliary.chain,
        [[0.2, -0.2], [0.3, -0.3], [0.4, -0.4]],
    )
    assert trajectories[20][0].auxiliary.old_log_prob == pytest.approx(0.002)
    assert trajectories[10][0].reward == pytest.approx(1.0)
    assert normalizer.update_calls == 0


def test_collector_builds_batch_and_bootstraps_truncation_after_terminal_only_tick() -> None:
    collector, _ = _collector()
    collector.reset(_step([1], [0.2]))
    terminal = collector.step(
        _step([1], [0.5], rewards=[3.0], truncated=[True])
    )

    assert terminal.actions.shape == (0, 2)
    batch = collector.build_batch()

    assert batch.chains.shape == (1, 3, 2)
    assert batch.actions.shape == (1, 2)
    assert batch.old_log_probs.shape == (1,)
    assert batch.returns.item() == pytest.approx(3.0 + 0.99 * 5.0)
