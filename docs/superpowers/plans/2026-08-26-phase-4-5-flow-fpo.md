# Phase 4–5 Flow Policy and FPO Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add reusable Rectified Flow policy infrastructure, reproduce the pinned local GridWorld FPO update, and train/evaluate FPO on Unity 3DBall with reproducible NFE and cost measurements.

**Architecture:** Keep Flow networks and Euler integration independent from FPO optimization. Move Unity event ownership into a generic on-policy collector core, then retain thin PPO and FPO batch builders so both algorithms share Agent-ID, GAE, terminal, logging, checkpoint, and evaluation infrastructure without sharing probability semantics.

**Tech Stack:** Python 3.10.12, NumPy 1.23.5, PyTorch 2.2.1+cu121, ML-Agents Env API 1.1.0, pytest 8.3.5, TensorBoard/CSV/YAML/JSON, Unity 6000.0.72f1 3DBall build, pinned local FPO commit `418c2554f7cd22d52e14c07d951280929d73bf2f`.

**Spec:** `docs/superpowers/specs/2026-08-26-phase-4-5-flow-fpo-design.md`

## Global Constraints

- Modify only `flow_rl/`, `reports/phase4/`, `reports/phase5/`, `docs/superpowers/`, and `docs/PROJECT_PLAN.md`.
- Treat `ml-agents/`, `fpo/`, `ReinFlow/`, `PolicyFlow/`, Unity assets, packages, project settings, and existing builds as read-only.
- Use `.conda/flow-rl/python.exe` for the Unity implementation; create `.conda/fpo-gridworld` only for the official smoke reproduction; keep `.conda/mlagents-r23` frozen.
- FPO math follows local `fpo/gridworld/models/fpo.py` and `diffusion_policy.py`: signed normalized advantage, same stored noise/time, two-level `[-3,3]` CFM-difference clamp, exponential ratio, PPO clip, MSE critic, and separate actor/critic gradient clipping.
- Flow sampling uses CondOT, 0→1 Euler, `tanh` only at the Unity action boundary, default NFE 4, and allowed experimental NFE values 1/2/4/8.
- Formal FPO seeds are 0/1/2, each with 500,000 environment decisions and an isolated seeded 100-episode evaluation.
- Preserve all existing workspace changes. Stage 3 tests and artifacts are regression evidence and must not be rewritten or deleted.
- Every new behavior follows strict TDD: add one behavior test, observe the expected failure, implement the minimum, and rerun focused plus affected tests.

---

### Task 0: Start Stage 4 and record the approved source decision

**Files:**
- Modify: `docs/PROJECT_PLAN.md`

**Interfaces:**
- Produces: Stage 4 status `进行中`, links to approved spec/plan, and an auditable baseline

- [x] **Step 1: Update milestone and Stage 4 status**

  Change only the current milestone, stage table row 4, and Stage 4 section status to `进行中`. Keep Stage 5 `未开始` until Stage 4 exit evidence passes.

- [x] **Step 2: Record baseline evidence**

  Run:

  ```powershell
  & '.\.conda\flow-rl\python.exe' -m pytest flow_rl/tests -q
  & '.\.conda\flow-rl\python.exe' -m pip check
  git status --short
  ```

  Record exact counts, the approved spec/plan paths, local FPO commit, and existing unrelated workspace changes in the Stage 4 evidence section.

- [ ] **Step 3: Commit the planning-state update**

  ```powershell
  git add -- docs/PROJECT_PLAN.md docs/superpowers/plans/2026-08-26-phase-4-5-flow-fpo.md
  git commit -m "docs: start flow policy phase"
  ```

---

### Task 1: Rectified Flow network and Euler sampler

**Files:**
- Create: `flow_rl/src/flow_rl/models/flow.py`
- Modify: `flow_rl/src/flow_rl/models/__init__.py`
- Create: `flow_rl/tests/models/test_flow.py`

**Interfaces:**
- Produces: `FlowTimeEmbedding.forward(time: Tensor) -> Tensor`
- Produces: `FlowStateEncoder.forward(observations: tuple[Tensor, ...]) -> Tensor`
- Produces: `ConditionalVelocityMLP.forward(observations, latent_action, time) -> Tensor`
- Produces: `EulerFlowSampler.sample(observations, *, nfe, generator, initial_noise=None, return_path=False) -> FlowSample`
- Produces: `FlowSample(latent_actions, actions, nfe, path)`

- [x] **Step 1: Write failing component tests**

  Add literal tests for tuple-stream flatten order, time shape validation, velocity output `(B,A)`, float32/device preservation, invalid batch dimensions, and finite backward gradients. Include a constant-velocity network test:

  ```python
  sample = sampler.sample(
      observations,
      nfe=4,
      initial_noise=torch.tensor([[1.0, -1.0]], dtype=torch.float32),
  )
  assert sample.latent_actions == pytest.approx([[1.5, -0.5]])
  assert sample.actions == pytest.approx(torch.tanh(sample.latent_actions))
  assert velocity.calls == 4
  ```

- [x] **Step 2: Run the focused test to RED**

  ```powershell
  & '.\.conda\flow-rl\python.exe' -m pytest flow_rl/tests/models/test_flow.py -q
  ```

  Expected: collection fails because `flow_rl.models.flow` does not exist.

- [x] **Step 3: Implement the minimal network components**

  Use the existing observation flatten convention, sinusoidal time features followed by an MLP, a 128-wide state encoder, and a 2×128 SiLU conditional velocity head. Validate finite float32 tensors at public boundaries.

- [x] **Step 4: Add failing sampler reproducibility and bounds tests**

  Assert generators with the same state yield bit-identical samples, sequential calls advance state, actions remain in `[-1,1]`, path shape is `(B,nfe+1,A)`, and NFE 1/2/4/8 invokes exactly that many velocity evaluations.

- [x] **Step 5: Implement Euler sampling and verify GREEN**

  Integrate with `dt=1/nfe`, times `k/nfe`, and no gradient-disabling inside the sampler. Return latent actions before `tanh`; never clamp them.

- [x] **Step 6: Run model and full regression tests**

  Run `test_flow.py`, existing model tests, then the entire suite. Commit only Task 1 files.

---

### Task 2: Flow checkpoint schema and NFE benchmark

**Files:**
- Create: `flow_rl/src/flow_rl/evaluation/flow_benchmark.py`
- Create: `flow_rl/src/flow_rl/cli/benchmark_flow.py`
- Modify: `flow_rl/src/flow_rl/tracking/checkpoint.py`
- Modify: `flow_rl/pyproject.toml`
- Create: `flow_rl/tests/evaluation/test_flow_benchmark.py`
- Modify: `flow_rl/tests/tracking/test_checkpoint.py`

**Interfaces:**
- Produces: `FlowSolverConfig(method, direction, nfe, action_transform)`
- Produces: `validate_flow_checkpoint(payload) -> None`
- Produces: `benchmark_flow_sampler(...) -> tuple[FlowBenchmarkResult, ...]`
- Produces console script: `flow-rl-benchmark-flow`

- [x] **Step 1: Add failing schema tests**

  Save/load a literal flow checkpoint and assert model, normalizer, solver, network sizes, NFE, seed, and RNG state survive. Reject missing solver fields, non-Euler method, wrong direction, unsupported action transform, and structural mismatch on resume.

- [x] **Step 2: Run checkpoint tests to RED, implement strict validator, rerun GREEN**

  Keep the generic atomic `save_checkpoint` payload unchanged; add Flow-specific validation without weakening PPO checkpoints.

- [x] **Step 3: Add failing benchmark tests with an injected clock**

  Assert sorted NFE output, warmups excluded, mean/P50/P95 finite, exact batch size/repeat count, identical initial noise for each NFE, and CPU peak GPU memory represented as `None`.

- [x] **Step 4: Implement benchmark and CLI**

  Synchronize CUDA around timing, reset/read `torch.cuda.max_memory_allocated()` only on CUDA, and write JSON with device, batch size, observation shapes, action size, NFE, latency, peak memory, and parameter count.

- [x] **Step 5: Run NFE 1/2/4/8 benchmark twice**

  Use a fixed synthetic observation batch and seed. Save identical-schema results to `reports/phase4/nfe-benchmark-run1.json` and `run2.json`; verify all metrics finite and NFE fields exact.

- [x] **Step 6: Run full regression and commit Task 2**

---

### Task 3: Shared on-policy event core without PPO regression

**Files:**
- Create: `flow_rl/src/flow_rl/training/on_policy.py`
- Modify: `flow_rl/src/flow_rl/training/collector.py`
- Create: `flow_rl/tests/training/test_on_policy.py`
- Modify: `flow_rl/tests/training/test_collector.py`

**Interfaces:**
- Produces: generic `PendingDecision[AuxT]`, `CollectedTransition[AuxT]`, `RolloutCollection`
- Produces: `OnPolicyCollectorCore.process(step, decisions) -> RolloutCollection`
- Consumes: algorithm callback returning bounded actions, values, and per-row auxiliary records
- Preserves: all public `PPOCollector` methods and returned `PPOBatch`

- [x] **Step 1: Add characterization tests before refactoring**

  Extend PPO tests for terminal+decision overlap, terminal-only steps, truncated bootstrap, pending survival across batch builds, shuffled Agent IDs, and exact action/log-prob ownership.

- [x] **Step 2: Run characterization tests GREEN**

  This is the safety baseline; do not refactor until all cases pass.

- [x] **Step 3: Add failing generic-core tests**

  Use string or dataclass auxiliary payloads to prove the core stores them by Agent ID, closes old pending transitions before opening new decisions, and never interprets algorithm fields.

- [x] **Step 4: Implement the generic event core**

  Move only event validation, pending/trajectory ownership, terminal bootstrap requests, and append behavior. Keep tensor conversion, policy calls, GAE, and PPOBatch construction in the PPO wrapper.

- [x] **Step 5: Rewire `PPOCollector` and verify byte-for-behavior compatibility**

  All old collector tests and the complete phase-3 suite must pass unchanged. Run a fake PPO training smoke to prove trainer integration.

- [x] **Step 6: Commit Task 3 independently**

---

### Task 4: FPO batch, CFM math, ratio, and optimizer

**Files:**
- Create: `flow_rl/src/flow_rl/algorithms/fpo.py`
- Modify: `flow_rl/src/flow_rl/algorithms/__init__.py`
- Create: `flow_rl/tests/algorithms/test_fpo.py`

**Interfaces:**
- Produces: `FPOBatch(observations, latent_actions, bounded_actions, loss_eps, loss_t, old_cfm_losses, old_values, advantages, returns)`
- Produces: `conditional_flow_matching_loss(...) -> Tensor` with result shape `(B,N)`
- Produces: `fpo_ratio(old_losses, new_losses, difference_clip=3.0) -> (ratio, log_ratio)`
- Produces: `fpo_clipped_policy_loss(ratio, advantages, clip_range) -> (...)`
- Produces: `FPOUpdater.update(batch, environment_steps) -> FPOMetrics`

- [x] **Step 1: Add literal CFM tests derived from the pinned clone**

  Use one-dimensional values whose answer is hand-computable:

  ```python
  action = torch.tensor([[[2.0]]])
  eps = torch.tensor([[[0.0]]])
  time = torch.tensor([[[0.25]]])
  # x_t=0.5, target velocity=2.0; predicted velocity=1.0
  assert conditional_flow_matching_loss(...) == pytest.approx([[1.0]])
  ```

  Add shape/dtype/NaN rejection and prove gradients reach only current actor parameters.

- [x] **Step 2: Run FPO math tests to RED, implement CFM, rerun**

- [x] **Step 3: Add literal two-level clamp and signed-advantage tests**

  Cover positive, zero, and negative advantages. Prove per-sample loss differences clamp before MC mean, the mean clamps again, ratio is `exp(log_ratio)`, and negative advantage selects the correct PPO clipped branch.

- [x] **Step 4: Implement pure ratio and surrogate functions**

  Match local clone lines 211–220 exactly in operation order. `positive_advantage` is accepted only as an explicit optional config branch and defaults false.

- [x] **Step 5: Add a failing real updater test**

  Construct a real velocity network and critic; assert actor and critic parameters change, stored noise/time remain unchanged, all required metrics are finite, partial minibatches are consumed, and fixed seeds reproduce metrics/parameters.

- [x] **Step 6: Implement `FPOUpdater`**

  Use separate Adam optimizers for actor and critic, signed population-normalized advantages, MSE critic, separate gradient clipping, linear learning-rate/clip schedules, and explicit finite checks before optimizer steps.

- [x] **Step 7: Run algorithm/model/full regression and commit Task 4**

---

### Task 5: Flow actor-critic and Agent-ID-safe FPO collector

**Files:**
- Create: `flow_rl/src/flow_rl/models/fpo_policy.py`
- Create: `flow_rl/src/flow_rl/training/fpo_collector.py`
- Create: `flow_rl/tests/models/test_fpo_policy.py`
- Create: `flow_rl/tests/training/test_fpo_collector.py`

**Interfaces:**
- Produces: `FPOPolicyOutput(actions, latent_actions, loss_eps, loss_t, old_cfm_losses, values, nfe)`
- Produces: `FlowActorCritic.act(..., nfe, num_fpo_samples, generator) -> FPOPolicyOutput`
- Produces: `FPOCollector.reset`, `.step`, `.build_batch`

- [x] **Step 1: Add failing policy tests**

  Assert bounded actions and unbounded latent targets remain distinct, loss tensors have `(B,N,A)`, `(B,N,1)`, `(B,N)`, old loss is detached, fixed generators reproduce outputs, and changing NFE changes only solver calls rather than CFM sample count.

- [x] **Step 2: Implement `FlowActorCritic` and verify model GREEN**

  Use one velocity actor and the existing independent `ValueCritic`. Generate sampling noise first, then CFM noise/time with the same explicit generator in a documented stable order.

- [x] **Step 3: Add failing FPO collector alignment tests**

  Repeat shuffled IDs, natural terminal, interrupted terminal, terminal+new-decision overlap, and terminal-only cases. Assert latent/bounded/eps/time/old-loss records stay attached to the originating Agent ID and GAE is unchanged from PPO.

- [x] **Step 4: Implement the thin FPO collector wrapper**

  Delegate all LLAPI event semantics to `OnPolicyCollectorCore`; the wrapper only normalizes observations, calls policy/critic, and builds validated `FPOBatch` tensors.

- [x] **Step 5: Run both PPO and FPO collector suites plus full regression**

- [x] **Step 6: Commit Task 5**

---

### Task 6: FPO configuration, trainer, logging, and resume

**Files:**
- Create: `flow_rl/src/flow_rl/training/fpo_config.py`
- Create: `flow_rl/src/flow_rl/training/fpo_trainer.py`
- Create: `flow_rl/src/flow_rl/cli/train_fpo.py`
- Modify: `flow_rl/src/flow_rl/tracking/run.py`
- Modify: `flow_rl/pyproject.toml`
- Create: `flow_rl/configs/phase5_fpo_3dball.yaml`
- Create: `flow_rl/configs/phase5_fpo_3dball_seed1.yaml`
- Create: `flow_rl/configs/phase5_fpo_3dball_seed2.yaml`
- Create: `flow_rl/configs/phase5_fpo_3dball_smoke.yaml`
- Create: `flow_rl/tests/training/test_fpo_config.py`
- Create: `flow_rl/tests/training/test_fpo_trainer.py`

**Interfaces:**
- Produces: `FPOTrainingConfig.from_yaml`, `.validate`, `.to_dict`
- Produces: `FPOTrainer.train() -> TrainingSummary`
- Produces console script: `flow-rl-train-fpo`

- [x] **Step 1: Add failing strict configuration tests**

  Require all PPO shared fields plus `state_size`, `time_embedding_size`, `velocity_hidden_sizes`, `nfe`, `num_fpo_samples`, `difference_clip`, and `positive_advantage`. Reject unknown/missing keys, NFE outside 1/2/4/8, true `positive_advantage` for production configs, nonpositive MC count, and invalid run/build/resume paths.

- [x] **Step 2: Implement config and exact YAMLs**

  Formal configs use seeds 0/1/2, 500k steps, NFE 4, `num_fpo_samples=50`, 2×128 SiLU velocity capacity, the phase-3 shared gamma/lambda/LR/clip/batch/epoch/gradient settings, unique workers/directories, and the same 3DBall build.

- [x] **Step 3: Add failing fake-adapter trainer tests**

  Assert exact environment count, update metrics, final drain, parameter changes, checkpoint schema/RNG state, resume equivalence, cleanup on exception, and no change to PPO trainer behavior.

- [x] **Step 4: Implement FPO trainer using shared services**

  Reuse adapter creation, liveness, episode logging, normalizer, atomic checkpoint, and `TrainingSummary`. Do not subclass or call `PPOUpdater`. Include local FPO commit and solver schema in each checkpoint.

- [x] **Step 5: Extend update logger columns generically**

  Preserve existing PPO CSV schema and allow FPO-specific finite metric fields without silently dropping them.

- [x] **Step 6: Run config/trainer/checkpoint/full regression and commit Task 6**

---

### Task 7: Seeded FPO evaluation and checkpoint NFE ablation

**Files:**
- Create: `flow_rl/src/flow_rl/evaluation/fpo.py`
- Create: `flow_rl/src/flow_rl/cli/evaluate_fpo.py`
- Create: `flow_rl/src/flow_rl/cli/ablate_fpo_nfe.py`
- Modify: `flow_rl/pyproject.toml`
- Create: `flow_rl/tests/evaluation/test_fpo_evaluation.py`
- Create: `flow_rl/tests/evaluation/test_fpo_nfe_ablation.py`

**Interfaces:**
- Produces: `evaluate_fpo_checkpoint(...) -> FPOEvaluationSummary`
- Produces: `ablate_fpo_nfe(...) -> tuple[FPONFEResult, ...]`
- Produces console scripts: `flow-rl-evaluate-fpo`, `flow-rl-ablate-fpo-nfe`

- [x] **Step 1: Add failing seeded evaluation tests**

  Load a real tiny checkpoint against a fake adapter. Assert identical checkpoint/seed/NFE yields identical action sequence and summary, different seed changes the sequence, normalizer remains frozen, requested episode count is exact, output directory is single-use, and cleanup occurs on exceptions.

- [x] **Step 2: Implement checkpoint reconstruction and evaluation**

  Restore structure only from validated checkpoint config/metadata, use a dedicated generator seeded from the evaluation argument, and write config, episodes, TensorBoard, Unity log, and JSON summary.

- [x] **Step 3: Add failing NFE ablation fairness tests**

  Assert each NFE receives the same initial noise sequence, one checkpoint is loaded once, output rows are ordered 1/2/4/8, and each contains return, actual velocity calls, latency, parameter count, and memory fields.

- [x] **Step 4: Implement ablation CLI and verify GREEN**

- [x] **Step 5: Run evaluation/full regression and commit Task 7**

---

### Task 8: Reproduce the pinned official GridWorld FPO smoke

**Files:**
- Create: `flow_rl/scripts/reproduce_official_fpo_gridworld.py`
- Create: `reports/phase5/official-gridworld/README.md`
- Create: `reports/phase5/official-gridworld/result.json`

**Interfaces:**
- Consumes read-only: `fpo/gridworld/models/fpo.py`, `diffusion_policy.py`, `main.py`, `requirements.txt`
- Produces: a bounded official-class rollout/update result with versions and finite metrics

- [x] **Step 1: Record source and dependency facts**

  Use CodeGraph before targeted reads. Record local commit, Python constraints, exact requirements, official CLI, hardcoded 200M-step behavior, default NFE, MC samples, advantage branch, difference clamp, and clip settings.

- [x] **Step 2: Create the isolated environment**

  Create `.conda/fpo-gridworld` with a compatible Python and install only the GridWorld requirements. Run `pip check` and save `pip freeze` in the report. Do not change either existing project environment.

- [x] **Step 3: Write and run a bounded official smoke harness**

  Import the official classes from the read-only clone, disable W&B online mode and rendering, use a temporary output directory, set a fixed seed, and execute one rollout/update-sized call rather than `main.py`'s 200M-step run. Assert action, CFM loss, ratio, actor loss, critic loss, and gradients are finite.

- [x] **Step 4: Save commands/results and commit Task 8 artifacts**

---

### Task 9: Stage 4 exit, Unity smoke, formal FPO runs, and Stage 5 exit

**Files:**
- Create: `reports/phase4/validation.md`
- Create: `reports/phase5/validation.md`
- Create: `reports/phase5/comparison.json`
- Modify: `docs/PROJECT_PLAN.md`

**Interfaces:**
- Produces: auditable Stage 4 and Stage 5 exit evidence and final experiment paths

- [x] **Step 1: Complete Stage 4 validation before starting Stage 5 state**

  Run all Flow math tests, two benchmark runs, full pytest, `pip check`, and `compileall`. Mark Stage 4 tasks/exit criteria complete with commands and artifacts; change Stage 4 to `已完成`, Stage 5 to `进行中`, and update the current milestone.

- [x] **Step 2: Run a CUDA 3DBall FPO smoke**

  Use the smoke YAML, inspect updates/episodes/summary/checkpoint, scan Unity logs for exceptions, and verify flow loss, ratio, proxy KL, critic loss, and gradient norms are finite before formal runs.

- [x] **Step 3: Train formal seeds 0/1/2**

  Run each 500k config to a distinct single-use directory. After each run, parse every update row for finite fields, verify final checkpoint metadata, and ensure no orphan Unity processes/ports remain.

- [x] **Step 4: Evaluate all three final checkpoints**

  Use 100 episodes, isolated worker IDs, fixed documented evaluation seeds, and NFE 4. Require at least two mean returns ≥90 and record mean/std/min/max, environment steps, wall time, throughput, and parameter count.

- [x] **Step 5: Run NFE 1/2/4/8 ablation**

  Use the selected fixed final checkpoint and shared initial noise/evaluation seed. Record return plus actual NFE, batch latency, end-to-end inference latency, peak GPU memory, and parameter count.

- [x] **Step 6: Produce the Gaussian PPO comparison**

  Compare only equal 500k environment-step runs. Report configuration differences, returns, wall-clock, update throughput, inference cost, parameter count, and seeds; do not claim distributional superiority from 3DBall.

- [x] **Step 7: Run final verification**

  ```powershell
  & '.\.conda\flow-rl\python.exe' -m pytest flow_rl/tests -q
  & '.\.conda\flow-rl\python.exe' -m pip check
  & '.\.conda\flow-rl\python.exe' -m compileall -q flow_rl/src flow_rl/tests
  git diff --check
  git status --short
  ```

  Parse all JSON/YAML/CSV artifacts, scan Unity logs, and confirm no training/build process remains.

- [ ] **Step 8: Update master plan and commit completion**

  Mark only evidence-backed Stage 4/5 boxes. Record dates, commands, exact results, artifact paths, blockers, and decisions. Commit implementation and evidence without staging unrelated user files.
