using System;
using System.Linq;
using LlamAcademy.Dinos.Training;
using Unity.InferenceEngine;

namespace LlamAcademy.Dinos.Inference
{
    public sealed class DinoSentisPolicyRunner : IDinoInferencePolicyRunner, IDisposable
    {
        private readonly Worker Worker;
        private readonly string OutputName;
        private bool IsDisposed;

        private DinoSentisPolicyRunner(Worker worker, string outputName)
        {
            Worker = worker;
            OutputName = outputName;
        }

        public static bool TryCreate(
            DinoInferenceModelConfig config,
            out DinoSentisPolicyRunner runner,
            out string failureReason)
        {
            runner = null;
            if (config == null)
            {
                failureReason = "No inference model configuration is available.";
                return false;
            }

            if (!config.TryValidateForDefault(out failureReason))
            {
                return false;
            }

            try
            {
                Model model = ModelLoader.Load(config.Asset);
                string[] inputNames = model.inputs.Select(input => input.name).ToArray();
                if (model.outputs.Count != 1)
                {
                    failureReason = "The inference model must expose exactly one output.";
                    return false;
                }

                DinoInferenceModelContract contract = DinoInferenceModelContract.CreateFromManifest(
                    config.Protocol,
                    config.Kind,
                    config.ModelHash);
                contract.ValidateModel(
                    inputNames,
                    model.outputs[0].name,
                    new[] { 1, DinoInferenceDecisionPolicy.ActionSize });
                runner = new DinoSentisPolicyRunner(
                    new Worker(model, BackendType.CPU),
                    config.ConfiguredOutputName);
                failureReason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                runner?.Dispose();
                runner = null;
                failureReason = $"The inference model could not be loaded: {exception.Message}";
                return false;
            }
        }

        public static bool TryValidateConfiguredModel(
            DinoInferenceModelConfig config,
            out string failureReason)
        {
            if (!TryCreate(config, out DinoSentisPolicyRunner runner, out failureReason))
            {
                return false;
            }

            try
            {
                DinoStructuredObservationFrame zeroFrame = new();
                if (!runner.TryRun(zeroFrame, out float[] actions, out failureReason))
                {
                    return false;
                }

                DinoInferenceDecisionPolicy policy = new();
                return policy.TrySanitizeActions(actions, out _, out failureReason);
            }
            finally
            {
                runner.Dispose();
            }
        }

        public bool TryRun(
            DinoStructuredObservationFrame frame,
            out float[] actions,
            out string failureReason)
        {
            actions = null;
            if (IsDisposed)
            {
                failureReason = "The inference runner is disposed.";
                return false;
            }

            if (frame == null)
            {
                failureReason = "The structured observation frame is missing.";
                return false;
            }

            Tensor[] inputs = null;
            try
            {
                frame.Validate();
                inputs = CreateInputs(frame);
                Worker.Schedule(inputs);
                Tensor<float> output = Worker.PeekOutput(OutputName) as Tensor<float>;
                if (output == null)
                {
                    failureReason = $"Inference output '{OutputName}' is not float32.";
                    return false;
                }

                using Tensor<float> cpuOutput = output.ReadbackAndClone();
                if (cpuOutput.shape.rank != 2 || cpuOutput.shape[0] != 1 ||
                    cpuOutput.shape[1] != DinoInferenceDecisionPolicy.ActionSize)
                {
                    failureReason = "Inference output must have shape (1, 4).";
                    return false;
                }

                actions = cpuOutput.DownloadToArray();
                failureReason = string.Empty;
                return true;
            }
            catch (Exception exception)
            {
                actions = null;
                failureReason = $"Inference execution failed: {exception.Message}";
                return false;
            }
            finally
            {
                if (inputs != null)
                {
                    foreach (Tensor input in inputs)
                    {
                        input?.Dispose();
                    }
                }
            }
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            IsDisposed = true;
            Worker?.Dispose();
        }

        private static Tensor[] CreateInputs(DinoStructuredObservationFrame frame) => new Tensor[]
        {
            new Tensor<float>(new TensorShape(1, 5), frame.Global),
            new Tensor<float>(new TensorShape(1, 5, 8), frame.Regions),
            new Tensor<float>(new TensorShape(1, 6, 5), frame.Walls),
            new Tensor<float>(new TensorShape(1, 11, 7), frame.Guards),
            new Tensor<float>(new TensorShape(1, 8, 6), frame.Houses),
            new Tensor<float>(new TensorShape(1, 10, 7), frame.Dinos)
        };
    }
}
