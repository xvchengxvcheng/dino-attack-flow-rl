# Dino Attack Environment Art Upgrade Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Upgrade the frozen quadrilateral Dino Attack map into a warm stylized low-poly tropical canyon with textured terrain, Tribal Hut village visuals, lightweight shallow water, and a modular rock Rim without changing gameplay semantics.

**Architecture:** Keep the existing quadrilateral zones, physical floor, triggers, deployment service, NavMesh, speed volumes, graybox building colliders, and attack rules as the gameplay layer. Extend the deterministic `OpenTropicalBattlefieldSceneBuilder` with an isolated visual layer whose renderers use project-local URP shaders/materials and whose imported models contain no active Collider, NavMeshObstacle, Rigidbody, or gameplay component; extend the validator and tests so canonical early return is possible only when both layers match.

**Tech Stack:** Unity 6000.3.22f1, Universal Render Pipeline, C# Editor tooling, ShaderLab/HLSL, NUnit EditMode/PlayMode tests, Unity NavMesh, deterministic scene builder, SHA-256 manifests.

**Spec:** `docs/superpowers/specs/2026-08-29-dino-attack-environment-art-upgrade-design.md`

## Global Constraints

- Do not change the five zone A/B/C/D vertices, bilinear `(u,v)` mapping, deployment legality, `1.10/1.00/0.85/0.80/0.70` speed multipliers, NavMesh semantics, attacks, 6D action, 504D observation, reward, or episode lifecycle.
- Do not modify `Packages/`, `ProjectSettings/`, official algorithm repositories, checkpoints, or frozen Python environments.
- Do not import `.blend`, GLB, OBJ, MTL, displacement maps, or entire source packs.
- Every decorative house, rock, plant, ruin, and visible Rim instance must have zero active Collider, NavMeshObstacle, Rigidbody, and gameplay MonoBehaviour components.
- Preserve graybox building colliders and gameplay placement, but disable their renderers after Tribal Hut visuals are present.
- The shallow-water shader must not use Scene Depth, Scene Color, GrabPass, refraction, reflection, RenderTexture, an extra camera, vertex displacement, or interactive ripples.
- Use `apply_patch` for source/document edits and the connected Unity Editor/builder for scene and asset mutation; never hand-edit `Dinos.unity` YAML or `.meta` files.
- Treat both outer and nested repositories as dirty user work. Stage only explicit task files after scoped review; never reset, clean, stash, or bulk-add.

## File Structure

- Create `dino-attack/Assets/ThirdParty/PolyHaven/EnvironmentGround/` — selected Diffuse/Normal/Roughness-or-Specular maps, licenses, and source inventory only.
- Create `dino-attack/Assets/ThirdParty/Kenney/ModularCaveKit/` — selected FBX modules, colormap, license, and source inventory only.
- Create `dino-attack/Assets/ThirdParty/environment-art-import-manifest.json` — exact source path, SHA-256, license, destination, role, and active state for every imported file.
- Create `dino-attack/Assets/LlamAcademy/Dinos/Stage8/Shaders/DinoAttackStylizedGround.shader` — world-XZ tiled ground shading with diffuse, weak normal, roughness, and macro tint; no displacement.
- Create `dino-attack/Assets/LlamAcademy/Dinos/Stage8/Shaders/DinoAttackShallowWater.shader` — transparent two-pan shallow-water shading and UV-edge foam.
- Create `dino-attack/Assets/LlamAcademy/Dinos/Editor/EnvironmentArtImportSettings.cs` — deterministic importer settings for approved textures and Cave Kit FBX files.
- Modify `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs` — material creation, house visuals, water binding, Rim modules, visual sanitation, and canonical checks.
- Modify `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs` — art-resource, visual-only, material/shader, house-tier, Rim, and gameplay-invariance report fields.
- Modify `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs` — asset governance, shader/material, house, Rim, repair, and byte-stability tests.
- Modify `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs` — runtime boundary visibility, attack, speed, NavMesh, visual-component, and cleanup regressions.
- Modify through builder `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`.
- Create `reports/phase8/environment-art-upgrade/` — baseline, import manifest copy, screenshots, performance, tests, longevity, Player smoke, validation, and replacement freeze manifest.
- Modify `docs/PROJECT_PLAN.md` and this plan with verified status and evidence only.

---

### Task 1: Freeze the art-upgrade baseline and exact resource inventory

**Files:**
- Create: `reports/phase8/environment-art-upgrade/baseline.md`
- Modify: `docs/PROJECT_PLAN.md`

**Interfaces:**
- Consumes: approved spec, current `A3CB0CAD...D00FF` quadrilateral freeze, source folders listed in the spec.
- Produces: preserved scene hash, source-file hashes, active `TASK-8.10-ART`, paused `TASK-8.11`, dirty-worktree inventory, and a Console/performance baseline.

- [x] **Step 1: Record the approved execution state**

Mark the written spec reviewed, mark the detailed plan approved only after this plan receives user approval, keep `TASK-8.10-ART` active, and keep 8.11 at `未开始（等待重新冻结）`.

- [x] **Step 2: Capture repository and Unity baselines**

Run:

```powershell
git status --short
git -C dino-attack status --short
Get-FileHash 'dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity' -Algorithm SHA256
Get-FileHash 'reports/phase8/quadrilateral-terrain-zones/manifest.json' -Algorithm SHA256
```

Record Unity version, active scene, Edit/Play state, active build target, quality level, resolution, renderer, Console cursor, fresh errors, and the exact screenshot/performance sampling conditions in `baseline.md`.

- [x] **Step 3: Hash only approved source candidates**

Inventory the Diffuse, Normal GL, Roughness/Specular maps from the five Poly Haven directories; inventory `template-wall`, `template-wall-half`, `template-wall-corner`, `template-wall-top`, `template-wall-detail-a`, `template-corner`, `gate-rock`, Cave Kit colormap/license, and Tribal Hut Tier 1–4 source Prefabs. Exclude all `.blend`, displacement, GLB, OBJ, and MTL files.

- [x] **Step 4: Establish a comparable performance sample**

Capture 300 warmed-up frames in the current Home view and record median/p95 CPU frame time, GPU frame time when available, batches, SetPass calls, triangles, and resolution. If GPU timing is unavailable, record it as unavailable rather than zero.

- [x] **Step 5: Review checkpoint**

Confirm only reports and plan status changed; the active scene is still clean and its hash remains `A3CB0CAD78D8B540C12CFB06CCAEC5CDCF9FEDDF27819C288AB8E037098D00FF`.

### Task 2: Import and govern the minimal art resource set

**Files:**
- Create: `dino-attack/Assets/ThirdParty/PolyHaven/EnvironmentGround/{Dirt,Sand03,BrownMud03,ForestGround01,MarbleRock03}/`
- Create: `dino-attack/Assets/ThirdParty/Kenney/ModularCaveKit/{Models,Textures}/`
- Create: `dino-attack/Assets/ThirdParty/environment-art-import-manifest.json`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Editor/EnvironmentArtImportSettings.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/EnvironmentArtTests/Editor/`

**Interfaces:**
- Consumes: Task 1 source inventory and hashes.
- Produces: approved project-relative texture/FBX paths and deterministic importer settings used by every later task.

- [x] **Step 1: Write failing asset-governance tests**

Add `EnvironmentArtAssets_AreSelectiveLicensedAndImportedDeterministically` asserting:

```csharp
Assert.That(Directory.GetFiles(root, "*.blend", SearchOption.AllDirectories), Is.Empty);
Assert.That(Directory.GetFiles(root, "*.obj", SearchOption.AllDirectories), Is.Empty);
Assert.That(Directory.GetFiles(root, "*.glb", SearchOption.AllDirectories), Is.Empty);
Assert.That(Directory.GetFiles(root, "*disp*", SearchOption.AllDirectories), Is.Empty);
Assert.That(manifest.Entries.All(entry => File.Exists(entry.Destination) && entry.Sha256.Length == 64), Is.True);
```

Define private `[Serializable]` test-only `EnvironmentArtManifest` and `EnvironmentArtManifestEntry` DTOs with `Entries`, `Source`, `SourceSha256`, `Destination`, `License`, `Role`, and `Active`, then parse with `JsonUtility.FromJson<EnvironmentArtManifest>`. Also assert every Diffuse is sRGB with max size 1024, every Normal is `TextureImporterType.NormalMap` with sRGB off, every Roughness/Specular is linear, and every Cave Kit model has readable disabled and no animation import.

- [x] **Step 2: Run the focused EditMode test and verify RED**

Run the connected Editor filter `OpenTropicalBattlefieldSceneTests.EnvironmentArtAssets_AreSelectiveLicensedAndImportedDeterministically`.

Expected: FAIL because the governed import folders and manifest do not exist.

- [x] **Step 3: Add deterministic import rules**

Implement an `AssetPostprocessor` restricted to the two new roots:

```csharp
private const string PolyHavenRoot = "Assets/ThirdParty/PolyHaven/EnvironmentGround/";
private const string CaveRoot = "Assets/ThirdParty/Kenney/ModularCaveKit/";

void OnPreprocessTexture()
{
    var importer = (TextureImporter)assetImporter;
    importer.maxTextureSize = 1024;
    importer.mipmapEnabled = true;
    importer.textureCompression = TextureImporterCompression.CompressedHQ;
    importer.sRGBTexture = assetPath.Contains("_diff_");
    if (assetPath.Contains("_nor_gl_")) importer.textureType = TextureImporterType.NormalMap;
}
```

For FBX files set `importAnimation=false`, `isReadable=false`, `importCameras=false`, `importLights=false`, and preserve a fixed scale factor verified with one representative module before importing the full approved subset.

- [x] **Step 4: Copy only the approved source files and licenses**

Copy Diffuse/Normal/Roughness-or-Specular maps and the eight Cave Kit FBX candidates. Do not copy displacement or `.blend`. Use the Cave Kit colormap and license from its source pack; add Poly Haven CC0 source/license metadata to the manifest. Reimport through Unity so `.meta` files are generated by the Editor.

- [x] **Step 5: Write the exact manifest**

Each JSON entry must contain:

```json
{"source":"absolute source file","sourceSha256":"64 hex chars","destination":"Assets/...","license":"CC0-1.0","role":"diffuse|normal|roughness|specular|rim-model|colormap|license","active":true}
```

Reject duplicate destinations, duplicate model formats, missing licenses, unlisted files, and source-hash drift.

- [x] **Step 6: Run one focused asset-governance test**

Expected: the dedicated governance test passes once. Do not run unrelated full EditMode or PlayMode suites for this import-only slice.

- [x] **Step 7: Address the completed independent review findings**

The amended import records Poly Haven source URLs and a license copy, hashes both source and destination bytes against an exact allowlist, uses a self-contained Task 2 test assembly, imports the Cave Kit colormap as sRGB, excludes Kenney Nature Kit, and changes no package/settings/scene path.

### Task 3: Add the stylized ground shader and material contract

**Files:**
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Stage8/Shaders/DinoAttackStylizedGround.shader`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- Modify: `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`

**Interfaces:**
- Consumes: Task 2 texture paths.
- Produces: shader `Dino Attack/Environment/Stylized Ground` and six canonical materials: neutral dirt, Z1 sand, Z2 forest ground, Z3 sand bed, Z4 brown mud, Z5 warm rock.

- [x] **Step 1: Implement the minimal URP ground shader**

Use world XZ coordinates and one macro modulation, with no vertex modification:

```hlsl
float2 detailUV = positionWS.xz / _DetailScale;
float2 macroUV = positionWS.xz / _MacroScale;
half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, detailUV).rgb * _BaseColor.rgb;
half macro = 0.9h + 0.1h * sin((macroUV.x + macroUV.y) * 6.28318h);
half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, detailUV), _NormalStrength);
half smoothness = 1.0h - SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, detailUV).r;
```

Use URP forward lighting, SRP Batcher-compatible `UnityPerMaterial`, opaque rendering, GPU instancing, and no tessellation/parallax/displacement.

- [x] **Step 2: Replace color-only material setup with explicit asset binding**

Add `EnsureGroundMaterial(string name, string diffusePath, string normalPath, string roughnessPath, Color tint, float detailScale, float macroScale, float normalStrength)` and bind the approved mapping without changing any zone geometry or component.

- [x] **Step 3: Run focused post-change validation**

Expected: materials import without pink rendering; exact five zone vertices, mesh signatures, triggers, and neutral-ground gameplay collider remain unchanged.

Implementation note (2026-08-29): the active camera renderer does not schedule this custom pass in queues `2000–2450`. Ground materials therefore use queue `2900`, before the water queue, while retaining `Blend One Zero`, alpha `1`, `ZWrite On`, and `ZTest LEqual`. This preserves opaque color/depth behavior without adding a GBuffer pass or changing project renderer settings.

- [ ] **Step 4: Review checkpoint**

Capture one close-up and one Home screenshot. Confirm natural differentiation without persistent high-saturation debug fills.

### Task 4: Replace visible graybox houses with visual-only Tribal Huts

**Files:**
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- Modify: `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`
- Modify through builder: `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`

**Interfaces:**
- Consumes: the eight existing `Graybox Building` anchors and project-local `Hut_Tribal_Tier1–4.prefab` assets.
- Produces: `Art Pass/Village Details/Houses` with deterministic Tier distribution `1,2,2,3,1,2,3,4`; invisible graybox renderers with unchanged gameplay colliders.

- [x] **Step 1: Add a visual-only prefab helper**

Implement:

```csharp
private static GameObject EnsureVisualPrefab(Transform parent, string name, string assetPath,
    Vector3 position, float yaw, Vector3 scale, bool preserveSourceMaterials)
```

Instantiate with `PrefabUtility.InstantiatePrefab`, apply a fixed transform, destroy Collider/Rigidbody/NavMeshObstacle components on the instance, remove non-rendering MonoBehaviours from this static visual clone, set all descendants to layer 0, and preserve Tribal Hut source materials.

- [x] **Step 2: Build the approved village hierarchy**

Remove builder-owned `Warm Roof 1–8` only, create the eight tiered visuals at their matching graybox anchors, disable but do not destroy graybox renderers, and keep Tier 4 at the least obstructive central landmark position selected by the existing Home camera.

- [x] **Step 3: Check the minimum house-replacement contract**

Confirm the graybox renderers and Warm Roof cubes are no longer visible, every replacement visual is free of Collider, Rigidbody, NavMeshObstacle, and gameplay scripts, and the existing graybox collider signatures remain unchanged. Do not require exact source GUID, name, transform, or tier-count matching.

- [x] **Step 4: Run one focused house-replacement check**

Expected: replacement visuals appear at the eight existing anchors, forbidden components are absent, and graybox gameplay colliders remain unchanged. Defer the shared NavMesh, defense-slot, and builder stability checks to Task 8.

### Task 5: Implement the lightweight URP shallow-water surface

**Files:**
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Stage8/Shaders/DinoAttackShallowWater.shader`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- Modify: `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`

**Interfaces:**
- Consumes: the unchanged Z3 quadrilateral mesh and its `[0,1]` UVs, plus the MIT-licensed local reference clone at `external/URP-WaterShaders`.
- Produces: shader `Dino Attack/Environment/Shallow Water`, material `ShallowWaterTeal`, and a transparent visual renderer independent of the Z3 trigger/NavMesh/speed volume.

- [x] **Step 1: Copy and simplify the reference water-surface implementation**

Adapt the reference graph's shallow/deep colors, flow speeds, normal distortion, shore blend, clarity, and opacity into one project-local URP HLSL shader. Keep two samples of one normal texture and UV-edge foam:

```hlsl
float2 uvA = input.uv * _TilingA + _Time.y * _PanA.xy;
float2 uvB = input.uv * _TilingB + _Time.y * _PanB.xy;
half3 normalTS = normalize(UnpackNormal(SAMPLE_TEXTURE2D(_FlowNormal, sampler_FlowNormal, uvA)) +
                           UnpackNormal(SAMPLE_TEXTURE2D(_FlowNormal, sampler_FlowNormal, uvB)));
half3 n = TransformTangentToWorld(normalTS, input.tangentToWorld);
half edge = 1.0h - smoothstep(0.0h, _FoamWidth,
    min(min(input.uv.x, 1.0h - input.uv.x), min(input.uv.y, 1.0h - input.uv.y)));
half fresnel = pow(1.0h - saturate(dot(normalize(input.viewDirWS), n)), _FresnelPower);
```

Retain the upstream MIT notice. Use transparent alpha blending and `ZWrite Off`; omit the reference project's SampleScene, underwater pass, Renderer Feature, scene/depth textures, and vertex displacement so no URP project setting changes are required.

- [x] **Step 2: Bind the shader without altering gameplay objects**

Assign the material only to `Terrain Surfaces/ShallowWater/Visible Graybox` (renamed `Water Surface` by the builder if tests and canonical paths are updated atomically). Keep the existing trigger mesh, `TerrainSurfaceVolume`, collider flags, layer, and `0.70` speed unchanged.

Implementation note (2026-08-29): keep the playable Z3 water quadrilateral free of all decoration props. `Art Pass/Z3 Riverbank` remains an empty identity root; the water surface, submerged riverbed, and invisible gameplay trigger remain under `Terrain Surfaces/ShallowWater`.

- [ ] **Step 3: Run the minimum water gameplay check**

Confirm the existing Z3 trigger signature is unchanged, measured movement remains `0.70` of neutral speed, and leaving Z3 restores neutral speed. Confirm by inspection that the water implementation adds no camera or RenderTexture; defer the shared path and gameplay matrix to Task 8.

- [ ] **Step 4: Review checkpoint**

Capture stills at Home and close zoom plus a short runtime observation. Confirm slow readable motion, visible riverbed, stable transparency order, and a narrow shore band.

### Task 7: Complete zone dressing and runtime presentation behavior

**Files:**
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- Modify: `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`

**Interfaces:**
- Consumes: Tasks 3–5 materials and visual helpers, existing PolyPerfect terrain assets, Mini Forest rocks, and selected Cave Kit accents.
- Produces: a simplified low Rim plus readable non-blocking Z1 ruins, Z2 vegetation, an intentionally prop-free Z3 water surface, Z4 mud/palms, and Z5 rock shelf; deployment-only exact outlines and natural Running presentation.

- [x] **Step 1: Simplify the Rim, replace fallback cubes, and tune deterministic dressing**

Lower and break up the current giant Rim instead of creating a separate three-layer Cave wall. Use a few flattened PolyPerfect mountain masses as distant anchors, Mini Forest `rocks-low`/`rocks-ramp`/`stones` as transitions, and Cave Kit `gate-rock`/corner/detail models only as local accents. Hide only superseded Rim renderers, preserve every boundary collider, and keep all new objects visual-only. Then use `DecorationSpec` records and `EnsureVisualPrefab` for Z1 ruins, Z2 low vegetation, Z4 palms/roots, and Z5 rock steps without suggesting false deployment edges. Keep Z3 itself prop-free; any future shoreline decoration must stay outside its four-vertex water polygon.

- [x] **Step 2: Enforce deployment-only precision overlays**

Keep `DinoDeploymentZonePresenter` as the sole exact-outline authority. Do not add persistent zone-fill toggles; verify textured ground remains visible while boundary renderers follow the existing selection/hover lifecycle.

- [ ] **Step 3: Review checkpoint**

Capture Home/close screenshots and confirm the Rim is lower and less repetitive, remains outside gameplay navigation, adds no attack blocker, all five areas are identifiable without pure-color cards, and the village and units remain the visual focus. Defer the shared gameplay matrix to Task 8.

### Task 8: Validate key gameplay, performance, and Player readiness

**Files:**
- Create: `reports/phase8/environment-art-upgrade/tests.json`
- Create: `reports/phase8/environment-art-upgrade/performance.json`
- Create: `reports/phase8/environment-art-upgrade/art-audit.json`
- Create: `reports/phase8/environment-art-upgrade/reload-smoke.json`
- Create: `reports/phase8/environment-art-upgrade/player-smoke.log`
- Create: `reports/phase8/environment-art-upgrade/validation.md`
- Create: `reports/phase8/environment-art-upgrade/manifest.json`
- Create: `reports/phase8/environment-art-upgrade/{home,close}.png`
- Modify: `docs/PROJECT_PLAN.md`

**Interfaces:**
- Consumes: canonical art-upgraded scene and Task 1 baseline conditions.
- Produces: key-contract results, a minimal visual audit, comparable performance data, two-reload smoke evidence, graphical Player evidence, the minimum replacement freeze, and resumed `TASK-8.11` status.

- [ ] **Step 1: Run the final key-contract validation**

Run compilation, the focused art contract, and only the existing critical gameplay checks for five-zone vertices and `(u,v)`, 15/15 navigation, terrain speed/restoration, deployment UI lifecycle, and attacks remaining unblocked. Finish with the validator and a fresh Console scan; do not run unrelated complete EditMode or PlayMode suites.

- [ ] **Step 2: Run a minimal visual-component audit**

Record the active visual roots and materials/shaders, then verify imported decoration and house visuals contain no active Collider, Rigidbody, NavMeshObstacle, or gameplay script. Do not inventory every renderer, triangle count, transform, or source GUID again.

- [ ] **Step 3: Capture comparable performance evidence**

Use Task 1 quality, resolution, Home camera, warmup, and 300-frame sample. Report before/after median/p95 CPU/GPU, batches, SetPass, triangles, percentage deltas, and whether every metric is validly comparable. The performance gate is p95 CPU and GPU frame time no more than 20% above baseline, SetPass no more than 25% above baseline, and batches no more than 35% above baseline. If GPU timing is unavailable, disclose that limitation and require the CPU, SetPass, and batch gates; do not claim a pass for mismatched samples.

- [ ] **Step 4: Capture two representative views**

Capture one Home view and one close view. Verify village readability, all five terrain identities, Rim silhouette, water transparency, and no pink or missing shader.

- [ ] **Step 5: Run two representative scene-reload smokes**

Run one representative win and one representative loss. Confirm reload cleanup, state transition, resource refund, speed restoration, NavMesh validity, and no new Console error; do not repeat the already completed 20-episode longevity matrix.

- [ ] **Step 6: Build and smoke the Windows Player**

Build the same `Dinos.unity`, launch the newly created Player, verify graphical startup and real round activity, close only that run's process/window, and scan its log for crash, exception, missing reference, assertion, NaN, NavMesh, waypoint, armor, error, and failed markers.

- [ ] **Step 7: Record the minimum replacement freeze and update status**

Write `validation.md` with direct artifact links and separate introduced failures from inherited warnings. Record the final `Dinos.unity` hash, Windows build hash, active shaders/materials/resources, five unchanged vertex arrays, speed table, 6D action/504 observation contract, and superseded quadrilateral freeze hash in `manifest.json`. Update `docs/PROJECT_PLAN.md` to complete `TASK-8.10-ART` and resume `TASK-8.11` only when these minimum gates pass.
