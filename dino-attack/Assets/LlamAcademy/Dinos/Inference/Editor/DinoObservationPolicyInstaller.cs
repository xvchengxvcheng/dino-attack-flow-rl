using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace LlamAcademy.Dinos.Inference.Editor
{
    public static class DinoObservationPolicyInstaller
    {
        public const string ModelAssetPath =
            "Assets/Resources/DinoInference/PPONavMesh16EnvSeed0TimeScale5Final4721698.onnx";
        public const string ConfigAssetPath =
            "Assets/Resources/DinoInference/DefaultModel.asset";
        public const string ModelId = "ppo-navmesh-16env-seed0-timescale5-final-4721698";
        public const string ExpectedModelSha256 =
            "30322b112c760b739d65347bae6c115d80e49c1fc0b51062554c7e223f88a954";
        public const string FpoModelAssetPath =
            "Assets/Resources/DinoInference/FPOUpdate160Step2606819.onnx";
        public const string FpoConfigAssetPath =
            "Assets/Resources/DinoInference/FpoModel.asset";
        public const string FpoModelId =
            "fpo-fixedclock-v5-seed0-update160-policy159-step2606819";
        public const string ExpectedFpoModelSha256 =
            "faef7069fec1131b55a68fb57034a063d811f2d7e300187c45a911e6c14cf6eb";

        [MenuItem("Dino Attack/Inference/Install NavMesh 16-env PPO Policy")]
        public static void Install()
        {
            InstallModel(
                ModelAssetPath,
                ConfigAssetPath,
                ModelId,
                ExpectedModelSha256,
                20260903,
                "DINO_NAVMESH_16ENV_POLICY_INSTALL");
        }

        [MenuItem("Dino Attack/Inference/Install FPO Update 160 Policy")]
        public static void InstallFpo()
        {
            InstallModel(
                FpoModelAssetPath,
                FpoConfigAssetPath,
                FpoModelId,
                ExpectedFpoModelSha256,
                20260905,
                "DINO_FPO_UPDATE160_POLICY_INSTALL");
        }

        [MenuItem("Dino Attack/Inference/Install PolicyFlow")]
        public static void InstallPolicyFlow()
        {
            InstallModel("Assets/Resources/DinoInference/PolicyFlowStep2147452.onnx",
                "Assets/Resources/DinoInference/PolicyFlowModel.asset",
                "policyflow-seed0-step2147452",
                "ce3e5243e3d1951c750eb8ab8e7b8a2ff8cb30437a61fa94c63076b6c7ffa39e",
                20260905, "DINO_POLICYFLOW_INSTALL");
        }

        private static void InstallModel(
            string modelPath,
            string configPath,
            string modelId,
            string expectedHash,
            int inferenceSeed,
            string logToken)
        {
            AssetDatabase.ImportAsset(modelPath, ImportAssetOptions.ForceSynchronousImport);
            ModelAsset modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(modelPath);
            if (modelAsset == null)
            {
                throw new InvalidOperationException(
                    $"Unity AI Inference did not import {modelPath} as a ModelAsset.");
            }

            string actualHash = ComputeSha256(Path.GetFullPath(modelPath));
            if (!string.Equals(actualHash, expectedHash, StringComparison.Ordinal))
            {
                throw new InvalidOperationException(
                    $"Inference policy hash mismatch: expected {expectedHash}, got {actualHash}.");
            }

            DinoInferenceModelConfig config =
                AssetDatabase.LoadAssetAtPath<DinoInferenceModelConfig>(configPath);
            bool created = config == null;
            if (created)
            {
                config = ScriptableObject.CreateInstance<DinoInferenceModelConfig>();
                AssetDatabase.CreateAsset(config, configPath);
            }

            SerializedObject serialized = new(config);
            serialized.FindProperty("ModelAsset").objectReferenceValue = modelAsset;
            serialized.FindProperty("ModelId").stringValue = modelId;
            serialized.FindProperty("ModelSha256").stringValue = actualHash;
            serialized.FindProperty("ProtocolVersion").stringValue =
                DinoInferenceModelContract.V2ProtocolVersion;
            serialized.FindProperty("ArtifactKind").stringValue =
                DinoInferenceModelContract.TrainedCheckpointArtifactKind;
            SerializedProperty inputNames = serialized.FindProperty("InputNames");
            string[] names = { "global", "regions", "walls", "guards", "houses", "dinos" };
            inputNames.arraySize = names.Length;
            for (int index = 0; index < names.Length; index++)
            {
                inputNames.GetArrayElementAtIndex(index).stringValue = names[index];
            }

            serialized.FindProperty("OutputName").stringValue = "actions";
            serialized.FindProperty("DefaultInferenceSeed").intValue = inferenceSeed;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(config);
            AssetDatabase.SaveAssets();

            if (!DinoSentisPolicyRunner.TryValidateConfiguredModel(config, out string reason))
            {
                throw new InvalidOperationException(
                    $"Inference policy failed Unity AI Inference validation: {reason}");
            }

            Debug.Log(
                $"{logToken} PASS" +
                $" created={created}" +
                $" model_id={modelId}" +
                $" model_sha256={actualHash}" +
                $" inputs={string.Join(",", config.ConfiguredInputNames)}" +
                $" output={config.ConfiguredOutputName}");
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return string.Concat(sha256.ComputeHash(stream).Select(value => value.ToString("x2")));
        }
    }
}
