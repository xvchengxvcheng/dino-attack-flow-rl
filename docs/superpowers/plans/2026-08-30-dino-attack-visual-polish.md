# Dino Attack Visual Polish Implementation Plan

> **Execution update (2026-08-30):** The visual implementation, canonical NavMesh rebuild, warm-neutral lighting correction, Start-frame removal, focused Unity regressions, screenshots, manifest, and stopped/clean Editor handoff are complete. The final scene is `611E5FE4...3DC8`, frozen gameplay signature `D96A805D...26E`, and the validator reports `15/15` zone/agent plus `6/6` defender paths with zero visual-isolation violations. The authored post-kill cooldowns were retained; a real-scene regression confirms dinosaurs resume by reselecting or damaging another target. Task 8 remains open until TASK-8.11 Task 10 supplies its formal Player/reload/four-process evidence. See `reports/phase8/visual-polish/validation.md` and `manifest.json`.

> **For Codex:** REQUIRED SUB-SKILL: Use `superpowers:executing-plans` to implement this plan task-by-task. Before any Unity operation, use `unity-cli` or `unity-workbench:unity-mcp-workflow` to verify that exactly one connected Editor owns the project. Use `unity-workbench:unity-feature-implementation` for the implementation and `unity-workbench:unity-build-validation` before claiming completion.

**Goal:** Upgrade `Dinos.unity` into a high-fidelity stylized Low-poly tropical canyon battlefield for Human play while proving that deployment, navigation, combat, village defenses, UI behavior, and the frozen TASK8.11 training protocol are unchanged.

**Architecture:** Keep gameplay roots frozen and extend only the deterministic `Art Pass`, rendering assets, post-processing profile, and USS presentation. A focused visual-polish builder owns fixed decoration tables and lighting/material parameters; the existing scene builder invokes it, and the validator rejects visual roots containing gameplay-affecting components before any save. The work begins only after TASK8.11 Task 4 saves its training installation and explicitly hands over Unity Editor exclusivity, then returns the Editor for TASK8.11 Task 10 to perform the combined final freeze.

**Tech Stack:** Unity 6 / URP, C#, Unity Test Framework, UI Toolkit UXML/USS, deterministic Editor scene construction, existing Stage8 shaders and imported Low-poly/PolyHaven assets.

**Project tracking ID:** `TASK-8.11-ART-PASS` (a coordinated visual handoff inside TASK8.11, not the already-reserved main-plan `TASK-8.12` Gaussian PPO stage).

---

## 0. Scope, Ownership, and Non-Negotiable Invariants

### Execution gate

- TASK8.11 Task 4 owns `Dinos.unity` until it has installed `/Training/FormalTrainingEnvironment`, saved the scene, recorded the post-install SHA-256, completed its focused Human/training tests, and explicitly released the Unity Editor.
- This plan may be reviewed while Task 4 runs, but Tasks 1–8 below must not open a second Editor, trigger import/compile/tests, or modify `Dinos.unity`, `OpenTropicalBattlefieldSceneBuilder.cs`, `OpenTropicalBattlefieldValidator.cs`, `RuntimeStyles.uss`, `DefaultVolumeProfile.asset`, or Stage8 materials until handoff.
- After Task 7, return Editor ownership to TASK8.11. TASK8.11 Task 10 is the single owner of the final Player build, 20 reloads, four-process rollout, and combined scene/protocol hash.
- If Task 4 cannot pause, wait until TASK8.11 Task 10 is complete; never allow simultaneous Unity writes.

### Frozen gameplay/protocol facts

- Preserve all Z1–Z5 A/B/C/D vertices, triggers, legal placement, `(u,v)` mapping, gaps, and surface multipliers `1.10/1.00/0.85/0.80/0.70`.
- Preserve all NavMesh surfaces/data/agents, Camera Bounds/Home behavior, eight house gameplay roots, six wall slots, three selected walls, wall guards, `3 Archer + 2 Mage`, combat/lifecycle/restart, and 50-second natural defeat.
- Preserve TASK8.11 protocol `dino_attack_structured_set_v1`: stream shapes `(3)`, `(6,5)`, `(8,7)`, `(8,6)`, `(10,7)`, total `207`, 4D continuous action, reward semantics, terminal/truncated rules, seeds, Agent IDs, and exact `--dino-training` flag.
- Preserve every UXML `name`, every `RuntimeUI.cs` query/callback/hotkey/state transition, Start/Victory/Defeat/Restart behavior, and every health-bar behavior.
- Add no deployment dust, hit flash, death dissolve, footsteps, water splash, or other combat VFX.
- Add no Collider, Trigger, Rigidbody, NavMeshObstacle, NavMeshModifier, NavMeshModifierVolume, NavMeshAgent, Agent, health/combat/deployment component, or custom gameplay `MonoBehaviour` below visual roots.
- Do not edit `Packages`, `ProjectSettings`, training configs, algorithms, checkpoints, existing validation artifacts, or historical scenes.
- Do not stage, commit, stash, reset, checkout, clean, or overwrite unrelated dirty work. Every task ends with a diff review checkpoint instead of a commit.

### Exact visual direction

- Clear golden afternoon: warm directional key, cool readable ambient fill, bright cyan-blue sky, restrained linear distance fog, warm ochre cliffs, teal shallow water, and the village/plaza as the focal point.
- Human mode receives dense but non-blocking visual dressing. `--dino-training` continues to disable Renderer/Light/Volume/audio at component level without disabling gameplay roots.
- No fixed FPS, resolution, or throughput target. Capture same-condition before/after timing only to identify obvious regressions.

## 1. Planned File Surface

### Create

- `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldVisualPolishBuilder.cs`
- `dino-attack/Assets/LlamAcademy/Dinos/Stage8/Materials/GoldenAfternoonSky.mat` (created through `AssetDatabase`, not hand-written YAML)
- `reports/phase8/visual-polish/baseline.json`
- `reports/phase8/visual-polish/validation.md`
- `reports/phase8/visual-polish/manifest.json`
- `reports/phase8/visual-polish/before-home.png`
- `reports/phase8/visual-polish/before-running.png`
- `reports/phase8/visual-polish/after-home.png`
- `reports/phase8/visual-polish/after-running.png`

Unity creates corresponding `.meta` files during the single-owner import. Do not manufacture GUIDs outside Unity.

### Modify

- `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`
- `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`
- `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeStyles.uss`
- `dino-attack/Assets/DefaultVolumeProfile.asset` through Unity serialization only
- Existing assets under `dino-attack/Assets/LlamAcademy/Dinos/Stage8/Materials/` through the deterministic builder
- `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity` through Unity Editor only
- `docs/PROJECT_PLAN.md` after validation evidence exists

### Explicitly read-only

- `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeUI.uxml`
- `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeUI.cs`
- `dino-attack/Assets/LlamAcademy/Dinos/Stage8/Shaders/DinoAttackStylizedGround.shader`
- `dino-attack/Assets/LlamAcademy/Dinos/Stage8/Shaders/DinoAttackShallowWater.shader`
- TASK8.11 Runtime/Core/Sensor/Agent/installer/build files
- All gameplay prefabs, NavMesh data, `Packages`, and `ProjectSettings`

The existing shaders already expose the required color, normal, macro-scale, smoothness, flow, foam, and transparency controls. Do not add shader features unless a failing visual contract demonstrates a missing capability; that would require a separate design amendment.

## 2. Required Interfaces

Add the following Editor-only API in `OpenTropicalBattlefieldVisualPolishBuilder.cs`:

```csharp
namespace LlamAcademy.Dinos.Map.Editor
{
    internal sealed class VisualPolishAudit
    {
        public int ColliderCount;
        public int TriggerCount;
        public int RigidbodyCount;
        public int NavMeshComponentCount;
        public int AgentCount;
        public int GameplayBehaviourCount;
        public bool Z3DecorationEmpty;
        public bool RequiredRootsPresent;
        public string[] ViolatingPaths = Array.Empty<string>();
        public bool IsIsolated => RequiredRootsPresent && Z3DecorationEmpty &&
            ColliderCount == 0 && TriggerCount == 0 && RigidbodyCount == 0 &&
            NavMeshComponentCount == 0 && AgentCount == 0 && GameplayBehaviourCount == 0;
    }

    internal static class OpenTropicalBattlefieldVisualPolishBuilder
    {
        internal static void Build(
            Scene scene,
            Transform world,
            Transform battlefield,
            Transform terrainRoot,
            Transform village,
            Transform outerScenery);

        internal static VisualPolishAudit Audit(Transform battlefield);
        internal static string BuildFrozenGameplaySignature(Scene scene);
    }
}
```

Extend `OpenTropicalBattlefieldReport` with:

```csharp
public bool VisualLayerIsolated;
public bool Z3DecorationEmpty;
public int VisualColliderCount;
public int VisualTriggerCount;
public int VisualNavMeshComponentCount;
public int VisualAgentCount;
public int VisualGameplayBehaviourCount;
public string[] VisualViolatingPaths = Array.Empty<string>();
public string FrozenGameplaySignature;
```

`BuildFrozenGameplaySignature` must hash stable, sorted records for the frozen roots only: hierarchy path, active state, world position/rotation/scale, component type names, zone vertices/surface/speed, house/wall/guard gameplay roots, camera bounds, NavMesh references, TASK8.11 BehaviorParameters/sensor/action configuration, and UXML behavioral IDs. It must exclude all `Art Pass` descendants, Renderer/material/Light/Volume fields, and USS bytes.

## Task 1: Acquire Editor Handoff and Freeze the TASK8.11 Baseline

**Files:**

- Read: `docs/PROJECT_PLAN.md`
- Read: `docs/superpowers/plans/2026-08-30-dino-attack-structured-parallel-training.md`
- Read: TASK8.11 Task 4 report under `.superpowers/sdd/2026-08-30-dino-attack-structured-parallel-training/`
- Create: `reports/phase8/visual-polish/baseline.json`
- Create: `reports/phase8/visual-polish/before-home.png`
- Create: `reports/phase8/visual-polish/before-running.png`

- [ ] Confirm TASK8.11 Task 4 is complete and its report includes the post-install scene hash, exact Human-mode pass, training-mode pass, five sensors, and continuous action size `4`.
- [ ] Confirm the other task has stopped all Unity imports/tests/saves and explicitly released the connected Editor. If not, stop this plan here without opening Unity.
- [ ] Record `git status --short` for the scoped files, the scene SHA-256, `DefaultVolumeProfile.asset` SHA-256, Runtime UI C#/UXML/USS SHA-256 values, all Stage8 material/shader hashes, Unity version, active scene, and Console cursor.
- [ ] In the connected Editor, ensure Play Mode is stopped and save no changes. Capture the current `OpenTropicalBattlefieldReport.Summary`, its structural signature, the TASK8.11 protocol version/shapes/action size, the active scene root list, and the exact Task 4 scene hash into `baseline.json`.
- [ ] Capture `before-home.png` at the exact Home camera state and `before-running.png` after Start with the same Game view aspect and quality level to be reused after implementation. Record the aspect and quality names in `baseline.json`; do not define them as global acceptance targets.
- [ ] Warm the Human scene for 60 frames, then record 300 consecutive CPU main-thread and GPU frame samples using the same Editor/Game view conditions. Store median and p95 values as observations, not pass/fail thresholds.
- [ ] Review the baseline diff. This task must not change Unity assets or the scene.

**Exit:** A reproducible pre-art baseline tied to the Task 4 scene hash exists, and Unity ownership is unambiguous.

## Task 2: Add RED Tests for Visual Isolation and Gameplay Signature Preservation

**Files:**

- Modify: `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`

- [ ] Add an Editor test named `VisualPolishRoots_ArePresentIsolatedAndKeepZ3Empty` that requires `Lighting & Atmosphere`, `Terrain Dressing`, existing `Village Details`, existing `Canyon Rim`, and `Canyon Rim/Distant Scenery`; asserts zero forbidden components and zero descendants below `Art Pass/Z3 Riverbank`.

```csharp
VisualPolishAudit audit = OpenTropicalBattlefieldVisualPolishBuilder.Audit(battlefield);
Assert.That(audit.RequiredRootsPresent, Is.True);
Assert.That(audit.Z3DecorationEmpty, Is.True);
Assert.That(audit.ColliderCount, Is.Zero);
Assert.That(audit.TriggerCount, Is.Zero);
Assert.That(audit.RigidbodyCount, Is.Zero);
Assert.That(audit.NavMeshComponentCount, Is.Zero);
Assert.That(audit.AgentCount, Is.Zero);
Assert.That(audit.GameplayBehaviourCount, Is.Zero,
    string.Join("\n", audit.ViolatingPaths));
```

- [ ] Add `Builder_VisualRepairPreservesFrozenGameplaySignatureAndBecomesByteStable`. Capture the frozen signature, delete only `Art Pass/Terrain Dressing/Z2 Meadow/Meadow Grass 01`, run `Build(saveScene:false)`, assert the visual is repaired, assert the gameplay signature is byte-identical, then run the builder again and assert the scene serialization is unchanged.
- [ ] Add `RuntimeUiVisualPolish_PreservesBehavioralElementContract`. Clone `RuntimeUI.uxml` and require the exact ID set: `start-button`, `wave-text`, `dino-button-container`, `resources-container`, `win-lose-text`, `deployment-feedback`, `terrain-feedback`, `phase-status`, `food-status`, `threat-status`, `result-panel`, `session-summary`, `restart-button`, and `logo-container`.
- [ ] Add a PlayMode test named `VisualPolish_HumanAndTrainingPresentationModesDoNotChangeGameplayRoots`. In Human mode require the Art Pass Renderer/Light/Volume components to be enabled as authored; in exact `--dino-training` simulation require presentation components disabled while deployment zones, colliders, NavMesh, RoundManager, Agent/sensors, houses, walls, and guards remain active/configured.
- [ ] Run only the four new tests and record the RED reasons. Expected RED: missing new builder/audit API and missing new responsibility roots. Existing gameplay tests must not be edited merely to make RED pass.
- [ ] Review the diff; verify no production behavior, scene, material, UXML, or USS has changed.

**Exit:** The failure proves the requested visual layer is absent, while invariants are executable rather than prose-only.

## Task 3: Implement Deterministic Visual Builder, Audit, and Validator Integration

**Files:**

- Create: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldVisualPolishBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`

- [ ] Implement `Audit` before adding decoration. Audit every descendant of `Art Pass`; count enabled colliders and triggers separately, Rigidbody, `NavMeshObstacle`, `NavMeshModifier`, `NavMeshModifierVolume`, `NavMeshSurface`, `NavMeshAgent`, ML-Agents `Agent`, and custom gameplay MonoBehaviours. Allow only render-only Unity components such as Transform, Renderer, MeshFilter, Light, and `UnityEngine.Rendering.Volume`; report every violation with full hierarchy path.
- [ ] Implement `BuildFrozenGameplaySignature` using invariant-culture numbers rounded to `F4`, sorted paths/types, and SHA-256. Include TASK8.11 formal training root configuration and exclude all fields the art pass is permitted to change.
- [ ] Add `OpenTropicalBattlefieldVisualPolishBuilder.Build(...)` at the end of the existing `BuildArtPass(...)`, before final validation/save. Do not move or rename current Z1–Z5, `Village Details`, or `Canyon Rim`, because existing validators and reports depend on them.
- [ ] Create the responsibility nodes deterministically: `Art Pass/Lighting & Atmosphere`, `Art Pass/Terrain Dressing` with children `Z1 Ruins`, `Z2 Meadow`, `Z4 Palms`, `Z5 Rocks`, and `Art Pass/Canyon Rim/Distant Scenery`. Keep `Art Pass/Z3 Riverbank` child count at zero and do not create a Terrain Dressing Z3 child.
- [ ] Make every visual prefab pass through a single sanitization method that destroys Collider/Rigidbody/NavMesh/custom MonoBehaviour components, assigns Layer 0, sets static flags only for rendering/batching, and controls shadow casting. It must reject a prefab if forbidden components remain.
- [ ] Extend `ValidateLoadedScene()` to populate the new visual fields and require `VisualLayerIsolated && Z3DecorationEmpty` in `IsValid`. Add counts and violating paths to `Summary` before any builder save.
- [ ] Run the new Editor tests. Expected: the isolation API tests compile; repair/signature stability remains RED until Tasks 4–5 create the authored visual content.
- [ ] Review the diff and confirm no gameplay assembly, training file, scene, or UXML behavior was modified.

**Exit:** There is one deterministic visual writer and one independent pre-save isolation audit.

## Task 4: Author Golden-Afternoon Lighting, Sky, Volume, Terrain, and Water

**Files:**

- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldVisualPolishBuilder.cs`
- Modify through Unity serialization: `dino-attack/Assets/DefaultVolumeProfile.asset`
- Create through `AssetDatabase`: `dino-attack/Assets/LlamAcademy/Dinos/Stage8/Materials/GoldenAfternoonSky.mat`
- Modify through builder: existing Stage8 materials
- Read-only: both Stage8 shaders

- [ ] Add exact lighting constants and apply them to the scene's existing `RenderSettings.sun`; fail on zero or multiple authored main suns rather than creating a duplicate. Use rotation `(42, -28, 0)`, color linearized from sRGB `#FFD39A`, intensity `1.25`, soft shadows, shadow strength `0.72`, bias `0.08`, and normal bias `0.45`.
- [x] ~~Configure cold-blue trilight/fog values.~~ **已取代：** 用户复核要求去除房屋和守卫蓝色覆层；当前 builder 固定使用暖中性 ambient sky/equator/ground/fog，只有浅水保持青绿色，不新增运行时灯光脚本。
- [ ] Create/reuse `GoldenAfternoonSky.mat` with `Skybox/Procedural`: sun disk high quality, sun size `0.035`, atmosphere thickness `0.85`, sky tint `#6FA9D2`, ground color `#6B4A36`, exposure `1.05`; assign through `RenderSettings.skybox`.
- [ ] Configure existing Volume overrides through `SerializedObject`/typed URP components, never YAML text editing: ACES tonemapping; White Balance temperature `+12`, tint `+2`; Color Adjustments post-exposure `+0.15`, contrast `+12`, saturation `-2`; Bloom threshold `1.10`, intensity `0.22`, scatter `0.55`; Vignette intensity `0.08`, smoothness `0.35`. Keep Depth of Field, Motion Blur, Chromatic Aberration, Film Grain, Lens Distortion, and Screen Space Lens Flare disabled. Do not edit pipeline/renderer/ProjectSettings assets.
- [ ] Retune only existing material parameters via the builder:

```text
CanyonRock:          tint #C97648, detail 2.8, macro 52, normal 0.34, smooth 0.04..0.30
VillageSand:         tint #E7C58B, detail 3.8, macro 58, normal 0.20, smooth 0.04..0.28
TropicalGrass:       tint #75975A, detail 3.2, macro 54, normal 0.30, smooth 0.04..0.25
NeutralTropicalGround tint #B59165, detail 4.5, macro 66, normal 0.24, smooth 0.04..0.24
RiverMud:            tint #8F775D, detail 3.4, macro 44, normal 0.28, smooth 0.06..0.42
RiverbedSand:        tint #8F9B75, detail 2.6, macro 42, normal 0.18, smooth 0.04..0.22
ShallowWaterTeal:    base #238D8F alpha .72, deep #175B66 alpha .82,
                     foam #9BCDC1 alpha .52, normal .18, tiling 3.8/5.3,
                     pan (.014,.008)/(-.009,.012), foam width .045,
                     fresnel 4.2, smoothness .74
```

- [ ] Keep the water surface and existing riverbed as the only visible objects inside Z3. Add no bridge, stone, bank prop, plant, particle, reflection camera, refraction, Scene Color, or displacement.
- [ ] Run material/shader isolation tests and inspect Game view for pink materials, transparency sorting, shadow crushing, and fog obscuring zone/village edges. If any occurs, correct parameters within the listed features; do not change shaders or gameplay geometry.
- [ ] Review scoped asset diffs and generated material dependencies. No `.shader`, Renderer asset, Package, or ProjectSettings diff is allowed.

**Exit:** The scene has a coherent golden-afternoon grade and natural teal water using existing rendering capabilities only.

## Task 5: Build High-Density, Non-Blocking Terrain, Village, and Distant Dressing

**Files:**

- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldVisualPolishBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Modify through Unity Editor: `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`

- [ ] Define immutable `DecorationSpec` records with name, existing asset path, position, yaw, scale, material policy, cast-shadow flag, and responsibility root. Use no runtime randomization; fixed arrays are the source of truth.
- [ ] Preserve existing Z1/Z2/Z4/Z5 landmark children and add these deterministic density bands under `Terrain Dressing`: Z1 `8` rock/grass details, Z2 `12` grass/fern/stone details, Z4 `10` palm/cycad/grass details, Z5 `8` rock/grass details. Use only existing imported assets:
  - `Assets/ThirdParty/Kenney/MiniForest/Models/rocks-low.fbx`
  - `Assets/ThirdParty/Kenney/MiniForest/Models/rocks-ramp.fbx`
  - `Assets/ThirdParty/Kenney/MiniForest/Models/stones.fbx`
  - `Assets/ThirdParty/Kenney/MiniForest/Models/patch-grass.fbx`
  - Tribal Pack `Grass.prefab`, `Grass_Long.prefab`, `Fern_Prehistoric.prefab`, `Cycad_Triple_Prehistoric.prefab`, `Bush_Cycad_Prehistoric.prefab`, and `Palm_Prehistoric_Small.prefab`.
- [ ] Keep all dressing at least `2.5 m` from the nearest frozen house/wall/guard root, at least `1.5 m` from deployment boundary line renderers, and outside Z3's quadrilateral. The builder must validate these distances before instantiation and throw before save if a spec violates them.
- [ ] Add exactly `8` village details to existing `Village Details`: two `Vase_Tribal_Small`, one `Vase_Tribal_Big`, one `CookingPot_Tribal`, one `Table_Stone_Tribal`, one `Totem_Dinosaur_Tribal`, and two `Flag_Stand_Tribal` instances. Reuse the existing two flags rather than duplicating them. Strip every gameplay component and keep details outside house health-bar sightlines, wall fronts, and the plaza-to-zone traffic lanes.
- [ ] Preserve all eight hut gameplay roots and existing Tribal Hut visual children. Material unification may change Renderer materials only; do not change hut visual world bounds by more than `0.02 m`, and do not touch their gameplay root transforms, colliders, health, target selection, death beams, or meat return.
- [x] ~~Add five distant silhouettes under `Canyon Rim/Distant Scenery`.~~ **已取代：** 用户 2026-08-30 要求去除五座外围山；builder 现在确定性保持该职责根为空。
- [x] 保留既有北侧 Rim 主体和空的 `Navigation Barriers`，不再使用远山 yaw/scale 数组；A 镜头固定为中心 `(-3.5,-2.5,-12)`、FOV `58`、FollowOffset `(0,82,38)`、ZoomSensitivity `0.22`。
- [ ] Run the isolation audit before saving. Then run the builder twice with `saveScene:false`; the second run must make no scene/material changes. Only then save once through Unity.
- [ ] Visually inspect Home and a running battle: village/plaza must be the focal point; Rim must frame rather than darken the battlefield; unit silhouettes, wall guards, house bars, deployment feedback, and main traffic lanes must remain readable.
- [ ] Review scene and builder diffs. Any frozen gameplay signature change blocks this task.

**Exit:** Human mode has denser, layered scenery without any physics, navigation, combat, deployment, or training contribution.

## Task 6: Restyle UI with USS Only

**Files:**

- Modify: `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeStyles.uss`
- Read-only: `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeUI.uxml`
- Read-only: `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeUI.cs`

- [ ] Record and later compare the UXML and C# SHA-256 values from Task 1. They must remain byte-identical.
- [ ] Change only USS presentation tokens to the warm wood/charcoal/sand-gold palette: `--light #F4E8D0`, `--dark #211C19`, `--green #5E9A62`, `--red #B54B3E`, accent `#D6A85F`, panel `rgba(28,23,20,.78)`, feedback `rgba(22,33,31,.80)`.
- [ ] Reduce map occlusion without changing behavior: bottom bar height `124px`, top bar `88px`, resource width `340px`, status panel minimum width `280px`, panel corner radius `12px`; preserve absolute anchoring and picking modes.
- [ ] Set information hierarchy: wave `54px`, resources `40px`, status `22px`, deployment feedback `23px`, terrain feedback `21px`, session summary `25px`, result title `82px`. Keep the existing Labrada font and text values.
- [ ] Restyle Start and Restart to `280×76` and `240×64` respectively, with sand-gold borders, charcoal translucent fill, readable enabled/active scale transitions, and no selector/state changes.
- [ ] Keep `result-panel` UXML default `display:none`, preserve `.hidden/.visible/.in/.out`, preserve all element names, and add no new element or C# query.
- [ ] Run `RuntimeUiVisualPolish_PreservesBehavioralElementContract` plus existing `RuntimeUiContainsDedicatedTerrainFeedbackThatDoesNotPickWorldInput` and `SceneDeploymentLifecycle_WiresFiveZonesHudInvalidFeedbackAndEndingCleanup`.
- [ ] Check one compact Game view only for overlap and clickable Start/Restart; do not restore the retired four-resolution matrix and do not create a fixed-resolution gate.
- [ ] Verify UXML and C# hashes remain identical to baseline; review the single USS diff.

**Exit:** UI appearance is cohesive and less obstructive, with zero behavior/source-structure change.

## Task 7: Focused Regression, Visual Evidence, and Editor Handoff

**Files:**

- Modify: `reports/phase8/visual-polish/validation.md`
- Create: `reports/phase8/visual-polish/after-home.png`
- Create: `reports/phase8/visual-polish/after-running.png`
- Create: `reports/phase8/visual-polish/manifest.json`

- [ ] Trigger one fresh Unity compilation. Record `failed=false`, exact compiler errors/warnings introduced by this task, and a fresh Console cursor; completion requires zero new errors.
- [ ] Run this focused EditMode set:
  - `LoadedDinosScene_SatisfiesApprovedGrayboxContract`
  - `ArtPassHasFiveDistinctNonBlockingLandmarksAndSafeMaterials`
  - `VisualPolishRoots_ArePresentIsolatedAndKeepZ3Empty`
  - `Builder_VisualRepairPreservesFrozenGameplaySignatureAndBecomesByteStable`
  - `RuntimeUiVisualPolish_PreservesBehavioralElementContract`
  - `EveryDefensePreset_HasCompleteGameplayAndDefenderSlotMatricesAndRestoresCanonicalScene`
- [ ] Run this focused PlayMode set:
  - TASK8.11 Task 4 Human-mode presentation case
  - TASK8.11 Task 4 exact-training-mode case
  - `StructuredObservations_RealSceneProducesFiveFiniteStableStreams`
  - `TrainingTask3_RegionActionsWaitAndDinoCapUseRealZones`
  - `TrainingTask3_FortySecondDefeatIsNaturalAndQueuesNextEpisodeSeed`
  - `SceneDeploymentLifecycle_WiresFiveZonesHudInvalidFeedbackAndEndingCleanup`
  - `SceneDeploymentZones_MapRelativeCoordinatesAndRejectNeutralGaps`
  - `ThreeGameplayPrefabs_MeasureRealEqualLengthTraversalAcrossSceneSurfaceVolumes`
- [ ] Run `OpenTropicalBattlefieldValidator.ValidateLoadedScene()` and require: visual forbidden counts `0`, Z3 empty, five zones/surfaces, eight houses, six wall slots, configured ground defense, `15/15` complete zone/agent paths, `6/6` defender paths, no active legacy strategy, and an unchanged frozen gameplay signature.
- [ ] Run canonical builder save once, record H0; run it twice more without manual edits and record H1/H2. Require `H0 == H1 == H2` and no NavMeshData replacement.
- [ ] Capture `after-home.png` and `after-running.png` using Task 1's exact camera/aspect/quality/state. Record a four-image checklist covering focal hierarchy, zone readability, shadow readability, Z3 emptiness, material errors, unit/health-bar visibility, and UI occlusion.
- [ ] Repeat the 60-frame warmup plus 300 CPU/GPU samples. Report median/p95 before/after and explain any material change; do not convert the numbers into an FPS or resolution completion gate.
- [ ] If the Task 4 formal Player already exists, launch one exact `--dino-training` episode and record that Art Pass Renderer/Light/Volume components are disabled and that decisions still arrive. Do not build a replacement Player, run 20 reloads, or run the four-process rollout here.
- [ ] Write `manifest.json` with scene, gameplay signature, volume, sky, Stage8 material/shader, RuntimeUI C#/UXML/USS, TASK8.11 protocol, and existing Player hashes. Write exact commands/test counts/results and known limitations in `validation.md`.
- [ ] Stop Play Mode, save only intended assets, verify fresh Console has zero new errors, then explicitly release Unity Editor ownership back to TASK8.11.

**Exit:** The art pass is independently validated and the Editor is cleanly returned for the combined training freeze.

## Task 8: Combined TASK8.11 Freeze and Project Plan Closure

**Files:**

- Read/update after TASK8.11 Task 10: `reports/phase8/visual-polish/validation.md`
- Modify: `docs/PROJECT_PLAN.md`

- [ ] TASK8.11 Task 10 runs its planned formal Player build, protocol check, 20 scene reloads, one four-process approximately 4096-decision rollout, natural terminal/truncated accounting, and failure recovery against the final visually polished scene. The visual task does not duplicate these runs.
- [ ] Copy only the final Task 10 scene/Player/protocol hashes and pass/fail evidence into the visual validation cross-reference. If Task 10 fails, leave the visual task `进行中` with the exact combined blocker; do not weaken art isolation or training assertions.
- [ ] Re-read `docs/PROJECT_PLAN.md`, append a new decision/status entry for the approved visual-polish task, mark it complete only if Tasks 1–8 have evidence, and identify the four screenshot/report paths.
- [ ] Run `git diff --check` on the plan/report/code/text files and inspect `git status --short` for unintended files. Do not stage or commit.
- [ ] Present the accumulated scoped diff and the four screenshots to the user for final Human visual review. User taste feedback may request another visual iteration, but it must not silently reopen gameplay scope.

**Exit:** One final scene supports both Human visuals and formal training, with unified hashes and no gameplay/protocol drift.

## Failure and Rollback Rules

- A changed frozen gameplay signature is an immediate stop. Revert only this task's specific visual references/parameters through the deterministic builder or Unity Undo; never use repository-wide reset/checkout/clean.
- A forbidden component below Art Pass is an immediate pre-save failure with a full hierarchy path.
- If lighting/post-processing causes illegible shadows or obvious timing regression, reduce in this order: Bloom/Vignette strength, shadow contribution, distant shadow casting, decoration density, water normal complexity. Do not change gameplay roots, camera bounds, simulation speed, training time scale, or protocol.
- If transparent water sorts incorrectly, adjust only existing material render-state/property values supported by `DinoAttackShallowWater.shader`; do not add reflection cameras, refraction, Scene Color, or interactive ripples.
- If UI behavior changes, restore UXML/C# baseline bytes and limit the fix to USS.
- If TASK8.11 changes the scene after handoff, recapture the baseline and rerun the frozen-signature/visual suite; never merge two independently saved `Dinos.unity` files by YAML.

## Execution Review Checkpoints

1. After Task 1: user can inspect the baseline and ownership handoff.
2. After Task 3: reviewer confirms the audit/signature architecture before any art save.
3. After Task 5: user inspects first complete scene screenshots before UI polish.
4. After Task 7: reviewer inspects focused regression evidence and final screenshots.
5. After Task 8: user performs final Human visual review; no automatic Git commit is made at any checkpoint.
