using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using LlamAcademy.Dinos.Inference;
using Unity.InferenceEngine;
using UnityEditor;
using UnityEngine;

namespace LlamAcademy.Dinos.Inference.Editor
{
    public sealed class DinoSentisFeasibilityResult
    {
        public string ModelAssetPath { get; }
        public string ModelSha256 { get; }
        public string[] InputNames { get; }
        public string OutputName { get; }
        public int[] OutputShape { get; }
        public float[] Actions { get; }
        public string[] Operators { get; }

        public DinoSentisFeasibilityResult(
            string modelAssetPath,
            string modelSha256,
            string[] inputNames,
            string outputName,
            int[] outputShape,
            float[] actions,
            string[] operators)
        {
            ModelAssetPath = modelAssetPath;
            ModelSha256 = modelSha256;
            InputNames = inputNames;
            OutputName = outputName;
            OutputShape = outputShape;
            Actions = actions;
            Operators = operators;
        }
    }

    public static class DinoSentisFeasibilityCheck
    {
        public const string FixtureAssetPath =
            "Assets/LlamAcademy/Dinos/Inference/Fixtures/DinoV2SetTransformerFeasibility.onnx";
        public const string ExpectedModelSha256 =
            "aa62da1118ec26888200765d1510802ed7f7e647911e4bd512f36d614eac4d6c";

        [MenuItem("Dino Attack/Inference/Run Sentis Feasibility Check")]
        public static void RunMenu()
        {
            try
            {
                DinoSentisFeasibilityResult result = Run();
                Debug.Log(
                    "DINO_SENTIS_FEASIBILITY PASS" +
                    $" model_sha256={result.ModelSha256}" +
                    $" inputs={string.Join(",", result.InputNames)}" +
                    $" output={result.OutputName}({string.Join(",", result.OutputShape)})" +
                    $" actions=[{string.Join(",", result.Actions.Select(value => value.ToString("R")))}]" +
                    $" operators=[{string.Join(",", result.Operators)}]");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                throw;
            }
        }

        public static DinoSentisFeasibilityResult Run()
        {
            AssetDatabase.ImportAsset(FixtureAssetPath, ImportAssetOptions.ForceSynchronousImport);
            ModelAsset modelAsset = AssetDatabase.LoadAssetAtPath<ModelAsset>(FixtureAssetPath);
            if (modelAsset == null)
            {
                throw new InvalidOperationException($"Sentis did not import {FixtureAssetPath} as a ModelAsset.");
            }

            string filesystemPath = Path.GetFullPath(FixtureAssetPath);
            string modelSha256 = ComputeSha256(filesystemPath);
            if (modelSha256 != ExpectedModelSha256)
            {
                throw new InvalidOperationException(
                    $"Feasibility fixture hash mismatch: expected {ExpectedModelSha256}, got {modelSha256}.");
            }

            DinoInferenceModelContract contract =
                DinoInferenceModelContract.CreateFeasibility(modelSha256);
            Model model = ModelLoader.Load(modelAsset);
            string[] inputNames = model.inputs.Select(input => input.name).ToArray();
            if (model.outputs.Count != 1)
            {
                throw new InvalidOperationException("Sentis model must expose exactly one output.");
            }

            Tensor[] inputs = CreateZeroInputs(contract.Inputs);
            try
            {
                using Worker worker = new Worker(model, BackendType.CPU);
                worker.Schedule(inputs);
                Tensor<float> output = worker.PeekOutput("actions") as Tensor<float>;
                if (output == null)
                {
                    throw new InvalidOperationException("Sentis output 'actions' is not float32.");
                }

                using Tensor<float> cpuOutput = output.ReadbackAndClone();
                float[] actions = cpuOutput.DownloadToArray();
                int[] outputShape = Enumerable.Range(0, cpuOutput.shape.rank)
                    .Select(index => cpuOutput.shape[index])
                    .ToArray();
                contract.ValidateModel(inputNames, model.outputs[0].name, outputShape);
                contract.ValidateActions(actions);

                return new DinoSentisFeasibilityResult(
                    FixtureAssetPath,
                    modelSha256,
                    inputNames,
                    model.outputs[0].name,
                    outputShape,
                    actions,
                    model.layers.Select(layer => layer.GetType().Name)
                        .Distinct()
                        .OrderBy(name => name, StringComparer.Ordinal)
                        .ToArray());
            }
            finally
            {
                for (int index = 0; index < inputs.Length; index++)
                {
                    inputs[index]?.Dispose();
                }
            }
        }

        private static Tensor[] CreateZeroInputs(
            IReadOnlyList<DinoInferenceTensorContract> contracts)
        {
            Tensor[] inputs = new Tensor[contracts.Count];
            for (int index = 0; index < contracts.Count; index++)
            {
                int[] featureShape = contracts[index].Shape.ToArray();
                int[] concreteShape = new int[featureShape.Length + 1];
                concreteShape[0] = 1;
                Array.Copy(featureShape, 0, concreteShape, 1, featureShape.Length);
                inputs[index] = new Tensor<float>(new TensorShape(concreteShape));
            }

            return inputs;
        }

        private static string ComputeSha256(string path)
        {
            using SHA256 sha256 = SHA256.Create();
            using FileStream stream = File.OpenRead(path);
            return string.Concat(sha256.ComputeHash(stream).Select(value => value.ToString("x2")));
        }
    }
}
