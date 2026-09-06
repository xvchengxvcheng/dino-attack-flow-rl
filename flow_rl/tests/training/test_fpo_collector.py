from __future__ import annotations

import numpy as np
import pytest
import torch

from flow_rl.envs.types import EnvStep
from flow_rl.models.fpo_policy import FPOPolicyOutput
from flow_rl.training.fpo_collector import FPOCollector


class _Normalizer:
    def update(self, observations: tuple[np.ndarray, ...]) -> None:
        pass

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
        nfe: int,
        num_fpo_samples: int,
        generator: torch.Generator,
    ) -> FPOPolicyOutput:
        del generator
        feature = observations[0][:, 0]
        latent = torch.stack((feature, -feature), dim=1)
        eps = feature[:, None, None].expand(-1, num_fpo_samples, 2).clone()
        time = torch.full((len(feature), num_fpo_samples, 1), 0.25)
        losses = feature[:, None].expand(-1, num_fpo_samples).clone()
        return FPOPolicyOutput(
            actions=torch.tanh(latent),
            latent_actions=latent,
            loss_eps=eps,
            loss_t=time,
            old_cfm_losses=losses,
            values=feature * 10.0,
            nfe=nfe,
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


def _collector() -> FPOCollector:
    return FPOCollector(
        policy=_Policy(),
        normalizer=_Normalizer(),
        device=torch.device("cpu"),
        gamma=0.99,
        gae_lambda=0.95,
        nfe=4,
        num_fpo_samples=3,
        generator=torch.Generator().manual_seed(5),
    )


def test_fpo_collector_keeps_all_auxiliary_tensors_with_reordered_ids() -> None:
    # Row-order storage would swap Agent 10 and 20's latent/CFM training records.
    collector = _collector()
    collector.reset(_step([20, 10], [2.0, 1.0]))

    collector.step(_step([10, 20], [3.0, 4.0], rewards=[1.0, 2.0]))

    trajectories = collector.trajectories()
    twenty = trajectories[20][0]
    assert twenty.reward == pytest.approx(2.0)
    np.testing.assert_allclose(twenty.auxiliary.latent_action, [2.0, -2.0])
    np.testing.assert_allclose(twenty.auxiliary.loss_eps, np.full((3, 2), 2.0))
    np.testing.assert_allclose(twenty.auxiliary.old_cfm_losses, [2.0, 2.0, 2.0])
    assert trajectories[10][0].reward == pytest.approx(1.0)


def test_fpo_collector_builds_shapes_and_bootstraps_interruption() -> None:
    collector = _collector()
    collector.reset(_step([1], [2.0]))
    collector.step(_step([1], [5.0], rewards=[3.0], truncated=[True]))

    batch = collector.build_batch()

    assert batch.latent_actions.shape == (1, 2)
    assert batch.bounded_actions.shape == (1, 2)
    assert batch.loss_eps.shape == (1, 3, 2)
    assert batch.loss_t.shape == (1, 3, 1)
    assert batch.old_cfm_losses.shape == (1, 3)
    assert batch.returns.item() == pytest.approx(3.0 + 0.99 * 50.0)
