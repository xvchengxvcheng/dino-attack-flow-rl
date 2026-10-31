# Phase 3 Gaussian PPO Design

## Purpose

Phase 3 adds a standalone Gaussian PPO baseline to `flow_rl`. It must train the
existing `Builds/3DBall/3DBall.exe` through the ML-Agents Low-Level API without
depending on `mlagents-learn` internals. The result establishes the shared
model, rollout, optimization, evaluation, checkpoint, logging, and dataset
interfaces that later Flow algorithms reuse.

The phase is complete only after two fixed seeds each train for at least
500,000 Agent decision steps, both runs remain numerically stable, independent
deterministic evaluation reaches a mean return of at least 90, a versioned
`(observation, action)` dataset is collected, and comparison evidence against
the official PPO baseline is recorded.

## Scope

### Included

- A tanh-squashed diagonal Gaussian continuous actor.
- A separate scalar critic with the same encoder capacity as the actor.
- Stable action sampling, deterministic action selection, and log-probability
  evaluation at action-space boundaries.
- PPO policy clipping, clipped value loss, base-Gaussian entropy regularization,
  normalized advantages, approximate KL, clip fraction, and gradient clipping.
- Multi-epoch shuffled minibatch updates with linear schedules for learning
  rate, policy clip range, and entropy coefficient.
- Agent-ID-safe on-policy collection over `UnityEnvAdapter`, including natural
  termination, interruption bootstrap, terminal-only batches, reordered Agents,
  and terminal/new-decision overlap for the same Agent ID.
- Atomic resumable checkpoints containing model, optimizer, normalizer,
  scheduler progress, configuration, source/build identity, and environment
  step metadata.
- Isolated deterministic evaluation on a worker ID different from training.
- Versioned collection of raw observations and executed bounded actions for
  later ReinFlow behavior cloning.
- Short smoke training followed by seed 0 and seed 1 runs of 500,000 decision
  steps each, plus a comparison report against the existing official PPO run.

### Excluded

- Modifying `mlagents-learn`, Unity scenes, prefabs, packages, project settings,
  the existing 3DBall build, or official PPO artifacts.
- ONNX export; it remains optional until a later phase requires deployment.
- Recurrent policies, discrete or hybrid actions, visual encoders, action
  chunks, Flow models, or official FPO/ReinFlow/PolicyFlow code changes.
- Hyperparameter sweeps beyond the fixed two-seed correctness protocol.

## Verified Context

- The current 3DBall behavior is `3DBall?team=0` with one vector observation
  stream shaped `(batch, 8)` and a two-dimensional continuous action.
- Stage 2 unit baseline is `42 passed, 2 deselected`.
- The official PPO baseline uses 2 hidden layers of 128 units, batch size 64,
  buffer size 12,000, learning rate `3e-4`, policy clip `0.2`, entropy
  coefficient `1e-3`, `gamma=0.99`, `lambda=0.99`, and 3 epochs.
- CodeGraph confirms official ML-Agents normalizes advantages once per update,
  shuffles the complete buffer each epoch, uses clipped policy and value losses,
  and linearly schedules learning rate, epsilon, and beta.
- The official artifact records 500,499 steps, final training return about 100,
  and 374.98 seconds wall-clock time.

## Architecture

The implementation is divided into focused model, algorithm, collection,
orchestration, and artifact modules. Algorithm-specific PPO data never enters
`UnityEnvAdapter`; the adapter remains responsible only for LLAPI ordering,
event mapping, action validation, and lifecycle.

### Models

`flow_rl.models.gaussian` owns `TanhGaussianActor`. It flattens each normalized
observation stream, concatenates the flattened streams in tuple order, and
passes the result through a two-layer 128-unit SiLU MLP. A linear mean head and
a state-independent trainable `log_std` define a diagonal Normal distribution.
`log_std` is clamped to `[-20, 2]` when constructing the distribution.

Stochastic actions use reparameterized samples `z = mean + std * epsilon` and
`action = tanh(z)`. Deterministic actions use `tanh(mean)`. Log probability is
the summed Normal log probability minus the tanh Jacobian, computed with the
stable identity

```text
log(1 - tanh(z)^2) = 2 * (log(2) - z - softplus(-2z)).
```

Re-evaluating a stored bounded action clamps it to `[-1 + 1e-6, 1 - 1e-6]`
before inverse tanh. Entropy regularization uses the analytic entropy of the
unsquashed diagonal Normal, matching the official baseline's practical
continuous-action entropy convention while keeping the policy likelihood fully
tanh-corrected.

`flow_rl.models.critic` owns `ValueCritic`, using an independent two-layer
128-unit SiLU MLP and a scalar linear output. Actor and critic do not share
parameters, matching `shared_critic: false` in the official baseline.

`flow_rl.models.policy` owns `GaussianActorCritic` and immutable `PolicyOutput`.
`PolicyOutput` contains bounded `float32` actions, scalar values, summed log
probabilities, and pre-tanh samples. This is the common action-selection
boundary; future algorithms may define different auxiliary data without
changing the environment adapter.

### PPO Algorithm

`flow_rl.algorithms.ppo` owns:

- `PPOBatch`, a validated tensor batch of normalized observations, bounded
  actions, old log probabilities, old values, advantages, and returns.
- `PPOMetrics`, containing policy loss, value loss, entropy, total loss,
  approximate KL, clip fraction, actor gradient norm, critic gradient norm,
  learning rate, clip range, and entropy coefficient.
- Pure loss functions for clipped policy loss and clipped value loss.
- `PPOUpdater`, which normalizes advantages once across the complete rollout,
  shuffles indices from a seeded Torch generator for each epoch, iterates every
  complete or final partial minibatch, rejects non-finite tensors, clips actor
  and critic gradient norms, and applies one Adam optimizer containing both
  networks.

For ratio `r = exp(new_log_prob - old_log_prob)`, policy loss is the negative
mean of `min(r*A, clamp(r, 1-epsilon, 1+epsilon)*A)`. Value loss is one half of
the mean of the maximum between unclipped squared error and error from the old
value clipped to `old_value ± epsilon`. Total loss is

```text
policy_loss + value_coefficient * value_loss
              - entropy_coefficient * entropy.
```

The fixed phase-3 value coefficient is `0.5`; maximum gradient norm is `0.5`.
Learning rate decays linearly from `3e-4` to `1e-10`, policy clip from `0.2` to
`0.1`, and entropy coefficient from `1e-3` to `1e-5` over 500,000 decision
steps. Every schedule is derived from environment-step progress and restored
exactly on resume.

### Agent-ID-Safe Collection

`flow_rl.training.collector` owns pending policy decisions and converts LLAPI
events into transitions. Pending records are keyed by stable Agent ID and hold
the normalized observation, action, old log probability, and old value used for
the submitted action.

For every returned `EnvStep`, collection processes terminal rows before
decision rows:

1. A terminal row closes the old pending action for that Agent. Natural
   termination uses `next_value=0`; interruption evaluates the critic on the
   terminal observation and uses that value for bootstrap.
2. A nonterminal decision row closes an existing pending action with the row's
   reward and the critic value of the new observation.
3. A decision row without a pending action is a reset baseline. Its reward is
   not counted as a transition, matching official `AgentProcessor` behavior.
4. After old transitions are closed, the policy selects actions for the current
   DecisionSteps order and stores new pending records before calling
   `adapter.step()`.

Completed transitions stay grouped chronologically by Agent ID. At each update
boundary, GAE is calculated independently for every Agent trajectory using the
existing `compute_gae`. A rollout cut bootstraps from its stored `next_value`
but does not propagate the GAE trace into the next optimizer buffer. Pending
actions remain valid across optimizer updates, so no environment decision is
dropped or duplicated.

The collector asserts shape, dtype, finite values, action bounds, pending Agent
identity, and total sample counts. It records natural termination and
interruption separately.

### Observation Normalization

The existing `ObservationNormalizer` remains the sole owner of per-stream
running statistics. Training updates statistics using raw DecisionSteps
observations before action selection. The normalized observation actually used
by the actor is stored in the PPO transition, preventing later normalizer
updates from changing old-policy inputs.

Evaluation and dataset collection load the checkpoint normalizer and keep it
frozen. Raw observations are retained in the behavior-cloning dataset so Stage
6 can reproduce normalization from the accompanying checkpoint state.

### Training Orchestration

`flow_rl.training.trainer` owns a typed `PPOTrainingConfig` and the training
loop. The phase-3 production configuration is:

```yaml
total_environment_steps: 500000
rollout_size: 12000
batch_size: 64
epochs: 3
gamma: 0.99
gae_lambda: 0.99
learning_rate: 0.0003
final_learning_rate: 1.0e-10
clip_range: 0.2
final_clip_range: 0.1
entropy_coefficient: 0.001
final_entropy_coefficient: 1.0e-5
value_coefficient: 0.5
max_gradient_norm: 0.5
hidden_sizes: [128, 128]
checkpoint_interval: 100000
time_scale: 20
device: cuda
```

The CLI rejects nonempty run directories before launching Unity. It seeds
Python, NumPy, Torch CPU, and Torch CUDA. Each run writes configuration YAML,
episode CSV, TensorBoard events, update metrics CSV, Unity player logs,
periodic checkpoints, a final checkpoint, and a machine-readable summary.

The short smoke run uses at least 24,000 decisions so it performs at least one
full PPO update and then verifies parameter changes, finite metrics, atomic
checkpoint reload, and deterministic evaluation wiring.

Formal training is sequential to avoid GPU and port contention:

- seed 0: training worker 80, evaluation worker 90;
- seed 1: training worker 81, evaluation worker 91.

Every formal run uses 500,000 decision steps. Evaluation is deterministic and
collects exactly 100 completed episodes on its isolated worker. A seed passes
when mean return is at least 90 and all reported loss, ratio, gradient, KL,
runtime, and throughput values are finite.

### Checkpoint and Resume

The existing atomic checkpoint helper is reused. The model payload contains the
complete actor-critic state; optimizer payload contains Adam state. Normalizer
state is mandatory for PPO checkpoints. Configuration is the fully resolved
training configuration. Metadata contains at least:

- schema version;
- environment steps and Unity steps;
- optimizer update count;
- seed and worker ID;
- observation shapes and action size;
- source identity and Unity build SHA-256;
- wall-clock accumulation;
- Torch RNG, CUDA RNG, and NumPy generator states.

Resume validates architecture, observation/action shape, build hash, seed, and
schedule-defining configuration before restoring. A mismatched checkpoint is
rejected instead of partially loaded.

### Evaluation

`flow_rl.cli.evaluate_ppo` loads a checkpoint on the configured device, freezes
the normalizer, switches the model to evaluation mode, and calls the shared
evaluator with deterministic actions. Evaluation uses a worker ID unequal to
the training worker and writes per-episode CSV plus `evaluation.json` with mean,
standard deviation, minimum, maximum, episode count, decision steps,
wall-clock, throughput, and policy parameter count.

Evaluation never updates model, optimizer, normalizer, or checkpoint files.

### ReinFlow Dataset

`flow_rl.data.demonstrations` and `flow_rl.cli.collect_ppo_dataset` collect
100,000 decision samples from the fixed seed-0 final checkpoint. The policy is
stochastic with the checkpoint's fixed dataset seed, and the exact bounded
actions submitted to Unity are stored. The dataset is a compressed NPZ with:

- `agent_ids` as `int64`;
- one `observations_<index>` `float32` array per raw observation stream;
- `actions` as bounded `float32`;
- episode-boundary flags needed for auditing.

An adjacent JSON metadata file records checkpoint path and SHA-256, build path
and SHA-256, behavior name, training seed, dataset seed, sample count,
observation shapes, action size, normalizer count, collection wall-clock, and
source identity. Both files are written through temporary files and atomically
replaced. Existing dataset targets are rejected.

## Error Handling and Safety

- Non-finite observation, action, log probability, value, advantage, return,
  loss, ratio, gradient norm, or metric aborts the run with a precise error.
- Unity, logger, TensorBoard, and dataset handles close through context managers
  or `finally` blocks on every failure path.
- A run or dataset never overwrites a nonempty existing target.
- Training and evaluation worker IDs must differ and formal runs use distinct
  IDs.
- Ports and `3DBall` processes are checked after smoke, training, evaluation,
  and dataset collection.
- Existing Unity assets, baseline results, reference repositories, and the
  frozen `.conda/mlagents-r23` environment remain untouched.

## Test Strategy

All production behavior is implemented test-first with a witnessed failing
test before code.

### Model tests

- Literal shape and dtype checks for stochastic and deterministic outputs.
- Actions remain inside `[-1, 1]` and log probabilities remain finite for
  actions at or within `1e-6` of both boundaries.
- A hand-derived scalar Normal/Jacobian example verifies log probability.
- Identical initialization and sampling seeds reproduce actions and log probs.
- Actor and critic gradients are finite and parameters are independent.

### PPO tests

- Hand-derived positive and negative advantage cases verify the clipped
  surrogate chooses the correct branch.
- Hand-derived value targets verify clipped value loss.
- Constant advantages normalize to finite zeros.
- A seeded minibatch update changes parameters, reports finite metrics, respects
  the gradient limit, and produces a nonzero clip fraction for a forced ratio.
- Invalid shape, dtype, NaN, and configuration values are rejected.

### Collector tests

- Synthetic reordered Agent IDs preserve observation/action/log-prob/value
  ownership.
- Natural termination disables bootstrap.
- Interruption bootstraps from the terminal observation value and cuts the GAE
  trace.
- Same-batch terminal/new-decision and delayed reset baselines do not duplicate
  rewards or transitions.
- Rollout cuts retain pending actions and create exactly the expected number of
  optimizer samples.

### Orchestration and artifact tests

- A small fake environment performs collection, one update, logging, and clean
  shutdown without mocking model behavior.
- Checkpoint round-trip restores model output, optimizer state, normalizer,
  progress, and RNG state exactly; configuration mismatch is rejected.
- Dataset round-trip validates literal rows, dtype, bounds, metadata hashes,
  and overwrite rejection.
- Evaluation keeps the normalizer frozen, is deterministic, respects episode
  limits, isolates worker IDs, and closes on exception.

### Real validation

1. Run the full non-integration suite.
2. Run the existing Unity integration tests.
3. Run a 24,000-step PPO smoke training and checkpoint/evaluation check.
4. Train seeds 0 and 1 for 500,000 decisions each.
5. Deterministically evaluate 100 episodes for each final checkpoint.
6. Collect the 100,000-sample seed-0 dataset.
7. Parse all CSV/JSON/NPZ artifacts and verify counts, dtypes, finite values,
   action bounds, episode-index continuity, and checkpoint hashes.
8. Compare external PPO results with the immutable official baseline artifacts.
9. Scan Unity logs and verify no matching process or port remains.

## Planned Files

### New production files

- `flow_rl/src/flow_rl/models/__init__.py`
- `flow_rl/src/flow_rl/models/gaussian.py`
- `flow_rl/src/flow_rl/models/critic.py`
- `flow_rl/src/flow_rl/models/policy.py`
- `flow_rl/src/flow_rl/algorithms/__init__.py`
- `flow_rl/src/flow_rl/algorithms/ppo.py`
- `flow_rl/src/flow_rl/training/__init__.py`
- `flow_rl/src/flow_rl/training/collector.py`
- `flow_rl/src/flow_rl/training/config.py`
- `flow_rl/src/flow_rl/training/trainer.py`
- `flow_rl/src/flow_rl/data/demonstrations.py`
- `flow_rl/src/flow_rl/cli/train_ppo.py`
- `flow_rl/src/flow_rl/cli/evaluate_ppo.py`
- `flow_rl/src/flow_rl/cli/collect_ppo_dataset.py`

### New tests

- `flow_rl/tests/models/test_gaussian.py`
- `flow_rl/tests/models/test_critic.py`
- `flow_rl/tests/models/test_policy.py`
- `flow_rl/tests/algorithms/test_ppo.py`
- `flow_rl/tests/training/test_collector.py`
- `flow_rl/tests/training/test_config.py`
- `flow_rl/tests/training/test_trainer.py`
- `flow_rl/tests/data/test_demonstrations.py`
- `flow_rl/tests/integration/test_ppo_training_smoke.py`

### Configuration and evidence

- `flow_rl/configs/phase3_ppo_3dball.yaml`
- `reports/phase3/README.md`
- `reports/phase3/ppo-smoke/`
- `reports/phase3/ppo-seed-0/`
- `reports/phase3/ppo-seed-1/`
- `reports/phase3/evaluation-seed-0/`
- `reports/phase3/evaluation-seed-1/`
- `reports/phase3/reinflow-dataset-seed-0/`
- `reports/phase3/comparison.json`
- `docs/PROJECT_PLAN.md`

## Acceptance Criteria

- All existing and new non-integration tests pass.
- Real Unity smoke and PPO integration tests pass without leaked processes or
  ports.
- Seed 0 and seed 1 each complete at least 500,000 decision steps with finite
  optimization metrics and resumable final checkpoints.
- Each seed's isolated deterministic 100-episode mean return is at least 90.
- The seed-0 dataset contains exactly 100,000 valid bounded `(observation,
  action)` samples with complete provenance metadata.
- The comparison report records environment steps, wall-clock time, return,
  throughput, parameter count, and protocol differences for external and
  official PPO.
- `docs/PROJECT_PLAN.md` marks only evidence-backed boxes complete, records
  commands and artifact paths, and keeps Stage 4 unstarted until every Stage 3
  exit criterion passes.
