using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LlamAcademy.Dinos.Deployment;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.Enemy.Defense;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Map.Adapters;
using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Training;
using LlamAcademy.Dinos.Unit;
using LlamAcademy.Dinos.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem.UI;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace LlamAcademy.Dinos.LayeredBattlefield.Editor
{
    public static class LayeredBattlefieldSceneBuilder
    {
        public const string SourceScenePath = "Assets/LlamAcademy/Dinos/Scenes/Dinos.unity";
        public const string LayeredScenePath = "Assets/LlamAcademy/Dinos/Scenes/LayeredBattlefield.unity";
        public const string MapSelectScenePath = "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity";
        private static readonly string[] HutPrefabPaths =
        {
            "Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Tribal Pack/T/- Prefabs_T/Tribal_T/Hut_Tribal_Tier1.prefab",
            "Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Tribal Pack/T/- Prefabs_T/Tribal_T/Hut_Tribal_Tier2.prefab",
            "Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Tribal Pack/T/- Prefabs_T/Tribal_T/Hut_Tribal_Tier3.prefab",
            "Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Tribal Pack/T/- Prefabs_T/Tribal_T/Hut_Tribal_Tier4.prefab"
        };

        [MenuItem("Dino Attack/Maps/Build Task 4 Scenes")]
        public static void BuildFromMenu() => Build();

        [MenuItem("Dino Attack/Maps/Repair Map Select Basic Interaction")]
        public static void RepairMapSelectBasicInteraction()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("MapSelect repair requires a stopped, compiled Editor.");
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty)
                throw new InvalidOperationException($"Refusing to close dirty scene '{active.path}'.");
            Scene scene = EditorSceneManager.OpenScene(MapSelectScenePath, OpenSceneMode.Single);
            WireMapSelectInteraction(scene);
            EditorSceneManager.SaveScene(scene);
        }

        [MenuItem("Dino Attack/Maps/Repair Layered Battlefield Visual Layout")]
        public static void RepairLayeredBattlefieldVisualLayout()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Layered battlefield repair requires a stopped, compiled Editor.");
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty)
                throw new InvalidOperationException($"Refusing to close dirty scene '{active.path}'.");

            Scene scene = EditorSceneManager.OpenScene(LayeredScenePath, OpenSceneMode.Single);
            GameObject oldRuntime = scene.GetRootGameObjects()
                .SingleOrDefault(value => value.name == "Layered Battlefield Runtime");
            if (oldRuntime != null)
            {
                UnityEngine.Object.DestroyImmediate(oldRuntime);
            }

            BuildLayeredScene(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log("Layered battlefield visible zones and deterministic Hut visuals repaired.");
        }

        [MenuItem("Dino Attack/Maps/Repair Layered Battlefield Base Ground")]
        public static void RepairLayeredBattlefieldBaseGround()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Layered battlefield ground repair requires a stopped, compiled Editor.");
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty)
                throw new InvalidOperationException($"Refusing to close dirty scene '{active.path}'.");

            Scene scene = EditorSceneManager.OpenScene(LayeredScenePath, OpenSceneMode.Single);
            GameObject runtimeRoot = scene.GetRootGameObjects()
                .Single(value => value.name == "Layered Battlefield Runtime");
            TerrainSurfaceVolume[] legacySurfaces = Find<TerrainSurfaceVolume>(scene)
                .Where(value => !value.transform.IsChildOf(runtimeRoot.transform))
                .ToArray();
            DisableLegacyMapVisuals(scene, legacySurfaces);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("Layered battlefield shared base ground enabled and riverbed layers removed.");
        }

        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling)
                throw new InvalidOperationException("Task 4 builder requires a stopped, compiled Editor.");
            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty)
                throw new InvalidOperationException($"Refusing to close dirty scene '{active.path}'.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(LayeredScenePath) != null ||
                AssetDatabase.LoadAssetAtPath<SceneAsset>(MapSelectScenePath) != null)
                throw new InvalidOperationException("Task 4 scene destinations must be absent before the one-shot build.");

            string sourceHash = Sha256(SourceScenePath);
            if (!AssetDatabase.CopyAsset(SourceScenePath, LayeredScenePath))
                throw new InvalidOperationException("Could not create LayeredBattlefield.unity from the approved source scene.");
            AssetDatabase.ImportAsset(LayeredScenePath, ImportAssetOptions.ForceSynchronousImport);

            Scene layered = EditorSceneManager.OpenScene(LayeredScenePath, OpenSceneMode.Single);
            BuildLayeredScene(layered);
            EditorSceneManager.SaveScene(layered);
            if (!string.Equals(sourceHash, Sha256(SourceScenePath), StringComparison.Ordinal))
                throw new InvalidOperationException("Dinos.unity changed while building the derived scene.");

            Scene mapSelect = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            BuildMapSelectScene(mapSelect);
            EditorSceneManager.SaveScene(mapSelect, MapSelectScenePath);
            AssetDatabase.SaveAssets();
            if (!string.Equals(sourceHash, Sha256(SourceScenePath), StringComparison.Ordinal))
                throw new InvalidOperationException("Dinos.unity changed while building MapSelect.unity.");

            LayeredBattlefieldValidator.ValidateAssets();
            Debug.Log("Task 4 scenes created and validated without saving Dinos.unity.");
        }

        private static void BuildLayeredScene(Scene scene)
        {
            LayeredBattlefieldLayoutConfig config = LayeredBattlefieldLayoutConfig.CreateApprovedDefault();
            GameObject root = new("Layered Battlefield Runtime");
            SceneManager.MoveGameObjectToScene(root, scene);

            DinoDeploymentZone[] legacyZones = Find<DinoDeploymentZone>(scene);
            TerrainSurfaceVolume[] legacySurfaces = Find<TerrainSurfaceVolume>(scene)
                .Where(value => !value.transform.IsChildOf(root.transform))
                .ToArray();
            Dictionary<TerrainSurfaceKind, Material> surfaceMaterials = legacySurfaces
                .GroupBy(value => value.Surface)
                .ToDictionary(
                    group => group.Key,
                    group => group.SelectMany(value => value.GetComponentsInChildren<MeshRenderer>(true))
                        .Select(value => value.sharedMaterial)
                        .FirstOrDefault(value => value != null));
            Dictionary<TerrainSurfaceKind, int> surfaceLayers = legacySurfaces
                .GroupBy(value => value.Surface)
                .ToDictionary(group => group.Key, group => group.First().gameObject.layer);
            int groundLayer = LayerMask.NameToLayer("Grass");
            Material boundaryMaterial = legacyZones
                .Select(value => value.transform.Find("Boundary")?.GetComponent<Renderer>()?.sharedMaterial)
                .FirstOrDefault(value => value != null);
            if (surfaceMaterials.Count != Enum.GetValues(typeof(TerrainSurfaceKind)).Length || groundLayer < 0 ||
                surfaceMaterials.Values.Any(value => value == null) || boundaryMaterial == null)
            {
                throw new InvalidOperationException("Map 1 must supply all five terrain materials and a deployment boundary material.");
            }
            foreach (DinoDeploymentZone zone in legacyZones) zone.gameObject.SetActive(false);
            DisableLegacyMapVisuals(scene, legacySurfaces);
            List<(int preset, DeploymentZoneId id, DinoDeploymentZone zone)> zoneBindings = new();
            Transform zoneRoot = Child(root.transform, "Three Zone Presets");
            foreach (BattlefieldZonePreset preset in config.ZonePresets)
            {
                Transform presetRoot = Child(zoneRoot, $"Preset {preset.StableIndex}");
                foreach (BattlefieldZoneDefinition definition in preset.Zones)
                {
                    GameObject zoneObject = new($"P{preset.StableIndex} {definition.ZoneId}");
                    zoneObject.transform.SetParent(presetRoot, false);
                    DinoDeploymentZone zone = zoneObject.AddComponent<DinoDeploymentZone>();
                    SetInt(zone, "<Id>k__BackingField", (int)definition.ZoneId);
                    SetString(zone, "<DisplayName>k__BackingField", definition.ZoneId.ToString());
                    SetInt(zone, "<DominantSurface>k__BackingField", (int)definition.Surface);
                    zone.ConfigureGeometry(definition.Geometry.A, definition.Geometry.B,
                        definition.Geometry.C, definition.Geometry.D);
                    BuildZoneVisualBundle(
                        zoneObject.transform,
                        definition,
                        surfaceMaterials[definition.Surface],
                        surfaceLayers[definition.Surface],
                        groundLayer,
                        boundaryMaterial);
                    zoneBindings.Add((preset.StableIndex, definition.ZoneId, zone));
                }
            }

            VillageHouse[] sourceHouses = Find<VillageHouse>(scene)
                .Where(value => !value.transform.IsChildOf(root.transform))
                .OrderBy(value => value.StableHouseId)
                .ToArray();
            if (sourceHouses.Length < 1) throw new InvalidOperationException("No approved Map 1 house model is available.");
            foreach (VillageHouse source in sourceHouses) source.gameObject.SetActive(false);
            Transform houseRoot = Child(root.transform, "House Candidates");
            List<(string id, GameObject candidate)> houseBindings = new();
            for (int index = 0; index < config.HouseCandidates.Count; index++)
            {
                BattlefieldHouseCandidate data = config.HouseCandidates[index];
                GameObject candidate = UnityEngine.Object.Instantiate(sourceHouses[0].gameObject, houseRoot);
                candidate.name = $"Candidate House {data.StableId}";
                candidate.transform.position = new Vector3(data.Position.x, sourceHouses[0].transform.position.y, data.Position.y);
                candidate.transform.rotation = Quaternion.identity;
                candidate.SetActive(true);
                VillageHouse house = candidate.GetComponent<VillageHouse>();
                SetInt(house, "<StableHouseId>k__BackingField", 400 + index);
                SetInt(house, "<Tier>k__BackingField", (int)data.Tier);
                int hp = data.Tier switch
                {
                    BattlefieldHouseTier.Tier1 => 100,
                    BattlefieldHouseTier.Tier2 => 150,
                    BattlefieldHouseTier.Tier3 => 225,
                    _ => 325
                };
                SetInt(house, "<MaxHealth>k__BackingField", hp);
                SetInt(house, "<Health>k__BackingField", hp);
                AttachHouseVisual(candidate, data);
                houseBindings.Add((data.StableId, candidate));
            }

            SessionDefenseLayoutController defense = ExactlyOne<SessionDefenseLayoutController>(scene);
            WallSpawnSlot[] slots = Find<WallSpawnSlot>(scene).OrderBy(value => value.StableId).Take(6).ToArray();
            if (slots.Length != 6) throw new InvalidOperationException("Map 1 must supply six reusable wall slots.");
            List<(string id, GameObject candidate)> wallBindings = new();
            for (int index = 0; index < slots.Length; index++)
            {
                BattlefieldWallCandidate data = config.WallCandidates[index];
                slots[index].transform.position = new Vector3(data.Position.x, 0f, data.Position.y);
                slots[index].GuardAnchor.position = new Vector3(data.GuardPosition.x, 1f, data.GuardPosition.y);
                slots[index].gameObject.SetActive(true);
                wallBindings.Add((data.StableId, slots[index].gameObject));
            }

            Transform guardRoot = Child(root.transform, "Ground Guard Candidates");
            List<(string id, GameObject candidate)> guardBindings = new();
            foreach (BattlefieldGuardCandidate data in config.GroundGuardCandidates)
            {
                GameObject marker = new($"Candidate Guard {data.StableId}");
                marker.transform.SetParent(guardRoot, false);
                marker.transform.position = new Vector3(data.Position.x, 0f, data.Position.y);
                guardBindings.Add((data.StableId, marker));
            }

            RoundManager round = ExactlyOne<RoundManager>(scene);
            GameObject targetTemplate = round.DinoTarget.gameObject;
            targetTemplate.SetActive(false);
            Transform targetRoot = Child(root.transform, "Core Target Candidates");
            List<(string id, GameObject candidate)> targetBindings = new();
            foreach (BattlefieldTargetCandidate data in config.TargetCandidates)
            {
                GameObject target = UnityEngine.Object.Instantiate(targetTemplate, targetRoot);
                target.name = $"Candidate Target {data.StableId}";
                target.transform.position = new Vector3(data.Position.x, round.DinoTarget.position.y, data.Position.y);
                target.SetActive(true);
                targetBindings.Add((data.StableId, target));
            }

            LayeredBattlefieldLayoutController layout = root.AddComponent<LayeredBattlefieldLayoutController>();
            SetZoneBindings(layout, zoneBindings);
            SetCandidateBindings(layout, "HouseBindings", houseBindings);
            SetCandidateBindings(layout, "WallBindings", wallBindings);
            SetCandidateBindings(layout, "GroundGuardBindings", guardBindings);
            SetCandidateBindings(layout, "TargetBindings", targetBindings);
            SetObject(layout, "RoundManagerSource", round);
            DinoTrainingAgent agent = Find<DinoTrainingAgent>(scene).SingleOrDefault();
            DinoStructuredObservationBuilder observations = Find<DinoStructuredObservationBuilder>(scene).SingleOrDefault();
            DinoDeploymentService deployment = Find<DinoDeploymentService>(scene).SingleOrDefault();
            SetObject(layout, "TrainingAgent", agent);
            SetObject(layout, "ObservationBuilder", observations);
            SetObject(layout, "DeploymentService", deployment);
            SetObject(layout, "ZonePresenter", Find<DinoDeploymentZonePresenter>(scene).SingleOrDefault());
            SetObject(defense, "LayeredLayout", layout);
            SetObject(ExactlyOne<SessionGroundDefenseController>(scene), "LayeredLayout", layout);
            SetBool(ExactlyOne<NavMeshManager>(scene), "AllowRuntimeBuild", false);

            if (Find<DinoTrainingEntryBootstrap>(scene).Length == 0)
                root.AddComponent<DinoTrainingEntryBootstrap>();
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void BuildZoneVisualBundle(
            Transform zone,
            BattlefieldZoneDefinition definition,
            Material surfaceMaterial,
            int surfaceLayer,
            int groundLayer,
            Material boundaryMaterial)
        {
            zone.gameObject.layer = 2;

            GameObject volumeObject = new("Surface Volume", typeof(MeshCollider));
            volumeObject.transform.SetParent(zone, false);
            volumeObject.layer = surfaceLayer;
            MeshCollider trigger = volumeObject.GetComponent<MeshCollider>();
            trigger.sharedMesh = QuadrilateralMeshFactory.CreateTriggerPrism(
                definition.Geometry, -0.5f, 1.5f, $"{definition.ZoneId} {definition.Surface} Trigger");
            trigger.convex = true;
            trigger.isTrigger = true;
            TerrainSurfaceVolume surfaceVolume = volumeObject.AddComponent<TerrainSurfaceVolume>();
            SetInt(surfaceVolume, "surface", (int)definition.Surface);

            GameObject visual = new("Visible Surface", typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider));
            visual.transform.SetParent(zone, false);
            visual.layer = groundLayer;
            Mesh groundMesh = QuadrilateralMeshFactory.CreateGroundMesh(
                definition.Geometry, 0.015f, $"{definition.ZoneId} {definition.Surface} Ground");
            visual.GetComponent<MeshFilter>().sharedMesh = groundMesh;
            visual.GetComponent<MeshRenderer>().sharedMaterial = surfaceMaterial;
            visual.GetComponent<MeshCollider>().sharedMesh = groundMesh;

            GameObject boundary = new("Boundary", typeof(MeshFilter), typeof(MeshRenderer));
            boundary.transform.SetParent(zone, false);
            boundary.layer = 2;
            boundary.GetComponent<MeshFilter>().sharedMesh = QuadrilateralMeshFactory.CreateOutlineMesh(
                definition.Geometry, 0.025f, $"{definition.ZoneId} Boundary");
            MeshRenderer boundaryRenderer = boundary.GetComponent<MeshRenderer>();
            boundaryRenderer.sharedMaterial = boundaryMaterial;
            boundaryRenderer.enabled = false;
        }

        private static void DisableLegacyMapVisuals(Scene scene, IEnumerable<TerrainSurfaceVolume> legacySurfaces)
        {
            TerrainSurfaceVolume[] surfaces = legacySurfaces
                .Where(value => value != null)
                .ToArray();
            foreach (GameObject terrainRoot in surfaces
                         .Select(value => value.transform.parent?.gameObject)
                         .Where(value => value != null)
                         .Distinct())
            {
                terrainRoot.SetActive(true);
                Transform neutralGround = terrainRoot.transform.Find("Neutral Ground");
                if (neutralGround != null) neutralGround.gameObject.SetActive(true);
            }

            foreach (TerrainSurfaceVolume surface in surfaces)
            {
                Transform riverbed = surface.transform.Find("Riverbed");
                if (riverbed != null) UnityEngine.Object.DestroyImmediate(riverbed.gameObject);
                surface.gameObject.SetActive(false);
            }

            string[] staleArtPaths =
            {
                "/World/Open Tropical Battlefield/Art Pass/Z1 Ruins",
                "/World/Open Tropical Battlefield/Art Pass/Z2 Meadow",
                "/World/Open Tropical Battlefield/Art Pass/Z3 Riverbank",
                "/World/Open Tropical Battlefield/Art Pass/Z4 Palms",
                "/World/Open Tropical Battlefield/Art Pass/Z5 Rocks",
                "/World/Open Tropical Battlefield/Art Pass/Village Details"
            };
            foreach (string path in staleArtPaths)
            {
                Transform transform = FindSceneTransform(scene, path);
                if (transform != null) transform.gameObject.SetActive(false);
            }
        }

        private static void AttachHouseVisual(GameObject candidate, BattlefieldHouseCandidate data)
        {
            int tierIndex = (int)data.Tier - 1;
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(HutPrefabPaths[tierIndex]);
            if (prefab == null)
            {
                throw new InvalidOperationException($"Missing approved Hut prefab for {data.Tier}.");
            }

            if (candidate.GetComponent<MeshRenderer>() is { } gameplayRenderer)
            {
                gameplayRenderer.enabled = false;
            }

            GameObject visual = (GameObject)PrefabUtility.InstantiatePrefab(prefab, candidate.transform);
            visual.name = $"Hut Visual {data.Tier}";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.Euler(0f, data.VisualYawDegrees, 0f);
            Vector3 parentScale = candidate.transform.lossyScale;
            visual.transform.localScale = new Vector3(
                data.VisualScale / Mathf.Abs(parentScale.x),
                data.VisualScale / Mathf.Abs(parentScale.y),
                data.VisualScale / Mathf.Abs(parentScale.z));

            foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
                UnityEngine.Object.DestroyImmediate(collider);
            foreach (NavMeshObstacle obstacle in visual.GetComponentsInChildren<NavMeshObstacle>(true))
                UnityEngine.Object.DestroyImmediate(obstacle);
            foreach (Transform child in visual.GetComponentsInChildren<Transform>(true))
                child.gameObject.layer = 2;

            Renderer[] renderers = visual.GetComponentsInChildren<Renderer>(true)
                .Where(value => value.enabled)
                .ToArray();
            if (renderers.Length == 0)
            {
                throw new InvalidOperationException($"Approved Hut prefab for {data.Tier} has no visible renderer.");
            }
            Bounds bounds = renderers[0].bounds;
            for (int index = 1; index < renderers.Length; index++) bounds.Encapsulate(renderers[index].bounds);
            visual.transform.position += Vector3.up * (candidate.transform.position.y + 0.03f - bounds.min.y);
        }

        private static Transform FindSceneTransform(Scene scene, string absolutePath)
        {
            string[] parts = absolutePath.Trim('/').Split('/');
            if (parts.Length == 0) return null;
            Transform current = scene.GetRootGameObjects()
                .Select(value => value.transform)
                .FirstOrDefault(value => value.name == parts[0]);
            for (int index = 1; current != null && index < parts.Length; index++)
                current = current.Find(parts[index]);
            return current;
        }

        private static void BuildMapSelectScene(Scene scene)
        {
            GameObject cameraObject = new("Main Camera", typeof(Camera), typeof(AudioListener));
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 0f, -10f);
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            GameObject bootstrap = new("Dino Training Entry Bootstrap", typeof(DinoTrainingEntryBootstrap));
            SceneManager.MoveGameObjectToScene(bootstrap, scene);
            GameObject eventSystem = new("EventSystem", typeof(EventSystem), typeof(InputSystemUIInputModule));
            SceneManager.MoveGameObjectToScene(eventSystem, scene);
            GameObject canvasObject = new("Map Select", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            SceneManager.MoveGameObjectToScene(canvasObject, scene);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            CreateLogo(canvasObject.transform);
            CreateButton(canvasObject.transform, "Open Tropical Battlefield", "开阔战场", new Vector2(-260f, -230f));
            CreateButton(canvasObject.transform, "Layered Battlefield", "分层战场", new Vector2(260f, -230f));
            CreateButton(canvasObject.transform, "Defense Battlefield", "防御战场", new Vector2(0f, -310f));
            CreateButton(canvasObject.transform, "Dinosaur Introduction", "恐龙介绍", new Vector2(0f, -370f));
            WireMapSelectInteraction(scene);
        }

        private static void WireMapSelectInteraction(Scene scene)
        {
            Button[] buttons = Find<Button>(scene);
            Button open = buttons.Single(value => value.name == "Open Tropical Battlefield");
            Button layered = buttons.Single(value => value.name == "Layered Battlefield");
            Button defense = buttons.SingleOrDefault(value => value.name == "Defense Battlefield");
            Button start = buttons.SingleOrDefault(value => value.name == "Start");
            if (start != null) UnityEngine.Object.DestroyImmediate(start.gameObject);

            EventSystem eventSystem = Find<EventSystem>(scene).Single();
            StandaloneInputModule legacyInput = eventSystem.GetComponent<StandaloneInputModule>();
            if (legacyInput != null) UnityEngine.Object.DestroyImmediate(legacyInput);
            if (eventSystem.GetComponent<InputSystemUIInputModule>() == null)
                eventSystem.gameObject.AddComponent<InputSystemUIInputModule>();

            GameSessionRestartService restart = Find<GameSessionRestartService>(scene).SingleOrDefault();
            if (restart == null)
            {
                GameObject serviceObject = new("Game Session Restart Service", typeof(GameSessionRestartService));
                SceneManager.MoveGameObjectToScene(serviceObject, scene);
                restart = serviceObject.GetComponent<GameSessionRestartService>();
            }

            Canvas canvas = Find<Canvas>(scene).Single();
            CreateLogo(canvas.transform);
            defense ??= CreateButton(canvas.transform, "Defense Battlefield", "防御战场", new Vector2(520f, -210f));
            SetAnchoredPosition(open.transform, new Vector2(-520f, -210f));
            SetAnchoredPosition(layered.transform, new Vector2(0f, -210f));
            SetAnchoredPosition(defense.transform, new Vector2(520f, -210f));
            EnsureDinosaurIntroduction(
                canvas.transform,
                out Button introduction,
                out GameObject introductionPanel,
                out Button closeIntroduction);
            MapSelectUI controller = canvas.GetComponent<MapSelectUI>();
            if (controller == null) controller = canvas.gameObject.AddComponent<MapSelectUI>();
            Text status = canvas.transform.Find("Selection Status")?.GetComponent<Text>();
            if (status == null)
            {
                GameObject statusObject = new("Selection Status", typeof(RectTransform), typeof(Text));
                statusObject.transform.SetParent(canvas.transform, false);
                RectTransform rect = (RectTransform)statusObject.transform;
                rect.sizeDelta = new Vector2(900f, 80f);
                status = statusObject.GetComponent<Text>();
                status.alignment = TextAnchor.MiddleCenter;
                status.fontSize = 34;
                status.color = Color.white;
                status.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            }
            SetAnchoredPosition(status.transform, new Vector2(0f, -500f));

            SetObject(controller, "OpenTropicalButton", open);
            SetObject(controller, "LayeredBattlefieldButton", layered);
            SetObject(controller, "DefenseBattlefieldButton", defense);
            SetObject(controller, "DinosaurIntroductionButton", introduction);
            SetObject(controller, "DinosaurIntroductionPanel", introductionPanel);
            SetObject(controller, "CloseIntroductionButton", closeIntroduction);
            SetObject(controller, "RestartService", restart);
            SetObject(controller, "StatusText", status);
            ReplacePersistentListeners(open, controller.LaunchOpenTropicalBattlefield);
            ReplacePersistentListeners(layered, controller.LaunchLayeredBattlefield);
            ReplacePersistentListeners(defense, controller.LaunchDefenseBattlefield);
            ReplacePersistentListeners(introduction, controller.ShowDinosaurIntroduction);
            ReplacePersistentListeners(closeIntroduction, controller.HideDinosaurIntroduction);
            introductionPanel.SetActive(false);
            EditorSceneManager.MarkSceneDirty(scene);
        }

        private static void EnsureDinosaurIntroduction(
            Transform canvas,
            out Button introduction,
            out GameObject panel,
            out Button close)
        {
            Transform existingIntroduction = canvas.Find("Dinosaur Introduction");
            introduction = existingIntroduction == null
                ? CreateButton(canvas, "Dinosaur Introduction", "恐龙介绍", new Vector2(0f, -370f))
                : existingIntroduction.GetComponent<Button>();
            SetAnchoredPosition(introduction.transform, new Vector2(0f, -350f));

            Transform existingPanel = canvas.Find("Dinosaur Introduction Panel");
            panel = existingPanel == null
                ? CreateDinosaurIntroductionPanel(canvas)
                : existingPanel.gameObject;
            panel.transform.SetAsLastSibling();

            Transform existingClose = panel.transform.Find("Close Dinosaur Introduction");
            close = existingClose == null
                ? CreateButton(panel.transform, "Close Dinosaur Introduction", "返回", new Vector2(0f, -420f))
                : existingClose.GetComponent<Button>();
        }

        private static GameObject CreateDinosaurIntroductionPanel(Transform parent)
        {
            GameObject panel = new("Dinosaur Introduction Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(parent, false);
            RectTransform panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color(0.055f, 0.043f, 0.035f, 0.97f);

            CreateText(panel.transform, "Introduction Title", "恐龙图鉴", new Vector2(0f, 420f),
                new Vector2(900f, 90f), 48, new Color(1f, 0.78f, 0.34f));
            CreateText(panel.transform, "Introduction Hint", "根据肉量与战场压力选择合适的恐龙",
                new Vector2(0f, 345f), new Vector2(1100f, 60f), 28, Color.white);

            CreateDinosaurCard(
                panel.transform,
                "Pachycephalosaurus Card",
                "肿头龙",
                "25 肉 · 100 生命\n低成本先锋，适合快速铺场。",
                "Assets/LlamAcademy/Dinos/Dinos/Pachycephalosaurus/Pachycephalosaurus.asset",
                -520f);
            CreateDinosaurCard(
                panel.transform,
                "Velociraptor Card",
                "迅猛龙",
                "50 肉 · 150 生命\n均衡进攻单位，适合持续施压。",
                "Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor.asset",
                0f);
            CreateDinosaurCard(
                panel.transform,
                "TRex Card",
                "霸王龙",
                "100 肉 · 600 生命\n高耐久重型单位，适合正面突破。",
                "Assets/LlamAcademy/Dinos/Dinos/TRex/TRex.asset",
                520f);
            return panel;
        }

        private static void CreateDinosaurCard(
            Transform parent,
            string objectName,
            string displayName,
            string description,
            string dinoAssetPath,
            float x)
        {
            GameObject card = new(objectName, typeof(RectTransform), typeof(Image));
            card.transform.SetParent(parent, false);
            RectTransform cardRect = (RectTransform)card.transform;
            cardRect.sizeDelta = new Vector2(440f, 520f);
            cardRect.anchoredPosition = new Vector2(x, 10f);
            card.GetComponent<Image>().color = new Color(0.25f, 0.13f, 0.06f, 0.96f);

            GameObject portrait = new("Portrait", typeof(RectTransform), typeof(Image));
            portrait.transform.SetParent(card.transform, false);
            RectTransform portraitRect = (RectTransform)portrait.transform;
            portraitRect.sizeDelta = new Vector2(280f, 250f);
            portraitRect.anchoredPosition = new Vector2(0f, 85f);
            DinoSO dino = AssetDatabase.LoadAssetAtPath<DinoSO>(dinoAssetPath);
            if (dino == null || dino.Sprite == null)
            {
                throw new InvalidOperationException($"Dinosaur introduction asset is missing: {dinoAssetPath}");
            }
            Image portraitImage = portrait.GetComponent<Image>();
            portraitImage.sprite = dino.Sprite;
            portraitImage.preserveAspect = true;
            portraitImage.raycastTarget = false;

            CreateText(card.transform, "Name", displayName, new Vector2(0f, -85f),
                new Vector2(400f, 70f), 36, new Color(1f, 0.79f, 0.37f));
            CreateText(card.transform, "Description", description, new Vector2(0f, -175f),
                new Vector2(390f, 110f), 25, Color.white);
        }

        private static Text CreateText(
            Transform parent,
            string name,
            string value,
            Vector2 position,
            Vector2 size,
            int fontSize,
            Color color)
        {
            GameObject textObject = new(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)textObject.transform;
            rect.sizeDelta = size;
            rect.anchoredPosition = position;
            Text text = textObject.GetComponent<Text>();
            text.text = value;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = fontSize;
            text.color = color;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return text;
        }

        private static void CreateLogo(Transform parent)
        {
            Transform existing = parent.Find("Dino Attack Logo");
            GameObject logoObject = existing == null
                ? new GameObject("Dino Attack Logo", typeof(RectTransform), typeof(Image))
                : existing.gameObject;
            if (existing == null) logoObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)logoObject.transform;
            rect.sizeDelta = new Vector2(540f, 540f);
            rect.anchoredPosition = new Vector2(0f, 170f);
            Image image = logoObject.GetComponent<Image>();
            if (image == null) image = logoObject.AddComponent<Image>();
            image.sprite = AssetDatabase.LoadAssetAtPath<Sprite>(
                "Assets/LlamAcademy/Dinos/UI/Textures/Dinos Logo.png");
            if (image.sprite == null) throw new InvalidOperationException("Dino Attack logo sprite is missing.");
            image.preserveAspect = true;
            image.raycastTarget = false;
            EditorUtility.SetDirty(image);
        }

        private static void SetAnchoredPosition(Transform transform, Vector2 position)
        {
            if (transform is RectTransform rect) rect.anchoredPosition = position;
        }

        private static void ReplacePersistentListeners(Button button, UnityEngine.Events.UnityAction action)
        {
            while (button.onClick.GetPersistentEventCount() > 0)
                UnityEventTools.RemovePersistentListener(button.onClick, 0);
            UnityEventTools.AddPersistentListener(button.onClick, action);
            EditorUtility.SetDirty(button);
        }

        private static Button CreateButton(Transform parent, string name, string label, Vector2 position)
        {
            GameObject buttonObject = new(name, typeof(RectTransform), typeof(Image), typeof(Button));
            buttonObject.transform.SetParent(parent, false);
            RectTransform rect = (RectTransform)buttonObject.transform;
            rect.sizeDelta = new Vector2(420f, 110f);
            rect.anchoredPosition = position;
            buttonObject.GetComponent<Image>().color = new Color(0.42f, 0.22f, 0.08f, 0.95f);
            GameObject textObject = new("Label", typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(buttonObject.transform, false);
            RectTransform textRect = (RectTransform)textObject.transform;
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = textRect.offsetMax = Vector2.zero;
            Text text = textObject.GetComponent<Text>();
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontSize = 36;
            text.color = Color.white;
            text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            return buttonObject.GetComponent<Button>();
        }

        private static Transform Child(Transform parent, string name)
        {
            GameObject child = new(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static T[] Find<T>(Scene scene) where T : Component =>
            UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(value => value.gameObject.scene == scene).ToArray();

        private static T ExactlyOne<T>(Scene scene) where T : Component
        {
            T[] found = Find<T>(scene);
            if (found.Length != 1) throw new InvalidOperationException($"Expected one {typeof(T).Name}, found {found.Length}.");
            return found[0];
        }

        private static void SetZoneBindings(Component component,
            IReadOnlyList<(int preset, DeploymentZoneId id, DinoDeploymentZone zone)> values)
        {
            SerializedObject serialized = new(component);
            SerializedProperty array = serialized.FindProperty("ZoneBindings");
            array.arraySize = values.Count;
            for (int index = 0; index < values.Count; index++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("PresetIndex").intValue = values[index].preset;
                element.FindPropertyRelative("ZoneId").enumValueIndex = (int)values[index].id;
                element.FindPropertyRelative("Zone").objectReferenceValue = values[index].zone;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetCandidateBindings(Component component, string field,
            IReadOnlyList<(string id, GameObject candidate)> values)
        {
            SerializedObject serialized = new(component);
            SerializedProperty array = serialized.FindProperty(field);
            array.arraySize = values.Count;
            for (int index = 0; index < values.Count; index++)
            {
                SerializedProperty element = array.GetArrayElementAtIndex(index);
                element.FindPropertyRelative("StableId").stringValue = values[index].id;
                element.FindPropertyRelative("Candidate").objectReferenceValue = values[index].candidate;
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetObject(Component component, string field, UnityEngine.Object value)
        {
            SerializedObject serialized = new(component);
            SerializedProperty property = serialized.FindProperty(field) ?? throw new MissingFieldException(component.GetType().Name, field);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetInt(Component component, string field, int value)
        {
            SerializedObject serialized = new(component);
            SerializedProperty property = serialized.FindProperty(field) ?? throw new MissingFieldException(component.GetType().Name, field);
            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetString(Component component, string field, string value)
        {
            SerializedObject serialized = new(component);
            SerializedProperty property = serialized.FindProperty(field) ?? throw new MissingFieldException(component.GetType().Name, field);
            property.stringValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetBool(Component component, string field, bool value)
        {
            SerializedObject serialized = new(component);
            SerializedProperty property = serialized.FindProperty(field) ?? throw new MissingFieldException(component.GetType().Name, field);
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string Sha256(string assetPath)
        {
            string fullPath = Path.GetFullPath(assetPath);
            using SHA256 hash = SHA256.Create();
            return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(fullPath))).Replace("-", string.Empty);
        }
    }
}
