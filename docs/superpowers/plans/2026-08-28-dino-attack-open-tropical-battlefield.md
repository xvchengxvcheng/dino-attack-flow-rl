# Dino Attack Open Tropical Battlefield Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the current four-zone/three-route Stage 8 map with the approved open tropical battlefield: an expanded enterable village, five always-open deployment zones, terrain speed modifiers, distributed defenses, full-island RTS framing, and a unified warm canyon-tropical art pass.

**Architecture:** Keep the existing `Dinos.unity`, round state machine, shared `DinoDeploymentService`, resource rules, attacks, and Human/AI contract. Add a small `DinoAttack.Map.Core` assembly for deterministic, testable map rules; keep scene-facing MonoBehaviours thin; generate and validate the scene through Unity Editor code rather than hand-editing YAML. Environment objects alter placement, navigation, and dinosaur speed but never attack hit eligibility.

**Tech Stack:** Unity 6000.3.22f1, URP, Unity AI Navigation/NavMesh, Cinemachine 3, Input System, UI Toolkit, NUnit EditMode/PlayMode tests, Unity CLI/live Editor, PowerShell.

**Spec:** `docs/superpowers/specs/2026-08-28-dino-attack-open-tropical-battlefield-design.md`

## Global Constraints

- Work only in `dino-attack/Assets/LlamAcademy/Dinos/`, `dino-attack/Assets/Tests/`, approved `dino-attack/Assets/ThirdParty/Kenney/` files, and `reports/phase8/`; do not change Packages or ProjectSettings without new authorization.
- Use the currently installed Unity `6000.3.22f1` and the project-specific Unity CLI/connected Editor workflow; never hand-edit `.unity`, `.prefab`, `.mat`, or `.meta` YAML.
- Preserve both dirty worktrees. Never reset, clean, stash, bulk-stage, overwrite unrelated edits, or create a commit unless the user explicitly requests it.
- Before every implementation task, read `AGENTS.md`, `docs/PROJECT_PLAN.md`, this plan, and the approved spec; use CodeGraph before locating or understanding indexed `dino-attack` code.
- Keep `Setup -> Running -> Ending -> Scoring -> EnemyRepairs -> Ended`, existing food accounting, Hard initial difficulty, the central victory-objective semantics, and the shared Human/AI deployment API.
- Five deployment zones are always open to all dinosaur types. Do not introduce routes, zone rotation, fixed unit slots, class restrictions, a sixth zone, or a rainforest clearing.
- Terrain speed multipliers are exact design constants: StoneRoad `1.10f`, Grass `1.00f`, Mud `0.85f`, Slope `0.80f`, ShallowWater `0.70f`.
- No environment object may block or cancel attacks. Do not add Physics/NavMesh raycasts, cover defense stats, hit chance, damage reduction, or cover UI.
- Existing Dino Attack art is primary. Kenney Tower Defense and Mini Forest assets are supplemental and must remain covered by the existing CC0 manifest.
- Each code task follows red-green-refactor. Each task ends with a reviewable diff and evidence record; a Git commit is optional and requires separate user authorization.

---

## File Structure

### Create

- `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/DinoAttack.Map.Core.asmdef` — testable map-rule assembly.
- `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/DeploymentZoneId.cs` — stable Z1–Z5 identifiers.
- `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/DeploymentHoverInfo.cs` — immutable zone/surface UI payload.
- `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/DinoDeploymentZone.cs` — zone collider, identity, label, visual state, and dominant surface.
- `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/TerrainSurfaceKind.cs` — five surface identifiers.
- `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/TerrainSpeedProfile.cs` — exact speed multipliers and overlap priority.
- `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/TerrainSurfaceStack.cs` — deterministic active-surface resolver.
- `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/TerrainSurfaceVolume.cs` — trigger-volume surface marker.
- `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/CameraFramingMath.cs` — clamping, zoom, and Home framing math.
- `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/DefensePresetSelector.cs` — deterministic preset selection by seed and round.
- `dino-attack/Assets/LlamAcademy/Dinos/Map/Runtime/DinoAttack.Map.Runtime.asmdef` — scene-runtime map adapters referencing only `Map.Core` and Unity packages.
- `dino-attack/Assets/LlamAcademy/Dinos/Map/Runtime/DinoDeploymentZonePresenter.cs` — selection/hover visibility and zone-information events.
- `dino-attack/Assets/LlamAcademy/Dinos/Map/Runtime/VillageDefenseLayoutController.cs` — activates a deterministic validated defense preset.
- `dino-attack/Assets/LlamAcademy/Dinos/Map/Runtime/DinoTerrainSpeedController.cs` — applies one surface multiplier to a dinosaur's captured base speed.
- `dino-attack/Assets/LlamAcademy/Dinos/Editor/DinoAttack.Map.Editor.asmdef` — Editor-only builder/validator assembly referenced by Editor tests.
- `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs` — idempotent graybox and art-pass scene construction.
- `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs` — hierarchy, zone, objective, camera, NavMesh, reference, and attack-semantics checks.
- `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldMapCoreTests.cs` — pure rule tests.
- `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs` — Editor scene contract tests.
- `dino-attack/Assets/Tests/PlayMode/DinoAttack.Map.PlayMode.Tests.asmdef` — PlayMode test assembly.
- `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs` — runtime deployment, speed, camera, and reset checks.
- `reports/phase8/open-tropical-battlefield/` — baseline, validation, screenshots, performance, manifest, and final report.

### Modify

- `dino-attack/Assets/LlamAcademy/Dinos/Deployment/DinoAttack.Deployment.Core.asmdef` — reference `DinoAttack.Map.Core`.
- `dino-attack/Assets/LlamAcademy/Dinos/Deployment/DinoDeploymentService.cs` — evaluate the union of five `DinoDeploymentZone` colliders and expose resolved-zone metadata.
- `dino-attack/Assets/LlamAcademy/Dinos/Player/PlaceDinoVisualization.cs` — drive zone visibility/hover feedback without cover indicators.
- `dino-attack/Assets/LlamAcademy/Dinos/Player/CameraControl.cs` — edge/keyboard pan, wheel zoom, middle-drag, Home reset, and validated bounds.
- `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeUI.cs` — display zone, surface, multiplier, and non-color invalid reason.
- `dino-attack/Assets/LlamAcademy/Dinos/Deployment/RuntimeUIStateFormatter.cs` — pure formatting for zone/surface feedback.
- `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeUI.uxml` and `RuntimeStyles.uss` — add compact hover feedback without covering Z3 or the plaza.
- `dino-attack/Assets/LlamAcademy/Dinos/Enemy/EnemyAIController.cs` — consume validated defense slots/preset RNG while preserving repair, upgrade, resource, and event behavior.
- `dino-attack/Assets/LlamAcademy/Dinos/Dinos/Pachycephalosaurus/Pachycephalosaurus_New.prefab` — add the speed controller.
- `dino-attack/Assets/LlamAcademy/Dinos/Dinos/TRex/Trex_New Variant.prefab` — add the speed controller.
- `dino-attack/Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor_New.prefab` — add the speed controller.
- `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity` — replace the old Stage 8 strategy root with the approved full map.
- `dino-attack/Assets/Tests/Editor/DinoAttack.Training.Core.Tests.asmdef` — reference `DinoAttack.Map.Core` and, after Task 5 creates it, `DinoAttack.Map.Editor`.
- `dino-attack/Assets/Tests/Editor/DinoDeploymentRulesTests.cs` and `RuntimeUIStateFormatterTests.cs` — five-zone and feedback regression coverage.
- `docs/PROJECT_PLAN.md`, `docs/superpowers/plans/2026-08-28-phase-8-dino-attack.md`, and this file — checkbox, evidence, state, and decision updates after every task.

## Stable Interfaces

```csharp
namespace LlamAcademy.Dinos.Map
{
    public enum DeploymentZoneId { Z1RuinsForecourt, Z2OpenMeadow, Z3RiverTerrace, Z4PalmGrove, Z5RockyShelf }
    public enum TerrainSurfaceKind { Grass, StoneRoad, Mud, Slope, ShallowWater }

    public static class TerrainSpeedProfile
    {
        public static float GetMultiplier(TerrainSurfaceKind surface);
        public static int GetPriority(TerrainSurfaceKind surface);
    }

    public sealed class TerrainSurfaceStack
    {
        public TerrainSurfaceKind Current { get; }
        public void Enter(TerrainSurfaceKind surface);
        public void Exit(TerrainSurfaceKind surface);
        public void Clear();
    }

    public static class CameraFramingMath
    {
        public static Vector3 ClampTarget(Vector3 position, Bounds worldBounds);
        public static float ClampZoom(float value, float minimum, float maximum);
        public static Vector3 ScaleFollowOffset(Vector3 homeOffset, float zoomScale);
    }

    public static class DefensePresetSelector
    {
        public static int SelectIndex(int mapSeed, int round, int presetCount);
    }
}
```

```csharp
public readonly struct DeploymentHoverInfo
{
    public DeploymentHoverInfo(
        DeploymentZoneId zoneId,
        string zoneName,
        TerrainSurfaceKind surface,
        float speedMultiplier);
    public DeploymentZoneId ZoneId { get; }
    public string ZoneName { get; }
    public TerrainSurfaceKind Surface { get; }
    public float SpeedMultiplier { get; }
}

public sealed class DinoDeploymentService : MonoBehaviour
{
    public bool TryGetZone(Vector3 position, out DinoDeploymentZone zone);
}

public sealed class VillageDefenseLayoutController : MonoBehaviour
{
    public int ActivePresetIndex { get; }
    public void ActivateForRound(int mapSeed, int round);
    public bool TryGetSpawnPosition(System.Random random, out Vector3 position);
}
```

The runtime source of truth is `DinoDeploymentZone[]`; UI, Human placement, and AI coordinate decoding must not keep separate zone geometries.

---

### Task 1: Refresh the Recoverable Baseline and Map Contract

**Files:**
- Create: `reports/phase8/open-tropical-battlefield/baseline.md`
- Create through Unity Editor API: `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Backups/Dinos.pre-open-tropical-20260828.unity`
- Modify: `docs/PROJECT_PLAN.md`
- Modify: `docs/superpowers/plans/2026-08-28-phase-8-dino-attack.md`

**Interfaces:**
- Consumes: approved spec and the current `Dinos.unity` dirty state.
- Produces: hashes, screenshots, hierarchy/Console/performance snapshots, file ownership, and a verified backup for every later task.

- [x] **Step 1: Record both repositories without changing them**

Run:

```powershell
git status --short
git -C dino-attack status --short
git rev-parse HEAD
git -C dino-attack rev-parse HEAD
Get-FileHash dino-attack\Assets\LlamAcademy\Dinos\Scenes\Dinos.unity -Algorithm SHA256
```

Expected: commands succeed; `baseline.md` classifies existing changes as user work or prior Stage 8 work, never as disposable files.

- [x] **Step 2: Capture the current scene baseline through the connected Editor**

Record the active scene path, `isDirty`, hierarchy under `/World`, deployment colliders, camera lens/follow offset/bounds, Console counts, Game and Scene screenshots, draw calls, triangles, vertices, CPU/GPU frame timing, NavMesh surfaces, and current path-completeness samples.

Expected: `Dinos.unity` is not saved merely by inspection; every metric records capture resolution and Play/Edit state.

- [x] **Step 3: Create and verify a fresh versioned backup**

Use an Editor method that calls `AssetDatabase.CopyAsset`, opens the copy additively/read-only for validation, returns to `Dinos.unity`, and compares SHA-256 before any new scene write.

Expected: the backup opens without missing references; the original scene hash remains unchanged during this step.

- [x] **Step 4: Freeze the approved map contract in the plan state**

Mark old 8.6–8.9 evidence as “historical first-pass evidence, superseded for current acceptance,” set redesigned 8.6 as the sole active implementation task, and block 8.10 until the new 8.9 passes.

- [x] **Step 5: Validate the documentation checkpoint**

Run:

```powershell
git diff --check -- docs\PROJECT_PLAN.md docs\superpowers\plans\2026-08-28-phase-8-dino-attack.md docs\superpowers\plans\2026-08-28-dino-attack-open-tropical-battlefield.md
```

Expected: no whitespace errors; no Unity runtime, scene, Prefab, Package, or ProjectSettings file changed in Task 1.

---

### Task 2: Add Testable Five-Zone, Terrain, Camera, and Defense Rules

**Files:**
- Create: `Assets/LlamAcademy/Dinos/Map/Core/DinoAttack.Map.Core.asmdef`
- Create: `Assets/LlamAcademy/Dinos/Map/Core/DeploymentZoneId.cs`
- Create: `Assets/LlamAcademy/Dinos/Map/Core/TerrainSurfaceKind.cs`
- Create: `Assets/LlamAcademy/Dinos/Map/Core/TerrainSpeedProfile.cs`
- Create: `Assets/LlamAcademy/Dinos/Map/Core/TerrainSurfaceStack.cs`
- Create: `Assets/LlamAcademy/Dinos/Map/Core/CameraFramingMath.cs`
- Create: `Assets/LlamAcademy/Dinos/Map/Core/DefensePresetSelector.cs`
- Create: `Assets/Tests/Editor/OpenTropicalBattlefieldMapCoreTests.cs`
- Modify: `Assets/Tests/Editor/DinoAttack.Training.Core.Tests.asmdef`
- Modify: `Assets/LlamAcademy/Dinos/Deployment/DinoAttack.Deployment.Core.asmdef`

**Interfaces:**
- Consumes: exact constants and identifiers in the approved spec.
- Produces: `DeploymentZoneId`, `TerrainSurfaceKind`, `TerrainSpeedProfile`, `TerrainSurfaceStack`, `CameraFramingMath`, and `DefensePresetSelector` for Tasks 3–7.

- [x] **Step 1: Write failing terrain profile tests**

```csharp
[TestCase(TerrainSurfaceKind.StoneRoad, 1.10f)]
[TestCase(TerrainSurfaceKind.Grass, 1.00f)]
[TestCase(TerrainSurfaceKind.Mud, 0.85f)]
[TestCase(TerrainSurfaceKind.Slope, 0.80f)]
[TestCase(TerrainSurfaceKind.ShallowWater, 0.70f)]
public void GetMultiplier_ReturnsApprovedValue(TerrainSurfaceKind surface, float expected)
{
    Assert.That(TerrainSpeedProfile.GetMultiplier(surface), Is.EqualTo(expected).Within(0.0001f));
}
```

- [x] **Step 2: Run the focused test and verify red**

Run the connected-Editor EditMode test filter `OpenTropicalBattlefieldMapCoreTests`.

Expected: compilation/test failure because `DinoAttack.Map.Core` types do not exist.

- [x] **Step 3: Implement the enums and exact profile**

Create the core assembly without project-runtime dependencies:

```json
{
  "name": "DinoAttack.Map.Core",
  "rootNamespace": "LlamAcademy.Dinos.Map",
  "autoReferenced": true
}
```

```csharp
public static float GetMultiplier(TerrainSurfaceKind surface) => surface switch
{
    TerrainSurfaceKind.StoneRoad => 1.10f,
    TerrainSurfaceKind.Grass => 1.00f,
    TerrainSurfaceKind.Mud => 0.85f,
    TerrainSurfaceKind.Slope => 0.80f,
    TerrainSurfaceKind.ShallowWater => 0.70f,
    _ => 1.00f
};
```

Implement priority as `ShallowWater 5`, `Mud 4`, `Slope 3`, `StoneRoad 2`, `Grass 1`.

- [x] **Step 4: Add failing overlap-stack tests**

```csharp
[Test]
public void SurfaceStack_UsesHighestPriorityAndRestoresPreviousSurface()
{
    TerrainSurfaceStack stack = new();
    stack.Enter(TerrainSurfaceKind.StoneRoad);
    stack.Enter(TerrainSurfaceKind.ShallowWater);
    Assert.That(stack.Current, Is.EqualTo(TerrainSurfaceKind.ShallowWater));
    stack.Exit(TerrainSurfaceKind.ShallowWater);
    Assert.That(stack.Current, Is.EqualTo(TerrainSurfaceKind.StoneRoad));
}
```

Also test duplicate Enter/Exit counts, `Clear()`, and Grass fallback.

- [x] **Step 5: Implement the minimal deterministic stack**

Use a count per `TerrainSurfaceKind`; recompute `Current` from non-zero entries and `GetPriority`. Never multiply several terrain coefficients.

- [x] **Step 6: Add camera and defense selector tests**

```csharp
[Test]
public void SelectIndex_IsStableForSeedRoundAndWithinRange()
{
    int first = DefensePresetSelector.SelectIndex(1701, 3, 4);
    Assert.That(first, Is.InRange(0, 3));
    Assert.That(DefensePresetSelector.SelectIndex(1701, 3, 4), Is.EqualTo(first));
}
```

Test `presetCount <= 0` throws `ArgumentOutOfRangeException`; test camera position and zoom clamps at both bounds and Home returns the exact configured position/offset.

- [x] **Step 7: Run all EditMode tests**

Expected: the new focused suite passes and the existing 27-test baseline does not regress. Save XML/JSON results under `reports/phase8/open-tropical-battlefield/task-2/`.

- [x] **Step 8: Record a review checkpoint**

Inspect only explicit Task 2 paths with `git diff --check` and a path-scoped diff. Do not commit unless the user separately authorizes it.

---

### Task 3: Replace Four Legacy Placement Bounds with Five Always-Open Zones

**Files:**
- Create: `Assets/LlamAcademy/Dinos/Map/Core/DinoDeploymentZone.cs`
- Create: `Assets/LlamAcademy/Dinos/Map/Core/DeploymentHoverInfo.cs`
- Create: `Assets/LlamAcademy/Dinos/Map/Runtime/DinoAttack.Map.Runtime.asmdef`
- Create: `Assets/LlamAcademy/Dinos/Map/Runtime/DinoDeploymentZonePresenter.cs`
- Modify: `Assets/LlamAcademy/Dinos/Deployment/DinoDeploymentService.cs`
- Modify: `Assets/LlamAcademy/Dinos/Deployment/DinoDeploymentZoneLayout.cs`
- Modify: `Assets/LlamAcademy/Dinos/Player/PlaceDinoVisualization.cs`
- Modify: `Assets/Tests/Editor/DinoDeploymentRulesTests.cs`

**Interfaces:**
- Consumes: `DeploymentZoneId`, existing `Evaluate(...)`, `DeploymentResult`, and `PlaceDinoVisualization.ChangeDino`.
- Produces: `DinoDeploymentZone[] PlacementZones`, `TryGetZone(Vector3, out DinoDeploymentZone)`, and `DeploymentHoverInfo` events used by UI and scene validation.

- [x] **Step 1: Write failing five-zone union tests**

```csharp
[Test]
public void ContainsPosition_AcceptsEachOfFiveZonesAndRejectsNorthGap()
{
    Bounds[] zones = FiveApprovedBounds();
    foreach (Bounds zone in zones)
        Assert.That(DinoDeploymentZoneLayout.ContainsPosition(default, zones, zone.center), Is.True);
    Assert.That(DinoDeploymentZoneLayout.ContainsPosition(default, zones, new Vector3(0f, 0f, 30f)), Is.False);
}
```

Add tests proving an empty explicit zone array rejects placement when `allowLegacyFallback=false`, while the historical diagnostic scene can still opt into its old fallback with `allowLegacyFallback=true`.

- [x] **Step 2: Run the focused deployment tests and verify red**

Expected: compilation fails because the layout has no explicit `allowLegacyFallback` parameter.

- [x] **Step 3: Implement stable zone components and lookup**

Create the runtime adapter assembly:

```json
{
  "name": "DinoAttack.Map.Runtime",
  "rootNamespace": "LlamAcademy.Dinos.Map",
  "references": ["DinoAttack.Map.Core"],
  "autoReferenced": true
}
```

```csharp
[RequireComponent(typeof(BoxCollider))]
public sealed class DinoDeploymentZone : MonoBehaviour
{
    [field: SerializeField] public DeploymentZoneId Id { get; private set; }
    [field: SerializeField] public string DisplayName { get; private set; }
    [field: SerializeField] public TerrainSurfaceKind DominantSurface { get; private set; }
    public BoxCollider BoundsCollider { get; private set; }
    public bool Contains(Vector3 point)
    {
        Bounds bounds = BoundsCollider.bounds;
        return point.x >= bounds.min.x && point.x <= bounds.max.x &&
               point.z >= bounds.min.z && point.z <= bounds.max.z;
    }
}
```

Add serialized `UseLegacyPlacementBounds`. `DinoDeploymentService.IsInsideApprovedZone` must iterate exactly the serialized five-zone array; only when the array is empty and this flag is true may it use `PlacementBounds`. Keep the flag enabled only in the historical diagnostic scene and disabled in `Dinos.unity`.

- [x] **Step 4: Implement presenter visibility rules**

The presenter keeps physical landmarks active at all times, disables boundary renderers when no dinosaur is selected, enables all five at low intensity during selection, highlights only the hovered zone, and clears state on cancellation or non-interactive game states.

- [x] **Step 5: Connect the existing placement preview**

`ChangeDino(null)` calls `ZonePresenter.Hide()`; selecting a dinosaur calls `ShowForSelection()`; `Update()` calls `UpdateHover(transform.position)` after `EvaluateDeployment`. Do not add any cover, LOS, or attack-blocked state.

- [x] **Step 6: Run EditMode and a minimal PlayMode placement smoke**

Deferred by the Task 3 preflight ruling: constructed-object EditMode coverage is complete, while the scene-based five-zone/three-dinosaur smoke requires the physical zones owned by Task 5. Task 5 must close this checkbox with the 15-combination matrix.

Verify Setup and Running accept a legal center point from Z1–Z5 for all three DinoSO types; north gap and removed rainforest coordinates return `OutsideBounds`; cancellation and Ending hide all boundaries.

- [x] **Step 7: Save evidence and review the diff**

Record the five IDs, collider centers/sizes, result matrix, Console count, and screenshots under `task-3/`; do not advance if the UI, Human controller, and AI coordinate mapping reference different geometry.

---

### Task 4: Implement Dinosaur Terrain Speed Without Changing Combat

**Files:**
- Create: `Assets/LlamAcademy/Dinos/Map/Core/TerrainSurfaceVolume.cs`
- Create: `Assets/LlamAcademy/Dinos/Map/Runtime/DinoTerrainSpeedController.cs`
- Create: `Assets/Tests/PlayMode/DinoAttack.Map.PlayMode.Tests.asmdef`
- Create: `Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`
- Modify through Unity Editor API: the three dinosaur Prefabs listed in File Structure.
- Verify unchanged behavior: `Assets/LlamAcademy/Dinos/Unit/AttackRadius.cs`, `Assets/LlamAcademy/Dinos/Behavior/AttackClosestObjectAction.cs`

**Interfaces:**
- Consumes: `TerrainSurfaceStack`, `TerrainSpeedProfile`, and each Prefab's existing `NavMeshAgent.speed`.
- Produces: `CurrentSurface`, `CurrentMultiplier`, and one reversible speed assignment per dinosaur.

- [x] **Step 1: Write failing runtime speed tests**

```csharp
[UnityTest]
public IEnumerator SpeedController_AppliesOneMultiplierAndRestoresBaseSpeed()
{
    NavMeshAgent agent = CreateAgent(baseSpeed: 8f);
    DinoTerrainSpeedController controller = agent.gameObject.AddComponent<DinoTerrainSpeedController>();
    controller.EnterForTest(TerrainSurfaceKind.ShallowWater);
    Assert.That(agent.speed, Is.EqualTo(5.6f).Within(0.001f));
    controller.ExitForTest(TerrainSurfaceKind.ShallowWater);
    Assert.That(agent.speed, Is.EqualTo(8f).Within(0.001f));
    yield return null;
}
```

Also test overlapping StoneRoad/ShallowWater, duplicate triggers, disable/enable reset, and finite positive base speed.

- [x] **Step 2: Run the focused PlayMode test and verify red**

Expected: compilation fails because the runtime controller does not exist.

- [x] **Step 3: Implement the thin runtime adapter**

Capture base speed once after the NavMeshAgent is available. `OnTriggerEnter/Exit` reads `TerrainSurfaceVolume.Surface`, updates the stack, and assigns `agent.speed = baseSpeed * TerrainSpeedProfile.GetMultiplier(stack.Current)`. `OnDisable` clears the stack and restores base speed.

The PlayMode test assembly references only explicit assemblies:

```json
{
  "name": "DinoAttack.Map.PlayMode.Tests",
  "references": ["DinoAttack.Map.Core", "DinoAttack.Map.Runtime", "DinoAttack.Deployment.Core"],
  "optionalUnityReferences": ["TestAssemblies"],
  "autoReferenced": false
}
```

- [x] **Step 4: Add the controller to all three dinosaur Prefabs**

Use `PrefabUtility.LoadPrefabContents`/`SaveAsPrefabAsset`; reject duplicate controllers; do not change health, attacks, radii, animations, base speed, cost, or Behavior Graph references.

- [x] **Step 5: Add the attack non-occlusion regression**

Construct attacker and target inside valid attack range with a solid building collider between them. Verify the existing attack event/effect still fires after cooldown. The test fails if any new LOS query cancels the hit.

- [x] **Step 6: Run the complete Unity test baseline**

Expected: all EditMode and PlayMode tests pass, no new Error/Exception/NaN, and the only speed differences are the five approved multipliers.

- [x] **Step 7: Record measured traversal evidence**

Deferred until Task 5 creates the scene-owned terrain strips and unified NavMesh. Task 4 validated exact runtime speed assignments; Task 5/8 must close physical traversal ratios at the ±5% threshold.

On equal-length strips, measure each dinosaur's Grass time and compare StoneRoad/Mud/Slope/ShallowWater ratios. Record actual/expected ratio and tolerance; use ±5% as the initial pass threshold and report any frame-rate dependence.

Closed by Task 5: all three gameplay Prefabs traversed real scene `TerrainSurfaceVolume` triggers over equal 4.000 m NavMesh paths. All 15 actual time ratios passed ±5%; the final maximum error was 0.3507%. Raw frames, seconds, path length, expected ratio, actual ratio, and error are in `reports/phase8/open-tropical-battlefield/task-5/traversal-ratios.json`.

---

### Task 5: Build the Expanded Village, Five-Zone Graybox, and Deterministic Defenses

**Files:**
- Create: `Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Create: `Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- Create: `Assets/LlamAcademy/Dinos/Editor/DinoAttack.Map.Editor.asmdef`
- Create: `Assets/LlamAcademy/Dinos/Map/Runtime/VillageDefenseLayoutController.cs`
- Create: `Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`
- Modify: `Assets/Tests/Editor/DinoAttack.Training.Core.Tests.asmdef` to reference `DinoAttack.Map.Editor`.
- Modify: `Assets/LlamAcademy/Dinos/Enemy/EnemyAIController.cs`
- Modify through Unity Editor API: `Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`

**Interfaces:**
- Consumes: five zone components, terrain volumes, defense selector, current NavMesh surfaces, existing wall/defender Prefabs, `RoundManager.Round`, and `RoundManager.DinoTarget`.
- Produces: `/World/Open Tropical Battlefield` hierarchy, central plaza target, four village districts, terrain volumes, defense preset roots/slots, updated world/camera bounds, and validator output.

- [x] **Step 1: Write failing scene-contract tests**

```csharp
[Test]
public void SceneContract_RequiresExactlyFiveUniqueDeploymentZones()
{
    OpenTropicalBattlefieldReport report = OpenTropicalBattlefieldValidator.ValidateLoadedScene();
    Assert.That(report.ZoneIds, Is.EquivalentTo(System.Enum.GetValues(typeof(DeploymentZoneId))));
}
```

Add assertions for one central objective under the plaza, four village district roots, five surface categories, no active `Stage8 Strategy` route dividers, one camera bounds collider, at least two defense presets, and no north/rainforest deployment zone.

- [x] **Step 2: Run the scene tests and verify red**

Expected: current four-zone/route-divider scene fails the approved contract.

- [x] **Step 3: Implement an idempotent graybox builder**

Create the Editor assembly and add its name to the existing Editor test asmdef references:

```json
{
  "name": "DinoAttack.Map.Editor",
  "rootNamespace": "LlamAcademy.Dinos.Map.Editor",
  "references": ["DinoAttack.Map.Core", "DinoAttack.Map.Runtime", "DinoAttack.Deployment.Core", "Unity.AI.Navigation"],
  "includePlatforms": ["Editor"],
  "autoReferenced": false
}
```

The builder creates or updates these exact roots:

```text
/World/Open Tropical Battlefield
  /Outer Scenery
  /Village/NorthWest
  /Village/NorthEast
  /Village/SouthWest
  /Village/SouthEast
  /Village/Central Plaza
  /Deployment Zones/Z1..Z5
  /Terrain Surfaces/Grass|StoneRoad|Mud|Slope|ShallowWater
  /Defense Presets/Preset 0..N
```

It preserves unrelated roots, moves the existing egg/target semantics to `Central Plaza`, and removes the active old route-divider/deployment-root only after the new hierarchy validates. Running the builder twice must not duplicate objects or materials.

- [x] **Step 4: Construct the approved 2–2–1 horseshoe graybox**

Build Z1 near west, Z2 middle west, Z3 far south, Z4 middle east, and Z5 near east. Expand effective navigable bounds by a measured 25–35% relative to Task 1, not by scaling all objects. Use low segmented perimeter cliffs and preserve a narrow northern backdrop.

- [x] **Step 5: Build four enterable districts and central victory plaza**

Use building footprints and street widths validated against the largest dinosaur NavMeshAgent diameter plus turning margin. No street may be tagged or carved as a compulsory route; every zone must retain at least one complete approach while defenses are intact. Record a second distinct approach where the geometry naturally supports it, but do not manufacture fixed lanes to satisfy a route count.

- [x] **Step 6: Add deterministic defense presets**

`VillageDefenseLayoutController.ActivateForRound(mapSeed, round)` calls `DefensePresetSelector.SelectIndex`, activates one preset root before enemy setup, and exposes validated defender spawn slots. `EnemyAIController.SpawnNewTroops` samples these slots with a seeded local RNG when the controller is present and falls back to current triangulation only in the historical diagnostic scene. Preserve repair, upgrade, food refund, `OnSpawnDefender`, `OnSpawnWall`, and death-event behavior.

- [x] **Step 7: Bake and validate all runtime NavMeshes**

For every dinosaur agent type and each Z1–Z5 sample set, calculate paths to the central plaza. Require `PathComplete`, finite length, and no hidden waypoint injection. Validate defenders can reach all defense patrol slots and that destructible defense changes do not strand agents.

- [x] **Step 8: Save graybox evidence**

Save top-down, 55° Game view, hierarchy, zone bounds, defense preset matrices, path lengths, NavMesh screenshots, scene hash, and Console output under `task-5/`. Do not begin art dressing until all hard graybox checks pass.

**Task 5 validation evidence (2026-08-28):** Three independent review rounds ended PASS after correcting legacy waypoints, dead-wall repair, safe local wall upgrades, historical-scene fallback, both-preset navigation, HealthBar lifecycle, and evidence strictness. Final connected-Editor evidence: focused EditMode 8/8, full EditMode 73/73, full PlayMode 20/20, Preset 0 and 1 each gameplay paths 15/15 and defender slots 3/3, real three-Prefab/five-surface traversal within ±5%, recompile `up_to_date`, fresh Console errors 0, `Dinos.unity` active/stopped/clean. Final scene SHA-256 is `2DFF1135F5161BB824B5D44BB380DAD467A474C752BD65857BC5A6C6B9B47D8C`; untouched backup remains `9A983C1840ACB7B4B988F85DFE9314CDBEE0D613C2B204E60E82AE943C161C20`. Evidence is under `reports/phase8/open-tropical-battlefield/task-5/` and the SDD Task 5 report.

---

### Task 6: Implement Full-Island RTS Camera Controls

**Files:**
- Modify: `Assets/LlamAcademy/Dinos/Player/CameraControl.cs`
- Modify through Unity Editor API: `Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`
- Extend: `Assets/Tests/Editor/OpenTropicalBattlefieldMapCoreTests.cs`
- Extend: `Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`

**Interfaces:**
- Consumes: `CameraFramingMath`, scene world bounds, current `CinemachineCamera`, and `CinemachineFollow.FollowOffset`.
- Produces: edge/arrow-key pan, wheel zoom, middle-button drag, Home reset, and deterministic complete-island framing.

- [x] **Step 1: Write failing camera-input state tests**

Test that Home restores the configured follow target and follow offset exactly, wheel input clamps at min/max zoom, pan clamps inside world bounds, and middle-drag uses world-plane delta rather than changing camera rotation.

- [x] **Step 2: Run the focused tests and verify red**

Expected: current `CameraControl` has no zoom, drag, or Home behavior.

- [x] **Step 3: Implement camera controls without changing the 55° presentation**

Cache `CinemachineCamera`, `CinemachineFollow`, home target position, and home follow offset in `Awake`. Apply pan in world XZ, zoom by scaling the follow offset within serialized limits, middle-drag by ray/ground-plane delta, and Home by restoring the cached values. Call one clamp path after every input mode.

- [x] **Step 4: Configure complete-island default framing**

Set the scene camera target, follow offset, field of view, and bounds so the entire island plus a controlled outer scenery rim fits at 16:9. Preserve the approved oblique angle; do not switch to orthographic or crop Z3.

- [x] **Step 5: Validate four aspect ratios**

Capture 1920×1080, 1920×1200, 2560×1080, and 1280×720. At Home, island edges, Z1–Z5, the plaza, and interaction UI must be visible; at maximum zoom, units and placement reasons must remain readable.

- [x] **Step 6: Record camera evidence**

Save screenshots, camera serialized values, bounds, input matrix, and Console output under `task-6/`.

**Historical Task 6 final scoped review (2026-08-29): PASS.** Production camera/input behavior from rounds 1–2 and the round-3 canonical guard remain unchanged. Focused scene tests `11/11`, full EditMode `81/81`, full PlayMode `26/26`, recompile `up_to_date` with 0 errors, and fresh Console after cursor `6103` with 0 errors agree across the corrected records. A full EditMode run may intentionally exercise saved-deviation repair/rebake paths and produce a new clean canonical serialization; the reviewer subsequently recorded before/after-first/after-second SHA-256 all equal to current hash `81EA6DB7DC96EFE464BDC78A3CD6E0E9DAC109DC745ADFB6A994CBBE785823E8`. NavMeshData references stayed stable across both canonical calls within the same connected Editor session. The saved camera-bounds deviation regression repaired the real deviation and then became byte-stable. Backup hash remains unchanged. At that historical point Task 6 completed and Task 7 became active; Tasks 7–8 and TASK-8.10 have since completed.

---

### Task 7: Apply the Warm Canyon-Tropical Art Pass and Deployment Feedback

**Files:**
- Modify through Unity Editor API: `Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`
- Create/modify approved assets under: `Assets/LlamAcademy/Dinos/Stage8/Materials/` and `Assets/ThirdParty/Kenney/`
- Modify: `PlaceDinoVisualization.cs`, `RuntimeUI.cs`, `RuntimeUIStateFormatter.cs`, `RuntimeUI.uxml`, `RuntimeStyles.uss`
- Extend: `RuntimeUIStateFormatterTests.cs`, `OpenTropicalBattlefieldSceneTests.cs`, and PlayMode tests.

**Interfaces:**
- Consumes: validated Task 5 graybox, zone hover events, current UI state, and the CC0 import manifest.
- Produces: final environment dressing, boundary presentation, hover text, invalid feedback, and performance evidence.

- [x] **Step 1: Write failing formatter and visibility tests**

```csharp
[Test]
public void FormatTerrain_ShowsNameAndMultiplier()
{
    Assert.That(RuntimeUIStateFormatter.FormatTerrain("河岸台地", "浅水", 0.70f),
        Is.EqualTo("河岸台地 · 浅水 0.70×"));
}
```

Test no-selection boundaries hidden, selection shows all five low-intensity boundaries, hover highlights one, invalid placement includes text and icon, and Ending clears them.

- [x] **Step 2: Run focused UI tests and verify red**

Expected: current formatter/presenter cannot produce the approved terrain feedback.

- [x] **Step 3: Replace legacy route materials and dress the five identities**

Use orange-brown segmented cliffs, sand village ground, low-saturation grass/palms, teal shallow water, and warm dark wood. Dress Z1 with ruins, Z2 with sparse meadow detail, Z3 with river/mud edges, Z4 with sparse palm trunks, and Z5 with low rocks/slope markers. Do not recreate visible route lanes.

- [x] **Step 4: Enlarge and dress the village without closing navigation**

Use the existing Dino Attack village assets first. Add only selected Kenney fences, bridge/platform pieces, tent/building details, rocks, flags, and markers through project-side URP materials. Keep all four districts and plaza enterable; treat small decorative objects as non-blocking unless the graybox validator explicitly approves their collider.

- [x] **Step 5: Implement deployment-only boundaries and hover information**

Keep zone landmarks visible; enable boundary renderers only during dinosaur selection; display zone name, surface, multiplier, and invalid reason. Never display cover, LOS, blocked-shot, defense bonus, or route labels.

- [x] **Step 6: Audit materials, colliders, NavMesh, and CC0 provenance**

Reject missing/pink materials, unmanifested Kenney files, duplicate GLB/OBJ/MTL variants, source-FBX edits, decorative NavMesh carving, or click-blocking colliders. Update the existing import manifest only for actually used supplemental files.

- [x] **Step 7: Re-run navigation and camera acceptance after dressing**

All 15 dinosaur-zone combinations remain `PathComplete`; terrain volume visuals align with triggers; the full-island Home framing and four aspect ratios remain valid.

- [x] **Step 8: Compare performance with Task 1 under identical conditions**

Use the same resolution, quality, Play state, camera, and sampling window. Report draw calls, triangles, vertices, CPU/GPU time, memory, and NavMesh update cost. If not comparable, mark the check invalid rather than claiming no regression.

**Task 7 implementer evidence (2026-08-29; final scoped-review fix complete, awaiting read-only re-review):** Initial TDD RED covered the missing terrain/failure formatter APIs and Art Pass; round 1 exposed `PlaceDinoVisualization.ZonePresenter=null`. The final review found that Labrada did not render the Unicode warning glyph, so a focused formatter RED passed 4/5 with `[!]` expected and the unsupported glyph actual. GREEN now uses the font-safe ASCII `[!]` marker in every failure reason. The real-scene lifecycle test is `1/1`: no selection hides five, selection shows five, Z3 hover displays `River Terrace · Shallow Water 0.70×`, illegal placement displays `[!]` plus text, and Ending clears both HUD labels, preview, SafeZone, and five boundaries. Verification followed the required order: focused tests, full EditMode `85/85`, full PlayMode `27/27`, four new Play Mode composited captures, then H0 and two canonical builds; no EditMode test ran after the builds. All four captures visibly show `[!] Outside the deployment area`; marker-prefix pixel counts are 135/243/269/304 and every audit row is `visible=true`, with no safe-area clipping. The art root remains 42 renderers/0 colliders and seven URP/Lit materials. Active CC0 manifest is 8/8 matching hashes with each model actively referenced and no duplicate GLB/OBJ/MTL; unused `selection-a.fbx` is deleted, while `tree.fbx` remains only for six inactive superseded objects and is not declared active. Canonical build H0/H1/H2 all equal SHA-256 `B5609076A9E52A8CEFEAF2231AE623D5E14319DBD8371CB33AD668D04EC0E347`; scene is active/stopped/clean, validator is `15/15`, and backup is unchanged. Performance comparison remains explicitly invalid because Task 1 omitted quality/sampling-window identity. Known external Search LMDB noise remains disclosed; after cursor `8196`, the fresh Console slice is empty. No process was killed or restarted; Task 8 was not entered.

**Task 7 final read-only re-review (2026-08-29): PASS.** No blocking findings remain. The independent review confirmed the four visible ASCII `[!]` captures and pixel counts, the Edit `85/85` -> Play `27/27` -> captures -> H0 -> two-build ordering, matching canonical hash `B5609076A9E52A8CEFEAF2231AE623D5E14319DBD8371CB33AD668D04EC0E347`, complete Ending/five-zone UI lifecycle cleanup, and active CC0 `8/8`. Performance comparability and historical Search LMDB noise remain non-blocking disclosed limitations. Task 7 is complete; Task 8 is active.

---

### Task 8: Complete Player/AI Regression, Freeze Evidence, and Hand Back to Stage 8.10

**Files:**
- Create: `reports/phase8/open-tropical-battlefield/validation.md`
- Create: `reports/phase8/open-tropical-battlefield/manifest.json`
- Modify: `docs/PROJECT_PLAN.md`
- Modify: `docs/superpowers/plans/2026-08-28-phase-8-dino-attack.md`
- Modify: this plan's checkboxes and evidence blocks.

**Interfaces:**
- Consumes: the completed map, current Human mode, existing AI diagnostic interfaces, and Tasks 1–7 evidence.
- Produces: a frozen scene/map contract ready for TASK-8.10 Human/AI same-scene integration.

- [x] **Step 1: Run all automated Unity tests**

Run the strongest connected-Editor EditMode and PlayMode suites. Expected: all tests pass; no new Error, Exception, Assert, NaN, missing reference, unit cleanup, or NavMesh-off-mesh warning attributable to the redesign.

- [x] **Step 2: Execute the complete player matrix**

Verify Setup/Running placement in all five zones, Wait, cancel, insufficient food, outside bounds, overlap, no ground, no NavMesh, UI blocking, kill/wall refunds, victory at the plaza, failure, Ending lock, enemy repairs, next round, scene reload, and repeated Home/reset.

- [x] **Step 3: Execute map-specific runtime acceptance**

Verify all 15 dinosaur-zone paths, five terrain ratios, deterministic defense presets, static-object movement detours, non-occluded attacks through each representative obstacle type, and no hidden fixed routes.

- [x] **Step 4: Run a bounded longevity test**

Run at least 20 complete rounds/episodes or the same stronger duration required by TASK-8.10. Track object counts, subscriptions, material instances, speed state, NavMesh updates, memory, Console warnings, worker ports, and process cleanup.

- [x] **Step 5: Produce the frozen manifest**

Record Unity version, `Dinos.unity` SHA-256, key script/Prefab/material hashes, Packages lock hash, five zone bounds/IDs, terrain constants, camera framing values, map seed/presets, Kenney manifest hash, test results, screenshots, and known limitations.

- [x] **Step 6: Self-review against all ten design acceptance criteria**

Map each criterion in the spec to exact test IDs, commands, screenshots, or measured outputs. Any missing mapping keeps redesigned 8.9 incomplete and TASK-8.10 blocked.

- [x] **Step 7: Update the single source of truth**

Only after all hard checks pass, mark redesigned 8.6–8.9 complete, set TASK-8.10 to `进行中`, add dated validation evidence and artifact paths to `docs/PROJECT_PLAN.md`, and preserve the old first-pass evidence as superseded history.

- [x] **Step 8: Final path-scoped review**

Run:

```powershell
git diff --check
git -C dino-attack diff --check
git status --short
git -C dino-attack status --short
```

Review only files attributed to this redesign. Do not stage or commit without explicit user authorization.

**Task 8 final evidence (2026-08-29): PASS.** Focused EditMode `37/37`, original focused PlayMode `27/27`, focused longevity `1/1`, full EditMode `85/85`, and full PlayMode `28/28` all passed; cursor `10261 -> 10934` contained zero fresh errors. The validation-only fixture completed 20 real `Dinos` scene reload episodes (10 wins/10 losses, every zone four times), the full state cycle, real win/loss/next-round UI, surviving-dino refund, dead-dino no-refund, and recorded per-episode object/subscription/material/speed/NavMesh/memory/Console evidence. Windows64 build `build_6bc19e48b9a2` succeeded with zero errors; H0/H1/H2 are identical at `DF751103EF721897F5F753CB1AA0EAA7758A2A52B56AE2A6D3935504B86173E5`, with no later EditMode run. Frozen evidence is in `reports/phase8/open-tropical-battlefield/validation.md`, `manifest.json`, and `longevity-20-episodes.json`. Task 8/TASK-8.9 is complete; TASK-8.10 is active.

**TASK-8.10 freeze handoff (2026-08-29): PASS.** The 486 successful-build warnings are frozen as 485 Unity AI Inference/Sentis shader diagnostics plus one unused optional RuntimePipelineManager/Pipeline-disabled warning. The manifest now includes verified hashes for two defense Prefabs, two UnitSO assets, and seven active materials. A graphical D3D11 Player remained alive for 12 seconds, ran the Dinos opening defense turn, then exited with code 0 after standard `WM_CLOSE`; its log has zero critical/runtime-warning matches. TASK-8.10 is complete and TASK-8.11 is active. Evidence: `task-8.10-final-freeze.md` and `task-8.10-player-smoke.log`.

## Plan Exit Criteria

- The approved five-zone open tropical battlefield is implemented in the same `Dinos.unity` and all design acceptance criteria have evidence.
- Old three-route/four-zone assets are not active gameplay structure; their historical reports remain available.
- Terrain speed works at exact approved ratios and returns to base speed correctly.
- Buildings and terrain alter movement but never attack eligibility.
- Full-island Home framing and RTS controls pass all four aspect ratios.
- Human gameplay is stable, and TASK-8.10 can resume against one frozen scene hash.
- `docs/PROJECT_PLAN.md`, the phase 8 plan, this plan, and evidence manifests agree on the single active task and completion state.
