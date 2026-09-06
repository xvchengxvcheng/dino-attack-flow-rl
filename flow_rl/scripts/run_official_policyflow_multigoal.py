from __future__ import annotations

import argparse
import copy
import json
import math
import sys
import time
import types
from pathlib import Path

import numpy as np
import torch
from omegaconf import OmegaConf


ROOT = Path(__file__).resolve().parents[2]
OFFICIAL = ROOT / "PolicyFlow"
MULTIGOAL = OFFICIAL / "scripts" / "multigoal"
sys.path.insert(0, str(MULTIGOAL))

# The fixed pre-release clone imports Isaac Lab's config decorator even for the
# standalone MultiGoal path. The decorator is only used as a class wrapper here;
# MultiGoal populates the instance attributes explicitly in cli_args.py.
isaaclab_module = types.ModuleType("isaaclab")
isaaclab_utils_module = types.ModuleType("isaaclab.utils")
isaaclab_utils_module.configclass = lambda cls: cls
isaaclab_module.utils = isaaclab_utils_module
sys.modules.setdefault("isaaclab", isaaclab_module)
sys.modules.setdefault("isaaclab.utils", isaaclab_utils_module)
sys.modules.setdefault("gymnasium_robotics", types.ModuleType("gymnasium_robotics"))

import cli_args  # noqa: E402
from multigoal import MultiGoalEnv  # noqa: E402
from policyflow_torch.agents import PolicyFlow  # noqa: E402
from policyflow_torch.modules import (  # noqa: E402
    ConditionLinearLayer,
    ContinuousNormalizingFlow,
    FlowMlp,
    Network,
)
from policyflow_torch.runners import MultiGoalRunner  # noqa: E402
from policyflow_torch.storage import ReplayBuffer  # noqa: E402
from policyflow_torch.utils.utils import seed as seed_everything  # noqa: E402


SOURCE_COMMIT = "7f304b96e93b2804bdcc943710db885adddd06b8"


def parse_args() -> argparse.Namespace:
    parser = argparse.ArgumentParser()
    parser.add_argument("--output", type=Path, required=True)
    parser.add_argument("--seed", type=int, default=0)
    parser.add_argument("--iterations", type=int, default=20)
    parser.add_argument("--rollouts", type=int, default=16)
    parser.add_argument("--num-envs", type=int, default=1024)
    parser.add_argument("--sample-steps", type=int, default=10)
    parser.add_argument("--mode-samples", type=int, default=8192)
    return parser.parse_args()


def _mode_statistics(
    actions: torch.Tensor,
    goal_directions: torch.Tensor,
    *,
    min_action_norm: float = 0.0,
    min_cosine: float = -1.0,
) -> dict:
    unit = actions / actions.norm(dim=1, keepdim=True).clamp_min(1e-8)
    cosines, assignments = torch.max(unit @ goal_directions.T, dim=1)
    eligible = (actions.norm(dim=1) >= min_action_norm) & (cosines >= min_cosine)
    eligible_assignments = assignments[eligible]
    counts = torch.bincount(
        eligible_assignments, minlength=goal_directions.shape[0]
    )
    probabilities = counts.float() / counts.sum().clamp_min(1.0)
    positive = probabilities[probabilities > 0]
    entropy = (
        -(positive * positive.log()).sum().item() if len(positive) else 0.0
    )
    return {
        "counts": counts.cpu().tolist(),
        "coverage": int((counts > 0).sum().item()),
        "eligible_count": int(eligible.sum().item()),
        "eligible_fraction": float(eligible.float().mean().item()),
        "min_action_norm": min_action_norm,
        "min_cosine": min_cosine,
        "entropy_nats": entropy,
        "normalized_entropy": entropy / math.log(goal_directions.shape[0]) if entropy else 0.0,
        "mean_action_norm": float(actions.norm(dim=1).mean().item()),
        "finite": bool(torch.isfinite(actions).all()),
    }


def main() -> None:
    args = parse_args()
    output = args.output.resolve()
    if output.exists() and any(output.iterdir()):
        raise FileExistsError(f"output directory is not empty: {output}")
    output.mkdir(parents=True, exist_ok=True)
    if min(args.iterations, args.rollouts, args.num_envs, args.sample_steps, args.mode_samples) <= 0:
        raise ValueError("all count arguments must be positive")

    seed_everything(args.seed)
    config = OmegaConf.load(MULTIGOAL / "multigoal_env_cfg.yaml")
    config.num_envs = args.num_envs
    config.device = "cuda" if torch.cuda.is_available() else "cpu"
    env = MultiGoalEnv(config)
    observations, _ = env.reset()
    observation_size = observations["actor_observations"].shape[1]
    action_size = 2
    buffer = ReplayBuffer(
        memory_size=args.rollouts,
        num_envs=env.num_envs,
        device=env.device,
    )
    model_config = cli_args.get_policyflow_models_cfg()
    model_config["critic"].update({"input_size": observation_size, "output_size": 1})
    model_config["actor"].update({"x_dim": action_size, "emb_dim": 64})
    flow = FlowMlp(**model_config["actor"]).to(env.device)
    condition = ConditionLinearLayer(cond_dim=observation_size, emb_dim=64).to(env.device)
    actor = ContinuousNormalizingFlow(
        x_dims=action_size,
        nn_flow=flow,
        nn_condition=condition,
        sample_steps=args.sample_steps,
        interpolation_type="rectified_flow",
        device=env.device,
    )
    models = {
        "critic": Network(**model_config["critic"]),
        "actor": actor,
    }
    initial_actor = copy.deepcopy(actor)
    agent_config = cli_args.get_policyflow_agent_cfg()
    agent = PolicyFlow(
        models=models,
        replay_buffer=buffer,
        device=env.device,
        cfg=agent_config.__dict__,
    )
    agent.init_replay_buffer(
        critic_observation_size=observation_size,
        actor_observation_size=observation_size,
        action_size=action_size,
    )
    runner = MultiGoalRunner(
        env=env,
        agent=agent,
        cfg={
            "max_iterations": args.iterations,
            "rollouts": args.rollouts,
            "save_interval": max(args.iterations, 1),
            "log_dir": str(output / "runs"),
            "experiment_name": "official",
        },
    )
    started = time.perf_counter()
    runner.train(return_epochs=100)
    elapsed = time.perf_counter() - started
    runner.save(str(output / "final.pt"))

    goal_directions = env.goal_positions / env.goal_positions.norm(
        dim=1, keepdim=True
    ).clamp_min(1e-8)
    def sample_actions(flow_actor: ContinuousNormalizingFlow) -> tuple[torch.Tensor, torch.Tensor]:
        prior_batches: list[torch.Tensor] = []
        sampled_batches: list[torch.Tensor] = []
        remaining = args.mode_samples
        with torch.inference_mode():
            while remaining:
                count = min(args.num_envs, remaining)
                origin = torch.zeros((count, observation_size), device=env.device)
                x0 = torch.randn((count, action_size), device=env.device)
                prior, std = flow_actor.sample(x0=x0, condition=origin, n_samples=count)
                epsilon = torch.randn_like(prior)
                prior_batches.append(prior)
                sampled_batches.append(prior + epsilon * std)
                remaining -= count
        return torch.cat(prior_batches), torch.cat(sampled_batches)

    # Use the same random stream for initialization and trained samples so the
    # comparison isolates the learned velocity field, not a different sample.
    torch.manual_seed(args.seed + 12345)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(args.seed + 12345)
    initial_priors, initial_sampled = sample_actions(initial_actor)
    torch.manual_seed(args.seed + 12345)
    if torch.cuda.is_available():
        torch.cuda.manual_seed_all(args.seed + 12345)
    priors, sampled = sample_actions(actor)
    parameters = list(agent.model_dict["actor"].model.parameters()) + list(
        agent.model_dict["critic"].parameters()
    )
    finite_parameters = all(torch.isfinite(parameter).all() for parameter in parameters)
    mode_report = {
        "source_commit": SOURCE_COMMIT,
        "seed": args.seed,
        "num_goals": int(env.num_goals),
        "mode_samples": args.mode_samples,
        "strict_thresholds": {"min_action_norm": 3.0, "min_cosine": math.cos(math.pi / 6.0)},
        "flow_prior": _mode_statistics(priors, goal_directions),
        "official_stochastic_action": _mode_statistics(sampled, goal_directions),
        "initial_flow_prior_strict": _mode_statistics(
            initial_priors, goal_directions, min_action_norm=3.0,
            min_cosine=math.cos(math.pi / 6.0),
        ),
        "trained_flow_prior_strict": _mode_statistics(
            priors, goal_directions, min_action_norm=3.0,
            min_cosine=math.cos(math.pi / 6.0),
        ),
        "initial_official_stochastic_action_strict": _mode_statistics(
            initial_sampled, goal_directions, min_action_norm=3.0,
            min_cosine=math.cos(math.pi / 6.0),
        ),
        "trained_official_stochastic_action_strict": _mode_statistics(
            sampled, goal_directions, min_action_norm=3.0,
            min_cosine=math.cos(math.pi / 6.0),
        ),
    }
    run_report = {
        "source_commit": SOURCE_COMMIT,
        "seed": args.seed,
        "iterations": args.iterations,
        "rollouts": args.rollouts,
        "num_envs": args.num_envs,
        "environment_transitions": args.iterations * args.rollouts * args.num_envs,
        "sample_steps": args.sample_steps,
        "velocity_nfe": 2 * args.sample_steps,
        "wall_clock_seconds": elapsed,
        "transitions_per_second": args.iterations * args.rollouts * args.num_envs / elapsed,
        "finite_parameters": bool(finite_parameters),
        "device": str(env.device),
    }
    for name, content in (("mode-coverage.json", mode_report), ("run.json", run_report)):
        with (output / name).open("w", encoding="utf-8", newline="\n") as handle:
            json.dump(content, handle, indent=2, sort_keys=True)
            handle.write("\n")
    env.close()


if __name__ == "__main__":
    main()
