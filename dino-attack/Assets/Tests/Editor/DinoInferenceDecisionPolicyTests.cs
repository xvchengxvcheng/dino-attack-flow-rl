using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using LlamAcademy.Dinos.Inference;
using LlamAcademy.Dinos.Session;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class DinoInferenceDecisionPolicyTests
    {
        [Test]
        public void RunningScheduleStartsImmediatelyThenWaitsTwentyFiveAcademySteps()
        {
            DinoInferenceDecisionPolicy policy = new();

            Assert.That(policy.TryBeginDecision(false, 100), Is.False);
            Assert.That(policy.TryBeginDecision(true, 100), Is.True);
            Assert.That(policy.TryBeginDecision(true, 100), Is.False, "The same Academy step must not run twice.");
            Assert.That(policy.TryBeginDecision(true, 124), Is.False);
            Assert.That(policy.TryBeginDecision(true, 125), Is.True);
            Assert.That(policy.TryBeginDecision(true, 149), Is.False);
            Assert.That(policy.TryBeginDecision(true, 150), Is.True);

            Assert.That(policy.TryBeginDecision(false, 151), Is.False);
            Assert.That(policy.TryBeginDecision(true, 151), Is.True,
                "Leaving Running resets the schedule for the next session.");
        }

        [Test]
        public void ActionSanitizerRejectsWrongShapeAndNonFiniteBeforeClampingBounds()
        {
            DinoInferenceDecisionPolicy policy = new();

            Assert.That(policy.TrySanitizeActions(new[] { 0f, 0f, 0f }, out _, out _), Is.False);
            Assert.That(policy.TrySanitizeActions(new[] { 0f, float.NaN, 0f, 0f }, out _, out _), Is.False);
            Assert.That(policy.TrySanitizeActions(new[] { 0f, 0f, float.PositiveInfinity, 0f }, out _, out _), Is.False);

            Assert.That(
                policy.TrySanitizeActions(new[] { -2f, -0.25f, 0.5f, 3f }, out float[] actions, out string reason),
                Is.True,
                reason);
            Assert.That(actions, Is.EqualTo(new[] { -1f, -0.25f, 0.5f, 1f }));
        }

        [Test]
        public void DefaultModelConfigFailsClosedForMissingModelAndKnownFeasibilityFixture()
        {
            Type configType = Type.GetType(
                "LlamAcademy.Dinos.Inference.DinoInferenceModelConfig, Assembly-CSharp");
            Assert.That(configType, Is.Not.Null, "The runtime default-model configuration type is missing.");

            ScriptableObject config = ScriptableObject.CreateInstance(configType);
            try
            {
                MethodInfo validate = configType.GetMethod(
                    "TryValidateForDefault",
                    BindingFlags.Instance | BindingFlags.Public);
                Assert.That(validate, Is.Not.Null);

                object[] missingArgs = { null };
                Assert.That((bool)validate.Invoke(config, missingArgs), Is.False);
                Assert.That((string)missingArgs[0], Does.Contain("model").IgnoreCase);

                UnityEngine.Object fixture = AssetDatabase.LoadMainAssetAtPath(
                    "Assets/LlamAcademy/Dinos/Inference/Fixtures/DinoV2SetTransformerFeasibility.onnx");
                Assert.That(fixture, Is.Not.Null);
                SerializedObject serialized = new(config);
                serialized.FindProperty("ModelAsset").objectReferenceValue = fixture;
                serialized.FindProperty("ModelId").stringValue = "fixture-must-not-be-default";
                serialized.FindProperty("ModelSha256").stringValue =
                    "aa62da1118ec26888200765d1510802ed7f7e647911e4bd512f36d614eac4d6c";
                serialized.FindProperty("ProtocolVersion").stringValue =
                    DinoInferenceModelContract.V2ProtocolVersion;
                serialized.FindProperty("ArtifactKind").stringValue =
                    DinoInferenceModelContract.FeasibilityArtifactKind;
                serialized.FindProperty("InputNames").arraySize = 6;
                string[] names = { "global", "regions", "walls", "guards", "houses", "dinos" };
                for (int index = 0; index < names.Length; index++)
                {
                    serialized.FindProperty("InputNames").GetArrayElementAtIndex(index).stringValue = names[index];
                }
                serialized.FindProperty("OutputName").stringValue = "actions";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                object[] fixtureArgs = { null };
                Assert.That((bool)validate.Invoke(config, fixtureArgs), Is.False);
                Assert.That((string)fixtureArgs[0], Does.Contain("trained").IgnoreCase);

                serialized.FindProperty("ArtifactKind").stringValue =
                    DinoInferenceModelContract.TrainedCheckpointArtifactKind;
                serialized.ApplyModifiedPropertiesWithoutUndo();
                object[] disguisedFixtureArgs = { null };
                Assert.That((bool)validate.Invoke(config, disguisedFixtureArgs), Is.False,
                    "The known feasibility bytes remain ineligible even if their artifact kind is relabeled.");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void EligibleBaseConfigReachesAndRejectsWrongProtocolBranch()
        {
            ScriptableObject config = CreateEligibleSyntheticConfig(
                out ScriptableObject syntheticModel,
                out SerializedObject serialized,
                out MethodInfo validate);
            try
            {
                serialized.FindProperty("ProtocolVersion").stringValue = "wrong_protocol";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                object[] args = { null };
                Assert.That((bool)validate.Invoke(config, args), Is.False);
                Assert.That((string)args[0], Does.Contain("v2 protocol").IgnoreCase);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(syntheticModel);
            }
        }

        [Test]
        public void EligibleBaseConfigReachesAndRejectsWrongInputNameAndOrderContract()
        {
            ScriptableObject config = CreateEligibleSyntheticConfig(
                out ScriptableObject syntheticModel,
                out SerializedObject serialized,
                out MethodInfo validate);
            try
            {
                DinoInferenceModelContract contract = DinoInferenceModelContract.CreateFromManifest(
                    DinoInferenceModelContract.V2ProtocolVersion,
                    DinoInferenceModelContract.TrainedCheckpointArtifactKind,
                    new string('b', 64));
                Assert.That(contract.Inputs[2].Name, Is.EqualTo("walls"));
                Assert.That(contract.Inputs[2].Shape, Is.EqualTo(new[] { 6, 5 }));
                Assert.That(contract.Inputs[3].Name, Is.EqualTo("guards"));
                Assert.That(contract.Inputs[3].Shape, Is.EqualTo(new[] { 11, 7 }));

                serialized.FindProperty("InputNames").GetArrayElementAtIndex(2).stringValue = "guards";
                serialized.FindProperty("InputNames").GetArrayElementAtIndex(3).stringValue = "walls";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                object[] args = { null };
                Assert.That((bool)validate.Invoke(config, args), Is.False);
                Assert.That((string)args[0], Does.Contain("input 2").And.Contain("walls"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(syntheticModel);
            }
        }

        [Test]
        public void EligibleBaseConfigReachesAndRejectsWrongOutputContract()
        {
            ScriptableObject config = CreateEligibleSyntheticConfig(
                out ScriptableObject syntheticModel,
                out SerializedObject serialized,
                out MethodInfo validate);
            try
            {
                serialized.FindProperty("OutputName").stringValue = "wrong_actions";
                serialized.ApplyModifiedPropertiesWithoutUndo();

                object[] args = { null };
                Assert.That((bool)validate.Invoke(config, args), Is.False);
                Assert.That((string)args[0], Does.Contain("output").And.Contain("actions"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(config);
                UnityEngine.Object.DestroyImmediate(syntheticModel);
            }
        }


        [Test]
        public void CompactPostfixTunedSeed0CheckpointIsConfiguredAsTheDefaultStrategy()
        {
            Type configType = Type.GetType(
                "LlamAcademy.Dinos.Inference.DinoInferenceModelConfig, Assembly-CSharp");
            Type runnerType = Type.GetType(
                "LlamAcademy.Dinos.Inference.DinoSentisPolicyRunner, Assembly-CSharp");
            Assert.That(configType, Is.Not.Null);
            Assert.That(runnerType, Is.Not.Null);

            MethodInfo loadDefault = configType.GetMethod("LoadDefault", BindingFlags.Public | BindingFlags.Static);
            ScriptableObject config = loadDefault.Invoke(null, null) as ScriptableObject;
            Assert.That(config, Is.Not.Null,
                "The selected compact tuned PPO policy must be available through Resources.");
            Assert.That(
                configType.GetProperty("StableModelId")?.GetValue(config),
                Is.EqualTo("ppo-compact-postfix-tuned-seed0-1574283"));
            Assert.That(
                configType.GetProperty("Protocol")?.GetValue(config),
                Is.EqualTo(DinoInferenceModelContract.V2ProtocolVersion));
            Assert.That(
                configType.GetProperty("Kind")?.GetValue(config),
                Is.EqualTo(DinoInferenceModelContract.TrainedCheckpointArtifactKind));
            UnityEngine.Object modelAsset =
                configType.GetProperty("Asset")?.GetValue(config) as UnityEngine.Object;
            Assert.That(modelAsset, Is.Not.Null);
            Assert.That(modelAsset.name, Is.EqualTo("PPOCompactPostfixTunedSeed0_1574283"));
            string configuredHash = configType.GetProperty("ModelHash")?.GetValue(config) as string;
            string modelPath = AssetDatabase.GetAssetPath(modelAsset);
            Assert.That(configuredHash, Is.EqualTo(ComputeSha256(modelPath)),
                "DefaultModel must pin the exact ONNX bytes imported by Unity.");

            MethodInfo validate = runnerType.GetMethod(
                "TryValidateConfiguredModel", BindingFlags.Public | BindingFlags.Static);
            object[] args = { config, null };
            Assert.That((bool)validate.Invoke(null, args), Is.True, args[1] as string);
            Assert.That((string)args[1], Is.Empty);
        }

        [Test]
        public void FpoUpdate160CheckpointIsConfiguredAsIndependentAiStrategy()
        {
            Type configType = Type.GetType(
                "LlamAcademy.Dinos.Inference.DinoInferenceModelConfig, Assembly-CSharp");
            Type runnerType = Type.GetType(
                "LlamAcademy.Dinos.Inference.DinoSentisPolicyRunner, Assembly-CSharp");
            Assert.That(configType, Is.Not.Null);
            Assert.That(runnerType, Is.Not.Null);

            MethodInfo loadFpo = configType.GetMethod(
                "LoadFpo", BindingFlags.Public | BindingFlags.Static);
            Assert.That(loadFpo, Is.Not.Null);
            ScriptableObject config = loadFpo.Invoke(null, null) as ScriptableObject;
            Assert.That(config, Is.Not.Null,
                "The selected FPO policy must be available through Resources.");
            Assert.That(
                configType.GetProperty("StableModelId")?.GetValue(config),
                Is.EqualTo("fpo-fixedclock-v5-seed0-update160-policy159-step2606819"));
            Assert.That(
                configType.GetProperty("Protocol")?.GetValue(config),
                Is.EqualTo(DinoInferenceModelContract.V2ProtocolVersion));
            Assert.That(
                configType.GetProperty("Kind")?.GetValue(config),
                Is.EqualTo(DinoInferenceModelContract.TrainedCheckpointArtifactKind));
            UnityEngine.Object modelAsset =
                configType.GetProperty("Asset")?.GetValue(config) as UnityEngine.Object;
            Assert.That(modelAsset, Is.Not.Null);
            Assert.That(modelAsset.name, Is.EqualTo("FPOUpdate160Step2606819"));
            string configuredHash = configType.GetProperty("ModelHash")?.GetValue(config) as string;
            Assert.That(configuredHash, Is.EqualTo(ComputeSha256(AssetDatabase.GetAssetPath(modelAsset))));

            MethodInfo validate = runnerType.GetMethod(
                "TryValidateConfiguredModel", BindingFlags.Public | BindingFlags.Static);
            object[] args = { config, null };
            Assert.That((bool)validate.Invoke(null, args), Is.True, args[1] as string);
            Assert.That((string)args[1], Is.Empty);
        }

        [Test]
        public void FpoLaunchRequestResolvesTheIndependentFpoResource()
        {
            Type configType = Type.GetType(
                "LlamAcademy.Dinos.Inference.DinoInferenceModelConfig, Assembly-CSharp");
            Assert.That(configType, Is.Not.Null);
            ScriptableObject fpo = configType.GetMethod(
                "LoadFpo", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null)
                as ScriptableObject;
            Assert.That(fpo, Is.Not.Null);
            object option = configType.GetMethod(
                "ToLaunchOption", BindingFlags.Public | BindingFlags.Instance)?.Invoke(fpo, null);
            Assert.That(option, Is.Not.Null);
            Type optionType = option.GetType();
            Assert.That((bool)optionType.GetProperty("IsAvailable")?.GetValue(option), Is.True);
            GameLaunchRequest request = new(
                GameMapId.OpenTropicalBattlefield,
                160,
                GameControllerMode.InferenceAI,
                (int)optionType.GetProperty("InferenceSeed")?.GetValue(option),
                (string)optionType.GetProperty("ModelId")?.GetValue(option),
                (string)optionType.GetProperty("ModelHash")?.GetValue(option),
                (string)optionType.GetProperty("ProtocolVersion")?.GetValue(option),
                true,
                true);

            ScriptableObject resolved = configType.GetMethod(
                "LoadForRequest", BindingFlags.Public | BindingFlags.Static)?.Invoke(
                null,
                new object[] { request }) as ScriptableObject;
            Assert.That(resolved, Is.SameAs(fpo));
        }

        [Test]
        public void PolicyFlowLaunchRequestResolvesTheIndependentPolicyFlowResource()
        {
            Type configType = Type.GetType(
                "LlamAcademy.Dinos.Inference.DinoInferenceModelConfig, Assembly-CSharp");
            Assert.That(configType, Is.Not.Null);
            ScriptableObject policyFlow = configType.GetMethod(
                "LoadPolicyFlow", BindingFlags.Public | BindingFlags.Static)?.Invoke(null, null)
                as ScriptableObject;
            Assert.That(policyFlow, Is.Not.Null);
            object option = configType.GetMethod(
                "ToLaunchOption", BindingFlags.Public | BindingFlags.Instance)?.Invoke(policyFlow, null);
            Assert.That(option, Is.Not.Null);
            Type optionType = option.GetType();
            Assert.That((bool)optionType.GetProperty("IsAvailable")?.GetValue(option), Is.True);
            GameLaunchRequest request = new(
                GameMapId.OpenTropicalBattlefield,
                160,
                GameControllerMode.InferenceAI,
                (int)optionType.GetProperty("InferenceSeed")?.GetValue(option),
                (string)optionType.GetProperty("ModelId")?.GetValue(option),
                (string)optionType.GetProperty("ModelHash")?.GetValue(option),
                (string)optionType.GetProperty("ProtocolVersion")?.GetValue(option),
                true,
                true);

            ScriptableObject resolved = configType.GetMethod(
                "LoadForRequest", BindingFlags.Public | BindingFlags.Static)?.Invoke(
                null,
                new object[] { request }) as ScriptableObject;
            Assert.That(resolved, Is.SameAs(policyFlow));
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return string.Concat(sha256.ComputeHash(stream).Select(value => value.ToString("x2")));
        }

        private static ScriptableObject CreateEligibleSyntheticConfig(
            out ScriptableObject syntheticModel,
            out SerializedObject serialized,
            out MethodInfo validate)
        {
            Type configType = Type.GetType(
                "LlamAcademy.Dinos.Inference.DinoInferenceModelConfig, Assembly-CSharp");
            Assert.That(configType, Is.Not.Null);
            ScriptableObject config = ScriptableObject.CreateInstance(configType);
            Type modelAssetType = configType
                .GetField("ModelAsset", BindingFlags.Instance | BindingFlags.NonPublic)
                .FieldType;
            syntheticModel = ScriptableObject.CreateInstance(modelAssetType);
            syntheticModel.name = "SyntheticEligibleModelForContractTests";

            serialized = new SerializedObject(config);
            serialized.FindProperty("ModelAsset").objectReferenceValue = syntheticModel;
            serialized.FindProperty("ModelId").stringValue = "synthetic-eligible-model";
            serialized.FindProperty("ModelSha256").stringValue = new string('b', 64);
            serialized.FindProperty("ProtocolVersion").stringValue =
                DinoInferenceModelContract.V2ProtocolVersion;
            serialized.FindProperty("ArtifactKind").stringValue =
                DinoInferenceModelContract.TrainedCheckpointArtifactKind;
            serialized.FindProperty("InputNames").arraySize = 6;
            string[] names = { "global", "regions", "walls", "guards", "houses", "dinos" };
            for (int index = 0; index < names.Length; index++)
            {
                serialized.FindProperty("InputNames").GetArrayElementAtIndex(index).stringValue = names[index];
            }
            serialized.FindProperty("OutputName").stringValue = "actions";
            serialized.FindProperty("DefaultInferenceSeed").intValue = 20260831;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            validate = configType.GetMethod(
                "TryValidateForDefault", BindingFlags.Instance | BindingFlags.Public);
            Assert.That(validate, Is.Not.Null);
            object[] eligibleArgs = { null };
            Assert.That((bool)validate.Invoke(config, eligibleArgs), Is.True, eligibleArgs[0] as string);
            return config;
        }
    }
}
