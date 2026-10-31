# Phase 3 Gaussian PPO Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build, validate, and train a standalone tanh-Gaussian PPO baseline on 3DBall for two 500,000-step seeds, then collect a versioned ReinFlow demonstration dataset.

**Architecture:** Keep Unity LLAPI handling in the existing adapter, put Gaussian policy and critic networks in `models`, PPO-only math in `algorithms`, Agent-ID-safe experience assembly in `training/collector.py`, and run orchestration in `training/trainer.py`. Evaluation and dataset collection load immutable checkpoints and frozen observation statistics through separate CLIs.

**Tech Stack:** Python 3.10.12, NumPy 1.23.5, PyTorch 2.2.1+cu121, ML-Agents Env API 1.1.0, pytest 8.3.5, TensorBoard/CSV/YAML/JSON/NPZ, Unity 6000.0.72f1 3DBall build.

**Spec:** `docs/superpowers/specs/2026-08-26-phase-3-gaussian-ppo-design.md`

## Global Constraints

- Modify only `flow_rl/`, `reports/phase3/`, this implementation plan, and `docs/PROJECT_PLAN.md`.
- Do not modify `ml-agents/`, Unity assets, packages, project settings, existing builds, official PPO outputs, or the FPO/ReinFlow/PolicyFlow reference repositories.
- Use `.conda/flow-rl/python.exe`; keep `.conda/mlagents-r23` frozen.
- The production 3DBall actor and critic each use independent `[128, 128]` SiLU MLPs over the ordered flattened observation tuple.
- Production PPO uses rollout 12,000, batch 64, 3 epochs, `gamma=0.99`, `lambda=0.99`, LR `3e-4 → 1e-10`, clip `0.2 → 0.1`, entropy coefficient `1e-3 → 1e-5`, value coefficient `0.5`, and gradient norm limit `0.5` over 500,000 decision steps.
- Formal seeds are 0 and 1; each trains at least 500,000 Agent decisions and receives a separate deterministic 100-episode evaluation.
- Seed 0 final checkpoint is the fixed source for exactly 100,000 ReinFlow dataset samples.
- New production behavior follows strict TDD: add one behavior test, run it to the expected failure, implement the minimum, and rerun focused plus affected tests.
- Run/evaluation/dataset target directories are single-use and must reject existing nonempty targets.
- The outer repository has no valid `HEAD`; do not initialize, commit, branch, or create a worktree. At task boundaries record `git status --short -- game_project` from the parent as a read-only workspace checkpoint.

---

### Task 0: Start Stage 3 and record approved decisions

**Files:**
- Modify: `docs/PROJECT_PLAN.md`

**Interfaces:**
- Produces: an auditable `进行中` Stage 3 state before production code changes

- [x] **Step 1: Update current status and decision records**

  Change the current milestone and Stage 3 table/section status to `进行中`.
  Add decision records for the standalone tanh-Gaussian likelihood,
  base-Normal entropy convention, deterministic 100-episode evaluation, fixed
  seed-0 dataset source, and two formal Stage 3 seeds. Leave every Stage 3
  implementation checkbox unchecked until evidence exists.

- [x] **Step 2: Record planning evidence**

  Link the approved specification and this implementation plan from the Stage 3
  validation section. Record that the stage-2 baseline is `42 passed, 2
  deselected`, CodeGraph was used for official PPO semantics, and the outer
  repository has no valid HEAD/worktree capability.

---

### Task 1: Gaussian actor, critic, and policy boundary

**Files:**
- Create: `flow_rl/src/flow_rl/models/__init__.py`
- Create: `flow_rl/src/flow_rl/models/gaussian.py`
- Create: `flow_rl/src/flow_rl/models/critic.py`
- Create: `flow_rl/src/flow_rl/models/policy.py`
- Create: `flow_rl/tests/models/__init__.py`
- Create: `flow_rl/tests/models/test_gaussian.py`
- Create: `flow_rl/tests/models/test_critic.py`
- Create: `flow_rl/tests/models/test_policy.py`

**Interfaces:**
- Produces: `flatten_observations(observations: tuple[torch.Tensor, ...]) -> torch.Tensor`
- Produces: `TanhGaussianActor.sample(...) -> GaussianSample`
- Produces: `TanhGaussianActor.evaluate_actions(...) -> tuple[log_prob, entropy]`
- Produces: `ValueCritic.forward(observations) -> torch.Tensor`
- Produces: `GaussianActorCritic.act(...) -> PolicyOutput`
- Produces: `GaussianActorCritic.evaluate_actions(...) -> tuple[log_prob, entropy, values]`

- [x] **Step 1: Add failing Gaussian distribution tests**

  Write literal tests that establish the public contract before the module exists:

  ```python
  def test_tanh_gaussian_matches_hand_derived_scalar_log_probability() -> None:
      actor = fixed_actor(mean=0.25, log_std=-0.5)
      action = torch.tensor([[0.2]], dtype=torch.float32)
      log_prob, _ = actor.evaluate_actions((torch.zeros(1, 1),), action)
      z = 0.2027325540540822  # atanh(0.2), independently calculated
      base = -0.5 * ((z - 0.25) / math.exp(-0.5)) ** 2
      base -= -0.5 + 0.5 * math.log(2.0 * math.pi)
      expected = base - math.log(1.0 - 0.2**2)
      assert log_prob.item() == pytest.approx(expected, abs=1e-6)

  @pytest.mark.parametrize("action", [-1.0, -0.999999, 0.999999, 1.0])
  def test_log_probability_is_finite_at_action_boundaries(action: float) -> None:
      actor = fixed_actor(mean=0.0, log_std=0.0)
      log_prob, entropy = actor.evaluate_actions(
          (torch.zeros(1, 1),), torch.tensor([[action]], dtype=torch.float32)
      )
      assert torch.isfinite(log_prob).all()
      assert torch.isfinite(entropy).all()
  ```

  Add separate tests for output shape `(batch, action_size)`, `float32`, strict bounds, deterministic `tanh(mean)`, fixed-generator reproduction, tuple-stream flatten order, invalid stream batch sizes, and `log_std` clamp behavior. Name the production mutation each test catches in a one-line comment above the assertion group.

- [x] **Step 2: Run Gaussian tests and verify expected RED**

  Run:

  ```powershell
  & '..\.conda\flow-rl\python.exe' -m pytest `
    'tests\models\test_gaussian.py' -q
  ```

  Expected: collection fails because `flow_rl.models.gaussian` does not exist.

- [x] **Step 3: Implement the Gaussian actor minimally**

  Implement immutable sample output and stable math:

  ```python
  @dataclass(frozen=True)
  class GaussianSample:
      actions: torch.Tensor
      pre_tanh: torch.Tensor
      log_probs: torch.Tensor
      entropy: torch.Tensor

  def _stable_log_tanh_jacobian(pre_tanh: torch.Tensor) -> torch.Tensor:
      return 2.0 * (math.log(2.0) - pre_tanh - F.softplus(-2.0 * pre_tanh))

  class TanhGaussianActor(nn.Module):
      def sample(
          self,
          observations: tuple[torch.Tensor, ...],
          *,
          deterministic: bool,
          generator: torch.Generator | None = None,
      ) -> GaussianSample: ...

      def evaluate_actions(
          self,
          observations: tuple[torch.Tensor, ...],
          actions: torch.Tensor,
      ) -> tuple[torch.Tensor, torch.Tensor]: ...
  ```

  Sum log probabilities and analytic base-Normal entropy over the action axis,
  returning shape `(batch,)`. Clamp stored actions to `±(1-1e-6)` only for
  inverse tanh; never clip a sampled action after `tanh`.

- [x] **Step 4: Run Gaussian tests to GREEN**

  Run the focused file until all Gaussian tests pass, then run all existing
  non-integration tests to catch dtype or import regressions.

- [x] **Step 5: Add failing critic and combined-policy tests**

  Add tests proving scalar value shape `(batch,)`, finite gradients, actor and
  critic parameter independence, deterministic output, and that
  `GaussianActorCritic.evaluate_actions` returns the action's new log prob,
  entropy, and critic value for the same ordered observation tuple.

- [x] **Step 6: Implement critic and combined policy, then verify GREEN**

  Implement:

  ```python
  @dataclass(frozen=True)
  class PolicyOutput:
      actions: torch.Tensor
      pre_tanh: torch.Tensor
      log_probs: torch.Tensor
      values: torch.Tensor
      entropy: torch.Tensor

  class GaussianActorCritic(nn.Module):
      actor: TanhGaussianActor
      critic: ValueCritic
  ```

  Use separate actor and critic encoders. Run all three model test files and the
  complete non-integration suite.

- [x] **Step 7: Record task checkpoint**

  Mark Task 1 checkboxes, record the passing test count in this plan, and run
  read-only `git status --short -- game_project` from the parent directory.

---

### Task 2: PPO batches, losses, schedules, and optimizer

**Files:**
- Create: `flow_rl/src/flow_rl/algorithms/__init__.py`
- Create: `flow_rl/src/flow_rl/algorithms/ppo.py`
- Create: `flow_rl/tests/algorithms/__init__.py`
- Create: `flow_rl/tests/algorithms/test_ppo.py`

**Interfaces:**
- Consumes: `GaussianActorCritic.evaluate_actions`
- Produces: `PPOBatch`, `PPOMetrics`, `linear_schedule`, `normalize_advantages`
- Produces: `clipped_policy_loss`, `clipped_value_loss`, `PPOUpdater.update`

- [x] **Step 1: Add failing pure-math PPO tests**

  Use hand-derived literals rather than implementation helpers:

  ```python
  def test_clipped_policy_loss_uses_lower_positive_advantage_surrogate() -> None:
      old = torch.tensor([0.0])
      new = torch.tensor([math.log(1.5)])
      advantage = torch.tensor([2.0])
      loss, ratio, clip_fraction = clipped_policy_loss(
          new, old, advantage, clip_range=0.2
      )
      assert loss.item() == pytest.approx(-2.4)
      assert ratio.item() == pytest.approx(1.5)
      assert clip_fraction.item() == pytest.approx(1.0)

  def test_clipped_value_loss_uses_larger_error() -> None:
      loss = clipped_value_loss(
          values=torch.tensor([3.0]),
          old_values=torch.tensor([0.0]),
          returns=torch.tensor([2.0]),
          clip_range=0.2,
      )
      assert loss.item() == pytest.approx(1.62)  # 0.5 * (2.0 - 0.2)^2
  ```

  Add negative-advantage, zero-variance advantage, finite schedule endpoints,
  batch shape/dtype/NaN rejection, and exact final partial minibatch coverage.

- [x] **Step 2: Run PPO tests and verify expected RED**

  Expected: import failure for `flow_rl.algorithms.ppo`.

- [x] **Step 3: Implement validated batch and pure functions**

  `PPOBatch` contains `observations: tuple[Tensor, ...]`, `actions`,
  `old_log_probs`, `old_values`, `advantages`, and `returns`. Every numeric
  tensor must be `float32`, finite, share its leading batch length, and live on
  one device. `normalize_advantages` uses population standard deviation with
  `unbiased=False` and epsilon `1e-8`.

- [x] **Step 4: Run pure PPO tests to GREEN**

  Run the test file and confirm the literal policy/value cases pass.

- [x] **Step 5: Add a failing real optimizer update test**

  Construct a small real `GaussianActorCritic`, generate old log probabilities
  before updating, call `PPOUpdater.update`, and assert:

  - at least one actor and one critic parameter changes;
  - every metric is finite;
  - `approximate_kl = mean(old_log_prob - new_log_prob)` from a hand-captured
    pre-update batch;
  - reported actor/critic gradient norms are finite;
  - batch size 5 with minibatch size 2 consumes indices in sizes 2, 2, and 1;
  - the same model/batch/generator seed reproduces metrics and parameters.

- [x] **Step 6: Implement `PPOUpdater` and verify GREEN**

  Implement one Adam optimizer over actor and critic parameters, update all
  schedule values from `environment_steps / total_environment_steps`, shuffle
  with a supplied Torch generator, call `clip_grad_norm_` separately for actor
  and critic for reporting, and reject any non-finite loss or gradient norm
  before stepping. Average metrics over all minibatches and epochs.

- [x] **Step 7: Run affected and complete tests**

  Run `tests/algorithms/test_ppo.py`, all model tests, and the full
  non-integration suite. Record the count and workspace status.

---

### Task 3: Agent-ID-safe PPO collector and GAE batch assembly

**Files:**
- Create: `flow_rl/src/flow_rl/training/__init__.py`
- Create: `flow_rl/src/flow_rl/training/collector.py`
- Create: `flow_rl/tests/training/__init__.py`
- Create: `flow_rl/tests/training/test_collector.py`

**Interfaces:**
- Consumes: `EnvStep`, `ObservationNormalizer`, `compute_gae`, `GaussianActorCritic`
- Produces: `PendingDecision`, `CollectedTransition`, `RolloutCollection`
- Produces: `PPOCollector.reset`, `PPOCollector.step`, `PPOCollector.build_batch`

- [x] **Step 1: Add failing synthetic event tests**

  Use literal Agent IDs and rewards to prove ownership. Include these sequences:

  1. reset decisions `[20, 10]`, next decisions `[10, 20]`; assert actions,
     values, log probs, rewards, and observations stay attached to the original
     ID rather than batch index;
  2. natural terminal closes with `next_value=0`;
  3. interrupted terminal uses the critic's literal terminal-observation value;
  4. same batch contains decision ID 2 and terminal ID 2; assert one old
     transition and one new pending decision;
  5. terminal-only batch followed by delayed decision; assert the delayed
     reset reward is not a transition;
  6. build a two-transition trajectory with literal rewards/values and verify
     exact GAE/return arrays against hand calculations;
  7. after `build_batch`, pending actions survive while completed samples clear.

- [x] **Step 2: Run collector tests and verify expected RED**

  Expected: missing `flow_rl.training.collector`.

- [x] **Step 3: Implement pending/event state machine**

  Process terminal indices before nonterminal indices even though `EnvStep`
  stores Decision rows first. Build maps from Agent ID to row index and reject
  duplicate rows except exactly one terminal plus one decision boundary overlap.
  Evaluate terminal observations only for interrupted rows. Store the normalized
  observations actually used for each action.

- [x] **Step 4: Implement per-Agent GAE flattening**

  Group transitions by Agent ID, call `compute_gae` independently, then
  concatenate trajectories in sorted Agent-ID order while keeping tuple-stream
  observations aligned. Return a `PPOBatch` with literal sample count equal to
  the number of completed transitions.

- [x] **Step 5: Run collector tests to GREEN and full regression**

  Run collector, existing rollout/GAE/episode tests, then all non-integration
  tests. Record counts and workspace status.

---

### Task 4: Configuration, training loop, logging, and checkpoint resume

**Files:**
- Create: `flow_rl/src/flow_rl/training/config.py`
- Create: `flow_rl/src/flow_rl/training/trainer.py`
- Create: `flow_rl/src/flow_rl/cli/train_ppo.py`
- Create: `flow_rl/tests/training/test_config.py`
- Create: `flow_rl/tests/training/test_trainer.py`
- Modify: `flow_rl/src/flow_rl/tracking/run.py`
- Modify: `flow_rl/src/flow_rl/tracking/checkpoint.py`
- Modify: `flow_rl/pyproject.toml`
- Create: `flow_rl/configs/phase3_ppo_3dball.yaml`

**Interfaces:**
- Produces: `PPOTrainingConfig.from_yaml`, `.to_dict`, `.validate`
- Produces: `PPOTrainer.train() -> TrainingSummary`
- Produces: `RunLogger.log_update(metrics, environment_steps)`
- Produces console script: `flow-rl-train-ppo`

- [x] **Step 1: Add failing configuration tests**

  Parse a complete temporary YAML and assert every resolved literal. Reject
  missing build, nonpositive sizes, rollout smaller than batch, invalid schedule
  endpoints, duplicate training/evaluation worker IDs, unsupported device, and
  an existing nonempty run directory before Unity launch.

- [x] **Step 2: Run configuration tests to expected RED**

  Expected: missing configuration module.

- [x] **Step 3: Implement typed configuration and production YAML**

  Use an immutable dataclass with exact fields from the design. Resolve build,
  run, and resume paths relative to the YAML file. `device: cuda` must require
  `torch.cuda.is_available()`. Serialize only plain YAML types.

- [x] **Step 4: Add failing run logger and checkpoint-resume tests**

  Extend real artifact tests to parse `updates.csv` with all `PPOMetrics`
  columns. Checkpoint round-trip must restore exact deterministic actions,
  optimizer tensors, normalizer state, environment/update steps, elapsed time,
  NumPy generator state, Torch CPU RNG, and CUDA RNG when available. A changed
  observation shape, action size, seed, build hash, hidden size, or schedule
  config must raise a clear mismatch error.

- [x] **Step 5: Implement update logging and checkpoint validation**

  Keep `save_checkpoint` atomic. Add PPO-specific metadata validation in the
  trainer rather than weakening the generic checkpoint helper. Flush update CSV
  and TensorBoard scalars after each optimizer update.

- [x] **Step 6: Add failing fake-environment trainer test**

  Use a real actor, critic, updater, normalizer, collector, logger, and
  checkpoint against a deterministic in-memory adapter. Assert exact decision
  count, at least one update, parameter change, episode rows, final checkpoint,
  finite summary, and adapter close on both success and injected exception.

- [x] **Step 7: Implement `PPOTrainer` and CLI**

  The loop must:

  1. create `RunLogger` before Unity side effects;
  2. seed Python, NumPy, Torch, and CUDA;
  3. reset adapter and construct normalizer/model from observed shapes/action;
  4. collect until 12,000 completed transitions, update, and continue without
     dropping pending actions;
  5. checkpoint at each crossed 100,000-step boundary and at final exit;
  6. continue a final liveness drain until all initial IDs appear;
  7. always close Unity and logger;
  8. write `summary.json` only after a successful final checkpoint.

- [x] **Step 8: Verify trainer GREEN and regressions**

  Run config/trainer/tracking tests and the full non-integration suite. Run
  `pip check` and confirm the new console entry point imports.

---

### Task 5: Deterministic checkpoint evaluation

**Files:**
- Create: `flow_rl/src/flow_rl/cli/evaluate_ppo.py`
- Create: `flow_rl/tests/evaluation/test_ppo_evaluation.py`
- Modify: `flow_rl/src/flow_rl/evaluation/runner.py`
- Modify: `flow_rl/pyproject.toml`

**Interfaces:**
- Produces: `evaluate_ppo_checkpoint(...) -> PPOEvaluationSummary`
- Produces console script: `flow-rl-evaluate-ppo`

- [x] **Step 1: Add failing evaluation tests**

  With a real checkpoint and in-memory adapter, assert deterministic actions,
  frozen normalizer count, exactly requested summaries after evaluator slicing,
  mean/std/min/max literals, decision-step count, finite wall-clock and
  throughput, parameter count, unequal worker validation, nonempty target
  rejection, and cleanup on exception.

- [x] **Step 2: Run evaluation tests to expected RED**

  Expected: missing checkpoint evaluation module/API.

- [x] **Step 3: Implement evaluation loader and CLI**

  Reconstruct architecture only from validated checkpoint config and metadata,
  load normalizer/model strictly, use `model.eval()` and `torch.inference_mode()`,
  call the existing evaluator with deterministic bounded actions, and write
  `episodes.csv`, `config.yaml`, TensorBoard events, Unity log, and
  `evaluation.json` in a new single-use directory.

- [x] **Step 4: Run evaluation tests and full regression**

  Confirm all evaluation tests and the complete non-integration suite pass.

---

### Task 6: Versioned ReinFlow demonstration dataset

**Files:**
- Create: `flow_rl/src/flow_rl/data/demonstrations.py`
- Create: `flow_rl/src/flow_rl/cli/collect_ppo_dataset.py`
- Create: `flow_rl/tests/data/test_demonstrations.py`
- Modify: `flow_rl/pyproject.toml`

**Interfaces:**
- Produces: `DemonstrationBatch`, `save_demonstrations`, `load_demonstrations`
- Produces: `collect_ppo_demonstrations(...) -> DemonstrationSummary`
- Produces console script: `flow-rl-collect-ppo-dataset`

- [x] **Step 1: Add failing dataset artifact tests**

  Save two literal observation streams, actions, Agent IDs, terminated, and
  truncated flags. Reload and assert exact arrays, dtype, readonly ownership,
  action bounds, SHA-256 metadata, and rejection of NaN, mismatched rows,
  simultaneous terminal/truncated flags, and existing targets.

- [x] **Step 2: Run artifact tests to expected RED**

  Expected: missing demonstrations module.

- [x] **Step 3: Implement atomic NPZ and metadata artifacts**

  Write compressed NPZ and adjacent JSON through temporary files in the target
  directory followed by `os.replace`. If either final target exists, fail before
  creating a temporary file. Validate on load rather than trusting stored data.

- [x] **Step 4: Add failing collection test**

  Use a real actor checkpoint and deterministic in-memory adapter to collect
  exactly 5 samples from batches of sizes 3 and 3; assert only the first 2 rows
  of the second batch are retained, actions are the exact submitted bounded
  actions, normalizer stays frozen, metadata names the checkpoint/build hashes,
  and adapter closes.

- [x] **Step 5: Implement collection CLI and verify GREEN**

  Load the seed-0 checkpoint, create a separate seeded Torch generator for
  stochastic action sampling, retain raw observations, align outcome flags to
  submitted actions by Agent ID, and stop at exactly the configured sample
  count. Run dataset, evaluation, and full non-integration tests.

---

### Task 7: Real Unity smoke training and failure-driven hardening

**Files:**
- Create: `flow_rl/tests/integration/test_ppo_training_smoke.py`
- Create: `reports/phase3/README.md`
- Create artifacts under: `reports/phase3/ppo-smoke/`

**Interfaces:**
- Consumes: production training/evaluation CLIs and 3DBall build
- Produces: first real PPO checkpoint and smoke evidence

- [x] **Step 1: Add an opt-in real Unity integration test**

  Require `FLOW_RL_UNITY_BUILD`. Use a unique worker, small hidden sizes and
  rollout/batch values sufficient for one optimizer update, then assert final
  decision count, changed parameters, finite metrics, checkpoint reload, and no
  leaked process. Keep it excluded by the existing `integration` marker.

- [x] **Step 2: Run integration test and diagnose the first real failure**

  Run with the real build. For every unexpected failure, use
  `superpowers:systematic-debugging`, write a focused regression test that
  reproduces the root cause, then apply the smallest fix. Preserve failed run
  directories with descriptive suffixes.

- [x] **Step 3: Run the 24,000-step production smoke**

  Use worker 79, seed 0, CUDA, time scale 20, rollout 12,000, batch 64, and 3
  epochs. Verify at least one update, finite metrics, checkpoint resume into a
  fresh directory, deterministic short evaluation on worker 89, Unity-log scan,
  ports 5084/5094, and process cleanup.

- [x] **Step 4: Run complete pre-training verification**

  Run all non-integration tests, all opt-in integration tests, and `pip check`.
  Do not start 500k runs while any regression is unresolved.

---

### Task 8: Two formal 500k seeds, evaluation, dataset, and phase evidence

**Files:**
- Create artifacts under: `reports/phase3/ppo-seed-0/`
- Create artifacts under: `reports/phase3/ppo-seed-1/`
- Create artifacts under: `reports/phase3/evaluation-seed-0/`
- Create artifacts under: `reports/phase3/evaluation-seed-1/`
- Create artifacts under: `reports/phase3/reinflow-dataset-seed-0/`
- Create: `reports/phase3/comparison.json`
- Modify: `reports/phase3/README.md`
- Modify: `docs/PROJECT_PLAN.md`

**Interfaces:**
- Produces: evidence satisfying every Stage 3 checkbox and exit criterion

- [x] **Step 1: Verify Stage 3 remains in progress before formal training**

  Re-read the Stage 3 section and decision records. Confirm status is still
  `进行中`, all formal-run settings match the approved spec, and no unchecked
  implementation or exit checkbox was marked without evidence.

- [x] **Step 2: Train seed 0 for 500,000 decisions**

  Run worker 80 with the production YAML into a fresh `ppo-seed-0` directory.
  Monitor update CSV for finite losses, KL, clip fraction, ratio-derived
  metrics, and gradient norms. Verify final checkpoint, summary count, logs,
  port 5085, and process cleanup.

- [x] **Step 3: Evaluate seed 0 for 100 episodes**

  Run deterministic evaluation on worker 90. Parse episode CSV and require mean
  return at least 90, finite standard deviation, exactly 100 rows, continuous
  per-Agent episode indices, frozen normalizer, and no process/port leak.

- [x] **Step 4: Train and evaluate seed 1**

  Repeat the exact protocol with training worker 81 and evaluation worker 91,
  changing only the seed and single-use paths. Require the same acceptance
  checks and mean return threshold.

- [x] **Step 5: Collect the seed-0 100,000-sample dataset**

  Use the seed-0 final checkpoint and a fixed dataset seed into a new
  `reinflow-dataset-seed-0` directory. Parse NPZ/JSON and assert exactly 100,000
  rows, observation shape `(100000, 8)`, action shape `(100000, 2)`, required
  dtypes, finite values, action bounds, hashes, and frozen normalizer metadata.

- [x] **Step 6: Generate the official comparison**

  Read only `results/ppo-3dball-baseline/configuration.yaml`,
  `run_logs/training_status.json`, `run_logs/timers.json`, and the official
  checkpoint. Write `comparison.json` containing for each implementation:
  environment steps, training or evaluation return with protocol label,
  wall-clock, steps/s, actor/critic/total parameter count, seeds, observation
  normalization, and known protocol differences. Never label official training
  return as independent evaluation.

- [x] **Step 7: Run final verification before claiming completion**

  Invoke `superpowers:verification-before-completion` and
  `unity-workbench:unity-build-validation`. Run fresh full non-integration and
  integration suites, `pip check`, artifact parsers, Unity log scans, process
  checks, and ports 5084–5096. Confirm both formal evaluations meet 90.

- [x] **Step 8: Request code review and apply valid findings**

  Invoke `superpowers:requesting-code-review`. Since project instructions do not
  authorize multi-agent implementation, perform local review unless the user
  separately authorizes a reviewer agent. For every valid defect, use a failing
  regression test before fixing it and rerun the strongest affected validation.

- [x] **Step 9: Update the plan and hand off**

  Mark only evidence-backed Stage 3 boxes and exit criteria complete. Record
  exact commands, test counts, metrics, output paths, blockers, and decision
  records in `docs/PROJECT_PLAN.md`. Keep Stage 4 `未开始`. Invoke
  `superpowers:finishing-a-development-branch`; because the outer repository
  has no valid HEAD, report that commit/merge options are unavailable and leave
  the current workspace intact.
