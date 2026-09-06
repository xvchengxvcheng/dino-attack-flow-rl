using System;
using System.Linq;
using LlamAcademy.Dinos.Config;
using LlamAcademy.Dinos.Enemy;
using LlamAcademy.Dinos.Enemy.Defense;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Player;
using LlamAcademy.Dinos.RoundManagement;
using Unity.MLAgents.Actuators;
using Unity.MLAgents.Policies;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace LlamAcademy.Dinos.Training.Editor
{
    public static class DinoFormalTrainingSceneInstaller
    {
        private const string ScenePath = DinoFormalTrainingBuildLayout.MainScene;
        private const string LayeredScenePath = "Assets/LlamAcademy/Dinos/Scenes/LayeredBattlefield.unity";
        private const string TrainingRootName = "Training";
        private const string EnvironmentName = "FormalTrainingEnvironment";

        private static readonly string[] DinoAssetPaths =
        {
            "Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor.asset",
            "Assets/LlamAcademy/Dinos/Dinos/Pachycephalosaurus/Pachycephalosaurus.asset",
            "Assets/LlamAcademy/Dinos/Dinos/TRex/TRex.asset"
        };

        [MenuItem("Dino Attack/Training/Install Formal Training Environment")]
        private static void InstallFromMenu()
        {
            Debug.Log(Install()
                ? "Installed formal training environment; save Dinos.unity to persist it."
                : "Formal training environment is already canonical; no changes made.");
        }

        public static bool Install()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded ||
                (scene.path != ScenePath && scene.path != LayeredScenePath))
            {
                throw new InvalidOperationException($"Open {ScenePath} or {LayeredScenePath} before installing formal training.");
            }

            bool changed = false;
            GameObject trainingRoot = GetOrCreateRoot(scene, TrainingRootName, ref changed);
            GameObject environment = GetOrCreateChild(trainingRoot.transform, EnvironmentName, ref changed);
            changed |= SetCanonicalTransform(trainingRoot.transform);
            changed |= SetCanonicalTransform(environment.transform);

            BoxCollider mapBounds = GetOrAdd<BoxCollider>(environment, ref changed);
            DinoTrainingArea trainingArea = GetOrAdd<DinoTrainingArea>(environment, ref changed);
            DinoTrainingSceneReloader reloader = GetOrAdd<DinoTrainingSceneReloader>(environment, ref changed);
            BehaviorParameters behavior = GetOrAdd<BehaviorParameters>(environment, ref changed);
            DinoTrainingAgent agent = GetOrAdd<DinoTrainingAgent>(environment, ref changed);
            DinoStructuredObservationBuilder builder = GetOrAdd<DinoStructuredObservationBuilder>(environment, ref changed);
            DinoStructuredObservationSensorComponent sensors =
                GetOrAdd<DinoStructuredObservationSensorComponent>(environment, ref changed);
            DinoTrainingDecisionScheduler scheduler = GetOrAdd<DinoTrainingDecisionScheduler>(environment, ref changed);
            DinoTrainingBootstrap bootstrap = GetOrAdd<DinoTrainingBootstrap>(environment, ref changed);

            DinoDeploymentZone[] zones = FindSceneObjects<DinoDeploymentZone>(scene)
                .Where(zone => scene.path != LayeredScenePath ||
                    (zone.transform.parent != null && zone.transform.parent.name == "Preset 0"))
                .OrderBy(zone => zone.Id)
                .ToArray();
            if (zones.Length != Enum.GetValues(typeof(DeploymentZoneId)).Length ||
                !zones.Select(zone => zone.Id).SequenceEqual(Enum.GetValues(typeof(DeploymentZoneId)).Cast<DeploymentZoneId>()))
            {
                throw new InvalidOperationException("Formal training requires exactly one valid Z1-Z5 zone sequence.");
            }

            DinoSO[] dinos = DinoAssetPaths.Select(path => AssetDatabase.LoadAssetAtPath<DinoSO>(path)).ToArray();
            if (dinos.Any(dino => dino == null))
            {
                throw new InvalidOperationException("Formal DinoSO assets are missing from the frozen action order.");
            }

            RoundManager roundManager = FindExactlyOne<RoundManager>(scene);
            DinoSpawner spawner = FindExactlyOne<DinoSpawner>(scene);
            EnemyAIController enemy = FindExactlyOne<EnemyAIController>(scene);
            SessionDefenseLayoutController defense = FindExactlyOne<SessionDefenseLayoutController>(scene);
            SessionGroundDefenseController groundDefense = FindExactlyOne<SessionGroundDefenseController>(scene);
            GameSessionRestartService restart = FindExactlyOne<GameSessionRestartService>(scene);
            _ = enemy;

            changed |= SetBoxCollider(mapBounds);
            changed |= SetSerialized(trainingArea,
                ("PlacementBounds", (UnityEngine.Object)mapBounds));
            changed |= SetSerialized(agent,
                ("TrainingArea", trainingArea),
                ("SceneReloader", reloader),
                ("ScenarioRandomizer", null));
            changed |= SetSerializedArray(agent, "DinoTypes", dinos);
            changed |= SetSerializedArray(agent, "Zones", zones);
            changed |= SetSerialized(builder,
                ("DefenseLayout", defense),
                ("GroundDefenseLayout", groundDefense),
                ("RoundManagerSource", roundManager),
                ("DinoSpawnerSource", spawner),
                ("TrainingAgent", agent));
            changed |= SetSerializedArray(builder, "DinoTypes", dinos);
            changed |= SetSerializedArray(builder, "Zones", zones);
            changed |= SetSerialized(sensors, ("Source", builder));
            changed |= SetSerialized(scheduler, ("Agent", agent));
            changed |= SetSerialized(bootstrap,
                ("Agent", agent),
                ("ObservationBuilder", builder),
                ("SensorComponent", sensors),
                ("DecisionScheduler", scheduler),
                ("SceneReloader", reloader),
                ("TrainingArea", trainingArea),
                ("BehaviorParameters", behavior),
                ("RestartService", restart));

            changed |= ConfigureBehaviorParameters(behavior);
            changed |= SetEnabled(mapBounds, false);
            changed |= SetEnabled(trainingArea, false);
            changed |= SetEnabled(reloader, false);
            changed |= SetEnabled(behavior, false);
            changed |= SetEnabled(agent, false);
            changed |= SetEnabled(builder, false);
            changed |= SetEnabled(sensors, false);
            changed |= SetEnabled(scheduler, false);
            changed |= SetEnabled(bootstrap, true);

            if (changed)
            {
                EditorSceneManager.MarkSceneDirty(scene);
            }
            return changed;
        }

        private static GameObject GetOrCreateRoot(Scene scene, string name, ref bool changed)
        {
            GameObject[] matches = scene.GetRootGameObjects().Where(root => root.name == name).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException($"Scene contains multiple /{name} roots.");
            if (matches.Length == 1) return matches[0];
            GameObject created = new(name);
            SceneManager.MoveGameObjectToScene(created, scene);
            changed = true;
            return created;
        }

        private static GameObject GetOrCreateChild(Transform parent, string name, ref bool changed)
        {
            Transform[] matches = parent.Cast<Transform>().Where(child => child.name == name).ToArray();
            if (matches.Length > 1) throw new InvalidOperationException($"Scene contains multiple /Training/{name} objects.");
            if (matches.Length == 1) return matches[0].gameObject;
            GameObject created = new(name);
            created.transform.SetParent(parent, false);
            changed = true;
            return created;
        }

        private static bool SetCanonicalTransform(Transform transform)
        {
            bool changed = transform.localPosition != Vector3.zero || transform.localRotation != Quaternion.identity ||
                           transform.localScale != Vector3.one || !transform.gameObject.activeSelf;
            if (!changed) return false;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
            transform.localScale = Vector3.one;
            transform.gameObject.SetActive(true);
            return true;
        }

        private static T GetOrAdd<T>(GameObject target, ref bool changed) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component != null) return component;
            changed = true;
            return target.AddComponent<T>();
        }

        private static bool SetBoxCollider(BoxCollider bounds)
        {
            Vector3 center = new(-3.5f, 0f, -12f);
            Vector3 size = new(104f, 2f, 82f);
            bool changed = bounds.center != center || bounds.size != size || !bounds.isTrigger;
            if (!changed) return false;
            bounds.center = center;
            bounds.size = size;
            bounds.isTrigger = true;
            EditorUtility.SetDirty(bounds);
            return true;
        }

        private static bool ConfigureBehaviorParameters(BehaviorParameters behavior)
        {
            bool changed = behavior.BehaviorName != "DinoAttackPlanner" ||
                           behavior.BehaviorType != BehaviorType.Default ||
                           behavior.BrainParameters.VectorObservationSize != 0 ||
                           behavior.BrainParameters.NumStackedVectorObservations != 1 ||
                           behavior.BrainParameters.ActionSpec.NumContinuousActions != DinoTrainingActionCodec.ActionSize ||
                           behavior.BrainParameters.ActionSpec.NumDiscreteActions != 0;
            if (!changed) return false;
            behavior.BehaviorName = "DinoAttackPlanner";
            behavior.BehaviorType = BehaviorType.Default;
            behavior.BrainParameters.VectorObservationSize = 0;
            behavior.BrainParameters.NumStackedVectorObservations = 1;
            behavior.BrainParameters.ActionSpec = ActionSpec.MakeContinuous(DinoTrainingActionCodec.ActionSize);
            EditorUtility.SetDirty(behavior);
            return true;
        }

        private static bool SetSerialized(Component component, params (string field, UnityEngine.Object value)[] values)
        {
            SerializedObject serialized = new(component);
            bool changed = false;
            foreach ((string field, UnityEngine.Object value) in values)
            {
                SerializedProperty property = serialized.FindProperty(field)
                    ?? throw new MissingFieldException(component.GetType().FullName, field);
                if (property.objectReferenceValue == value) continue;
                property.objectReferenceValue = value;
                changed = true;
            }
            if (changed) serialized.ApplyModifiedPropertiesWithoutUndo();
            return changed;
        }

        private static bool SetSerializedArray<T>(Component component, string field, T[] values)
            where T : UnityEngine.Object
        {
            SerializedObject serialized = new(component);
            SerializedProperty property = serialized.FindProperty(field)
                ?? throw new MissingFieldException(component.GetType().FullName, field);
            bool changed = property.arraySize != values.Length;
            if (!changed)
            {
                for (int index = 0; index < values.Length; index++)
                    changed |= property.GetArrayElementAtIndex(index).objectReferenceValue != values[index];
            }
            if (!changed) return false;
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return true;
        }

        private static bool SetEnabled(Behaviour behaviour, bool value)
        {
            if (behaviour.enabled == value) return false;
            behaviour.enabled = value;
            EditorUtility.SetDirty(behaviour);
            return true;
        }

        private static bool SetEnabled(Collider collider, bool value)
        {
            if (collider.enabled == value) return false;
            collider.enabled = value;
            EditorUtility.SetDirty(collider);
            return true;
        }

        private static T FindExactlyOne<T>(Scene scene) where T : Component
        {
            T[] matches = FindSceneObjects<T>(scene);
            if (matches.Length != 1)
                throw new InvalidOperationException($"Formal training requires exactly one {typeof(T).Name}, found {matches.Length}.");
            return matches[0];
        }

        private static T[] FindSceneObjects<T>(Scene scene) where T : Component =>
            UnityEngine.Object.FindObjectsByType<T>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Where(component => component.gameObject.scene == scene)
                .ToArray();
    }
}
