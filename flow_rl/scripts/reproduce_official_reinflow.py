from __future__ import annotations

import argparse
import json
import math
import subprocess
import sys
from pathlib import Path

import torch


PINNED_COMMIT = "e722e151bed767f3ffef47527cf697f2358af55d"


def main() -> int:
    parser = argparse.ArgumentParser(description="Run a bounded official ReinFlow model smoke")
    parser.add_argument("--repository", type=Path, required=True)
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--device", choices=("cpu", "cuda"), default="cpu")
    args = parser.parse_args()

    repository = args.repository.resolve()
    revision = subprocess.check_output(
        ["git", "-C", str(repository), "rev-parse", "HEAD"], text=True
    ).strip()
    if revision != PINNED_COMMIT:
        raise RuntimeError(f"unexpected ReinFlow revision: {revision}")
    sys.path.insert(0, str(repository))

    from model.common.critic import CriticObs
    from model.flow.ft_ppo.ppoflow import PPOFlow
    from model.flow.mlp_flow import FlowMLP

    device = torch.device(args.device)
    if device.type == "cuda" and not torch.cuda.is_available():
        raise RuntimeError("CUDA was requested but is unavailable")
    torch.manual_seed(20260827)
    if device.type == "cuda":
        torch.cuda.manual_seed_all(20260827)

    policy = FlowMLP(
        horizon_steps=1,
        action_dim=2,
        cond_dim=8,
        time_dim=16,
        mlp_dims=[32, 32],
        activation_type="ReLU",
        out_activation_type="Identity",
        use_layernorm=False,
        residual_style=False,
    )
    critic = CriticObs(
        cond_dim=8,
        mlp_dims=[32, 32],
        activation_type="Mish",
        residual_style=False,
    )
    model = PPOFlow(
        device=device,
        policy=policy,
        critic=critic,
        actor_policy_path=None,
        act_dim=2,
        horizon_steps=1,
        act_min=-1.0,
        act_max=1.0,
        obs_dim=8,
        cond_steps=1,
        noise_scheduler_type="learn_decay",
        inference_steps=4,
        ft_denoising_steps=4,
        randn_clip_value=3.0,
        min_sampling_denoising_std=0.1,
        min_logprob_denoising_std=0.1,
        logprob_min=-1.0,
        logprob_max=1.0,
        clip_ploss_coef=0.01,
        clip_ploss_coef_base=0.01,
        clip_ploss_coef_rate=3.0,
        clip_vloss_coef=None,
        denoised_clip_value=1.0,
        max_logprob_denoising_std=0.24,
        time_dim_explore=0,
        learn_explore_time_embedding=False,
        use_time_independent_noise=False,
        noise_hidden_dims=[16],
        logprob_debug_sample=False,
        logprob_debug_recalculate=False,
        explore_net_activation_type="Tanh",
    ).to(device)

    batch_size = 8
    observations = {"state": torch.randn(batch_size, 1, 8, device=device)}
    with torch.no_grad():
        actions, chains, old_log_probs = model.get_actions(
            observations,
            eval_mode=False,
            save_chains=True,
            normalize_denoising_horizon=True,
            normalize_act_space_dimension=True,
            clip_intermediate_actions=True,
            account_for_initial_stochasticity=True,
        )
        old_values = model.critic(observations).view(-1)
    if chains.shape != (batch_size, 5, 1, 2):
        raise AssertionError(f"unexpected chain shape: {tuple(chains.shape)}")
    if not torch.all((actions >= -1.0) & (actions <= 1.0)):
        raise AssertionError("official sampled actions are outside [-1, 1]")

    advantages = torch.tensor(
        [1.0, -1.0, 0.5, -0.5, 0.25, -0.25, 0.75, -0.75], device=device
    )
    returns = old_values.detach() + advantages
    losses = model.loss(
        observations,
        chains,
        returns,
        old_values,
        advantages,
        old_log_probs,
        normalize_denoising_horizon=True,
        normalize_act_space_dimension=True,
        verbose=False,
        clip_intermediate_actions=True,
        account_for_initial_stochasticity=True,
    )
    policy_loss, entropy_loss, value_loss = losses[:3]
    clip_fraction, approximate_kl, ratio = losses[4:7]
    noise_std = float(losses[13])
    total_loss = policy_loss + 0.03 * entropy_loss + 0.5 * value_loss
    total_loss.backward()

    actor_gradient = torch.nn.utils.clip_grad_norm_(
        model.actor_ft.policy.parameters(), float("inf")
    )
    noise_gradient = torch.nn.utils.clip_grad_norm_(
        model.actor_ft.explore_noise_net.parameters(), float("inf")
    )
    critic_gradient = torch.nn.utils.clip_grad_norm_(
        model.critic.parameters(), float("inf")
    )
    values = {
        "policy_loss": float(policy_loss.detach()),
        "entropy_loss": float(entropy_loss.detach()),
        "value_loss": float(value_loss.detach()),
        "clip_fraction": float(clip_fraction),
        "approximate_kl": float(approximate_kl),
        "ratio": float(ratio),
        "noise_std": noise_std,
        "actor_gradient_norm": float(actor_gradient),
        "noise_gradient_norm": float(noise_gradient),
        "critic_gradient_norm": float(critic_gradient),
        "action_min": float(actions.min()),
        "action_max": float(actions.max()),
        "old_log_prob_min": float(old_log_probs.min()),
        "old_log_prob_max": float(old_log_probs.max()),
    }
    if not all(math.isfinite(value) for value in values.values()):
        raise AssertionError("official ReinFlow smoke produced non-finite metrics")
    if abs(values["ratio"] - 1.0) > 1e-6:
        raise AssertionError(f"initial ratio is not one: {values['ratio']}")
    if not 0.1 <= noise_std <= 0.24:
        raise AssertionError(f"noise std is outside configured bounds: {noise_std}")

    payload = {
        "status": "passed",
        "official_repository": str(repository),
        "official_revision": revision,
        "python": sys.version.split()[0],
        "torch": torch.__version__,
        "device": str(device),
        "seed": 20260827,
        "configuration": {
            "batch_size": batch_size,
            "observation_size": 8,
            "action_size": 2,
            "horizon_steps": 1,
            "nfe": 4,
            "noise_std_bounds": [0.1, 0.24],
            "log_prob_bounds": [-1.0, 1.0],
            "ppo_clip": 0.01,
            "entropy_coefficient": 0.03,
            "normalize_denoising_horizon": True,
            "normalize_action_dimensions": True,
        },
        "shapes": {
            "actions": list(actions.shape),
            "chains": list(chains.shape),
            "old_log_probs": list(old_log_probs.shape),
        },
        "metrics": values,
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(payload, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(payload, indent=2))
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
