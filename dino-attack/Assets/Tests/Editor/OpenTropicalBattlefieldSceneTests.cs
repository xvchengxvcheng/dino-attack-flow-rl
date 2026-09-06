using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using LlamAcademy.Dinos.Deployment;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Map.Editor;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class OpenTropicalBattlefieldSceneTests
    {
        [OneTimeSetUp]
        public void OpenDinosScene()
        {
            EditorSceneManager.OpenScene("Assets/LlamAcademy/Dinos/Scenes/Dinos.unity", OpenSceneMode.Single);
        }

        [Test]
        public void LoadedDinosScene_SatisfiesApprovedGrayboxContract()
        {
            OpenTropicalBattlefieldReport report = OpenTropicalBattlefieldValidator.ValidateLoadedScene();

            Assert.That(report.ScenePath, Does.EndWith("/Dinos.unity"));
            Assert.That(report.MissingHierarchyPaths, Is.Empty);
            Assert.That(report.ZoneIds, Is.EquivalentTo(Enum.GetValues(typeof(DeploymentZoneId))));
            Assert.That(report.SurfaceKinds, Is.EquivalentTo(Enum.GetValues(typeof(TerrainSurfaceKind))));
            Assert.That(report.ObjectiveCount, Is.EqualTo(1));
            Assert.That(report.CameraBoundsCount, Is.EqualTo(1));
            Assert.That(report.DefensePresetCount, Is.GreaterThanOrEqualTo(2));
            Assert.That(report.ActiveDefensePresetCount, Is.Zero,
                "canonical authoring keeps both presets inactive; the session layout owns active walls");
            Assert.That(report.ServiceUsesExactZoneInstances, Is.True);
            Assert.That(report.PresenterUsesServiceZones, Is.True);
            Assert.That(report.LegacyStrategyActive, Is.False);
            Assert.That(report.HasForbiddenDeploymentZone, Is.False);
            Assert.That(report.BoundsAreaIncreasePercent, Is.EqualTo(30.56f).Within(0.02f));
            Assert.That(report.IsValid, Is.True, report.Summary);
        }

        [Test]
        public void ArtPassHasFiveDistinctNonBlockingLandmarksAndSafeMaterials()
        {
            Transform art = GameObject.Find("World/Open Tropical Battlefield/Art Pass")?.transform;
            Assert.That(art, Is.Not.Null, "Task 7 must create one explicit art-pass root.");

            string[] identities = { "Z1 Ruins", "Z2 Meadow", "Z4 Palms", "Z5 Rocks" };
            foreach (string identity in identities)
            {
                Transform root = art.Find(identity);
                Assert.That(root, Is.Not.Null, $"Missing visual identity root {identity}.");
                Renderer[] renderers = root.GetComponentsInChildren<Renderer>(true);
                Assert.That(renderers.Length, Is.GreaterThanOrEqualTo(3), $"{identity} needs readable landmarks.");
                Assert.That(root.GetComponentsInChildren<Collider>(true), Is.Empty,
                    $"{identity} decorations must not become click blockers or alter navigation.");
                Assert.That(renderers.All(renderer => renderer.sharedMaterial != null &&
                    renderer.sharedMaterial.shader != null && renderer.sharedMaterial.shader.name != "Hidden/InternalErrorShader"),
                    Is.True, $"{identity} has a missing/pink material.");
            }
            Transform z3 = art.Find("Z3 Riverbank");
            Assert.That(z3, Is.Not.Null);
            Assert.That(z3.childCount, Is.Zero, "Z3 must contain only the water authored under Terrain Surfaces");
            Assert.That(z3.GetComponentsInChildren<Renderer>(true), Is.Empty);
            Assert.That(GameObject.Find("World/Open Tropical Battlefield/Terrain Surfaces/ShallowWater/Riverbed"),
                Is.Null, "The approved water presentation no longer uses a separate riverbed layer.");
        }

        [Test]
        public void VisualPolishRoots_ArePresentIsolatedAndKeepZ3Empty()
        {
            Transform battlefield = GameObject.Find("World/Open Tropical Battlefield")?.transform;
            Assert.That(battlefield, Is.Not.Null);

            Transform distantScenery = battlefield.Find("Art Pass/Canyon Rim/Distant Scenery");
            Assert.That(distantScenery, Is.Not.Null,
                "the visual responsibility root must remain available for future scenery passes");
            Assert.That(distantScenery.childCount, Is.Zero,
                "the approved A framing removes the five out-of-bounds mountain silhouettes");

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
        }

        [Test]
        public void VisualPolishLighting_UsesWarmNeutralAmbientWithoutBlueCast()
        {
            Assert.That(RenderSettings.ambientMode, Is.EqualTo(UnityEngine.Rendering.AmbientMode.Trilight));
            Assert.That(RenderSettings.ambientSkyColor.r, Is.GreaterThan(RenderSettings.ambientSkyColor.b));
            Assert.That(RenderSettings.ambientEquatorColor.r, Is.GreaterThan(RenderSettings.ambientEquatorColor.b));
            Assert.That(RenderSettings.ambientGroundColor.r, Is.GreaterThan(RenderSettings.ambientGroundColor.b));
            Assert.That(RenderSettings.fogColor.r, Is.GreaterThan(RenderSettings.fogColor.b));

            Material water = AssetDatabase.LoadAssetAtPath<Material>(
                "Assets/LlamAcademy/Dinos/Stage8/Materials/ShallowWaterTeal.mat");
            Assert.That(water, Is.Not.Null);
            Color waterColor = water.GetColor("_BaseColor");
            Assert.That(waterColor.g, Is.GreaterThan(waterColor.r));
            Assert.That(waterColor.b, Is.GreaterThan(waterColor.r));
        }

        [Test]
        public void Builder_VisualRepairPreservesFrozenGameplaySignatureAndBecomesByteStable()
        {
            try
            {
                Transform battlefield = GameObject.Find("World/Open Tropical Battlefield")?.transform;
                Assert.That(battlefield, Is.Not.Null);
                Transform missingVisual = battlefield.Find("Art Pass/Terrain Dressing/Z2 Meadow/Meadow Grass 01");
                Assert.That(missingVisual, Is.Not.Null, "the canonical visual pass must author the repair target");

                Component[] navSurfaces = FindRuntimeComponents("Unity.AI.Navigation.NavMeshSurface");
                Dictionary<Component, NavMeshData> navDataBefore = navSurfaces.ToDictionary(
                    surface => surface, NavMeshDataForSurface);
                string frozenBefore = OpenTropicalBattlefieldVisualPolishBuilder.BuildFrozenGameplaySignature(
                    SceneManager.GetActiveScene());
                UnityEngine.Object.DestroyImmediate(missingVisual.gameObject);

                OpenTropicalBattlefieldSceneBuilder.Build(saveScene: false);
                Assert.That(battlefield.Find("Art Pass/Terrain Dressing/Z2 Meadow/Meadow Grass 01"), Is.Not.Null,
                    "the deterministic builder must repair a deleted visual without touching gameplay");
                Assert.That(OpenTropicalBattlefieldVisualPolishBuilder.BuildFrozenGameplaySignature(
                        SceneManager.GetActiveScene()), Is.EqualTo(frozenBefore));
                Assert.That(FindRuntimeComponents("Unity.AI.Navigation.NavMeshSurface"), Has.Length.EqualTo(navSurfaces.Length));
                foreach (KeyValuePair<Component, NavMeshData> pair in navDataBefore)
                    Assert.That(NavMeshDataForSurface(pair.Key), Is.SameAs(pair.Value),
                        "visual-only repair must never replace frozen NavMeshData");

                string firstVisualSerialization = VisualSerializationSignature(battlefield.Find("Art Pass"));
                OpenTropicalBattlefieldSceneBuilder.Build(saveScene: false);
                string secondVisualSerialization = VisualSerializationSignature(battlefield.Find("Art Pass"));

                Assert.That(secondVisualSerialization, Is.EqualTo(firstVisualSerialization));
                Assert.That(OpenTropicalBattlefieldVisualPolishBuilder.BuildFrozenGameplaySignature(
                        SceneManager.GetActiveScene()), Is.EqualTo(frozenBefore));
            }
            finally
            {
                EditorSceneManager.OpenScene("Assets/LlamAcademy/Dinos/Scenes/Dinos.unity", OpenSceneMode.Single);
            }
        }

        [Test]
        public void ApprovedScopedRepair_IsNoOpWhenSceneIsAlreadyCanonical()
        {
            Scene scene = SceneManager.GetActiveScene();
            Assert.That(scene.isDirty, Is.False, "the scoped installer only accepts a clean handoff scene");
            Component[] surfaces = FindRuntimeComponents("Unity.AI.Navigation.NavMeshSurface");
            Dictionary<Component, NavMeshData> navDataBefore = surfaces.ToDictionary(
                surface => surface, NavMeshDataForSurface);
            string gameplayBefore = OpenTropicalBattlefieldVisualPolishBuilder.BuildFrozenGameplaySignature(scene);

            OpenTropicalBattlefieldReport report =
                OpenTropicalBattlefieldSceneBuilder.ApplyApprovedCameraSceneryAndNavMeshRepair(saveScene: false);

            Assert.That(report.IsValid, Is.True, report.Summary);
            Assert.That(SceneManager.GetActiveScene().isDirty, Is.False,
                "a canonical scoped install must not dirty or resave the scene");
            Assert.That(OpenTropicalBattlefieldVisualPolishBuilder.BuildFrozenGameplaySignature(scene),
                Is.EqualTo(gameplayBefore));
            Assert.That(surfaces.ToDictionary(surface => surface, NavMeshDataForSurface),
                Is.EqualTo(navDataBefore), "a canonical second install must not rebake NavMeshData");
        }

        [Test]
        public void RuntimeUiVisualPolish_PreservesBehavioralElementContract()
        {
            string[] requiredIds =
            {
                "start-button", "wave-text", "dino-button-container", "resources-container",
                "win-lose-text", "deployment-feedback", "terrain-feedback", "phase-status",
                "food-status", "threat-status", "result-panel", "session-summary",
                "restart-button"
            };
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/LlamAcademy/Dinos/UI/RuntimeUI.uxml");
            Assert.That(asset, Is.Not.Null);
            VisualElement root = asset.CloneTree();

            string[] actualBehavioralIds = requiredIds
                .Where(id => root.Q(id) != null)
                .OrderBy(id => id, StringComparer.Ordinal)
                .ToArray();

            Assert.That(actualBehavioralIds,
                Is.EqualTo(requiredIds.OrderBy(id => id, StringComparer.Ordinal).ToArray()),
                "USS-only polish must retain every RuntimeUI query/callback target");
            Assert.That(root.Q("logo-container"), Is.Null,
                "The Dino Attack logo must be owned exclusively by MapSelect, not gameplay HUDs.");
            string uxmlSource = File.ReadAllText(AssetDatabase.GetAssetPath(asset));
            StringAssert.IsMatch(
                @"<ui:VisualElement\s+name=""result-panel""[^>]*style=""[^""]*display:\s*none;",
                uxmlSource,
                "result-panel must remain hidden by default in the UXML source");
        }

        [Test]
        public void FrozenGameplaySignature_IgnoresApprovedLightingAndDetectsDinoOrder()
        {
            try
            {
                Scene scene = SceneManager.GetActiveScene();
                string baseline = OpenTropicalBattlefieldVisualPolishBuilder.BuildFrozenGameplaySignature(scene);
                Light sun = RenderSettings.sun;
                Quaternion originalRotation = sun.transform.rotation;
                sun.transform.rotation = Quaternion.Euler(8f, 19f, 0f);
                Assert.That(OpenTropicalBattlefieldVisualPolishBuilder.BuildFrozenGameplaySignature(scene),
                    Is.EqualTo(baseline), "the approved visual lighting transform must be excluded");
                sun.transform.rotation = originalRotation;

                Component agent = FindRuntimeComponents("LlamAcademy.Dinos.Training.DinoTrainingAgent").Single();
                SerializedObject serialized = new(agent);
                SerializedProperty dinos = serialized.FindProperty("DinoTypes");
                Assert.That(dinos, Is.Not.Null);
                Assert.That(dinos.arraySize, Is.EqualTo(3));
                UnityEngine.Object first = dinos.GetArrayElementAtIndex(0).objectReferenceValue;
                UnityEngine.Object second = dinos.GetArrayElementAtIndex(1).objectReferenceValue;
                dinos.GetArrayElementAtIndex(0).objectReferenceValue = second;
                dinos.GetArrayElementAtIndex(1).objectReferenceValue = first;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                Assert.That(OpenTropicalBattlefieldVisualPolishBuilder.BuildFrozenGameplaySignature(scene),
                    Is.Not.EqualTo(baseline), "the frozen Wait + three-dino choice order must be signed");
            }
            finally
            {
                EditorSceneManager.OpenScene("Assets/LlamAcademy/Dinos/Scenes/Dinos.unity", OpenSceneMode.Single);
            }
        }

        [Test]
        public void RuntimeUiContainsDedicatedTerrainFeedbackThatDoesNotPickWorldInput()
        {
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/LlamAcademy/Dinos/UI/RuntimeUI.uxml");
            VisualElement root = asset.CloneTree();
            Label terrain = root.Q<Label>("terrain-feedback");

            Assert.That(terrain, Is.Not.Null);
            Assert.That(terrain.pickingMode, Is.EqualTo(PickingMode.Ignore));

            Component runtimeUi = FindComponent("LlamAcademy.Dinos.UI.RuntimeUI");
            Assert.That(runtimeUi, Is.Not.Null);
            Assert.That(new SerializedObject(runtimeUi).FindProperty("ZonePresenter").objectReferenceValue,
                Is.EqualTo(FindComponent("LlamAcademy.Dinos.Map.DinoDeploymentZonePresenter")));
            Component visualization = FindComponent("LlamAcademy.Dinos.Player.PlaceDinoVisualization");
            Assert.That(visualization, Is.Not.Null);
            Assert.That(new SerializedObject(visualization).FindProperty("ZonePresenter").objectReferenceValue,
                Is.EqualTo(FindComponent("LlamAcademy.Dinos.Map.DinoDeploymentZonePresenter")));
        }

        [Test]
        public void FullIslandCamera_UsesApprovedPerspectiveHomeAndRtsInputConfiguration()
        {
            Component control = FindComponent("LlamAcademy.Dinos.Player.CameraControl");
            Component camera = FindComponent("Unity.Cinemachine.CinemachineCamera");
            Component follow = FindComponent("Unity.Cinemachine.CinemachineFollow");
            Assert.That(control, Is.Not.Null);
            Assert.That(camera, Is.Not.Null);
            Assert.That(follow, Is.Not.Null);

            SerializedObject controlData = new(control);
            Assert.That(controlData.FindProperty("EnableMousePan").boolValue, Is.True);
            Assert.That(controlData.FindProperty("KeyboardSpeed").floatValue, Is.EqualTo(20f));
            Assert.That(controlData.FindProperty("ZoomSensitivity").floatValue, Is.EqualTo(0.22f).Within(0.0001f));
            Assert.That(controlData.FindProperty("MinimumZoomScale").floatValue, Is.EqualTo(0.45f).Within(0.0001f));
            Assert.That(controlData.FindProperty("MaximumZoomScale").floatValue, Is.EqualTo(1.35f).Within(0.0001f));

            SerializedObject cameraData = new(camera);
            Transform target = (Transform)cameraData.FindProperty("Target.TrackingTarget").objectReferenceValue;
            Assert.That(target.position, Is.EqualTo(new Vector3(-3.5f, -2.5f, -12f)));
            Assert.That(cameraData.FindProperty("Lens.FieldOfView").floatValue, Is.EqualTo(58f));
            Assert.That(cameraData.FindProperty("Lens.ModeOverride").enumValueIndex, Is.EqualTo(0),
                "Home framing must remain perspective, not orthographic.");

            Vector3 offset = new SerializedObject(follow).FindProperty("FollowOffset").vector3Value;
            Assert.That(offset, Is.EqualTo(new Vector3(0f, 82f, 38f)));
            float pitch = Mathf.Atan2(offset.y, offset.z) * Mathf.Rad2Deg;
            Assert.That(pitch, Is.EqualTo(65.15f).Within(0.05f));
        }

        [Test]
        public void QuadrilateralZones_ShareApprovedGeometryAndLeaveNeutralBattlefield()
        {
            Transform battlefield = GameObject.Find("World/Open Tropical Battlefield").transform;
            DinoDeploymentZone[] zones = battlefield.Find("Deployment Zones")
                .GetComponentsInChildren<DinoDeploymentZone>(true)
                .OrderBy(zone => zone.Id)
                .ToArray();
            Assert.That(zones.Length, Is.EqualTo(5));

            Vector2[][] expected =
            {
                new[] { new Vector2(-27f, -44f), new Vector2(-27f, -25f), new Vector2(-50f, -16f), new Vector2(-52f, -49f) },
                new[] { new Vector2(-25f, -19f), new Vector2(-13f, -17f), new Vector2(-22f, 18f), new Vector2(-44f, 7f) },
                new[] { new Vector2(-10f, -17f), new Vector2(7f, -17f), new Vector2(14f, 27f), new Vector2(-18f, 27f) },
                new[] { new Vector2(10f, -18f), new Vector2(22f, -21f), new Vector2(43f, 5f), new Vector2(20f, 18f) },
                new[] { new Vector2(23f, -44f), new Vector2(23f, -25f), new Vector2(46f, -17f), new Vector2(48f, -48f) }
            };
            QuadrilateralXZ[] geometries = new QuadrilateralXZ[zones.Length];
            Transform terrainRoot = battlefield.Find("Terrain Surfaces");
            float neutralTop = terrainRoot.Find("Neutral Ground").GetComponent<Collider>().bounds.max.y;

            for (int i = 0; i < zones.Length; i++)
            {
                DinoDeploymentZone zone = zones[i];
                Assert.That(zone.IsGeometryValid, Is.True, zone.name);
                Assert.That(zone.GetComponent<BoxCollider>(), Is.Null, $"{zone.name} must not use AABB placement truth.");
                Assert.That(zone.WorldVertices.Select(vertex => new Vector2(vertex.x, vertex.z)).ToArray(), Is.EqualTo(expected[i]), zone.name);
                Assert.That(QuadrilateralXZ.TryCreate(expected[i][0], expected[i][1], expected[i][2], expected[i][3], out geometries[i]), Is.True);

                Mesh boundaryMesh = zone.transform.Find("Boundary").GetComponent<MeshFilter>().sharedMesh;
                Assert.That(boundaryMesh, Is.Not.Null, zone.name);
                Assert.That(boundaryMesh.GetTopology(0), Is.EqualTo(MeshTopology.Lines), zone.name);

                Transform surface = terrainRoot.Find(zone.DominantSurface.ToString());
                Assert.That(surface, Is.Not.Null, zone.name);
                MeshCollider trigger = surface.GetComponent<MeshCollider>();
                TerrainSurfaceVolume volume = surface.GetComponent<TerrainSurfaceVolume>();
                Transform visible = surface.Find("Visible Graybox");
                Mesh visualMesh = visible.GetComponent<MeshFilter>().sharedMesh;
                Assert.That(trigger, Is.Not.Null, zone.name);
                Assert.That(trigger.convex && trigger.isTrigger, Is.True, zone.name);
                Assert.That(volume.Surface, Is.EqualTo(zone.DominantSurface), zone.name);
                Assert.That(trigger.gameObject.layer, Is.EqualTo(2), zone.name);
                Assert.That(surface.Find("Visible Graybox").GetComponent<Collider>(), Is.Null, zone.name);
                Assert.That(visible.GetComponent<Renderer>().bounds.min.y, Is.GreaterThan(neutralTop + 0.001f),
                    $"{zone.name} terrain material must render above the neutral ground instead of being occluded");
                Assert.That(visualMesh.vertices.Select(vertex => new Vector2(vertex.x, vertex.z)).ToArray(), Is.EqualTo(expected[i]), zone.name);
                Assert.That(trigger.sharedMesh.vertices.Take(4).Select(vertex => new Vector2(vertex.x, vertex.z)).ToArray(), Is.EqualTo(expected[i]), zone.name);
            }

            for (int first = 0; first < geometries.Length; first++)
            {
                for (int second = first + 1; second < geometries.Length; second++)
                {
                    Assert.That(geometries[first].Overlaps(geometries[second]), Is.False, $"Z{first + 1}/Z{second + 1}");
                }
            }

            Vector2[] neutralSamples =
            {
                new(-26f, -21f), new(-11.5f, -19f), new(8.5f, -19f), new(22.5f, -23f), new(0f, 29f)
            };
            Assert.That(neutralSamples.All(sample => geometries.All(geometry => !geometry.Contains(sample))), Is.True);
            Assert.That(geometries.All(geometry => !geometry.Contains(new Vector2(-2.23f, -38.26f))), Is.True,
                "The village plaza must remain outside every deployment zone.");

            Transform neutral = terrainRoot.Find("Neutral Ground");
            Assert.That(neutral, Is.Not.Null);
            Assert.That(neutral.GetComponent<TerrainSurfaceVolume>(), Is.Null);
            Collider neutralCollider = neutral.GetComponent<Collider>();
            Assert.That(neutralCollider, Is.Not.Null);
            Assert.That(neutralCollider.isTrigger, Is.False);
            Assert.That(neutral.gameObject.layer, Is.EqualTo(6));
            Assert.That(neutral.GetComponent<Renderer>().sharedMaterial,
                Is.Not.SameAs(terrainRoot.Find("Grass/Visible Graybox").GetComponent<Renderer>().sharedMaterial),
                "the Grass region must remain visually distinguishable from public neutral ground");
            Assert.That(UnityEngine.Object.FindFirstObjectByType<DinoDeploymentService>(FindObjectsInactive.Include)
                .ValidateZoneConfiguration(), Is.True);
        }

        [Test]
        public void Validator_LegacyBoxTerrainDuringMigrationDoesNotThrow()
        {
            Assert.That(() => OpenTropicalBattlefieldValidator.ValidateLoadedScene(), Throws.Nothing);
        }

        [Test]
        public void Builder_IsIdempotent()
        {
            OpenTropicalBattlefieldSceneBuilder.Build(saveScene: true);
            string first = OpenTropicalBattlefieldValidator.ValidateLoadedScene().StructuralSignature;
            string firstCriticalReferences = CriticalReferenceSignature();
            string firstUnrelatedRoots = UnrelatedRootSignature();
            string firstBattlefieldBindings = BattlefieldBindingSignature();
            string firstNonBattlefieldWorld = NonBattlefieldWorldSignature();
            string firstBaseGuid = AssetDatabase.AssetPathToGUID("Assets/LlamAcademy/Dinos/Map/Generated/Open Tropical Defense Wall.prefab");
            string firstUpgradeGuid = AssetDatabase.AssetPathToGUID("Assets/LlamAcademy/Dinos/Map/Generated/Open Tropical Defense Wall Upgrade.prefab");
            OpenTropicalBattlefieldSceneBuilder.Build(saveScene: true);
            string second = OpenTropicalBattlefieldValidator.ValidateLoadedScene().StructuralSignature;

            Assert.That(second, Is.EqualTo(first));
            Assert.That(CriticalReferenceSignature(), Is.EqualTo(firstCriticalReferences));
            Assert.That(UnrelatedRootSignature(), Is.EqualTo(firstUnrelatedRoots));
            Assert.That(BattlefieldBindingSignature(), Is.EqualTo(firstBattlefieldBindings));
            Assert.That(NonBattlefieldWorldSignature(), Is.EqualTo(firstNonBattlefieldWorld));
            Assert.That(AssetDatabase.AssetPathToGUID("Assets/LlamAcademy/Dinos/Map/Generated/Open Tropical Defense Wall.prefab"), Is.EqualTo(firstBaseGuid));
            Assert.That(AssetDatabase.AssetPathToGUID("Assets/LlamAcademy/Dinos/Map/Generated/Open Tropical Defense Wall Upgrade.prefab"), Is.EqualTo(firstUpgradeGuid));
            Assert.That(EditorSceneManager.GetActiveScene().isDirty, Is.False);
        }

        [Test]
        public void Builder_CanonicalSceneIsByteStableAndDoesNotReplaceNavMeshData()
        {
            const string scenePath = "Assets/LlamAcademy/Dinos/Scenes/Dinos.unity";
            Component[] surfaces = GameObject.Find("World").GetComponents<Component>()
                .Where(component => component != null && component.GetType().FullName == "Unity.AI.Navigation.NavMeshSurface")
                .OrderBy(NavMeshAgentTypeId).ToArray();
            NavMeshData[] originalData = surfaces.Select(NavMeshDataForSurface).ToArray();
            string beforeHash = FileSha256(scenePath);

            OpenTropicalBattlefieldSceneBuilder.Build(saveScene: true);
            string firstHash = FileSha256(scenePath);
            NavMeshData[] firstData = surfaces.Select(NavMeshDataForSurface).ToArray();

            OpenTropicalBattlefieldSceneBuilder.Build(saveScene: true);
            string secondHash = FileSha256(scenePath);
            NavMeshData[] secondData = surfaces.Select(NavMeshDataForSurface).ToArray();
            string changedSurfaces = string.Join(", ", surfaces.Select((surface, index) =>
                    ReferenceEquals(originalData[index], firstData[index]) && ReferenceEquals(firstData[index], secondData[index])
                        ? null
                        : $"agentType={NavMeshAgentTypeId(surface)}")
                .Where(value => value != null));

            Assert.That(firstHash, Is.EqualTo(beforeHash),
                $"canonical Build(true) rewrote scene bytes; changed NavMeshData: [{changedSurfaces}]");
            Assert.That(secondHash, Is.EqualTo(beforeHash),
                $"second canonical Build(true) rewrote scene bytes; changed NavMeshData: [{changedSurfaces}]");
            Assert.That(firstData, Is.EqualTo(originalData), "canonical build must retain every NavMeshData reference");
            Assert.That(secondData, Is.EqualTo(originalData), "repeated canonical build must retain every NavMeshData reference");
            Assert.That(EditorSceneManager.GetActiveScene().isDirty, Is.False);
        }

        [Test]
        public void Builder_RepairsSavedCameraBoundsDeviationThenBecomesByteStable()
        {
            const string scenePath = "Assets/LlamAcademy/Dinos/Scenes/Dinos.unity";
            OpenTropicalBattlefieldSceneBuilder.Build(saveScene: true);
            string canonicalHash = FileSha256(scenePath);
            BoxCollider bounds = GameObject.Find("World/Camera Bounds").GetComponent<BoxCollider>();
            bounds.size = Vector3.one;
            EditorUtility.SetDirty(bounds);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            string deviationHash = FileSha256(scenePath);

            OpenTropicalBattlefieldReport repaired = OpenTropicalBattlefieldSceneBuilder.Build(saveScene: true);
            string repairedHash = FileSha256(scenePath);
            OpenTropicalBattlefieldSceneBuilder.Build(saveScene: true);
            string repeatedHash = FileSha256(scenePath);

            Assert.That(repaired.IsValid, Is.True);
            Assert.That(bounds.size, Is.EqualTo(new Vector3(104f, 6f, 82f)));
            Assert.That(repairedHash, Is.Not.EqualTo(deviationHash), "a real saved deviation must be repaired and written");
            Assert.That(repairedHash, Is.EqualTo(canonicalHash),
                "a camera-only deviation must not replace unrelated NavMeshData or scene object identities");
            Assert.That(repeatedHash, Is.EqualTo(repairedHash), "the repaired canonical scene must become a no-op");
            Assert.That(EditorSceneManager.GetActiveScene().isDirty, Is.False);
        }

        [Test]
        public void Builder_RepairsSavedZoneAndTriggerVertexDriftThenBecomesByteStable()
        {
            const string scenePath = "Assets/LlamAcademy/Dinos/Scenes/Dinos.unity";
            OpenTropicalBattlefieldSceneBuilder.Build(saveScene: true);
            DinoDeploymentZone zone = GameObject.Find("World/Open Tropical Battlefield/Deployment Zones/Z3")
                .GetComponent<DinoDeploymentZone>();
            SerializedObject zoneData = new(zone);
            SerializedProperty vertexC = zoneData.FindProperty("VertexC");
            vertexC.vector2Value += new Vector2(1f, 0f);
            zoneData.ApplyModifiedPropertiesWithoutUndo();

            MeshCollider trigger = GameObject.Find("World/Open Tropical Battlefield/Terrain Surfaces/ShallowWater")
                .GetComponent<MeshCollider>();
            Vector3[] drifted = trigger.sharedMesh.vertices;
            drifted[2] += new Vector3(1f, 0f, 0f);
            trigger.sharedMesh.vertices = drifted;
            EditorUtility.SetDirty(trigger.sharedMesh);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());
            EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene());
            string deviationHash = FileSha256(scenePath);

            OpenTropicalBattlefieldReport repaired = OpenTropicalBattlefieldSceneBuilder.Build(saveScene: true);
            string repairedHash = FileSha256(scenePath);
            OpenTropicalBattlefieldSceneBuilder.Build(saveScene: true);
            string repeatedHash = FileSha256(scenePath);

            Assert.That(repaired.IsValid, Is.True, repaired.Summary);
            Assert.That(zone.WorldVertices[2], Is.EqualTo(new Vector3(14f, 0f, 27f)));
            Assert.That(trigger.sharedMesh.vertices[2], Is.EqualTo(new Vector3(14f, -0.5f, 27f)));
            Assert.That(repairedHash, Is.Not.EqualTo(deviationHash));
            Assert.That(repeatedHash, Is.EqualTo(repairedHash));
            Assert.That(EditorSceneManager.GetActiveScene().isDirty, Is.False);
        }

        [Test]
        public void DefensePresets_UseRealWallsAndEnemyAiRegistersOnlyTheActivePreset()
        {
            VillageDefenseLayoutController layout = UnityEngine.Object.FindFirstObjectByType<VillageDefenseLayoutController>(FindObjectsInactive.Include);
            Component enemyAi = FindComponent("LlamAcademy.Dinos.Enemy.EnemyAIController");
            Assert.That(layout, Is.Not.Null);
            Assert.That(enemyAi, Is.Not.Null);

            Transform presetRoot = layout.transform;
            Transform[] presets = presetRoot.Cast<Transform>().Where(child => child.name.StartsWith("Preset ", StringComparison.Ordinal)).ToArray();
            Assert.That(presets.Length, Is.GreaterThanOrEqualTo(2));
            Assert.That(presets.All(preset => WallComponents(preset, true).Length >= 1), Is.True);

            MethodInfo refresh = enemyAi.GetType().GetMethod("RefreshActiveWalls", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo wallDatasField = enemyAi.GetType().GetField("WallDatas", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(refresh, Is.Not.Null);
            Assert.That(wallDatasField, Is.Not.Null);

            for (int round = 0; round < 2; round++)
            {
                Assert.That(layout.ActivateForRound(20260828, round), Is.True);
                refresh.Invoke(enemyAi, null);
                Component[] activePresetWalls = WallComponents(layout.ActivePresetRoot.transform, false);
                List<Component> registered = RegisteredWalls((IEnumerable)wallDatasField.GetValue(enemyAi));

                Assert.That(activePresetWalls, Is.Not.Empty);
                Assert.That(registered, Is.EquivalentTo(activePresetWalls));
                Assert.That(presets.Where(preset => preset.gameObject != layout.ActivePresetRoot)
                    .SelectMany(preset => WallComponents(preset, true))
                    .All(wall => !wall.gameObject.activeInHierarchy), Is.True);
                Assert.That(presets.SelectMany(preset => preset.GetComponentsInChildren<NavMeshObstacle>(true))
                    .All(obstacle => !obstacle.enabled), Is.True);
            }

            OpenTropicalBattlefieldSceneBuilder.Build(saveScene: true);
        }

        [Test]
        public void ActiveDefenseWall_PreservesRepairAndDeathEventBehavior()
        {
            VillageDefenseLayoutController layout = UnityEngine.Object.FindFirstObjectByType<VillageDefenseLayoutController>(FindObjectsInactive.Include);
            Component enemyAi = FindComponent("LlamAcademy.Dinos.Enemy.EnemyAIController");
            Assert.That(layout.ActivateForRound(20260828, 0), Is.True);

            MethodInfo refresh = enemyAi.GetType().GetMethod("RefreshActiveWalls", BindingFlags.Instance | BindingFlags.NonPublic);
            refresh.Invoke(enemyAi, null);
            Component wall = WallComponents(layout.ActivePresetRoot.transform, false).FirstOrDefault();
            Assert.That(wall, Is.Not.Null);

            PropertyInfo maxHealth = wall.GetType().GetProperty("MaxHealth", BindingFlags.Instance | BindingFlags.Public);
            PropertyInfo health = wall.GetType().GetProperty("Health", BindingFlags.Instance | BindingFlags.Public);
            int originalMaxHealth = (int)maxHealth.GetValue(wall);
            int originalHealth = (int)health.GetValue(wall);
            maxHealth.SetValue(wall, Math.Max(10, originalMaxHealth));
            health.SetValue(wall, 1);
            wall.GetType().GetMethod("Repair", BindingFlags.Instance | BindingFlags.Public).Invoke(wall, new object[] { 1 });
            Assert.That((int)health.GetValue(wall), Is.EqualTo(2));
            maxHealth.SetValue(wall, originalMaxHealth);
            health.SetValue(wall, originalHealth);

            FieldInfo wallDatasField = enemyAi.GetType().GetField("WallDatas", BindingFlags.Instance | BindingFlags.NonPublic);
            object registeredData = ((IEnumerable)wallDatasField.GetValue(enemyAi)).Cast<object>()
                .Single(data => ReferenceEquals(data.GetType().GetField("Wall").GetValue(data), wall));
            int deathsBefore = (int)registeredData.GetType().GetField("TimesDied").GetValue(registeredData);
            MethodInfo raiseDeath = wall.GetType().BaseType.GetMethod("RaiseDeathEvent", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(raiseDeath, Is.Not.Null);
            raiseDeath.Invoke(wall, null);
            object afterDeath = ((IEnumerable)wallDatasField.GetValue(enemyAi)).Cast<object>()
                .Single(data => ReferenceEquals(data.GetType().GetField("Wall").GetValue(data), wall));
            Assert.That((int)afterDeath.GetType().GetField("TimesDied").GetValue(afterDeath), Is.EqualTo(deathsBefore + 1));

            OpenTropicalBattlefieldSceneBuilder.Build(saveScene: true);
        }

        [Test]
        public void NewMapEnemyAi_IgnoresLegacyWaypointsAndRegistersDeadWallWhenPresetReturns()
        {
            VillageDefenseLayoutController layout = UnityEngine.Object.FindFirstObjectByType<VillageDefenseLayoutController>(FindObjectsInactive.Include);
            Component enemyAi = FindComponent("LlamAcademy.Dinos.Enemy.EnemyAIController");
            SerializedObject ai = new(enemyAi);
            Assert.That(ai.FindProperty("WaypointTransforms").arraySize, Is.Zero, "new-map AI must not retain /World/Archer Waypoints");

            Assert.That(layout.ActivateForRound(20260828, 0), Is.True);
            Component deadWall = WallComponents(layout.ActivePresetRoot.transform, true).Single();
            deadWall.gameObject.SetActive(false);
            Assert.That(layout.ActivateForRound(20260828, 1), Is.True);
            Assert.That(layout.ActivateForRound(20260828, 0), Is.True);
            enemyAi.GetType().GetMethod("RefreshActiveWalls", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(enemyAi, null);
            IEnumerable data = (IEnumerable)enemyAi.GetType().GetField("WallDatas", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(enemyAi);
            Assert.That(RegisteredWalls(data), Does.Contain(deadWall), "inactive dead wall must remain repairable when its preset returns");
            OpenTropicalBattlefieldSceneBuilder.Build(saveScene: true);
        }

        [Test]
        public void DefenseWalls_UseProjectLocalSafeOneStepUpgradeChain()
        {
            VillageDefenseLayoutController layout = UnityEngine.Object.FindFirstObjectByType<VillageDefenseLayoutController>(FindObjectsInactive.Include);
            Component wall = WallComponents(layout.ActivePresetRoot.transform, true).Single();
            SerializedObject serializedWall = new(wall);
            UnityEngine.Object unit = serializedWall.FindProperty("<UnitType>k__BackingField").objectReferenceValue;
            Assert.That(serializedWall.FindProperty("HealthBar").objectReferenceValue, Is.Not.Null,
                "generated walls must preserve player-facing damage and repair feedback");
            Assert.That(wall.GetComponentsInChildren<Component>(true)
                .Where(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.UI.HealthBar")
                .Any(component => component.gameObject.activeSelf), Is.True);
            Assert.That(AssetDatabase.GetAssetPath(unit), Does.StartWith("Assets/LlamAcademy/Dinos/Map/Generated/"));
            SerializedObject serializedUnit = new(unit);
            UnityEngine.Object upgrade = serializedUnit.FindProperty("<Upgrade>k__BackingField").objectReferenceValue;
            Assert.That(upgrade, Is.Not.Null);
            Assert.That(AssetDatabase.GetAssetPath(upgrade), Does.StartWith("Assets/LlamAcademy/Dinos/Map/Generated/"));
            Assert.That(new SerializedObject(upgrade).FindProperty("<Upgrade>k__BackingField").objectReferenceValue, Is.Null);
        }

        [Test]
        public void HistoricalTrainingScene_ExplicitlyEnablesGuardedTriangulationFallback()
        {
            string activePath = EditorSceneManager.GetActiveScene().path;
            UnityEngine.SceneManagement.Scene training = EditorSceneManager.OpenScene(
                "Assets/LlamAcademy/Dinos/Scenes/DinoAttackTraining.unity", OpenSceneMode.Additive);
            Component ai = training.GetRootGameObjects().SelectMany(root => root.GetComponentsInChildren<Component>(true))
                .First(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Enemy.EnemyAIController");
            Assert.That(new SerializedObject(ai).FindProperty("AllowHistoricalTriangulationFallback").boolValue, Is.True);
            Assert.That(UnityEngine.Object.FindObjectsByType<VillageDefenseLayoutController>(FindObjectsInactive.Include,
                FindObjectsSortMode.None).All(layout => layout.gameObject.scene != training), Is.True);
            Assert.That(training.isDirty, Is.False);
            EditorSceneManager.CloseScene(training, true);
            Assert.That(EditorSceneManager.GetActiveScene().path, Is.EqualTo(activePath));
        }

        [Test]
        public void EveryDefensePreset_HasCompleteGameplayAndDefenderSlotMatricesAndRestoresCanonicalScene()
        {
            const string scenePath = "Assets/LlamAcademy/Dinos/Scenes/Dinos.unity";
            string beforeHash = FileSha256(scenePath);
            OpenTropicalPresetNavigationReport[] reports = OpenTropicalBattlefieldValidator.ValidateEveryPresetAndRestoreCanonical(true);
            string afterHash = FileSha256(scenePath);
            Assert.That(reports.Length, Is.EqualTo(2));
            Assert.That(reports.All(report => report.Paths.Length == 15 && report.Paths.All(path =>
                path.StartSampled && path.GoalSampled && path.Status == NavMeshPathStatus.PathComplete.ToString() && path.Length > 0f)), Is.True);
            Assert.That(reports.All(report => report.DefenderSlotPaths.Length > 0 && report.DefenderSlotPaths.All(path =>
                path.StartSampled && path.GoalSampled && path.Status == NavMeshPathStatus.PathComplete.ToString() && path.Length > 0f)), Is.True);
            VillageDefenseLayoutController layout = UnityEngine.Object.FindFirstObjectByType<VillageDefenseLayoutController>(FindObjectsInactive.Include);
            Assert.That(layout.ActivePresetIndex, Is.EqualTo(-1));
            Assert.That(layout.gameObject.activeSelf, Is.False);
            Assert.That(layout.transform.Cast<Transform>().Where(child =>
                child.name.StartsWith("Preset ", StringComparison.Ordinal)).All(child => !child.gameObject.activeSelf), Is.True);
            Assert.That(EditorSceneManager.GetActiveScene().isDirty, Is.False);
            Assert.That(afterHash, Is.EqualTo(beforeHash),
                "preset validation must restore the exact canonical scene instead of persisting temporary NavMesh bakes");
        }

        private static List<Component> RegisteredWalls(IEnumerable wallDatas)
        {
            List<Component> walls = new();
            foreach (object data in wallDatas)
            {
                FieldInfo wallField = data.GetType().GetField("Wall", BindingFlags.Instance | BindingFlags.Public);
                walls.Add((Component)wallField.GetValue(data));
            }

            return walls;
        }

        private static Component FindComponent(string fullName) =>
            UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(component => component != null && component.GetType().FullName == fullName);

        private static Component[] WallComponents(Transform root, bool includeInactive) =>
            root.GetComponentsInChildren<Component>(includeInactive)
                .Where(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Unit.Wall")
                .ToArray();

        private static string CriticalReferenceSignature()
        {
            VillageDefenseLayoutController layout = UnityEngine.Object.FindFirstObjectByType<VillageDefenseLayoutController>(FindObjectsInactive.Include);
            Component ai = FindComponent("LlamAcademy.Dinos.Enemy.EnemyAIController");
            SerializedObject serializedAi = new(ai);
            string slots = string.Join("|", layout.transform.GetComponentsInChildren<Transform>(true)
                .Where(transform => transform.name.StartsWith("Spawn Slot ", StringComparison.Ordinal))
                .OrderBy(transform => transform.parent.name).ThenBy(transform => transform.name)
                .Select(transform => transform.parent.name + "/" + transform.name + "@" + transform.position.ToString("F3")));
            return $"layout={serializedAi.FindProperty("DefenseLayout").objectReferenceValue == layout};" +
                   $"waypointTransforms={serializedAi.FindProperty("WaypointTransforms").arraySize};" +
                   $"slots={slots}";
        }

        private static string UnrelatedRootSignature() => string.Join("|", SceneManager.GetActiveScene().GetRootGameObjects()
            .Where(root => root.name != "Open Tropical Battlefield")
            .OrderBy(root => root.name)
            .Select(root => root.name + "=" + root.activeSelf));

        private static string BattlefieldBindingSignature()
        {
            VillageDefenseLayoutController layout = UnityEngine.Object.FindFirstObjectByType<VillageDefenseLayoutController>(FindObjectsInactive.Include);
            Component service = FindComponent("LlamAcademy.Dinos.Map.DinoDeploymentService");
            Component presenter = FindComponent("LlamAcademy.Dinos.Map.DinoDeploymentZonePresenter");
            List<string> parts = new()
            {
                "layout:" + SerializedObjectReferenceSignature(layout),
                "service:" + SerializedObjectReferenceSignature(service),
                "presenter:" + SerializedObjectReferenceSignature(presenter)
            };
            Transform battlefield = layout.transform.root.Find("World/Open Tropical Battlefield") ??
                                    GameObject.Find("World/Open Tropical Battlefield")?.transform;
            Assert.That(battlefield, Is.Not.Null);
            foreach (Renderer renderer in battlefield.GetComponentsInChildren<Renderer>(true)
                         .OrderBy(candidate => HierarchyPath(candidate.transform)))
            {
                string materials = string.Join(",", renderer.sharedMaterials.Select(ObjectIdentity));
                parts.Add($"renderer:{HierarchyPath(renderer.transform)}:enabled={renderer.enabled}:materials={materials}");
            }
            return string.Join("\n", parts);
        }

        private static string NonBattlefieldWorldSignature()
        {
            Transform world = SceneManager.GetActiveScene().GetRootGameObjects()
                .Select(root => root.transform).First(transform => transform.name == "World");
            List<string> parts = new();
            foreach (Transform topLevel in world.Cast<Transform>()
                         .Where(child => child.name != "Open Tropical Battlefield")
                         .OrderBy(child => child.name))
            foreach (Transform transform in topLevel.GetComponentsInChildren<Transform>(true)
                         .OrderBy(HierarchyPath))
            {
                string components = string.Join(",", transform.GetComponents<Component>()
                    .Where(component => component != null).Select(component => component.GetType().FullName));
                string references = string.Join(";", transform.GetComponents<Component>()
                    .Where(component => component != null).Select(SerializedObjectReferenceSignature));
                parts.Add($"{HierarchyPath(transform)}:active={transform.gameObject.activeSelf}:sibling={transform.GetSiblingIndex()}:" +
                          $"components={components}:refs={references}");
            }
            return string.Join("\n", parts);
        }

        private static string SerializedObjectReferenceSignature(Component component)
        {
            if (component == null) return "<missing>";
            SerializedObject serialized = new(component);
            SerializedProperty iterator = serialized.GetIterator();
            List<string> references = new();
            bool enterChildren = true;
            while (iterator.Next(enterChildren))
            {
                enterChildren = false;
                if (iterator.propertyType == SerializedPropertyType.ObjectReference)
                    references.Add(iterator.propertyPath + "=" + ObjectIdentity(iterator.objectReferenceValue));
            }
            return component.GetType().FullName + "{" + string.Join(",", references) + "}";
        }

        private static string ObjectIdentity(UnityEngine.Object value)
        {
            if (value == null) return "null";
            string assetPath = AssetDatabase.GetAssetPath(value);
            if (!string.IsNullOrEmpty(assetPath))
                return $"asset:{AssetDatabase.AssetPathToGUID(assetPath)}:{assetPath}:{value.name}";
            if (value is Component component)
                return $"scene:{HierarchyPath(component.transform)}:{component.GetType().FullName}";
            if (value is GameObject gameObject)
                return "scene:" + HierarchyPath(gameObject.transform);
            return value.GetType().FullName + ":" + value.name;
        }

        private static string HierarchyPath(Transform transform)
        {
            Stack<string> names = new();
            while (transform != null)
            {
                names.Push(transform.name);
                transform = transform.parent;
            }
            return "/" + string.Join("/", names);
        }

        private static Component[] FindRuntimeComponents(string fullTypeName) =>
            UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(component => component != null && component.GetType().FullName == fullTypeName)
                .ToArray();

        private static string VisualSerializationSignature(Transform artPass)
        {
            Assert.That(artPass, Is.Not.Null);
            StringBuilder builder = new();
            foreach (Transform transform in artPass.GetComponentsInChildren<Transform>(true)
                         .OrderBy(HierarchyPath, StringComparer.Ordinal))
            {
                builder.Append(HierarchyPath(transform)).Append('|')
                    .Append(transform.gameObject.activeSelf).Append('|')
                    .Append(transform.gameObject.layer).Append('|')
                    .Append((int)GameObjectUtility.GetStaticEditorFlags(transform.gameObject)).Append('|')
                    .Append(transform.localPosition.ToString("F4")).Append('|')
                    .Append(transform.localEulerAngles.ToString("F4")).Append('|')
                    .Append(transform.localScale.ToString("F4")).Append('|')
                    .Append(string.Join(",", transform.GetComponents<Component>()
                        .Where(component => component != null)
                        .Select(component => component.GetType().FullName)
                        .OrderBy(name => name, StringComparer.Ordinal)));
                foreach (Renderer renderer in transform.GetComponents<Renderer>())
                {
                    builder.Append("|renderer=").Append(renderer.enabled).Append(':')
                        .Append(renderer.shadowCastingMode).Append(':').Append(renderer.receiveShadows).Append(':')
                        .Append(string.Join(",", renderer.sharedMaterials.Select(material =>
                            ObjectIdentity(material) + ":" + (material == null ? "null" : EditorJsonUtility.ToJson(material)))));
                }
                builder.AppendLine();
            }

            using SHA256 sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(Encoding.UTF8.GetBytes(builder.ToString())))
                .Replace("-", string.Empty);
        }

        private static string FileSha256(string assetPath)
        {
            string absolutePath = Path.GetFullPath(assetPath);
            using SHA256 sha256 = SHA256.Create();
            return BitConverter.ToString(sha256.ComputeHash(File.ReadAllBytes(absolutePath))).Replace("-", string.Empty);
        }

        private static int NavMeshAgentTypeId(Component surface) =>
            (int)surface.GetType().GetProperty("agentTypeID").GetValue(surface);

        private static NavMeshData NavMeshDataForSurface(Component surface) =>
            new SerializedObject(surface).FindProperty("m_NavMeshData")?.objectReferenceValue as NavMeshData;
    }
}
