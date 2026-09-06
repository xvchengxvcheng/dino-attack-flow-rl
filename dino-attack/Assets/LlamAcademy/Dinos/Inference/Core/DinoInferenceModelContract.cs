using System;
using System.Collections.Generic;

namespace LlamAcademy.Dinos.Inference
{
    public sealed class DinoInferenceTensorContract
    {
        private readonly int[] _shape;

        public string Name { get; }
        public IReadOnlyList<int> Shape => _shape;

        public DinoInferenceTensorContract(string name, params int[] shape)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                throw new ArgumentException("Tensor name is required.", nameof(name));
            }

            if (shape == null || shape.Length == 0)
            {
                throw new ArgumentException("Tensor shape is required.", nameof(shape));
            }

            for (int index = 0; index < shape.Length; index++)
            {
                if (shape[index] <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(shape), "Tensor dimensions must be positive.");
                }
            }

            Name = name;
            _shape = (int[])shape.Clone();
        }
    }

    public sealed class DinoInferenceModelContract
    {
        public const string V2ProtocolVersion = "dino_attack_structured_set_v2";
        public const string FeasibilityArtifactKind = "feasibility_only";
        public const string TrainedCheckpointArtifactKind = "trained_checkpoint";
        public const string FeasibilityFixtureSha256 =
            "aa62da1118ec26888200765d1510802ed7f7e647911e4bd512f36d614eac4d6c";

        private static readonly DinoInferenceTensorContract[] V2Inputs =
        {
            new DinoInferenceTensorContract("global", 5),
            new DinoInferenceTensorContract("regions", 5, 8),
            new DinoInferenceTensorContract("walls", 6, 5),
            new DinoInferenceTensorContract("guards", 11, 7),
            new DinoInferenceTensorContract("houses", 8, 6),
            new DinoInferenceTensorContract("dinos", 10, 7)
        };

        private readonly DinoInferenceTensorContract[] _inputs;

        public string ProtocolVersion { get; }
        public string ArtifactKind { get; }
        public string ModelSha256 { get; }
        public IReadOnlyList<DinoInferenceTensorContract> Inputs => _inputs;
        public DinoInferenceTensorContract Output { get; }
        public bool IsDefaultModelEligible =>
            ArtifactKind == TrainedCheckpointArtifactKind &&
            !string.Equals(ModelSha256, FeasibilityFixtureSha256, StringComparison.OrdinalIgnoreCase);

        private DinoInferenceModelContract(
            string protocolVersion,
            string artifactKind,
            string modelSha256)
        {
            if (protocolVersion != V2ProtocolVersion)
            {
                throw new ArgumentException("Only the frozen v2 protocol is supported.", nameof(protocolVersion));
            }

            if (string.IsNullOrWhiteSpace(artifactKind))
            {
                throw new ArgumentException("Artifact kind is required.", nameof(artifactKind));
            }

            if (!IsSha256(modelSha256))
            {
                throw new ArgumentException("Model hash must be a lowercase or uppercase SHA-256 hex string.", nameof(modelSha256));
            }

            ProtocolVersion = protocolVersion;
            ArtifactKind = artifactKind;
            ModelSha256 = modelSha256.ToLowerInvariant();
            _inputs = (DinoInferenceTensorContract[])V2Inputs.Clone();
            Output = new DinoInferenceTensorContract("actions", 4);
        }

        public static DinoInferenceModelContract CreateFeasibility(string modelSha256)
        {
            return CreateFromManifest(
                V2ProtocolVersion,
                FeasibilityArtifactKind,
                modelSha256);
        }

        public static DinoInferenceModelContract CreateFromManifest(
            string protocolVersion,
            string artifactKind,
            string modelSha256)
        {
            return new DinoInferenceModelContract(
                protocolVersion,
                artifactKind,
                modelSha256);
        }

        public void ValidateModel(
            IReadOnlyList<string> inputNames,
            string outputName,
            IReadOnlyList<int> concreteOutputShape)
        {
            if (inputNames == null || inputNames.Count != _inputs.Length)
            {
                throw new InvalidOperationException("Model must expose exactly six inputs.");
            }

            for (int index = 0; index < _inputs.Length; index++)
            {
                if (inputNames[index] != _inputs[index].Name)
                {
                    throw new InvalidOperationException(
                        $"Model input {index} must be '{_inputs[index].Name}'.");
                }
            }

            if (outputName != Output.Name)
            {
                throw new InvalidOperationException("Model output must be named 'actions'.");
            }

            if (concreteOutputShape == null || concreteOutputShape.Count != 2 ||
                concreteOutputShape[0] <= 0 || concreteOutputShape[1] != Output.Shape[0])
            {
                throw new InvalidOperationException("Model output must have concrete shape (batch, 4).");
            }
        }

        public void ValidateActions(IReadOnlyList<float> actions)
        {
            if (actions == null || actions.Count != Output.Shape[0])
            {
                throw new InvalidOperationException("Inference must return exactly four actions.");
            }

            for (int index = 0; index < actions.Count; index++)
            {
                float value = actions[index];
                if (float.IsNaN(value) || float.IsInfinity(value) || value < -1f || value > 1f)
                {
                    throw new InvalidOperationException(
                        $"Action {index} must be finite and within [-1, 1].");
                }
            }
        }

        private static bool IsSha256(string value)
        {
            if (value == null || value.Length != 64)
            {
                return false;
            }

            for (int index = 0; index < value.Length; index++)
            {
                char character = value[index];
                bool digit = character >= '0' && character <= '9';
                bool lower = character >= 'a' && character <= 'f';
                bool upper = character >= 'A' && character <= 'F';
                if (!digit && !lower && !upper)
                {
                    return false;
                }
            }

            return true;
        }
    }
}
