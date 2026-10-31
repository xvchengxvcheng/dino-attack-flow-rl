# Phase 6 ReinFlow Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Train a reproducible Flow BC policy from the fixed phase-3 dataset and fine-tune it on Unity 3DBall with the pinned official ReinFlow Markov-chain likelihood and learnable exploration noise.

**Architecture:** Keep BC, stochastic Flow-chain policy, ReinFlow PPO update, Unity collection, and evaluation as separate modules. Reuse the existing observation normalizer, velocity network, critic, Agent-ID-safe on-policy core, GAE, logger, and checkpoint primitives, while preserving ReinFlow-specific chain log-probability and entropy semantics.

**Tech Stack:** Python 3.10.12, NumPy 1.23.5, PyTorch 2.2.1+cu121, ML-Agents Env API 1.1.0, pytest 8.3.5, Unity 3DBall, local ReinFlow `release` commit `e722e151bed767f3ffef47527cf697f2358af55d`.

**Spec:** `docs/superpowers/specs/2026-08-27-phase-6-reinflow-design.md`

## Global Constraints

- Treat `ReinFlow/`, `ml-agents/`, `fpo/`, `PolicyFlow/`, Unity assets/settings/packages, builds, and previous reports as read-only.
- Use only `datasets/phase3/ppo-seed-0-dataset-seed-10000-100k/` for formal BC.
- Keep action horizon 1; NFE may be 1/2/4/8 and formal NFE is 4.
- Follow official bounded-action chain semantics; do not reuse FPO CFM-difference ratio.
- Freeze the dataset normalizer during BC evaluation and online fine-tuning.
- Use strict TDD and single-use run directories; retain failed artifacts.
- Use `.conda/flow-rl` for Unity implementation and an isolated environment for official smoke dependencies.

---

### Task 0: Record authoritative source and official bounded smoke

**Files:**
- Create: `flow_rl/scripts/reproduce_official_reinflow.py`
- Create: `reports/phase6/official-reinflow/README.md`
- Create: `reports/phase6/official-reinflow/result.json`
- Modify: `docs/PROJECT_PLAN.md`

**Interfaces:**
- Consumes: local `FlowMLP`, `NoisyFlowMLP`, `PPOFlow` at the pinned commit
- Produces: finite synthetic chain/noise/log-prob/ratio evidence with dependency versions

- [x] **Step 1: Verify source before imports**

  Run CodeGraph for `TrainReFlowAgent`, `PPOFlow.get_actions/get_logprobs/loss`, `NoisyFlowMLP`, and Hydra `script/run.py`; assert `git rev-parse HEAD` equals the pinned commit.

- [x] **Step 2: Write the bounded harness assertions**

  The harness must construct the official state-only model at action size 2, horizon 1, NFE 4; generate a batch of chains with fixed seeds; assert `(B,5,1,2)`, bounded final actions, noise within configured bounds, finite old/new log-prob and entropy, and ratio exactly 1 before an optimizer step.

- [x] **Step 3: Run in an isolated environment**

  Install only dependencies required to import these official model classes. Do not install Gym/MuJoCo/D4RL unless the model import proves it is required. Save `pip freeze`, command, stdout metrics, commit, and any Windows limitation.

- [x] **Step 4: Update source evidence**

  Record official Hydra pretrain/fine-tune commands, config paths, defaults used in the Unity design, and deliberate horizon/network/environment differences.

### Task 1: Flow BC configuration, dataset validation, and split

**Files:**
- Create: `flow_rl/src/flow_rl/training/flow_bc_config.py`
- Create: `flow_rl/tests/training/test_flow_bc_config.py`
- Create: `flow_rl/configs/phase6_flow_bc_3dball.yaml`

**Interfaces:**
- Produces: `FlowBCTrainingConfig.from_yaml(path)`, `.validate()`, `.to_dict()`
- Produces: `split_demonstrations(batch, validation_fraction, seed) -> (train_indices, validation_indices)`

- [x] **Step 1: Write failing config and split tests**

  Use a six-row literal dataset and assert seed-identical disjoint splits with full coverage. Reject unknown keys, wrong dataset/build/hash metadata, non-bounded actions, invalid validation fraction, empty run directory reuse, invalid NFE, and CUDA without availability.

- [x] **Step 2: Run RED**

  Run `pytest flow_rl/tests/training/test_flow_bc_config.py -q`; expect import failure for `flow_bc_config`.

- [x] **Step 3: Implement strict config and split**

  Formal YAML: dataset fixed above, run `reports/phase6/flow-bc-seed-0`, seed 600, split seed 601, batch 1024, epochs 200, LR `1e-3`, NFE 4, 2×128 SiLU velocity network, CUDA.

- [x] **Step 4: Run GREEN and affected data tests**

  Run the focused file plus `tests/data/test_demonstrations.py`.

### Task 2: Flow BC trainer and checkpoint

**Files:**
- Create: `flow_rl/src/flow_rl/training/flow_bc_trainer.py`
- Create: `flow_rl/src/flow_rl/cli/train_flow_bc.py`
- Modify: `flow_rl/src/flow_rl/tracking/checkpoint.py`
- Modify: `flow_rl/pyproject.toml`
- Create: `flow_rl/tests/training/test_flow_bc_trainer.py`
- Modify: `flow_rl/tests/tracking/test_flow_checkpoint.py`

**Interfaces:**
- Produces: `FlowBCTrainer.train() -> FlowBCSummary`
- Produces: `validate_flow_bc_checkpoint(payload) -> None`
- Produces console script: `flow-rl-train-flow-bc`

- [x] **Step 1: Write a failing hand-derived BC loss test**

  With `action=[[2.0]]`, `noise=[[0.0]]`, `time=[[0.25]]`, and a velocity prediction of 1, assert trajectory 0.5, target velocity 2, and MSE 1. Reject wrong dtype/shape and non-finite values.

- [x] **Step 2: Write failing trainer/checkpoint tests**

  Train a real tiny velocity model on an in-memory demonstration set. Assert parameters change, fixed seeds reproduce split/loss, validation uses fixed samples, best/final checkpoint validates, metadata contains every source hash and normalizer, and resume restores optimizer/RNG into a fresh directory.

- [x] **Step 3: Implement minimal trainer**

  Normalize raw observations from metadata, sample CondOT noise/time from explicit generators, optimize velocity MSE, log train/validation CSV/TensorBoard, atomically save best/final checkpoints, and never mutate the normalizer.

- [x] **Step 4: Run focused and full regression**

  Run BC, Flow, checkpoint, dataset, and full tests; run `pip check` and `compileall`.

### Task 3: BC checkpoint evaluator

**Files:**
- Create: `flow_rl/src/flow_rl/evaluation/flow_bc.py`
- Create: `flow_rl/src/flow_rl/cli/evaluate_flow_bc.py`
- Create: `flow_rl/tests/evaluation/test_flow_bc_evaluation.py`
- Modify: `flow_rl/pyproject.toml`

**Interfaces:**
- Produces: `evaluate_flow_bc_checkpoint(...) -> FlowBCEvaluationSummary`
- Produces console script: `flow-rl-evaluate-flow-bc`

- [x] **Step 1: Write failing fake-adapter tests**

  Assert exact episode count, deterministic repetition for checkpoint/seed/NFE, different seed changes initial noise, means are clipped each Euler step, actions remain bounded, normalizer count is frozen, terminal-only ticks work, single-use output and cleanup hold.

- [x] **Step 2: Implement evaluator and CLI**

  Reconstruct only from validated checkpoint, sample Gaussian initial state with a dedicated generator, run deterministic Euler transitions, and write config/episodes/TensorBoard/Unity log/evaluation JSON.

- [x] **Step 3: Run focused and full tests**

### Task 4: Learnable noise and ReinFlow chain policy

**Files:**
- Create: `flow_rl/src/flow_rl/models/reinflow_policy.py`
- Modify: `flow_rl/src/flow_rl/models/__init__.py`
- Create: `flow_rl/tests/models/test_reinflow_policy.py`

**Interfaces:**
- Produces: `LearnableFlowNoise.forward(observations, time, step) -> Tensor`
- Produces: `ReinFlowActorCritic.act(..., evaluation, generator) -> ReinFlowPolicyOutput`
- Produces: `ReinFlowPolicyOutput(actions, chains, old_log_probs, values, entropy_rate, mean_noise_std, nfe)`

- [x] **Step 1: Write failing noise-head tests**

  Assert exact output `(B,A)`, `min_std <= std <= max_std`, finite gradients reach the state encoder and noise MLP, time embedding is detached for the noise branch, and invalid bounds/step/dtype fail.

- [x] **Step 2: Run RED and implement the minimum noise head**

  Map `tanh(raw_logvar)` to the configured log-variance interval and exponentiate half-logvar.

- [x] **Step 3: Write failing chain tests**

  Use constant velocity/noise and injected initial/transition noise. Hand-check all K+1 chain values, `mean±3*std` clipping, final action bounds, exact NFE calls, fixed-generator reproduction, and evaluation mode's absence of transition noise.

- [x] **Step 4: Implement the chain sampler and actor-critic**

  Load the BC actor strictly, preserve a full differentiable recomputation path for update, and detach rollout auxiliaries.

- [x] **Step 5: Run model and full regression**

### Task 5: Chain log-probability, entropy, ratio, and updater

**Files:**
- Create: `flow_rl/src/flow_rl/algorithms/reinflow.py`
- Modify: `flow_rl/src/flow_rl/algorithms/__init__.py`
- Create: `flow_rl/tests/algorithms/test_reinflow.py`

**Interfaces:**
- Produces: `reinflow_chain_statistics(...) -> ReinFlowChainStatistics`
- Produces: `ReinFlowBatch(...)`
- Produces: `ReinFlowUpdater.update(batch, environment_steps) -> ReinFlowMetrics`

- [x] **Step 1: Write hand-derived likelihood/entropy tests**

  For one dimension, one transition, mean 0 and std 1, compare initial and transition log-probs and Normal entropy to literal analytical values. Assert normalization by two probability steps and one action dimension; cover exclusion of initial stochasticity.

- [x] **Step 2: Write ratio and clipped-surrogate tests**

  Cover log-prob clamp `[-1,1]`, ratio 1, positive/zero/negative advantages, clip range 0.01, and `((ratio-1)-logratio).mean()` approximate KL.

- [x] **Step 3: Write real updater warmup tests**

  Before warmup assert only critic parameters change. At the boundary assert velocity actor and noise head change, both optimizers retain state, all metrics/gradients are finite, partial minibatches are consumed, and fixed seeds reproduce results.

- [x] **Step 4: Implement pure statistics and updater**

  Use separate actor/critic losses and optimizer steps so warmup never accumulates or applies actor gradients accidentally. Reject non-finite metrics before either step.

- [x] **Step 5: Run algorithm/model/full regression**

### Task 6: Agent-ID-safe ReinFlow collector

**Files:**
- Create: `flow_rl/src/flow_rl/training/reinflow_collector.py`
- Create: `flow_rl/tests/training/test_reinflow_collector.py`

**Interfaces:**
- Produces: `ReinFlowCollector.reset`, `.step`, `.build_batch`
- Consumes: `OnPolicyCollectorCore` and `ReinFlowPolicyOutput`

- [x] **Step 1: Write failing ownership tests**

  Repeat shuffled IDs, natural terminal, interrupted terminal, overlap, terminal-only and empty-decision cases. Assert every full chain/old log-prob/value remains attached to its originating Agent ID and GAE matches PPO/FPO semantics.

- [x] **Step 2: Implement thin wrapper**

  Freeze the restored BC normalizer, normalize decision observations, call the chain policy, and delegate all event ownership to `OnPolicyCollectorCore`.

- [x] **Step 3: Run PPO/FPO/ReinFlow collector regression**

### Task 7: ReinFlow config, trainer, logging, checkpoint, and resume

**Files:**
- Create: `flow_rl/src/flow_rl/training/reinflow_config.py`
- Create: `flow_rl/src/flow_rl/training/reinflow_trainer.py`
- Create: `flow_rl/src/flow_rl/cli/train_reinflow.py`
- Create: `flow_rl/configs/phase6_reinflow_3dball_smoke.yaml`
- Create: `flow_rl/configs/phase6_reinflow_3dball.yaml`
- Create: `flow_rl/tests/training/test_reinflow_config.py`
- Create: `flow_rl/tests/training/test_reinflow_trainer.py`
- Modify: `flow_rl/src/flow_rl/tracking/checkpoint.py`
- Modify: `flow_rl/pyproject.toml`

**Interfaces:**
- Produces: `ReinFlowTrainingConfig`, `ReinFlowTrainer.train()`, `validate_reinflow_checkpoint`
- Produces console script: `flow-rl-train-reinflow`

- [x] **Step 1: Write strict config tests**

  Reject incompatible BC checkpoint/build/network, reused directories, worker collision, invalid noise/log-prob ranges, negative warmup, NFE mismatch, nonpositive entropy, and unsupported horizon. Formal config uses NFE 4, 500k, rollout 12k, batch 256, 3 epochs, actor LR `4.5e-5`, critic LR `6.5e-4`, clip 0.01, min/max std 0.10/0.24, entropy 0.03, and 24k critic warmup.

- [x] **Step 2: Write fake-adapter trainer tests**

  Assert exact step count, warmup transition, actor/noise/critic updates, frozen normalizer, finite logger columns, all-ID final drain, complete schema, exact resume including CUDA RNG, and cleanup on exception.

- [x] **Step 3: Implement trainer and checkpoint**

  Reuse adapter/liveness/episode logger/run directory/checkpoint primitives. Store the pinned ReinFlow commit and BC source hash in every checkpoint.

- [x] **Step 4: Run focused and full regression**

### Task 8: ReinFlow evaluator

**Files:**
- Create: `flow_rl/src/flow_rl/evaluation/reinflow.py`
- Create: `flow_rl/src/flow_rl/cli/evaluate_reinflow.py`
- Create: `flow_rl/tests/evaluation/test_reinflow_evaluation.py`
- Modify: `flow_rl/pyproject.toml`

**Interfaces:**
- Produces: `evaluate_reinflow_checkpoint(...) -> ReinFlowEvaluationSummary`
- Produces console script: `flow-rl-evaluate-reinflow`

- [x] **Step 1: Write seeded fake-adapter tests**

  Assert evaluation disables transition noise but preserves seeded initial stochasticity, exact episode count, frozen normalizer, finite latency/NFE, single-use output, and cleanup.

- [x] **Step 2: Implement reconstruction/evaluation and run tests**

### Task 9: BC training, online smoke, minimal ablation, and formal fine-tuning

**Files:**
- Create: `flow_rl/configs/phase6_reinflow_ablation_low_noise_no_entropy.yaml`
- Create: `reports/phase6/validation.md`
- Create: `reports/phase6/comparison.json`
- Modify: `docs/PROJECT_PLAN.md`

**Interfaces:**
- Produces: evidence-backed stage exit and separate pretraining/online costs

- [x] **Step 1: Train and evaluate Flow BC**

  Train the fixed dataset config, parse all losses/checkpoints, then evaluate 100 episodes on an isolated worker. Require mean return at least 80 before online work.

- [x] **Step 2: Run 24k ReinFlow smoke and resume check**

  Verify critic-only warmup, post-warmup actor/noise update, finite chain likelihood/ratio/entropy/KL/gradients, checkpoint resume, logs, process and port cleanup.

- [x] **Step 3: Run minimal short-budget ablation**

  Compare standard 0.10–0.24 noise with entropy 0.03 against 0.04–0.10 noise with entropy 0.0 from the same BC checkpoint, same 120k online budget and evaluation seed. Select by return subject to finite/stable metrics; record both, not only the winner.

- [x] **Step 4: Run selected 500k formal fine-tune**

  Train in a fresh directory, evaluate 100 episodes, and require mean return not below the same BC evaluation mean. If it is lower, preserve the result and run only the smallest evidence-backed adjustment allowed by the ablation.

- [x] **Step 5: Produce comparison and final validation**

  Report dataset sample count and collection source, BC GPU/wall-clock/epochs, BC evaluation, online decisions/wall-clock/NFE, ReinFlow evaluation, Gaussian PPO reference, noise/entropy ablation, parameter count and known differences from official action-chunk Gym configs.

- [x] **Step 6: Run strongest verification**

  Run real-Unity full pytest, `pip check`, `compileall`, JSON/CSV parsing, checkpoint validation, Unity log scan, process/port scan, `git diff --check`, and update every evidence-backed master-plan checkbox.
