using System;
using System.IO;
using System.Linq;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.Player;
using LlamAcademy.Dinos.UI;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LlamAcademy.Dinos.Training.Editor
{
    public static class DinoTrainingSceneBuilder
    {
        private const string SourceScene = "Assets/LlamAcademy/Dinos/Scenes/Dinos.unity";
        private const string TrainingScene = DinoTrainingBuildLayout.TrainingScene;

        [MenuItem("Dino Attack/Training/Rebuild Training Scene")]
        public static void Build()
        {
            Scene scene = EditorSceneManager.OpenScene(SourceScene, OpenSceneMode.Single);
            if (!scene.IsValid())
            {
                throw new InvalidOperationException($"Unable to open source scene {SourceScene}.");
            }

            if (!EditorSceneManager.SaveScene(scene, TrainingScene, true))
            {
                throw new InvalidOperationException($"Unable to copy scene to {TrainingScene}.");
            }

            DinoSpawner spawner = UnityEngine.Object.FindFirstObjectByType<DinoSpawner>();
            PlaceDinoVisualization visualization = UnityEngine.Object.FindFirstObjectByType<PlaceDinoVisualization>();
            if (spawner == null || visualization == null)
            {
                throw new InvalidOperationException("Training scene requires DinoSpawner and PlaceDinoVisualization.");
            }

            SerializedObject spawnerObject = new(spawner);
            LayerMask groundLayers = spawnerObject.FindProperty("GroundLayer").intValue;
            SerializedObject visualizationObject = new(visualization);
            LayerMask unsafeLayers = visualizationObject.FindProperty("UnsafeLayers").intValue;

            GameObject environment = new("Dino Training Environment");
            BoxCollider placementBounds = environment.AddComponent<BoxCollider>();
            placementBounds.isTrigger = true;
            Bounds groundBounds = CalculateGroundBounds(groundLayers, spawner.transform.position);
            placementBounds.center = groundBounds.center;
            placementBounds.size = new Vector3(
                Mathf.Max(groundBounds.size.x, 30f),
                Mathf.Max(groundBounds.size.y, 2f),
                Mathf.Max(groundBounds.size.z, 30f));

            DinoTrainingArea trainingArea = environment.AddComponent<DinoTrainingArea>();
            trainingArea.Configure(placementBounds, groundLayers, unsafeLayers);
            DinoTrainingSceneReloader reloader = environment.AddComponent<DinoTrainingSceneReloader>();
            DinoTrainingScenarioRandomizer randomizer = environment.AddComponent<DinoTrainingScenarioRandomizer>();
            randomizer.Configure(trainingArea);

            BehaviorParameters behaviorParameters = environment.AddComponent<BehaviorParameters>();
            behaviorParameters.BehaviorName = "DinoAttackPlanner";
            behaviorParameters.BehaviorType = BehaviorType.Default;
            behaviorParameters.BrainParameters.VectorObservationSize = DinoTrainingObservationLayout.ObservationSize;
            behaviorParameters.BrainParameters.NumStackedVectorObservations = 1;
            behaviorParameters.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(DinoTrainingActionCodec.ActionSize);

            DinoTrainingAgent agent = environment.AddComponent<DinoTrainingAgent>();
            DinoSO[] dinoTypes = LoadDinoTypes();
            agent.Configure(trainingArea, reloader, randomizer, dinoTypes);
            DinoTrainingDecisionScheduler scheduler = environment.AddComponent<DinoTrainingDecisionScheduler>();
            scheduler.Configure(agent, 0.75f);

            spawner.enabled = false;
            visualization.enabled = false;
            RuntimeUI runtimeUi = UnityEngine.Object.FindFirstObjectByType<RuntimeUI>();
            if (runtimeUi != null)
            {
                runtimeUi.enabled = false;
            }
            CameraControl cameraControl = UnityEngine.Object.FindFirstObjectByType<CameraControl>();
            if (cameraControl != null)
            {
                cameraControl.enabled = false;
            }

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, TrainingScene))
            {
                throw new InvalidOperationException($"Unable to save training scene {TrainingScene}.");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Created {TrainingScene} with {DinoTrainingObservationLayout.ObservationSize} observations and {DinoTrainingActionCodec.ActionSize} continuous actions.");
        }

        [MenuItem("Dino Attack/Training/Build Training Player")]
        public static void BuildTrainingPlayer()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the Unity project root.");
            string outputPath = DinoTrainingBuildLayout.ResolvePlayerPath(projectRoot);
            string outputDirectory = Path.GetDirectoryName(outputPath)
                ?? throw new InvalidOperationException($"Unable to resolve build directory for {outputPath}.");
            Directory.CreateDirectory(outputDirectory);

            BuildPlayerOptions options = new()
            {
                scenes = DinoTrainingBuildLayout.CreateSceneList(),
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };

            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Training Player build failed with result {report.summary.result} and " +
                    $"{report.summary.totalErrors} errors.");
            }

            Debug.Log(
                $"Built isolated Dino Attack training Player at {outputPath}; " +
                $"scenes={string.Join(",", options.scenes)}; bytes={report.summary.totalSize}.");
        }

        private static Bounds CalculateGroundBounds(LayerMask groundLayers, Vector3 fallbackCenter)
        {
            Collider[] colliders = UnityEngine.Object.FindObjectsByType<Collider>(
                FindObjectsInactive.Exclude,
                FindObjectsSortMode.None);
            bool found = false;
            Bounds bounds = new(fallbackCenter, new Vector3(30f, 2f, 30f));
            foreach (Collider collider in colliders)
            {
                if (collider.isTrigger || (groundLayers.value & 1 << collider.gameObject.layer) == 0)
                {
                    continue;
                }

                if (!found)
                {
                    bounds = collider.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(collider.bounds);
                }
            }
            return bounds;
        }

        private static DinoSO[] LoadDinoTypes()
        {
            string[] guids = AssetDatabase.FindAssets(
                "t:DinoSO",
                new[] { "Assets/LlamAcademy/Dinos/Dinos" });
            DinoSO[] dinos = guids
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<DinoSO>)
                .Where(dino => dino != null)
                .OrderBy(dino => dino.Cost)
                .Take(3)
                .ToArray();
            if (dinos.Length != 3)
            {
                throw new InvalidOperationException($"Expected three DinoSO assets, found {dinos.Length}.");
            }
            return dinos;
        }

    }
}
