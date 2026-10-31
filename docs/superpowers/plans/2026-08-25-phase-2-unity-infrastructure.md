# Phase 2 Unity External Training Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the shared, testable Unity ML-Agents external-training infrastructure required by project phase 2 and validate it for 100,000 3DBall environment steps.

**Architecture:** A standalone `flow_rl` package drives Unity through the ML-Agents Low-Level API. The Unity adapter preserves sensor tuples and decision ordering, while rollout, GAE, normalization, tracking, and evaluation remain algorithm-neutral and testable without launching Unity.

**Tech Stack:** Python 3.10.12, NumPy 1.23.5, PyTorch 2.2.1+cu121, ML-Agents Env API 1.1.0, pytest, TensorBoard, CSV, YAML.

**Spec:** `docs/PROJECT_PLAN.md`

## Global Constraints

- Do not modify `ml-agents/`, its Unity assets/settings, the 3DBall build, PPO baseline artifacts, or the three algorithm reference repositories.
- Keep `.conda/mlagents-r23` frozen; create and use `.conda/flow-rl`.
- Use raw ML-Agents LLAPI, preserve every observation stream as a tuple, and submit actions in current `DecisionSteps.agent_id` order.
- Map natural terminal events to `terminated=True`; map `TerminalSteps.interrupted=True` to `truncated=True` and bootstrap them in GAE.
- Use test-first red/green cycles for production behavior.
- Do not create Git commits because the parent repository has no `HEAD` and the complete `game_project/` directory is currently untracked.

---

### Task 1: Package, locked environment, and diagnostics

**Files:**
- Create: `flow_rl/pyproject.toml`
- Create: `flow_rl/environment.yml`
- Create: `flow_rl/requirements.lock`
- Create: `flow_rl/src/flow_rl/__init__.py`
- Create: `flow_rl/src/flow_rl/cli/diagnose.py`
- Create: `flow_rl/tests/test_diagnostics.py`

**Interfaces:**
- Produces: `flow_rl.cli.diagnose.collect_diagnostics(build_path: Path) -> dict[str, object]`
- Produces: console script `flow-rl-diagnose`

- [x] Write a failing test that supplies the known 3DBall path and asserts the returned mapping contains exact keys for Python, NumPy, Torch, CUDA availability/version, ML-Agents, and resolved build existence.
- [x] Run `pytest flow_rl/tests/test_diagnostics.py -q` and confirm failure because `flow_rl.cli.diagnose` does not exist.
- [x] Add the `src` package skeleton, exact direct dependency pins, pytest configuration, and diagnostic implementation.
- [x] Run the diagnostic test and full unit suite.
- [x] Create `.conda/flow-rl` by cloning the frozen compatible baseline environment, install only the package/test additions into the clone, and record exact resolved packages in `requirements.lock` without mutating `.conda/mlagents-r23`.
- [x] Run `.conda/flow-rl/python.exe -m flow_rl.cli.diagnose --build Builds/3DBall/3DBall.exe` and save the output under `reports/phase2/diagnostics.json`.

### Task 2: EnvStep and UnityEnvAdapter

**Files:**
- Create: `flow_rl/src/flow_rl/envs/__init__.py`
- Create: `flow_rl/src/flow_rl/envs/types.py`
- Create: `flow_rl/src/flow_rl/envs/unity.py`
- Create: `flow_rl/tests/envs/fakes.py`
- Create: `flow_rl/tests/envs/test_types.py`
- Create: `flow_rl/tests/envs/test_unity_adapter.py`

**Interfaces:**
- Produces:

```python
@dataclass(frozen=True)
class EnvStep:
    agent_ids: np.ndarray
    observations: tuple[np.ndarray, ...]
    rewards: np.ndarray
    terminated: np.ndarray
    truncated: np.ndarray

class UnityEnvAdapter:
    def reset(self) -> EnvStep: ...
    def step(self, actions: np.ndarray) -> EnvStep: ...
    def close(self) -> None: ...
    @property
    def pending_agent_ids(self) -> np.ndarray: ...
    @property
    def continuous_action_size(self) -> int: ...
```

- [x] Write failing `EnvStep` tests for equal batch dimensions, integer Agent IDs, float32 observations/rewards, boolean flags, finite data, and mutual exclusion of `terminated`/`truncated`.
- [x] Run the focused tests and confirm expected missing-module failure.
- [x] Implement immutable validated `EnvStep`, copying public arrays so callers cannot mutate adapter state.
- [x] Run the focused tests to green.
- [x] Write failing adapter tests using a complete LLAPI-shaped fake for: exactly-one behavior auto-selection, explicit behavior selection, multiple-behavior rejection, tuple preservation, decision-first then terminal row order, natural/interrupted mapping, and pending Agent ID replacement across reorder/disappearance/reappearance.
- [x] Write failing action tests for wrong shape, wrong dtype, NaN/Inf, out-of-range values, and correct `ActionTuple` row ordering.
- [x] Write a failing lifecycle test proving context-manager cleanup occurs when policy/environment stepping raises and repeated adapter `close()` is harmless.
- [x] Implement the minimal raw-LLAPI adapter with an injectable environment factory. Never concatenate observation streams; combine decision/terminal rows sensor-by-sensor only for the returned event batch.
- [x] Run adapter tests, then all unit tests.

### Task 3: Agent-keyed rollout, GAE, and observation normalization

**Files:**
- Create: `flow_rl/src/flow_rl/data/__init__.py`
- Create: `flow_rl/src/flow_rl/data/rollout.py`
- Create: `flow_rl/src/flow_rl/data/gae.py`
- Create: `flow_rl/src/flow_rl/data/normalization.py`
- Create: `flow_rl/tests/data/test_rollout.py`
- Create: `flow_rl/tests/data/test_gae.py`
- Create: `flow_rl/tests/data/test_normalization.py`

**Interfaces:**
- Produces: `AgentRolloutBuffer.append(transition: Transition) -> None`
- Produces: `AgentRolloutBuffer.trajectories() -> Mapping[int, tuple[Transition, ...]]`
- Produces: `compute_gae(rewards, values, next_values, terminated, truncated, gamma, gae_lambda) -> tuple[np.ndarray, np.ndarray]`
- Produces: `ObservationNormalizer.update/normalize/state_dict/load_state_dict`

- [x] Write failing rollout tests whose interleaved input order is `[20, 10]`, `[10]`, `[20]`, and verify each Agent trajectory remains chronological without batch-index assumptions.
- [x] Implement the smallest Agent-ID-keyed buffer with copied arrays and finite/shape validation; run focused tests to green.
- [x] Write hand-calculated failing GAE tests showing natural termination blocks bootstrap, truncation uses `next_value` but cuts the recursive trace, and ordinary steps bootstrap and recurse.
- [x] Implement reverse-scan GAE using `~terminated` for TD bootstrap and `~(terminated | truncated)` for trace continuation; run focused tests to green.
- [x] Write failing normalizer tests for two independent observation streams, zero-variance stability, finite outputs, and exact state round-trip.
- [x] Implement numerically stable parallel running mean/variance and tuple-stream normalization; run focused and full unit tests.

### Task 4: Episode accounting, checkpointing, TensorBoard/CSV, and evaluation

**Files:**
- Create: `flow_rl/src/flow_rl/tracking/__init__.py`
- Create: `flow_rl/src/flow_rl/tracking/episodes.py`
- Create: `flow_rl/src/flow_rl/tracking/run.py`
- Create: `flow_rl/src/flow_rl/tracking/checkpoint.py`
- Create: `flow_rl/src/flow_rl/evaluation/__init__.py`
- Create: `flow_rl/src/flow_rl/evaluation/runner.py`
- Create: `flow_rl/tests/tracking/test_episodes.py`
- Create: `flow_rl/tests/tracking/test_run.py`
- Create: `flow_rl/tests/tracking/test_checkpoint.py`
- Create: `flow_rl/tests/evaluation/test_runner.py`

**Interfaces:**
- Produces: `EpisodeTracker.record(step: EnvStep) -> tuple[EpisodeSummary, ...]`
- Produces: `RunLogger.log_episode(summary, environment_steps)`, configuration snapshot, CSV, and TensorBoard events.
- Produces: `save_checkpoint/load_checkpoint` with model/optimizer/normalizer/config metadata.
- Produces: `evaluate_policy(..., training_worker_id: int, evaluation_worker_id: int)` with unequal-worker validation.

- [x] Write failing episode tests with reordered, disappearing, terminal, and interrupted agents; assert emitted return and length literals reconstruct each episode exactly.
- [x] Implement episode accounting keyed by Agent ID and run focused tests.
- [x] Write failing run-logger tests that parse the generated CSV, reload YAML config, and confirm TensorBoard event creation after close.
- [x] Implement run-directory creation, configuration snapshot, CSV append/flush, and TensorBoard scalar logging; run focused tests.
- [x] Write a failing atomic checkpoint round-trip test including normalizer statistics and arbitrary algorithm metadata.
- [x] Implement temporary-file plus `os.replace` checkpoint writes using `torch.save/load`; run focused tests.
- [x] Write failing evaluator tests for equal worker-ID rejection, deterministic action ordering, episode limit, and cleanup on exception.
- [x] Implement the algorithm-neutral evaluator and run all unit tests.

### Task 5: Unity smoke runner and 100k-step phase validation

**Files:**
- Create: `flow_rl/src/flow_rl/cli/random_policy.py`
- Create: `flow_rl/tests/integration/test_unity_smoke.py`
- Create: `flow_rl/configs/phase2_random_3dball.yaml`
- Create: `reports/phase2/README.md`
- Modify: `docs/PROJECT_PLAN.md`

**Interfaces:**
- Produces: console script `flow-rl-random-policy`
- Produces: machine-readable `reports/phase2/random-3dball-100k/summary.json`, `episodes.csv`, TensorBoard events, config snapshot, and Unity player log.

- [x] Write an opt-in integration test that imports `mlagents_envs`, launches `Builds/3DBall/3DBall.exe` on a dedicated worker ID, resets, submits one correctly shaped random action batch, steps, and always closes.
- [x] Run the integration test with its environment variable enabled and confirm it fails before the runner/fixtures are complete.
- [x] Implement the seeded random-policy CLI using `with UnityEnvAdapter(...)`, finite checks every event batch, Agent-ID episode accounting, and `try/finally` logger cleanup.
- [x] Run the smoke test to green and verify no matching Unity process remains after exit.
- [x] Run the complete unit suite with integration excluded, then the integration smoke test.
- [x] Run the random policy for at least 100,000 Agent decision steps on an isolated worker ID; verify exit code, summary step count, finite statistics, episode reconstruction, output files, and absence of leaked Unity processes/ports.
- [x] Re-read every phase 2 checkbox and exit criterion. Update `docs/PROJECT_PLAN.md` status, checkboxes, exact commands, test count, environment-step count, outputs, blockers, and any new decisions. Do not mark criteria that lack evidence.

### Post-review hardening

- [x] Match official `AgentProcessor` semantics by excluding the first Decision baseline after every terminal, including same-batch terminal/new-decision overlap and delayed reappearance.
- [x] Reject nonempty run directories before writing any config, CSV, TensorBoard, Unity log, or summary artifact.
- [x] Reject non-`float32` LLAPI observations/rewards instead of silently casting them.
- [x] Record maximum Agent absence and drain after the target budget until every initial Agent ID is observed again; validate the 3DBall threshold with a real 100k run.
- [x] Re-run unit, machine diagnostic, Unity smoke, dependency, durability, CSV/JSON, Unity-log, process, and port verification after review fixes.
