"""Run a bounded smoke check against the read-only official GridWorld FPO code."""

from __future__ import annotations

import argparse
import json
import os
import subprocess
import sys
from importlib import metadata
from pathlib import Path

os.environ.setdefault("MPLBACKEND", "Agg")


def _parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--seed", type=int, default=20260826)
    parser.add_argument("--num-fpo-samples", type=int, default=50)
    parser.add_argument("--nfe", type=int, default=10)
    return parser.parse_args()


def _git_revision(repository: Path) -> str:
    return subprocess.check_output(
        ["git", "rev-parse", "HEAD"], cwd=repository, text=True
    ).strip()


def main() -> None:
    args = _parse_args()
    workspace = Path(__file__).resolve().parents[2]
    official_repository = workspace / "fpo"
    official_gridworld = official_repository / "gridworld"
    sys.path.insert(0, str(official_gridworld))

    import numpy as np
    import torch

    from models.diffusion_policy import DiffusionPolicy
    from models.network import FeedForwardNN
    from utils.gridworld import GridWorldEnv

    torch.manual_seed(args.seed)
    np.random.seed(args.seed)
    device = torch.device("cuda" if torch.cuda.is_available() else "cpu")

    env = GridWorldEnv(mode="two_walls", max_steps=8)
    observation, _ = env.reset(seed=args.seed)
    observation_tensor = torch.as_tensor(
        observation, dtype=torch.float32, device=device
    )

    actor = DiffusionPolicy(
        in_dim=5,
        out_dim=2,
        device=device,
        num_steps=args.nfe,
        fixed_noise_inference=False,
    )
    critic = FeedForwardNN(2, 1).to(device)
    actor_optimizer = torch.optim.Adam(actor.parameters(), lr=3e-4)
    critic_optimizer = torch.optim.Adam(critic.parameters(), lr=3e-4)

    action, path, eps, time, old_cfm_loss = actor.sample_action_with_info(
        observation_tensor, num_train_samples=args.num_fpo_samples
    )
    tiled_observation = observation_tensor.expand(args.num_fpo_samples, -1)
    tiled_action = action.detach().expand(args.num_fpo_samples, -1)
    new_cfm_loss = actor.compute_cfm_loss(tiled_observation, tiled_action, eps, time)
    cfm_difference = torch.clamp(old_cfm_loss - new_cfm_loss, -3.0, 3.0)
    ratio = torch.exp(torch.clamp(cfm_difference.mean(), -3.0, 3.0))
    advantage = torch.tensor(0.75, device=device)
    surrogate_1 = ratio * advantage
    surrogate_2 = torch.clamp(ratio, 0.8, 1.2) * advantage
    actor_loss = -torch.minimum(surrogate_1, surrogate_2)

    actor_before = [parameter.detach().clone() for parameter in actor.parameters()]
    actor_optimizer.zero_grad()
    actor_loss.backward()
    actor_grad_norm = torch.nn.utils.clip_grad_norm_(actor.parameters(), 0.5)
    actor_optimizer.step()
    actor_parameter_delta = sum(
        (before - after.detach()).abs().sum().item()
        for before, after in zip(actor_before, actor.parameters(), strict=True)
    )

    critic_prediction = critic(observation_tensor).squeeze()
    critic_target = torch.tensor(0.25, device=device)
    critic_loss = torch.nn.functional.mse_loss(critic_prediction, critic_target)
    critic_optimizer.zero_grad()
    critic_loss.backward()
    critic_grad_norm = torch.nn.utils.clip_grad_norm_(critic.parameters(), 0.5)
    critic_optimizer.step()

    next_observation, reward, terminated, truncated, _ = env.step(
        action.detach().cpu().numpy()
    )
    env.close()

    finite_values = torch.tensor(
        [
            actor_loss.detach(),
            critic_loss.detach(),
            ratio.detach(),
            actor_grad_norm.detach(),
            critic_grad_norm.detach(),
        ],
        device=device,
    )
    if not bool(torch.isfinite(finite_values).all()):
        raise RuntimeError(f"non-finite official FPO smoke values: {finite_values}")
    if actor_parameter_delta <= 0.0:
        raise RuntimeError("official actor parameters did not change after one update")
    if path.shape != (1, args.nfe + 1, 2):
        raise RuntimeError(f"unexpected official path shape: {tuple(path.shape)}")
    if next_observation.shape != (2,):
        raise RuntimeError(
            f"unexpected GridWorld observation shape: {next_observation.shape}"
        )

    result = {
        "status": "passed",
        "official_repository": str(official_repository),
        "official_revision": _git_revision(official_repository),
        "seed": args.seed,
        "device": str(device),
        "dependencies": {
            package: metadata.version(package)
            for package in ("torch", "numpy", "gymnasium", "wandb", "torchdiffeq")
        },
        "algorithm": {
            "nfe": args.nfe,
            "num_fpo_samples": args.num_fpo_samples,
            "positive_advantage": False,
            "ratio_clamp": [-3.0, 3.0],
            "ppo_clip": 0.2,
        },
        "metrics": {
            "actor_loss": float(actor_loss.detach().cpu()),
            "critic_loss": float(critic_loss.detach().cpu()),
            "ratio": float(ratio.detach().cpu()),
            "actor_grad_norm": float(actor_grad_norm.detach().cpu()),
            "critic_grad_norm": float(critic_grad_norm.detach().cpu()),
            "actor_parameter_delta_l1": actor_parameter_delta,
            "action_min": float(action.detach().min().cpu()),
            "action_max": float(action.detach().max().cpu()),
            "reward": float(reward),
            "terminated": bool(terminated),
            "truncated": bool(truncated),
        },
        "shapes": {
            "action": list(action.shape),
            "path": list(path.shape),
            "eps": list(eps.shape),
            "time": list(time.shape),
            "cfm_loss": list(old_cfm_loss.shape),
        },
    }
    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(json.dumps(result, indent=2) + "\n", encoding="utf-8")
    print(json.dumps(result, indent=2))


if __name__ == "__main__":
    main()
