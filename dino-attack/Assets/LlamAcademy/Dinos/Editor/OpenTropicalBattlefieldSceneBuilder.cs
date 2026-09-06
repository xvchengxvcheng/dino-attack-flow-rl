using System;
using System.Collections.Generic;
using System.Linq;
using LlamAcademy.Dinos.Deployment;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace LlamAcademy.Dinos.Map.Editor
{
    public static class OpenTropicalBattlefieldSceneBuilder
    {
        private const string ScenePath = "Assets/LlamAcademy/Dinos/Scenes/Dinos.unity";
        private const string HistoricalTrainingScenePath = "Assets/LlamAcademy/Dinos/Scenes/DinoAttackTraining.unity";
        private const string SourceWallPrefabPath = "Assets/LlamAcademy/Dinos/Enemy AI/Walls/Arch Entry Wall.prefab";
        private const string SourceUpgradePrefabPath = "Assets/LlamAcademy/Dinos/Enemy AI/Walls/Arch Entry Wall Upgrade 1.prefab";
        private const string SourceWallUnitPath = "Assets/LlamAcademy/Dinos/Enemy AI/Walls/Arch Entry Wall.asset";
        private const string SourceUpgradeUnitPath = "Assets/LlamAcademy/Dinos/Enemy AI/Walls/Arch Entry Wall Upgrade 1.asset";
        private const string GeneratedFolder = "Assets/LlamAcademy/Dinos/Map/Generated";
        private const string DefenseWallPrefabPath = GeneratedFolder + "/Open Tropical Defense Wall.prefab";
        private const string DefenseUpgradePrefabPath = GeneratedFolder + "/Open Tropical Defense Wall Upgrade.prefab";
        private const string DefenseWallUnitPath = GeneratedFolder + "/Open Tropical Defense Wall.asset";
        private const string DefenseUpgradeUnitPath = GeneratedFolder + "/Open Tropical Defense Wall Upgrade.asset";
        private const string ArcherUnitPath = "Assets/LlamAcademy/Dinos/Enemy AI/Archer/Archer.asset";
        private const string MageUnitPath = "Assets/LlamAcademy/Dinos/Enemy AI/Mage/Mage.asset";
        private const string HealthBarPrefabPath = "Assets/LlamAcademy/Dinos/Prefabs/Health Bar.prefab";
        private const string Stage8MaterialFolder = "Assets/LlamAcademy/Dinos/Stage8/Materials";
        private const string GroundShaderName = "Dino Attack/Environment/Stylized Ground";
        private const string ShallowWaterShaderName = "Dino Attack/Environment/Shallow Water";
        private const string ShallowWaterNormalPath = "Assets/ThirdParty/PolyHaven/EnvironmentGround/Sand03/sand_03_nor_gl_1k.exr";
        private const string PalmPrefabPath = "Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Tribal Pack/T/- Prefabs_T/Nature_T/Prehistoric_T/Palm_Prehistoric_Small.prefab";
        private const string DesertMountainPrefabPath = "Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Ultimate Pack/T/- Meshes_T/Terrains_T/mountain-desert.fbx";
        private const string CaveGatePath = "Assets/ThirdParty/Kenney/ModularCaveKit/Models/gate-rock.fbx";
        private const string CaveCornerPath = "Assets/ThirdParty/Kenney/ModularCaveKit/Models/template-wall-corner.fbx";
        private const string MiniForestLowRocksPath = "Assets/ThirdParty/Kenney/MiniForest/Models/rocks-low.fbx";
        private const string MiniForestRampRocksPath = "Assets/ThirdParty/Kenney/MiniForest/Models/rocks-ramp.fbx";

        private static readonly string[] HutPrefabPaths =
        {
            "Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Tribal Pack/T/- Prefabs_T/Tribal_T/Hut_Tribal_Tier1.prefab",
            "Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Tribal Pack/T/- Prefabs_T/Tribal_T/Hut_Tribal_Tier2.prefab",
            "Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Tribal Pack/T/- Prefabs_T/Tribal_T/Hut_Tribal_Tier3.prefab",
            "Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Tribal Pack/T/- Prefabs_T/Tribal_T/Hut_Tribal_Tier4.prefab"
        };

        private readonly struct ZoneSpec
        {
            public readonly string Name;
            public readonly DeploymentZoneId Id;
            public readonly string DisplayName;
            public readonly TerrainSurfaceKind Surface;
            public readonly QuadrilateralXZ Geometry;

            public ZoneSpec(string name, DeploymentZoneId id, string displayName, TerrainSurfaceKind surface,
                Vector2 a, Vector2 b, Vector2 c, Vector2 d)
            {
                Name = name;
                Id = id;
                DisplayName = displayName;
                Surface = surface;
                if (!QuadrilateralXZ.TryCreate(a, b, c, d, out Geometry))
                {
                    throw new InvalidOperationException($"Canonical zone {name} is not a valid convex quadrilateral.");
                }
            }
        }

        private static readonly ZoneSpec[] CanonicalZoneSpecs =
        {
            new("Z1", DeploymentZoneId.Z1RuinsForecourt, "Ruins Forecourt", TerrainSurfaceKind.StoneRoad,
                new Vector2(-27f, -44f), new Vector2(-27f, -25f), new Vector2(-50f, -16f), new Vector2(-52f, -49f)),
            new("Z2", DeploymentZoneId.Z2OpenMeadow, "Open Meadow", TerrainSurfaceKind.Grass,
                new Vector2(-25f, -19f), new Vector2(-13f, -17f), new Vector2(-22f, 18f), new Vector2(-44f, 7f)),
            new("Z3", DeploymentZoneId.Z3RiverTerrace, "River Terrace", TerrainSurfaceKind.ShallowWater,
                new Vector2(-10f, -17f), new Vector2(7f, -17f), new Vector2(14f, 27f), new Vector2(-18f, 27f)),
            new("Z4", DeploymentZoneId.Z4PalmGrove, "Palm Grove", TerrainSurfaceKind.Mud,
                new Vector2(10f, -18f), new Vector2(22f, -21f), new Vector2(43f, 5f), new Vector2(20f, 18f)),
            new("Z5", DeploymentZoneId.Z5RockyShelf, "Rocky Shelf", TerrainSurfaceKind.Slope,
                new Vector2(23f, -44f), new Vector2(23f, -25f), new Vector2(46f, -17f), new Vector2(48f, -48f))
        };

        [MenuItem("Tools/Dino Attack/Build Open Tropical Battlefield")]
        public static void BuildFromMenu()
        {
            OpenTropicalBattlefieldReport report = Build(saveScene: true);
            if (!report.IsValid)
            {
                throw new InvalidOperationException("Open Tropical Battlefield validation failed: " + report.Summary);
            }

            Debug.Log("Open Tropical Battlefield built and validated: " + report.Summary);
        }

        [MenuItem("Tools/Dino Attack/Remove Map One Riverbed Layer")]
        public static void RemoveMapOneRiverbedLayer()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Riverbed repair requires a stopped, compiled Editor.");
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty)
                throw new InvalidOperationException($"Refusing to close dirty scene '{active.path}'.");

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            Transform shallowWater = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(value => value.name == "ShallowWater" &&
                    value.parent != null && value.parent.name == "Terrain Surfaces");
            if (shallowWater == null)
                throw new InvalidOperationException("Map 1 shallow-water terrain root is missing.");
            DestroyNamedChildren(shallowWater, "Riverbed");
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Map 1 riverbed layer removed; shallow-water surface material retained.");
        }

        public static bool ConfigureHistoricalTrainingFallback()
        {
            Scene active = SceneManager.GetActiveScene();
            Scene training = EditorSceneManager.OpenScene(HistoricalTrainingScenePath, OpenSceneMode.Additive);
            try
            {
                Component enemyAi = training.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Component>(true))
                    .FirstOrDefault(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Enemy.EnemyAIController");
                if (enemyAi == null)
                {
                    throw new InvalidOperationException("Historical training scene has no EnemyAIController.");
                }

                SerializedObject serialized = new(enemyAi);
                SerializedProperty fallback = RequireProperty(serialized, "AllowHistoricalTriangulationFallback");
                bool changed = !fallback.boolValue;
                fallback.boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                if (changed)
                {
                    EditorSceneManager.MarkSceneDirty(training);
                    EditorSceneManager.SaveScene(training);
                }
                return changed;
            }
            finally
            {
                EditorSceneManager.CloseScene(training, true);
                if (active.IsValid() && active.isLoaded) SceneManager.SetActiveScene(active);
            }
        }

        [MenuItem("Tools/Dino Attack/Install Open Tropical Pre-Baked NavMesh Contract")]
        public static void InstallPreBakedNavMeshContract()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
            {
                throw new InvalidOperationException(
                    "The pre-baked NavMesh contract requires a stopped, compiled Editor.");
            }
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty)
            {
                throw new InvalidOperationException($"Refusing to close dirty scene '{active.path}'.");
            }

            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            if (ConfigurePreBakedNavMeshContract(scene))
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            Debug.Log("Open Tropical now installs pre-baked NavMeshData without runtime surface builds.");
        }

        public static OpenTropicalBattlefieldReport Build(bool saveScene)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                throw new InvalidOperationException($"Expected active scene '{ScenePath}', got '{scene.path}'.");
            }

            bool navMeshContractChanged = ConfigurePreBakedNavMeshContract(scene);
            Transform world = OpenTropicalBattlefieldValidator.Find(scene, "/World");
            if (world == null)
            {
                throw new InvalidOperationException("The active Dinos scene has no /World root.");
            }

            OpenTropicalBattlefieldReport initialReport = OpenTropicalBattlefieldValidator.ValidateLoadedScene();
            if (IsCanonicalScene(scene, world, initialReport))
            {
                if (saveScene && navMeshContractChanged)
                {
                    EditorSceneManager.MarkSceneDirty(scene);
                    EditorSceneManager.SaveScene(scene);
                }
                return initialReport;
            }

            if (CanRepairVisualOnly(scene, world, initialReport))
            {
                Transform visualBattlefield = world.Find("Open Tropical Battlefield");
                string frozenBefore = OpenTropicalBattlefieldVisualPolishBuilder.BuildFrozenGameplaySignature(scene);
                OpenTropicalBattlefieldVisualPolishBuilder.Build(
                    scene,
                    world,
                    visualBattlefield,
                    visualBattlefield.Find("Terrain Surfaces"),
                    visualBattlefield.Find("Village"),
                    visualBattlefield.Find("Outer Scenery"));
                string frozenAfter = OpenTropicalBattlefieldVisualPolishBuilder.BuildFrozenGameplaySignature(scene);
                if (!string.Equals(frozenBefore, frozenAfter, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Visual-only repair changed the frozen gameplay signature: {frozenBefore} -> {frozenAfter}");
                }

                EditorSceneManager.MarkSceneDirty(scene);
                OpenTropicalBattlefieldReport visualRepairReport = OpenTropicalBattlefieldValidator.ValidateLoadedScene();
                if (saveScene && visualRepairReport.IsValid) EditorSceneManager.SaveScene(scene);
                return visualRepairReport;
            }

            if (CanRepairCameraOnly(scene, world, initialReport))
            {
                ConfigureCameraBounds(world);
                ConfigureFullIslandCamera(scene, world);
                EditorSceneManager.MarkSceneDirty(scene);
                OpenTropicalBattlefieldReport cameraRepairReport = OpenTropicalBattlefieldValidator.ValidateLoadedScene();
                if (saveScene && cameraRepairReport.IsValid)
                {
                    EditorSceneManager.SaveScene(scene);
                }

                return cameraRepairReport;
            }

            EnsureSafeDefenseAssets();

            Transform battlefield = EnsureChild(world, "Open Tropical Battlefield");
            Transform outerScenery = EnsureChild(battlefield, "Outer Scenery");
            Transform village = EnsureChild(battlefield, "Village");
            Transform northWest = EnsureChild(village, "NorthWest");
            Transform northEast = EnsureChild(village, "NorthEast");
            Transform southWest = EnsureChild(village, "SouthWest");
            Transform southEast = EnsureChild(village, "SouthEast");
            Transform plaza = EnsureChild(village, "Central Plaza");
            ConfigureWorldTransform(northWest, new Vector3(-14f, 0f, -42f));
            ConfigureWorldTransform(northEast, new Vector3(9f, 0f, -42f));
            ConfigureWorldTransform(southWest, new Vector3(-14f, 0f, -31f));
            ConfigureWorldTransform(southEast, new Vector3(9f, 0f, -31f));
            ConfigureWorldTransform(plaza, new Vector3(-2.23f, -0.1f, -38.26f));

            Transform objective = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Transform>(true))
                .FirstOrDefault(t => t.name == "Dino Egg Spawn");
            if (objective == null)
            {
                throw new InvalidOperationException("The existing Dino Egg Spawn objective was not found.");
            }
            if (objective.parent != plaza)
            {
                Undo.SetTransformParent(objective, plaza, "Move victory objective to central plaza");
            }

            BuildVillageGraybox(northWest, northEast, southWest, southEast, plaza);
            BuildOuterScenery(outerScenery);

            Transform zoneRoot = EnsureChild(battlefield, "Deployment Zones");
            DinoDeploymentZone[] zones = CanonicalZoneSpecs.Select(spec => ConfigureZone(zoneRoot, spec)).ToArray();

            Transform terrainRoot = EnsureChild(battlefield, "Terrain Surfaces");
            foreach (ZoneSpec spec in CanonicalZoneSpecs)
            {
                ConfigureSurface(terrainRoot, spec);
            }
            ConfigureNeutralGround(terrainRoot);

            BuildArtPass(scene, world, battlefield, village, outerScenery, zoneRoot, terrainRoot);
            Component[] villageHouses = ConfigureVillageHouseGameplay(battlefield);
            ConfigureRoundManagerHouses(villageHouses);

            Transform defenseRoot = EnsureChild(battlefield, "Defense Presets");
            GameObject[] presets = BuildDefensePresets(defenseRoot);
            VillageDefenseLayoutController defenseController = GetOrAdd<VillageDefenseLayoutController>(defenseRoot.gameObject);
            SetObjectArray(defenseController, "presetRoots", presets.Cast<UnityEngine.Object>().ToArray());
            SetInt(defenseController, "mapSeed", 20260828);
            defenseController.enabled = false;
            foreach (GameObject preset in presets)
            {
                preset.SetActive(false);
            }
            defenseRoot.gameObject.SetActive(false);
            Component sessionDefenseLayout = BuildSessionDefenseLayout(battlefield);
            Component sessionGroundDefenseLayout = BuildSessionGroundDefenseLayout(battlefield, zones);
            ConfigureEnemyAiForNewMap(sessionDefenseLayout, sessionGroundDefenseLayout);

            ConfigureDeployment(zones);
            ConfigureSessionRestartService();
            ConfigureRuntimeUi();
            ConfigureCameraBounds(world);
            ConfigureFullIslandCamera(scene, world);
            EditorSceneManager.MarkSceneDirty(scene);

            OpenTropicalBattlefieldReport beforeLegacyDeactivation = OpenTropicalBattlefieldValidator.ValidateLoadedScene();
            Transform legacy = OpenTropicalBattlefieldValidator.Find(scene, "/World/Stage8 Strategy");
            if (beforeLegacyDeactivation.IsStructurallyReadyToDeactivateLegacy && legacy != null && legacy.gameObject.activeSelf)
            {
                Undo.RecordObject(legacy.gameObject, "Deactivate superseded Stage8 strategy");
                legacy.gameObject.SetActive(false);
                EditorUtility.SetDirty(legacy.gameObject);
            }
            if (beforeLegacyDeactivation.IsStructurallyReadyToDeactivateLegacy)
            {
                DeactivateSupersededWorldGeometry(world, battlefield);
                NavMeshSurface[] runtimeSurfaces = ConfigureNavMeshes(world);
                SetObjectArray(defenseController, "runtimeSurfaces", runtimeSurfaces.Cast<UnityEngine.Object>().ToArray());
            }

            OpenTropicalBattlefieldReport finalReport = OpenTropicalBattlefieldValidator.ValidateLoadedScene();
            if (saveScene && finalReport.IsValid)
            {
                EditorSceneManager.SaveScene(scene);
            }

            return finalReport;
        }

        private static bool ConfigurePreBakedNavMeshContract(Scene scene)
        {
            Component navigation = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Component>(true))
                .Single(component => component != null &&
                    component.GetType().FullName ==
                    "LlamAcademy.Dinos.RoundManagement.NavMeshManager");
            SerializedObject serialized = new(navigation);
            SerializedProperty allowRuntimeBuild = RequireProperty(serialized, "AllowRuntimeBuild");
            if (!allowRuntimeBuild.boolValue)
            {
                return false;
            }

            allowRuntimeBuild.boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(navigation);
            return true;
        }

        public static OpenTropicalBattlefieldReport ApplyApprovedCameraSceneryAndNavMeshRepair(bool saveScene)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                throw new InvalidOperationException($"Expected active scene '{ScenePath}', got '{scene.path}'.");
            }
            if (scene.isDirty)
            {
                throw new InvalidOperationException(
                    "Approved scoped repair requires a clean scene so a failed install can restore the saved handoff exactly.");
            }

            Transform world = OpenTropicalBattlefieldValidator.Find(scene, "/World");
            Transform battlefield = world?.Find("Open Tropical Battlefield");
            if (world == null || battlefield == null)
            {
                throw new InvalidOperationException("The canonical battlefield roots are missing.");
            }

            NavMeshSurface[] existingSurfaces = world.GetComponents<NavMeshSurface>();
            if (existingSurfaces.Length != 4 || existingSurfaces.Select(surface => surface.agentTypeID).Distinct().Count() != 4)
            {
                throw new InvalidOperationException(
                    $"Approved scoped repair requires exactly four distinct canonical NavMesh surfaces; found {existingSurfaces.Length}.");
            }

            OpenTropicalBattlefieldReport initialReport = OpenTropicalBattlefieldValidator.ValidateLoadedScene();
            Transform distant = battlefield.Find("Art Pass/Canyon Rim/Distant Scenery");
            if (initialReport.IsValid && CameraIsCanonical(scene, world) &&
                distant != null && distant.childCount == 0 && NavMeshesAreCanonical(world))
            {
                return initialReport;
            }

            string gameplayBefore = OpenTropicalBattlefieldVisualPolishBuilder.BuildFrozenGameplaySignature(scene);
            try
            {
                ConfigureCameraBounds(world);
                ConfigureFullIslandCamera(scene, world);
                OpenTropicalBattlefieldVisualPolishBuilder.RemoveDistantScenery(battlefield);
                ConfigureNavMeshes(world);
                string gameplayAfter = OpenTropicalBattlefieldVisualPolishBuilder.BuildFrozenGameplaySignature(scene);
                if (!string.Equals(gameplayBefore, gameplayAfter, StringComparison.Ordinal))
                {
                    throw new InvalidOperationException(
                        $"Approved camera/scenery/NavMesh repair changed frozen gameplay state: {gameplayBefore} -> {gameplayAfter}");
                }

                EditorSceneManager.MarkSceneDirty(scene);
                OpenTropicalBattlefieldReport report = OpenTropicalBattlefieldValidator.ValidateLoadedScene();
                if (!report.IsValid)
                {
                    throw new InvalidOperationException("Approved scoped repair validation failed: " + report.Summary);
                }

                if (saveScene)
                {
                    EditorSceneManager.SaveScene(scene);
                }

                return report;
            }
            catch
            {
                EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
                throw;
            }
        }

        private static bool IsCanonicalScene(
            Scene scene,
            Transform world,
            OpenTropicalBattlefieldReport report)
        {
            if (!report.IsValid || !HasCanonicalNonCameraState(scene, world, report))
            {
                return false;
            }

            return CameraIsCanonical(scene, world);
        }

        private static bool CanRepairCameraOnly(
            Scene scene,
            Transform world,
            OpenTropicalBattlefieldReport report) =>
            HasCanonicalNonCameraState(scene, world, report) && !CameraIsCanonical(scene, world);

        private static bool CanRepairVisualOnly(Scene scene, Transform world, OpenTropicalBattlefieldReport report)
        {
            bool missingOnlyVisual = report.MissingHierarchyPaths.All(path =>
                path.StartsWith(OpenTropicalBattlefieldValidator.BattlefieldPath + "/Art Pass/", StringComparison.Ordinal));
            bool pathsAreCanonical = report.Paths.Length == 15 && report.CompletePathCount == 15 &&
                report.DefenderSlotPaths.Length == 6 && report.DefenderSlotPaths.All(path =>
                    path.StartSampled && path.GoalSampled && path.Status == NavMeshPathStatus.PathComplete.ToString() &&
                    float.IsFinite(path.Length) && path.Length > 0.1f);
            Transform battlefield = world.Find("Open Tropical Battlefield");
            return battlefield != null && missingOnlyVisual && pathsAreCanonical && !report.LegacyStrategyActive &&
                   !report.HasForbiddenDeploymentZone && report.ZoneIds.Length == 5 && report.SurfaceKinds.Length == 5 &&
                   report.ObjectiveCount == 1 && report.CameraBoundsCount == 1 &&
                   report.DefensePresetCount >= 2 && report.ActiveDefensePresetCount == 0 &&
                   report.DefenseWallsSanitized && report.VillageHouseCount == 8 && report.RimVisualOnly &&
                   report.WallSpawnSlotCount == 6 && report.GroundDefenseConfigured &&
                   report.ServiceUsesExactZoneInstances && report.PresenterUsesServiceZones &&
                   report.BoundsAreaIncreasePercent >= 25f && report.BoundsAreaIncreasePercent <= 35f &&
                   SafeDefenseAssetsAreCanonical() && ZonesAreCanonical(battlefield) && SurfacesAreCanonical(battlefield) &&
                   DefenseBindingsAreCanonical(battlefield, world) && DeploymentBindingsAreCanonical(battlefield) &&
                   NavMeshesAreCanonical(world) && SupersededGeometryIsInactive(world, battlefield) &&
                   CameraIsCanonical(scene, world);
        }

        private static bool HasCanonicalNonCameraState(
            Scene scene,
            Transform world,
            OpenTropicalBattlefieldReport report)
        {
            bool navigationIsCanonical = report.Paths.Length == 15 && report.CompletePathCount == 15 &&
                report.DefenderSlotPaths.Length > 0 && report.DefenderSlotPaths.All(path =>
                    path.StartSampled && path.GoalSampled && path.Status == NavMeshPathStatus.PathComplete.ToString() &&
                    float.IsFinite(path.Length) && path.Length > 0.1f);
            if (scene.isDirty || report.MissingHierarchyPaths.Length != 0 || report.LegacyStrategyActive ||
                !navigationIsCanonical || !SafeDefenseAssetsAreCanonical())
            {
                return false;
            }

            Transform battlefield = world.Find("Open Tropical Battlefield");
            bool zones = battlefield != null && ZonesAreCanonical(battlefield);
            bool surfaces = battlefield != null && SurfacesAreCanonical(battlefield);
            bool defense = battlefield != null && DefenseBindingsAreCanonical(battlefield, world);
            bool deployment = battlefield != null && DeploymentBindingsAreCanonical(battlefield);
            bool navMeshes = NavMeshesAreCanonical(world);
            bool art = battlefield != null && ArtPassIsCanonical(battlefield);
            bool superseded = battlefield != null && SupersededGeometryIsInactive(world, battlefield);
            if (battlefield == null || !zones || !surfaces || !defense || !deployment || !navMeshes || !art || !superseded)
            {
                return false;
            }

            return true;
        }

        private static bool SafeDefenseAssetsAreCanonical()
        {
            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefenseWallPrefabPath);
            GameObject upgradePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefenseUpgradePrefabPath);
            UnityEngine.Object baseUnit = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DefenseWallUnitPath);
            UnityEngine.Object upgradeUnit = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DefenseUpgradeUnitPath);
            Component baseWall = basePrefab == null ? null : FindWall(basePrefab);
            Component upgradeWall = upgradePrefab == null ? null : FindWall(upgradePrefab);
            if (baseWall == null || upgradeWall == null || baseUnit == null || upgradeUnit == null)
            {
                return false;
            }

            SerializedObject baseUnitData = new(baseUnit);
            SerializedObject upgradeUnitData = new(upgradeUnit);
            return baseUnitData.FindProperty("<Prefab>k__BackingField")?.objectReferenceValue == baseWall &&
                   baseUnitData.FindProperty("<Upgrade>k__BackingField")?.objectReferenceValue == upgradeUnit &&
                   upgradeUnitData.FindProperty("<Prefab>k__BackingField")?.objectReferenceValue == upgradeWall &&
                   upgradeUnitData.FindProperty("<Upgrade>k__BackingField")?.objectReferenceValue == null &&
                   DefenseWallIsCanonical(baseWall, baseUnit) && DefenseWallIsCanonical(upgradeWall, upgradeUnit);
        }

        private static bool DefenseWallIsCanonical(Component wall, UnityEngine.Object unit)
        {
            SerializedObject serialized = new(wall);
            return serialized.FindProperty("Root")?.objectReferenceValue == wall.gameObject &&
                   serialized.FindProperty("<UnitType>k__BackingField")?.objectReferenceValue == unit &&
                   serialized.FindProperty("HealthBar")?.objectReferenceValue != null &&
                   serialized.FindProperty("UpdateNavMeshData.<Obstacles>k__BackingField")?.arraySize == 0 &&
                   serialized.FindProperty("UpdateNavMeshData.<RebakeSurfacesOnDeath>k__BackingField")?.arraySize == 0 &&
                   serialized.FindProperty("UpdateNavMeshData.<EnableObjectsBeforeRebake>k__BackingField")?.arraySize == 0 &&
                   serialized.FindProperty("UpdateNavMeshData.<EnableComponentsBeforeRebake>k__BackingField")?.arraySize == 0 &&
                   wall.GetComponentsInChildren<Transform>(true).All(transform => transform.gameObject.layer == 7) &&
                   wall.GetComponentsInChildren<NavMeshObstacle>(true).All(obstacle => !obstacle.enabled && !obstacle.carving);
        }

        private static bool ZonesAreCanonical(Transform battlefield)
        {
            Transform root = battlefield.Find("Deployment Zones");
            if (root == null) return false;
            foreach (ZoneSpec value in CanonicalZoneSpecs)
            {
                Transform transform = root.Find(value.Name);
                DinoDeploymentZone zone = transform == null ? null : transform.GetComponent<DinoDeploymentZone>();
                Transform boundaryTransform = transform?.Find("Boundary");
                Renderer boundary = boundaryTransform?.GetComponent<Renderer>();
                Mesh boundaryMesh = boundaryTransform?.GetComponent<MeshFilter>()?.sharedMesh;
                if (zone == null || boundary == null || zone.Id != value.Id ||
                    zone.DisplayName != value.DisplayName || zone.DominantSurface != value.Surface ||
                    transform.position != Vector3.zero || transform.rotation != Quaternion.identity || transform.localScale != Vector3.one ||
                    transform.gameObject.layer != 2 || transform.GetComponent<BoxCollider>() != null ||
                    !ZoneVerticesMatch(zone, value.Geometry) || !MeshMatchesOutline(boundaryMesh, value.Geometry, 0.02f) || boundary.enabled)
                {
                    return false;
                }
            }

            return true;
        }

        private static bool SurfacesAreCanonical(Transform battlefield)
        {
            Transform root = battlefield.Find("Terrain Surfaces");
            if (root == null) return false;
            foreach (ZoneSpec value in CanonicalZoneSpecs)
            {
                Transform transform = root.Find(value.Surface.ToString());
                TerrainSurfaceVolume volume = transform == null ? null : transform.GetComponent<TerrainSurfaceVolume>();
                MeshCollider trigger = transform == null ? null : transform.GetComponent<MeshCollider>();
                Transform visual = transform?.Find("Visible Graybox");
                Mesh visualMesh = visual?.GetComponent<MeshFilter>()?.sharedMesh;
                if (volume == null || trigger == null || visual == null || volume.Surface != value.Surface ||
                    transform.position != Vector3.zero || transform.gameObject.layer != 2 ||
                    !trigger.convex || !trigger.isTrigger || !MeshMatchesPrism(trigger.sharedMesh, value.Geometry, -0.5f, 1.5f) ||
                    visual.position != Vector3.zero || visual.localScale != Vector3.one ||
                    visual.GetComponent<Collider>() != null || !MeshMatchesGround(visualMesh, value.Geometry, 0.015f))
                {
                    return false;
                }
            }

            Transform neutral = root.Find("Neutral Ground");
            BoxCollider neutralCollider = neutral == null ? null : neutral.GetComponent<BoxCollider>();
            return neutral != null && neutral.gameObject.layer == 6 && neutral.position == new Vector3(-3.5f, -0.25f, -12f) &&
                   neutral.localScale == new Vector3(104f, 0.5f, 82f) && neutralCollider != null && !neutralCollider.isTrigger &&
                   neutral.GetComponent<TerrainSurfaceVolume>() == null;
        }

        private static bool ZoneVerticesMatch(DinoDeploymentZone zone, QuadrilateralXZ geometry)
        {
            IReadOnlyList<Vector3> actual = zone.WorldVertices;
            Vector3[] expected = LayerVertices(geometry, 0f);
            return actual.Count == expected.Length && actual.Select((value, index) => Approximately(value, expected[index])).All(value => value);
        }

        private static bool MeshMatchesGround(Mesh mesh, QuadrilateralXZ geometry, float y)
        {
            return MeshVerticesMatch(mesh, LayerVertices(geometry, y)) && mesh.subMeshCount == 1 &&
                   mesh.GetTopology(0) == MeshTopology.Triangles &&
                   mesh.GetIndices(0).SequenceEqual(new[] { 0, 2, 1, 0, 3, 2 });
        }

        private static bool MeshMatchesOutline(Mesh mesh, QuadrilateralXZ geometry, float y)
        {
            return MeshVerticesMatch(mesh, LayerVertices(geometry, y)) && mesh.subMeshCount == 1 &&
                   mesh.GetTopology(0) == MeshTopology.Lines &&
                   mesh.GetIndices(0).SequenceEqual(new[] { 0, 1, 1, 2, 2, 3, 3, 0 });
        }

        private static bool MeshMatchesPrism(Mesh mesh, QuadrilateralXZ geometry, float bottomY, float topY)
        {
            Vector3[] bottom = LayerVertices(geometry, bottomY);
            Vector3[] top = LayerVertices(geometry, topY);
            Vector3[] expected = bottom.Concat(top).ToArray();
            return MeshVerticesMatch(mesh, expected) && mesh.subMeshCount == 1 &&
                   mesh.GetTopology(0) == MeshTopology.Triangles && mesh.GetIndices(0).Length == 36;
        }

        private static bool MeshVerticesMatch(Mesh mesh, Vector3[] expected)
        {
            if (mesh == null) return false;
            Vector3[] actual = mesh.vertices;
            return actual.Length == expected.Length && actual.Select((value, index) => Approximately(value, expected[index])).All(value => value);
        }

        private static Vector3[] LayerVertices(QuadrilateralXZ geometry, float y) => new[]
        {
            new Vector3(geometry.A.x, y, geometry.A.y),
            new Vector3(geometry.B.x, y, geometry.B.y),
            new Vector3(geometry.C.x, y, geometry.C.y),
            new Vector3(geometry.D.x, y, geometry.D.y)
        };

        private static bool Approximately(Vector3 actual, Vector3 expected) =>
            Mathf.Abs(actual.x - expected.x) <= 0.0001f &&
            Mathf.Abs(actual.y - expected.y) <= 0.0001f &&
            Mathf.Abs(actual.z - expected.z) <= 0.0001f;

        private static bool DefenseBindingsAreCanonical(Transform battlefield, Transform world)
        {
            Transform root = battlefield.Find("Defense Presets");
            VillageDefenseLayoutController layout = root == null ? null : root.GetComponent<VillageDefenseLayoutController>();
            if (layout == null || layout.MapSeed != 20260828 || root.gameObject.activeSelf || layout.enabled)
            {
                return false;
            }

            SerializedObject serialized = new(layout);
            SerializedProperty presetRoots = serialized.FindProperty("presetRoots");
            SerializedProperty runtimeSurfaces = serialized.FindProperty("runtimeSurfaces");
            NavMeshSurface[] surfaces = world.GetComponents<NavMeshSurface>();
            if (presetRoots == null || presetRoots.arraySize != 2 || runtimeSurfaces == null || runtimeSurfaces.arraySize != surfaces.Length)
            {
                return false;
            }

            for (int index = 0; index < 2; index++)
            {
                GameObject expected = root.Find($"Preset {index}")?.gameObject;
                if (expected == null || presetRoots.GetArrayElementAtIndex(index).objectReferenceValue != expected ||
                    expected.activeSelf)
                {
                    return false;
                }
            }
            for (int index = 0; index < surfaces.Length; index++)
            {
                if (runtimeSurfaces.GetArrayElementAtIndex(index).objectReferenceValue != surfaces[index])
                {
                    return false;
                }
            }

            Component enemyAi = UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Enemy.EnemyAIController");
            Transform sessionRoot = battlefield.Find("Session Defense Layout");
            Component sessionLayout = sessionRoot?.GetComponents<Component>()
                .FirstOrDefault(component => component != null &&
                    component.GetType().FullName == "LlamAcademy.Dinos.Enemy.Defense.SessionDefenseLayoutController");
            Transform slotsRoot = sessionRoot?.Find("Wall Spawn Slots");
            Transform groundRoot = battlefield.Find("Session Ground Defense Layout");
            Component groundLayout = groundRoot?.GetComponents<Component>()
                .FirstOrDefault(component => component != null &&
                    component.GetType().FullName == "LlamAcademy.Dinos.Enemy.Defense.SessionGroundDefenseController");
            SerializedObject sessionData = sessionLayout == null ? null : new SerializedObject(sessionLayout);
            SerializedObject groundData = groundLayout == null ? null : new SerializedObject(groundLayout);
            if (enemyAi == null || sessionRoot == null || !sessionRoot.gameObject.activeSelf ||
                sessionLayout is not Behaviour sessionBehaviour || !sessionBehaviour.enabled ||
                slotsRoot == null || slotsRoot.childCount != 6 ||
                sessionData.FindProperty("Slots")?.arraySize != 6 ||
                sessionData.FindProperty("WallPrefab")?.objectReferenceValue == null ||
                sessionData.FindProperty("ArcherPrefab")?.objectReferenceValue == null ||
                sessionData.FindProperty("SpawnRoot")?.objectReferenceValue != sessionRoot.Find("Spawned Defenses") ||
                sessionData.FindProperty("DefaultStartSeed")?.intValue != 20260829 ||
                !Approximately(sessionData.FindProperty("WallGuardAttackRadius")?.floatValue ?? 0f, 30f) ||
                groundLayout is not Behaviour groundBehaviour || !groundBehaviour.enabled ||
                groundData.FindProperty("DeploymentZones")?.arraySize != 5 ||
                groundData.FindProperty("ArcherPrefab")?.objectReferenceValue == null ||
                groundData.FindProperty("MagePrefab")?.objectReferenceValue == null ||
                groundData.FindProperty("SpawnRoot")?.objectReferenceValue != groundRoot.Find("Spawned Ground Guards") ||
                !Approximately(groundData.FindProperty("MinimumSpacing")?.floatValue ?? 0f, 6f))
            {
                return false;
            }
            SerializedObject ai = new(enemyAi);
            return ai.FindProperty("WaypointTransforms")?.arraySize == 0 && ai.FindProperty("Waypoints")?.arraySize == 0 &&
                   ai.FindProperty("DefenseLayout")?.objectReferenceValue == null &&
                   ai.FindProperty("SessionDefenseLayout")?.objectReferenceValue == sessionLayout &&
                   ai.FindProperty("SessionGroundDefenseLayout")?.objectReferenceValue == groundLayout &&
                   ai.FindProperty("AllowHistoricalTriangulationFallback")?.boolValue == false;
        }

        private static bool DeploymentBindingsAreCanonical(Transform battlefield)
        {
            DinoDeploymentService service = UnityEngine.Object.FindFirstObjectByType<DinoDeploymentService>(FindObjectsInactive.Include);
            DinoDeploymentZonePresenter presenter = UnityEngine.Object.FindFirstObjectByType<DinoDeploymentZonePresenter>(FindObjectsInactive.Include);
            DinoDeploymentZone[] zones = battlefield.Find("Deployment Zones")?.GetComponentsInChildren<DinoDeploymentZone>(true);
            if (service == null || presenter == null || zones == null || !service.ValidateZoneConfiguration() || !presenter.ValidateConfiguration())
            {
                return false;
            }

            SerializedObject serviceData = new(service);
            SerializedObject presenterData = new(presenter);
            if (serviceData.FindProperty("UseLegacyPlacementBounds")?.boolValue != false ||
                presenterData.FindProperty("DeploymentService")?.objectReferenceValue != service)
            {
                return false;
            }

            Component runtimeUi = UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.UI.RuntimeUI");
            Component visualization = UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Player.PlaceDinoVisualization");
            if (runtimeUi == null || visualization == null ||
                new SerializedObject(runtimeUi).FindProperty("ZonePresenter")?.objectReferenceValue != presenter ||
                new SerializedObject(visualization).FindProperty("ZonePresenter")?.objectReferenceValue != presenter)
            {
                return false;
            }

            return service.ConfiguredZones.Count == zones.Length && new HashSet<DinoDeploymentZone>(service.ConfiguredZones).SetEquals(zones);
        }

        private static bool ArtPassIsCanonical(Transform battlefield)
        {
            Transform art = battlefield.Find("Art Pass");
            VisualPolishAudit visualAudit = OpenTropicalBattlefieldVisualPolishBuilder.Audit(battlefield);
            string[] identities = { "Z1 Ruins", "Z2 Meadow", "Z3 Riverbank", "Z4 Palms", "Z5 Rocks" };
            if (art == null || !visualAudit.IsIsolated || identities.Any(name => art.Find(name) == null))
            {
                return false;
            }

            foreach (string identity in identities)
            {
                Transform root = art.Find(identity);
                if (identity == "Z3 Riverbank")
                {
                    if (root.childCount != 0)
                    {
                        return false;
                    }

                    continue;
                }

                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length < 3 || root.GetComponentsInChildren<Collider>(true).Length != 0 ||
                    renderers.Any(renderer => renderer.sharedMaterial == null || renderer.sharedMaterial.shader == null ||
                                             renderer.sharedMaterial.shader.name == "Hidden/InternalErrorShader"))
                {
                    return false;
                }
            }

            Material neutralMaterial = battlefield.Find("Terrain Surfaces/Neutral Ground")
                ?.GetComponent<Renderer>()?.sharedMaterial;
            Material grassMaterial = battlefield.Find("Terrain Surfaces/Grass/Visible Graybox")
                ?.GetComponent<Renderer>()?.sharedMaterial;
            Material waterMaterial = battlefield.Find("Terrain Surfaces/ShallowWater/Visible Graybox")
                ?.GetComponent<Renderer>()?.sharedMaterial;
            Transform houses = art.Find("Village Details/Houses");
            Transform riverbed = battlefield.Find("Terrain Surfaces/ShallowWater/Riverbed");
            Transform villageDetails = art.Find("Village Details");
            Renderer[] grayboxBuildings = battlefield.Find("Village")?.GetComponentsInChildren<Renderer>(true) ??
                                          Array.Empty<Renderer>();
            bool housesAreSafe = houses != null && houses.childCount == 8 &&
                                 houses.Cast<Transform>().All(VisualInstanceIsSafe);
            bool oldRoofsAreGone = villageDetails != null &&
                                   !villageDetails.Cast<Transform>().Any(child =>
                                       child.name.StartsWith("Warm Roof", StringComparison.Ordinal));
            bool grayboxRenderersHidden = grayboxBuildings
                .Where(renderer => renderer.transform.name.StartsWith("Graybox Building", StringComparison.Ordinal))
                .All(renderer => !renderer.enabled);
            return villageDetails?.Find("Village Ground") != null &&
                   art.Find("Canyon Rim/Rim North Gate") != null &&
                   art.Find("Canyon Rim/Navigation Barriers")?.childCount == 0 &&
                   TransformMatches(art.Find("Canyon Rim/Rim North West Mass"), new Vector3(-39f, -2.7f, -69f), new Vector3(0.61f, 0.44f, 0.61f)) &&
                   TransformMatches(art.Find("Canyon Rim/Rim North East Mass"), new Vector3(33f, -2.7f, -69f), new Vector3(0.58f, 0.40f, 0.61f)) &&
                   UsesShader(neutralMaterial, GroundShaderName) &&
                   UsesShader(grassMaterial, GroundShaderName) && neutralMaterial != grassMaterial &&
                   neutralMaterial.renderQueue == (int)UnityEngine.Rendering.RenderQueue.Transparent - 100 &&
                   grassMaterial.renderQueue == (int)UnityEngine.Rendering.RenderQueue.Transparent - 100 &&
                   riverbed == null &&
                   waterMaterial != null && waterMaterial.shader != null &&
                   waterMaterial.shader.name == ShallowWaterShaderName && waterMaterial.GetTexture("_FlowNormal") != null &&
                   housesAreSafe && oldRoofsAreGone && grayboxRenderersHidden &&
                   OpenTropicalBattlefieldVisualPolishBuilder.IsCanonical(
                       SceneManager.GetActiveScene(), battlefield);
        }

        private static bool TransformMatches(Transform transform, Vector3 position, Vector3 scale) =>
            transform != null && Approximately(transform.position, position) && Approximately(transform.localScale, scale);

        private static bool CameraIsCanonical(Scene scene, Transform world)
        {
            Transform boundsTransform = world.Find("Camera Bounds");
            BoxCollider bounds = boundsTransform == null ? null : boundsTransform.GetComponent<BoxCollider>();
            Transform lookTarget = OpenTropicalBattlefieldValidator.Find(scene, "/Look Target");
            Component control = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Component>(true))
                .FirstOrDefault(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Player.CameraControl");
            if (bounds == null || lookTarget == null || control == null || boundsTransform.position != new Vector3(-3.5f, -2.5f, -12f) ||
                boundsTransform.gameObject.layer != 2 || bounds.center != Vector3.zero || bounds.size != new Vector3(104f, 6f, 82f) || !bounds.isTrigger ||
                lookTarget.position != new Vector3(-3.5f, -2.5f, -12f))
            {
                return false;
            }

            SerializedObject controlData = new(control);
            if (controlData.FindProperty("WorldBounds")?.objectReferenceValue != bounds ||
                controlData.FindProperty("EnableMousePan")?.boolValue != true ||
                !Approximately(controlData.FindProperty("KeyboardSpeed")?.floatValue ?? 0f, 20f) ||
                !Approximately(controlData.FindProperty("ZoomSensitivity")?.floatValue ?? 0f, 0.22f) ||
                !Approximately(controlData.FindProperty("MinimumZoomScale")?.floatValue ?? 0f, 0.45f) ||
                !Approximately(controlData.FindProperty("MaximumZoomScale")?.floatValue ?? 0f, 1.35f))
            {
                return false;
            }

            Component camera = control.GetComponents<Component>()
                .FirstOrDefault(component => component != null && component.GetType().FullName == "Unity.Cinemachine.CinemachineCamera");
            Component follow = control.GetComponents<Component>()
                .FirstOrDefault(component => component != null && component.GetType().FullName == "Unity.Cinemachine.CinemachineFollow");
            if (camera == null || follow == null) return false;
            SerializedObject cameraData = new(camera);
            return cameraData.FindProperty("Target.TrackingTarget")?.objectReferenceValue == lookTarget &&
                   Approximately(cameraData.FindProperty("Lens.FieldOfView")?.floatValue ?? 0f, 58f) &&
                   cameraData.FindProperty("Lens.ModeOverride")?.enumValueIndex == 0 &&
                   new SerializedObject(follow).FindProperty("FollowOffset")?.vector3Value == new Vector3(0f, 82f, 38f);
        }

        private static bool NavMeshesAreCanonical(Transform world)
        {
            NavMeshSurface[] surfaces = world.GetComponents<NavMeshSurface>();
            return surfaces.Length == 4 && surfaces.Select(surface => surface.agentTypeID).Distinct().Count() == 4 &&
                   surfaces.All(surface => surface.navMeshData != null && surface.collectObjects == CollectObjects.Volume &&
                       surface.center == new Vector3(-3.5f, 2f, -12f) && surface.size == new Vector3(104f, 10f, 82f) &&
                       surface.layerMask == ((1 << 6) | (1 << 7)) && surface.useGeometry == NavMeshCollectGeometry.PhysicsColliders);
        }

        private static bool SupersededGeometryIsInactive(Transform world, Transform battlefield)
        {
            string[] exactNames = { "Ground", "Archer Waypoints", "Block Dino Spawn", "Village Props", "Smooth Floor" };
            string[] prefixes = { "mountain-desert", "Big Tower with Platform", "Arch Entry Wall", "Ramp_Tribal", "Bridge_Tribal_Tier1" };
            return world.Cast<Transform>().Where(child => child != battlefield &&
                    (exactNames.Contains(child.name) || prefixes.Any(prefix => child.name.StartsWith(prefix, StringComparison.Ordinal))))
                .All(child => !child.gameObject.activeSelf);
        }

        private static bool Approximately(float actual, float expected) => Mathf.Abs(actual - expected) <= 0.0001f;

        private static void BuildVillageGraybox(params Transform[] districts)
        {
            Vector3[][] housePositions =
            {
                new[] { new Vector3(-19f, 1.5f, -46f), new Vector3(-10f, 1.25f, -47f) },
                new[] { new Vector3(15f, 1.5f, -46f), new Vector3(5f, 1.25f, -48f) },
                new[] { new Vector3(-19f, 1.25f, -28f), new Vector3(-11f, 1.5f, -25f) },
                new[] { new Vector3(15f, 1.25f, -28f), new Vector3(6f, 1.5f, -24f) },
                Array.Empty<Vector3>()
            };
            for (int districtIndex = 0; districtIndex < districts.Length; districtIndex++)
            {
                for (int i = 0; i < housePositions[districtIndex].Length; i++)
                {
                    EnsureCube(districts[districtIndex], $"Graybox Building {i + 1}", housePositions[districtIndex][i], new Vector3(5f, 3f, 4f), true);
                }
            }
            EnsureCube(districts[4], "Plaza Marker", new Vector3(-2.23f, -0.18f, -38.26f), new Vector3(14f, 0.12f, 12f), false);
        }

        private static Component[] ConfigureVillageHouseGameplay(Transform battlefield)
        {
            string[] grayboxPaths =
            {
                "Village/NorthWest/Graybox Building 1",
                "Village/NorthWest/Graybox Building 2",
                "Village/NorthEast/Graybox Building 1",
                "Village/NorthEast/Graybox Building 2",
                "Village/SouthWest/Graybox Building 1",
                "Village/SouthWest/Graybox Building 2",
                "Village/SouthEast/Graybox Building 1",
                "Village/SouthEast/Graybox Building 2"
            };
            int[] tiers = { 1, 2, 2, 3, 1, 2, 3, 4 };
            Transform visualRoot = battlefield.Find("Art Pass/Village Details/Houses");
            Transform beamCollection = EnsureChild(
                battlefield.Find("Art Pass/Village Details"),
                "Destroyed House Beams");
            Material beamMaterial = EnsureMaterial(
                "DestroyedHouseBeam",
                new Color(0.42f, 0.18f, 0.055f, 1f),
                0f,
                0.18f);
            UnityEngine.Object buildingUnit = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DefenseWallUnitPath);
            if (visualRoot == null || buildingUnit == null)
            {
                throw new InvalidOperationException("Village visuals or the building UnitSO are missing.");
            }

            Component[] houses = new Component[grayboxPaths.Length];
            for (int index = 0; index < grayboxPaths.Length; index++)
            {
                Transform gameplayRoot = battlefield.Find(grayboxPaths[index]);
                Transform intactVisual = visualRoot.Find($"House {index + 1} - Tier {tiers[index]}");
                if (gameplayRoot == null || intactVisual == null)
                {
                    throw new InvalidOperationException($"House {index + 1} gameplay or visual root is missing.");
                }

                BoxCollider occupancy = gameplayRoot.GetComponent<BoxCollider>();
                if (occupancy == null)
                {
                    occupancy = Undo.AddComponent<BoxCollider>(gameplayRoot.gameObject);
                }
                occupancy.enabled = true;
                gameplayRoot.gameObject.layer = 7;

                Component metadata = GetOrAddRuntimeComponent(
                    gameplayRoot.gameObject,
                    "LlamAcademy.Dinos.Unit.DinoTargetMetadata");
                SerializedObject metadataData = new(metadata);
                RequireProperty(metadataData, "<Category>k__BackingField").enumValueIndex = 0;
                RequireProperty(metadataData, "<StableId>k__BackingField").intValue = index + 1;
                metadataData.ApplyModifiedPropertiesWithoutUndo();
                ((Behaviour)metadata).enabled = true;

                Transform beamRoot = EnsureChild(beamCollection, $"House {index + 1} Beams");
                ConfigureWorldTransform(beamRoot, new Vector3(gameplayRoot.position.x, 0.18f, gameplayRoot.position.z));
                int beamCount = 3 + index % 3;
                for (int beamIndex = 0; beamIndex < beamCount; beamIndex++)
                {
                    GameObject beam = EnsureCube(beamRoot, $"Beam {beamIndex + 1}", Vector3.zero, Vector3.one, false);
                    beam.transform.localPosition = new Vector3(
                        (beamIndex % 3 - 1) * 0.78f,
                        beamIndex * 0.055f,
                        ((beamIndex * 2 + index) % 3 - 1) * 0.62f);
                    beam.transform.localRotation = Quaternion.Euler(
                        0f,
                        (index * 19f + beamIndex * 43f) % 180f,
                        beamIndex % 2 == 0 ? 7f : -9f);
                    beam.transform.localScale = new Vector3(2.8f + beamIndex * 0.16f, 0.22f, 0.28f);
                    beam.GetComponent<MeshRenderer>().sharedMaterial = beamMaterial;
                    beam.layer = 0;
                }
                for (int extraIndex = beamCount + 1; extraIndex <= 5; extraIndex++)
                {
                    Transform extra = beamRoot.Find($"Beam {extraIndex}");
                    if (extra != null)
                    {
                        Undo.DestroyObjectImmediate(extra.gameObject);
                    }
                }
                beamRoot.gameObject.SetActive(false);
                intactVisual.gameObject.SetActive(true);

                GameObject healthBarPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(HealthBarPrefabPath);
                if (healthBarPrefab == null)
                {
                    throw new InvalidOperationException($"House health bar prefab is missing: {HealthBarPrefabPath}");
                }
                Transform healthBarRoot = gameplayRoot.Find("House Health Bar");
                if (healthBarRoot == null)
                {
                    GameObject instance = PrefabUtility.InstantiatePrefab(healthBarPrefab, gameplayRoot.gameObject.scene) as GameObject;
                    if (instance == null)
                    {
                        throw new InvalidOperationException("Could not instantiate the house health bar prefab.");
                    }
                    Undo.RegisterCreatedObjectUndo(instance, "Create house health bar");
                    instance.name = "House Health Bar";
                    instance.transform.SetParent(gameplayRoot, true);
                    healthBarRoot = instance.transform;
                }
                Component healthBar = healthBarRoot.GetComponents<Component>()
                    .FirstOrDefault(component => component != null &&
                        component.GetType().FullName == "LlamAcademy.Dinos.UI.HealthBar");
                if (healthBar == null)
                {
                    throw new InvalidOperationException($"House {index + 1} health bar component is missing.");
                }
                Renderer[] hutRenderers = intactVisual.GetComponentsInChildren<Renderer>(true);
                if (hutRenderers.Length == 0)
                {
                    throw new InvalidOperationException($"House {index + 1} visual has no renderer bounds.");
                }
                Bounds hutBounds = hutRenderers[0].bounds;
                for (int rendererIndex = 1; rendererIndex < hutRenderers.Length; rendererIndex++)
                {
                    hutBounds.Encapsulate(hutRenderers[rendererIndex].bounds);
                }
                SerializedObject healthBarData = new(healthBar);
                RequireProperty(healthBarData, "<FollowOffset>k__BackingField").vector3Value =
                    new Vector3(hutBounds.center.x, hutBounds.max.y + 0.75f, hutBounds.center.z) - gameplayRoot.position;
                RequireProperty(healthBarData, "<OnDeathBehavior>k__BackingField").enumValueIndex = 0;
                healthBarData.ApplyModifiedPropertiesWithoutUndo();
                healthBarRoot.gameObject.SetActive(true);
                EditorUtility.SetDirty(healthBar);

                Component house = GetOrAddRuntimeComponent(
                    gameplayRoot.gameObject,
                    "LlamAcademy.Dinos.Unit.VillageHouse");
                SerializedObject houseData = new(house);
                int maxHealth = tiers[index] switch
                {
                    1 => 100,
                    2 => 150,
                    3 => 225,
                    4 => 325,
                    _ => throw new InvalidOperationException("Unsupported house tier.")
                };
                RequireProperty(houseData, "<StableHouseId>k__BackingField").intValue = index + 1;
                RequireProperty(houseData, "<Tier>k__BackingField").enumValueIndex = tiers[index] - 1;
                RequireProperty(houseData, "<MaxHealth>k__BackingField").intValue = maxHealth;
                RequireProperty(houseData, "<Health>k__BackingField").intValue = maxHealth;
                RequireProperty(houseData, "<UnitType>k__BackingField").objectReferenceValue = buildingUnit;
                RequireProperty(houseData, "<IsDestroyed>k__BackingField").boolValue = false;
                RequireProperty(houseData, "IntactVisualRoot").objectReferenceValue = intactVisual.gameObject;
                RequireProperty(houseData, "BeamRoot").objectReferenceValue = beamRoot.gameObject;
                RequireProperty(houseData, "OccupancyCollider").objectReferenceValue = occupancy;
                RequireProperty(houseData, "TargetMetadata").objectReferenceValue = metadata;
                RequireProperty(houseData, "HealthBar").objectReferenceValue = healthBar;
                houseData.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(house);
                EditorUtility.SetDirty(metadata);
                houses[index] = house;
            }

            return houses;
        }

        private static void ConfigureRoundManagerHouses(Component[] houses)
        {
            Component roundManager = UnityEngine.Object.FindObjectsByType<Component>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .FirstOrDefault(component => component != null
                    && component.GetType().FullName == "LlamAcademy.Dinos.RoundManagement.RoundManager");
            if (roundManager == null)
            {
                throw new InvalidOperationException("Dinos.unity must contain RoundManager.");
            }

            SerializedObject serialized = new(roundManager);
            SerializedProperty property = RequireProperty(serialized, "Houses");
            property.arraySize = houses.Length;
            for (int index = 0; index < houses.Length; index++)
            {
                property.GetArrayElementAtIndex(index).objectReferenceValue = houses[index];
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(roundManager);
        }

        private static Component GetOrAddRuntimeComponent(GameObject gameObject, string fullName)
        {
            Component existing = gameObject.GetComponents<Component>()
                .FirstOrDefault(component => component != null && component.GetType().FullName == fullName);
            if (existing != null)
            {
                return existing;
            }

            Type type = Type.GetType(fullName + ", Assembly-CSharp");
            if (type == null || !typeof(Component).IsAssignableFrom(type))
            {
                throw new InvalidOperationException($"Runtime component type is unavailable: {fullName}");
            }

            return Undo.AddComponent(gameObject, type);
        }

        private static void BuildOuterScenery(Transform parent)
        {
            Vector3[] positions = { new(-49f, 1f, -49f), new(42f, 1f, -49f), new(-49f, 1f, 27f), new(42f, 1f, 27f) };
            for (int i = 0; i < positions.Length; i++)
            {
                EnsureCube(parent, $"Low Perimeter Cliff {i + 1}", positions[i], new Vector3(10f, 2f, 5f), true);
            }
        }

        private static DinoDeploymentZone ConfigureZone(Transform parent, ZoneSpec spec)
        {
            Transform transform = EnsureChild(parent, spec.Name);
            ConfigureWorldTransform(transform, Vector3.zero);
            transform.gameObject.layer = 2; // Ignore Raycast: trigger-only authoring volume, excluded from NavMesh sources.
            BoxCollider collider = transform.GetComponent<BoxCollider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            DinoDeploymentZone zone = GetOrAdd<DinoDeploymentZone>(transform.gameObject);
            zone.ConfigureGeometry(spec.Geometry.A, spec.Geometry.B, spec.Geometry.C, spec.Geometry.D);
            SerializedObject serialized = new(zone);
            serialized.FindProperty("<Id>k__BackingField").enumValueIndex = (int)spec.Id;
            serialized.FindProperty("<DisplayName>k__BackingField").stringValue = spec.DisplayName;
            serialized.FindProperty("<DominantSurface>k__BackingField").enumValueIndex = (int)spec.Surface;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            GameObject boundary = EnsureMeshChild(transform, "Boundary",
                QuadrilateralMeshFactory.CreateOutlineMesh(spec.Geometry, 0.02f, spec.Name + " Boundary"));
            Collider boundaryCollider = boundary.GetComponent<Collider>();
            if (boundaryCollider != null) UnityEngine.Object.DestroyImmediate(boundaryCollider);
            boundary.GetComponent<Renderer>().enabled = false;
            return zone;
        }

        private static TerrainSurfaceVolume ConfigureSurface(Transform parent, ZoneSpec spec)
        {
            Transform transform = EnsureChild(parent, spec.Surface.ToString());
            ConfigureWorldTransform(transform, Vector3.zero);
            transform.gameObject.layer = 2; // Keep terrain triggers from becoming elevated NavMesh geometry.
            MeshCollider trigger = GetOrAdd<MeshCollider>(transform.gameObject);
            trigger.sharedMesh = QuadrilateralMeshFactory.CreateTriggerPrism(spec.Geometry, -0.5f, 1.5f,
                spec.Surface + " Trigger");
            trigger.convex = true;
            trigger.isTrigger = true;
            BoxCollider oldBox = transform.GetComponent<BoxCollider>();
            if (oldBox != null) UnityEngine.Object.DestroyImmediate(oldBox);
            TerrainSurfaceVolume volume = GetOrAdd<TerrainSurfaceVolume>(transform.gameObject);
            SerializedObject serialized = new(volume);
            serialized.FindProperty("surface").enumValueIndex = (int)spec.Surface;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            GameObject visual = EnsureMeshChild(transform, "Visible Graybox",
                QuadrilateralMeshFactory.CreateGroundMesh(spec.Geometry, 0.015f, spec.Surface + " Ground"));
            Collider visualCollider = visual.GetComponent<Collider>();
            if (visualCollider != null) UnityEngine.Object.DestroyImmediate(visualCollider);
            visual.layer = 2;
            return volume;
        }

        private static void ConfigureNeutralGround(Transform parent)
        {
            GameObject neutral = EnsureCube(parent, "Neutral Ground", new Vector3(-3.5f, -0.25f, -12f),
                new Vector3(104f, 0.5f, 82f), true);
            neutral.layer = 6;
            BoxCollider collider = GetOrAdd<BoxCollider>(neutral);
            collider.isTrigger = false;
            TerrainSurfaceVolume volume = neutral.GetComponent<TerrainSurfaceVolume>();
            if (volume != null) UnityEngine.Object.DestroyImmediate(volume);
        }

        private static GameObject EnsureMeshChild(Transform parent, string name, Mesh mesh)
        {
            Transform transform = EnsureChild(parent, name);
            ConfigureWorldTransform(transform, Vector3.zero);
            MeshFilter filter = GetOrAdd<MeshFilter>(transform.gameObject);
            GetOrAdd<MeshRenderer>(transform.gameObject);
            filter.sharedMesh = mesh;
            EditorUtility.SetDirty(filter);
            return transform.gameObject;
        }

        private static void BuildArtPass(
            Scene scene,
            Transform world,
            Transform battlefield,
            Transform village,
            Transform outerScenery,
            Transform zoneRoot,
            Transform terrainRoot)
        {
            Material canyon = EnsureGroundMaterial("CanyonRock",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/MarbleRock03/marble_rock_03_diff_1k.jpg",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/MarbleRock03/marble_rock_03_nor_gl_1k.exr",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/MarbleRock03/marble_rock_03_rough_1k.exr",
                new Color(0.92f, 0.58f, 0.36f, 1f), 2.8f, 44f, 0.30f, false);
            Material sand = EnsureGroundMaterial("VillageSand",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/Sand03/sand_03_diff_1k.jpg",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/Sand03/sand_03_nor_gl_1k.exr",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/Sand03/sand_03_rough_1k.exr",
                new Color(1.00f, 0.82f, 0.60f, 1f), 3.8f, 52f, 0.20f, false);
            Material grass = EnsureGroundMaterial("TropicalGrass",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/ForestGround01/forrest_ground_01_diff_1k.jpg",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/ForestGround01/forrest_ground_01_nor_gl_1k.exr",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/ForestGround01/forrest_ground_01_rough_1k.jpg",
                new Color(0.62f, 0.78f, 0.48f, 1f), 3.2f, 48f, 0.28f, false);
            Material neutralGround = EnsureGroundMaterial("NeutralTropicalGround",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/Dirt/dirt_diff_1k.jpg",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/Dirt/dirt_nor_gl_1k.exr",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/Dirt/dirt_rough_1k.exr",
                new Color(0.82f, 0.70f, 0.52f, 1f), 4.5f, 60f, 0.22f, false);
            Material mud = EnsureGroundMaterial("RiverMud",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/BrownMud03/brown_mud_03_diff_1k.jpg",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/BrownMud03/brown_mud_03_nor_gl_1k.exr",
                "Assets/ThirdParty/PolyHaven/EnvironmentGround/BrownMud03/brown_mud_03_spec_1k.png",
                new Color(0.78f, 0.66f, 0.52f, 1f), 3.4f, 40f, 0.26f, true);
            Material water = EnsureShallowWaterMaterial();
            Material wood = EnsureMaterial("WarmDarkWood", new Color(0.25f, 0.11f, 0.055f), 0f, 0.22f);
            Material boundary = EnsureMaterial("DeploymentBoundary", new Color(0.12f, 0.72f, 0.66f), 0f, 0.45f);

            SetTerrainMaterial(terrainRoot.Find("Grass/Visible Graybox"), grass);
            SetTerrainMaterial(terrainRoot.Find("Neutral Ground"), neutralGround);
            SetTerrainMaterial(terrainRoot.Find("StoneRoad/Visible Graybox"), sand);
            SetTerrainMaterial(terrainRoot.Find("Mud/Visible Graybox"), mud);
            SetTerrainMaterial(terrainRoot.Find("Slope/Visible Graybox"), canyon);
            SetTerrainMaterial(terrainRoot.Find("ShallowWater/Visible Graybox"), water);
            DestroyNamedChildren(terrainRoot.Find("ShallowWater"), "Riverbed");
            foreach (DinoDeploymentZone zone in zoneRoot.GetComponentsInChildren<DinoDeploymentZone>(true))
            {
                SetSharedMaterial(zone.transform.Find("Boundary"), boundary);
            }
            foreach (Transform cliff in outerScenery.Cast<Transform>())
            {
                SetSharedMaterial(cliff, canyon);
            }
            foreach (Renderer renderer in village.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.transform.name.StartsWith("Graybox Building", StringComparison.Ordinal))
                {
                    renderer.enabled = false;
                }
                else if (renderer.transform.name == "Plaza Marker")
                {
                    renderer.sharedMaterial = canyon;
                }
            }

            Transform art = EnsureChild(battlefield, "Art Pass");
            Transform z1 = EnsureChild(art, "Z1 Ruins");
            Transform z2 = EnsureChild(art, "Z2 Meadow");
            Transform z3 = EnsureChild(art, "Z3 Riverbank");
            Transform z4 = EnsureChild(art, "Z4 Palms");
            Transform z5 = EnsureChild(art, "Z5 Rocks");
            Transform villageDetails = EnsureChild(art, "Village Details");
            Transform canyonRim = EnsureChild(art, "Canyon Rim");

            // Keep the entire playable shallow-water quadrilateral visually clear. The water surface,
            // riverbed and trigger live under Terrain Surfaces; Z3 carries no decoration props.
            DestroyAllChildren(z3);

            EnsureVisualCube(villageDetails, "Village Ground", new Vector3(-2.5f, 0.03f, -36f), new Vector3(45f, 0.06f, 29f), 0f, sand);
            EnsureVisualCube(villageDetails, "West Courtyard", new Vector3(-20f, 0.07f, -38f), new Vector3(12f, 0.08f, 12f), -8f, canyon);
            EnsureVisualCube(villageDetails, "East Courtyard", new Vector3(15f, 0.07f, -38f), new Vector3(12f, 0.08f, 12f), 7f, canyon);
            EnsureVisualCube(villageDetails, "South Courtyard", new Vector3(-2f, 0.07f, -26f), new Vector3(18f, 0.08f, 7f), -4f, sand);

            string[] obsoleteRoofNames = Enumerable.Range(1, 8).Select(index => $"Warm Roof {index}").ToArray();
            DestroyNamedChildren(villageDetails, obsoleteRoofNames);
            Transform houses = EnsureChild(villageDetails, "Houses");
            Vector3[] buildingPositions =
            {
                new(-19f, 0f, -46f), new(-10f, 0f, -47f), new(15f, 0f, -46f), new(5f, 0f, -48f),
                new(-19f, 0f, -28f), new(-11f, 0f, -25f), new(15f, 0f, -28f), new(6f, 0f, -24f)
            };
            int[] hutTiers = { 1, 2, 2, 3, 1, 2, 3, 4 };
            float[] hutYaws = { 8f, -12f, -8f, 14f, 12f, -10f, -14f, 7f };
            float[] hutScales = { 0.65f, 0.63f, 0.63f, 0.56f, 0.65f, 0.63f, 0.56f, 0.53f };
            for (int index = 0; index < buildingPositions.Length; index++)
            {
                EnsureVisualPrefab(houses, $"House {index + 1} - Tier {hutTiers[index]}",
                    HutPrefabPaths[hutTiers[index] - 1], buildingPositions[index], hutYaws[index],
                    Vector3.one * hutScales[index], true);
            }

            DestroyNamedChildren(canyonRim,
                "Rim North 1", "Rim North 2", "Rim North 3", "Rim West 1", "Rim West 2",
                "Rim East 1", "Rim East 2", "Rim South West", "Rim South East");
            (string Name, string Path, Vector3 Position, Vector3 Scale, float Yaw)[] rimSegments =
            {
                ("Rim North West Mass", DesertMountainPrefabPath, new Vector3(-39f, -2.7f, -69f), new Vector3(0.61f, 0.44f, 0.61f), 7f),
                ("Rim North Gate", CaveGatePath, new Vector3(-5f, 0f, -51f), Vector3.one * 1.65f, 0f),
                ("Rim North East Mass", DesertMountainPrefabPath, new Vector3(33f, -2.7f, -69f), new Vector3(0.58f, 0.40f, 0.61f), -8f),
                ("Rim West Rocks", MiniForestLowRocksPath, new Vector3(-51f, 0f, -20f), Vector3.one * 2.0f, 88f),
                ("Rim West Corner", CaveCornerPath, new Vector3(-49f, 0f, 10f), Vector3.one * 1.45f, 90f),
                ("Rim East Rocks", MiniForestLowRocksPath, new Vector3(45f, 0f, -18f), Vector3.one * 1.95f, -88f),
                ("Rim East Corner", CaveCornerPath, new Vector3(43f, 0f, 10f), Vector3.one * 1.45f, -90f),
                ("Rim South West", MiniForestRampRocksPath, new Vector3(-35f, 0f, 28f), new Vector3(1.8f, 1.3f, 1.8f), 4f),
                ("Rim South East", MiniForestRampRocksPath, new Vector3(28f, 0f, 28f), new Vector3(1.75f, 1.25f, 1.75f), -4f)
            };
            foreach (var segment in rimSegments)
            {
                GameObject rim = EnsureVisualPrefab(canyonRim, segment.Name, segment.Path, segment.Position,
                    segment.Yaw, segment.Scale, false);
                SetSharedMaterialRecursively(rim.transform, canyon);
            }
            ConfigureNavigationBarriers(canyonRim);

            EnsureDecoration(z1, "Broken Stone A", "Assets/ThirdParty/Kenney/MiniForest/Models/rocks-low.fbx", new Vector3(-47f, 0f, -35f), new Vector3(1.4f, 1.1f, 1.4f), canyon);
            EnsureDecoration(z1, "Broken Stone B", "Assets/ThirdParty/Kenney/TowerDefenseKit/Models/detail-rocks.fbx", new Vector3(-41f, 0f, -37f), new Vector3(1.2f, 1f, 1.2f), canyon);
            EnsureDecoration(z1, "Ruin Scatter", "Assets/ThirdParty/Kenney/MiniForest/Models/stones.fbx", new Vector3(-46f, 0f, -26f), new Vector3(1.3f, 1f, 1.3f), canyon);

            EnsureDecoration(z2, "Meadow Patch A", "Assets/ThirdParty/Kenney/MiniForest/Models/patch-grass.fbx", new Vector3(-34f, 0f, -11f), new Vector3(1.8f, 1f, 1.8f), grass);
            EnsureDecoration(z2, "Meadow Patch B", "Assets/ThirdParty/Kenney/MiniForest/Models/patch-grass.fbx", new Vector3(-23f, 0f, -2f), new Vector3(1.5f, 1f, 1.5f), grass);
            EnsureDecoration(z2, "Meadow Stones", "Assets/ThirdParty/Kenney/MiniForest/Models/stones.fbx", new Vector3(-36f, 0f, 1f), Vector3.one, canyon);

            EnsureDecoration(z4, "Palm A", PalmPrefabPath, new Vector3(18f, 0f, -11f), Vector3.one * 2.1f, grass);
            EnsureDecoration(z4, "Palm B", PalmPrefabPath, new Vector3(27f, 0f, -4f), Vector3.one * 2.35f, grass);
            EnsureDecoration(z4, "Palm C", PalmPrefabPath, new Vector3(31f, 0f, 1f), Vector3.one * 1.9f, grass);

            EnsureDecoration(z5, "Shelf Ramp", "Assets/ThirdParty/Kenney/MiniForest/Models/rocks-ramp.fbx", new Vector3(36f, 0f, -35f), new Vector3(1.4f, 1.1f, 1.4f), canyon);
            EnsureDecoration(z5, "Shelf Rocks", "Assets/ThirdParty/Kenney/MiniForest/Models/rocks-low.fbx", new Vector3(41f, 0f, -29f), new Vector3(1.3f, 1f, 1.3f), canyon);
            EnsureDecoration(z5, "Shelf Stones", "Assets/ThirdParty/Kenney/TowerDefenseKit/Models/detail-rocks.fbx", new Vector3(34f, 0f, -25f), Vector3.one, canyon);

            EnsureDecoration(villageDetails, "Plaza Flag West", "Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Tribal Pack/T/- Prefabs_T/Tribal_T/Flag_Stand_Tribal.prefab", new Vector3(-8f, 0f, -38f), Vector3.one * 0.8f, wood);
            EnsureDecoration(villageDetails, "Plaza Flag East", "Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Tribal Pack/T/- Prefabs_T/Tribal_T/Flag_Stand_Tribal.prefab", new Vector3(4f, 0f, -38f), Vector3.one * 0.8f, wood);
            EnsureDecoration(villageDetails, "Market Detail", "Assets/ThirdParty/Kenney/TowerDefenseKit/Models/selection-b.fbx", new Vector3(-3f, 0f, -31f), Vector3.one * 1.4f, wood);
            EnsureVisualCube(z1, "Ruin Pillar A", new Vector3(-45f, 1.2f, -32f), new Vector3(1.1f, 2.4f, 1.1f), 8f, canyon);
            EnsureVisualCube(z1, "Ruin Pillar B", new Vector3(-40f, 0.8f, -27f), new Vector3(1f, 1.6f, 1f), -12f, canyon);

            OpenTropicalBattlefieldVisualPolishBuilder.Build(
                scene, world, battlefield, terrainRoot, village, outerScenery);
        }

        private static Transform ConfigureNavigationBarriers(Transform canyonRim)
        {
            Transform root = EnsureChild(canyonRim, "Navigation Barriers");
            DestroyAllChildren(root);

            return root;
        }

        private static bool UsesShader(Material material, string shaderName)
        {
            return material != null && material.shader != null && material.shader.name == shaderName;
        }

        private static bool VisualInstanceIsSafe(Transform root)
        {
            return root != null && root.GetComponentsInChildren<Renderer>(true).Length > 0 &&
                   root.GetComponentsInChildren<Collider>(true).Length == 0 &&
                   root.GetComponentsInChildren<Rigidbody>(true).Length == 0 &&
                   root.GetComponentsInChildren<NavMeshObstacle>(true).Length == 0 &&
                   root.GetComponentsInChildren<MonoBehaviour>(true).Length == 0;
        }

        private static Material EnsureGroundMaterial(
            string name,
            string diffusePath,
            string normalPath,
            string surfacePath,
            Color tint,
            float detailScale,
            float macroScale,
            float normalStrength,
            bool surfaceMapIsSpecular)
        {
            Shader shader = Shader.Find(GroundShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Required ground shader was not imported: {GroundShaderName}");
            }

            Texture diffuse = AssetDatabase.LoadAssetAtPath<Texture>(diffusePath);
            Texture normal = AssetDatabase.LoadAssetAtPath<Texture>(normalPath);
            Texture surface = AssetDatabase.LoadAssetAtPath<Texture>(surfacePath);
            if (diffuse == null || normal == null || surface == null)
            {
                throw new InvalidOperationException(
                    $"Ground material '{name}' is missing an approved texture: {diffusePath}, {normalPath}, {surfacePath}");
            }

            string path = $"{Stage8MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetTexture("_BaseMap", diffuse);
            material.SetTexture("_NormalMap", normal);
            material.SetTexture("_SurfaceMap", surface);
            material.SetColor("_BaseColor", tint);
            material.SetFloat("_DetailScale", detailScale);
            material.SetFloat("_MacroScale", macroScale);
            material.SetFloat("_NormalStrength", normalStrength);
            material.SetFloat("_SurfaceMapIsSpecular", surfaceMapIsSpecular ? 1f : 0f);
            material.SetFloat("_SmoothnessMin", 0.04f);
            material.SetFloat("_SmoothnessMax", surfaceMapIsSpecular ? 0.48f : 0.34f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent - 100;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material EnsureMaterial(string name, Color color, float metallic, float smoothness)
        {
            string path = $"{Stage8MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static GameObject EnsureVisualPrefab(
            Transform parent,
            string name,
            string assetPath,
            Vector3 position,
            float yaw,
            Vector3 scale,
            bool preserveSourceMaterials)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (source == null)
            {
                throw new InvalidOperationException($"Required visual prefab was not imported: {assetPath}");
            }

            Transform existing = parent.Find(name);
            GameObject instance = existing == null ? null : existing.gameObject;
            if (instance != null &&
                !string.Equals(PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance), assetPath,
                    StringComparison.Ordinal))
            {
                Undo.DestroyObjectImmediate(instance);
                instance = null;
            }

            if (instance == null)
            {
                instance = PrefabUtility.InstantiatePrefab(source, parent) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException($"Could not instantiate visual prefab: {assetPath}");
                }

                Undo.RegisterCreatedObjectUndo(instance, "Create battlefield visual");
                instance.name = name;
            }

            if (instance.transform.parent != parent)
            {
                Undo.SetTransformParent(instance.transform, parent, "Parent battlefield visual");
            }
            instance.transform.position = position;
            instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            instance.transform.localScale = scale;

            OpenTropicalBattlefieldVisualPolishBuilder.SanitizeVisualHierarchy(
                instance.transform, castShadows: true);
            foreach (Renderer renderer in instance.GetComponentsInChildren<Renderer>(true))
            {
                if (preserveSourceMaterials)
                {
                    EditorUtility.SetDirty(renderer);
                }
            }

            SetLayerRecursively(instance.transform, 0);
            return instance;
        }

        private static void DestroyNamedChildren(Transform parent, params string[] names)
        {
            foreach (string name in names)
            {
                Transform child = parent.Find(name);
                if (child != null)
                {
                    Undo.DestroyObjectImmediate(child.gameObject);
                }
            }
        }

        private static void DestroyAllChildren(Transform parent)
        {
            for (int index = parent.childCount - 1; index >= 0; index--)
            {
                Undo.DestroyObjectImmediate(parent.GetChild(index).gameObject);
            }
        }

        private static void SetSharedMaterialRecursively(Transform root, Material material)
        {
            foreach (Renderer renderer in root.GetComponentsInChildren<Renderer>(true))
            {
                int slotCount = Math.Max(1, renderer.sharedMaterials.Length);
                renderer.sharedMaterials = Enumerable.Repeat(material, slotCount).ToArray();
                EditorUtility.SetDirty(renderer);
            }
        }

        private static Material EnsureShallowWaterMaterial()
        {
            string path = $"{Stage8MaterialFolder}/ShallowWaterTeal.mat";
            Shader shader = Shader.Find(ShallowWaterShaderName);
            if (shader == null)
            {
                throw new InvalidOperationException($"Required shallow-water shader was not imported: {ShallowWaterShaderName}");
            }

            Texture flowNormal = AssetDatabase.LoadAssetAtPath<Texture>(ShallowWaterNormalPath);
            if (flowNormal == null)
            {
                throw new InvalidOperationException($"Required shallow-water normal was not imported: {ShallowWaterNormalPath}");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = "ShallowWaterTeal" };
                AssetDatabase.CreateAsset(material, path);
            }
            else if (material.shader != shader)
            {
                material.shader = shader;
            }

            material.SetColor("_BaseColor", new Color(0.10f, 0.56f, 0.58f, 0.76f));
            material.SetColor("_DeepColor", new Color(0.055f, 0.34f, 0.40f, 0.80f));
            material.SetColor("_FoamColor", new Color(0.65f, 0.90f, 0.82f, 0.75f));
            material.SetTexture("_FlowNormal", flowNormal);
            material.SetFloat("_NormalStrength", 0.22f);
            material.SetFloat("_TilingA", 3.2f);
            material.SetFloat("_TilingB", 4.7f);
            material.SetVector("_PanA", new Vector4(0.018f, 0.010f, 0f, 0f));
            material.SetVector("_PanB", new Vector4(-0.012f, 0.016f, 0f, 0f));
            material.SetFloat("_FoamWidth", 0.065f);
            material.SetFloat("_FresnelPower", 3.5f);
            material.SetFloat("_Smoothness", 0.78f);
            material.SetFloat("_ZWrite", 0f);
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.Transparent;
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureDecoration(
            Transform parent,
            string name,
            string assetPath,
            Vector3 position,
            Vector3 scale,
            Material material)
        {
            GameObject instance = EnsureVisualPrefab(parent, name, assetPath, position, 0f, scale, false);
            SetSharedMaterialRecursively(instance.transform, material);
        }

        private static void SetSharedMaterial(Transform transform, Material material)
        {
            Renderer renderer = transform == null ? null : transform.GetComponent<Renderer>();
            if (renderer != null) renderer.sharedMaterial = material;
        }

        private static void SetTerrainMaterial(Transform transform, Material material)
        {
            Renderer renderer = transform == null ? null : transform.GetComponent<Renderer>();
            if (renderer == null)
            {
                return;
            }

            renderer.sharedMaterial = material;
            renderer.enabled = true;
            EditorUtility.SetDirty(renderer);
        }

        private static void EnsureVisualCube(
            Transform parent,
            string name,
            Vector3 position,
            Vector3 size,
            float yaw,
            Material material)
        {
            GameObject visual = EnsureCube(parent, name, position, size, false);
            visual.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            visual.GetComponent<Renderer>().sharedMaterial = material;
            visual.layer = 0;
        }

        private static GameObject[] BuildDefensePresets(Transform parent)
        {
            GameObject[] presets = new GameObject[2];
            Vector3[][] slots =
            {
                new[] { new Vector3(-16f, 0f, -38f), new Vector3(12f, 0f, -38f), new Vector3(-4f, 0f, -29f) },
                new[] { new Vector3(-10f, 0f, -40f), new Vector3(7f, 0f, -45f), new Vector3(16f, 0f, -31f) }
            };
            for (int i = 0; i < presets.Length; i++)
            {
                Transform preset = EnsureChild(parent, $"Preset {i}");
                presets[i] = preset.gameObject;
                preset.gameObject.SetActive(false);
                Transform slotRoot = EnsureChild(preset, "Spawn Slots");
                for (int j = 0; j < slots[i].Length; j++)
                {
                    Transform slot = EnsureChild(slotRoot, $"Spawn Slot {j + 1}");
                    ConfigureWorldTransform(slot, slots[i][j]);
                }
                Vector3 barrierPosition = i == 0 ? new Vector3(-21f, 0.65f, -36f) : new Vector3(18f, 0.65f, -36f);
                EnsureDefenseWall(preset, new Vector3(barrierPosition.x, -1.4f, barrierPosition.z));
            }
            int selected = DefensePresetSelector.SelectIndex(20260828, 0, presets.Length);
            for (int i = 0; i < presets.Length; i++) presets[i].SetActive(i == selected);
            return presets;
        }

        private static Component EnsureDefenseWall(Transform preset, Vector3 position)
        {
            Transform obsoleteGraybox = preset.Find("Short Military Barrier");
            if (obsoleteGraybox != null)
            {
                Undo.DestroyObjectImmediate(obsoleteGraybox.gameObject);
            }

            const string instanceName = "Military Defense Wall";
            Transform existing = preset.Find(instanceName);
            Component wall = existing == null ? null : FindWall(existing.gameObject);
            if (existing != null && wall == null)
            {
                Undo.DestroyObjectImmediate(existing.gameObject);
                existing = null;
            }

            if (existing == null)
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(DefenseWallPrefabPath);
                if (prefab == null || FindWall(prefab) == null)
                {
                    throw new InvalidOperationException($"Defense wall prefab is missing a Wall root: {DefenseWallPrefabPath}");
                }

                GameObject instance = PrefabUtility.InstantiatePrefab(prefab, preset.gameObject.scene) as GameObject;
                if (instance == null)
                {
                    throw new InvalidOperationException($"Failed to instantiate defense wall prefab: {DefenseWallPrefabPath}");
                }

                Undo.RegisterCreatedObjectUndo(instance, "Create deterministic defense wall");
                instance.name = instanceName;
                instance.transform.SetParent(preset, true);
                existing = instance.transform;
                wall = FindWall(instance);
            }

            ConfigureWorldTransform(existing, position);
            existing.localScale = Vector3.one * 0.5f;
            SanitizeDefenseWallInstance(wall, DefenseWallUnitPath);
            SetLayerRecursively(existing, 7); // Active preset walls participate in the controlled static/runtime rebake.
            foreach (NavMeshObstacle obstacle in existing.GetComponentsInChildren<NavMeshObstacle>(true))
            {
                obstacle.enabled = false;
                obstacle.carving = false;
                EditorUtility.SetDirty(obstacle);
            }

            return wall;
        }

        private static Component BuildSessionDefenseLayout(Transform battlefield)
        {
            Transform layoutRoot = EnsureChild(battlefield, "Session Defense Layout");
            Transform slotsRoot = EnsureChild(layoutRoot, "Wall Spawn Slots");
            Transform spawnedRoot = EnsureChild(layoutRoot, "Spawned Defenses");
            Vector3[] positions =
            {
                new(-26f, 0f, -45f),
                new(-26f, 0f, -31f),
                new(-12f, 0f, -21f),
                new(7f, 0f, -25f),
                new(22f, 0f, -31f),
                new(22f, 0f, -45f)
            };
            Component[] slots = new Component[positions.Length];
            Vector3 villageCenter = new(-2f, 0f, -36f);
            for (int index = 0; index < positions.Length; index++)
            {
                Transform slot = EnsureChild(slotsRoot, $"Wall Slot {index + 1}");
                Vector3 outward = (positions[index] - villageCenter).normalized;
                ConfigureWorldTransform(slot, positions[index]);
                slot.rotation = Quaternion.LookRotation(outward.sqrMagnitude < 0.01f ? Vector3.forward : outward);
                Transform guardAnchor = EnsureChild(slot, "Guard Anchor");
                guardAnchor.localPosition = new Vector3(0f, 0f, -2.2f);
                guardAnchor.localRotation = Quaternion.Euler(0f, 180f, 0f);
                guardAnchor.localScale = Vector3.one;

                Component slotComponent = GetOrAddRuntimeComponent(
                    slot.gameObject,
                    "LlamAcademy.Dinos.Enemy.Defense.WallSpawnSlot");
                SerializedObject slotData = new(slotComponent);
                RequireProperty(slotData, "<StableId>k__BackingField").intValue = index + 1;
                RequireProperty(slotData, "<GuardAnchor>k__BackingField").objectReferenceValue = guardAnchor;
                slotData.ApplyModifiedPropertiesWithoutUndo();
                EditorUtility.SetDirty(slotComponent);
                slots[index] = slotComponent;
            }

            GameObject wallPrefabObject = AssetDatabase.LoadAssetAtPath<GameObject>(DefenseWallPrefabPath);
            Component wallPrefab = wallPrefabObject == null ? null : FindWall(wallPrefabObject);
            UnityEngine.Object archerUnit = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ArcherUnitPath);
            SerializedObject archerUnitData = archerUnit == null ? null : new SerializedObject(archerUnit);
            UnityEngine.Object archerPrefab = archerUnitData?.FindProperty("<Prefab>k__BackingField")?.objectReferenceValue;
            if (wallPrefab == null || archerPrefab == null)
            {
                throw new InvalidOperationException("Session wall or archer prefab is missing.");
            }

            Component controller = GetOrAddRuntimeComponent(
                layoutRoot.gameObject,
                "LlamAcademy.Dinos.Enemy.Defense.SessionDefenseLayoutController");
            SerializedObject controllerData = new(controller);
            SerializedProperty slotArray = RequireProperty(controllerData, "Slots");
            slotArray.arraySize = slots.Length;
            for (int index = 0; index < slots.Length; index++)
            {
                slotArray.GetArrayElementAtIndex(index).objectReferenceValue = slots[index];
            }
            RequireProperty(controllerData, "WallPrefab").objectReferenceValue = wallPrefab;
            RequireProperty(controllerData, "ArcherPrefab").objectReferenceValue = archerPrefab;
            RequireProperty(controllerData, "SpawnRoot").objectReferenceValue = spawnedRoot;
            RequireProperty(controllerData, "DefaultStartSeed").intValue = 20260829;
            RequireProperty(controllerData, "WallGuardAttackRadius").floatValue = 30f;
            controllerData.ApplyModifiedPropertiesWithoutUndo();
            ((Behaviour)controller).enabled = true;
            layoutRoot.gameObject.SetActive(true);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static Component BuildSessionGroundDefenseLayout(Transform battlefield, DinoDeploymentZone[] zones)
        {
            Transform layoutRoot = EnsureChild(battlefield, "Session Ground Defense Layout");
            Transform spawnedRoot = EnsureChild(layoutRoot, "Spawned Ground Guards");
            UnityEngine.Object archerUnit = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(ArcherUnitPath);
            UnityEngine.Object mageUnit = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(MageUnitPath);
            UnityEngine.Object archerPrefab = archerUnit == null
                ? null
                : new SerializedObject(archerUnit).FindProperty("<Prefab>k__BackingField")?.objectReferenceValue;
            UnityEngine.Object magePrefab = mageUnit == null
                ? null
                : new SerializedObject(mageUnit).FindProperty("<Prefab>k__BackingField")?.objectReferenceValue;
            if (archerPrefab == null || magePrefab == null)
            {
                throw new InvalidOperationException("Ground Archer or Mage prefab is missing.");
            }

            Component controller = GetOrAddRuntimeComponent(
                layoutRoot.gameObject,
                "LlamAcademy.Dinos.Enemy.Defense.SessionGroundDefenseController");
            SerializedObject data = new(controller);
            SerializedProperty zoneArray = RequireProperty(data, "DeploymentZones");
            zoneArray.arraySize = zones.Length;
            for (int index = 0; index < zones.Length; index++)
            {
                zoneArray.GetArrayElementAtIndex(index).objectReferenceValue = zones[index];
            }
            RequireProperty(data, "ArcherPrefab").objectReferenceValue = archerPrefab;
            RequireProperty(data, "MagePrefab").objectReferenceValue = magePrefab;
            RequireProperty(data, "SpawnRoot").objectReferenceValue = spawnedRoot;
            RequireProperty(data, "FaceTarget").objectReferenceValue = battlefield.Find("Village/Central Plaza");
            RequireProperty(data, "SpawnMin").vector2Value = new Vector2(-24f, -49f);
            RequireProperty(data, "SpawnMax").vector2Value = new Vector2(20f, -21f);
            RequireProperty(data, "MinimumSpacing").floatValue = 6f;
            RequireProperty(data, "SpawnClearance").floatValue = 2.5f;
            RequireProperty(data, "NavMeshSampleRadius").floatValue = 0.75f;
            RequireProperty(data, "MaximumAttempts").intValue = 512;
            RequireProperty(data, "BlockingLayers").intValue = 1 << 7;
            RequireProperty(data, "DefaultStartSeed").intValue = 20260829;
            data.ApplyModifiedPropertiesWithoutUndo();
            ((Behaviour)controller).enabled = true;
            layoutRoot.gameObject.SetActive(true);
            EditorUtility.SetDirty(controller);
            return controller;
        }

        private static void SanitizeDefenseWallInstance(Component wall, string unitPath)
        {
            if (wall == null)
            {
                throw new InvalidOperationException("A deterministic defense wall instance has no Wall component.");
            }

            SerializedObject serialized = new(wall);
            SerializedProperty root = RequireProperty(serialized, "Root");
            root.objectReferenceValue = wall.gameObject;
            UnityEngine.Object unit = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unitPath);
            if (unit == null)
            {
                throw new InvalidOperationException($"Safe defense wall UnitSO was not found: {unitPath}");
            }
            RequireProperty(serialized, "<UnitType>k__BackingField").objectReferenceValue = unit;
            Component healthBar = wall.GetComponentsInChildren<Component>(true)
                .FirstOrDefault(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.UI.HealthBar");
            if (healthBar == null)
            {
                throw new InvalidOperationException("Safe defense wall prefab lost its required HealthBar child.");
            }
            RequireProperty(serialized, "HealthBar").objectReferenceValue = healthBar;
            RequireProperty(serialized, "UpdateNavMeshData.<Obstacles>k__BackingField").arraySize = 0;
            RequireProperty(serialized, "UpdateNavMeshData.<RebakeSurfacesOnDeath>k__BackingField").arraySize = 0;
            RequireProperty(serialized, "UpdateNavMeshData.<EnableObjectsBeforeRebake>k__BackingField").arraySize = 0;
            RequireProperty(serialized, "UpdateNavMeshData.<EnableComponentsBeforeRebake>k__BackingField").arraySize = 0;
            RequireProperty(serialized, "UpdateNavMeshData.<DeathObstacleHeight>k__BackingField").floatValue = 0f;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            foreach (Component component in wall.GetComponentsInChildren<Component>(true))
            {
                if (component != null && component.GetType().FullName == "LlamAcademy.Dinos.UI.HealthBar")
                {
                    component.gameObject.SetActive(true);
                    EditorUtility.SetDirty(component.gameObject);
                }
            }
        }

        private static void EnsureSafeDefenseAssets()
        {
            if (!AssetDatabase.IsValidFolder(GeneratedFolder))
            {
                string parent = "Assets/LlamAcademy/Dinos/Map";
                AssetDatabase.CreateFolder(parent, "Generated");
            }

            CopyAssetOnce(SourceWallPrefabPath, DefenseWallPrefabPath);
            CopyAssetOnce(SourceUpgradePrefabPath, DefenseUpgradePrefabPath);
            CopyAssetOnce(SourceWallUnitPath, DefenseWallUnitPath);
            CopyAssetOnce(SourceUpgradeUnitPath, DefenseUpgradeUnitPath);

            ConfigureSafePrefab(DefenseWallPrefabPath, DefenseWallUnitPath);
            ConfigureSafePrefab(DefenseUpgradePrefabPath, DefenseUpgradeUnitPath);
            ConfigureSafeUnit(DefenseWallUnitPath, DefenseWallPrefabPath, DefenseUpgradeUnitPath);
            ConfigureSafeUnit(DefenseUpgradeUnitPath, DefenseUpgradePrefabPath, null);
            AssetDatabase.SaveAssets();
        }

        private static void CopyAssetOnce(string source, string destination)
        {
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(destination) == null && !AssetDatabase.CopyAsset(source, destination))
            {
                throw new InvalidOperationException($"Could not create safe defense asset '{destination}' from '{source}'.");
            }
        }

        private static void ConfigureSafePrefab(string prefabPath, string unitPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            try
            {
                Component wall = FindWall(root);
                SanitizeDefenseWallInstance(wall, unitPath);
                SetLayerRecursively(root.transform, 7);
                foreach (NavMeshObstacle obstacle in root.GetComponentsInChildren<NavMeshObstacle>(true))
                {
                    obstacle.enabled = false;
                    obstacle.carving = false;
                }
                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static void ConfigureSafeUnit(string unitPath, string prefabPath, string upgradePath)
        {
            UnityEngine.Object unit = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(unitPath);
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            SerializedObject serialized = new(unit);
            RequireProperty(serialized, "<Prefab>k__BackingField").objectReferenceValue = FindWall(prefab);
            RequireProperty(serialized, "<Upgrade>k__BackingField").objectReferenceValue =
                string.IsNullOrEmpty(upgradePath) ? null : AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(upgradePath);
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(unit);
        }

        private static Component FindWall(GameObject root) => root.GetComponentsInChildren<Component>(true)
            .FirstOrDefault(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Unit.Wall");

        private static SerializedProperty RequireProperty(SerializedObject serialized, string path)
        {
            SerializedProperty property = serialized.FindProperty(path);
            if (property == null)
            {
                throw new InvalidOperationException($"Required serialized Wall property was not found: {path}");
            }

            return property;
        }

        private static void SetLayerRecursively(Transform root, int layer)
        {
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                transform.gameObject.layer = layer;
                EditorUtility.SetDirty(transform.gameObject);
            }
        }

        private static void ConfigureDeployment(DinoDeploymentZone[] zones)
        {
            DinoDeploymentService service = UnityEngine.Object.FindFirstObjectByType<DinoDeploymentService>(FindObjectsInactive.Include);
            DinoDeploymentZonePresenter presenter = UnityEngine.Object.FindFirstObjectByType<DinoDeploymentZonePresenter>(FindObjectsInactive.Include);
            if (service == null)
            {
                throw new InvalidOperationException("Dinos.unity must contain the existing deployment service.");
            }
            if (presenter == null)
            {
                presenter = Undo.AddComponent<DinoDeploymentZonePresenter>(service.gameObject);
            }

            SetObjectArray(service, "PlacementZones", zones.Cast<UnityEngine.Object>().ToArray());
            SetBool(service, "UseLegacyPlacementBounds", false);
            Transform barrierRoot = OpenTropicalBattlefieldValidator.Find(
                service.gameObject.scene,
                "/World/Open Tropical Battlefield/Art Pass/Canyon Rim/Navigation Barriers");
            if (barrierRoot == null)
            {
                throw new InvalidOperationException("Navigation barrier root is missing.");
            }
            SerializedObject serializedService = new(service);
            RequireProperty(serializedService, "NavigationBarrierRoot").objectReferenceValue = barrierRoot;
            serializedService.ApplyModifiedPropertiesWithoutUndo();

            SerializedObject serializedPresenter = new(presenter);
            serializedPresenter.FindProperty("DeploymentService").objectReferenceValue = service;
            SerializedProperty bindings = serializedPresenter.FindProperty("Bindings");
            bindings.arraySize = zones.Length;
            for (int i = 0; i < zones.Length; i++)
            {
                SerializedProperty binding = bindings.GetArrayElementAtIndex(i);
                binding.FindPropertyRelative("Zone").objectReferenceValue = zones[i];
                binding.FindPropertyRelative("BoundaryRenderer").objectReferenceValue = zones[i].transform.Find("Boundary").GetComponent<Renderer>();
            }
            serializedPresenter.ApplyModifiedPropertiesWithoutUndo();
            if (!service.ValidateZoneConfiguration() || !presenter.ValidateConfiguration())
            {
                throw new InvalidOperationException("Five-zone deployment service/presenter wiring did not validate.");
            }
        }

        private static void ConfigureRuntimeUi()
        {
            DinoDeploymentZonePresenter presenter = UnityEngine.Object.FindFirstObjectByType<DinoDeploymentZonePresenter>(FindObjectsInactive.Include);
            Component[] components = UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            Component runtimeUi = components.FirstOrDefault(component =>
                component != null && component.GetType().FullName == "LlamAcademy.Dinos.UI.RuntimeUI");
            Component visualization = components.FirstOrDefault(component =>
                component != null && component.GetType().FullName == "LlamAcademy.Dinos.Player.PlaceDinoVisualization");
            if (presenter == null || runtimeUi == null || visualization == null)
            {
                throw new InvalidOperationException("Runtime UI, placement visualization, or deployment-zone presenter is missing.");
            }

            SerializedObject serializedUi = new(runtimeUi);
            RequireProperty(serializedUi, "ZonePresenter").objectReferenceValue = presenter;
            serializedUi.ApplyModifiedPropertiesWithoutUndo();
            SerializedObject serializedVisualization = new(visualization);
            RequireProperty(serializedVisualization, "ZonePresenter").objectReferenceValue = presenter;
            serializedVisualization.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(runtimeUi);
            EditorUtility.SetDirty(visualization);
        }

        private static void ConfigureSessionRestartService()
        {
            Component roundManager = UnityEngine.Object.FindObjectsByType<Component>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .FirstOrDefault(component => component != null
                    && component.GetType().FullName == "LlamAcademy.Dinos.RoundManagement.RoundManager");
            if (roundManager == null)
            {
                throw new InvalidOperationException("Dinos.unity must contain RoundManager.");
            }

            Component restartService = GetOrAddRuntimeComponent(
                roundManager.gameObject,
                "LlamAcademy.Dinos.RoundManagement.GameSessionRestartService");
            SerializedObject serialized = new(restartService);
            RequireProperty(serialized, "InitialStartSeed").intValue = 20260829;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            ((Behaviour)restartService).enabled = true;
            EditorUtility.SetDirty(restartService);
        }

        private static void ConfigureEnemyAiForNewMap(Component sessionLayout, Component sessionGroundLayout)
        {
            Component enemyAi = UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Enemy.EnemyAIController");
            if (enemyAi == null) throw new InvalidOperationException("Dinos.unity must contain EnemyAIController.");
            SerializedObject serialized = new(enemyAi);
            RequireProperty(serialized, "WaypointTransforms").arraySize = 0;
            RequireProperty(serialized, "Waypoints").arraySize = 0;
            RequireProperty(serialized, "DefenseLayout").objectReferenceValue = null;
            RequireProperty(serialized, "SessionDefenseLayout").objectReferenceValue = sessionLayout;
            RequireProperty(serialized, "SessionGroundDefenseLayout").objectReferenceValue = sessionGroundLayout;
            RequireProperty(serialized, "AllowHistoricalTriangulationFallback").boolValue = false;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(enemyAi);
        }

        private static void ConfigureCameraBounds(Transform world)
        {
            Transform boundsTransform = world.Find("Camera Bounds");
            if (boundsTransform == null) throw new InvalidOperationException("Existing /World/Camera Bounds was not found.");
            ConfigureWorldTransform(boundsTransform, new Vector3(-3.5f, -2.5f, -12f));
            boundsTransform.gameObject.layer = 2;
            BoxCollider bounds = boundsTransform.GetComponent<BoxCollider>();
            bounds.center = Vector3.zero;
            bounds.size = new Vector3(104f, 6f, 82f);
            bounds.isTrigger = true;
            EditorUtility.SetDirty(bounds);
        }

        private static void ConfigureFullIslandCamera(Scene scene, Transform world)
        {
            Transform lookTarget = OpenTropicalBattlefieldValidator.Find(scene, "/Look Target");
            Component control = scene.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Component>(true))
                .FirstOrDefault(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Player.CameraControl");
            BoxCollider bounds = world.Find("Camera Bounds")?.GetComponent<BoxCollider>();
            if (lookTarget == null || control == null || bounds == null)
            {
                throw new InvalidOperationException("Full-island camera objects or Camera Bounds are missing.");
            }

            ConfigureWorldTransform(lookTarget, new Vector3(-3.5f, -2.5f, -12f));
            SerializedObject controlData = new(control);
            RequireProperty(controlData, "WorldBounds").objectReferenceValue = bounds;
            RequireProperty(controlData, "EnableMousePan").boolValue = true;
            RequireProperty(controlData, "KeyboardSpeed").floatValue = 20f;
            RequireProperty(controlData, "ZoomSensitivity").floatValue = 0.22f;
            RequireProperty(controlData, "MinimumZoomScale").floatValue = 0.45f;
            RequireProperty(controlData, "MaximumZoomScale").floatValue = 1.35f;
            controlData.ApplyModifiedPropertiesWithoutUndo();

            Component camera = control.GetComponents<Component>()
                .FirstOrDefault(component => component != null && component.GetType().FullName == "Unity.Cinemachine.CinemachineCamera");
            Component follow = control.GetComponents<Component>()
                .FirstOrDefault(component => component != null && component.GetType().FullName == "Unity.Cinemachine.CinemachineFollow");
            if (camera == null || follow == null)
            {
                throw new InvalidOperationException("Full-island camera is missing Cinemachine camera/follow components.");
            }

            SerializedObject cameraData = new(camera);
            RequireProperty(cameraData, "Target.TrackingTarget").objectReferenceValue = lookTarget;
            RequireProperty(cameraData, "Lens.FieldOfView").floatValue = 58f;
            RequireProperty(cameraData, "Lens.ModeOverride").enumValueIndex = 0;
            cameraData.ApplyModifiedPropertiesWithoutUndo();
            SerializedObject followData = new(follow);
            RequireProperty(followData, "FollowOffset").vector3Value = new Vector3(0f, 82f, 38f);
            followData.ApplyModifiedPropertiesWithoutUndo();
        }

        private static NavMeshSurface[] ConfigureNavMeshes(Transform world)
        {
            NavMeshSurface[] surfaces = world.GetComponents<NavMeshSurface>();
            if (surfaces.Select(surface => surface.agentTypeID).Distinct().Count() < 4)
            {
                throw new InvalidOperationException("Expected defender plus three gameplay NavMeshSurface agent types on /World.");
            }
            foreach (NavMeshSurface surface in surfaces)
            {
                surface.RemoveData();
                surface.collectObjects = CollectObjects.Volume;
                surface.center = new Vector3(-3.5f, 2f, -12f);
                surface.size = new Vector3(104f, 10f, 82f);
                // Existing Ground/Wall layers, after superseded roots have been safely deactivated.
                surface.layerMask = (1 << 6) | (1 << 7);
                surface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
                surface.BuildNavMesh();
                EditorUtility.SetDirty(surface);
            }

            return surfaces;
        }

        private static void DeactivateSupersededWorldGeometry(Transform world, Transform battlefield)
        {
            string[] exactNames =
            {
                "Ground", "Archer Waypoints", "Block Dino Spawn", "Village Props", "Smooth Floor"
            };
            string[] prefixes =
            {
                "mountain-desert", "Big Tower with Platform", "Arch Entry Wall", "Ramp_Tribal", "Bridge_Tribal_Tier1"
            };
            for (int i = 0; i < world.childCount; i++)
            {
                Transform child = world.GetChild(i);
                if (child == battlefield || !child.gameObject.activeSelf)
                {
                    continue;
                }
                bool superseded = exactNames.Contains(child.name) ||
                                  prefixes.Any(prefix => child.name.StartsWith(prefix, StringComparison.Ordinal));
                if (!superseded)
                {
                    continue;
                }
                Undo.RecordObject(child.gameObject, "Deactivate superseded battlefield geometry");
                child.gameObject.SetActive(false);
                EditorUtility.SetDirty(child.gameObject);
            }
        }

        private static Transform EnsureChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) return child;
            GameObject created = new(name);
            Undo.RegisterCreatedObjectUndo(created, "Build Open Tropical Battlefield");
            created.transform.SetParent(parent, false);
            return created.transform;
        }

        private static GameObject EnsureCube(Transform parent, string name, Vector3 position, Vector3 size, bool keepCollider)
        {
            Transform existing = parent.Find(name);
            GameObject cube;
            if (existing == null)
            {
                cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
                cube.name = name;
                Undo.RegisterCreatedObjectUndo(cube, "Build Open Tropical Battlefield graybox");
                cube.transform.SetParent(parent, true);
            }
            else cube = existing.gameObject;
            ConfigureWorldTransform(cube.transform, position);
            cube.transform.localScale = size;
            BoxCollider collider = cube.GetComponent<BoxCollider>();
            if (keepCollider && collider == null) collider = cube.AddComponent<BoxCollider>();
            if (!keepCollider && collider != null) UnityEngine.Object.DestroyImmediate(collider);
            cube.layer = keepCollider ? 7 : 0;
            return cube;
        }

        private static void ConfigureWorldTransform(Transform transform, Vector3 position)
        {
            transform.position = position;
            transform.rotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            EditorUtility.SetDirty(transform);
        }

        private static T GetOrAdd<T>(GameObject gameObject) where T : Component =>
            gameObject.TryGetComponent(out T existing) ? existing : Undo.AddComponent<T>(gameObject);

        private static void SetObjectArray(UnityEngine.Object target, string field, UnityEngine.Object[] values)
        {
            SerializedObject serialized = new(target);
            SerializedProperty property = serialized.FindProperty(field);
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++) property.GetArrayElementAtIndex(i).objectReferenceValue = values[i];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(UnityEngine.Object target, string field, bool value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(field).boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(UnityEngine.Object target, string field, int value)
        {
            SerializedObject serialized = new(target);
            serialized.FindProperty(field).intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
