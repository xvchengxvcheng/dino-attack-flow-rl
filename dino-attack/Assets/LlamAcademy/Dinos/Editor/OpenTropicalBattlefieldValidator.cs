using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using LlamAcademy.Dinos.Deployment;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace LlamAcademy.Dinos.Map.Editor
{
    [Serializable]
    public class OpenTropicalPathRecord
    {
        public string ZoneId;
        public int AgentTypeId;
        public bool StartSampled;
        public bool GoalSampled;
        public string Status;
        public float Length;
    }

    [Serializable]
    public sealed class DefenderSlotPathRecord : OpenTropicalPathRecord
    {
        public string Slot;
    }

    [Serializable]
    public sealed class OpenTropicalPresetNavigationReport
    {
        public int PresetIndex;
        public string PresetId;
        public OpenTropicalPathRecord[] Paths = Array.Empty<OpenTropicalPathRecord>();
        public DefenderSlotPathRecord[] DefenderSlotPaths = Array.Empty<DefenderSlotPathRecord>();
    }

    [Serializable]
    public sealed class OpenTropicalBattlefieldReport
    {
        public string ScenePath;
        public string[] MissingHierarchyPaths = Array.Empty<string>();
        public DeploymentZoneId[] ZoneIds = Array.Empty<DeploymentZoneId>();
        public TerrainSurfaceKind[] SurfaceKinds = Array.Empty<TerrainSurfaceKind>();
        public int ObjectiveCount;
        public int CameraBoundsCount;
        public int DefensePresetCount;
        public int ActiveDefensePresetCount;
        public int[] DefensePresetWallCounts = Array.Empty<int>();
        public int ActiveDefenseWallCount;
        public bool DefenseWallsSanitized;
        public int VillageHouseCount;
        public int NavigationBarrierCount;
        public bool RimVisualOnly;
        public int WallSpawnSlotCount;
        public bool GroundDefenseConfigured;
        public bool ServiceUsesExactZoneInstances;
        public bool PresenterUsesServiceZones;
        public bool LegacyStrategyActive;
        public bool HasForbiddenDeploymentZone;
        public bool VisualLayerIsolated;
        public bool Z3DecorationEmpty;
        public int VisualColliderCount;
        public int VisualTriggerCount;
        public int VisualRigidbodyCount;
        public int VisualNavMeshComponentCount;
        public int VisualAgentCount;
        public int VisualGameplayBehaviourCount;
        public string[] VisualViolatingPaths = Array.Empty<string>();
        public float BoundsAreaIncreasePercent;
        public bool IsStructurallyReadyToDeactivateLegacy;
        public OpenTropicalPathRecord[] Paths = Array.Empty<OpenTropicalPathRecord>();
        public DefenderSlotPathRecord[] DefenderSlotPaths = Array.Empty<DefenderSlotPathRecord>();
        public int CompletePathCount;
        public bool IsValid;
        public string StructuralSignature;
        public string FrozenGameplaySignature;
        public string Summary;
    }

    public static class OpenTropicalBattlefieldValidator
    {
        public const string BattlefieldPath = "/World/Open Tropical Battlefield";
        private const float BaselineArea = 92f * 71f;

        private static readonly string[] RequiredPaths =
        {
            BattlefieldPath,
            BattlefieldPath + "/Outer Scenery",
            BattlefieldPath + "/Village/NorthWest",
            BattlefieldPath + "/Village/NorthEast",
            BattlefieldPath + "/Village/SouthWest",
            BattlefieldPath + "/Village/SouthEast",
            BattlefieldPath + "/Village/Central Plaza",
            BattlefieldPath + "/Deployment Zones/Z1",
            BattlefieldPath + "/Deployment Zones/Z2",
            BattlefieldPath + "/Deployment Zones/Z3",
            BattlefieldPath + "/Deployment Zones/Z4",
            BattlefieldPath + "/Deployment Zones/Z5",
            BattlefieldPath + "/Terrain Surfaces/Grass",
            BattlefieldPath + "/Terrain Surfaces/StoneRoad",
            BattlefieldPath + "/Terrain Surfaces/Mud",
            BattlefieldPath + "/Terrain Surfaces/Slope",
            BattlefieldPath + "/Terrain Surfaces/ShallowWater",
            BattlefieldPath + "/Terrain Surfaces/Neutral Ground",
            BattlefieldPath + "/Defense Presets",
            BattlefieldPath + "/Session Defense Layout/Wall Spawn Slots",
            BattlefieldPath + "/Session Ground Defense Layout/Spawned Ground Guards",
            BattlefieldPath + "/Art Pass/Canyon Rim/Navigation Barriers",
            BattlefieldPath + "/Art Pass/Lighting & Atmosphere",
            BattlefieldPath + "/Art Pass/Terrain Dressing",
            BattlefieldPath + "/Art Pass/Terrain Dressing/Z1 Ruins",
            BattlefieldPath + "/Art Pass/Terrain Dressing/Z2 Meadow",
            BattlefieldPath + "/Art Pass/Terrain Dressing/Z4 Palms",
            BattlefieldPath + "/Art Pass/Terrain Dressing/Z5 Rocks",
            BattlefieldPath + "/Art Pass/Canyon Rim/Distant Scenery"
        };

        public static OpenTropicalBattlefieldReport ValidateLoadedScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            OpenTropicalBattlefieldReport report = new() { ScenePath = scene.path };
            Transform[] all = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<Transform>(true)).ToArray();
            report.MissingHierarchyPaths = RequiredPaths.Where(path => Find(scene, path) == null).ToArray();

            Transform zoneRoot = Find(scene, BattlefieldPath + "/Deployment Zones");
            DinoDeploymentZone[] zones = zoneRoot == null
                ? Array.Empty<DinoDeploymentZone>()
                : zoneRoot.GetComponentsInChildren<DinoDeploymentZone>(true);
            report.ZoneIds = zones.Where(zone => zone != null).Select(zone => zone.Id).ToArray();

            Transform surfaceRoot = Find(scene, BattlefieldPath + "/Terrain Surfaces");
            TerrainSurfaceVolume[] volumes = surfaceRoot == null
                ? Array.Empty<TerrainSurfaceVolume>()
                : surfaceRoot.GetComponentsInChildren<TerrainSurfaceVolume>(true);
            report.SurfaceKinds = volumes.Where(volume => volume != null).Select(volume => volume.Surface).ToArray();

            Transform plaza = Find(scene, BattlefieldPath + "/Village/Central Plaza");
            report.ObjectiveCount = plaza == null ? 0 : plaza.GetComponentsInChildren<Transform>(true)
                .Count(t => t.name.Equals("Dino Egg Spawn", StringComparison.Ordinal));
            report.CameraBoundsCount = all.Count(t => t.name.Equals("Camera Bounds", StringComparison.Ordinal) &&
                                                      t.GetComponent<BoxCollider>() != null);

            Transform presetRoot = Find(scene, BattlefieldPath + "/Defense Presets");
            GameObject[] presets = presetRoot == null
                ? Array.Empty<GameObject>()
                : Enumerable.Range(0, presetRoot.childCount).Select(i => presetRoot.GetChild(i).gameObject)
                    .Where(go => go.name.StartsWith("Preset ", StringComparison.Ordinal)).ToArray();
            report.DefensePresetCount = presets.Length;
            report.ActiveDefensePresetCount = presets.Count(preset => preset.activeSelf);
            report.DefensePresetWallCounts = presets.Select(preset => WallComponents(preset).Length).ToArray();
            report.ActiveDefenseWallCount = presets.Where(preset => preset.activeSelf)
                .Sum(preset => WallComponents(preset).Count(wall => wall.gameObject.activeInHierarchy));
            report.DefenseWallsSanitized = presets.Length >= 2 && presets.All(PresetWallsAreSafe);

            Component[] houses = all.SelectMany(transform => transform.GetComponents<Component>())
                .Where(component => component != null
                    && component.GetType().FullName == "LlamAcademy.Dinos.Unit.VillageHouse")
                .ToArray();
            report.VillageHouseCount = houses.Length;
            Transform barrierRoot = Find(scene, BattlefieldPath + "/Art Pass/Canyon Rim/Navigation Barriers");
            Transform[] barriers = barrierRoot == null
                ? Array.Empty<Transform>()
                : barrierRoot.Cast<Transform>().ToArray();
            report.NavigationBarrierCount = barriers.Length;
            Transform canyonRim = Find(scene, BattlefieldPath + "/Art Pass/Canyon Rim");
            report.RimVisualOnly = canyonRim != null &&
                canyonRim.GetComponentsInChildren<Collider>(true).All(collider => !collider.enabled || collider.isTrigger) &&
                canyonRim.GetComponentsInChildren<NavMeshObstacle>(true).Length == 0 &&
                canyonRim.GetComponentsInChildren<NavMeshModifier>(true).Length == 0;
            Transform slotRoot = Find(scene, BattlefieldPath + "/Session Defense Layout/Wall Spawn Slots");
            Component[] wallSlots = slotRoot == null
                ? Array.Empty<Component>()
                : slotRoot.GetComponentsInChildren<Component>(true)
                    .Where(component => component != null
                        && component.GetType().FullName == "LlamAcademy.Dinos.Enemy.Defense.WallSpawnSlot")
                    .ToArray();
            report.WallSpawnSlotCount = wallSlots.Length;
            bool housesValid = HousesAreValid(houses);
            bool barriersValid = BarriersAreValid(barriers);
            bool wallSlotsValid = WallSlotsAreValid(wallSlots);
            Component groundDefense = all.SelectMany(transform => transform.GetComponents<Component>())
                .FirstOrDefault(component => component != null &&
                    component.GetType().FullName == "LlamAcademy.Dinos.Enemy.Defense.SessionGroundDefenseController");
            report.GroundDefenseConfigured = GroundDefenseIsValid(groundDefense);

            DinoDeploymentService service = UnityEngine.Object.FindFirstObjectByType<DinoDeploymentService>(FindObjectsInactive.Include);
            report.ServiceUsesExactZoneInstances = service != null && service.ValidateZoneConfiguration() &&
                                                   service.ConfiguredZones.Count == zones.Length &&
                                                   new HashSet<DinoDeploymentZone>(service.ConfiguredZones).SetEquals(zones);
            DinoDeploymentZonePresenter presenter = UnityEngine.Object.FindFirstObjectByType<DinoDeploymentZonePresenter>(FindObjectsInactive.Include);
            report.PresenterUsesServiceZones = PresenterUsesService(presenter, service) && presenter.ValidateConfiguration();

            Transform legacy = Find(scene, "/World/Stage8 Strategy");
            report.LegacyStrategyActive = legacy != null && legacy.gameObject.activeInHierarchy;
            report.HasForbiddenDeploymentZone = all.Any(t => t.gameObject.activeInHierarchy &&
                (t.name.IndexOf("rainforest", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 t.name.IndexOf("north deployment", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                all.Select(t => t.GetComponent<DinoDeploymentZone>()).Any(zone => zone != null &&
                    zone.gameObject.activeInHierarchy && !zones.Contains(zone));

            Transform battlefield = Find(scene, BattlefieldPath);
            VisualPolishAudit visualAudit = OpenTropicalBattlefieldVisualPolishBuilder.Audit(battlefield);
            report.VisualLayerIsolated = visualAudit.IsIsolated;
            report.Z3DecorationEmpty = visualAudit.Z3DecorationEmpty;
            report.VisualColliderCount = visualAudit.ColliderCount;
            report.VisualTriggerCount = visualAudit.TriggerCount;
            report.VisualRigidbodyCount = visualAudit.RigidbodyCount;
            report.VisualNavMeshComponentCount = visualAudit.NavMeshComponentCount;
            report.VisualAgentCount = visualAudit.AgentCount;
            report.VisualGameplayBehaviourCount = visualAudit.GameplayBehaviourCount;
            report.VisualViolatingPaths = visualAudit.ViolatingPaths;

            BoxCollider cameraBounds = all.Where(t => t.name == "Camera Bounds")
                .Select(t => t.GetComponent<BoxCollider>()).FirstOrDefault(c => c != null);
            if (cameraBounds != null)
            {
                float area = cameraBounds.bounds.size.x * cameraBounds.bounds.size.z;
                report.BoundsAreaIncreasePercent = (area / BaselineArea - 1f) * 100f;
            }

            bool exactZones = report.ZoneIds.Length == 5 && report.ZoneIds.Distinct().Count() == 5 &&
                              new HashSet<DeploymentZoneId>(report.ZoneIds)
                                  .SetEquals((DeploymentZoneId[])Enum.GetValues(typeof(DeploymentZoneId)));
            bool exactSurfaces = report.SurfaceKinds.Length == 5 && report.SurfaceKinds.Distinct().Count() == 5 &&
                                 new HashSet<TerrainSurfaceKind>(report.SurfaceKinds)
                                     .SetEquals((TerrainSurfaceKind[])Enum.GetValues(typeof(TerrainSurfaceKind)));
            report.IsStructurallyReadyToDeactivateLegacy = report.MissingHierarchyPaths.Length == 0 &&
                exactZones && exactSurfaces && QuadrilateralSceneContractIsValid(zones, surfaceRoot) &&
                report.ObjectiveCount == 1 && report.CameraBoundsCount == 1 &&
                report.DefensePresetCount >= 2 && report.ActiveDefensePresetCount == 0 &&
                report.DefensePresetWallCounts.All(count => count >= 1) &&
                report.DefenseWallsSanitized &&
                housesValid && barriersValid && report.RimVisualOnly && wallSlotsValid && report.GroundDefenseConfigured &&
                report.ServiceUsesExactZoneInstances && report.PresenterUsesServiceZones &&
                !report.HasForbiddenDeploymentZone && report.BoundsAreaIncreasePercent >= 25f &&
                report.BoundsAreaIncreasePercent <= 35f;

            report.Paths = ValidatePaths(zones, plaza).ToArray();
            report.DefenderSlotPaths = ValidateDefenderSlots(plaza).ToArray();
            report.CompletePathCount = report.Paths.Count(path => path.StartSampled && path.GoalSampled &&
                path.Status == NavMeshPathStatus.PathComplete.ToString() && float.IsFinite(path.Length) && path.Length > 0.1f);
            bool defenderSlotsValid = report.DefenderSlotPaths.Length > 0 && report.DefenderSlotPaths.All(path =>
                path.StartSampled && path.GoalSampled && path.Status == NavMeshPathStatus.PathComplete.ToString() &&
                float.IsFinite(path.Length) && path.Length > 0.1f);
            report.IsValid = report.IsStructurallyReadyToDeactivateLegacy && !report.LegacyStrategyActive &&
                             report.VisualLayerIsolated && report.Z3DecorationEmpty &&
                             report.Paths.Length == 15 && report.CompletePathCount == 15 && defenderSlotsValid;
            report.StructuralSignature = BuildSignature(report, zones, volumes, presets, cameraBounds);
            report.FrozenGameplaySignature = OpenTropicalBattlefieldVisualPolishBuilder.BuildFrozenGameplaySignature(scene);
            report.Summary = $"missing={report.MissingHierarchyPaths.Length}; zones={report.ZoneIds.Length}; " +
                             $"surfaces={report.SurfaceKinds.Length}; objective={report.ObjectiveCount}; " +
                             $"presets={report.DefensePresetCount}/{report.ActiveDefensePresetCount}; " +
                             $"houses={report.VillageHouseCount}/{housesValid}; barriers={report.NavigationBarrierCount}/{barriersValid}; rimVisual={report.RimVisualOnly}; " +
                             $"slots={report.WallSpawnSlotCount}/{wallSlotsValid}; groundDefense={report.GroundDefenseConfigured}; structural={report.IsStructurallyReadyToDeactivateLegacy}; " +
                             $"defenderPaths={report.DefenderSlotPaths.Count(path => path.Status == NavMeshPathStatus.PathComplete.ToString())}/{report.DefenderSlotPaths.Length}; " +
                             $"defenderFailures={string.Join(",", report.DefenderSlotPaths.Where(path => path.Status != NavMeshPathStatus.PathComplete.ToString()).Select(path => path.Slot + ":" + path.StartSampled + "/" + path.GoalSampled + "/" + path.Status))}; " +
                             $"legacy={report.LegacyStrategyActive}; paths={report.CompletePathCount}/{report.Paths.Length}; " +
                             $"visual={report.VisualLayerIsolated}; z3Empty={report.Z3DecorationEmpty}; " +
                             $"visualForbidden={report.VisualColliderCount}/{report.VisualTriggerCount}/{report.VisualRigidbodyCount}/" +
                             $"{report.VisualNavMeshComponentCount}/{report.VisualAgentCount}/{report.VisualGameplayBehaviourCount}; " +
                             $"visualViolations={string.Join(",", report.VisualViolatingPaths)}; " +
                             $"area={report.BoundsAreaIncreasePercent:F2}%";
            return report;
        }

        public static OpenTropicalPresetNavigationReport[] ValidateEveryPresetAndRestoreCanonical(bool saveScene)
        {
            Scene scene = SceneManager.GetActiveScene();
            VillageDefenseLayoutController layout = UnityEngine.Object.FindFirstObjectByType<VillageDefenseLayoutController>(FindObjectsInactive.Include);
            Transform zoneRoot = Find(scene, BattlefieldPath + "/Deployment Zones");
            Transform plaza = Find(scene, BattlefieldPath + "/Village/Central Plaza");
            if (layout == null || zoneRoot == null || plaza == null)
            {
                return Array.Empty<OpenTropicalPresetNavigationReport>();
            }

            DinoDeploymentZone[] zones = zoneRoot.GetComponentsInChildren<DinoDeploymentZone>(true);
            SerializedObject serializedLayout = new(layout);
            SerializedProperty roots = serializedLayout.FindProperty("presetRoots");
            SerializedProperty surfaces = serializedLayout.FindProperty("runtimeSurfaces");
            List<OpenTropicalPresetNavigationReport> reports = new();
            try
            {
                layout.gameObject.SetActive(true);
                for (int presetIndex = 0; presetIndex < roots.arraySize; presetIndex++)
                {
                    int round = FindRoundForPreset(layout.MapSeed, presetIndex, roots.arraySize);
                    layout.ActivateForRound(layout.MapSeed, round);
                    BuildSerializedSurfaces(surfaces);
                    reports.Add(new OpenTropicalPresetNavigationReport
                    {
                        PresetIndex = presetIndex,
                        PresetId = layout.ActivePresetId,
                        Paths = ValidatePaths(zones, plaza).ToArray(),
                        DefenderSlotPaths = ValidateDefenderSlots(plaza).ToArray()
                    });
                }
            }
            finally
            {
                layout.ActivateForRound(layout.MapSeed, FindRoundForPreset(layout.MapSeed, 0, roots.arraySize));
                BuildSerializedSurfaces(surfaces);
                if (saveScene)
                {
                    // Preset validation bakes temporary NavMeshData for every layout. Reload the
                    // already-saved canonical scene instead of serializing those transient bakes,
                    // otherwise every validation run changes the scene bytes and object references.
                    EditorSceneManager.OpenScene(scene.path, OpenSceneMode.Single);
                }
                else
                {
                    layout.gameObject.SetActive(false);
                }
            }

            return reports.ToArray();
        }

        private static int FindRoundForPreset(int seed, int presetIndex, int presetCount)
        {
            for (int round = 0; round < 1024; round++)
                if (DefensePresetSelector.SelectIndex(seed, round, presetCount) == presetIndex) return round;
            throw new InvalidOperationException($"No deterministic round selected preset {presetIndex}.");
        }

        private static void BuildSerializedSurfaces(SerializedProperty surfaces)
        {
            for (int index = 0; index < surfaces.arraySize; index++)
            {
                MonoBehaviour surface = surfaces.GetArrayElementAtIndex(index).objectReferenceValue as MonoBehaviour;
                surface?.GetType().GetMethod("BuildNavMesh")?.Invoke(surface, null);
            }
        }

        private static bool PresetWallsAreSafe(GameObject preset)
        {
            Component[] walls = WallComponents(preset);
            if (walls.Length == 0)
            {
                return false;
            }

            foreach (Component wall in walls)
            {
                SerializedObject serialized = new(wall);
                UnityEngine.Object unit = serialized.FindProperty("<UnitType>k__BackingField")?.objectReferenceValue;
                SerializedProperty upgrade = unit == null
                    ? null
                    : new SerializedObject(unit).FindProperty("<Upgrade>k__BackingField");
                UnityEngine.Object upgradeUnit = upgrade?.objectReferenceValue;
                SerializedProperty terminalUpgrade = upgradeUnit == null ? null :
                    new SerializedObject(upgradeUnit).FindProperty("<Upgrade>k__BackingField");
                if (serialized.FindProperty("Root")?.objectReferenceValue != wall.gameObject ||
                    serialized.FindProperty("HealthBar")?.objectReferenceValue == null ||
                    unit == null || !AssetDatabase.GetAssetPath(unit).StartsWith("Assets/LlamAcademy/Dinos/Map/Generated/", StringComparison.Ordinal) ||
                    upgradeUnit == null || terminalUpgrade == null || terminalUpgrade.objectReferenceValue != null ||
                    serialized.FindProperty("UpdateNavMeshData.<Obstacles>k__BackingField")?.arraySize != 0 ||
                    serialized.FindProperty("UpdateNavMeshData.<RebakeSurfacesOnDeath>k__BackingField")?.arraySize != 0 ||
                    serialized.FindProperty("UpdateNavMeshData.<EnableObjectsBeforeRebake>k__BackingField")?.arraySize != 0 ||
                    serialized.FindProperty("UpdateNavMeshData.<EnableComponentsBeforeRebake>k__BackingField")?.arraySize != 0)
                {
                    return false;
                }

                if (wall.GetComponentsInChildren<Component>(true)
                    .Where(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.UI.HealthBar")
                    .Any(component => !component.gameObject.activeSelf))
                {
                    return false;
                }

                Transform[] hierarchy = wall.GetComponentsInChildren<Transform>(true);
                if (hierarchy.Any(transform => transform.gameObject.layer != 7))
                {
                    return false;
                }

                NavMeshObstacle[] obstacles = wall.GetComponentsInChildren<NavMeshObstacle>(true);
                if (obstacles.Length == 0 || obstacles.Any(obstacle => obstacle.enabled || obstacle.carving ||
                    !obstacle.transform.IsChildOf(preset.transform)))
                {
                    return false;
                }
            }

            return true;
        }

        private static bool HousesAreValid(Component[] houses)
        {
            if (houses.Length != 8)
            {
                return false;
            }

            HashSet<int> ids = new();
            foreach (Component house in houses)
            {
                SerializedObject serialized = new(house);
                int id = serialized.FindProperty("<StableHouseId>k__BackingField")?.intValue ?? -1;
                int maxHealth = serialized.FindProperty("<MaxHealth>k__BackingField")?.intValue ?? 0;
                Collider occupancy = serialized.FindProperty("OccupancyCollider")?.objectReferenceValue as Collider;
                UnityEngine.Object intact = serialized.FindProperty("IntactVisualRoot")?.objectReferenceValue;
                GameObject beams = serialized.FindProperty("BeamRoot")?.objectReferenceValue as GameObject;
                Component healthBar = serialized.FindProperty("HealthBar")?.objectReferenceValue as Component;
                if (!ids.Add(id)
                    || maxHealth is not (100 or 150 or 225 or 325)
                    || occupancy == null
                    || !occupancy.enabled
                    || intact == null
                    || healthBar == null
                    || beams == null
                    || beams.transform.childCount is < 3 or > 5)
                {
                    return false;
                }

                foreach (Transform beam in beams.transform)
                {
                    Component[] components = beam.GetComponents<Component>();
                    if (components.Any(component => component is not Transform
                        && component is not MeshFilter
                        && component is not MeshRenderer))
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool BarriersAreValid(Transform[] barriers)
        {
            return barriers.Length == 0;
        }

        private static bool GroundDefenseIsValid(Component groundDefense)
        {
            if (groundDefense == null || groundDefense is not Behaviour behaviour || !behaviour.enabled)
            {
                return false;
            }

            SerializedObject serialized = new(groundDefense);
            return serialized.FindProperty("DeploymentZones")?.arraySize == 5 &&
                   serialized.FindProperty("ArcherPrefab")?.objectReferenceValue != null &&
                   serialized.FindProperty("MagePrefab")?.objectReferenceValue != null &&
                   serialized.FindProperty("SpawnRoot")?.objectReferenceValue != null &&
                   Mathf.Approximately(serialized.FindProperty("MinimumSpacing")?.floatValue ?? 0f, 6f);
        }

        private static bool WallSlotsAreValid(Component[] wallSlots)
        {
            if (wallSlots.Length != 6)
            {
                return false;
            }

            HashSet<int> ids = new();
            foreach (Component slot in wallSlots)
            {
                SerializedObject serialized = new(slot);
                int id = serialized.FindProperty("<StableId>k__BackingField")?.intValue ?? -1;
                Transform guardAnchor = serialized.FindProperty("<GuardAnchor>k__BackingField")?.objectReferenceValue as Transform;
                if (guardAnchor == null || !ids.Add(id))
                {
                    return false;
                }
            }
            return true;
        }

        private static Component[] WallComponents(GameObject root) => root.GetComponentsInChildren<Component>(true)
            .Where(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Unit.Wall")
            .ToArray();

        private static IEnumerable<OpenTropicalPathRecord> ValidatePaths(DinoDeploymentZone[] zones, Transform plaza)
        {
            if (plaza == null)
            {
                yield break;
            }

            string[] prefabPaths = {
                "Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor_New.prefab",
                "Assets/LlamAcademy/Dinos/Dinos/Pachycephalosaurus/Pachycephalosaurus_New.prefab",
                "Assets/LlamAcademy/Dinos/Dinos/TRex/Trex_New Variant.prefab"
            };
            int[] agentTypes = prefabPaths.Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Select(prefab => prefab == null ? null : prefab.GetComponent<NavMeshAgent>())
                .Where(agent => agent != null).Select(agent => agent.agentTypeID).Distinct().OrderBy(id => id).ToArray();
            foreach (int agentType in agentTypes)
            {
                NavMeshQueryFilter filter = new() { agentTypeID = agentType, areaMask = NavMesh.AllAreas };
                foreach (DinoDeploymentZone zone in zones.OrderBy(zone => zone.Id))
                {
                    OpenTropicalPathRecord record = new() { ZoneId = zone.Id.ToString(), AgentTypeId = agentType };
                    bool hasZoneCenter = zone.TryResolveRelative(new Vector2(0.5f, 0.5f), out Vector3 zoneCenter);
                    NavMeshHit start = default;
                    record.StartSampled = hasZoneCenter && NavMesh.SamplePosition(zoneCenter, out start, 3f, filter);
                    record.GoalSampled = NavMesh.SamplePosition(plaza.position, out NavMeshHit goal, 2f, filter);
                    NavMeshPath path = new();
                    if (record.StartSampled && record.GoalSampled)
                    {
                        NavMesh.CalculatePath(start.position, goal.position, filter, path);
                    }

                    record.Status = path.status.ToString();
                    record.Length = PathLength(path);
                    yield return record;
                }
            }
        }

        private static IEnumerable<DefenderSlotPathRecord> ValidateDefenderSlots(Transform plaza)
        {
            Transform slotRoot = Find(SceneManager.GetActiveScene(), BattlefieldPath + "/Session Defense Layout/Wall Spawn Slots");
            if (slotRoot == null || plaza == null) yield break;
            int[] defenderAgentTypes = ActiveDefenderAgentTypes();
            foreach (int agentType in defenderAgentTypes)
            foreach (Transform slot in slotRoot.GetComponentsInChildren<Transform>(true)
                         .Where(transform => transform.name == "Guard Anchor"))
            {
                NavMeshQueryFilter filter = new() { agentTypeID = agentType, areaMask = NavMesh.AllAreas };
                DefenderSlotPathRecord record = new() { Slot = slot.parent.name, ZoneId = "Session", AgentTypeId = agentType };
                record.StartSampled = NavMesh.SamplePosition(plaza.position, out NavMeshHit start, 2f, filter);
                record.GoalSampled = NavMesh.SamplePosition(slot.position, out NavMeshHit goal, 1f, filter);
                NavMeshPath path = new();
                if (record.StartSampled && record.GoalSampled) NavMesh.CalculatePath(start.position, goal.position, filter, path);
                record.Status = path.status.ToString();
                record.Length = PathLength(path);
                yield return record;
            }
        }

        private static int[] ActiveDefenderAgentTypes()
        {
            Component enemyAi = UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Enemy.EnemyAIController");
            UnityEngine.Object config = enemyAi == null ? null : new SerializedObject(enemyAi).FindProperty("ActiveConfig")?.objectReferenceValue;
            SerializedProperty units = config == null ? null : new SerializedObject(config).FindProperty("<Units>k__BackingField");
            if (units == null || !units.isArray) return Array.Empty<int>();
            List<int> agentTypes = new();
            for (int index = 0; index < units.arraySize; index++)
            {
                UnityEngine.Object unit = units.GetArrayElementAtIndex(index).FindPropertyRelative("UnitSO")?.objectReferenceValue;
                UnityEngine.Object prefabReference = unit == null
                    ? null
                    : new SerializedObject(unit).FindProperty("<Prefab>k__BackingField")?.objectReferenceValue;
                GameObject prefab = prefabReference switch
                {
                    GameObject gameObject => gameObject,
                    Component component => component.gameObject,
                    _ => null
                };
                NavMeshAgent agent = prefab == null ? null : prefab.GetComponent<NavMeshAgent>();
                if (agent != null) agentTypes.Add(agent.agentTypeID);
            }

            return agentTypes.Distinct().OrderBy(id => id).ToArray();
        }

        private static float PathLength(NavMeshPath path)
        {
            if (path.corners == null || path.corners.Length < 2)
            {
                return path.status == NavMeshPathStatus.PathComplete ? 0f : float.PositiveInfinity;
            }

            float length = 0f;
            for (int i = 1; i < path.corners.Length; i++)
            {
                length += Vector3.Distance(path.corners[i - 1], path.corners[i]);
            }

            return length;
        }

        private static bool PresenterUsesService(DinoDeploymentZonePresenter presenter, DinoDeploymentService service)
        {
            if (presenter == null || service == null)
            {
                return false;
            }

            SerializedObject serialized = new(presenter);
            return serialized.FindProperty("DeploymentService")?.objectReferenceValue == service;
        }

        private static bool QuadrilateralSceneContractIsValid(DinoDeploymentZone[] zones, Transform surfaceRoot)
        {
            if (zones.Length != 5 || zones.Any(zone => zone == null || !zone.IsGeometryValid || zone.GetComponent<BoxCollider>() != null) ||
                surfaceRoot == null)
            {
                return false;
            }

            QuadrilateralXZ[] geometries = new QuadrilateralXZ[zones.Length];
            for (int i = 0; i < zones.Length; i++)
            {
                IReadOnlyList<Vector3> world = zones[i].WorldVertices;
                if (world.Count != 4 || !QuadrilateralXZ.TryCreate(
                    new Vector2(world[0].x, world[0].z), new Vector2(world[1].x, world[1].z),
                    new Vector2(world[2].x, world[2].z), new Vector2(world[3].x, world[3].z), out geometries[i]))
                {
                    return false;
                }

                Transform surface = surfaceRoot.Find(zones[i].DominantSurface.ToString());
                MeshCollider trigger = surface == null ? null : surface.GetComponent<MeshCollider>();
                if (trigger == null || !trigger.convex || !trigger.isTrigger || trigger.sharedMesh == null ||
                    surface.GetComponent<TerrainSurfaceVolume>() == null ||
                    surface.Find("Visible Graybox")?.GetComponent<MeshFilter>()?.sharedMesh == null)
                {
                    return false;
                }
            }

            for (int first = 0; first < geometries.Length; first++)
            {
                for (int second = first + 1; second < geometries.Length; second++)
                {
                    if (geometries[first].Overlaps(geometries[second])) return false;
                }
            }

            Transform neutral = surfaceRoot.Find("Neutral Ground");
            Collider neutralCollider = neutral == null ? null : neutral.GetComponent<Collider>();
            return neutral != null && neutral.gameObject.layer == 6 && neutralCollider != null && !neutralCollider.isTrigger &&
                   neutral.GetComponent<TerrainSurfaceVolume>() == null;
        }

        private static string BuildSignature(OpenTropicalBattlefieldReport report, DinoDeploymentZone[] zones,
            TerrainSurfaceVolume[] volumes, GameObject[] presets, BoxCollider bounds)
        {
            StringBuilder signature = new();
            signature.Append(string.Join("|", report.MissingHierarchyPaths.OrderBy(x => x))).Append(';');
            foreach (DinoDeploymentZone zone in zones.OrderBy(zone => zone.Id))
            {
                signature.Append(zone.Id).Append('@');
                foreach (Vector3 vertex in zone.WorldVertices)
                {
                    signature.Append(vertex.ToString("F3")).Append(',');
                }
                Mesh boundary = zone.transform.Find("Boundary")?.GetComponent<MeshFilter>()?.sharedMesh;
                signature.Append(':').Append(QuadrilateralMeshFactory.ComputeSignature(boundary)).Append(';');
            }
            signature.Append(string.Join(",", volumes.Select(v => v.Surface + "=" + SurfaceMeshSignature(v)).OrderBy(v => v))).Append(';');
            signature.Append(string.Join(",", presets.Select(p => p.name + "=" + p.activeSelf).OrderBy(v => v))).Append(';');
            signature.Append("walls=").Append(string.Join(",", report.DefensePresetWallCounts)).Append(';');
            signature.Append(bounds == null ? "no-bounds" : bounds.bounds.center.ToString("F3") + bounds.bounds.size.ToString("F3"));
            signature.Append(";legacy=").Append(report.LegacyStrategyActive);
            return signature.ToString();
        }

        private static string SurfaceMeshSignature(TerrainSurfaceVolume volume)
        {
            MeshCollider collider = volume == null ? null : volume.GetComponent<MeshCollider>();
            return collider == null ? string.Empty : QuadrilateralMeshFactory.ComputeSignature(collider.sharedMesh);
        }

        internal static Transform Find(Scene scene, string absolutePath)
        {
            string[] parts = absolutePath.Trim('/').Split('/');
            Transform current = scene.GetRootGameObjects().FirstOrDefault(root => root.name == parts[0])?.transform;
            for (int i = 1; current != null && i < parts.Length; i++)
            {
                current = current.Find(parts[i]);
            }
            return current;
        }
    }
}
