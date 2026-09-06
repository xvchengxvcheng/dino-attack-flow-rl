using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Collections;
using System.Collections.Generic;
using System.Linq.Expressions;
using LlamAcademy.Dinos.Map;
using LlamAcademy.Dinos.Deployment;
using LlamAcademy.Dinos.Inference;
using LlamAcademy.Dinos.Session;
using LlamAcademy.Dinos.Training;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.InputSystem;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.Tests.PlayMode
{
    public sealed class OpenTropicalBattlefieldPlayModeTests
    {
        private const float BaseSpeed = 8f;
        private static readonly List<object> SpawnedWalls = new();
        private static int WallDeathEvents;

        [UnityTest]
        public IEnumerator GameResultCollector_EndedReadsTheFormalRewardSummaryExactlyOnce()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 3; frame++) yield return null;

            Component roundManager = FindRuntimeComponent(
                "LlamAcademy.Dinos.RoundManagement.RoundManager");
            Component spawner = FindRuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
            Component rewardAgent = FindRuntimeComponent(
                "LlamAcademy.Dinos.Training.DinoTrainingAgent");
            Component runtimeUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
            Component collector = runtimeUi.GetComponent(RuntimeType(
                "LlamAcademy.Dinos.Session.GameResultCollector"));
            Assert.That(rewardAgent, Is.Not.Null);
            Assert.That(collector, Is.Not.Null);

            IBattlefieldLayoutSignatureProvider[] signatureProviders = UnityEngine.Object
                .FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .OfType<IBattlefieldLayoutSignatureProvider>()
                .ToArray();
            Assert.That(signatureProviders, Is.Not.Empty);
            BattlefieldLayoutSignature readySignature = null;
            for (int frame = 0; frame < 60 && readySignature == null; frame++)
            {
                readySignature = signatureProviders
                    .Select(provider => provider.TryCapture(out BattlefieldLayoutSignature candidate)
                        ? candidate
                        : null)
                    .SingleOrDefault(candidate => candidate != null);
                yield return null;
            }
            Assert.That(readySignature, Is.Not.Null,
                "The generated session layout must be ready before the synthetic ending.");

            roundManager.GetType().GetMethod("StartRound").Invoke(roundManager, null);
            spawner.GetType().GetProperty("ResourcesToSpend").SetValue(spawner, 321);
            roundManager.GetType().GetMethod(
                "BeginEnding", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(roundManager, new object[] { true });
            roundManager.GetType().GetMethod(
                "FinishSession", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(roundManager, null);
            yield return null;

            Assert.That(
                (bool)collector.GetType().GetProperty("HasFrozenResult").GetValue(collector),
                Is.True);
            GameResultSnapshot frozen = (GameResultSnapshot)collector.GetType()
                .GetProperty("FrozenResult").GetValue(collector);
            float formalReward = (float)rewardAgent.GetType()
                .GetProperty("LastCompletedEpisodeReward").GetValue(rewardAgent);
            Assert.That(frozen.TotalReward, Is.EqualTo(formalReward),
                "The collector must copy the formal reward summary, not recompute it.");
            Assert.That(frozen.FinalMeat, Is.EqualTo(321));
            Assert.That(frozen.Won, Is.True);

            GameResultSnapshot beforeRepeatedEnded = frozen;
            roundManager.GetType().GetMethod(
                "FinishSession", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(roundManager, null);
            yield return null;
            Assert.That(
                (GameResultSnapshot)collector.GetType().GetProperty("FrozenResult").GetValue(collector),
                Is.EqualTo(beforeRepeatedEnded));
        }

        [UnityTest]
        public IEnumerator GameResultCollector_TrainingLaunchDoesNotCapturePlayerComparisonResult()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 3; frame++) yield return null;

            Component roundManager = FindRuntimeComponent(
                "LlamAcademy.Dinos.RoundManagement.RoundManager");
            Component runtimeUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
            Component collector = runtimeUi.GetComponent(RuntimeType(
                "LlamAcademy.Dinos.Session.GameResultCollector"));
            Assert.That(roundManager, Is.Not.Null);
            Assert.That(collector, Is.Not.Null);

            GameLaunchRequest training = GameLaunchRequest.CreateTraining(
                GameMapId.OpenTropicalBattlefield,
                271828);
            collector.GetType().GetMethod("Configure").Invoke(
                collector,
                new object[] { training, (GameResultSnapshot?)null });

            roundManager.GetType().GetMethod("StartRound").Invoke(roundManager, null);
            roundManager.GetType().GetMethod(
                "BeginEnding", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(roundManager, new object[] { false });
            Assert.DoesNotThrow(() => roundManager.GetType().GetMethod(
                "FinishSession", BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(roundManager, null));
            yield return null;

            Assert.That(
                (bool)collector.GetType().GetProperty("HasFrozenResult").GetValue(collector),
                Is.False,
                "Training episodes are consumed by ML-Agents and must not enter player/AI comparison capture.");
        }

        [UnityTest]
        public IEnumerator TrainingAndInference_SameSeedAndActionsKeepGameplayLifecycleEquivalent()
        {
            const int baseSeed = 7;
            const int environmentIndex = 2;
            const int processGeneration = 3;
            const string modelId = "lifecycle-equivalence-test";
            string modelHash = new('c', 64);
            int layoutSeed = (int)RuntimeType(
                    "LlamAcademy.Dinos.Training.DinoTrainingRunContext")
                .GetMethod("DeriveEpisodeSeed", BindingFlags.Static | BindingFlags.Public)
                .Invoke(null, new object[] { baseSeed, environmentIndex, 0 });
            float[] rejectedDeployment = { -1f, 0f, 0f, -0.25f };
            string[] trainingArguments =
            {
                "game.exe", "--dino-training", "--dino-base-seed", baseSeed.ToString(),
                "--dino-environment-index", environmentIndex.ToString(),
                "--dino-process-generation", processGeneration.ToString()
            };
            object originalArgumentsProvider = OverrideTrainingArguments(trainingArguments);
            float originalTimeScale = Time.timeScale;
            int unexpectedErrors = 0;
            bool argumentsRestored = false;
            ScriptableObject config = null;
            ScriptableObject syntheticModel = null;
            Type academyType = RuntimeType("Unity.MLAgents.Academy");
            object academy = academyType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public)
                .GetValue(null);
            PropertyInfo automaticStepping = academyType.GetProperty(
                "AutomaticSteppingEnabled", BindingFlags.Instance | BindingFlags.Public);
            bool originalAutomaticStepping = (bool)automaticStepping.GetValue(academy);

            void CaptureUnexpectedErrors(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    unexpectedErrors++;
                }
            }

            Application.logMessageReceived += CaptureUnexpectedErrors;
            try
            {
                automaticStepping.SetValue(academy, false);
                GameLaunchContext.Clear();
                Type entryBootstrap = RuntimeType(
                    "LlamAcademy.Dinos.Training.DinoTrainingEntryBootstrap");
                Assert.That((bool)entryBootstrap.GetMethod(
                        "TryRoute", BindingFlags.Static | BindingFlags.Public)
                    .Invoke(null, new object[] { trainingArguments }), Is.True,
                    "the comparison must enter training through the same command-line route as the executable");
                yield return WaitForScene("Dinos");
                Component scheduler = FindRuntimeComponent(
                    "LlamAcademy.Dinos.Training.DinoTrainingDecisionScheduler");
                Assert.That(scheduler, Is.Not.Null);
                if (scheduler is Behaviour schedulerBehaviour) schedulerBehaviour.enabled = false;
                yield return WaitForRoundState("Running");

                Component trainingAgent = FindRuntimeComponent(
                    "LlamAcademy.Dinos.Training.DinoTrainingAgent");
                Component trainingBuilder = FindRuntimeComponent(
                    "LlamAcademy.Dinos.Training.DinoStructuredObservationBuilder");
                Component trainingSpawner = FindRuntimeComponent(
                    "LlamAcademy.Dinos.Player.DinoSpawner");
                Component trainingRound = FindRuntimeComponent(
                    "LlamAcademy.Dinos.RoundManagement.RoundManager");
                Component trainingRestart = FindRuntimeComponent(
                    "LlamAcademy.Dinos.RoundManagement.GameSessionRestartService");
                Component trainingCollector = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI")
                    .GetComponent(RuntimeType("LlamAcademy.Dinos.Session.GameResultCollector"));
                Assert.That(trainingAgent, Is.Not.Null);
                Assert.That(trainingBuilder, Is.Not.Null);
                Assert.That(trainingSpawner, Is.Not.Null);
                Assert.That(trainingRound, Is.Not.Null);
                Assert.That(trainingCollector, Is.Not.Null);
                Assert.That(((GameLaunchRequest)trainingRestart.GetType()
                    .GetProperty("CurrentLaunchRequest").GetValue(trainingRestart)).Controller,
                    Is.EqualTo(GameControllerMode.TrainingAI));
                Assert.That(((GameLaunchRequest)trainingRestart.GetType()
                    .GetProperty("CurrentLaunchRequest").GetValue(trainingRestart)).LayoutSeed,
                    Is.EqualTo(layoutSeed));

                SetPrivateField(trainingAgent, "SceneReloader", null);
                trainingSpawner.GetType().GetProperty("ResourcesToSpend").SetValue(trainingSpawner, 0);
                DinoStructuredObservationFrame trainingBefore =
                    (DinoStructuredObservationFrame)trainingBuilder.GetType()
                        .GetMethod("BuildFrame").Invoke(trainingBuilder, null);
                InvokeTrainingAction(trainingAgent, rejectedDeployment);
                DinoStructuredObservationFrame trainingAfter =
                    (DinoStructuredObservationFrame)trainingBuilder.GetType()
                        .GetMethod("BuildFrame").Invoke(trainingBuilder, null);
                Assert.That(trainingAgent.GetType().GetProperty("LastActionWasValid")
                    .GetValue(trainingAgent), Is.False);
                Assert.That(trainingAfter.Global[2], Is.EqualTo(0f));
                Assert.That(trainingSpawner.GetType().GetProperty("ResourcesToSpend")
                    .GetValue(trainingSpawner), Is.EqualTo(0));
                Assert.That(trainingRound.GetType().GetProperty("AliveDinos")
                    .GetValue(trainingRound), Is.EqualTo(0));

                List<string> trainingStates = new() { "Running" };
                trainingRound.GetType().GetMethod(
                    "BeginEnding", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(trainingRound, new object[] { false });
                trainingStates.Add(trainingRound.GetType().GetProperty("State")
                    .GetValue(trainingRound).ToString());
                trainingRound.GetType().GetMethod(
                    "FinishSession", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(trainingRound, null);
                trainingStates.Add(trainingRound.GetType().GetProperty("State")
                    .GetValue(trainingRound).ToString());
                Assert.That((bool)trainingCollector.GetType().GetProperty("HasFrozenResult")
                    .GetValue(trainingCollector), Is.False);

                RestoreTrainingArguments(originalArgumentsProvider);
                argumentsRestored = true;
                Time.timeScale = originalTimeScale;
                GameLaunchContext.Clear();
                GameLaunchRequest inferenceRequest = new(
                    GameMapId.OpenTropicalBattlefield,
                    layoutSeed,
                    GameControllerMode.InferenceAI,
                    layoutSeed,
                    modelId,
                    modelHash,
                    GameLaunchRequest.CurrentProtocolVersion,
                    true,
                    true);
                Assert.That(GameLaunchContext.TryPublish(inferenceRequest), Is.True);

                AsyncOperation inferenceLoad = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!inferenceLoad.isDone) yield return null;
                Type inferenceControllerType = RuntimeType(
                    "LlamAcademy.Dinos.Inference.DinoInferenceController");
                Component inferenceController = FindRuntimeComponent(
                    "LlamAcademy.Dinos.Inference.DinoInferenceController");
                if (inferenceController == null)
                {
                    Component host = FindRuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
                    inferenceController = host.gameObject.AddComponent(inferenceControllerType);
                }
                ((Behaviour)inferenceController).enabled = false;

                Component inferenceSpawner = FindRuntimeComponent(
                    "LlamAcademy.Dinos.Player.DinoSpawner");
                Component inferenceRound = FindRuntimeComponent(
                    "LlamAcademy.Dinos.RoundManagement.RoundManager");
                Component inferenceRestart = FindRuntimeComponent(
                    "LlamAcademy.Dinos.RoundManagement.GameSessionRestartService");
                Component inferenceCollector = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI")
                    .GetComponent(RuntimeType("LlamAcademy.Dinos.Session.GameResultCollector"));
                Component inferenceBuilder = FindRuntimeComponent(
                    "LlamAcademy.Dinos.Training.DinoStructuredObservationBuilder");
                Assert.That(((GameLaunchRequest)inferenceRestart.GetType()
                    .GetProperty("CurrentLaunchRequest").GetValue(inferenceRestart)),
                    Is.EqualTo(inferenceRequest));

                Type configType = RuntimeType(
                    "LlamAcademy.Dinos.Inference.DinoInferenceModelConfig");
                config = ScriptableObject.CreateInstance(configType);
                FieldInfo modelField = configType.GetField(
                    "ModelAsset", BindingFlags.Instance | BindingFlags.NonPublic);
                syntheticModel = ScriptableObject.CreateInstance(modelField.FieldType);
                syntheticModel.name = "SyntheticLifecycleEquivalenceModel";
                SetPrivateField(config, "ModelAsset", syntheticModel);
                SetPrivateField(config, "ModelId", modelId);
                SetPrivateField(config, "ModelSha256", modelHash);
                SetPrivateField(config, "ProtocolVersion", DinoInferenceModelContract.V2ProtocolVersion);
                SetPrivateField(config, "ArtifactKind",
                    DinoInferenceModelContract.TrainedCheckpointArtifactKind);
                LifecycleInferenceRunner runner = new(rejectedDeployment);
                MethodInfo configure = inferenceControllerType.GetMethod(
                    "TryConfigure", BindingFlags.Instance | BindingFlags.Public);
                object[] configureArguments = { inferenceRequest, config, runner, null };
                Assert.That((bool)configure.Invoke(inferenceController, configureArguments),
                    Is.True, configureArguments[3] as string);
                inferenceCollector.GetType().GetMethod(
                        "Subscribe", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(inferenceCollector, null);

                inferenceSpawner.GetType().GetProperty("ResourcesToSpend").SetValue(inferenceSpawner, 0);
                DinoStructuredObservationFrame inferenceBefore =
                    (DinoStructuredObservationFrame)inferenceBuilder.GetType()
                        .GetMethod("BuildFrame").Invoke(inferenceBuilder, null);
                LogAssert.Expect(LogType.Warning, new System.Text.RegularExpressions.Regex(
                    "Dino inference rejected 1 decision"));
                MethodInfo processDecision = inferenceControllerType.GetMethod(
                    "TryProcessDecision", BindingFlags.Instance | BindingFlags.Public);
                object[] decisionArguments = { 100, null };
                Assert.That((bool)processDecision.Invoke(inferenceController, decisionArguments), Is.False);
                DinoStructuredObservationFrame inferenceAfter =
                    (DinoStructuredObservationFrame)inferenceBuilder.GetType()
                        .GetMethod("BuildFrame").Invoke(inferenceBuilder, null);
                Assert.That(runner.CallCount, Is.EqualTo(1));
                Assert.That(inferenceAfter.Global[2], Is.EqualTo(0f));
                Assert.That(inferenceSpawner.GetType().GetProperty("ResourcesToSpend")
                    .GetValue(inferenceSpawner), Is.EqualTo(0));
                Assert.That(inferenceRound.GetType().GetProperty("AliveDinos")
                    .GetValue(inferenceRound), Is.EqualTo(0));

                List<string> inferenceStates = new() { "Running" };
                inferenceRound.GetType().GetMethod(
                    "BeginEnding", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(inferenceRound, new object[] { false });
                inferenceStates.Add(inferenceRound.GetType().GetProperty("State")
                    .GetValue(inferenceRound).ToString());
                inferenceRound.GetType().GetMethod(
                    "FinishSession", BindingFlags.Instance | BindingFlags.NonPublic)
                    .Invoke(inferenceRound, null);
                inferenceStates.Add(inferenceRound.GetType().GetProperty("State")
                    .GetValue(inferenceRound).ToString());

                Assert.That(inferenceStates, Is.EqualTo(trainingStates));
                AssertGameplayObservationEquivalent(trainingBefore, inferenceBefore, "before action");
                AssertGameplayObservationEquivalent(trainingAfter, inferenceAfter, "after rejected action");
                Assert.That((bool)inferenceCollector.GetType().GetProperty("HasFrozenResult")
                    .GetValue(inferenceCollector), Is.True,
                    "game inference owns a comparison snapshot while training intentionally does not");
                GameResultSnapshot frozen = (GameResultSnapshot)inferenceCollector.GetType()
                    .GetProperty("FrozenResult").GetValue(inferenceCollector);
                Assert.That(frozen.Controller, Is.EqualTo(GameControllerMode.InferenceAI));
                Assert.That(frozen.LayoutSeed, Is.EqualTo(layoutSeed));
                Assert.That(unexpectedErrors, Is.Zero,
                    "equivalent lifecycle paths must not emit Error/Exception/Assert logs");
            }
            finally
            {
                Application.logMessageReceived -= CaptureUnexpectedErrors;
                if (!argumentsRestored) RestoreTrainingArguments(originalArgumentsProvider);
                automaticStepping.SetValue(academy, originalAutomaticStepping);
                Time.timeScale = originalTimeScale;
                GameLaunchContext.Clear();
                if (syntheticModel != null) UnityEngine.Object.DestroyImmediate(syntheticModel);
                if (config != null) UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [UnityTest]
        public IEnumerator TrainingAndInference_RandomPolicyAcrossBothMapsKeepsLifecycleEquivalent()
        {
            const int environmentIndex = 2;
            const int processGeneration = 3;
            const int scenariosPerMap = 3;
            const int actionsPerScenario = 128;
            const string modelId = "random-lifecycle-matrix-test";
            string modelHash = new('d', 64);
            Type runContextType = RuntimeType(
                "LlamAcademy.Dinos.Training.DinoTrainingRunContext");
            MethodInfo deriveEpisodeSeed = runContextType.GetMethod(
                "DeriveEpisodeSeed", BindingFlags.Static | BindingFlags.Public);
            Type entryBootstrapType = RuntimeType(
                "LlamAcademy.Dinos.Training.DinoTrainingEntryBootstrap");
            FieldInfo routeAttempted = entryBootstrapType.GetField(
                "RouteAttempted", BindingFlags.Static | BindingFlags.NonPublic);
            Type trainingBootstrapType = RuntimeType(
                "LlamAcademy.Dinos.Training.DinoTrainingBootstrap");
            FieldInfo argumentsProvider = trainingBootstrapType.GetField(
                "CommandLineArgumentsProvider", BindingFlags.Static | BindingFlags.NonPublic);
            object originalArgumentsProvider = argumentsProvider.GetValue(null);
            float originalTimeScale = Time.timeScale;
            Type academyType = RuntimeType("Unity.MLAgents.Academy");
            object academy = academyType.GetProperty(
                    "Instance", BindingFlags.Static | BindingFlags.Public)
                .GetValue(null);
            PropertyInfo automaticStepping = academyType.GetProperty(
                "AutomaticSteppingEnabled", BindingFlags.Instance | BindingFlags.Public);
            bool originalAutomaticStepping = (bool)automaticStepping.GetValue(academy);
            ScriptableObject config = null;
            ScriptableObject syntheticModel = null;
            int unexpectedErrors = 0;
            int pairedOutcomeDifferences = 0;
            int pairedDecisionCountDifferences = 0;
            int expectedSnapshotFailures = 0;
            int observedSnapshotErrors = 0;

            void CaptureUnexpectedErrors(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    if (type == LogType.Error && condition.Contains(
                            "Cannot freeze the game result without one valid battlefield layout signature provider."))
                    {
                        observedSnapshotErrors++;
                    }
                    else
                    {
                        unexpectedErrors++;
                    }
                }
            }

            List<(int BaseSeed, int LayoutSeed, GameMapId Map)> mapOneScenarios = new();
            List<(int BaseSeed, int LayoutSeed, GameMapId Map)> mapTwoScenarios = new();
            for (int candidate = 101;
                 mapOneScenarios.Count < scenariosPerMap || mapTwoScenarios.Count < scenariosPerMap;
                 candidate++)
            {
                int layoutSeed = (int)deriveEpisodeSeed.Invoke(
                    null, new object[] { candidate, environmentIndex, 0 });
                bool isMapOne = (unchecked((uint)layoutSeed) & 1u) == 0u;
                List<(int BaseSeed, int LayoutSeed, GameMapId Map)> target =
                    isMapOne ? mapOneScenarios : mapTwoScenarios;
                if (target.Count < scenariosPerMap)
                {
                    target.Add((
                        candidate,
                        layoutSeed,
                        isMapOne
                            ? GameMapId.OpenTropicalBattlefield
                            : GameMapId.LayeredBattlefield));
                }
            }

            List<(int BaseSeed, int LayoutSeed, GameMapId Map)> scenarios = new();
            for (int index = 0; index < scenariosPerMap; index++)
            {
                scenarios.Add(mapOneScenarios[index]);
                scenarios.Add(mapTwoScenarios[index]);
            }

            Application.logMessageReceived += CaptureUnexpectedErrors;
            try
            {
                automaticStepping.SetValue(academy, false);
                Type configType = RuntimeType(
                    "LlamAcademy.Dinos.Inference.DinoInferenceModelConfig");
                config = ScriptableObject.CreateInstance(configType);
                FieldInfo modelField = configType.GetField(
                    "ModelAsset", BindingFlags.Instance | BindingFlags.NonPublic);
                syntheticModel = ScriptableObject.CreateInstance(modelField.FieldType);
                syntheticModel.name = "SyntheticRandomLifecycleMatrixModel";
                SetPrivateField(config, "ModelAsset", syntheticModel);
                SetPrivateField(config, "ModelId", modelId);
                SetPrivateField(config, "ModelSha256", modelHash);
                SetPrivateField(config, "ProtocolVersion", DinoInferenceModelContract.V2ProtocolVersion);
                SetPrivateField(config, "ArtifactKind",
                    DinoInferenceModelContract.TrainedCheckpointArtifactKind);

                for (int scenarioIndex = 0; scenarioIndex < scenarios.Count; scenarioIndex++)
                {
                    (int baseSeed, int layoutSeed, GameMapId map) = scenarios[scenarioIndex];
                    string sceneName = map == GameMapId.OpenTropicalBattlefield
                        ? "Dinos"
                        : "LayeredBattlefield";
                    string scenario = $"scenario={scenarioIndex + 1}/{scenarios.Count}; " +
                                      $"map={map}; base_seed={baseSeed}; layout_seed={layoutSeed}";
                    System.Random random = new(unchecked(layoutSeed ^ 0x5A17C3));
                    List<float[]> actions = new();
                    for (int actionIndex = 0; actionIndex < actionsPerScenario; actionIndex++)
                    {
                        float[] action = new float[4];
                        for (int valueIndex = 0; valueIndex < action.Length; valueIndex++)
                        {
                            action[valueIndex] = (float)(random.NextDouble() * 2.0 - 1.0);
                        }
                        actions.Add(action);
                    }

                    string[] trainingArguments =
                    {
                        "game.exe", "--dino-training",
                        "--dino-base-seed", baseSeed.ToString(CultureInfo.InvariantCulture),
                        "--dino-environment-index", environmentIndex.ToString(CultureInfo.InvariantCulture),
                        "--dino-process-generation", processGeneration.ToString(CultureInfo.InvariantCulture)
                    };
                    argumentsProvider.SetValue(
                        null, new Func<string[]>(() => trainingArguments));
                    routeAttempted.SetValue(null, false);
                    GameLaunchContext.Clear();
                    Assert.That((bool)entryBootstrapType.GetMethod(
                            "TryRoute", BindingFlags.Static | BindingFlags.Public)
                        .Invoke(null, new object[] { trainingArguments }), Is.True, scenario);
                    yield return WaitForScene(sceneName);
                    Component scheduler = FindRuntimeComponent(
                        "LlamAcademy.Dinos.Training.DinoTrainingDecisionScheduler");
                    Assert.That(scheduler, Is.Not.Null, scenario);
                    if (scheduler is Behaviour schedulerBehaviour)
                    {
                        schedulerBehaviour.enabled = false;
                    }
                    yield return WaitForRoundState("Running");

                    Component trainingAgent = FindRuntimeComponent(
                        "LlamAcademy.Dinos.Training.DinoTrainingAgent");
                    Component trainingBuilder = FindRuntimeComponent(
                        "LlamAcademy.Dinos.Training.DinoStructuredObservationBuilder");
                    Component trainingSpawner = FindRuntimeComponent(
                        "LlamAcademy.Dinos.Player.DinoSpawner");
                    Component trainingRound = FindRuntimeComponent(
                        "LlamAcademy.Dinos.RoundManagement.RoundManager");
                    Component trainingRestart = FindRuntimeComponent(
                        "LlamAcademy.Dinos.RoundManagement.GameSessionRestartService");
                    Component trainingCollector = FindRuntimeComponent(
                            "LlamAcademy.Dinos.UI.RuntimeUI")
                        .GetComponent(RuntimeType(
                            "LlamAcademy.Dinos.Session.GameResultCollector"));
                    Assert.That(trainingAgent, Is.Not.Null, scenario);
                    Assert.That(trainingBuilder, Is.Not.Null, scenario);
                    Assert.That(trainingSpawner, Is.Not.Null, scenario);
                    Assert.That(trainingRound, Is.Not.Null, scenario);
                    Assert.That(trainingCollector, Is.Not.Null, scenario);
                    GameLaunchRequest trainingRequest = (GameLaunchRequest)trainingRestart
                        .GetType().GetProperty("CurrentLaunchRequest").GetValue(trainingRestart);
                    Assert.That(trainingRequest.Controller, Is.EqualTo(GameControllerMode.TrainingAI), scenario);
                    Assert.That(trainingRequest.MapId, Is.EqualTo(map), scenario);
                    Assert.That(trainingRequest.LayoutSeed, Is.EqualTo(layoutSeed), scenario);
                    SetPrivateField(trainingAgent, "SceneReloader", null);

                    DinoStructuredObservationFrame trainingInitial =
                        (DinoStructuredObservationFrame)trainingBuilder.GetType()
                            .GetMethod("BuildFrame").Invoke(trainingBuilder, null);
                    List<string> trainingStates = new() { "Running" };
                    int trainingDecisionCount = 0;
                    int trainingValidActions = 0;
                    for (int actionIndex = 0;
                         actionIndex < actions.Count && trainingRound.GetType()
                             .GetProperty("State").GetValue(trainingRound).ToString() == "Running";
                         actionIndex++)
                    {
                        InvokeTrainingAction(trainingAgent, actions[actionIndex]);
                        trainingDecisionCount++;
                        if ((bool)trainingAgent.GetType().GetProperty("LastActionWasValid")
                                .GetValue(trainingAgent))
                        {
                            trainingValidActions++;
                        }
                        for (int fixedStep = 0; fixedStep < 25; fixedStep++)
                        {
                            yield return new WaitForFixedUpdate();
                            AppendLifecycleState(trainingRound, trainingStates);
                            if (trainingStates[^1] != "Running")
                            {
                                break;
                            }
                        }
                    }
                    Assert.That(trainingStates, Does.Contain("Ending"),
                        $"{scenario}; random TrainingAI episode did not reach a natural ending");
                    float trainingEndDeadline = Time.realtimeSinceStartup + 10f;
                    while (trainingStates[^1] != "Ended" &&
                           Time.realtimeSinceStartup < trainingEndDeadline)
                    {
                        yield return null;
                        AppendLifecycleState(trainingRound, trainingStates);
                    }
                    Assert.That(trainingStates[^1], Is.EqualTo("Ended"), scenario);
                    bool playerWon = (bool)trainingRound.GetType().GetProperty("PlayerWon")
                        .GetValue(trainingRound);
                    string trainingOutcome = trainingRound.GetType().GetProperty("OutcomeReason")
                        .GetValue(trainingRound).ToString();
                    int trainingFinalResources = (int)trainingSpawner.GetType()
                        .GetProperty("ResourcesToSpend").GetValue(trainingSpawner);
                    int trainingFinalAliveDinos = (int)trainingRound.GetType()
                        .GetProperty("AliveDinos").GetValue(trainingRound);
                    float trainingElapsed = (float)trainingRound.GetType()
                        .GetProperty("RunningElapsedSeconds").GetValue(trainingRound);
                    Assert.That((bool)trainingCollector.GetType()
                        .GetProperty("HasFrozenResult").GetValue(trainingCollector), Is.False, scenario);

                    argumentsProvider.SetValue(null, originalArgumentsProvider);
                    GameLaunchContext.Clear();
                    GameLaunchRequest inferenceRequest = new(
                        map,
                        layoutSeed,
                        GameControllerMode.InferenceAI,
                        layoutSeed,
                        modelId,
                        modelHash,
                        GameLaunchRequest.CurrentProtocolVersion,
                        true,
                        true);
                    Assert.That(GameLaunchContext.TryPublish(inferenceRequest), Is.True, scenario);
                    AsyncOperation inferenceLoad = SceneManager.LoadSceneAsync(
                        sceneName, LoadSceneMode.Single);
                    while (!inferenceLoad.isDone)
                    {
                        yield return null;
                    }

                    Type inferenceControllerType = RuntimeType(
                        "LlamAcademy.Dinos.Inference.DinoInferenceController");
                    Component inferenceController = FindRuntimeComponent(
                        "LlamAcademy.Dinos.Inference.DinoInferenceController");
                    if (inferenceController == null)
                    {
                        Component host = FindRuntimeComponent(
                            "LlamAcademy.Dinos.Player.DinoSpawner");
                        inferenceController = host.gameObject.AddComponent(inferenceControllerType);
                    }
                    ((Behaviour)inferenceController).enabled = false;

                    Component inferenceSpawner = FindRuntimeComponent(
                        "LlamAcademy.Dinos.Player.DinoSpawner");
                    Component inferenceRound = FindRuntimeComponent(
                        "LlamAcademy.Dinos.RoundManagement.RoundManager");
                    Component inferenceRestart = FindRuntimeComponent(
                        "LlamAcademy.Dinos.RoundManagement.GameSessionRestartService");
                    Component inferenceCollector = FindRuntimeComponent(
                            "LlamAcademy.Dinos.UI.RuntimeUI")
                        .GetComponent(RuntimeType(
                            "LlamAcademy.Dinos.Session.GameResultCollector"));
                    Component inferenceBuilder = FindRuntimeComponent(
                        "LlamAcademy.Dinos.Training.DinoStructuredObservationBuilder");
                    Assert.That((GameLaunchRequest)inferenceRestart.GetType()
                        .GetProperty("CurrentLaunchRequest").GetValue(inferenceRestart),
                        Is.EqualTo(inferenceRequest), scenario);
                    LifecycleInferenceRunner runner = new(actions);
                    MethodInfo configure = inferenceControllerType.GetMethod(
                        "TryConfigure", BindingFlags.Instance | BindingFlags.Public);
                    object[] configureArguments = { inferenceRequest, config, runner, null };
                    Assert.That((bool)configure.Invoke(inferenceController, configureArguments),
                        Is.True, $"{scenario}; {configureArguments[3] as string}");
                    inferenceCollector.GetType().GetMethod(
                            "Subscribe", BindingFlags.Instance | BindingFlags.NonPublic)
                        .Invoke(inferenceCollector, null);

                    DinoStructuredObservationFrame inferenceInitial =
                        (DinoStructuredObservationFrame)inferenceBuilder.GetType()
                            .GetMethod("BuildFrame").Invoke(inferenceBuilder, null);
                    AssertGameplayObservationEquivalent(
                        trainingInitial, inferenceInitial, $"{scenario}; initial");
                    MethodInfo processDecision = inferenceControllerType.GetMethod(
                        "TryProcessDecision", BindingFlags.Instance | BindingFlags.Public);
                    List<string> inferenceStates = new() { "Running" };
                    int inferenceDecisionCount = 0;
                    int inferenceValidActions = 0;
                    for (int actionIndex = 0;
                         actionIndex < actions.Count && inferenceStates[^1] == "Running";
                         actionIndex++)
                    {
                        object[] decisionArguments = { 100 + actionIndex * 25, null };
                        bool actualValidity = (bool)processDecision.Invoke(
                            inferenceController, decisionArguments);
                        inferenceDecisionCount++;
                        Assert.That(runner.CallCount, Is.EqualTo(inferenceDecisionCount),
                            $"{scenario}; decision {inferenceDecisionCount} was not due");
                        if (actualValidity)
                        {
                            inferenceValidActions++;
                        }
                        for (int fixedStep = 0; fixedStep < 25; fixedStep++)
                        {
                            yield return new WaitForFixedUpdate();
                            AppendLifecycleState(inferenceRound, inferenceStates);
                            if (inferenceStates[^1] != "Running")
                            {
                                break;
                            }
                        }
                    }
                    Assert.That(runner.CallCount, Is.EqualTo(inferenceDecisionCount), scenario);
                    Assert.That(inferenceStates, Does.Contain("Ending"),
                        $"{scenario}; random InferenceAI episode did not reach a natural ending " +
                        $"after {inferenceDecisionCount} decisions");
                    string providerTypeName = map == GameMapId.OpenTropicalBattlefield
                        ? "LlamAcademy.Dinos.Map.Adapters.OpenTropicalBattlefieldLayoutSignatureProvider"
                        : "LlamAcademy.Dinos.Map.Adapters.LayeredBattlefieldLayoutController";
                    Component layoutProvider = FindRuntimeComponent(providerTypeName);
                    Assert.That(layoutProvider, Is.Not.Null, scenario);
                    object[] captureArguments = { null };
                    bool layoutCanBeCaptured = (bool)layoutProvider.GetType()
                        .GetMethod("TryCapture", BindingFlags.Instance | BindingFlags.Public)
                        .Invoke(layoutProvider, captureArguments);
                    if (!layoutCanBeCaptured)
                    {
                        expectedSnapshotFailures++;
                        LogAssert.Expect(
                            LogType.Error,
                            "Cannot freeze the game result without one valid battlefield layout signature provider.");
                    }
                    float inferenceEndDeadline = Time.realtimeSinceStartup + 10f;
                    while (inferenceStates[^1] != "Ended" &&
                           Time.realtimeSinceStartup < inferenceEndDeadline)
                    {
                        yield return null;
                        AppendLifecycleState(inferenceRound, inferenceStates);
                    }
                    Assert.That(inferenceStates[^1], Is.EqualTo("Ended"), scenario);
                    Assert.That(inferenceStates, Is.EqualTo(trainingStates), scenario);
                    string inferenceOutcome = inferenceRound.GetType()
                        .GetProperty("OutcomeReason").GetValue(inferenceRound).ToString();
                    bool inferenceWon = (bool)inferenceRound.GetType().GetProperty("PlayerWon")
                        .GetValue(inferenceRound);
                    int inferenceFinalResources = (int)inferenceSpawner.GetType()
                        .GetProperty("ResourcesToSpend").GetValue(inferenceSpawner);
                    int inferenceFinalAliveDinos = (int)inferenceRound.GetType()
                        .GetProperty("AliveDinos").GetValue(inferenceRound);
                    float inferenceElapsed = (float)inferenceRound.GetType()
                        .GetProperty("RunningElapsedSeconds").GetValue(inferenceRound);
                    Assert.That(trainingOutcome, Is.Not.EqualTo("None"), scenario);
                    Assert.That(trainingOutcome, Does.Not.StartWith("Manual"), scenario);
                    Assert.That(inferenceOutcome, Is.Not.EqualTo("None"), scenario);
                    Assert.That(inferenceOutcome, Does.Not.StartWith("Manual"), scenario);
                    if (trainingOutcome != inferenceOutcome || playerWon != inferenceWon)
                    {
                        pairedOutcomeDifferences++;
                    }
                    if (trainingDecisionCount != inferenceDecisionCount)
                    {
                        pairedDecisionCountDifferences++;
                    }
                    bool hasFrozenResult = (bool)inferenceCollector.GetType()
                        .GetProperty("HasFrozenResult").GetValue(inferenceCollector);
                    Assert.That(hasFrozenResult, Is.EqualTo(layoutCanBeCaptured), scenario);
                    if (hasFrozenResult)
                    {
                        GameResultSnapshot frozen = (GameResultSnapshot)inferenceCollector.GetType()
                            .GetProperty("FrozenResult").GetValue(inferenceCollector);
                        Assert.That(frozen.Controller, Is.EqualTo(GameControllerMode.InferenceAI), scenario);
                        Assert.That(frozen.MapId, Is.EqualTo(map), scenario);
                        Assert.That(frozen.LayoutSeed, Is.EqualTo(layoutSeed), scenario);
                    }
                    Debug.Log(
                        "DINO_LIFECYCLE_MATRIX " +
                        $"scenario={scenarioIndex + 1}; map={map}; layout_seed={layoutSeed}; " +
                        $"training_outcome={trainingOutcome}; training_won={playerWon}; " +
                        $"training_decisions={trainingDecisionCount}; " +
                        $"training_valid={trainingValidActions}; " +
                        $"training_invalid={trainingDecisionCount - trainingValidActions}; " +
                        $"training_elapsed={trainingElapsed.ToString("R", CultureInfo.InvariantCulture)}; " +
                        $"training_food={trainingFinalResources}; " +
                        $"training_alive={trainingFinalAliveDinos}; " +
                        $"inference_outcome={inferenceOutcome}; inference_won={inferenceWon}; " +
                        $"inference_decisions={inferenceDecisionCount}; " +
                        $"inference_valid={inferenceValidActions}; " +
                        $"inference_invalid={inferenceDecisionCount - inferenceValidActions}; " +
                        $"inference_elapsed={inferenceElapsed.ToString("R", CultureInfo.InvariantCulture)}; " +
                        $"inference_food={inferenceFinalResources}; " +
                        $"inference_alive={inferenceFinalAliveDinos}; " +
                        $"inference_snapshot_frozen={hasFrozenResult}");
                }

                Assert.That(unexpectedErrors, Is.Zero,
                    "random-policy lifecycle matrix emitted Error/Exception/Assert logs");
                Assert.That(observedSnapshotErrors, Is.EqualTo(expectedSnapshotFailures),
                    "layout signature preflight and GameResultCollector errors must agree");
                Debug.Log(
                    "DINO_LIFECYCLE_MATRIX_SUMMARY " +
                    $"scenarios={scenarios.Count}; decisions_per_policy_max={actionsPerScenario}; " +
                    $"paired_outcome_differences={pairedOutcomeDifferences}; " +
                    $"paired_decision_count_differences={pairedDecisionCountDifferences}; " +
                    $"inference_snapshot_failures={expectedSnapshotFailures}");
                Assert.That(expectedSnapshotFailures, Is.Zero,
                    $"InferenceAI result lifecycle failed to freeze {expectedSnapshotFailures}/" +
                    $"{scenarios.Count} natural random-policy episodes");
            }
            finally
            {
                Application.logMessageReceived -= CaptureUnexpectedErrors;
                argumentsProvider.SetValue(null, originalArgumentsProvider);
                routeAttempted.SetValue(null, false);
                automaticStepping.SetValue(academy, originalAutomaticStepping);
                Time.timeScale = originalTimeScale;
                GameLaunchContext.Clear();
                if (syntheticModel != null)
                {
                    UnityEngine.Object.DestroyImmediate(syntheticModel);
                }
                if (config != null)
                {
                    UnityEngine.Object.DestroyImmediate(config);
                }
            }
        }

        [UnityTest]
        public IEnumerator FrozenStep131189InferenceAI_ReportsWinRateOnTrainingEvaluationSeedStream()
        {
            const int episodesPerMap = 20;
            const int evaluationSeed = 101;
            const int environmentIndex = 0;
            const int decisionsPerEpisodeLimit = 128;
            const string modelAssetPath =
                "Assets/Tests/Fixtures/PPOSeed0Step131189FrozenEval.onnx";
            const string modelId = "ppo-seed0-step131189-frozen-eval";
            const string modelHash =
                "e68c13f3a5156df6c8e9fd2a011293a282e859e08e0f138d6968ce143fd8c572";

            float originalTimeScale = Time.timeScale;
            ScriptableObject config = null;
            IDisposable runner = null;
            int unexpectedErrors = 0;

            void CaptureUnexpectedErrors(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                {
                    unexpectedErrors++;
                }
            }

            Type runContextType = RuntimeType(
                "LlamAcademy.Dinos.Training.DinoTrainingRunContext");
            MethodInfo deriveEpisodeSeed = runContextType.GetMethod(
                "DeriveEpisodeSeed", BindingFlags.Static | BindingFlags.Public);
            List<(int EpisodeIndex, int LayoutSeed, GameMapId Map)> mapOne = new();
            List<(int EpisodeIndex, int LayoutSeed, GameMapId Map)> mapTwo = new();
            for (int episodeIndex = 0;
                 mapOne.Count < episodesPerMap || mapTwo.Count < episodesPerMap;
                 episodeIndex++)
            {
                int layoutSeed = (int)deriveEpisodeSeed.Invoke(
                    null, new object[] { evaluationSeed, environmentIndex, episodeIndex });
                bool isMapOne = (unchecked((uint)layoutSeed) & 1u) == 0u;
                List<(int EpisodeIndex, int LayoutSeed, GameMapId Map)> target =
                    isMapOne ? mapOne : mapTwo;
                if (target.Count < episodesPerMap)
                {
                    target.Add((
                        episodeIndex,
                        layoutSeed,
                        isMapOne
                            ? GameMapId.OpenTropicalBattlefield
                            : GameMapId.LayeredBattlefield));
                }
            }

            List<(int EpisodeIndex, int LayoutSeed, GameMapId Map)> scenarios = new();
            for (int index = 0; index < episodesPerMap; index++)
            {
                scenarios.Add(mapOne[index]);
                scenarios.Add(mapTwo[index]);
            }

            Application.logMessageReceived += CaptureUnexpectedErrors;
            try
            {
                Time.timeScale = 100f;
                Type configType = RuntimeType(
                    "LlamAcademy.Dinos.Inference.DinoInferenceModelConfig");
                config = ScriptableObject.CreateInstance(configType);
                FieldInfo modelField = configType.GetField(
                    "ModelAsset", BindingFlags.Instance | BindingFlags.NonPublic);
                Type assetDatabaseType = Type.GetType("UnityEditor.AssetDatabase, UnityEditor");
                Assert.That(assetDatabaseType, Is.Not.Null,
                    "The diagnostic requires the Unity Editor asset database.");
                MethodInfo loadAssetAtPath = assetDatabaseType.GetMethod(
                    "LoadAssetAtPath",
                    BindingFlags.Static | BindingFlags.Public,
                    null,
                    new[] { typeof(string), typeof(Type) },
                    null);
                UnityEngine.Object modelAsset = (UnityEngine.Object)loadAssetAtPath.Invoke(
                    null, new object[] { modelAssetPath, modelField.FieldType });
                Assert.That(modelAsset, Is.Not.Null,
                    $"Sentis did not import the frozen checkpoint at {modelAssetPath}.");
                SetPrivateField(config, "ModelAsset", modelAsset);
                SetPrivateField(config, "ModelId", modelId);
                SetPrivateField(config, "ModelSha256", modelHash);
                SetPrivateField(config, "ProtocolVersion",
                    DinoInferenceModelContract.V2ProtocolVersion);
                SetPrivateField(config, "ArtifactKind",
                    DinoInferenceModelContract.TrainedCheckpointArtifactKind);
                SetPrivateField(config, "DefaultInferenceSeed", evaluationSeed);

                Type runnerType = RuntimeType(
                    "LlamAcademy.Dinos.Inference.DinoSentisPolicyRunner");
                MethodInfo createRunner = runnerType.GetMethod(
                    "TryCreate", BindingFlags.Static | BindingFlags.Public);
                object[] createArguments = { config, null, null };
                Assert.That((bool)createRunner.Invoke(null, createArguments), Is.True,
                    createArguments[2] as string);
                runner = (IDisposable)createArguments[1];

                Dictionary<GameMapId, int> wins = new()
                {
                    [GameMapId.OpenTropicalBattlefield] = 0,
                    [GameMapId.LayeredBattlefield] = 0
                };
                Dictionary<GameMapId, float> elapsedTotals = new()
                {
                    [GameMapId.OpenTropicalBattlefield] = 0f,
                    [GameMapId.LayeredBattlefield] = 0f
                };

                for (int scenarioIndex = 0; scenarioIndex < scenarios.Count; scenarioIndex++)
                {
                    (int episodeIndex, int layoutSeed, GameMapId map) = scenarios[scenarioIndex];
                    string scenario =
                        $"scenario={scenarioIndex + 1}/{scenarios.Count}; map={map}; " +
                        $"episode_index={episodeIndex}; layout_seed={layoutSeed}";
                    GameLaunchContext.Clear();
                    GameLaunchRequest request = new(
                        map,
                        layoutSeed,
                        GameControllerMode.InferenceAI,
                        evaluationSeed,
                        modelId,
                        modelHash,
                        GameLaunchRequest.CurrentProtocolVersion,
                        true,
                        true);
                    Assert.That(GameLaunchContext.TryPublish(request), Is.True, scenario);

                    AsyncOperation load = SceneManager.LoadSceneAsync(
                        map.SceneName, LoadSceneMode.Single);
                    while (!load.isDone)
                    {
                        yield return null;
                    }

                    Type controllerType = RuntimeType(
                        "LlamAcademy.Dinos.Inference.DinoInferenceController");
                    Component controller = FindRuntimeComponent(
                        "LlamAcademy.Dinos.Inference.DinoInferenceController");
                    if (controller == null)
                    {
                        Component host = FindRuntimeComponent(
                            "LlamAcademy.Dinos.Player.DinoSpawner");
                        controller = host.gameObject.AddComponent(controllerType);
                    }
                    ((Behaviour)controller).enabled = false;

                    MethodInfo configure = controllerType.GetMethod(
                        "TryConfigure", BindingFlags.Instance | BindingFlags.Public);
                    object[] configureArguments = { request, config, runner, null };
                    Assert.That((bool)configure.Invoke(controller, configureArguments), Is.True,
                        $"{scenario}; {configureArguments[3] as string}");

                    Component round = FindRuntimeComponent(
                        "LlamAcademy.Dinos.RoundManagement.RoundManager");
                    Assert.That(round, Is.Not.Null, scenario);
                    PropertyInfo state = round.GetType().GetProperty("State");
                    MethodInfo processDecision = controllerType.GetMethod(
                        "TryProcessDecision", BindingFlags.Instance | BindingFlags.Public);
                    int decisions = 0;
                    while (state.GetValue(round).ToString() == "Running" &&
                           decisions < decisionsPerEpisodeLimit)
                    {
                        object[] decisionArguments = { 100 + decisions * 25, null };
                        processDecision.Invoke(controller, decisionArguments);
                        decisions++;
                        for (int fixedStep = 0; fixedStep < 25; fixedStep++)
                        {
                            yield return new WaitForFixedUpdate();
                            if (state.GetValue(round).ToString() != "Running")
                            {
                                break;
                            }
                        }
                    }

                    Assert.That(state.GetValue(round).ToString(), Is.EqualTo("Ending"),
                        $"{scenario}; deterministic Sentis policy did not reach a natural terminal " +
                        $"within {decisions} decisions");
                    bool won = (bool)round.GetType().GetProperty("PlayerWon").GetValue(round);
                    string outcome = round.GetType().GetProperty("OutcomeReason")
                        .GetValue(round).ToString();
                    float elapsed = (float)round.GetType().GetProperty("RunningElapsedSeconds")
                        .GetValue(round);
                    if (won)
                    {
                        wins[map]++;
                    }
                    elapsedTotals[map] += elapsed;
                    Debug.Log(
                        "DINO_FROZEN_INFERENCE_EVAL " +
                        $"map={map}; episode_index={episodeIndex}; layout_seed={layoutSeed}; " +
                        $"won={won}; outcome={outcome}; decisions={decisions}; " +
                        $"elapsed={elapsed.ToString("R", CultureInfo.InvariantCulture)}");

                    // The win-rate diagnostic ends at the natural terminal. Avoid advancing into
                    // the known, independent map-one result-snapshot defect while loading the next scene.
                    if (round is Behaviour roundBehaviour)
                    {
                        roundBehaviour.enabled = false;
                    }
                }

                int mapOneWins = wins[GameMapId.OpenTropicalBattlefield];
                int mapTwoWins = wins[GameMapId.LayeredBattlefield];
                Debug.Log(
                    "DINO_FROZEN_INFERENCE_EVAL_SUMMARY " +
                    $"model_id={modelId}; action_mode=deterministic; evaluation_seed={evaluationSeed}; " +
                    $"map1_wins={mapOneWins}; map1_episodes={episodesPerMap}; " +
                    $"map1_win_rate={(mapOneWins / (float)episodesPerMap).ToString("R", CultureInfo.InvariantCulture)}; " +
                    $"map1_mean_elapsed={(elapsedTotals[GameMapId.OpenTropicalBattlefield] / episodesPerMap).ToString("R", CultureInfo.InvariantCulture)}; " +
                    $"map2_wins={mapTwoWins}; map2_episodes={episodesPerMap}; " +
                    $"map2_win_rate={(mapTwoWins / (float)episodesPerMap).ToString("R", CultureInfo.InvariantCulture)}; " +
                    $"map2_mean_elapsed={(elapsedTotals[GameMapId.LayeredBattlefield] / episodesPerMap).ToString("R", CultureInfo.InvariantCulture)}; " +
                    $"overall_wins={mapOneWins + mapTwoWins}; overall_episodes={scenarios.Count}; " +
                    $"overall_win_rate={((mapOneWins + mapTwoWins) / (float)scenarios.Count).ToString("R", CultureInfo.InvariantCulture)}");
                Assert.That(unexpectedErrors, Is.Zero,
                    "the frozen Sentis inference evaluation emitted Error/Exception/Assert logs");
            }
            finally
            {
                Application.logMessageReceived -= CaptureUnexpectedErrors;
                runner?.Dispose();
                Time.timeScale = originalTimeScale;
                GameLaunchContext.Clear();
                if (config != null)
                {
                    UnityEngine.Object.DestroyImmediate(config);
                }
            }
        }

        [UnityTest]
        public IEnumerator GameResultCollector_PairsSameLayoutAndFreezesRepeatedEndedOnce()
        {
            const int layoutSeed = 2718;
            const string layout = "open_tropical_battlefield|2718|synthetic-layout";
            string modelHash = new('a', 64);
            GameResultSnapshot player = new(
                GameMapId.OpenTropicalBattlefield,
                GameMapId.OpenTropicalBattlefield.SceneStableId,
                layoutSeed,
                GameControllerMode.Human,
                true,
                16f,
                225,
                12.25f,
                layout,
                string.Empty,
                string.Empty,
                GameLaunchRequest.CurrentProtocolVersion);
            GameResultSnapshot ai = new(
                GameMapId.OpenTropicalBattlefield,
                GameMapId.OpenTropicalBattlefield.SceneStableId,
                layoutSeed,
                GameControllerMode.InferenceAI,
                false,
                40f,
                25,
                -0.93f,
                layout,
                "default-v2",
                modelHash,
                GameLaunchRequest.CurrentProtocolVersion);
            GameLaunchRequest request = GameLaunchContext.DeriveInferenceReplay(
                GameLaunchRequest.CreateHuman(GameMapId.OpenTropicalBattlefield, layoutSeed),
                7,
                "default-v2",
                modelHash,
                GameLaunchRequest.CurrentProtocolVersion);

            GameObject owner = new("GameResultCollector.Synthetic.Open");
            Component collector = owner.AddComponent(RuntimeType(
                "LlamAcademy.Dinos.Session.GameResultCollector"));
            try
            {
                collector.GetType().GetMethod("Configure").Invoke(
                    collector,
                    new object[] { request, (GameResultSnapshot?)player });
                MethodInfo freeze = collector.GetType().GetMethod("TryFreeze");

                Assert.That((bool)freeze.Invoke(collector, new object[] { ai }), Is.True);
                Assert.That((bool)freeze.Invoke(collector, new object[] { ai }), Is.False,
                    "Repeated Ended must not replace or republish the frozen result.");
                Assert.That(
                    (bool)collector.GetType().GetProperty("HasFrozenResult").GetValue(collector),
                    Is.True);
                Assert.That(
                    (GameResultPairingFailure)collector.GetType().GetProperty("PairingFailure").GetValue(collector),
                    Is.EqualTo(GameResultPairingFailure.None));
                GameResultComparison? comparison = (GameResultComparison?)collector.GetType()
                    .GetProperty("Comparison").GetValue(collector);
                Assert.That(comparison.HasValue, Is.True);
                Assert.That(comparison.Value.Human, Is.EqualTo(player));
                Assert.That(comparison.Value.InferenceAi, Is.EqualTo(ai));
            }
            finally
            {
                UnityEngine.Object.Destroy(owner);
            }

            yield return null;
        }

        [UnityTest]
        public IEnumerator EndedMapOne_ResultAndMapSelectionButtonsReceiveRealMouseClicks()
        {
            InputTestFixture input = new();
            input.Setup();
            InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
                for (int frame = 0; frame < 3; frame++) yield return null;

                Component runtimeUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
                UIDocument document = runtimeUi.GetComponent<UIDocument>();
                Component controller = runtimeUi.GetComponent(
                    RuntimeType("LlamAcademy.Dinos.UI.GameResultActionController"));
                Button liveSelectMapButton = document.rootVisualElement.Q<Button>("select-map-button");
                Button boundSelectMapButton = (Button)controller.GetType()
                    .GetField("SelectMapButton", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(controller);

                Assert.That(boundSelectMapButton, Is.SameAs(liveSelectMapButton),
                    "Result actions must bind the buttons from the live UIDocument tree, not the tree from Awake.");

                Component roundManager = FindRuntimeComponent(
                    "LlamAcademy.Dinos.RoundManagement.RoundManager");
                Button liveStartButton = document.rootVisualElement.Q<Button>("start-button");
                Button boundStartButton = (Button)runtimeUi.GetType()
                    .GetField("RegisteredStartButton", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(runtimeUi);
                Assert.That(boundStartButton, Is.SameAs(liveStartButton),
                    "RuntimeUI must bind Start to the live UIDocument tree after its startup rebuild.");
                yield return ClickUiButton(input, mouse, liveStartButton);
                float runningDeadline = Time.realtimeSinceStartup + 3f;
                while (roundManager.GetType().GetProperty("State").GetValue(roundManager).ToString() != "Running" &&
                       Time.realtimeSinceStartup < runningDeadline)
                {
                    yield return null;
                }
                Assert.That(roundManager.GetType().GetProperty("State").GetValue(roundManager).ToString(),
                    Is.EqualTo("Running"), "the live Start button must accept a real mouse click");
                InvokePrivate(roundManager, "BeginEnding", false);
                float endDeadline = Time.realtimeSinceStartup + 4f;
                while (roundManager.GetType().GetProperty("State").GetValue(roundManager).ToString() != "Ended" &&
                       Time.realtimeSinceStartup < endDeadline)
                {
                    yield return null;
                }

                Assert.That(roundManager.GetType().GetProperty("State").GetValue(roundManager).ToString(),
                    Is.EqualTo("Ended"));
                Assert.That(liveSelectMapButton.enabledSelf, Is.True);
                yield return null;

                Vector2 panelCenter = liveSelectMapButton.worldBound.center;
                Rect panelBounds = document.rootVisualElement.worldBound;
                Vector2 screenCenter = new(
                    panelCenter.x * Screen.width / panelBounds.width,
                    Screen.height - panelCenter.y * Screen.height / panelBounds.height);
                input.Set(mouse.position, screenCenter);
                yield return null;
                input.Press(mouse.leftButton);
                yield return null;
                input.Release(mouse.leftButton);

                float deadline = Time.realtimeSinceStartup + 3f;
                while (SceneManager.GetActiveScene().name != "MapSelect" &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MapSelect"),
                    $"screen={Screen.width}x{Screen.height}, root={panelBounds}, " +
                    $"button={liveSelectMapButton.worldBound}, pointer={screenCenter}");

                yield return null;
                Component mapSelectUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.MapSelectUI");
                UnityEngine.UI.Button mapOneButton = (UnityEngine.UI.Button)mapSelectUi.GetType()
                    .GetField("OpenTropicalButton", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(mapSelectUi);
                Vector2 mapOneCenter = RectTransformUtility.WorldToScreenPoint(
                    null,
                    mapOneButton.GetComponent<RectTransform>().position);
                input.Set(mouse.position, mapOneCenter);
                yield return null;
                input.Press(mouse.leftButton);
                yield return null;
                input.Release(mouse.leftButton);

                deadline = Time.realtimeSinceStartup + 3f;
                while (SceneManager.GetActiveScene().name != "Dinos" &&
                       Time.realtimeSinceStartup < deadline)
                {
                    yield return null;
                }

                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("Dinos"),
                    $"Map one at {mapOneCenter} must launch directly from MapSelect.");
                for (int frame = 0; frame < 3; frame++) yield return null;

                Component restartService = FindRuntimeComponent(
                    "LlamAcademy.Dinos.RoundManagement.GameSessionRestartService");
                int firstSeed = ((GameLaunchRequest)restartService.GetType()
                    .GetProperty("CurrentLaunchRequest").GetValue(restartService)).LayoutSeed;
                yield return EndCurrentMapOneSession();
                int previousSceneHandle = SceneManager.GetActiveScene().handle;
                yield return ClickResultButton(input, mouse, "restart-button");
                yield return WaitForReloadedScene("Dinos", previousSceneHandle);
                restartService = FindRuntimeComponent(
                    "LlamAcademy.Dinos.RoundManagement.GameSessionRestartService");
                Assert.That(((GameLaunchRequest)restartService.GetType()
                    .GetProperty("CurrentLaunchRequest").GetValue(restartService)).LayoutSeed, Is.EqualTo(firstSeed));

                yield return EndCurrentMapOneSession();
                previousSceneHandle = SceneManager.GetActiveScene().handle;
                yield return ClickResultButton(input, mouse, "new-layout-button");
                yield return WaitForReloadedScene("Dinos", previousSceneHandle);
                restartService = FindRuntimeComponent(
                    "LlamAcademy.Dinos.RoundManagement.GameSessionRestartService");
                int secondSeed = ((GameLaunchRequest)restartService.GetType()
                    .GetProperty("CurrentLaunchRequest").GetValue(restartService)).LayoutSeed;
                Assert.That(secondSeed, Is.Not.EqualTo(firstSeed));

                yield return EndCurrentMapOneSession();
                UIDocument beforeSelection = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI")
                    .GetComponent<UIDocument>();
                Button observationAi = beforeSelection.rootVisualElement.Q<Button>("ai-strategy-button");
                Assert.That(observationAi.enabledSelf, Is.True,
                    "The approved observation-only PPO policy must enable AI Strategy.");
                Assert.That(beforeSelection.rootVisualElement.Q<Button>("restart-button").enabledSelf, Is.True);
                Assert.That(beforeSelection.rootVisualElement.Q<Button>("new-layout-button").enabledSelf, Is.True);
                Assert.That(beforeSelection.rootVisualElement.Q<Button>("select-map-button").enabledSelf, Is.True);

                yield return ClickResultButton(input, mouse, "select-map-button");
                yield return WaitForScene("MapSelect");
                yield return ClickMapOneButton(input, mouse);
                yield return WaitForScene("Dinos");

                yield return EndCurrentMapOneSession();
                yield return ClickResultButton(input, mouse, "select-map-button");
                yield return WaitForScene("MapSelect");
            }
            finally
            {
                input.TearDown();
                GameLaunchContext.Clear();
            }
        }

        [UnityTest]
        public IEnumerator CompactTunedAiStrategy_ReplaysSameLayoutAndTakesControl()
        {
            InputTestFixture input = new();
            input.Setup();
            Mouse mouse = InputSystem.AddDevice<Mouse>();
            InputSystem.AddDevice<Keyboard>();
            GameLaunchContext.Clear();
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("MapSelect", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
                for (int frame = 0; frame < 3; frame++) yield return null;
                Component mapSelectUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.MapSelectUI");
                UnityEngine.UI.Button mapOneButton = (UnityEngine.UI.Button)mapSelectUi.GetType()
                    .GetField("OpenTropicalButton", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(mapSelectUi);
                Assert.That(mapOneButton, Is.Not.Null);
                Assert.That(mapOneButton.interactable, Is.True);
                mapOneButton.onClick.Invoke();
                yield return WaitForScene("Dinos");

                Component restartService = FindRuntimeComponent(
                    "LlamAcademy.Dinos.RoundManagement.GameSessionRestartService");
                int layoutSeed = ((GameLaunchRequest)restartService.GetType()
                    .GetProperty("CurrentLaunchRequest").GetValue(restartService)).LayoutSeed;
                yield return EndCurrentMapOneSession();
                UIDocument document = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI")
                    .GetComponent<UIDocument>();
                Assert.That(document.rootVisualElement.Q<Button>("ai-strategy-button").enabledSelf, Is.True);
                for (int frame = 0; frame < 3; frame++) yield return null;
                Button aiStrategy = document.rootVisualElement.Q<Button>("ai-strategy-button");
                Assert.That(aiStrategy.worldBound.width, Is.GreaterThan(0f));
                Assert.That(aiStrategy.worldBound.height, Is.GreaterThan(0f));

                int previousSceneHandle = SceneManager.GetActiveScene().handle;
                Submit(aiStrategy);
                // Let UI Toolkit resolve the newly visible strategy panel before reading
                // and submitting the selected strategy.
                for (int frame = 0; frame < 3; frame++) yield return null;
                VisualElement strategyPanel = document.rootVisualElement.Q("strategy-panel");
                Button ppoStrategy = document.rootVisualElement.Q<Button>("ppo-strategy-button");
                Assert.That(strategyPanel.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(ppoStrategy.worldBound.width, Is.GreaterThan(0f));
                Assert.That(ppoStrategy.worldBound.height, Is.GreaterThan(0f));
                Submit(ppoStrategy);
                yield return WaitForReloadedScene("Dinos", previousSceneHandle);
                restartService = FindRuntimeComponent(
                    "LlamAcademy.Dinos.RoundManagement.GameSessionRestartService");
                GameLaunchRequest observationRequest = (GameLaunchRequest)restartService.GetType()
                    .GetProperty("CurrentLaunchRequest").GetValue(restartService);
                Assert.That(observationRequest.Controller, Is.EqualTo(GameControllerMode.InferenceAI));
                Assert.That(observationRequest.LayoutSeed, Is.EqualTo(layoutSeed));
                Assert.That(observationRequest.ModelId,
                    Is.EqualTo("ppo-compact-postfix-tuned-seed0-1574283"));
                Type configType = Type.GetType(
                    "LlamAcademy.Dinos.Inference.DinoInferenceModelConfig, Assembly-CSharp");
                Assert.That(configType, Is.Not.Null);
                ScriptableObject defaultModel = configType
                    .GetMethod("LoadDefault", BindingFlags.Public | BindingFlags.Static)
                    ?.Invoke(null, null) as ScriptableObject;
                Assert.That(defaultModel, Is.Not.Null);
                string defaultModelHash = configType.GetProperty("ModelHash")
                    ?.GetValue(defaultModel) as string;
                Assert.That(observationRequest.ModelHash,
                    Is.EqualTo(defaultModelHash));
                for (int frame = 0; frame < 5; frame++) yield return null;

                Component observationSpawner = FindRuntimeComponent(
                    "LlamAcademy.Dinos.Player.DinoSpawner");
                Assert.That((bool)observationSpawner.GetType().GetProperty("PlayerInputEnabled")
                    .GetValue(observationSpawner), Is.False);
                Component roundManager = FindRuntimeComponent(
                    "LlamAcademy.Dinos.RoundManagement.RoundManager");
                Assert.That(roundManager.GetType().GetProperty("State").GetValue(roundManager).ToString(),
                    Is.EqualTo("Running"));
                Type defenderType = RuntimeType("LlamAcademy.Dinos.Enemy.Defender");
                Type graphAgentType = RuntimeType("Unity.Behavior.BehaviorGraphAgent");
                Type attackRadiusType = RuntimeType("LlamAcademy.Dinos.Unit.AttackRadius");
                Component[] defenders = UnityEngine.Object.FindObjectsByType<Component>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(component => component != null && component.GetType() == defenderType)
                    .ToArray();
                Assert.That(defenders, Has.Length.EqualTo(8),
                    "AI Strategy replay on Dinos must retain three fixed and five ground guards");
                foreach (Component defender in defenders)
                {
                    Assert.That(defenderType.GetProperty("IsDead")?.GetValue(defender), Is.False);
                    Assert.That(((Behaviour)defender.GetComponent(graphAgentType)).enabled, Is.True,
                        $"AI Strategy replay disabled {defender.name}'s Enemy Graph");
                    Component attackRadius = defender.GetComponentInChildren(attackRadiusType, true);
                    Assert.That(attackRadius, Is.InstanceOf<Behaviour>());
                    Assert.That(((Behaviour)attackRadius).enabled, Is.True);
                    Assert.That(attackRadius.GetComponent<Collider>()?.enabled, Is.True);
                }
            }
            finally
            {
                input.TearDown();
                GameLaunchContext.Clear();
            }
        }

        [UnityTest]
        public IEnumerator ResultPanelRoutesEveryButtonAndSuppressesPendingDuplicates()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 3; frame++) yield return null;

            Component runtimeUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
            UIDocument document = runtimeUi.GetComponent<UIDocument>();
            Component controller = runtimeUi.GetComponent(
                RuntimeType("LlamAcademy.Dinos.UI.GameResultActionController"));
            Assert.That(controller, Is.Not.Null);
            Component collector = runtimeUi.GetComponent(
                RuntimeType("LlamAcademy.Dinos.Session.GameResultCollector"));
            Component comparisonPresenter = runtimeUi.GetComponent(
                RuntimeType("LlamAcademy.Dinos.UI.GameComparisonPresenter"));
            Assert.That(collector, Is.Not.Null,
                "RuntimeUI must own the result capture bridge without a scene edit.");
            Assert.That(comparisonPresenter, Is.Not.Null,
                "RuntimeUI must own the comparison presenter without a scene edit.");

            GameLaunchRequest current = GameLaunchRequest.CreateHuman(
                GameMapId.OpenTropicalBattlefield,
                1234);
            GameInferenceLaunchOption inference = GameInferenceLaunchOption.Available(
                5678,
                "default-v2",
                new string('c', 64),
                GameLaunchRequest.CurrentProtocolVersion);
            MethodInfo configure = controller.GetType().GetMethod(
                "Configure", BindingFlags.Instance | BindingFlags.Public);
            configure.Invoke(controller, new object[] { document.rootVisualElement, current, null, inference, null });
            collector.GetType().GetMethod("Configure").Invoke(
                collector,
                new object[] { current, null });
            GameResultSnapshot justFinishedPlayerResult = new(
                GameMapId.OpenTropicalBattlefield,
                GameMapId.OpenTropicalBattlefield.SceneStableId,
                1234,
                GameControllerMode.Human,
                true,
                15f,
                200,
                12f,
                "open_tropical_battlefield|1234|runtime-ui-bridge",
                string.Empty,
                string.Empty,
                GameLaunchRequest.CurrentProtocolVersion);
            Assert.That(
                (bool)collector.GetType().GetMethod("TryFreeze").Invoke(
                    collector,
                    new object[] { justFinishedPlayerResult }),
                Is.True);
            Assert.That(
                (GameResultSnapshot?)controller.GetType()
                    .GetField("PlayerResult", BindingFlags.Instance | BindingFlags.NonPublic)
                    .GetValue(controller),
                Is.EqualTo(justFinishedPlayerResult),
                "The just-finished Human snapshot must be the one carried by AI Strategy.");

            List<GameResultActionRoute> routes = new();
            Action<GameResultActionRoute> observe = route => routes.Add(route);
            EventInfo routeResolved = controller.GetType().GetEvent(
                "RouteResolved", BindingFlags.Instance | BindingFlags.Public);
            routeResolved.AddEventHandler(controller, observe);
            try
            {
                ((Behaviour)controller).enabled = false;
                yield return null;
                ((Behaviour)controller).enabled = true;
                yield return null;

                Submit(document.rootVisualElement.Q<Button>("restart-button"));
                Submit(document.rootVisualElement.Q<Button>("new-layout-button"));
                Submit(document.rootVisualElement.Q<Button>("ai-strategy-button"));
                Assert.That(routes, Has.Count.EqualTo(2),
                    "Opening the strategy picker must not launch a model before one is selected.");
                Submit(document.rootVisualElement.Q<Button>("ppo-strategy-button"));
                Submit(document.rootVisualElement.Q<Button>("select-map-button"));

                Assert.That(routes, Has.Count.EqualTo(4));
                Assert.That(routes[0].Request.MapId, Is.EqualTo(GameMapId.OpenTropicalBattlefield));
                Assert.That(routes[0].Request.LayoutSeed, Is.EqualTo(1234));
                Assert.That(routes[0].Request.Controller, Is.EqualTo(GameControllerMode.Human));
                Assert.That(routes[0].Request.StartAutomatically, Is.True);
                Assert.That(routes[1].Request.MapId, Is.EqualTo(GameMapId.OpenTropicalBattlefield));
                Assert.That(routes[1].Request.LayoutSeed, Is.Not.EqualTo(1234));
                Assert.That(routes[1].Request.Controller, Is.EqualTo(GameControllerMode.Human));
                Assert.That(routes[1].Request.StartAutomatically, Is.True);
                Assert.That(routes[2].Request.MapId, Is.EqualTo(GameMapId.OpenTropicalBattlefield));
                Assert.That(routes[2].Request.LayoutSeed, Is.EqualTo(1234));
                Assert.That(routes[2].Request.Controller, Is.EqualTo(GameControllerMode.InferenceAI));
                Assert.That(routes[2].Request.StartAutomatically, Is.True);
                Assert.That(routes[3].Request.MapId, Is.EqualTo(GameMapId.MapSelect));
                Assert.That(routes[3].Request.Controller, Is.EqualTo(GameControllerMode.Human));
                Assert.That(routes[3].Request.StartAutomatically, Is.False);
                Assert.That(document.rootVisualElement.Q<Button>("start-button").resolvedStyle.display,
                    Is.EqualTo(DisplayStyle.Flex),
                    "a fresh menu launch must retain the central Start entry");

                SetPrivateField(controller, "IsActionPending", true);
                Submit(document.rootVisualElement.Q<Button>("restart-button"));
                Assert.That(routes, Has.Count.EqualTo(4),
                    "a pending full-scene reload must suppress duplicate result clicks");

                Type stateType = RuntimeType("LlamAcademy.Dinos.RoundManagement.GameState");
                object ended = Enum.Parse(stateType, "Ended");
                InvokePrivate(runtimeUi, "OnGameStateChange", ended, ended);
                string firstOutcome = document.rootVisualElement.Q<Label>("win-lose-text").text;
                InvokePrivate(runtimeUi, "OnGameStateChange", ended, ended);
                Assert.That(document.rootVisualElement.Query<Label>("win-lose-text").ToList(), Has.Count.EqualTo(1));
                Assert.That(document.rootVisualElement.Q<Label>("win-lose-text").text, Is.EqualTo(firstOutcome),
                    "repeated Ended notifications must not duplicate or replace the single outcome");
            }
            finally
            {
                routeResolved.RemoveEventHandler(controller, observe);
                SetPrivateField(controller, "IsActionPending", false);
            }
        }

        private static void Submit(Button button)
        {
            Assert.That(button, Is.Not.Null);
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }

        private static IEnumerator ClickUiButton(InputTestFixture input, Mouse mouse, Button button)
        {
            Assert.That(button, Is.Not.Null);
            Assert.That(button.enabledSelf, Is.True);
            Assert.That(button.worldBound.width, Is.GreaterThan(0f));
            Assert.That(button.worldBound.height, Is.GreaterThan(0f));

            UIDocument document = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI")
                .GetComponent<UIDocument>();
            Vector2 panelCenter = button.worldBound.center;
            Rect panelBounds = document.rootVisualElement.worldBound;
            Vector2 screenCenter = new(
                panelCenter.x * Screen.width / panelBounds.width,
                Screen.height - panelCenter.y * Screen.height / panelBounds.height);
            input.Set(mouse.position, screenCenter);
            yield return null;
            input.Press(mouse.leftButton);
            yield return null;
            input.Release(mouse.leftButton);
            yield return null;
        }

        private static IEnumerator EndCurrentMapOneSession()
        {
            for (int frame = 0; frame < 3; frame++) yield return null;
            Component roundManager = FindRuntimeComponent(
                "LlamAcademy.Dinos.RoundManagement.RoundManager");
            object state = roundManager.GetType().GetProperty("State").GetValue(roundManager);
            if (state.ToString() == "Setup")
            {
                roundManager.GetType().GetMethod("StartRound", BindingFlags.Instance | BindingFlags.Public)
                    .Invoke(roundManager, null);
            }

            InvokePrivate(roundManager, "BeginEnding", false);
            float deadline = Time.realtimeSinceStartup + 4f;
            while (roundManager.GetType().GetProperty("State").GetValue(roundManager).ToString() != "Ended" &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(roundManager.GetType().GetProperty("State").GetValue(roundManager).ToString(),
                Is.EqualTo("Ended"));
        }

        private static IEnumerator ClickResultButton(
            InputTestFixture input,
            Mouse mouse,
            string buttonName,
            bool requireEnabled = true)
        {
            Component runtimeUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
            UIDocument document = runtimeUi.GetComponent<UIDocument>();
            Button button = document.rootVisualElement.Q<Button>(buttonName);
            Assert.That(button, Is.Not.Null);
            if (requireEnabled)
            {
                Assert.That(button.enabledSelf, Is.True, $"{buttonName} must still be interactive.");
            }

            Rect rootBounds = document.rootVisualElement.worldBound;
            Vector2 panelCenter = button.worldBound.center;
            Vector2 screenCenter = new(
                panelCenter.x * Screen.width / rootBounds.width,
                Screen.height - panelCenter.y * Screen.height / rootBounds.height);
            input.Set(mouse.position, screenCenter);
            yield return null;
            input.Press(mouse.leftButton);
            yield return null;
            input.Release(mouse.leftButton);
        }

        private static IEnumerator ClickMapOneButton(InputTestFixture input, Mouse mouse)
        {
            Component mapSelectUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.MapSelectUI");
            UnityEngine.UI.Button button = (UnityEngine.UI.Button)mapSelectUi.GetType()
                .GetField("OpenTropicalButton", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(mapSelectUi);
            Assert.That(button, Is.Not.Null);
            Assert.That(button.interactable, Is.True);
            Vector2 center = RectTransformUtility.WorldToScreenPoint(
                null,
                button.GetComponent<RectTransform>().position);
            input.Set(mouse.position, center);
            yield return null;
            input.Press(mouse.leftButton);
            yield return null;
            input.Release(mouse.leftButton);
        }

        private static IEnumerator WaitForScene(string sceneName)
        {
            float deadline = Time.realtimeSinceStartup + 4f;
            while (SceneManager.GetActiveScene().name != sceneName &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(sceneName));
            for (int frame = 0; frame < 3; frame++) yield return null;
        }

        private static IEnumerator WaitForReloadedScene(string sceneName, int previousSceneHandle)
        {
            float deadline = Time.realtimeSinceStartup + 4f;
            while ((SceneManager.GetActiveScene().name != sceneName ||
                    SceneManager.GetActiveScene().handle == previousSceneHandle) &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(sceneName));
            Assert.That(SceneManager.GetActiveScene().handle, Is.Not.EqualTo(previousSceneHandle),
                "A same-map result action must complete a full scene reload before the next result cycle.");
            for (int frame = 0; frame < 3; frame++) yield return null;
        }

        [UnityTest]
        public IEnumerator Map1LayoutSignature_CapturesTheActualGeneratedLayoutAfterInitialization()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 3; frame++) yield return null;

            GameObject providerObject = new("Map1 Layout Signature Test Provider");
            Component providerComponent = providerObject.AddComponent(
                RuntimeType("LlamAcademy.Dinos.Map.Adapters.OpenTropicalBattlefieldLayoutSignatureProvider"));
            IBattlefieldLayoutSignatureProvider provider =
                providerComponent as IBattlefieldLayoutSignatureProvider;
            Assert.That(provider, Is.Not.Null);

            BattlefieldLayoutSignature signature = null;
            for (int frame = 0; frame < 10 && !provider.TryCapture(out signature); frame++)
            {
                yield return null;
            }

            Assert.That(signature, Is.Not.Null, "the provider must wait for and capture generated runtime objects");
            Assert.That(signature.MapStableId, Is.EqualTo("open_tropical_battlefield"));
            Assert.That(signature.ZoneVertices, Has.Count.EqualTo(5));
            Assert.That(signature.Houses, Has.Count.EqualTo(8));
            Assert.That(signature.Walls, Has.Count.EqualTo(3));
            Assert.That(signature.Guards, Has.Count.EqualTo(8),
                "Map1 has five generated ground guards and three guards attached to active walls");
            Assert.That(signature.Targets, Has.Count.EqualTo(1));

            Assert.That(provider.TryCapture(out BattlefieldLayoutSignature repeated), Is.True);
            Assert.That(repeated, Is.EqualTo(signature),
                "capturing the same initialized scene twice must preserve actual coordinates and IDs");

            UnityEngine.Object.Destroy(providerObject);
        }

        [UnityTest]
        public IEnumerator SceneDeploymentLifecycle_WiresFiveZonesHudInvalidFeedbackAndEndingCleanup()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 3; frame++) yield return null;

            Component runtimeUI = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
            Component visualization = FindRuntimeComponent("LlamAcademy.Dinos.Player.PlaceDinoVisualization");
            DinoDeploymentZonePresenter presenter = UnityEngine.Object.FindFirstObjectByType<DinoDeploymentZonePresenter>();
            Assert.That(runtimeUI, Is.Not.Null);
            Assert.That(visualization, Is.Not.Null);
            Assert.That(presenter, Is.Not.Null);
            Assert.That(GetPrivateField(runtimeUI, "ZonePresenter"), Is.SameAs(presenter),
                "the real RuntimeUI must be wired to the scene presenter");
            Assert.That(GetPrivateField(visualization, "ZonePresenter"), Is.SameAs(presenter),
                "the real placement preview must be wired to the same scene presenter");

            Array bindings = (Array)GetPrivateField(presenter, "Bindings");
            Renderer[] boundaries = bindings.Cast<object>()
                .Select(binding => (Renderer)binding.GetType().GetProperty("Renderer").GetValue(binding))
                .ToArray();
            Assert.That(boundaries, Has.Length.EqualTo(5));

            MethodInfo changeDino = visualization.GetType().GetMethod("ChangeDino", BindingFlags.Instance | BindingFlags.Public);
            changeDino.Invoke(visualization, new object[] { null });
            yield return null;
            Assert.That(boundaries.All(boundary => !boundary.enabled), Is.True,
                "no selection must hide every deployment boundary");

            Array dinos = (Array)GetPrivateField(runtimeUI, "Dinos");
            Assert.That(dinos, Is.Not.Null.And.Not.Empty);
            changeDino.Invoke(visualization, new[] { dinos.GetValue(0) });
            yield return null;
            Assert.That(boundaries.All(boundary => boundary.enabled), Is.True,
                "a real dino selection must reveal all five possible deployment regions");

            DinoDeploymentZone river = UnityEngine.Object.FindObjectsByType<DinoDeploymentZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Single(zone => zone.Id == DeploymentZoneId.Z3RiverTerrace);
            presenter.UpdateHover(ResolveZonePoint(river, new Vector2(0.5f, 0.5f)));
            yield return null;

            UIDocument document = runtimeUI.GetComponent<UIDocument>();
            Label terrain = document.rootVisualElement.Q<Label>("terrain-feedback");
            Label deployment = document.rootVisualElement.Q<Label>("deployment-feedback");
            Button start = document.rootVisualElement.Q<Button>("start-button");
            Assert.That(start, Is.Not.Null);
            Assert.That(start.enabledSelf, Is.True,
                "Setup must allow Start before the player deploys any dinosaur");
            Assert.That(terrain.text, Is.EqualTo("River Terrace · Shallow Water 0.70×"));
            Assert.That(presenter.IsHighlighted(river), Is.True);

            InvokePrivate(runtimeUI, "HandleDeploymentEvaluated",
                DeploymentResult.Rejected(DeploymentFailureReason.OutsideBounds));
            Assert.That(deployment.text, Does.StartWith("[!]"));
            Assert.That(deployment.text, Does.Contain("deployment area"));

            Type stateType = RuntimeType("LlamAcademy.Dinos.RoundManagement.GameState");
            object setup = Enum.Parse(stateType, "Setup");
            object ending = Enum.Parse(stateType, "Ending");
            InvokePrivate(visualization, "OnGameStateChange", setup, ending);
            InvokePrivate(runtimeUI, "OnGameStateChange", setup, ending);
            yield return new WaitForSeconds(0.35f);

            Assert.That(terrain.text, Is.Empty, "Ending must clear terrain HUD feedback");
            Assert.That(deployment.text, Is.Empty, "Ending must clear deployment HUD feedback");
            Assert.That(boundaries.All(boundary => !boundary.enabled), Is.True,
                "Ending must hide all five boundaries");
            GameObject safeZone = (GameObject)GetPrivateField(visualization, "SafeZone");
            Assert.That(safeZone.activeSelf, Is.False, "Ending must fully hide the placement safe-zone preview");
            ICollection previews = (ICollection)GetPrivateField(visualization, "Visualizations");
            Assert.That(previews.Count, Is.Zero, "Ending must destroy and clear all selected-dino previews");
        }

        [UnityTest]
        public IEnumerator RunningSession_AutomaticallyFailsAtFiftySeconds()
        {
            InputTestFixture input = new();
            input.Setup();
            InputSystem.AddDevice<Keyboard>();
            InputSystem.AddDevice<Mouse>();
            float originalTimeScale = Time.timeScale;
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
                for (int frame = 0; frame < 3; frame++) yield return null;

                Component roundManager = FindRuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
                Assert.That(roundManager, Is.Not.Null);
                roundManager.GetType().GetMethod("StartRound", BindingFlags.Instance | BindingFlags.Public)
                    .Invoke(roundManager, null);

                Time.timeScale = 100f;
                float runningStartedAt = Time.time;
                while (Time.time - runningStartedAt < 48f)
                {
                    yield return null;
                }
                bool endedBeforeLimit = (bool)roundManager.GetType().GetProperty("HasSessionOutcome").GetValue(roundManager);

                while (Time.time - runningStartedAt < 51f)
                {
                    yield return null;
                }
                bool endedAfterLimit = (bool)roundManager.GetType().GetProperty("HasSessionOutcome").GetValue(roundManager);
                bool playerWon = (bool)roundManager.GetType().GetProperty("PlayerWon").GetValue(roundManager);

                Assert.That(endedBeforeLimit, Is.False, "the session must remain active before 50 gameplay seconds");
                Assert.That(endedAfterLimit, Is.True, "the session must end at 50 gameplay seconds");
                Assert.That(playerWon, Is.False, "a timeout must use the existing defeat outcome");
            }
            finally
            {
                Time.timeScale = originalTimeScale;
                input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator VictoryTrigger_RejectsNonDinoAndAuditsRegisteredLiveDinoOnce()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 3; frame++) yield return null;

            Component roundManager = FindRuntimeComponent(
                "LlamAcademy.Dinos.RoundManagement.RoundManager");
            Component runtimeUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
            Component spawner = FindRuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
            Component invalidTrigger = UnityEngine.Object.FindObjectsByType<Component>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .First(component => component != null &&
                    component.GetType().FullName == "LlamAcademy.Dinos.Unit.VillageHouse" &&
                    (int)component.GetType().GetProperty("Health").GetValue(component) > 0);
            DinoDeploymentZone zone = UnityEngine.Object.FindObjectsByType<DinoDeploymentZone>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .OrderBy(candidate => candidate.Id)
                .First();
            Array dinos = (Array)GetPrivateField(runtimeUi, "Dinos");
            spawner.GetType().GetProperty("ResourcesToSpend").SetValue(spawner, 10_000);
            object[] deploymentArguments =
            {
                dinos.GetValue(0), ResolveZonePoint(zone, new Vector2(0.5f, 0.5f)), false, null, null
            };
            Assert.That((bool)spawner.GetType().GetMethod("TryDeploy").Invoke(spawner, deploymentArguments),
                Is.True);
            Component validTrigger = (Component)deploymentArguments[4];
            Assert.That(validTrigger, Is.Not.Null);
            List<string> outcomeLogs = new();

            void CaptureOutcome(string condition, string stackTrace, LogType type)
            {
                if (condition.StartsWith("DINO_SESSION_OUTCOME ", StringComparison.Ordinal))
                {
                    outcomeLogs.Add(condition);
                }
            }

            Application.logMessageReceived += CaptureOutcome;
            try
            {
                roundManager.GetType().GetMethod("StartRound").Invoke(roundManager, null);
                InvokePrivate(roundManager, "HandleDinoEnterEggRadius", invalidTrigger);

                Assert.That(roundManager.GetType().GetProperty("State").GetValue(roundManager).ToString(),
                    Is.EqualTo("Running"), "a house or any other non-Dino IDamageable must not win");
                Assert.That(roundManager.GetType().GetProperty("HasSessionOutcome").GetValue(roundManager),
                    Is.False);
                Assert.That(outcomeLogs, Is.Empty);

                InvokePrivate(roundManager, "HandleDinoEnterEggRadius", validTrigger);
                InvokePrivate(roundManager, "HandleDinoEnterEggRadius", validTrigger);

                Assert.That(outcomeLogs, Has.Count.EqualTo(1),
                    "one natural terminal must emit exactly one durable outcome audit record");
                string outcome = outcomeLogs[0];
                Assert.That(outcome, Does.Contain("won=true"));
                Assert.That(outcome, Does.Contain("reason=TargetRadiusEntered"));
                Assert.That(outcome, Does.Contain($"trigger_name={validTrigger.name}"));
                Assert.That(outcome, Does.Contain($"trigger_type={validTrigger.GetType().FullName}"));
                Assert.That(outcome, Does.Contain($"trigger_instance_id={validTrigger.GetInstanceID()}"));
                Assert.That(outcome, Does.Contain(
                    $"trigger_position_x={validTrigger.transform.position.x.ToString("R", CultureInfo.InvariantCulture)}"));
                Assert.That(outcome, Does.Contain(
                    $"trigger_position_y={validTrigger.transform.position.y.ToString("R", CultureInfo.InvariantCulture)}"));
                Assert.That(outcome, Does.Contain(
                    $"trigger_position_z={validTrigger.transform.position.z.ToString("R", CultureInfo.InvariantCulture)}"));
            }
            finally
            {
                Application.logMessageReceived -= CaptureOutcome;
            }
        }

        [UnityTest]
        public IEnumerator StructuredObservations_RealSceneEmitsDinoTargetAndDynamicRegions()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frameIndex = 0; frameIndex < 3; frameIndex++) yield return null;

            GameObject observationRoot = new("Structured Observation Test Root");
            try
            {
                Component roundManager = FindRuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
                DinoDeploymentZone[] zones = UnityEngine.Object
                    .FindObjectsByType<DinoDeploymentZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .OrderBy(zone => zone.Id)
                    .ToArray();
                Assert.That(roundManager, Is.Not.Null);
                Assert.That(zones.Select(zone => zone.Id), Is.EqualTo(Enum.GetValues(typeof(DeploymentZoneId))));

                Type builderType = RuntimeType("LlamAcademy.Dinos.Training.DinoStructuredObservationBuilder");
                Component builder = observationRoot.AddComponent(builderType);
                SetPrivateField(builder, "Zones", zones);
                MethodInfo buildFrame = builderType.GetMethod("BuildFrame", BindingFlags.Instance | BindingFlags.Public);
                Assert.That(buildFrame, Is.Not.Null);
                object frame = buildFrame.Invoke(builder, null);

                float[] global = ReadStream(frame, "Global");
                Assert.That(global, Has.Length.EqualTo(5));
                Transform dinoTarget = (Transform)roundManager.GetType().GetProperty("DinoTarget")?.GetValue(roundManager);
                Assert.That(dinoTarget, Is.Not.Null, "the one RoundManager DinoTarget is the only final destination");
                Assert.That(global[3], Is.EqualTo(NormalizeObservationCoordinate(builderType, dinoTarget.position.x, -55.5f, 48.5f)));
                Assert.That(global[4], Is.EqualTo(NormalizeObservationCoordinate(builderType, dinoTarget.position.z, -53f, 29f)));

                float[][] regionRows = ReadRows(frame, "Regions", 8).ToArray();
                Assert.That(regionRows, Has.Length.EqualTo(5));
                for (int zoneIndex = 0; zoneIndex < zones.Length; zoneIndex++)
                {
                    Vector3[] vertices = zones[zoneIndex].WorldVertices.ToArray();
                    Assert.That(vertices, Has.Length.EqualTo(4));
                    float[] expected = vertices.SelectMany(vertex => new[]
                    {
                        NormalizeObservationCoordinate(builderType, vertex.x, -55.5f, 48.5f),
                        NormalizeObservationCoordinate(builderType, vertex.z, -53f, 29f)
                    }).ToArray();
                    Assert.That(regionRows[zoneIndex], Is.EqualTo(expected), $"{zones[zoneIndex].Id} must emit A/B/C/D in frozen Z1-Z5 order");
                }

                Assert.That(ReadStream(frame, "Walls"), Has.Length.EqualTo(30));
                Assert.That(ReadRows(frame, "Walls", 5).All(row => row[0] == 1f), Is.True);
                Assert.That(ReadRows(frame, "Walls", 5).Count(row => row[1] == 1f), Is.EqualTo(3));
                float[][] guardRows = ReadRows(frame, "Guards", 7).ToArray();
                Assert.That(guardRows.Count(row => row[0] == 1f), Is.EqualTo(8));
                Assert.That(guardRows.Where(row => row[0] == 1f).All(row => row[1] + row[2] == 1f), Is.True,
                    "the v1 guard stream has Archer/Mage columns only; there is no Cannoneer column");
                Assert.That(guardRows.Count(row => row[6] == 1f), Is.EqualTo(3));
                Assert.That(ReadRows(frame, "Houses", 6).All(row => row[0] == 1f), Is.True);
                Assert.That(ReadRows(frame, "Dinos", 7).All(row => row[0] == 0f), Is.True);
                Assert.That(new[] { "Global", "Regions", "Walls", "Guards", "Houses", "Dinos" }
                        .SelectMany(streamName => ReadStream(frame, streamName))
                        .All(float.IsFinite),
                    Is.True,
                    "every structured observation value must be finite");

                Type sensorComponentType = RuntimeType(
                    "LlamAcademy.Dinos.Training.DinoStructuredObservationSensorComponent");
                Component sensorComponent = observationRoot.AddComponent(sensorComponentType);
                Array sensors = (Array)sensorComponentType
                    .GetMethod("CreateSensors", BindingFlags.Instance | BindingFlags.Public)
                    .Invoke(sensorComponent, null);

                string[] expectedNames = { "00_Global", "01_Regions", "02_Walls", "03_Guards", "04_Houses", "05_Dinos" };
                int[][] expectedShapes =
                {
                    new[] { 5 },
                    new[] { 5, 8 },
                    new[] { 6, 5 },
                    new[] { 11, 7 },
                    new[] { 8, 6 },
                    new[] { 10, 7 }
                };
                Assert.That(sensors, Has.Length.EqualTo(expectedNames.Length));
                for (int sensorIndex = 0; sensorIndex < sensors.Length; sensorIndex++)
                {
                    object sensor = sensors.GetValue(sensorIndex);
                    Assert.That(sensor.GetType().GetMethod("GetName").Invoke(sensor, null),
                        Is.EqualTo(expectedNames[sensorIndex]));
                    object spec = sensor.GetType().GetMethod("GetObservationSpec").Invoke(sensor, null);
                    Assert.That(ReadObservationShape(spec), Is.EqualTo(expectedShapes[sensorIndex]));
                }

                MethodInfo getCachedFrame = builderType.GetMethod(
                    "GetFrameForAcademyStep", BindingFlags.Instance | BindingFlags.Public);
                object cachedFrame = getCachedFrame.Invoke(builder, new object[] { 41 });
                foreach (object sensor in sensors)
                {
                    sensor.GetType().GetMethod("Update").Invoke(sensor, null);
                    sensor.GetType().GetMethod("Reset").Invoke(sensor, null);
                }
                Assert.That(getCachedFrame.Invoke(builder, new object[] { 41 }), Is.SameAs(cachedFrame),
                    "all sensors must retain one immutable frame for the same Academy step");
                Assert.That(getCachedFrame.Invoke(builder, new object[] { 42 }), Is.Not.SameAs(cachedFrame),
                    "a changed Academy step must replace the cached frame");

                FieldInfo dinoTargetField = roundManager.GetType().GetField(
                    "<DinoTarget>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(dinoTargetField, Is.Not.Null);
                Transform originalDinoTarget = (Transform)dinoTargetField.GetValue(roundManager);
                try
                {
                    dinoTargetField.SetValue(roundManager, null);
                    AssertInvalidObservationContract(buildFrame, builder, "DinoTarget");
                }
                finally
                {
                    dinoTargetField.SetValue(roundManager, originalDinoTarget);
                }

                DinoDeploymentZone[] originalZones = (DinoDeploymentZone[])GetPrivateField(builder, "Zones");
                FieldInfo zoneIdField = typeof(DinoDeploymentZone).GetField(
                    "<Id>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(zoneIdField, Is.Not.Null);
                DeploymentZoneId originalId = zones[1].Id;
                try
                {
                    zoneIdField.SetValue(zones[1], zones[0].Id);
                    AssertInvalidObservationContract(buildFrame, builder, "unique Zone IDs");
                }
                finally
                {
                    zoneIdField.SetValue(zones[1], originalId);
                }

                try
                {
                    DinoDeploymentZone[] missingZone = originalZones.ToArray();
                    missingZone[0] = null;
                    SetPrivateField(builder, "Zones", missingZone);
                    AssertInvalidObservationContract(buildFrame, builder, "zone references");
                }
                finally
                {
                    SetPrivateField(builder, "Zones", originalZones);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(observationRoot);
            }
        }

        [UnityTest]
        public IEnumerator TrainingTask3_RegionActionsWaitAndDinoCapUseRealZones()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frameIndex = 0; frameIndex < 3; frameIndex++) yield return null;

            Component runtimeUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
            Component spawner = FindRuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
            Component roundManager = FindRuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
            DinoDeploymentZone[] zones = UnityEngine.Object
                .FindObjectsByType<DinoDeploymentZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .OrderBy(zone => zone.Id)
                .ToArray();
            Assert.That(runtimeUi, Is.Not.Null);
            Assert.That(spawner, Is.Not.Null);
            Assert.That(roundManager, Is.Not.Null);
            Assert.That(zones.Select(zone => zone.Id), Is.EqualTo(Enum.GetValues(typeof(DeploymentZoneId))));

            GameObject trainingRoot = new("Task 3 Action Agent");
            try
            {
                Component reloader = trainingRoot.AddComponent(
                    RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingSceneReloader"));
                Component agent = trainingRoot.AddComponent(
                    RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingAgent"));
                SetPrivateField(agent, "SceneReloader", reloader);
                SetPrivateField(agent, "DinoTypes", GetPrivateField(runtimeUi, "Dinos"));
                spawner.GetType().GetProperty("ResourcesToSpend").SetValue(spawner, 1_000_000);

                int foodBeforeWait = (int)spawner.GetType().GetProperty("ResourcesToSpend").GetValue(spawner);
                int dinosBeforeWait = (int)roundManager.GetType().GetProperty("AliveDinos").GetValue(roundManager);
                SetPrivateField(agent, "Zones", null);
                InvokeTrainingAction(agent, new[] { 1f, -1f, 1f, -1f });
                Assert.That(agent.GetType().GetProperty("LastActionWasValid").GetValue(agent), Is.True,
                    "Wait must be legal without consulting a deployment zone");
                Assert.That(roundManager.GetType().GetProperty("AliveDinos").GetValue(roundManager),
                    Is.EqualTo(dinosBeforeWait));
                Assert.That(spawner.GetType().GetProperty("ResourcesToSpend").GetValue(spawner),
                    Is.EqualTo(foodBeforeWait));

                SetPrivateField(agent, "Zones", zones);
                Vector2[] cornerAndCenterSamples =
                {
                    new(0.05f, 0.05f),
                    new(0.95f, 0.05f),
                    new(0.95f, 0.95f),
                    new(0.05f, 0.95f),
                    new(0.5f, 0.5f)
                };
                DinoDeploymentZone sampledZone = zones[1];
                foreach (Vector2 relativeUv in cornerAndCenterSamples)
                {
                    Vector3 expectedCandidate = ResolveZonePoint(sampledZone, relativeUv);
                    int countBefore = (int)roundManager.GetType().GetProperty("AliveDinos").GetValue(roundManager);
                    InvokeTrainingAction(agent, EncodeTrainingAction(1, relativeUv, 0));
                    Assert.That(agent.GetType().GetProperty("LastActionWasValid").GetValue(agent), Is.True,
                        $"the Agent path must accept {sampledZone.Id}/{relativeUv}");
                    Assert.That(roundManager.GetType().GetProperty("AliveDinos").GetValue(roundManager),
                        Is.EqualTo(countBefore + 1));

                    Component spawned = ActiveDinoComponents(roundManager).Last();
                    Vector2 expectedXz = new(expectedCandidate.x, expectedCandidate.z);
                    Vector2 actualXz = new(spawned.transform.position.x, spawned.transform.position.z);
                    Assert.That(Vector2.Distance(actualXz, expectedXz), Is.LessThanOrEqualTo(2.01f),
                        "the selected quadrilateral candidate may only move by the deployment service's 2m NavMesh projection");
                }

                int fillIndex = 0;
                while ((int)roundManager.GetType().GetProperty("AliveDinos").GetValue(roundManager) < 10)
                {
                    int zoneIndex = fillIndex % zones.Length;
                    Vector2 relativeUv = fillIndex % 2 == 0 ? new Vector2(0.25f, 0.5f) : new Vector2(0.75f, 0.5f);
                    InvokeTrainingAction(agent, EncodeTrainingAction(zoneIndex, relativeUv, 0));
                    Assert.That(agent.GetType().GetProperty("LastActionWasValid").GetValue(agent), Is.True,
                        $"deployment {fillIndex + 6} must fill the legal ten-dino capacity");
                    fillIndex++;
                }

                int foodBeforeEleventh = (int)spawner.GetType().GetProperty("ResourcesToSpend").GetValue(spawner);
                InvokeTrainingAction(agent, EncodeTrainingAction(4, new Vector2(0.5f, 0.5f), 0));
                Assert.That(agent.GetType().GetProperty("LastActionWasValid").GetValue(agent), Is.False,
                    "the eleventh dino must be rejected by the shared deployment path");
                Assert.That(roundManager.GetType().GetProperty("AliveDinos").GetValue(roundManager), Is.EqualTo(10));
                Assert.That(spawner.GetType().GetProperty("ResourcesToSpend").GetValue(spawner),
                    Is.EqualTo(foodBeforeEleventh), "a rejected eleventh dino must not consume food");
            }
            finally
            {
                UnityEngine.Object.Destroy(trainingRoot);
            }
        }

        [UnityTest]
        public IEnumerator TrainingRewardAgent_UsesApprovedActionAndNaturalDefeatRewards()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frameIndex = 0; frameIndex < 3; frameIndex++) yield return null;

            Component runtimeUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
            Component spawner = FindRuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
            Component roundManager = FindRuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
            Component restartService = FindRuntimeComponent(
                "LlamAcademy.Dinos.RoundManagement.GameSessionRestartService");
            DinoDeploymentZone[] zones = UnityEngine.Object
                .FindObjectsByType<DinoDeploymentZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .OrderBy(zone => zone.Id)
                .ToArray();

            Type contextType = RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingRunContext");
            contextType.GetMethod("Configure", BindingFlags.Static | BindingFlags.Public)
                .Invoke(null, new object[] { 7, 2, 3 });

            GameObject trainingRoot = new("Task 3 Natural Terminal Agent");
            float originalTimeScale = Time.timeScale;
            try
            {
                Component reloader = trainingRoot.AddComponent(
                    RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingSceneReloader"));
                Component agent = trainingRoot.AddComponent(
                    RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingAgent"));
                SetPrivateField(agent, "SceneReloader", reloader);
                SetPrivateField(agent, "DinoTypes", GetPrivateField(runtimeUi, "Dinos"));
                SetPrivateField(agent, "Zones", zones);
                spawner.GetType().GetProperty("ResourcesToSpend").SetValue(spawner, 1_000_000);

                yield return null;
                yield return null;
                InvokeTrainingAction(agent, Array.Empty<float>());
                Assert.That(GetInheritedField(agent, "m_Reward"), Is.EqualTo(-0.02f).Within(0.001f),
                    "a rejected non-Wait action must receive exactly one invalid-deployment penalty");

                InvokeTrainingAction(agent, new[] { -0.8f, 0f, 0f, -1f });
                Assert.That(GetInheritedField(agent, "m_Reward"), Is.EqualTo(-0.02f).Within(0.001f),
                    "Wait must not access placement or change reward");

                InvokeTrainingAction(agent, EncodeTrainingAction(1, new Vector2(0.5f, 0.5f), 0));
                Assert.That(roundManager.GetType().GetProperty("State").GetValue(roundManager).ToString(),
                    Is.EqualTo("Running"));
                Assert.That(GetInheritedField(agent, "m_Reward"), Is.EqualTo(-0.02f).Within(0.001f),
                    "a legal deployment must not receive an additional cost penalty");

                Component livingDino = ActiveDinoComponents(roundManager).Single();
                livingDino.gameObject.SetActive(false);

                Time.timeScale = 100f;
                float realDeadline = Time.realtimeSinceStartup + 2f;
                while (!(bool)roundManager.GetType().GetProperty("HasSessionOutcome").GetValue(roundManager) &&
                       Time.realtimeSinceStartup < realDeadline)
                {
                    yield return null;
                }

                Assert.That(roundManager.GetType().GetProperty("AliveDinos").GetValue(roundManager),
                    Is.EqualTo(1), "the timeout case must still have a live dino");
                Assert.That(roundManager.GetType().GetProperty("PlayerWon").GetValue(roundManager), Is.False);
                Assert.That(reloader.GetType().GetProperty("IsReloadPending").GetValue(reloader), Is.True);
                object terminalInfo = GetInheritedField(agent, "m_Info");
                Assert.That(GetFieldValue(terminalInfo, "done"), Is.True);
                Assert.That(GetFieldValue(terminalInfo, "maxStepReached"), Is.False,
                    "a gameplay timeout is a natural terminal, not an infrastructure interruption");
                Assert.That(GetFieldValue(terminalInfo, "reward"), Is.EqualTo(-1.02f).Within(0.001f),
                    "a natural defeat must pay -1 without a remaining-meat reward");
                Assert.That(contextType.GetProperty("EpisodeIndex").GetValue(null), Is.EqualTo(1));
                Assert.That(contextType.GetProperty("EpisodeSeed").GetValue(null), Is.EqualTo(1123543703));
                Assert.That(GetStaticField(restartService.GetType(), "PendingStartSeed"), Is.EqualTo(1123543703));
            }
            finally
            {
                Time.timeScale = originalTimeScale;
                if (restartService is MonoBehaviour restartBehaviour)
                {
                    restartBehaviour.StopAllCoroutines();
                    SetAutoProperty(restartService, "IsReloadPending", false);
                    SetStaticField(restartService.GetType(), "PendingStartSeed", null);
                }
                UnityEngine.Object.Destroy(trainingRoot);
            }
        }

        [UnityTest]
        public IEnumerator TrainingRewardAgent_VictoryFreezesProcessAndFinalMeatRewardExactlyOnce()
        {
            bool addedKeyboard = Keyboard.current == null;
            bool addedMouse = Mouse.current == null;
            Keyboard testKeyboard = addedKeyboard ? InputSystem.AddDevice<Keyboard>() : Keyboard.current;
            Mouse testMouse = addedMouse ? InputSystem.AddDevice<Mouse>() : Mouse.current;
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frameIndex = 0; frameIndex < 3; frameIndex++) yield return null;

            Component runtimeUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
            Component spawner = FindRuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
            Component roundManager = FindRuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
            DinoDeploymentZone[] zones = UnityEngine.Object
                .FindObjectsByType<DinoDeploymentZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .OrderBy(zone => zone.Id)
                .ToArray();

            GameObject trainingRoot = new("Reward Victory Agent");
            try
            {
                Component agent = trainingRoot.AddComponent(
                    RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingAgent"));
                SetPrivateField(agent, "DinoTypes", GetPrivateField(runtimeUi, "Dinos"));
                SetPrivateField(agent, "Zones", zones);
                spawner.GetType().GetProperty("ResourcesToSpend").SetValue(spawner, 1_000_000);

                yield return null;
                yield return null;
                InvokeTrainingAction(agent, EncodeTrainingAction(1, new Vector2(0.5f, 0.5f), 0));
                Assert.That(roundManager.GetType().GetProperty("State").GetValue(roundManager).ToString(),
                    Is.EqualTo("Running"));

                Component wall = UnityEngine.Object.FindObjectsByType<Component>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .First(component => component != null &&
                        component.GetType().FullName == "LlamAcademy.Dinos.Unit.Wall" &&
                        (int)component.GetType().GetProperty("Health").GetValue(component) > 0);
                wall.GetType().GetMethod("TakeDamage").Invoke(wall, new object[] { int.MaxValue });
                Assert.That(GetInheritedField(agent, "m_Reward"), Is.EqualTo(0.07f).Within(0.001f),
                    "the real wall death must reward both the wall and its retired fixed guard exactly once");

                Component house = UnityEngine.Object.FindObjectsByType<Component>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .First(component => component != null &&
                        component.GetType().FullName == "LlamAcademy.Dinos.Unit.VillageHouse" &&
                        !(bool)component.GetType().GetProperty("IsDestroyed").GetValue(component));
                house.GetType().GetMethod("Die").Invoke(house, null);
                house.GetType().GetMethod("Die").Invoke(house, null);
                Assert.That(GetInheritedField(agent, "m_Reward"), Is.EqualTo(0.17f).Within(0.001f),
                    "each stable house must add +0.1 exactly once");

                const int finalMeat = 200;
                spawner.GetType().GetProperty("ResourcesToSpend").SetValue(spawner, finalMeat);
                InvokePrivate(roundManager, "BeginEnding", true);

                float completedReward = (float)agent.GetType()
                    .GetProperty("LastCompletedEpisodeReward").GetValue(agent);
                Assert.That(completedReward, Is.EqualTo(12.17f).Within(0.001f),
                    "the completed reward must include process + 10 + 0.01 * frozen final meat");
                Assert.That(roundManager.GetType().GetProperty("PlayerWon").GetValue(roundManager), Is.True);
                Assert.That(roundManager.GetType().GetProperty("AliveDinos").GetValue(roundManager), Is.Zero,
                    "the actual victory Ending path must clear remaining dinos after reward freezing");

                InvokePrivate(roundManager, "BeginEnding", true);
                yield return null;
                Assert.That(agent.GetType().GetProperty("LastCompletedEpisodeReward").GetValue(agent),
                    Is.EqualTo(12.17f).Within(0.001f),
                    "victory cleanup and duplicate Ending must not append rewards");
            }
            finally
            {
                UnityEngine.Object.Destroy(trainingRoot);
                if (addedKeyboard)
                {
                    InputSystem.RemoveDevice(testKeyboard);
                }
                if (addedMouse)
                {
                    InputSystem.RemoveDevice(testMouse);
                }
            }
        }

        [UnityTest]
        public IEnumerator TrainingRewardAgent_InfrastructureInterruptionHasNoTerminalOrMeatReward()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frameIndex = 0; frameIndex < 3; frameIndex++) yield return null;

            Component spawner = FindRuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
            GameObject trainingRoot = new("Reward Interrupted Agent");
            try
            {
                Component agent = trainingRoot.AddComponent(
                    RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingAgent"));
                spawner.GetType().GetProperty("ResourcesToSpend").SetValue(spawner, 10_000);
                yield return null;

                agent.GetType().GetMethod("InterruptEpisodeForInfrastructure").Invoke(agent, null);
                object terminalInfo = GetInheritedField(agent, "m_Info");
                Assert.That(agent.GetType().GetProperty("LastCompletedEpisodeReward").GetValue(agent), Is.Zero);
                Assert.That(GetFieldValue(terminalInfo, "reward"), Is.Zero,
                    "interrupted must not synthesize win/loss or final-meat reward");
                Assert.That(GetFieldValue(terminalInfo, "done"), Is.True);
                Assert.That(GetFieldValue(terminalInfo, "maxStepReached"), Is.True,
                    "the public interruption entry point must create an interrupted terminal");

                agent.GetType().GetMethod("InterruptEpisodeForInfrastructure").Invoke(agent, null);
                Assert.That(agent.GetType().GetProperty("LastCompletedEpisodeReward").GetValue(agent), Is.Zero,
                    "duplicate interruption must not append reward");
                Assert.That(GetFieldValue(terminalInfo, "reward"), Is.Zero);
            }
            finally
            {
                UnityEngine.Object.Destroy(trainingRoot);
            }
        }

        [Test]
        public void TrainingTask3_RunContextUsesStableSeedsAndPreservesSameProcessAcrossReloads()
        {
            Type contextType = RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingRunContext");
            MethodInfo configure = contextType.GetMethod("Configure", BindingFlags.Static | BindingFlags.Public);
            MethodInfo advance = contextType.GetMethod("AdvanceEpisode", BindingFlags.Static | BindingFlags.Public);
            Assert.That(configure, Is.Not.Null);
            Assert.That(advance, Is.Not.Null);

            configure.Invoke(null, new object[] { 7, 2, 103 });
            Assert.That(contextType.GetProperty("EnvironmentId").GetValue(null), Is.EqualTo(2));
            Assert.That(contextType.GetProperty("EpisodeIndex").GetValue(null), Is.EqualTo(0));
            Assert.That(contextType.GetProperty("EpisodeSeed").GetValue(null), Is.EqualTo(1106766084));
            Assert.That(contextType.GetProperty("ProcessGeneration").GetValue(null), Is.EqualTo(103));

            Assert.That(advance.Invoke(null, null), Is.EqualTo(1123543703));
            Assert.That(contextType.GetProperty("EpisodeIndex").GetValue(null), Is.EqualTo(1));
            configure.Invoke(null, new object[] { 7, 2, 103 });
            Assert.That(contextType.GetProperty("EpisodeIndex").GetValue(null), Is.EqualTo(1),
                "reloading the same process must not reset its episode index");

            configure.Invoke(null, new object[] { 7, 2, 104 });
            Assert.That(contextType.GetProperty("EpisodeIndex").GetValue(null), Is.EqualTo(0),
                "a new process generation starts a fresh episode sequence");
        }

        [Test]
        public void TrainingTask3_DecisionsAreImmediateThenEveryTwentyFiveAcademySteps()
        {
            GameObject root = new("Task 3 Decision Scheduler");
            try
            {
                Component scheduler = root.AddComponent(
                    RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingDecisionScheduler"));
                MethodInfo consume = scheduler.GetType().GetMethod(
                    "TryConsumeDecisionStep", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                Assert.That(consume, Is.Not.Null);
                Type stateType = RuntimeType("LlamAcademy.Dinos.RoundManagement.GameState");
                object setup = Enum.Parse(stateType, "Setup");
                object running = Enum.Parse(stateType, "Running");
                object ending = Enum.Parse(stateType, "Ending");

                Assert.That(consume.Invoke(scheduler, new[] { setup, (object)0 }), Is.False);
                Assert.That(consume.Invoke(scheduler, new[] { running, (object)0 }), Is.True,
                    "the first Running Academy pre-step requests immediately");
                Assert.That(consume.Invoke(scheduler, new[] { running, (object)0 }), Is.False);
                Assert.That(consume.Invoke(scheduler, new[] { running, (object)24 }), Is.False);
                Assert.That(consume.Invoke(scheduler, new[] { running, (object)25 }), Is.True);
                Assert.That(consume.Invoke(scheduler, new[] { running, (object)49 }), Is.False);
                Assert.That(consume.Invoke(scheduler, new[] { running, (object)50 }), Is.True);
                Assert.That(consume.Invoke(scheduler, new[] { ending, (object)51 }), Is.False);
                Assert.That(consume.Invoke(scheduler, new[] { running, (object)80 }), Is.True,
                    "a later Running transition begins with an immediate decision again");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        [UnityTest]
        public IEnumerator TrainingTask4_HumanModeWithoutExactFlagPreservesPresentationAndInput()
        {
            object originalArgumentsProvider = OverrideTrainingArguments(
                new[] { "game.exe", "--dino-training-extra", "--batchmode", "--nographics" });
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
                for (int frameIndex = 0; frameIndex < 5; frameIndex++) yield return null;

                Component bootstrap = FindRuntimeComponent("LlamAcademy.Dinos.Training.DinoTrainingBootstrap");
                Component runtimeUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
                Component cameraControl = FindRuntimeComponent("LlamAcademy.Dinos.Player.CameraControl");
                Component spawner = FindRuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
                Component visualization = FindRuntimeComponent("LlamAcademy.Dinos.Player.PlaceDinoVisualization");
                Assert.That(bootstrap, Is.Not.Null);
                Assert.That(bootstrap.GetType().GetProperty("IsTrainingMode")?.GetValue(bootstrap), Is.False);
                Assert.That(((Behaviour)runtimeUi).enabled, Is.True);
                UIDocument document = runtimeUi.GetComponent<UIDocument>();
                Assert.That(document.enabled, Is.True);
                Assert.That(document.rootVisualElement.Q("logo-container"), Is.Null,
                    "The Dino Attack logo belongs to MapSelect, not either gameplay map.");
                Assert.That(document.rootVisualElement.Q<Button>("start-button").resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(Camera.main, Is.Not.Null);
                Assert.That(Camera.main.enabled, Is.True);
                Assert.That(((Behaviour)cameraControl).enabled, Is.True);
                Assert.That(((Behaviour)spawner).enabled, Is.True);
                Assert.That(spawner.GetType().GetProperty("PlayerInputEnabled")?.GetValue(spawner), Is.True);
                Assert.That(((Behaviour)visualization).enabled, Is.True);
            }
            finally
            {
                RestoreTrainingArguments(originalArgumentsProvider);
            }
        }

        [UnityTest]
        public IEnumerator TrainingTask4_ExactFlagRunsFormalAgentAndOnlyShutsDownPresentation()
        {
            object originalArgumentsProvider = OverrideTrainingArguments(new[]
            {
                "game.exe", "--dino-training", "--dino-base-seed", "7",
                "--dino-environment-index", "2", "--dino-process-generation", "3"
            });
            float originalTimeScale = Time.timeScale;
            float originalCaptureDeltaTime = Time.captureDeltaTime;
            try
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!load.isDone) yield return null;

                Component roundManager = null;
                float deadline = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    roundManager = FindRuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
                    if (roundManager != null &&
                        roundManager.GetType().GetProperty("State")?.GetValue(roundManager)?.ToString() == "Running")
                    {
                        break;
                    }
                    yield return null;
                }

                for (int frameIndex = 0; frameIndex < 2; frameIndex++)
                {
                    yield return null;
                    Assert.That(
                        roundManager.GetType().GetProperty("State")?.GetValue(roundManager)?.ToString(),
                        Is.EqualTo("Running"),
                        $"formal training must remain Running after startup frame {frameIndex + 1}");
                }

                Transform formalRoot = GameObject.Find("Training/FormalTrainingEnvironment")?.transform;
                Component bootstrap = FindRuntimeComponent("LlamAcademy.Dinos.Training.DinoTrainingBootstrap");
                Component spawner = FindRuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
                Component enemy = FindRuntimeComponent("LlamAcademy.Dinos.Enemy.EnemyAIController");
                Assert.That(formalRoot, Is.Not.Null);
                Assert.That(bootstrap.GetType().GetProperty("IsTrainingMode")?.GetValue(bootstrap), Is.True);
                Assert.That(roundManager.GetType().GetProperty("State")?.GetValue(roundManager)?.ToString(), Is.EqualTo("Running"));
                Assert.That(((Behaviour)roundManager).enabled, Is.True);
                Assert.That(((Behaviour)spawner).enabled, Is.True);
                Assert.That(spawner.GetType().GetProperty("PlayerInputEnabled")?.GetValue(spawner), Is.False);
                Assert.That(((Behaviour)enemy).enabled, Is.True);
                Assert.That(UnityEngine.Object.FindObjectsByType<Component>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(component => component != null && component.GetType().FullName ==
                        "LlamAcademy.Dinos.Training.DinoTrainingScenarioRandomizer")
                    .All(randomizer => !((Behaviour)randomizer).enabled), Is.True,
                    "formal structured-set training must not add legacy frame-timed carving obstacles");
                Type defenderType = RuntimeType("LlamAcademy.Dinos.Enemy.Defender");
                Type graphAgentType = RuntimeType("Unity.Behavior.BehaviorGraphAgent");
                Type fixedGraphDriverType = RuntimeType(
                    "LlamAcademy.Dinos.Simulation.DinoFixedBehaviorGraphDriver");
                Type attackRadiusType = RuntimeType("LlamAcademy.Dinos.Unit.AttackRadius");
                Component[] defenders = UnityEngine.Object.FindObjectsByType<Component>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(component => component != null && component.GetType() == defenderType)
                    .ToArray();
                Component wallLayout = FindRuntimeComponent(
                    "LlamAcademy.Dinos.Enemy.Defense.SessionDefenseLayoutController");
                Component groundLayout = FindRuntimeComponent(
                    "LlamAcademy.Dinos.Enemy.Defense.SessionGroundDefenseController");
                object[] registeredWallGuards = ((IEnumerable)wallLayout.GetType()
                        .GetProperty("ActiveGuards")?.GetValue(wallLayout))
                    .Cast<object>().ToArray();
                object[] registeredGroundGuards = ((IEnumerable)groundLayout.GetType()
                        .GetProperty("ActiveGroundGuards")?.GetValue(groundLayout))
                    .Cast<object>().ToArray();
                int liveRegisteredWallGuards = registeredWallGuards.Count(guard =>
                    guard is UnityEngine.Object unityGuard && unityGuard != null);
                int liveRegisteredGroundGuards = registeredGroundGuards.Count(guard =>
                    guard is UnityEngine.Object unityGuard && unityGuard != null);
                Assert.That(registeredWallGuards, Has.Length.EqualTo(3));
                Assert.That(liveRegisteredWallGuards, Is.EqualTo(3));
                Assert.That(registeredGroundGuards, Has.Length.EqualTo(5));
                Assert.That(liveRegisteredGroundGuards, Is.EqualTo(5));
                Assert.That(defenders, Has.Length.EqualTo(8),
                    $"formal Dinos training must retain all three fixed and five ground guards; " +
                    $"wall registered/live={registeredWallGuards.Length}/{liveRegisteredWallGuards}, " +
                    $"ground registered/live={registeredGroundGuards.Length}/{liveRegisteredGroundGuards}, " +
                    $"found={string.Join(", ", defenders.Select(defender => defender.name))}");
                foreach (Component defender in defenders)
                {
                    Assert.That(((Behaviour)defender).enabled, Is.True);
                    Assert.That(defenderType.GetProperty("IsDead")?.GetValue(defender), Is.False);
                    Assert.That(defender.GetComponent(graphAgentType), Is.InstanceOf<Behaviour>());
                    Assert.That(((Behaviour)defender.GetComponent(graphAgentType)).enabled, Is.False,
                        $"{defender.name}'s package Update graph must remain disabled");
                    Assert.That(defender.GetComponent(fixedGraphDriverType), Is.InstanceOf<Behaviour>());
                    Assert.That(((Behaviour)defender.GetComponent(fixedGraphDriverType)).enabled, Is.True,
                        $"{defender.name}'s fixed-step graph driver must remain enabled");
                    Component attackRadius = defender.GetComponentInChildren(attackRadiusType, true);
                    Assert.That(attackRadius, Is.InstanceOf<Behaviour>());
                    Assert.That(((Behaviour)attackRadius).enabled, Is.True,
                        $"training presentation shutdown disabled {defender.name}'s target sensor");
                    Assert.That(attackRadius.GetComponent<Collider>()?.enabled, Is.True,
                        $"training presentation shutdown disabled {defender.name}'s sensor collider");
                }
                Assert.That(UnityEngine.Object.FindObjectsByType<DinoDeploymentZone>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .OrderBy(zone => zone.Id).Select(zone => zone.Id),
                    Is.EqualTo(Enum.GetValues(typeof(DeploymentZoneId))));
                Assert.That(UnityEngine.Object.FindObjectsByType<Collider>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Any(collider => collider.enabled), Is.True);

                Assert.That(UnityEngine.Object.FindObjectsByType<Camera>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).All(camera => !camera.enabled), Is.True);
                Renderer[] renderers = UnityEngine.Object.FindObjectsByType<Renderer>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                Assert.That(renderers.All(renderer => !renderer.enabled), Is.True,
                    $"enabled renderers: {string.Join(", ", renderers.Where(renderer => renderer.enabled).Select(renderer => renderer.name))}");
                Assert.That(UnityEngine.Object.FindObjectsByType<UIDocument>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).All(document => !document.enabled), Is.True);

                Component agent = formalRoot.GetComponent(RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingAgent"));
                Component sensorComponent = formalRoot.GetComponent(
                    RuntimeType("LlamAcademy.Dinos.Training.DinoStructuredObservationSensorComponent"));
                Component behaviorParameters = formalRoot.GetComponent(RuntimeType("Unity.MLAgents.Policies.BehaviorParameters"));
                Assert.That(((Behaviour)agent).enabled, Is.True);
                Assert.That(((Behaviour)sensorComponent).enabled, Is.True);
                Array sensors = (Array)sensorComponent.GetType().GetMethod("CreateSensors")?.Invoke(sensorComponent, null);
                Assert.That(sensors, Has.Length.EqualTo(6));
                object brain = behaviorParameters.GetType().GetProperty("BrainParameters")?.GetValue(behaviorParameters);
                object actionSpec = brain.GetType().GetProperty("ActionSpec")?.GetValue(brain);
                Assert.That(actionSpec.GetType().GetProperty("NumContinuousActions")?.GetValue(actionSpec), Is.EqualTo(4));
                Assert.That(brain.GetType().GetField("VectorObservationSize")?.GetValue(brain), Is.EqualTo(0));

                Type context = RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingRunContext");
                Assert.That(context.GetProperty("BaseSeed")?.GetValue(null), Is.EqualTo(7));
                Assert.That(context.GetProperty("EnvironmentId")?.GetValue(null), Is.EqualTo(2));
                Assert.That(context.GetProperty("ProcessGeneration")?.GetValue(null), Is.EqualTo(3));
                Assert.That(context.GetProperty("EpisodeSeed")?.GetValue(null), Is.EqualTo(1106766084));

                Time.timeScale = 5f;
                Time.captureFramerate = 0;
                Assert.That(Time.captureDeltaTime, Is.Zero,
                    "the test must reproduce EngineConfigurationChannel's captureFramerate=0 override");
                int repairsBeforeOverride = (int)bootstrap.GetType()
                    .GetProperty("TrainingClockRepairCount")?.GetValue(bootstrap);
                for (int frameIndex = 0; frameIndex < 4; frameIndex++)
                {
                    yield return null;
                }
                Assert.That(Time.timeScale, Is.EqualTo(5f),
                    "the bootstrap must not replace the externally configured time scale");
                Assert.That(Time.captureFramerate, Is.EqualTo(250));
                Assert.That(Time.captureDeltaTime, Is.EqualTo(Time.fixedDeltaTime / 5f)
                    .Within(0.0000001f),
                    "every Academy step must repair captureDeltaTime from the active external time scale");
                Assert.That(
                    (int)bootstrap.GetType().GetProperty("TrainingClockRepairCount")?.GetValue(bootstrap),
                    Is.EqualTo(repairsBeforeOverride + 1),
                    "the capture clock must remain stable after one repair, not be rewritten every step");
            }
            finally
            {
                Component activeAgent = FindRuntimeComponent("LlamAcademy.Dinos.Training.DinoTrainingAgent");
                if (activeAgent is Behaviour activeAgentBehaviour && activeAgentBehaviour.enabled)
                {
                    activeAgent.GetType().GetMethod("EpisodeInterrupted")?.Invoke(activeAgent, null);
                    activeAgentBehaviour.enabled = false;
                }
                Time.timeScale = originalTimeScale;
                Time.captureDeltaTime = originalCaptureDeltaTime;
                RestoreTrainingArguments(originalArgumentsProvider);
            }
        }

        [UnityTest]
        public IEnumerator VisualPolish_HumanAndTrainingPresentationModesDoNotChangeGameplayRoots()
        {
            object originalArgumentsProvider = OverrideTrainingArguments(
                new[] { "game.exe", "--dino-training-extra" });
            float originalTimeScale = Time.timeScale;
            try
            {
                AsyncOperation humanLoad = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!humanLoad.isDone) yield return null;
                for (int frameIndex = 0; frameIndex < 5; frameIndex++) yield return null;

                Transform humanBattlefield = GameObject.Find("World/Open Tropical Battlefield")?.transform;
                Transform humanArt = humanBattlefield?.Find("Art Pass");
                Assert.That(humanArt, Is.Not.Null);
                Assert.That(humanArt.Find("Lighting & Atmosphere"), Is.Not.Null);
                Assert.That(humanArt.Find("Terrain Dressing"), Is.Not.Null);
                Assert.That(humanArt.Find("Canyon Rim/Distant Scenery"), Is.Not.Null);
                Assert.That(humanArt.GetComponentsInChildren<Renderer>(true)
                    .Any(renderer => renderer.gameObject.activeInHierarchy && renderer.enabled), Is.True);
                Assert.That(UnityEngine.Object.FindObjectsByType<Light>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).Any(light => light.enabled), Is.True);
                Component[] humanVolumes = UnityEngine.Object.FindObjectsByType<Component>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(component => component != null &&
                        component.GetType().FullName == "UnityEngine.Rendering.Volume")
                    .ToArray();
                Assert.That(humanVolumes, Is.Not.Empty);
                Assert.That(humanVolumes.Cast<Behaviour>().Any(volume => volume.enabled), Is.True);

                OverrideTrainingArguments(new[]
                {
                    "game.exe", "--dino-training", "--dino-base-seed", "7",
                    "--dino-environment-index", "2", "--dino-process-generation", "3"
                });
                AsyncOperation trainingLoad = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!trainingLoad.isDone) yield return null;

                Component roundManager = null;
                float deadline = Time.realtimeSinceStartup + 3f;
                while (Time.realtimeSinceStartup < deadline)
                {
                    roundManager = FindRuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
                    if (roundManager != null &&
                        roundManager.GetType().GetProperty("State")?.GetValue(roundManager)?.ToString() == "Running")
                    {
                        break;
                    }
                    yield return null;
                }

                Transform trainingBattlefield = GameObject.Find("World/Open Tropical Battlefield")?.transform;
                Transform trainingArt = trainingBattlefield?.Find("Art Pass");
                Transform formalRoot = GameObject.Find("Training/FormalTrainingEnvironment")?.transform;
                Assert.That(trainingArt, Is.Not.Null);
                Assert.That(formalRoot, Is.Not.Null);
                Assert.That(trainingArt.GetComponentsInChildren<Renderer>(true).All(renderer => !renderer.enabled), Is.True);
                Assert.That(UnityEngine.Object.FindObjectsByType<Light>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None).All(light => !light.enabled), Is.True);
                Component[] trainingVolumes = UnityEngine.Object.FindObjectsByType<Component>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(component => component != null &&
                        component.GetType().FullName == "UnityEngine.Rendering.Volume")
                    .ToArray();
                Assert.That(trainingVolumes, Is.Not.Empty);
                Assert.That(trainingVolumes.Cast<Behaviour>().All(volume => !volume.enabled), Is.True);

                Transform deploymentZones = trainingBattlefield.Find("Deployment Zones");
                Assert.That(deploymentZones.gameObject.activeInHierarchy, Is.True);
                DinoDeploymentZone[] activeZones = deploymentZones.GetComponentsInChildren<DinoDeploymentZone>(true);
                Assert.That(activeZones, Has.Length.EqualTo(5));
                Assert.That(activeZones.All(zone => zone.gameObject.activeInHierarchy && zone.enabled), Is.True);
                Transform terrainSurfaces = trainingBattlefield.Find("Terrain Surfaces");
                TerrainSurfaceVolume[] surfaceVolumes = terrainSurfaces.GetComponentsInChildren<TerrainSurfaceVolume>(true);
                Assert.That(surfaceVolumes, Has.Length.EqualTo(5));
                Assert.That(surfaceVolumes.All(volume => volume.gameObject.activeInHierarchy && volume.enabled &&
                    volume.GetComponents<Collider>().Any(collider => collider.enabled && collider.isTrigger)), Is.True,
                    "all five frozen terrain triggers must remain active in exact training mode");
                Component[] navMeshSurfaces = UnityEngine.Object.FindObjectsByType<Component>(
                        FindObjectsInactive.Include, FindObjectsSortMode.None)
                    .Where(component => component != null &&
                        component.GetType().FullName == "Unity.AI.Navigation.NavMeshSurface")
                    .ToArray();
                Assert.That(navMeshSurfaces, Is.Not.Empty);
                Assert.That(navMeshSurfaces.Cast<Behaviour>().All(surface => surface.enabled), Is.True);
                Assert.That(((Behaviour)roundManager).enabled, Is.True);
                ICollection sessionHouses = roundManager.GetType().GetProperty("SessionHouses")
                    ?.GetValue(roundManager) as ICollection;
                Assert.That(sessionHouses, Is.Not.Null);
                Assert.That(sessionHouses.Count, Is.EqualTo(8));
                Component[] wallSlots = trainingBattlefield.Find("Session Defense Layout/Wall Spawn Slots")
                    .GetComponentsInChildren<Component>(true).Where(component => component != null &&
                        component.GetType().FullName == "LlamAcademy.Dinos.Enemy.Defense.WallSpawnSlot").ToArray();
                Assert.That(wallSlots, Has.Length.EqualTo(6));
                Assert.That(wallSlots.All(slot => slot.gameObject.activeInHierarchy), Is.True);
                Component[] activeWalls = trainingBattlefield.Find("Session Defense Layout")
                    .GetComponentsInChildren<Component>(true).Where(component => component != null &&
                        component.GetType().FullName == "LlamAcademy.Dinos.Unit.Wall" && component.gameObject.activeInHierarchy)
                    .ToArray();
                Assert.That(activeWalls, Has.Length.EqualTo(3), "the selected frozen defense preset must spawn three walls");
                Assert.That((int)roundManager.GetType().GetProperty("AliveDefenderCount")?.GetValue(roundManager),
                    Is.EqualTo(8), "three wall guards plus 3 Archer + 2 Mage ground guards must remain active");

                Component agent = formalRoot.GetComponent(RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingAgent"));
                Component sensor = formalRoot.GetComponent(
                    RuntimeType("LlamAcademy.Dinos.Training.DinoStructuredObservationSensorComponent"));
                Assert.That(((Behaviour)agent).enabled, Is.True);
                Assert.That(((Behaviour)sensor).enabled, Is.True);
                Array streams = (Array)sensor.GetType().GetMethod("CreateSensors")?.Invoke(sensor, null);
                Assert.That(streams, Has.Length.EqualTo(5));
            }
            finally
            {
                Component activeAgent = FindRuntimeComponent("LlamAcademy.Dinos.Training.DinoTrainingAgent");
                if (activeAgent is Behaviour activeAgentBehaviour && activeAgentBehaviour.enabled)
                {
                    activeAgent.GetType().GetMethod("EpisodeInterrupted")?.Invoke(activeAgent, null);
                    activeAgentBehaviour.enabled = false;
                }
                Time.timeScale = originalTimeScale;
                RestoreTrainingArguments(originalArgumentsProvider);
            }
        }

        [UnityTest]
        public IEnumerator SceneDeploymentZones_MapRelativeCoordinatesAndRejectNeutralGaps()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 3; frame++) yield return null;

            DinoDeploymentService service = UnityEngine.Object.FindFirstObjectByType<DinoDeploymentService>();
            DinoDeploymentZone[] zones = UnityEngine.Object
                .FindObjectsByType<DinoDeploymentZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .OrderBy(zone => zone.Id)
                .ToArray();
            Assert.That(service, Is.Not.Null);
            Assert.That(service.ValidateZoneConfiguration(), Is.True);
            Assert.That(zones, Has.Length.EqualTo(5));

            Vector2[] relativeSamples =
            {
                new(0.05f, 0.05f),
                new(0.5f, 0.5f),
                new(0.25f, 0.75f),
                new(0.95f, 0.95f)
            };
            int gameplayGroundMask = 1 << LayerMask.NameToLayer("Grass");
            foreach (DinoDeploymentZone zone in zones)
            foreach (Vector2 relative in relativeSamples)
            {
                Vector3 worldPoint = ResolveZonePoint(zone, relative);
                Assert.That(zone.Contains(worldPoint), Is.True, $"{zone.Id}/{relative} must remain inside its quadrilateral");
                Assert.That(service.TryGetZone(worldPoint, out DinoDeploymentZone resolved), Is.True);
                Assert.That(resolved, Is.SameAs(zone), $"{zone.Id}/{relative} must resolve to the selected region");
                Assert.That(Physics.Raycast(worldPoint + Vector3.up * 20f, Vector3.down, out RaycastHit ground,
                    40f, gameplayGroundMask, QueryTriggerInteraction.Ignore), Is.True,
                    $"{zone.Id}/{relative} must project onto real ground");
                Assert.That(NavMesh.SamplePosition(ground.point, out _, 2f, NavMesh.AllAreas), Is.True,
                    $"{zone.Id}/{relative} must sample the runtime NavMesh near {ground.point}");
            }

            Vector3[] neutralGaps =
            {
                new(-26f, 0f, -21f),
                new(-11.5f, 0f, -19f),
                new(8.5f, 0f, -19f),
                new(22.5f, 0f, -23f),
                new(0f, 0f, 29f)
            };
            foreach (Vector3 gap in neutralGaps)
            {
                Assert.That(service.TryGetZone(gap, out _), Is.False, $"neutral gap {gap} must remain unavailable");
            }

            Transform plaza = GameObject.Find("Central Plaza")?.transform;
            Assert.That(plaza, Is.Not.Null);
            Assert.That(service.TryGetZone(plaza.position, out _), Is.False, "the village plaza is not a deployment region");
        }

        [UnityTest]
        public IEnumerator SceneCamera_StatePathsClampZoomDragAndRestoreExactHome()
        {
            Component control = FindRuntimeComponent("LlamAcademy.Dinos.Player.CameraControl");
            if (control == null)
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
                yield return null;
                control = FindRuntimeComponent("LlamAcademy.Dinos.Player.CameraControl");
            }

            Assert.That(control, Is.Not.Null);
            Component camera = control.GetComponent(RuntimeType("Unity.Cinemachine.CinemachineCamera"));
            Component follow = control.GetComponent(RuntimeType("Unity.Cinemachine.CinemachineFollow"));
            Transform target = (Transform)camera.GetType().GetProperty("Follow").GetValue(camera);
            FieldInfo followOffset = follow.GetType().GetField("FollowOffset");
            Vector3 homeTarget = target.position;
            Assert.That(followOffset, Is.Not.Null);
            Vector3 homeOffset = (Vector3)followOffset.GetValue(follow);
            Quaternion rotation = control.transform.rotation;

            InvokeCameraControl(control, "ApplyPan", new Vector3(1000f, 0f, -1000f));
            Assert.That(target.position.x, Is.EqualTo(48.5f).Within(0.001f));
            Assert.That(target.position.z, Is.EqualTo(-53f).Within(0.001f));

            InvokeCameraControl(control, "ApplyZoom", 100f);
            Assert.That(((Vector3)followOffset.GetValue(follow)).magnitude,
                Is.EqualTo(homeOffset.magnitude * 0.45f).Within(0.001f));
            InvokeCameraControl(control, "ApplyZoom", -100f);
            Assert.That(((Vector3)followOffset.GetValue(follow)).magnitude,
                Is.EqualTo(homeOffset.magnitude * 1.35f).Within(0.001f));

            InvokeCameraControl(control, "ResetHome");
            InvokeCameraControl(control, "ApplyMiddleDragWorldDelta", new Vector3(3f, 0f, 5f));
            Assert.That(Vector3.Distance(target.position, homeTarget + new Vector3(3f, 0f, 5f)), Is.LessThan(0.001f));
            Assert.That(control.transform.rotation, Is.EqualTo(rotation));

            InvokeCameraControl(control, "ResetHome");
            Assert.That(Vector3.Distance(target.position, homeTarget), Is.LessThan(0.001f));
            Assert.That(Vector3.Distance((Vector3)followOffset.GetValue(follow), homeOffset), Is.LessThan(0.001f));
            Assert.That(control.transform.rotation, Is.EqualTo(rotation));
            yield return null;
        }

        [UnityTest]
        public IEnumerator SceneCamera_HomeFramesIslandZonesAndPlazaAtFourApprovedAspects()
        {
            Component control = FindRuntimeComponent("LlamAcademy.Dinos.Player.CameraControl");
            if (control == null)
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
                yield return null;
                control = FindRuntimeComponent("LlamAcademy.Dinos.Player.CameraControl");
            }

            InvokeCameraControl(control, "ResetHome");
            for (int frame = 0; frame < 20; frame++) yield return null;
            Camera output = Camera.main;
            Assert.That(output, Is.Not.Null);
            Assert.That(output.orthographic, Is.False);
            Assert.That(output.transform.eulerAngles.x, Is.EqualTo(55f).Within(0.5f));
            BoxCollider bounds = (BoxCollider)control.GetType()
                .GetField("WorldBounds", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(control);
            Component[] zones = UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Map.DinoDeploymentZone")
                .ToArray();
            Assert.That(zones.Length, Is.EqualTo(5));
            Transform plaza = GameObject.Find("Central Plaza")?.transform;
            Assert.That(plaza, Is.Not.Null);

            Vector3[] requiredPoints =
            {
                new(bounds.bounds.min.x, 0f, bounds.bounds.min.z),
                new(bounds.bounds.min.x, 0f, bounds.bounds.max.z),
                new(bounds.bounds.max.x, 0f, bounds.bounds.min.z),
                new(bounds.bounds.max.x, 0f, bounds.bounds.max.z),
                plaza.position
            };
            requiredPoints = requiredPoints.Concat(zones
                .Cast<DinoDeploymentZone>()
                .Select(zone => ResolveZonePoint(zone, new Vector2(0.5f, 0.5f))))
                .ToArray();
            float originalAspect = output.aspect;
            float[] aspects = { 1920f / 1080f, 1920f / 1200f, 2560f / 1080f, 1280f / 720f };
            foreach (float aspect in aspects)
            {
                output.aspect = aspect;
                foreach (Vector3 point in requiredPoints)
                {
                    Vector3 viewport = output.WorldToViewportPoint(point);
                    Assert.That(viewport.z, Is.GreaterThan(0f), $"{point} is behind the camera at aspect {aspect:F4}");
                    Assert.That(viewport.x, Is.InRange(0.05f, 0.95f), $"{point} x={viewport.x:F4} at aspect {aspect:F4}");
                    Assert.That(viewport.y, Is.InRange(0.15f, 0.95f), $"{point} y={viewport.y:F4} at aspect {aspect:F4}");
                }
            }

            output.aspect = originalAspect;
            InvokeCameraControl(control, "ApplyZoom", 100f);
            for (int frame = 0; frame < 20; frame++) yield return null;
            Vector3 unitBottom = output.WorldToViewportPoint(plaza.position);
            Vector3 unitTop = output.WorldToViewportPoint(plaza.position + Vector3.up * 2f);
            Assert.That(Mathf.Abs(unitTop.y - unitBottom.y), Is.GreaterThan(0.01f),
                "a two-metre unit must remain legible at maximum zoom");
            Assert.That(FindRuntimeComponent("UnityEngine.UIElements.UIDocument"), Is.Not.Null,
                "the interaction HUD must remain active while zooming");
            InvokeCameraControl(control, "ResetHome");
        }

        [UnityTest]
        public IEnumerator SceneCamera_ConsumesRealKeyboardMouseWheelMiddleDragAndHomeStates()
        {
            InputTestFixture input = new();
            input.Setup();
            try
            {
                Keyboard keyboard = InputSystem.AddDevice<Keyboard>();
                Mouse mouse = InputSystem.AddDevice<Mouse>();
                Component control = FindRuntimeComponent("LlamAcademy.Dinos.Player.CameraControl");
                if (control == null)
                {
                    AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                    while (!load.isDone) yield return null;
                    yield return null;
                    control = FindRuntimeComponent("LlamAcademy.Dinos.Player.CameraControl");
                }

                Component camera = control.GetComponent(RuntimeType("Unity.Cinemachine.CinemachineCamera"));
                Component follow = control.GetComponent(RuntimeType("Unity.Cinemachine.CinemachineFollow"));
                Transform target = (Transform)camera.GetType().GetProperty("Follow").GetValue(camera);
                FieldInfo followOffset = follow.GetType().GetField("FollowOffset");
                InvokeCameraControl(control, "ResetHome");
                Vector3 homeTarget = target.position;
                Vector3 homeOffset = (Vector3)followOffset.GetValue(follow);
                input.Set(mouse.position, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));

                input.Press(keyboard.upArrowKey);
                yield return null;
                input.Release(keyboard.upArrowKey);
                Assert.That(target.position.z, Is.LessThan(homeTarget.z), "Up Arrow must pan toward screen-up/world-back.");

                input.Set(mouse.scroll, new Vector2(0f, 120f));
                yield return null;
                Assert.That(((Vector3)followOffset.GetValue(follow)).magnitude, Is.LessThan(homeOffset.magnitude));

                input.Set(mouse.position, new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
                input.Press(mouse.middleButton);
                yield return null;
                Vector3 dragStart = target.position;
                Quaternion rotationBeforeDrag = control.transform.rotation;
                input.Set(mouse.position, new Vector2(Screen.width * 0.6f, Screen.height * 0.5f));
                yield return null;
                input.Release(mouse.middleButton);
                Assert.That(target.position, Is.Not.EqualTo(dragStart));
                Assert.That(Quaternion.Angle(control.transform.rotation, rotationBeforeDrag), Is.LessThan(0.25f),
                    "middle drag must translate on the ground plane rather than rotate the camera");

                input.Press(keyboard.homeKey);
                yield return null;
                input.Release(keyboard.homeKey);
                Assert.That(Vector3.Distance(target.position, homeTarget), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance((Vector3)followOffset.GetValue(follow), homeOffset), Is.LessThan(0.001f));
                for (int frame = 0; frame < 20; frame++) yield return null;
                Assert.That(control.transform.eulerAngles.x, Is.EqualTo(55f).Within(0.5f));
            }
            finally
            {
                input.TearDown();
            }
        }

        [UnityTest]
        public IEnumerator SceneCamera_RealEdgePanUsesOutputCameraPixelRect()
        {
            InputTestFixture input = new();
            input.Setup();
            Component control = null;
            Func<bool> originalFocusProvider = null;
            Func<bool> injectedFocusProvider = null;
            bool originalFocusResult = false;
            try
            {
                InputSystem.AddDevice<Keyboard>();
                Mouse mouse = InputSystem.AddDevice<Mouse>();
                control = FindRuntimeComponent("LlamAcademy.Dinos.Player.CameraControl");
                if (control == null)
                {
                    AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                    while (!load.isDone) yield return null;
                    yield return null;
                    control = FindRuntimeComponent("LlamAcademy.Dinos.Player.CameraControl");
                }

                originalFocusProvider = GetCameraFocusProvider(control);
                originalFocusResult = originalFocusProvider();
                injectedFocusProvider = SetCameraFocusProvider(control, true);
                InvokeCameraControl(control, "ResetHome");
                Transform target = CameraFollowTarget(control);
                Vector3 start = target.position;
                Rect pixelRect = Camera.main.pixelRect;
                input.Set(mouse.position, new Vector2(pixelRect.xMin + 1f, pixelRect.center.y));

                for (int frame = 0; frame < 3; frame++) yield return null;

                Assert.That(target.position.x, Is.GreaterThan(start.x),
                    "real mouse state inside the left edge of OutputCamera.pixelRect must pan world-right");
            }
            finally
            {
                try
                {
                    if (control != null && originalFocusProvider != null)
                    {
                        RestoreCameraFocusProvider(control, originalFocusProvider, originalFocusResult);
                    }
                }
                finally
                {
                    injectedFocusProvider = null;
                    originalFocusProvider = null;
                    control = null;
                    input.TearDown();
                    Assert.That(injectedFocusProvider, Is.Null);
                    Assert.That(originalFocusProvider, Is.Null);
                    Assert.That(control, Is.Null);
                }
            }
        }

        [UnityTest]
        public IEnumerator SceneCamera_OutsideOutputCameraPixelRectSuppressesWheelAndMiddleDrag()
        {
            InputTestFixture input = new();
            input.Setup();
            Component control = null;
            Func<bool> originalFocusProvider = null;
            Func<bool> injectedFocusProvider = null;
            bool originalFocusResult = false;
            try
            {
                InputSystem.AddDevice<Keyboard>();
                Mouse mouse = InputSystem.AddDevice<Mouse>();
                control = FindRuntimeComponent("LlamAcademy.Dinos.Player.CameraControl");
                if (control == null)
                {
                    AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                    while (!load.isDone) yield return null;
                    yield return null;
                    control = FindRuntimeComponent("LlamAcademy.Dinos.Player.CameraControl");
                }

                originalFocusProvider = GetCameraFocusProvider(control);
                originalFocusResult = originalFocusProvider();
                injectedFocusProvider = SetCameraFocusProvider(control, true);
                InvokeCameraControl(control, "ResetHome");
                Transform target = CameraFollowTarget(control);
                Component follow = control.GetComponent(RuntimeType("Unity.Cinemachine.CinemachineFollow"));
                FieldInfo followOffset = follow.GetType().GetField("FollowOffset");
                Vector3 homeTarget = target.position;
                Vector3 homeOffset = (Vector3)followOffset.GetValue(follow);
                Rect pixelRect = Camera.main.pixelRect;
                Vector2 outside = new(pixelRect.xMax, pixelRect.center.y);

                input.Set(mouse.position, outside);
                input.Set(mouse.scroll, new Vector2(0f, 120f));
                yield return null;
                input.Set(mouse.scroll, Vector2.zero);
                Assert.That(Vector3.Distance((Vector3)followOffset.GetValue(follow), homeOffset), Is.LessThan(0.001f),
                    "wheel input at the exclusive pixelRect upper bound must be ignored");

                input.Press(mouse.middleButton);
                yield return null;
                input.Set(mouse.position, outside + Vector2.right * 80f);
                yield return null;
                input.Release(mouse.middleButton);
                yield return null;
                Assert.That(Vector3.Distance(target.position, homeTarget), Is.LessThan(0.001f),
                    "middle drag originating outside OutputCamera.pixelRect must be ignored");
            }
            finally
            {
                try
                {
                    if (control != null && originalFocusProvider != null)
                    {
                        RestoreCameraFocusProvider(control, originalFocusProvider, originalFocusResult);
                    }
                }
                finally
                {
                    injectedFocusProvider = null;
                    originalFocusProvider = null;
                    control = null;
                    input.TearDown();
                    Assert.That(injectedFocusProvider, Is.Null);
                    Assert.That(originalFocusProvider, Is.Null);
                    Assert.That(control, Is.Null);
                }
            }
        }

        [UnityTest]
        public IEnumerator SceneCamera_UnfocusedPointerInputIsSuppressedAndActiveDragIsCancelled()
        {
            InputTestFixture input = new();
            input.Setup();
            Component control = null;
            Func<bool> originalFocusProvider = null;
            Func<bool> injectedFocusProvider = null;
            bool originalFocusResult = false;
            try
            {
                InputSystem.AddDevice<Keyboard>();
                Mouse mouse = InputSystem.AddDevice<Mouse>();
                control = FindRuntimeComponent("LlamAcademy.Dinos.Player.CameraControl");
                if (control == null)
                {
                    AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                    while (!load.isDone) yield return null;
                    yield return null;
                    control = FindRuntimeComponent("LlamAcademy.Dinos.Player.CameraControl");
                }

                originalFocusProvider = GetCameraFocusProvider(control);
                originalFocusResult = originalFocusProvider();
                injectedFocusProvider = SetCameraFocusProvider(control, true);
                InvokeCameraControl(control, "ResetHome");
                Transform target = CameraFollowTarget(control);
                Component follow = control.GetComponent(RuntimeType("Unity.Cinemachine.CinemachineFollow"));
                FieldInfo followOffset = follow.GetType().GetField("FollowOffset");
                Vector3 homeTarget = target.position;
                Vector3 homeOffset = (Vector3)followOffset.GetValue(follow);
                Rect pixelRect = Camera.main.pixelRect;
                Vector2 center = pixelRect.center;

                injectedFocusProvider = SetCameraFocusProvider(control, false);
                input.Set(mouse.position, new Vector2(pixelRect.xMin + 1f, center.y));
                input.Set(mouse.scroll, new Vector2(0f, 120f));
                input.Press(mouse.middleButton);
                yield return null;
                input.Set(mouse.scroll, Vector2.zero);
                input.Set(mouse.position, center + Vector2.right * 80f);
                yield return null;
                Assert.That(Vector3.Distance(target.position, homeTarget), Is.LessThan(0.001f));
                Assert.That(Vector3.Distance((Vector3)followOffset.GetValue(follow), homeOffset), Is.LessThan(0.001f));
                input.Release(mouse.middleButton);
                yield return null;

                injectedFocusProvider = SetCameraFocusProvider(control, true);
                input.Set(mouse.position, center);
                input.Press(mouse.middleButton);
                yield return null;
                input.Set(mouse.position, center + Vector2.right * 40f);
                yield return null;
                Assert.That(Vector3.Distance(target.position, homeTarget), Is.GreaterThan(0.001f),
                    "focused in-viewport middle drag must begin normally");

                injectedFocusProvider = SetCameraFocusProvider(control, false);
                Vector3 positionAtFocusLoss = target.position;
                input.Set(mouse.position, center + Vector2.right * 100f);
                yield return null;
                Assert.That(Vector3.Distance(target.position, positionAtFocusLoss), Is.LessThan(0.001f),
                    "losing focus must freeze and cancel an active drag");

                injectedFocusProvider = SetCameraFocusProvider(control, true);
                input.Set(mouse.position, center - Vector2.right * 100f);
                yield return null;
                Assert.That(Vector3.Distance(target.position, positionAtFocusLoss), Is.LessThan(0.001f),
                    "re-entering while middle remains held must not resume from a stale drag origin");
                input.Release(mouse.middleButton);
                yield return null;
            }
            finally
            {
                try
                {
                    if (control != null && originalFocusProvider != null)
                    {
                        RestoreCameraFocusProvider(control, originalFocusProvider, originalFocusResult);
                    }
                }
                finally
                {
                    injectedFocusProvider = null;
                    originalFocusProvider = null;
                    control = null;
                    input.TearDown();
                    Assert.That(injectedFocusProvider, Is.Null);
                    Assert.That(originalFocusProvider, Is.Null);
                    Assert.That(control, Is.Null);
                }
            }
        }

        [UnityTest]
        public IEnumerator HistoricalTrainingScene_FirstTurnSpawnsThroughExplicitGuardedFallback()
        {
            LoadEditorSceneInPlayMode("Assets/LlamAcademy/Dinos/Scenes/DinoAttackTraining.unity");
            yield return null;
            Component ai = FindRuntimeComponent("LlamAcademy.Dinos.Enemy.EnemyAIController");
            Assert.That(ai, Is.Not.Null);
            Assert.That(FindRuntimeComponent("LlamAcademy.Dinos.Map.VillageDefenseLayoutController"), Is.Null);
            Assert.That((bool)ai.GetType().GetField("AllowHistoricalTriangulationFallback", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(ai), Is.True);
            for (int frame = 0; frame < 30; frame++) yield return null;
            FieldInfo defendersField = ai.GetType().GetField("Defenders", BindingFlags.Instance | BindingFlags.NonPublic);
            int defenderCount = 0;
            foreach (object entry in (IEnumerable)defendersField.GetValue(ai))
                defenderCount += ((IEnumerable)entry.GetType().GetProperty("Value").GetValue(entry)).Cast<object>().Count();
            Assert.That(defenderCount, Is.GreaterThan(0));
            Assert.That((int)ai.GetType().GetProperty("HistoricalFallbackSpawnCount").GetValue(ai), Is.GreaterThan(0));
            LoadEditorSceneInPlayMode("Assets/LlamAcademy/Dinos/Scenes/Dinos.unity");
            yield return null;
        }

        [Serializable]
        private sealed class TraversalMeasurement
        {
            public string Prefab;
            public string Surface;
            public int Frames;
            public float Seconds;
            public float PathLength;
            public float ActualRatio;
            public float ExpectedRatio;
            public float ErrorPercent;
        }

        [UnityTest]
        public IEnumerator ThreeGameplayPrefabs_MeasureRealEqualLengthTraversalAcrossSceneSurfaceVolumes()
        {
            Time.timeScale = 1f;
            Component layout = FindRuntimeComponent("LlamAcademy.Dinos.Map.VillageDefenseLayoutController");
            if (layout == null)
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
                yield return null;
                layout = FindRuntimeComponent("LlamAcademy.Dinos.Map.VillageDefenseLayoutController");
            }
            for (int frame = 0; frame < 5; frame++) yield return null;
            ((Behaviour)layout).enabled = false; // Freeze round/death-only rebuilds while timing identical paths.

            string[] prefabPaths =
            {
                "Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor_New.prefab",
                "Assets/LlamAcademy/Dinos/Dinos/Pachycephalosaurus/Pachycephalosaurus_New.prefab",
                "Assets/LlamAcademy/Dinos/Dinos/TRex/Trex_New Variant.prefab"
            };
            TerrainSurfaceVolume[] volumes = UnityEngine.Object.FindObjectsByType<TerrainSurfaceVolume>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Where(volume => volume.transform.IsChildOf(GameObject.Find("World/Open Tropical Battlefield/Terrain Surfaces").transform))
                .ToArray();
            Assert.That(volumes.Select(volume => volume.Surface).Distinct().Count(), Is.EqualTo(5));
            List<TraversalMeasurement> measurements = new();

            foreach (string prefabPath in prefabPaths)
            {
                GameObject prefab = LoadAssetAtPath(prefabPath) as GameObject;
                Assert.That(prefab, Is.Not.Null, prefabPath);
                Dictionary<TerrainSurfaceKind, TraversalMeasurement> perSurface = new();
                foreach (TerrainSurfaceKind surface in Enum.GetValues(typeof(TerrainSurfaceKind)))
                {
                    TerrainSurfaceVolume volume = volumes.Single(candidate => candidate.Surface == surface);
                    TraversalMeasurement measurement = null;
                    yield return MeasureRealTraversal(prefab, volume, value => measurement = value);
                    Assert.That(measurement, Is.Not.Null);
                    measurement.Prefab = prefab.name;
                    measurement.Surface = surface.ToString();
                    perSurface.Add(surface, measurement);
                    measurements.Add(measurement);
                }

                float grassSeconds = perSurface[TerrainSurfaceKind.Grass].Seconds;
                foreach (KeyValuePair<TerrainSurfaceKind, TraversalMeasurement> pair in perSurface)
                {
                    TraversalMeasurement measurement = pair.Value;
                    measurement.ActualRatio = measurement.Seconds / grassSeconds;
                    measurement.ExpectedRatio = 1f / TerrainSpeedProfile.GetMultiplier(pair.Key);
                    measurement.ErrorPercent = Mathf.Abs(measurement.ActualRatio - measurement.ExpectedRatio) /
                                               measurement.ExpectedRatio * 100f;
                    Assert.That(measurement.ErrorPercent, Is.LessThanOrEqualTo(5f),
                        $"{prefab.name}/{pair.Key}: actual={measurement.ActualRatio:F4}, expected={measurement.ExpectedRatio:F4}, " +
                        $"frames={measurement.Frames}, seconds={measurement.Seconds:F4}");
                }
            }

            foreach (TraversalMeasurement measurement in measurements)
                Debug.Log("TASK5_TRAVERSAL " + JsonUtility.ToJson(measurement));
            ((Behaviour)layout).enabled = true;
        }

        private static IEnumerator MeasureRealTraversal(GameObject prefab, TerrainSurfaceVolume volume,
            Action<TraversalMeasurement> completed)
        {
            NavMeshAgent sourceAgent = prefab.GetComponent<NavMeshAgent>();
            Assert.That(sourceAgent, Is.Not.Null);
            Assert.That(TryFindEqualLengthRoute(volume, sourceAgent.agentTypeID, out Vector3 start, out Vector3 goal, out float pathLength),
                Is.True, $"No controlled four-metre route inside {volume.Surface} for {prefab.name}");
            GameObject instance = UnityEngine.Object.Instantiate(prefab, start, Quaternion.identity);
            instance.name = $"TraversalProbe_{prefab.name}_{volume.Surface}";
            DinoTerrainSpeedController speedController = instance.GetComponent<DinoTerrainSpeedController>();
            Assert.That(speedController, Is.Not.Null, $"{prefab.name} must carry the gameplay terrain controller");
            foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                if (behaviour != speedController) behaviour.enabled = false;
            foreach (Animator animator in instance.GetComponentsInChildren<Animator>(true)) animator.enabled = false;
            Rigidbody rigidbody = instance.GetComponent<Rigidbody>();
            if (rigidbody != null) rigidbody.isKinematic = true;
            NavMeshAgent agent = instance.GetComponent<NavMeshAgent>();
            agent.enabled = true;
            agent.acceleration = 10000f;
            agent.angularSpeed = 10000f;
            agent.autoBraking = false;
            agent.stoppingDistance = 0f;
            agent.updatePosition = true;
            agent.isStopped = false;
            Assert.That(agent.Warp(start), Is.True);
            Assert.That(agent.isOnNavMesh, Is.True);

            yield return new WaitForFixedUpdate();
            for (int frame = 0; frame < 5 && speedController.CurrentSurface != volume.Surface; frame++)
                yield return new WaitForFixedUpdate();
            Assert.That(speedController.CurrentSurface, Is.EqualTo(volume.Surface),
                $"the real physics trigger must select {volume.Surface}");
            IDictionary activeVolumes = (IDictionary)typeof(DinoTerrainSpeedController)
                .GetField("activeVolumes", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(speedController);
            Assert.That(activeVolumes.Contains(volume.GetComponent<Collider>()), Is.True,
                $"the actual {volume.Surface} scene collider must enter the gameplay trigger stack");
            NavMeshQueryFilter movementFilter = new() { agentTypeID = agent.agentTypeID, areaMask = NavMesh.AllAreas };
            NavMeshPath measuredPath = new();
            Assert.That(NavMesh.CalculatePath(start, goal, movementFilter, measuredPath), Is.True);
            Assert.That(measuredPath.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
            Assert.That(agent.SetPath(measuredPath), Is.True);
            float startedAt = Time.time;
            int frames = 0;
            while (frames < 10 && (agent.pathPending || !agent.hasPath))
            {
                frames++;
                yield return null;
            }
            Assert.That(agent.hasPath, Is.True,
                $"{prefab.name}/{volume.Surface} did not acquire its measured path; enabled={agent.enabled}, onMesh={agent.isOnNavMesh}, " +
                $"stopped={agent.isStopped}, speed={agent.speed}, pending={agent.pathPending}, remaining={agent.remainingDistance}, " +
                $"status={agent.pathStatus}, position={instance.transform.position}, goal={goal}, timeScale={Time.timeScale}");
            while (frames < 1200 && Vector3.Distance(instance.transform.position, goal) > 0.05f)
            {
                frames++;
                yield return null;
            }
            float seconds = Time.time - startedAt;
            Assert.That(frames, Is.LessThan(1200),
                $"{prefab.name}/{volume.Surface} traversal timed out; hasPath={agent.hasPath}, pending={agent.pathPending}, " +
                $"remaining={agent.remainingDistance}, velocity={agent.velocity}, desired={agent.desiredVelocity}, delta={Time.deltaTime}, " +
                $"position={instance.transform.position}, goal={goal}");
            Assert.That(Vector3.Distance(instance.transform.position, goal), Is.LessThan(0.15f));
            completed(new TraversalMeasurement { Frames = frames, Seconds = seconds, PathLength = pathLength });
            UnityEngine.Object.Destroy(instance);
            yield return null;
        }

        private static bool TryFindEqualLengthRoute(TerrainSurfaceVolume volume, int agentTypeId,
            out Vector3 start, out Vector3 goal, out float pathLength)
        {
            Bounds bounds = volume.GetComponent<Collider>().bounds;
            Vector3[] directions = { Vector3.right, Vector3.forward };
            float[] offsets = { 0f, -2f, 2f, -4f, 4f };
            NavMeshQueryFilter filter = new() { agentTypeID = agentTypeId, areaMask = NavMesh.AllAreas };
            foreach (float zOffset in offsets)
            foreach (float xOffset in offsets)
            foreach (Vector3 direction in directions)
            {
                Vector3 center = new(bounds.center.x + xOffset, bounds.center.y, bounds.center.z + zOffset);
                Vector3 candidateStart = center - direction * 2f;
                Vector3 candidateGoal = center + direction * 2f;
                if (!bounds.Contains(new Vector3(candidateStart.x, bounds.center.y, candidateStart.z)) ||
                    !bounds.Contains(new Vector3(candidateGoal.x, bounds.center.y, candidateGoal.z))) continue;
                if (!NavMesh.SamplePosition(candidateStart, out NavMeshHit startHit, 0.75f, filter) ||
                    !NavMesh.SamplePosition(candidateGoal, out NavMeshHit goalHit, 0.75f, filter)) continue;
                NavMeshPath path = new();
                if (!NavMesh.CalculatePath(startHit.position, goalHit.position, filter, path) ||
                    path.status != NavMeshPathStatus.PathComplete) continue;
                float length = 0f;
                for (int index = 1; index < path.corners.Length; index++)
                    length += Vector3.Distance(path.corners[index - 1], path.corners[index]);
                if (length < 3.9f || length > 4.1f) continue;
                start = startHit.position;
                goal = goalHit.position;
                pathLength = length;
                return true;
            }

            start = default;
            goal = default;
            pathLength = 0f;
            return false;
        }

        [UnityTest]
        public System.Collections.IEnumerator SceneEnemyAi_UsesActiveSlotsAndCompletesRealUpgradeDeathRepairCycle()
        {
            Component layout = FindRuntimeComponent("LlamAcademy.Dinos.Map.VillageDefenseLayoutController");
            if (layout == null)
            {
                AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                while (!load.isDone) yield return null;
                yield return null;
                layout = FindRuntimeComponent("LlamAcademy.Dinos.Map.VillageDefenseLayoutController");
            }
            Component ai = FindRuntimeComponent("LlamAcademy.Dinos.Enemy.EnemyAIController");
            Assert.That(layout, Is.Not.Null);
            Assert.That(ai, Is.Not.Null);
            yield return null;

            FieldInfo waypointTransforms = ai.GetType().GetField("WaypointTransforms", BindingFlags.Instance | BindingFlags.NonPublic);
            FieldInfo waypoints = ai.GetType().GetField("Waypoints", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(((ICollection)waypointTransforms.GetValue(ai)).Count, Is.Zero);
            ICollection patrol = (ICollection)waypoints.GetValue(ai);
            Assert.That(patrol.Count, Is.GreaterThan(0));
            FieldInfo defendersField = ai.GetType().GetField("Defenders", BindingFlags.Instance | BindingFlags.NonPublic);
            List<Component> defenders = new();
            foreach (object entry in (IEnumerable)defendersField.GetValue(ai))
            {
                object values = entry.GetType().GetProperty("Value").GetValue(entry);
                defenders.AddRange(((IEnumerable)values).Cast<Component>());
            }
            Assert.That(defenders, Is.Not.Empty, "SpawnUnitsFirst must create a real defender on the first turn");
            foreach (Component defender in defenders)
            foreach (Vector3 target in patrol)
            {
                NavMeshAgent agent = (NavMeshAgent)defender.GetType().GetProperty("Agent").GetValue(defender);
                NavMeshPath path = new();
                Assert.That(agent.CalculatePath(target, path), Is.True);
                Assert.That(path.status, Is.EqualTo(NavMeshPathStatus.PathComplete));
            }

            PropertyInfo activeRootProperty = layout.GetType().GetProperty("ActivePresetRoot");
            GameObject activeRoot = (GameObject)activeRootProperty.GetValue(layout);
            Component wall = activeRoot.GetComponentsInChildren<Component>(true)
                .Single(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Unit.Wall");
            object baseUnit = wall.GetType().GetProperty("UnitType").GetValue(wall);
            object upgradeUnit = baseUnit.GetType().GetProperty("Upgrade").GetValue(baseUnit);
            for (int round = 0; upgradeUnit == null && round < 8; round++)
            {
                layout.GetType().GetMethod("ActivateForRound").Invoke(layout, new object[] { 20260828, round });
                ai.GetType().GetMethod("RefreshActiveWalls", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ai, null);
                activeRoot = (GameObject)activeRootProperty.GetValue(layout);
                wall = activeRoot.GetComponentsInChildren<Component>(true)
                    .Single(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Unit.Wall");
                baseUnit = wall.GetType().GetProperty("UnitType").GetValue(wall);
                upgradeUnit = baseUnit.GetType().GetProperty("Upgrade").GetValue(baseUnit);
            }
            Assert.That(upgradeUnit, Is.Not.Null);
            int cost = (int)upgradeUnit.GetType().GetProperty("Cost").GetValue(upgradeUnit);
            AssertSingleRegisteredHealthBar(wall, 1f);
            AssertInactivePresetWallHealthBarsAreHidden(activeRoot);

            SpawnedWalls.Clear();
            WallDeathEvents = 0;
            EventInfo spawnEvent = ai.GetType().GetEvent("OnSpawnWall");
            ParameterExpression parameter = System.Linq.Expressions.Expression.Parameter(spawnEvent.EventHandlerType.GetMethod("Invoke").GetParameters()[0].ParameterType, "wall");
            MethodInfo capture = GetType().GetMethod(nameof(CaptureSpawnedWall), BindingFlags.Static | BindingFlags.NonPublic);
            Delegate callback = System.Linq.Expressions.Expression.Lambda(spawnEvent.EventHandlerType,
                System.Linq.Expressions.Expression.Call(capture, System.Linq.Expressions.Expression.Convert(parameter, typeof(object))), parameter).Compile();
            spawnEvent.AddEventHandler(ai, callback);
            EventInfo deathEvent = ai.GetType().GetEvent("OnWallDeath");
            ParameterExpression deathParameter = System.Linq.Expressions.Expression.Parameter(deathEvent.EventHandlerType.GetMethod("Invoke").GetParameters()[0].ParameterType, "wall");
            MethodInfo captureDeath = GetType().GetMethod(nameof(CaptureWallDeath), BindingFlags.Static | BindingFlags.NonPublic);
            Delegate deathCallback = System.Linq.Expressions.Expression.Lambda(deathEvent.EventHandlerType,
                System.Linq.Expressions.Expression.Call(captureDeath), deathParameter).Compile();
            deathEvent.AddEventHandler(ai, deathCallback);
            PropertyInfo rebuildCount = layout.GetType().GetProperty("RuntimeRebuildCount");
            int rebuildsBeforeUpgrade = (int)rebuildCount.GetValue(layout);
            ai.GetType().GetProperty("ResourcesToSpend").SetValue(ai, cost + 1000);
            int beforeResources = (int)ai.GetType().GetProperty("ResourcesToSpend").GetValue(ai);
            MethodInfo upgrades = ai.GetType().GetMethod("HandleUpgrades", BindingFlags.Instance | BindingFlags.NonPublic);
            upgrades.Invoke(ai, new object[] { 1f, cost, 1 });
            for (int frame = 0; frame < 10 && (int)rebuildCount.GetValue(layout) <= rebuildsBeforeUpgrade; frame++) yield return null;
            spawnEvent.RemoveEventHandler(ai, callback);
            Assert.That(SpawnedWalls.Count, Is.EqualTo(1));
            Component upgraded = (Component)SpawnedWalls[0];
            Assert.That(upgraded.transform.IsChildOf(activeRoot.transform), Is.True);
            Assert.That((int)ai.GetType().GetProperty("ResourcesToSpend").GetValue(ai), Is.EqualTo(beforeResources - cost));
            Assert.That((int)rebuildCount.GetValue(layout), Is.GreaterThan(rebuildsBeforeUpgrade),
                "upgrade replacement must trigger the controlled runtime surface rebake");
            double upgradeRebuildMilliseconds = (double)layout.GetType().GetProperty("LastRuntimeRebuildMilliseconds").GetValue(layout);

            PropertyInfo health = upgraded.GetType().GetProperty("Health");
            PropertyInfo maxHealth = upgraded.GetType().GetProperty("MaxHealth");
            AssertSingleRegisteredHealthBar(upgraded, 1f);
            int nonlethalDamage = Math.Max(1, (int)maxHealth.GetValue(upgraded) / 4);
            upgraded.GetType().GetMethod("TakeDamage").Invoke(upgraded, new object[] { nonlethalDamage });
            AssertSingleRegisteredHealthBar(upgraded,
                (int)health.GetValue(upgraded) / (float)(int)maxHealth.GetValue(upgraded));
            int rebuildsBeforeDeath = (int)rebuildCount.GetValue(layout);
            upgraded.GetType().GetMethod("TakeDamage").Invoke(upgraded, new object[] { (int)health.GetValue(upgraded) });
            for (int frame = 0; frame < 10 && ((int)rebuildCount.GetValue(layout) <= rebuildsBeforeDeath || upgraded.gameObject.activeInHierarchy); frame++) yield return null;
            Assert.That(upgraded.gameObject.activeInHierarchy, Is.False);
            Assert.That(WallDeathEvents, Is.EqualTo(1), "one real death must produce exactly one EnemyAI wall-death event");
            Assert.That((int)rebuildCount.GetValue(layout), Is.GreaterThan(rebuildsBeforeDeath),
                "death must rebake after the wall root becomes inactive");
            Assert.That(RegisteredHealthBars().Any(entry => ReferenceEquals(entry.Wall, upgraded)), Is.False,
                "dead wall must be removed from the canvas registry");
            Assert.That(GetWallHealthBar(upgraded).gameObject.activeInHierarchy, Is.False);
            double deathRebuildMilliseconds = (double)layout.GetType().GetProperty("LastRuntimeRebuildMilliseconds").GetValue(layout);
            GameObject deadPreset = activeRoot;
            for (int round = 0; round < 8 && ReferenceEquals(activeRootProperty.GetValue(layout), deadPreset); round++)
                layout.GetType().GetMethod("ActivateForRound").Invoke(layout, new object[] { 20260828, round });
            GameObject otherPreset = (GameObject)activeRootProperty.GetValue(layout);
            Component otherWall = otherPreset.GetComponentsInChildren<Component>(true)
                .Single(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Unit.Wall");
            AssertSingleRegisteredHealthBar(otherWall, 1f);
            Assert.That(GetWallHealthBar(upgraded).gameObject.activeInHierarchy, Is.False,
                "inactive dead preset must not show its health bar");
            for (int round = 0; round < 8 && !ReferenceEquals(activeRootProperty.GetValue(layout), deadPreset); round++)
                layout.GetType().GetMethod("ActivateForRound").Invoke(layout, new object[] { 20260828, round });
            ai.GetType().GetMethod("RefreshActiveWalls", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ai, null);
            FieldInfo wallDatas = ai.GetType().GetField("WallDatas", BindingFlags.Instance | BindingFlags.NonPublic);
            int deadRegistrations = ((IEnumerable)wallDatas.GetValue(ai)).Cast<object>().Count(data =>
                ReferenceEquals(data.GetType().GetField("Wall").GetValue(data), upgraded));
            Assert.That(deadRegistrations, Is.EqualTo(1), "the inactive upgraded wall must re-register exactly once when its preset returns");
            ai.GetType().GetProperty("ResourcesToSpend").SetValue(ai, 10000);
            int rebuildsBeforeRepair = (int)rebuildCount.GetValue(layout);
            ai.GetType().GetMethod("HandleRepairs", BindingFlags.Instance | BindingFlags.NonPublic).Invoke(ai, new object[] { 1f, 10000 });
            for (int frame = 0; frame < 10 && (int)rebuildCount.GetValue(layout) <= rebuildsBeforeRepair; frame++) yield return null;
            Assert.That(upgraded.gameObject.activeInHierarchy, Is.True);
            Assert.That((int)health.GetValue(upgraded), Is.GreaterThan(0));
            AssertSingleRegisteredHealthBar(upgraded,
                (int)health.GetValue(upgraded) / (float)(int)maxHealth.GetValue(upgraded));
            AssertInactivePresetWallHealthBarsAreHidden(deadPreset);
            Assert.That((int)rebuildCount.GetValue(layout), Is.GreaterThan(rebuildsBeforeRepair),
                "repair reactivation must trigger the controlled runtime surface rebake");
            Assert.That(WallDeathEvents, Is.EqualTo(1));
            int rebuildsBeforeSecondDeath = (int)rebuildCount.GetValue(layout);
            upgraded.GetType().GetMethod("TakeDamage").Invoke(upgraded, new object[] { (int)health.GetValue(upgraded) });
            for (int frame = 0; frame < 10 && ((int)rebuildCount.GetValue(layout) <= rebuildsBeforeSecondDeath || upgraded.gameObject.activeInHierarchy); frame++) yield return null;
            Assert.That(WallDeathEvents, Is.EqualTo(2),
                "repaired wall must have exactly one death subscription after preset switches");
            Assert.That(RegisteredHealthBars().Any(entry => ReferenceEquals(entry.Wall, upgraded)), Is.False);
            deathEvent.RemoveEventHandler(ai, deathCallback);
            double repairRebuildMilliseconds = (double)layout.GetType().GetProperty("LastRuntimeRebuildMilliseconds").GetValue(layout);
            Debug.Log($"TASK5_REBUILD upgradeMs={upgradeRebuildMilliseconds:F3}; deathMs={deathRebuildMilliseconds:F3}; " +
                      $"repairMs={repairRebuildMilliseconds:F3}; calls={rebuildCount.GetValue(layout)}");
        }

        private static void CaptureSpawnedWall(object wall) => SpawnedWalls.Add(wall);
        private static void CaptureWallDeath() => WallDeathEvents++;

        [UnityTest]
        public IEnumerator SceneSessionLifecycle_TwentyReloadedEpisodesCoverWinLossWithoutDrift()
        {
            const int episodeCount = 20;
            int capturedErrors = 0;
            int initialSceneObjects = -1;
            int initialMaterials = -1;
            int initialNavMeshVertices = -1;
            int initialNavMeshIndices = -1;
            int initialRoundSubscribers = -1;
            long firstManagedBytes = 0;
            long maximumManagedBytes = 0;

            void CaptureLog(string condition, string stackTrace, LogType type)
            {
                if (type == LogType.Error || type == LogType.Exception || type == LogType.Assert)
                    capturedErrors++;
            }

            Application.logMessageReceived += CaptureLog;
            try
            {
                for (int episode = 0; episode < episodeCount; episode++)
                {
                    int errorsBeforeEpisode = capturedErrors;
                    AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
                    while (!load.isDone) yield return null;
                    // The real EnemyAI initial turn intentionally spans several frames while it
                    // chooses a preset, updates runtime navigation, and spawns the opening defense.
                    for (int frame = 0; frame < 20; frame++) yield return null;

                    Component roundManager = FindRuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
                    Component spawner = FindRuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
                    Component runtimeUI = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
                    Assert.That(roundManager, Is.Not.Null);
                    Assert.That(spawner, Is.Not.Null);
                    Assert.That(runtimeUI, Is.Not.Null);
                    Assert.That(roundManager.GetType().GetProperty("State").GetValue(roundManager).ToString(), Is.EqualTo("Setup"));

                    Scene scene = SceneManager.GetActiveScene();
                    int sceneObjectsBefore = CountSceneObjects(scene);
                    int materialCountBefore = CountSceneSharedMaterials(scene);
                    NavMeshTriangulation navigationBefore = NavMesh.CalculateTriangulation();
                    int roundSubscribers = EventSubscriberCount(roundManager, "OnGameStateChange");
                    long managedBytesBefore = GC.GetTotalMemory(false);
                    if (episode == 0)
                    {
                        initialSceneObjects = sceneObjectsBefore;
                        initialMaterials = materialCountBefore;
                        initialNavMeshVertices = navigationBefore.vertices.Length;
                        initialNavMeshIndices = navigationBefore.indices.Length;
                        initialRoundSubscribers = roundSubscribers;
                        firstManagedBytes = managedBytesBefore;
                    }

                    // Opening defenders are intentionally selected with production randomness, so
                    // child and shared-material counts are recorded rather than required to match.
                    Assert.That(sceneObjectsBefore, Is.InRange(400, 1500), "reloaded scene hierarchy must remain bounded");
                    Assert.That(materialCountBefore, Is.InRange(Math.Max(1, initialMaterials - 32), initialMaterials + 32),
                        "opening-defense variation must not create an unbounded material set");
                    Assert.That(navigationBefore.vertices, Has.Length.EqualTo(initialNavMeshVertices));
                    Assert.That(navigationBefore.indices, Has.Length.EqualTo(initialNavMeshIndices));
                    Assert.That(roundSubscribers, Is.EqualTo(initialRoundSubscribers), "scene reload must not duplicate round subscriptions");

                    Array dinos = (Array)GetPrivateField(runtimeUI, "Dinos");
                    Assert.That(dinos, Is.Not.Null.And.Not.Empty);
                    DinoDeploymentZone[] zones = UnityEngine.Object
                        .FindObjectsByType<DinoDeploymentZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                        .OrderBy(zone => zone.Id)
                        .ToArray();
                    Assert.That(zones, Has.Length.EqualTo(5));
                    DinoDeploymentZone zone = zones[episode % zones.Length];
                    spawner.GetType().GetProperty("ResourcesToSpend").SetValue(spawner, 10000);
                    Vector3 deploymentPoint = ResolveZonePoint(zone, new Vector2(0.5f, 0.5f));
                    object[] deploymentArguments = { dinos.GetValue(episode % dinos.Length), deploymentPoint, false, null, null };
                    MethodInfo tryDeploy = spawner.GetType().GetMethod("TryDeploy", BindingFlags.Instance | BindingFlags.Public);
                    Assert.That((bool)tryDeploy.Invoke(spawner, deploymentArguments), Is.True,
                        $"episode {episode + 1} must deploy through real zone {zone.Id}");
                    Component spawnedDino = (Component)deploymentArguments[4];
                    Assert.That(spawnedDino, Is.Not.Null);
                    for (int frame = 0; frame < 2; frame++) yield return null;

                    DinoTerrainSpeedController[] speedControllers = UnityEngine.Object
                        .FindObjectsByType<DinoTerrainSpeedController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
                    Assert.That(speedControllers, Is.Not.Empty, "a real deployed dino must own terrain speed control");
                    Assert.That(speedControllers.All(controller => float.IsFinite(controller.CurrentMultiplier)), Is.True);

                    roundManager.GetType().GetMethod("StartRound", BindingFlags.Instance | BindingFlags.Public).Invoke(roundManager, null);
                    Assert.That(roundManager.GetType().GetProperty("State").GetValue(roundManager).ToString(), Is.EqualTo("Running"));
                    bool victory = episode % 2 == 0;
                    if (victory)
                    {
                        InvokePrivate(roundManager, "HandleDinoEnterEggRadius", spawnedDino);
                    }
                    else
                    {
                        spawner.GetType().GetProperty("ResourcesToSpend").SetValue(spawner, 0);
                        spawnedDino.GetType().GetMethod("TakeDamage", BindingFlags.Instance | BindingFlags.Public)
                            .Invoke(spawnedDino, new object[] { int.MaxValue });
                    }

                    Assert.That(roundManager.GetType().GetProperty("State").GetValue(roundManager).ToString(), Is.EqualTo("Ending"));
                    ((MonoBehaviour)roundManager).CancelInvoke();
                    InvokePrivate(roundManager, "FinishSession");
                    Assert.That(roundManager.GetType().GetProperty("State").GetValue(roundManager).ToString(), Is.EqualTo("Ended"));
                    Assert.That(roundManager.GetType().GetProperty("HasSessionOutcome").GetValue(roundManager), Is.True);
                    Assert.That(roundManager.GetType().GetProperty("PlayerWon").GetValue(roundManager), Is.EqualTo(victory));
                    Label resultLabel = runtimeUI.GetComponent<UIDocument>().rootVisualElement.Q<Label>("win-lose-text");
                    Assert.That(resultLabel.text, Does.Contain(victory ? "Victory" : "Defeat"));

                    NavMeshTriangulation navigationAfter = NavMesh.CalculateTriangulation();
                    long managedBytesAfter = GC.GetTotalMemory(false);
                    maximumManagedBytes = Math.Max(maximumManagedBytes, managedBytesAfter);
                    Assert.That(navigationAfter.vertices, Is.Not.Empty);
                    Assert.That(navigationAfter.indices, Is.Not.Empty);
                    Assert.That(capturedErrors - errorsBeforeEpisode, Is.Zero, "episode emitted an Error/Exception/Assert log");
                    Debug.Log($"TASK8_SESSION_EPISODE episode={episode + 1}; outcome={(victory ? "win" : "loss")}; " +
                              $"zone={zone.Id}; objects={sceneObjectsBefore}; subscriptions={roundSubscribers}; " +
                              $"materials={materialCountBefore}; speedControllers={speedControllers.Length}; " +
                              $"navVertices={navigationAfter.vertices.Length}; navIndices={navigationAfter.indices.Length}; " +
                              $"managedBefore={managedBytesBefore}; managedAfter={managedBytesAfter}; consoleErrors=0; state=Ended");
                }

                Assert.That(maximumManagedBytes - firstManagedBytes, Is.LessThan(256L * 1024L * 1024L),
                    "twenty reloads must not show unbounded managed-memory growth");
            }
            finally
            {
                Application.logMessageReceived -= CaptureLog;
            }
        }

        private static int CountSceneObjects(Scene scene) => scene.GetRootGameObjects()
            .Sum(root => root.GetComponentsInChildren<Transform>(true).Length);

        private static int CountSceneSharedMaterials(Scene scene) => scene.GetRootGameObjects()
            .SelectMany(root => root.GetComponentsInChildren<Renderer>(true))
            .SelectMany(renderer => renderer.sharedMaterials)
            .Where(material => material != null)
            .Select(material => material.GetInstanceID())
            .Distinct()
            .Count();

        private static int EventSubscriberCount(object target, string eventName)
        {
            FieldInfo eventField = target.GetType().GetField(eventName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(eventField, Is.Not.Null, $"missing event backing field {eventName}");
            return ((Delegate)eventField.GetValue(target))?.GetInvocationList().Length ?? 0;
        }

        private static List<(Component Wall, Component Bar)> RegisteredHealthBars()
        {
            Type canvasType = RuntimeType("LlamAcademy.Dinos.Utility.HealthBarCanvas");
            Component canvas = (Component)canvasType.GetProperty("Instance", BindingFlags.Static | BindingFlags.Public).GetValue(null);
            Assert.That(canvas, Is.Not.Null);
            IEnumerable dictionary = (IEnumerable)canvasType
                .GetField("HealthBars", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(canvas);
            List<(Component Wall, Component Bar)> result = new();
            foreach (object entry in dictionary)
            {
                Type entryType = entry.GetType();
                result.Add(((Component)entryType.GetProperty("Key").GetValue(entry),
                    (Component)entryType.GetProperty("Value").GetValue(entry)));
            }
            return result;
        }

        private static Component GetWallHealthBar(Component wall)
        {
            Type type = wall.GetType();
            while (type != null)
            {
                FieldInfo field = type.GetField("HealthBar", BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null) return (Component)field.GetValue(wall);
                type = type.BaseType;
            }
            Assert.Fail($"{wall.name} does not expose a serialized HealthBar field");
            return null;
        }

        private static void AssertSingleRegisteredHealthBar(Component wall, float expectedProgress)
        {
            List<(Component Wall, Component Bar)> matches = RegisteredHealthBars()
                .Where(entry => ReferenceEquals(entry.Wall, wall)).ToList();
            Assert.That(matches, Has.Count.EqualTo(1), $"{wall.name} must have exactly one canvas registration");
            Component bar = matches[0].Bar;
            Assert.That(bar, Is.SameAs(GetWallHealthBar(wall)));
            Assert.That(bar.gameObject.activeInHierarchy, Is.True);
            Type healthBarType = RuntimeType("LlamAcademy.Dinos.UI.HealthBar");
            UnityEngine.UI.Image fill = (UnityEngine.UI.Image)healthBarType
                .GetField("FillImage", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(bar);
            Assert.That(fill, Is.Not.Null);
            Assert.That(fill.fillAmount, Is.EqualTo(expectedProgress).Within(0.001f));
        }

        private static void AssertInactivePresetWallHealthBarsAreHidden(GameObject activePreset)
        {
            Component[] walls = activePreset.transform.parent.GetComponentsInChildren<Component>(true)
                .Where(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Unit.Wall")
                .ToArray();
            foreach (Component candidate in walls.Where(candidate => !candidate.transform.IsChildOf(activePreset.transform)))
            {
                Component bar = GetWallHealthBar(candidate);
                Assert.That(bar.gameObject.activeInHierarchy, Is.False,
                    $"inactive preset wall {candidate.name} must not show a health bar");
                Assert.That(RegisteredHealthBars().Any(entry => ReferenceEquals(entry.Wall, candidate)), Is.False);
            }
        }

        private static object GetPrivateField(object target, string fieldName) =>
            target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(target);

        private static IEnumerator WaitForRoundState(string expectedState)
        {
            float deadline = Time.realtimeSinceStartup + 5f;
            while (Time.realtimeSinceStartup < deadline)
            {
                Component round = FindRuntimeComponent(
                    "LlamAcademy.Dinos.RoundManagement.RoundManager");
                if (round != null && round.GetType().GetProperty("State")
                    ?.GetValue(round)?.ToString() == expectedState)
                {
                    yield break;
                }
                yield return null;
            }
            Assert.Fail($"RoundManager did not reach {expectedState} before the timeout.");
        }

        private static void AppendLifecycleState(Component roundManager, List<string> states)
        {
            string current = roundManager.GetType().GetProperty("State")
                .GetValue(roundManager).ToString();
            if (states.Count == 0 || states[^1] != current)
            {
                states.Add(current);
            }
        }

        private static void AssertGameplayObservationEquivalent(
            DinoStructuredObservationFrame training,
            DinoStructuredObservationFrame inference,
            string phase)
        {
            Assert.That(inference.Global[0], Is.EqualTo(training.Global[0]).Within(0.05f),
                $"{phase}: remaining-time observation drifted between controllers");
            for (int index = 1; index < training.Global.Length; index++)
            {
                Assert.That(inference.Global[index], Is.EqualTo(training.Global[index]).Within(0.000001f),
                    $"{phase}: Global[{index}] differs between controllers");
            }
            CollectionAssert.AreEqual(training.Regions, inference.Regions, $"{phase}: Regions differ");
            CollectionAssert.AreEqual(training.Walls, inference.Walls, $"{phase}: Walls differ");
            CollectionAssert.AreEqual(training.Guards, inference.Guards, $"{phase}: Guards differ");
            CollectionAssert.AreEqual(training.Houses, inference.Houses, $"{phase}: Houses differ");
            CollectionAssert.AreEqual(training.Dinos, inference.Dinos, $"{phase}: Dinos differ");
        }

        private sealed class LifecycleInferenceRunner : IDinoInferencePolicyRunner
        {
            private readonly IReadOnlyList<float[]> ActionSequence;

            public LifecycleInferenceRunner(float[] actions)
                : this(new[] { actions })
            {
            }

            public LifecycleInferenceRunner(IEnumerable<float[]> actions) =>
                ActionSequence = actions.Select(action => (float[])action.Clone()).ToArray();

            public int CallCount { get; private set; }

            public bool TryRun(
                DinoStructuredObservationFrame frame,
                out float[] actions,
                out string failureReason)
            {
                Assert.That(frame, Is.Not.Null);
                if (CallCount >= ActionSequence.Count)
                {
                    actions = Array.Empty<float>();
                    failureReason = "The lifecycle test runner exhausted its deterministic action sequence.";
                    return false;
                }
                actions = (float[])ActionSequence[CallCount].Clone();
                CallCount++;
                failureReason = string.Empty;
                return true;
            }
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"missing private field {target.GetType().FullName}.{fieldName}");
            field.SetValue(target, value);
        }

        private static object GetStaticField(Type type, string fieldName)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"missing static field {type.FullName}.{fieldName}");
            return field.GetValue(null);
        }

        private static object GetInheritedField(object target, string fieldName)
        {
            for (Type type = target.GetType(); type != null; type = type.BaseType)
            {
                FieldInfo field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    return field.GetValue(target);
                }
            }

            Assert.Fail($"missing inherited field {target.GetType().FullName}.{fieldName}");
            return null;
        }

        private static object GetFieldValue(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"missing field {target.GetType().FullName}.{fieldName}");
            return field.GetValue(target);
        }

        private static void SetStaticField(Type type, string fieldName, object value)
        {
            FieldInfo field = type.GetField(fieldName, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"missing static field {type.FullName}.{fieldName}");
            field.SetValue(null, value);
        }

        private static void SetAutoProperty(object target, string propertyName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                $"<{propertyName}>k__BackingField", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"missing auto-property storage {target.GetType().FullName}.{propertyName}");
            field.SetValue(target, value);
        }

        private static void InvokeTrainingAction(Component agent, float[] continuousActions)
        {
            Type buffersType = RuntimeType("Unity.MLAgents.Actuators.ActionBuffers");
            object buffers = Activator.CreateInstance(
                buffersType,
                new object[] { continuousActions, Array.Empty<int>() });
            MethodInfo receive = agent.GetType().GetMethod("OnActionReceived", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(receive, Is.Not.Null);
            receive.Invoke(agent, new[] { buffers });
        }

        private static float[] EncodeTrainingAction(int zoneIndex, Vector2 relativeUv, int dinoIndex)
        {
            float zoneValue = -0.8f + zoneIndex * 0.4f;
            float choiceValue = -0.25f + dinoIndex * 0.5f;
            return new[]
            {
                zoneValue,
                relativeUv.x * 2f - 1f,
                relativeUv.y * 2f - 1f,
                choiceValue
            };
        }

        private static IEnumerable<Component> ActiveDinoComponents(Component roundManager) =>
            ((IEnumerable)roundManager.GetType().GetProperty("ActiveDinoUnits").GetValue(roundManager))
            .Cast<Component>();

        private static object InvokePrivate(object target, string methodName, params object[] arguments)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"missing private lifecycle method {methodName}");
            return method.Invoke(target, arguments);
        }

        private static Component FindRuntimeComponent(string fullName) =>
            UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(component => component != null && component.GetType().FullName == fullName);

        private static object OverrideTrainingArguments(string[] arguments)
        {
            Type bootstrapType = RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingBootstrap");
            FieldInfo field = bootstrapType.GetField(
                "CommandLineArgumentsProvider", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            object original = field.GetValue(null);
            field.SetValue(null, new Func<string[]>(() => arguments));
            return original;
        }

        private static void RestoreTrainingArguments(object originalProvider)
        {
            Type bootstrapType = RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingBootstrap");
            FieldInfo field = bootstrapType.GetField(
                "CommandLineArgumentsProvider", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(null, originalProvider);
        }

        [UnityTest]
        public System.Collections.IEnumerator SpeedController_AppliesOneMultiplierAndRestoresBaseSpeed()
        {
            NavMeshAgent agent = CreateAgent(BaseSpeed);
            DinoTerrainSpeedController controller = agent.gameObject.AddComponent<DinoTerrainSpeedController>();
            TerrainSurfaceVolume shallowWater = CreateSurface(TerrainSurfaceKind.ShallowWater);

            InvokeTerrainTrigger(controller, "OnTriggerEnter", shallowWater.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(5.6f).Within(0.001f));
            Assert.That(controller.CurrentSurface, Is.EqualTo(TerrainSurfaceKind.ShallowWater));
            Assert.That(controller.CurrentMultiplier, Is.EqualTo(0.70f).Within(0.0001f));

            InvokeTerrainTrigger(controller, "OnTriggerExit", shallowWater.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(BaseSpeed).Within(0.001f));
            yield return Cleanup(agent.gameObject, shallowWater.gameObject);
        }

        [UnityTest]
        public System.Collections.IEnumerator SpeedController_ResolvesOverlapsBySingleHighestPriorityMultiplier()
        {
            NavMeshAgent agent = CreateAgent(BaseSpeed);
            DinoTerrainSpeedController controller = agent.gameObject.AddComponent<DinoTerrainSpeedController>();
            TerrainSurfaceVolume stoneRoad = CreateSurface(TerrainSurfaceKind.StoneRoad);
            TerrainSurfaceVolume shallowWater = CreateSurface(TerrainSurfaceKind.ShallowWater);

            InvokeTerrainTrigger(controller, "OnTriggerEnter", stoneRoad.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(8.8f).Within(0.001f));
            InvokeTerrainTrigger(controller, "OnTriggerEnter", shallowWater.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(5.6f).Within(0.001f));
            InvokeTerrainTrigger(controller, "OnTriggerExit", shallowWater.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(8.8f).Within(0.001f));
            InvokeTerrainTrigger(controller, "OnTriggerExit", stoneRoad.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(BaseSpeed).Within(0.001f));

            yield return Cleanup(agent.gameObject, stoneRoad.gameObject, shallowWater.gameObject);
        }

        [UnityTest]
        public System.Collections.IEnumerator SpeedController_CountsDuplicateTriggersAndRestoresWithoutCompounding()
        {
            NavMeshAgent agent = CreateAgent(BaseSpeed);
            DinoTerrainSpeedController controller = agent.gameObject.AddComponent<DinoTerrainSpeedController>();
            TerrainSurfaceVolume shallowWater = CreateSurface(TerrainSurfaceKind.ShallowWater);

            InvokeTerrainTrigger(controller, "OnTriggerEnter", shallowWater.GetComponent<Collider>());
            InvokeTerrainTrigger(controller, "OnTriggerEnter", shallowWater.GetComponent<Collider>());
            InvokeTerrainTrigger(controller, "OnTriggerExit", shallowWater.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(5.6f).Within(0.001f));
            InvokeTerrainTrigger(controller, "OnTriggerExit", shallowWater.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(BaseSpeed).Within(0.001f));

            InvokeTerrainTrigger(controller, "OnTriggerEnter", shallowWater.GetComponent<Collider>());
            controller.enabled = false;
            Assert.That(agent.speed, Is.EqualTo(BaseSpeed).Within(0.001f));
            controller.enabled = true;
            Assert.That(agent.speed, Is.EqualTo(BaseSpeed).Within(0.001f));
            InvokeTerrainTrigger(controller, "OnTriggerEnter", shallowWater.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(5.6f).Within(0.001f));

            yield return Cleanup(agent.gameObject, shallowWater.gameObject);
        }

        [UnityTest]
        public System.Collections.IEnumerator SpeedController_CapturesSurfaceAtFirstEnterUntilTheMatchingFinalExit()
        {
            NavMeshAgent agent = CreateAgent(BaseSpeed);
            DinoTerrainSpeedController controller = agent.gameObject.AddComponent<DinoTerrainSpeedController>();
            TerrainSurfaceVolume firstWater = CreateSurface(TerrainSurfaceKind.ShallowWater);
            TerrainSurfaceVolume secondWater = CreateSurface(TerrainSurfaceKind.ShallowWater);

            InvokeTerrainTrigger(controller, "OnTriggerEnter", firstWater.GetComponent<Collider>());
            InvokeTerrainTrigger(controller, "OnTriggerEnter", secondWater.GetComponent<Collider>());
            SetSurface(firstWater, TerrainSurfaceKind.Mud);
            InvokeTerrainTrigger(controller, "OnTriggerExit", firstWater.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(5.6f).Within(0.001f));
            InvokeTerrainTrigger(controller, "OnTriggerExit", secondWater.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(BaseSpeed).Within(0.001f));

            yield return Cleanup(agent.gameObject, firstWater.gameObject, secondWater.gameObject);
        }

        [UnityTest]
        public System.Collections.IEnumerator SpeedController_IgnoresStaleAndUnenteredVolumeExits()
        {
            NavMeshAgent agent = CreateAgent(BaseSpeed);
            DinoTerrainSpeedController controller = agent.gameObject.AddComponent<DinoTerrainSpeedController>();
            TerrainSurfaceVolume water = CreateSurface(TerrainSurfaceKind.ShallowWater);

            InvokeTerrainTrigger(controller, "OnTriggerExit", water.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(BaseSpeed).Within(0.001f));
            InvokeTerrainTrigger(controller, "OnTriggerEnter", water.GetComponent<Collider>());
            InvokeTerrainTrigger(controller, "OnTriggerExit", water.GetComponent<Collider>());
            InvokeTerrainTrigger(controller, "OnTriggerExit", water.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(BaseSpeed).Within(0.001f));

            yield return Cleanup(agent.gameObject, water.gameObject);
        }

        [UnityTest]
        public System.Collections.IEnumerator SpeedController_RespondsToRealPhysicsTriggerEnterAndExit()
        {
            NavMeshAgent agent = CreateAgent(BaseSpeed);
            SphereCollider agentCollider = agent.gameObject.AddComponent<SphereCollider>();
            agentCollider.radius = 0.5f;
            Rigidbody body = agent.gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            body.useGravity = false;
            agent.gameObject.AddComponent<DinoTerrainSpeedController>();
            TerrainSurfaceVolume water = CreateSurface(TerrainSurfaceKind.ShallowWater);
            agent.enabled = false;
            agent.transform.position = Vector3.right * 200f;
            water.transform.position = agent.transform.position;

            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            Assert.That(agent.speed, Is.EqualTo(5.6f).Within(0.001f));

            water.transform.position = Vector3.right * 10f;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();
            yield return new WaitForFixedUpdate();
            Assert.That(agent.speed, Is.EqualTo(BaseSpeed).Within(0.001f));

            yield return Cleanup(agent.gameObject, water.gameObject);
        }

        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        [TestCase(float.NegativeInfinity)]
        public void SpeedController_DoesNotWriteNonFiniteCapturedBaseSpeed(float invalidBaseSpeed)
        {
            NavMeshAgent agent = CreateAgent(BaseSpeed);
            DinoTerrainSpeedController controller = agent.gameObject.AddComponent<DinoTerrainSpeedController>();
            TerrainSurfaceVolume water = CreateSurface(TerrainSurfaceKind.ShallowWater);

            typeof(DinoTerrainSpeedController)
                .GetField("baseSpeed", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, invalidBaseSpeed);
            typeof(DinoTerrainSpeedController)
                .GetField("hasValidBaseSpeed", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(controller, true);
            InvokeTerrainTrigger(controller, "OnTriggerEnter", water.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(BaseSpeed).Within(0.001f));
            Assert.That(float.IsFinite(agent.speed), Is.True);
            Assert.That(agent.speed, Is.GreaterThan(0f));

            UnityEngine.Object.DestroyImmediate(agent.gameObject);
            UnityEngine.Object.DestroyImmediate(water.gameObject);
        }

        [TestCase(TerrainSurfaceKind.StoneRoad, 8.8f)]
        [TestCase(TerrainSurfaceKind.Grass, 8.0f)]
        [TestCase(TerrainSurfaceKind.Mud, 6.8f)]
        [TestCase(TerrainSurfaceKind.Slope, 6.4f)]
        [TestCase(TerrainSurfaceKind.ShallowWater, 5.6f)]
        public void SpeedController_UsesExactApprovedMultiplier(TerrainSurfaceKind surface, float expectedSpeed)
        {
            NavMeshAgent agent = CreateAgent(BaseSpeed);
            DinoTerrainSpeedController controller = agent.gameObject.AddComponent<DinoTerrainSpeedController>();
            TerrainSurfaceVolume volume = CreateSurface(surface);

            InvokeTerrainTrigger(controller, "OnTriggerEnter", volume.GetComponent<Collider>());
            Assert.That(agent.speed, Is.EqualTo(expectedSpeed).Within(0.001f));

            UnityEngine.Object.DestroyImmediate(agent.gameObject);
            UnityEngine.Object.DestroyImmediate(volume.gameObject);
        }

        [UnityTest]
        public System.Collections.IEnumerator SpeedController_IgnoresUnrelatedCollidersAndNeverWritesInvalidBaseSpeed()
        {
            NavMeshAgent invalidAgent = CreateAgent(0f);
            DinoTerrainSpeedController invalidController = invalidAgent.gameObject.AddComponent<DinoTerrainSpeedController>();
            TerrainSurfaceVolume shallowWater = CreateSurface(TerrainSurfaceKind.ShallowWater);
            BoxCollider unrelated = new GameObject("Unrelated").AddComponent<BoxCollider>();

            InvokeTerrainTrigger(invalidController, "OnTriggerEnter", unrelated);
            Assert.That(invalidAgent.speed, Is.EqualTo(0f));
            InvokeTerrainTrigger(invalidController, "OnTriggerEnter", shallowWater.GetComponent<Collider>());
            Assert.That(float.IsFinite(invalidAgent.speed), Is.True);
            Assert.That(invalidAgent.speed, Is.EqualTo(0f));

            yield return Cleanup(invalidAgent.gameObject, shallowWater.gameObject, unrelated.gameObject);
        }

        [UnityTest]
        public System.Collections.IEnumerator AttackPath_WithSolidBlocker_StillRaisesTheRealAttackEvent()
        {
            GameObject attacker = new GameObject("Attacker");
            GameObject target = UnityEngine.Object.Instantiate(LoadAssetAtPath("Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor_New.prefab") as GameObject);
            GameObject solidBlocker = new GameObject("SolidBlocker");
            attacker.AddComponent<NavMeshAgent>().speed = BaseSpeed;
            attacker.transform.position = Vector3.zero;
            target.transform.position = Vector3.forward * 10f;
            solidBlocker.transform.position = Vector3.forward * 5f;
            solidBlocker.AddComponent<BoxCollider>();

            Component targetDino = target.GetComponents<Component>().First(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Unit.Dino");
            targetDino.GetType().GetProperty("MaxHealth").SetValue(targetDino, 1000);
            targetDino.GetType().GetProperty("Health").SetValue(targetDino, 1000);

            Type helperType = RuntimeType("LlamAcademy.Dinos.Utility.ParticleSystemHelper");
            GameObject helper = new GameObject("ParticleSystemHelper");
            helper.AddComponent(helperType);

            Type attackActionType = RuntimeType("LlamAcademy.Dinos.Behavior.AttackClosestObjectAction");
            Type eventChannelType = RuntimeType("LlamAcademy.Dinos.Behavior.AttackEventChannel");
            object action = Activator.CreateInstance(attackActionType);
            ScriptableObject attackEventChannel = ScriptableObject.CreateInstance(eventChannelType);
            CaptureAttackEvent(null, null);
            EventInfo eventInfo = eventChannelType.GetEvent("Event");
            Delegate listener = Delegate.CreateDelegate(eventInfo.EventHandlerType, typeof(OpenTropicalBattlefieldPlayModeTests).GetMethod(nameof(CaptureAttackEvent), BindingFlags.Static | BindingFlags.NonPublic));
            eventInfo.AddEventHandler(attackEventChannel, listener);

            SetActionField(action, "Self", CreateBlackboardVariable(typeof(GameObject), attacker));
            SetActionField(action, "ClosestAttackable", CreateBlackboardVariable(typeof(GameObject), target));
            SetActionField(action, "AttackConfig", CreateBlackboardVariable(RuntimeType("LlamAcademy.Dinos.Config.AttackConfigSO"), LoadAssetAtPath("Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor Attack Config.asset")));
            SetActionField(action, "RotationSpeed", CreateBlackboardVariable(typeof(float), 5f));
            SetActionField(action, "LastAttackTime", CreateBlackboardVariable(typeof(float), 0f));
            SetActionField(action, "AttackEventChannel", CreateBlackboardVariable(eventChannelType, attackEventChannel));

            object startStatus = InvokeAction(action, "OnStart");
            Assert.That(startStatus.ToString(), Is.EqualTo("Running"));
            ForceAttackCooldownReady(action);
            InvokeAction(action, "OnUpdate");
            Assert.That(CapturedAttacker, Is.EqualTo(attacker));
            Assert.That(CapturedTarget, Is.EqualTo(target));

            eventInfo.RemoveEventHandler(attackEventChannel, listener);
            UnityEngine.Object.Destroy(attackEventChannel);
            yield return Cleanup(attacker, target, solidBlocker, helper);
        }

        [UnityTest]
        public System.Collections.IEnumerator AttackAction_RejectsATargetThatIsAlreadyDead()
        {
            GameObject attacker = new GameObject("DeadTargetAttacker");
            GameObject target = UnityEngine.Object.Instantiate(
                LoadAssetAtPath("Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor_New.prefab") as GameObject);
            attacker.AddComponent<NavMeshAgent>().speed = BaseSpeed;

            Component damageable = target.GetComponents<Component>()
                .First(component => component != null && component.GetType().FullName == "LlamAcademy.Dinos.Unit.Dino");
            damageable.GetType().GetProperty("MaxHealth").SetValue(damageable, 100);
            damageable.GetType().GetProperty("Health").SetValue(damageable, 0);

            Type attackActionType = RuntimeType("LlamAcademy.Dinos.Behavior.AttackClosestObjectAction");
            object action = Activator.CreateInstance(attackActionType);
            SetActionField(action, "Self", CreateBlackboardVariable(typeof(GameObject), attacker));
            SetActionField(action, "ClosestAttackable", CreateBlackboardVariable(typeof(GameObject), target));
            SetActionField(action, "AttackConfig", CreateBlackboardVariable(
                RuntimeType("LlamAcademy.Dinos.Config.AttackConfigSO"),
                LoadAssetAtPath("Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor Attack Config.asset")));

            object startStatus = InvokeAction(action, "OnStart");
            Assert.That(startStatus.ToString(), Is.EqualTo("Failure"),
                "a dead cached target must return control to target selection immediately");

            yield return Cleanup(attacker, target);
        }

        [UnityTest]
        public IEnumerator DinoAfterKillingATarget_ReselectsAndContinuesTheBattle()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 3; frame++) yield return null;

            float originalTimeScale = Time.timeScale;
            Time.timeScale = 8f;
            try
            {
                Component roundManager = FindRuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
                Component spawner = FindRuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
                Component runtimeUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
                Assert.That(roundManager, Is.Not.Null);
                Assert.That(spawner, Is.Not.Null);
                Assert.That(runtimeUi, Is.Not.Null);

                Array dinoTypes = (Array)GetPrivateField(runtimeUi, "Dinos");
                UnityEngine.Object tRex = dinoTypes.Cast<UnityEngine.Object>()
                    .Single(dino => dino.name.Equals("TRex", StringComparison.OrdinalIgnoreCase));
                spawner.GetType().GetProperty("ResourcesToSpend")?.SetValue(spawner, 9999);

                DinoDeploymentZone z1 = UnityEngine.Object
                    .FindObjectsByType<DinoDeploymentZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .Single(zone => zone.Id == DeploymentZoneId.Z1RuinsForecourt);
                object[] deployArguments = { tRex, ResolveZonePoint(z1, new Vector2(0.5f, 0.5f)), false, null, null };
                bool deployed = (bool)spawner.GetType().GetMethod("TryDeploy")
                    .Invoke(spawner, deployArguments);
                Assert.That(deployed, Is.True, "the real T-Rex must deploy into Z1 for the battle continuation check");
                Component dino = (Component)deployArguments[4];
                Assert.That(dino, Is.Not.Null);

                roundManager.GetType().GetMethod("StartRound")?.Invoke(roundManager, null);
                Component[] initialTargets = UnityEngine.Object.FindObjectsByType<Component>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .Where(component => component != null &&
                        (component.GetType().FullName == "LlamAcademy.Dinos.Unit.Wall" ||
                         component.GetType().FullName == "LlamAcademy.Dinos.Enemy.Defender" ||
                         component.GetType().FullName == "LlamAcademy.Dinos.Unit.VillageHouse"))
                    .ToArray();
                Dictionary<Component, int> initialHealth = initialTargets.ToDictionary(
                    target => target,
                    target => (int)target.GetType().GetProperty("Health").GetValue(target));

                Component killedTarget = null;
                float killDeadline = Time.time + 30f;
                while (Time.time < killDeadline && killedTarget == null)
                {
                    killedTarget = initialTargets.FirstOrDefault(target => target != null &&
                        (int)target.GetType().GetProperty("Health").GetValue(target) <= 0);
                    yield return null;
                }

                Assert.That(killedTarget, Is.Not.Null,
                    "the real T-Rex must kill one scene target before continuation is evaluated");
                Vector3 positionAtKill = dino.transform.position;
                NavMeshAgent agent = dino.GetComponent<NavMeshAgent>();
                bool resumedMovement = false;
                bool engagedAnotherTarget = false;
                float continuationDeadline = Time.time + 3f;
                while (Time.time < continuationDeadline)
                {
                    resumedMovement |= Vector3.Distance(positionAtKill, dino.transform.position) > 0.4f ||
                                       agent.velocity.sqrMagnitude > 0.01f;
                    engagedAnotherTarget |= initialTargets.Any(target => target != null && target != killedTarget &&
                        (int)target.GetType().GetProperty("Health").GetValue(target) < initialHealth[target]);
                    if (resumedMovement || engagedAnotherTarget) break;
                    yield return null;
                }

                Assert.That(resumedMovement || engagedAnotherTarget, Is.True,
                    "after a kill the behavior graph must leave the dead target and either move or attack the next living target");
            }
            finally
            {
                Time.timeScale = originalTimeScale;
            }
        }

        [UnityTest]
        public IEnumerator DinoAfterLosingItsLastNearbyTarget_ContinuesTowardVillageCenter()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 3; frame++) yield return null;

            float originalTimeScale = Time.timeScale;
            Time.timeScale = 8f;
            try
            {
                Component roundManager = FindRuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
                Component spawner = FindRuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
                Component runtimeUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
                Array dinoTypes = (Array)GetPrivateField(runtimeUi, "Dinos");
                UnityEngine.Object tRex = dinoTypes.Cast<UnityEngine.Object>()
                    .Single(dino => dino.name.Equals("TRex", StringComparison.OrdinalIgnoreCase));
                spawner.GetType().GetProperty("ResourcesToSpend")?.SetValue(spawner, 9999);

                DinoDeploymentZone z1 = UnityEngine.Object
                    .FindObjectsByType<DinoDeploymentZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .Single(zone => zone.Id == DeploymentZoneId.Z1RuinsForecourt);
                object[] deployArguments = { tRex, ResolveZonePoint(z1, new Vector2(0.5f, 0.5f)), false, null, null };
                Assert.That((bool)spawner.GetType().GetMethod("TryDeploy").Invoke(spawner, deployArguments), Is.True);
                Component dino = (Component)deployArguments[4];
                roundManager.GetType().GetMethod("StartRound")?.Invoke(roundManager, null);

                Component graph = dino.GetComponent(RuntimeType("Unity.Behavior.BehaviorGraphAgent"));
                MethodInfo getVariable = graph.GetType().GetMethods()
                    .Single(method => method.Name == "GetVariable" && method.IsGenericMethodDefinition &&
                        method.GetParameters().Length == 2 &&
                        method.GetParameters()[0].ParameterType == typeof(string))
                    .MakeGenericMethod(typeof(List<GameObject>));
                object[] getArguments = { "NearbyAttackables", null };
                Assert.That((bool)getVariable.Invoke(graph, getArguments), Is.True);
                object nearby = getArguments[1];
                List<GameObject> nearbyValues = (List<GameObject>)nearby.GetType().GetProperty("Value").GetValue(nearby);
                float contactDeadline = Time.time + 20f;
                while (Time.time < contactDeadline && nearbyValues.Count == 0) yield return null;
                Assert.That(nearbyValues.Count, Is.GreaterThan(0),
                    "the real dinosaur must first acquire a scene target before target-loss recovery is evaluated");

                GameObject departingTarget = nearbyValues.First(target => target != null);
                Component survivorDamageable = UnityEngine.Object.FindObjectsByType<Component>(
                        FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                    .First(component => component != null && component.gameObject != departingTarget &&
                        component.GetType().GetInterfaces()
                            .Any(contract => contract.FullName == "LlamAcademy.Dinos.Unit.IDamageable") &&
                        (int)component.GetType().GetProperty("Health").GetValue(component) > 0);
                MethodInfo onTargetEnter = dino.GetType().GetMethod(
                    "OnTargetEnter", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo onTargetExit = dino.GetType().GetMethod(
                    "OnTargetExit", BindingFlags.Instance | BindingFlags.NonPublic);
                onTargetEnter.Invoke(dino, new object[] { survivorDamageable });
                Transform villageCenter = (Transform)roundManager.GetType().GetProperty("DinoTarget").GetValue(roundManager);
                NavMeshAgent agent = dino.GetComponent<NavMeshAgent>();
                Component departingDamageable = departingTarget.GetComponents<Component>()
                    .First(component => component != null && component.GetType().GetInterfaces()
                        .Any(contract => contract.FullName == "LlamAcademy.Dinos.Unit.IDamageable"));
                NavMeshQueryFilter filter = new() { agentTypeID = agent.agentTypeID, areaMask = agent.areaMask };
                Assert.That(NavMesh.SamplePosition(survivorDamageable.transform.position,
                    out NavMeshHit survivorHit, 10f, filter), Is.True);
                Assert.That(agent.SetDestination(survivorHit.position), Is.True);
                Vector3 combatDestination = agent.destination;
                onTargetExit.Invoke(dino, new object[] { departingDamageable });

                Assert.That(nearbyValues, Has.No.Member(departingTarget));
                Assert.That(nearbyValues, Has.Member(survivorDamageable.gameObject));
                Assert.That(Vector3.Distance(agent.destination, combatDestination), Is.LessThan(0.1f),
                    "losing one of multiple live targets must not overwrite the active combat path with the village target");

                agent.ResetPath();
                foreach (GameObject remainingTarget in nearbyValues.Where(target => target != null).ToArray())
                {
                    Component remainingDamageable = remainingTarget.GetComponents<Component>()
                        .First(component => component != null && component.GetType().GetInterfaces()
                            .Any(contract => contract.FullName == "LlamAcademy.Dinos.Unit.IDamageable"));
                    onTargetExit.Invoke(dino, new object[] { remainingDamageable });
                }

                Assert.That(agent.hasPath, Is.True,
                    "losing the final nearby target must immediately restore the village-center NavMesh command");
                Assert.That(Vector3.Distance(agent.destination, villageCenter.position), Is.LessThan(1f),
                    "the restored NavMesh command must target the village center, not the dead combat target");
                float distanceAtLoss = Vector3.Distance(dino.transform.position, villageCenter.position);
                float bestDistance = distanceAtLoss;
                float recoveryDeadline = Time.time + 4f;
                while (Time.time < recoveryDeadline)
                {
                    bestDistance = Mathf.Min(bestDistance,
                        Vector3.Distance(dino.transform.position, villageCenter.position));
                    if (distanceAtLoss - bestDistance > 1f) break;
                    yield return null;
                }

                Assert.That(distanceAtLoss - bestDistance, Is.GreaterThan(1f),
                    $"after losing its last target the dinosaur must resume toward the village center; " +
                    $"start={distanceAtLoss:F2}, best={bestDistance:F2}, hasPath={agent.hasPath}, " +
                    $"pathStatus={agent.pathStatus}, stopped={agent.isStopped}, velocity={agent.velocity.magnitude:F2}");
            }
            finally
            {
                Time.timeScale = originalTimeScale;
            }
        }

        [Test]
        public void DinoCrowdPolicy_DisablesPhysicalBlockingWithoutChangingNavMeshAvoidance()
        {
            GameObject first = new GameObject("FirstDinoBody");
            GameObject second = new GameObject("SecondDinoBody");
            BoxCollider firstCollider = first.AddComponent<BoxCollider>();
            BoxCollider secondCollider = second.AddComponent<BoxCollider>();

            Type policyType = RuntimeType("LlamAcademy.Dinos.Unit.DinoCrowdCollisionPolicy");
            MethodInfo ignoreMethod = policyType.GetMethod(
                "IgnoreMutualBodyCollisions", BindingFlags.Static | BindingFlags.Public);
            Assert.That(ignoreMethod, Is.Not.Null);
            ignoreMethod.Invoke(null, new object[] { new Collider[] { firstCollider }, new Collider[] { secondCollider } });

            Assert.That(Physics.GetIgnoreCollision(firstCollider, secondCollider), Is.True,
                "dinosaur bodies must be able to overlap instead of forming a traffic jam");

            UnityEngine.Object.DestroyImmediate(first);
            UnityEngine.Object.DestroyImmediate(second);
        }

        [Test]
        public void AttackPath_HasNoOcclusionQueryInvariant()
        {

            string attackSource = File.ReadAllText(Path.Combine(Application.dataPath, "LlamAcademy/Dinos/Behavior/AttackClosestObjectAction.cs"));
            string radiusSource = File.ReadAllText(Path.Combine(Application.dataPath, "LlamAcademy/Dinos/Unit/AttackRadius.cs"));
            string combined = attackSource + "\n" + radiusSource;
            string[] forbiddenOcclusionQueries =
            {
                "Physics.Raycast", "Physics.Linecast", "Physics.SphereCast", "Physics.CapsuleCast",
                "NavMesh.Raycast", "LineOfSight", "LOS", "Cover"
            };

            foreach (string query in forbiddenOcclusionQueries)
            {
                Assert.That(combined, Does.Not.Contain(query), $"The solid blocker must not cancel attacks through {query}.");
            }
        }

        private static NavMeshAgent CreateAgent(float baseSpeed)
        {
            GameObject gameObject = new GameObject("TerrainSpeedAgent");
            NavMeshAgent agent = gameObject.AddComponent<NavMeshAgent>();
            agent.speed = baseSpeed;
            return agent;
        }

        private static Vector3 ResolveZonePoint(DinoDeploymentZone zone, Vector2 relative)
        {
            Assert.That(zone, Is.Not.Null);
            Assert.That(zone.TryResolveRelative(relative, out Vector3 worldPoint), Is.True,
                $"{zone.Id} must resolve relative coordinate {relative}");
            return worldPoint;
        }

        private static TerrainSurfaceVolume CreateSurface(TerrainSurfaceKind surface)
        {
            GameObject gameObject = new GameObject(surface.ToString());
            BoxCollider collider = gameObject.AddComponent<BoxCollider>();
            collider.isTrigger = true;
            TerrainSurfaceVolume volume = gameObject.AddComponent<TerrainSurfaceVolume>();
            typeof(TerrainSurfaceVolume)
                .GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(volume, surface);
            return volume;
        }

        private static void SetSurface(TerrainSurfaceVolume volume, TerrainSurfaceKind surface)
        {
            typeof(TerrainSurfaceVolume)
                .GetField("surface", BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(volume, surface);
        }

        private static void InvokeTerrainTrigger(DinoTerrainSpeedController controller, string methodName, Collider collider)
        {
            typeof(DinoTerrainSpeedController)
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(controller, new object[] { collider });
        }

        private static void InvokeCameraControl(Component control, string methodName, params object[] arguments)
        {
            MethodInfo method = control.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null, $"CameraControl must implement {methodName}.");
            method.Invoke(control, arguments);
        }

        private static Transform CameraFollowTarget(Component control)
        {
            Component camera = control.GetComponent(RuntimeType("Unity.Cinemachine.CinemachineCamera"));
            return (Transform)camera.GetType().GetProperty("Follow").GetValue(camera);
        }

        private static Func<bool> SetCameraFocusProvider(Component control, bool isFocused)
        {
            FieldInfo field = control.GetType().GetField("ApplicationFocusProvider", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "CameraControl must expose one controllable application-focus provider for pointer gating tests.");
            Func<bool> injectedProvider = new(() => isFocused);
            field.SetValue(control, injectedProvider);
            return injectedProvider;
        }

        private static Func<bool> GetCameraFocusProvider(Component control)
        {
            FieldInfo field = control.GetType().GetField("ApplicationFocusProvider", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, "CameraControl must expose one controllable application-focus provider for pointer gating tests.");
            return (Func<bool>)field.GetValue(control);
        }

        private static void RestoreCameraFocusProvider(
            Component control,
            Func<bool> originalProvider,
            bool originalResult)
        {
            FieldInfo field = control.GetType().GetField("ApplicationFocusProvider", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null);
            field.SetValue(control, originalProvider);
            Func<bool> restoredProvider = (Func<bool>)field.GetValue(control);
            Assert.That(restoredProvider, Is.SameAs(originalProvider),
                "teardown must restore the exact production focus delegate instance");
            Assert.That(restoredProvider(), Is.EqualTo(originalResult),
                "the restored production focus delegate must preserve its original result");
        }

        private static System.Collections.IEnumerator Cleanup(params GameObject[] gameObjects)
        {
            foreach (GameObject gameObject in gameObjects)
            {
                UnityEngine.Object.Destroy(gameObject);
            }

            yield return null;
        }

        private static GameObject CapturedAttacker;
        private static GameObject CapturedTarget;

        private static void CaptureAttackEvent(GameObject attacker, GameObject target)
        {
            CapturedAttacker = attacker;
            CapturedTarget = target;
        }

        private static Type RuntimeType(string fullName)
        {
            Type type = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(candidate => candidate != null);
            Assert.That(type, Is.Not.Null, $"Missing runtime type {fullName}.");
            return type;
        }

        private static float[] ReadStream(object frame, string propertyName)
        {
            PropertyInfo property = frame.GetType().GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public);
            if (property != null)
            {
                return (float[])property.GetValue(frame);
            }

            FieldInfo field = frame.GetType().GetField(propertyName, BindingFlags.Instance | BindingFlags.Public);
            Assert.That(field, Is.Not.Null, $"Missing observation stream {propertyName}.");
            return (float[])field.GetValue(frame);
        }

        private static float NormalizeObservationCoordinate(Type builderType, float value, float minimum, float maximum)
        {
            MethodInfo normalize = builderType.GetMethod("NormalizeCoordinate", BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(normalize, Is.Not.Null, "the builder must own the shared world-bound normalization");
            return (float)normalize.Invoke(null, new object[] { value, minimum, maximum });
        }

        private static void AssertInvalidObservationContract(MethodInfo buildFrame, Component builder, string contractName)
        {
            TargetInvocationException invocation = Assert.Throws<TargetInvocationException>(() => buildFrame.Invoke(builder, null));
            Assert.That(invocation.InnerException, Is.TypeOf<InvalidOperationException>());
            Assert.That(invocation.InnerException.Message, Does.Contain(contractName));
        }

        private static IEnumerable<float[]> ReadRows(object frame, string propertyName, int width)
        {
            float[] stream = ReadStream(frame, propertyName);
            Assert.That(stream.Length % width, Is.Zero);
            return Enumerable.Range(0, stream.Length / width)
                .Select(row => stream.Skip(row * width).Take(width).ToArray());
        }

        private static int[] ReadObservationShape(object spec)
        {
            object shape = spec.GetType().GetProperty("Shape").GetValue(spec);
            Type shapeType = shape.GetType();
            int rank = (int)shapeType.GetProperty("Length").GetValue(shape);
            PropertyInfo indexer = shapeType.GetProperty("Item");
            return Enumerable.Range(0, rank)
                .Select(index => (int)indexer.GetValue(shape, new object[] { index }))
                .ToArray();
        }

        private static UnityEngine.Object LoadAssetAtPath(string path)
        {
            Type databaseType = RuntimeType("UnityEditor.AssetDatabase");
            MethodInfo loadMethod = databaseType.GetMethod("LoadMainAssetAtPath", BindingFlags.Public | BindingFlags.Static);
            return (UnityEngine.Object)loadMethod.Invoke(null, new object[] { path });
        }

        private static void LoadEditorSceneInPlayMode(string path)
        {
            Type editorSceneManager = RuntimeType("UnityEditor.SceneManagement.EditorSceneManager");
            MethodInfo load = editorSceneManager.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Single(method => method.Name == "LoadSceneInPlayMode" && method.GetParameters().Length == 2);
            load.Invoke(null, new object[] { path, new LoadSceneParameters(LoadSceneMode.Single) });
        }

        private static object CreateBlackboardVariable(Type valueType, object value)
        {
            Type variableType = RuntimeType("Unity.Behavior.BlackboardVariable`1").MakeGenericType(valueType);
            object variable = Activator.CreateInstance(variableType);
            variableType.GetProperty("Value").SetValue(variable, value);
            return variable;
        }

        private static void SetActionField(object action, string fieldName, object value)
        {
            action.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.Public).SetValue(action, value);
        }

        private static object InvokeAction(object action, string methodName)
        {
            return action.GetType()
                .GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic)
                .Invoke(action, null);
        }

        private static void ForceAttackCooldownReady(object action)
        {
            System.Collections.IDictionary attackTimes = (System.Collections.IDictionary)action.GetType()
                .GetField("AttackTimes", BindingFlags.Instance | BindingFlags.NonPublic)
                .GetValue(action);
            foreach (object attack in attackTimes.Keys.Cast<object>().ToArray())
            {
                attackTimes[attack] = -100L;
            }
        }
    }
}
