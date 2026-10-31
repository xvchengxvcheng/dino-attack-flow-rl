# Dino Attack Multi-Map Structured Observation v2 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade the completed Dino Attack v1 structured pipeline to a six-stream, 270-scalar v2 protocol whose target and five region geometries come from the active Unity scene, allowing one model to support two same-size maps.

**Architecture:** Unity remains the source of live gameplay state and now emits normalized `RoundManager.DinoTarget` plus ordered Z1–Z5 vertices. Python validates the v2 stream contract and feeds dynamic Regions into both encoders; the Set Transformer removes its map-specific geometry buffer. The absent second map is not fabricated: this plan validates v2 on `Dinos.unity` and leaves two-scene 50/50 integration behind a separate map-availability gate.

**Tech Stack:** Unity 6000.3.22f1, ML-Agents 4.0.0, C#, Python 3.10, PyTorch 2.2.1, pytest.

**Spec:** `docs/superpowers/specs/2026-08-30-dino-attack-multimap-structured-observation-v2-design.md`

## Global Constraints

- Protocol is exactly `dino_attack_structured_set_v2`; stream order is Global, Regions, Walls, Guards, Houses, Dinos with shapes `(5,)`, `(5,8)`, `(6,5)`, `(11,7)`, `(8,6)`, `(10,7)`.
- `MaxGuards=11`: at most eight ground guards plus three wall archers. Guard fields and zero-padding semantics remain unchanged.
- Both maps use shared bounds X `[-55.5,48.5]`, Z `[-53,29]`; map dimensions, map ID, terrain speed and defense multipliers are not observations.
- `RoundManager.DinoTarget` is the only final destination. Do not create `FinalDestination` or duplicate target state.
- Regions always contain exactly five valid A/B/C/D rows ordered by `DeploymentZoneId`; there is no region mask.
- Keep the 4D action and all v1 entity fields, masks, limits and terminal semantics unchanged.
- Preserve user dirty work. Do not stage, commit, stash, clean, reset or hand-edit Unity scene YAML.
- Do not run a full Unity suite, performance matrix, formal long training or nonexistent second-map validation.
- Do not access or save Unity until `TASK-8.11-ART-PASS` explicitly returns a stopped/clean Editor.

---

### Task 1: Freeze the Unity v2 Core Contract

**Files:**
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Core/DinoTrainingObservationLayout.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Core/DinoStructuredObservationFrame.cs`
- Modify: `dino-attack/Assets/Tests/Editor/DinoStructuredObservationFrameTests.cs`

**Interfaces:**
- Produces: `ProtocolVersion=v2`, `GlobalFields=5`, `RegionCount=5`, `RegionFields=8`, `MaxGuards=11`, `TotalScalarCount=270`, and `DinoStructuredObservationFrame.Regions`.
- Preserves: action codec, Wall/House/Dino maxima and all entity field counts; only the Guard row maximum changes.

- [x] **Step 1: Write the v2 layout RED test**

Add assertions equivalent to:

```csharp
Assert.That(DinoTrainingObservationLayout.ProtocolVersion,
    Is.EqualTo("dino_attack_structured_set_v2"));
Assert.That(DinoTrainingObservationLayout.GlobalShape, Is.EqualTo(new[] { 5 }));
Assert.That(DinoTrainingObservationLayout.RegionShape, Is.EqualTo(new[] { 5, 8 }));
Assert.That(DinoTrainingObservationLayout.GuardShape, Is.EqualTo(new[] { 11, 7 }));
Assert.That(DinoTrainingObservationLayout.TotalScalarCount, Is.EqualTo(270));
Assert.That(new DinoStructuredObservationFrame().Streams.Length, Is.EqualTo(6));
```

- [x] **Step 2: Run only the core RED fixture**

Run `DinoStructuredObservationFrameTests` in EditMode. Expected: failure on v1 version/missing Region shape.

- [x] **Step 3: Implement the minimal core layout**

Set `GlobalFields=5`, add `RegionCount=5`, `RegionFields=8`, `RegionShape`, include `RegionCount * RegionFields` in `TotalScalarCount`, and add Regions between Global and Walls in `Streams` and `Validate()`.

- [x] **Step 4: Re-run the focused fixture**

Expected: all `DinoStructuredObservationFrameTests` pass and compile reports no new errors.

### Task 2: Emit DinoTarget and Dynamic Region Geometry from Unity

**Files:**
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoStructuredObservationBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoStructuredObservationSensor.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoStructuredObservationSensorComponent.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Editor/DinoFormalTrainingSceneInstaller.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`

**Interfaces:**
- Consumes: `RoundManager.DinoTarget`, five `DinoDeploymentZone.WorldVertices`, shared map bounds and Task 1 frame/layout.
- Produces: cached six-stream frames and named sensors `00_Global`, `01_Regions`, `02_Walls`, `03_Guards`, `04_Houses`, `05_Dinos`.

- [x] **Step 1: Add one focused scene observation RED test**

In the existing PlayMode fixture, resolve the formal observation builder, RoundManager and five zones. Assert Global indices 3/4 equal the builder's shared-bound normalization of `RoundManager.DinoTarget.position`; assert each Regions row equals normalized `WorldVertices` A/B/C/D in Z1–Z5 order; assert six sensor specs and exact shapes.

- [x] **Step 2: Add fail-fast cases without expanding the suite**

In the same fixture, temporarily supply a null `DinoTarget`, duplicate zone ID and missing zone reference one case at a time, and assert `InvalidOperationException` contains the failing contract name. Restore references in `finally` blocks.

- [x] **Step 3: Run the single RED fixture**

Expected: failure because Global has no target fields, frame has no Regions and only five sensors exist.

- [x] **Step 4: Implement scene-derived values**

Add a serialized five-zone array to the builder. `ValidateSources()` requires non-null `DinoTarget`, exactly five zones, unique complete IDs and valid geometry. `WriteGlobal()` writes normalized target x/z at indices 3/4. `WriteRegions()` sorts by ID, reads four `WorldVertices`, normalizes x/z with the same helper used by entities, and fills 40 finite values.

- [x] **Step 5: Register the sixth sensor and wire the installer**

Extend the stream enum/switch and sensor component with the approved names/order. Update `DinoFormalTrainingSceneInstaller` to assign the same ordered zone array to `DinoTrainingAgent` and `DinoStructuredObservationBuilder`; preserve installer idempotence.

- [x] **Step 6: Run the focused GREEN checks**

Run the core EditMode fixture and the one PlayMode observation fixture. Expected: v2 values/shapes pass, failure cases are explicit, Human/training gameplay roots are unchanged. Stop Play Mode and leave the active scene clean; do not run unrelated tests.

### Task 3: Replace the Python v1 Geometry Contract with v2 Streams

**Files:**
- Create: `flow_rl/configs/dino_attack_structured_set_v2.yaml`
- Modify: `flow_rl/src/flow_rl/envs/dino_protocol.py`
- Modify: `flow_rl/tests/envs/test_dino_protocol.py`
- Modify: `flow_rl/tests/envs/test_unity_adapter.py`

**Interfaces:**
- Produces: `DinoProtocol` metadata for six streams and action4 without `region_vertices` or `region_vertices_normalized`.
- Preserves: shared bounds, maxima, field order, action bins and manifest hashing.

- [x] **Step 1: Write v2 parser and LLAPI RED tests**

Set expected shapes to:

```python
EXPECTED_SHAPES = (
    (5,), (5, 8), (6, 5), (11, 7), (8, 6), (10, 7)
)
```

Assert protocol version v2, no map-specific vertex metadata, correct stream order and rejection of v1, reordered or wrong-shaped LLAPI specs.

- [x] **Step 2: Run the two focused Python files**

Run:

```powershell
.conda/flow-rl/python.exe -m pytest flow_rl/tests/envs/test_dino_protocol.py flow_rl/tests/envs/test_unity_adapter.py -q
```

Expected: RED on the existing v1 parser and five shapes.

- [x] **Step 3: Add the v2 manifest and parser**

The YAML contains six stream definitions, shared bounds, existing scales/maxima and the unchanged action block. Remove the `regions.zones` world-vertex section. Update `DinoProtocol` to validate exact v2 keys/shapes and omit fixed region vertices from dataclass state and checkpoint metadata.

- [x] **Step 4: Re-run the two focused files**

Expected: both pass; the adapter retains all six rank-aware arrays and rejects v1/incorrect order.

### Task 4: Feed Dynamic Regions into Deep Sets and Set Transformer

**Files:**
- Modify: `flow_rl/src/flow_rl/models/state_encoder.py`
- Modify: `flow_rl/src/flow_rl/models/dino_deep_sets.py`
- Modify: `flow_rl/src/flow_rl/models/dino_set_transformer.py`
- Modify: `flow_rl/tests/models/dino_fixtures.py`
- Modify: `flow_rl/tests/models/test_dino_deep_sets.py`
- Modify: `flow_rl/tests/models/test_dino_set_transformer.py`
- Modify: `flow_rl/tests/models/test_dino_actor_critic.py`

**Interfaces:**
- Consumes: six tensors in v2 order.
- Produces: 256-dimensional encodings with dynamic region tokens; no fixed geometry buffer.

- [x] **Step 1: Upgrade synthetic fixtures and write RED assertions**

Create batches with Global `(B,5)` and Regions `(B,5,8)`. Keep entity mask extraction limited to Walls/Guards/Houses/Dinos rather than treating Regions column 0 as a mask. Assert changing only Regions changes both encoder outputs, while permuting valid entity rows preserves outputs.

- [x] **Step 2: Require removal of the fixed buffer**

Assert `"region_vertices" not in dict(encoder.named_buffers())`; the same Set Transformer instance must accept two batches with different Regions and return finite `(B,5,64)` region states plus finite `(B,256)` output.

- [x] **Step 3: Run the three focused model files**

Run:

```powershell
.conda/flow-rl/python.exe -m pytest flow_rl/tests/models/test_dino_deep_sets.py flow_rl/tests/models/test_dino_set_transformer.py flow_rl/tests/models/test_dino_actor_critic.py -q
```

Expected: RED on five-stream unpacking, Global input 3 and fixed region buffer behavior.

- [x] **Step 4: Implement explicit v2 stream unpacking**

Extend `extract_entity_masks` with an explicit `stream_indices: Sequence[int] | None = None` parameter; Dino v2 encoders pass `(2, 3, 4, 5)`, while the default preserves the existing `observations[1:]` behavior. Deep Sets encodes Regions through a shared `8 -> 32 -> 64` MLP plus five learned identity embeddings, concatenates the ordered 320-dimensional region state with four 128-dimensional entity states and Global64, and changes fusion input to 896. Set Transformer reads Regions from the batch, preserves learned identity embeddings and existing Cross-Attention, changes Global MLP input to 5, and removes protocol geometry buffer construction.

- [x] **Step 5: Re-run focused model and actor/critic tests**

Expected: all selected tests pass; outputs/gradients are finite and PPO/FPO actor/critic encoder ownership remains independent.

### Task 5: Reinstall v2 and Resume the Single-Map Task 10 Gate

**Files:**
- Modify: `flow_rl/configs/phase8_dino_ppo_smoke.yaml`
- Modify: `flow_rl/configs/phase8_dino_fpo_smoke.yaml`
- Create: `flow_rl/scripts/dino_v2_single_update_smoke.py`
- Create: `flow_rl/tests/integration/test_dino_v2_single_update_smoke_script.py`
- Create: `reports/phase8/structured-parallel-training/v2-validation.md`
- Modify: `docs/PROJECT_PLAN.md`
- Modify: `docs/superpowers/plans/2026-08-30-dino-attack-structured-parallel-training.md`
- Modify: this plan file

**Interfaces:**
- Consumes: stopped/clean Editor returned by `TASK-8.11-ART-PASS`, completed Tasks 1–4 and the final current-map scene.
- Produces: one v2 formal Player and focused current-map integration evidence. It does not claim dual-map training.

- [x] **Step 1: Record the returned scene baseline**

Confirm active `Dinos.unity`, stopped Play Mode, no compile/domain reload, dirty=false, Console error count and SHA-256. If any state is unknown or dirty, stop without saving and return the Editor owner conflict.

- [x] **Step 2: Run the installer twice and save once**

Install six-sensor v2 wiring through the connected Editor. Require first run to report the expected change and second run to be a no-op. Save only after validation confirms the same five zones and existing `RoundManager.DinoTarget` are wired.

- [x] **Step 3: Build the formal Player once**

Update both smoke configs to v2, build the current `Dinos.unity` Player once, and record scene/build/protocol hashes. Do not add a placeholder second scene to Build Settings.

- [x] **Step 4: Run only the retained Task 10 integration**

Run the existing 20-reload sequence once and one four-environment approximately 4096-decision PPO rollout plus one finite FPO update smoke. Add v2 assertions: six exact streams, dynamic target/Regions finite, no v1 metadata, natural terminal/truncated semantics unchanged and clean process/port shutdown.

- [x] **Step 5: Freeze evidence without overstating scope**

Record exact commands, counts, hashes and failures in `v2-validation.md`. Mark v2 current-map integration complete only if all retained checks pass. State explicitly: `second_map_available=false`, `dual_map_sampling_validated=false`; keep 50/50 dual-map validation pending until the second scene exists.

**2026-08-30 execution complete for the retained current-map gate:** Steps 1–5 completed. Installer results were `changed=True` then `changed=False`; the validated scene SHA-256 is `ECFD823C14F5EC9BAE061D56B50D8A809EED3AACBABC12D33A6F7ACE4EC63812`. The first reload invocation exposed a transient Test Runner teardown/race and the first real LLAPI diagnostics exposed a separate lifecycle race: `RoundManager.Start` overwrote an already-running training session with `Setup`. A focused two-frame persistence assertion reproduced that defect RED, the guarded initialization fix passed GREEN, and clean outer build `build_d56f4ab06098` produced `Assembly-CSharp.dll` SHA-256 `01BB59BED8577D3A2176D27EBA02A93748FC6F9D1C0F4B2246D6A6F16BF35880`. The retained 20-reload test passed `1/1`; a fresh explicit-Wait Player returned its first step in `0.047856000 s`; the four-environment PPO gate collected/drained `4,099` decisions with `93` natural terminals and one update; the finite FPO gate collected `4,098` decisions with `101` natural terminals and one update. All workers contributed and final process/port cleanup passed. Exact evidence is in `reports/phase8/structured-parallel-training/v2-validation.md`; `second_map_available=false` and `dual_map_sampling_validated=false` remain explicit.

**2026-08-30 independent-review fix:** The real single-update harness is now persisted as the parameterized `flow_rl/scripts/dino_v2_single_update_smoke.py`, with JSON result/metrics/exit artifacts and focused CLI/zero-Decision tests. After preserving an initial zero-Decision failure, the final script tests passed `2/2`; fresh script-driven PPO and FPO runs each collected/drained `4,099` transitions from all four workers and completed exactly one finite update (`96` and `92` natural terminals respectively, `0` truncated). The minimum existing adapter/collector interrupted mapping gate passed `2/2`. This does not complete the broader real injected-Player interruption matrix: original Task 10 Step 2 remains pending.

## Execution Order and Review Gates

1. Tasks 1 and 3 may proceed independently without Unity scene access; Task 4 depends on Task 3's v2 Python contract.
2. Task 2 production-code work may be prepared without opening Unity, but its PlayMode/installer/save steps wait for explicit Editor return from `TASK-8.11-ART-PASS`.
3. Task 5 is the only Player build, 20-reload and four-process integration run.
4. Reward implementation remains a separate approved-spec gate and must be incorporated before final formal training, not silently mixed into v2 protocol edits.
5. No task performs an automatic Git commit; the user reviews accumulated changes at checkpoints they choose.
