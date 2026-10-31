# Dino Attack Quadrilateral Terrain Zones Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the five rectangular Dino Attack deployment/terrain volumes with five approved convex fan-shaped quadrilaterals that share one geometry contract for placement, terrain speed, visuals, validation, and future AI-relative coordinates.

**Architecture:** Add a pure `DinoAttack.Map.Core` quadrilateral math type and keep `DinoDeploymentZone` as the Transform-aware scene adapter. A single deterministic mesh factory will derive the ground mesh, outline mesh, and convex trigger prism from the same four vertices; the scene builder remains the only writer of `Dinos.unity` and its canonical preflight compares both values and mesh signatures before returning. The neutral battlefield remains a physical/visual floor without a terrain volume or deployment membership.

**Tech Stack:** Unity 6000.3.22f1, C# 9, NUnit EditMode/PlayMode tests, Unity AI Navigation/NavMesh, project Unity MCP connected Editor, URP/Lit materials.

**Spec:** `docs/superpowers/specs/2026-08-29-dino-attack-quadrilateral-terrain-zones-design.md`

## Global Constraints

- Preserve `Dinos.unity`, the current Human/AI controller split, the 6-dimensional action, 504-dimensional observation, rewards, and episode protocol.
- Preserve zone IDs, display names, dominant surfaces, and multipliers: Z1 StoneRoad 1.10, Z2 Grass 1.00, Z3 ShallowWater 0.70, Z4 Mud 0.85, Z5 Slope 0.80.
- Every zone is a strict convex `A -> B -> C -> D` quadrilateral with a village-side `AB`, outer-side `DC`, finite coordinates, no self-intersection, and no pairwise overlap.
- The five zones do not cover the village or the whole battlefield; traversable neutral gaps remain between them and outside them.
- The relative-coordinate API accepts finite `[0,1]` values only and never silently clamps.
- Terrain triggers affect movement speed only; neither triggers nor visual meshes block movement, navigation, or attacks.
- Do not modify ProjectSettings, Packages, historical training scenes, algorithm repositories, checkpoints, or the frozen `.conda/mlagents-r23` environment.
- Do not hand-edit Unity scene YAML. Run the scene builder through the connected Editor and preserve all unrelated dirty work.
- Automatic Git staging and commits are forbidden by the project rules; each task ends with a scoped diff/review checkpoint instead.

## File Structure

- Create `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/QuadrilateralXZ.cs` — immutable pure XZ geometry, validation, containment, bilinear mapping, area, and overlap helpers.
- Create `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/QuadrilateralMeshFactory.cs` — deterministic ground, outline, and convex-prism mesh data from four vertices.
- Modify `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/DinoDeploymentZone.cs` — serialized local vertices and Transform-aware public zone API; the collider is no longer placement truth.
- Modify `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs` — approved five-zone vertex table, neutral floor, shared mesh construction, component wiring, and canonical mesh signatures.
- Modify `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs` — exact-five geometry, separation, neutral-floor, mesh identity, NavMesh, and non-blocking checks.
- Modify `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldMapCoreTests.cs` — pure quadrilateral and mesh-contract tests.
- Modify `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs` — saved-scene vertices, mesh/trigger identity, neutral ground, repair, and idempotence tests.
- Modify `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs` — placement gaps, relative samples, terrain speed restoration, navigation, UI lifecycle, and attack regression tests.
- Modify `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity` — only through the deterministic builder.
- Create `reports/phase8/quadrilateral-terrain-zones/` evidence — vertices, areas, gaps, screenshots, tests, longevity, Player smoke, and replacement freeze manifest.
- Modify `docs/PROJECT_PLAN.md` and this plan — active-state transitions, checkboxes, commands, outcomes, hashes, and limitations.

---

### Task 1: Freeze the revision boundary and establish the baseline

**Files:**
- Modify: `docs/PROJECT_PLAN.md`
- Create: `reports/phase8/quadrilateral-terrain-zones/baseline.md`

**Interfaces:**
- Consumes: approved design spec and prior TASK-8.10 manifest.
- Produces: active task `TASK-8.10-Q`, preserved historical hash, dirty-worktree inventory, scene/Unity baseline, and explicit pause of TASK-8.11.

- [x] **Step 1: Record the scope decision before code changes**

Add `TASK-8.10-Q` after 8.10 with status `进行中`, set 8.11 to `未开始（等待 8.10-Q 重新冻结）`, and append a decision stating that the old `DF751...73E5` freeze is historical rather than current.

- [x] **Step 2: Capture the read-only baseline**

Run:

```powershell
git status --short
git -C dino-attack status --short
Get-FileHash dino-attack\Assets\LlamAcademy\Dinos\Scenes\Dinos.unity -Algorithm SHA256
```

Record the exact outputs, Unity version, active scene, Play/Edit state, Console cursor, and any pre-existing errors in `baseline.md`.

- [x] **Step 3: Verify documentation integrity**

Run:

```powershell
git diff --check -- docs\PROJECT_PLAN.md docs\superpowers\plans\2026-08-29-dino-attack-quadrilateral-terrain-zones.md
$forbidden = @(('T'+'BD'), ('T'+'ODO'), ('implement '+'later'), ('fill in '+'details')); Select-String -Path docs\superpowers\plans\2026-08-29-dino-attack-quadrilateral-terrain-zones.md -Pattern $forbidden
```

Expected: `diff --check` has no output and the placeholder scan has no matches.

- [x] **Step 4: Review checkpoint**

Confirm the scoped diff changes only plan/status text and the evidence folder. Do not stage or commit.

### Task 2: Implement strict quadrilateral math with TDD

**Files:**
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/QuadrilateralXZ.cs`
- Modify: `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldMapCoreTests.cs`

**Interfaces:**
- Consumes: four local `Vector2` vertices in `A, B, C, D` order.
- Produces: `QuadrilateralXZ.TryCreate(Vector2 a, Vector2 b, Vector2 c, Vector2 d, out QuadrilateralXZ value)`, `bool Contains(Vector2 point)`, `bool TryEvaluate(Vector2 uv01, out Vector2 point)`, `float SignedArea`, `IReadOnlyList<Vector2> Vertices`, and `bool Overlaps(QuadrilateralXZ other)`.

- [x] **Step 1: Write failing validation and mapping tests**

Add NUnit cases covering corners `(0,0)=A`, `(1,0)=B`, `(1,1)=C`, `(0,1)=D`, edge midpoints, center, an asymmetric convex quad, NaN/infinity, out-of-range UV, duplicate vertices, zero area, concavity, and bow-tie self-intersection.

```csharp
Assert.That(quad.TryEvaluate(new Vector2(0.25f, 0.75f), out Vector2 point), Is.True);
Vector2 inner = Vector2.Lerp(a, b, 0.25f);
Vector2 outer = Vector2.Lerp(d, c, 0.25f);
Assert.That(point, Is.EqualTo(Vector2.Lerp(inner, outer, 0.75f)).Using(Vector2ComparerWithEqualsOperator.Instance));
```

- [x] **Step 2: Run the focused tests and verify RED**

Run connected-Editor EditMode filter `OpenTropicalBattlefieldMapCoreTests`.

Expected: compilation/test failure because `QuadrilateralXZ` does not exist.

- [x] **Step 3: Add the minimal immutable implementation**

Use cross-product signs for strict convexity, segment intersection for invalid polygons, epsilon-inclusive half-plane containment for borders, and the exact approved bilinear formula:

```csharp
Vector2 inner = Vector2.Lerp(A, B, uv01.x);
Vector2 outer = Vector2.Lerp(D, C, uv01.x);
point = Vector2.Lerp(inner, outer, uv01.y);
```

Reject every non-finite coordinate and use explicit constants for minimum area/edge length and numerical epsilon.

- [x] **Step 4: Add and pass containment/overlap tests**

Test interior, every edge midpoint, every vertex, exterior points, separated quads, edge-touching quads, and true area overlap. Treat touching as non-overlap only when the canonical scene keeps the configured minimum neutral gap.

- [x] **Step 5: Run focused and full core tests**

Expected: all `OpenTropicalBattlefieldMapCoreTests` pass with no new Console error.

- [x] **Step 6: Review checkpoint**

Inspect only the new math file, its `.meta`, and the focused test diff; confirm no UnityEngine object or scene dependency entered the pure API.

### Task 3: Move deployment legality to the four-vertex contract

**Files:**
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/DinoDeploymentZone.cs`
- Modify: `dino-attack/Assets/Tests/Editor/DinoDeploymentRulesTests.cs`
- Modify: `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldMapCoreTests.cs`

**Interfaces:**
- Consumes: `QuadrilateralXZ` and the zone Transform.
- Produces: `bool IsGeometryValid`, `bool Contains(Vector3 worldPoint)`, `bool TryResolveRelative(Vector2 uv01, out Vector3 worldPoint)`, `IReadOnlyList<Vector3> WorldVertices`, and an Editor/builder-only configuration method for the four local XZ vertices.

- [x] **Step 1: Write failing zone adapter tests**

Create a temporary GameObject, configure an asymmetric quad, and assert valid geometry, strict UV failure, inside/outside containment, exact world corners, translation, and yaw rotation. Add a regression proving a point inside the old BoxCollider AABB but outside the quad is rejected.

- [x] **Step 2: Run focused EditMode tests and verify RED**

Expected: failure because the new properties/configuration API are absent.

- [x] **Step 3: Implement the Transform-aware adapter**

Store four serialized local XZ vertices, build the pure value on validation, map local XZ through `TransformPoint`, and evaluate containment by `InverseTransformPoint`. Keep a `MeshCollider` reference only for terrain-trigger presentation; never read its bounds for deployment legality.

- [x] **Step 4: Keep service behavior shared and fail closed**

Verify `DinoDeploymentService.TryGetZone` needs no semantic fork: it continues calling `candidate.Contains(position)`. Invalid zone geometry returns false and makes configuration validation fail.

- [x] **Step 5: Run deployment and core suites**

Expected: focused suites pass, including exact-five/unique-ID behavior and the new AABB regression.

- [x] **Step 6: Review checkpoint**

Confirm Human placement and future AI relative placement both terminate at the same zone API and no action/observation code changed.

### Task 4: Generate all zone meshes from one deterministic source

**Files:**
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/QuadrilateralMeshFactory.cs`
- Modify: `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldMapCoreTests.cs`

**Interfaces:**
- Consumes: valid `QuadrilateralXZ`, `float groundY`, `float triggerBottom`, `float triggerTop`.
- Produces: deterministic `CreateGroundMesh`, `CreateOutlineMesh`, `CreateTriggerPrism`, and a stable geometry signature over ordered vertices/indices.

- [x] **Step 1: Write failing mesh-contract tests**

Assert ground vertices use A/B/C/D once with two consistently wound triangles; outline edges are AB, BC, CD, DA; prism has eight vertices, closed faces, nonzero volume, and a valid convex `MeshCollider` assignment. Assert repeated generation yields the same signature.

- [x] **Step 2: Run focused tests and verify RED**

Expected: `QuadrilateralMeshFactory` missing.

- [x] **Step 3: Implement minimal deterministic mesh creation**

Use fixed vertex and triangle ordering, recalculate bounds/normals once, assign stable names, and avoid reading global state or random values. The prism collider mesh must be convex-compatible and the outline must not create colliders.

- [x] **Step 4: Run focused tests and inspect allocations/assets**

Expected: all mesh tests pass; transient test meshes are destroyed in teardown; no asset files are created by pure tests.

- [x] **Step 5: Review checkpoint**

Confirm every derivative is generated from the same ordered source vertices and the signature detects a one-vertex drift.

### Task 5: Rebuild the five-zone scene and canonical preflight

**Files:**
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- Modify: `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`
- Modify through builder: `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`

**Interfaces:**
- Consumes: Task 2–4 APIs and the approved west-to-east Z1–Z5 topology.
- Produces: five canonical vertex arrays, one neutral battlefield floor, five visual meshes, five outline meshes, five convex trigger prisms with `TerrainSurfaceVolume`, and an early-return canonical preflight.

- [x] **Step 1: Write failing saved-scene contract tests**

Assert exact-five zones, approved IDs/surfaces, strict convexity, no pairwise overlap, no village sample containment, at least one neutral sample between each adjacent pair, no full-map terrain volume, and no BoxCollider-based deployment truth.

- [x] **Step 2: Write failing builder repair/idempotence tests**

Perturb one serialized vertex and one trigger-mesh vertex, run the builder, and assert both repair to canonical values. On an already canonical scene, assert the builder returns before mesh replacement, NavMesh rebake, dirtying, or saving.

- [x] **Step 3: Run scene tests and verify RED**

Expected: old rectangular/full-Grass scene violates geometry and neutral-floor assertions.

- [x] **Step 4: Define and validate the canonical vertex table**

Configure Z1 west, Z2 southwest, Z3 south, Z4 southeast, and Z5 east. For each table entry, validate winding, convexity, area, village clearance, pairwise gap, map bounds, and representative NavMesh samples before any scene mutation. Record the final world vertices in the evidence report.

- [x] **Step 5: Replace rectangular construction with shared quadrilateral meshes**

Create/repair each identity root with: non-colliding ground visual, non-colliding boundary renderer, convex trigger-only `MeshCollider`, `TerrainSurfaceVolume`, and `DinoDeploymentZone` configured from the same source. Remove only superseded builder-owned rectangular zone/surface children after resolving their exact hierarchy paths.

- [x] **Step 6: Convert the full-map Grass volume into neutral ground**

Retain the Ground-layer physical floor and approved visual base, remove its `TerrainSurfaceVolume` and deployment identity, and verify leaving every zone restores the dinosaur prefab base speed.

- [x] **Step 7: Extend canonical preflight and validator**

Compare IDs, names, surfaces, ordered vertices, stable mesh signatures, materials, layers, `isTrigger`, `convex`, component bindings, neutral-floor semantics, camera, NavMesh, defenses, and runtime UI. Canonical input must return before `SetDirty`, mesh recreation, rebake, or save.

- [x] **Step 8: Build through the connected Editor**

Invoke the builder once in Edit Mode, allow compilation/import to settle, confirm `Dinos.unity` remains the active clean scene, and do not hand-edit YAML.

- [x] **Step 9: Run focused scene tests twice**

First run verifies repair; second run verifies canonical no-op behavior. Expected: both pass and fresh Console errors equal zero.

- [x] **Step 10: Review checkpoint**

Inspect the saved hierarchy and scoped Git diff. Confirm no user-owned objects, Packages, ProjectSettings, historical scenes, or AI protocol files changed.

### Task 6: Validate runtime placement, speed, navigation, UI, and attacks

**Files:**
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`
- Modify: `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`
- Create: `reports/phase8/quadrilateral-terrain-zones/runtime-validation.md`

**Interfaces:**
- Consumes: rebuilt canonical scene and zone API.
- Produces: executable evidence for placement legality, UV resolution, 15/15 reachability, five speed multipliers, neutral restoration, UI cleanup, and no attack blocking.

- [x] **Step 1: Write runtime tests for zone legality and relative samples**

For each zone test corners inset by epsilon, center, and asymmetric UV values. Verify village/public-gap samples return `OutsideDeploymentArea`, while every valid resolved sample is contained and can be grounded/NavMesh-sampled.

- [x] **Step 2: Run focused PlayMode tests and fix only contract defects**

Expected: every new sample passes after the scene rebuild; failures report zone ID and UV/world coordinate.

- [x] **Step 3: Exercise the 3×5 navigation and speed matrices**

Run all three gameplay dinosaur prefabs from each zone to village objectives and measure at least 4 m of travel in each terrain. Expected multipliers are Z1 1.10, Z2 1.00, Z3 0.70, Z4 0.85, Z5 0.80 within the existing tolerance; then move onto neutral ground and assert exact base-speed restoration.

- [x] **Step 4: Re-run UI lifecycle and attack regressions**

Assert no-selection, selection, hover, invalid public-gap placement, Ending, reset, and cleanup states. Reuse the real attack event harness to prove static visuals and trigger prisms do not suppress attack events.

- [x] **Step 5: Run full EditMode and PlayMode suites**

Expected: all tests pass, no NaN/exception/missing reference, and fresh Console error count is zero. Record exact totals, durations, Unity version, and Console cursor.

- [x] **Step 6: Review checkpoint**

Compare failures, if any, to the baseline and distinguish pre-existing noise from introduced regressions before changing code.

### Task 7: Produce visual, geometry, longevity, and Player evidence

**Files:**
- Create: `reports/phase8/quadrilateral-terrain-zones/terrain-zones-topology.png`
- Create: `reports/phase8/quadrilateral-terrain-zones/geometry.json`
- Create: `reports/phase8/quadrilateral-terrain-zones/longevity-20-episodes.json`
- Create: `reports/phase8/quadrilateral-terrain-zones/player-smoke.log`
- Create: `reports/phase8/quadrilateral-terrain-zones/validation.md`

**Interfaces:**
- Consumes: the tested scene and existing approved screenshot/build harnesses.
- Produces: human-readable PNG plus machine-readable geometry and runtime evidence.

- [x] **Step 1: Export authoritative geometry evidence**

For Z1–Z5 record ordered A/B/C/D world vertices, signed/absolute area, map-area ratio, minimum inter-zone gap, neutral samples, terrain kind/multiplier, mesh signature, and NavMesh sample results in `geometry.json`.

- [x] **Step 2: Capture the requested PNG and gameplay views**

Save a labeled top-down PNG showing the large village, five quadrilateral regions, their A/B/C/D order, and neutral gaps. Also capture Home at 16:9, 16:10, 21:9, and 1280×720 with matching camera/state conditions.

- [x] **Step 3: Run 20 real scene reload episodes**

Use ten wins and ten losses with each zone exercised four times. Record round transitions, refund behavior, object/subscription/material counts, speed reset, NavMesh validity, memory trend, and Console errors.

- [x] **Step 4: Build and smoke the Windows Player**

Build the same `Dinos.unity`, launch the created Player, verify a stable graphical startup and real round activity, close only the process/window created by this run, and scan its log for crash, exception, missing reference, assertion, NaN, NavMesh, waypoint, armor, error, and failed markers.

- [x] **Step 5: Write the validation report**

Link every command and artifact; disclose inherited warnings and invalid comparisons rather than turning them into pass claims.

- [x] **Step 6: Review checkpoint**

Visually inspect the PNGs at full resolution and confirm the five regions are distinguishable, connect toward the village without entering it, preserve traversable gaps, and align visual/outline/trigger geometry.

### Task 8: Re-freeze the environment and resume protocol validation

**Files:**
- Create: `reports/phase8/quadrilateral-terrain-zones/manifest.json`
- Modify: `reports/phase8/open-tropical-battlefield/manifest.json` only to mark its freeze as historical/superseded without deleting evidence.
- Modify: `docs/PROJECT_PLAN.md`
- Modify: `docs/superpowers/plans/2026-08-29-dino-attack-quadrilateral-terrain-zones.md`

**Interfaces:**
- Consumes: all Task 1–7 evidence.
- Produces: byte-stable scene/build/package/script/material/prefab hashes, completed `TASK-8.10-Q`, and resumed `TASK-8.11`.

- [x] **Step 1: Perform the final verification order**

Run compile, focused EditMode, focused PlayMode, full EditMode, full PlayMode, validator, 15/15 navigation/speed, UI/attack regressions, screenshots, longevity, and Player smoke. After the final full EditMode run, take H0 and do not run another test that may save the scene.

- [x] **Step 2: Prove canonical byte stability**

Invoke `Build(true)` twice after H0 and record H1/H2. Expected: H0 = H1 = H2, scene remains clean, and the NavMeshData reference/signature remains unchanged.

- [x] **Step 3: Create the replacement manifest**

Hash the scene, Windows build, Packages lock, geometry/core/builder scripts, two defense prefabs, two UnitSO assets, active materials, imported CC0 assets, and test/evidence files. Include final vertices and both the superseded and replacement scene hashes.

- [x] **Step 4: Update status and evidence in the source-of-truth plan**

Mark `TASK-8.10-Q` complete only when every hard gate passes; restore `TASK-8.11` to `进行中`; append exact test totals, commands, paths, hashes, warnings, limitations, and the decision that the 6D/504 protocol is unchanged.

- [x] **Step 5: Run final documentation and scoped-diff checks**

```powershell
git diff --check -- docs\PROJECT_PLAN.md docs\superpowers\plans\2026-08-29-dino-attack-quadrilateral-terrain-zones.md reports\phase8\quadrilateral-terrain-zones
$forbidden = @(('T'+'BD'), ('T'+'ODO'), ('implement '+'later'), ('fill in '+'details')); Select-String -Path docs\superpowers\plans\2026-08-29-dino-attack-quadrilateral-terrain-zones.md -Pattern $forbidden
git status --short
git -C dino-attack status --short
```

Expected: no whitespace or placeholder failures; status contains only preserved pre-existing work plus the explicitly listed quadrilateral-zone deliverables.

- [x] **Step 6: Final review checkpoint**

Read the spec requirement-by-requirement and point each one to a passing test or artifact. Do not claim completion for any missing hard gate, and do not stage or commit.
