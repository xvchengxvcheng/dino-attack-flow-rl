using System;
using LlamAcademy.Dinos.Session;
using Unity.InferenceEngine;
using UnityEngine;

namespace LlamAcademy.Dinos.Inference
{
    [CreateAssetMenu(
        fileName = "DinoInferenceModelConfig",
        menuName = "Dino Attack/Inference Model Config")]
    public sealed class DinoInferenceModelConfig : ScriptableObject
    {
        public const string DefaultResourcesPath = "DinoInference/DefaultModel";
        public const string FpoResourcesPath = "DinoInference/FpoModel";
        public static DinoInferenceModelConfig LoadPolicyFlow() => Resources.Load<DinoInferenceModelConfig>("DinoInference/PolicyFlowModel");
        private const string FeasibilityFixtureAssetName = "DinoV2SetTransformerFeasibility";

        [SerializeField] private ModelAsset ModelAsset;
        [SerializeField] private string ModelId = string.Empty;
        [SerializeField] private string ModelSha256 = string.Empty;
        [SerializeField] private string ProtocolVersion = DinoInferenceModelContract.V2ProtocolVersion;
        [SerializeField] private string ArtifactKind = string.Empty;
        [SerializeField] private string[] InputNames =
            { "global", "regions", "walls", "guards", "houses", "dinos" };
        [SerializeField] private string OutputName = "actions";
        [SerializeField] private int DefaultInferenceSeed = 20260831;

        public ModelAsset Asset => ModelAsset;
        public string StableModelId => ModelId ?? string.Empty;
        public string ModelHash => ModelSha256 ?? string.Empty;
        public string Protocol => ProtocolVersion ?? string.Empty;
        public string Kind => ArtifactKind ?? string.Empty;
        public string[] ConfiguredInputNames => InputNames == null
            ? Array.Empty<string>()
            : (string[])InputNames.Clone();
        public string ConfiguredOutputName => OutputName ?? string.Empty;
        public int InferenceSeed => DefaultInferenceSeed;

        public static DinoInferenceModelConfig LoadDefault() =>
            Resources.Load<DinoInferenceModelConfig>(DefaultResourcesPath);

        public static DinoInferenceModelConfig LoadFpo() =>
            Resources.Load<DinoInferenceModelConfig>(FpoResourcesPath);

        public static DinoInferenceModelConfig LoadForRequest(GameLaunchRequest request)
        {
            foreach (DinoInferenceModelConfig config in new[] { LoadDefault(), LoadFpo(), LoadPolicyFlow() })
            {
                if (config != null && config.Matches(request, out _))
                {
                    return config;
                }
            }

            return null;
        }

        public bool TryValidateForDefault(out string failureReason)
        {
            if (ModelAsset == null)
            {
                failureReason = "No trained inference model asset is configured.";
                return false;
            }

            if (string.Equals(ModelAsset.name, FeasibilityFixtureAssetName, StringComparison.Ordinal))
            {
                failureReason = "The Sentis feasibility fixture cannot be used as a trained default model.";
                return false;
            }

            if (string.IsNullOrWhiteSpace(ModelId))
            {
                failureReason = "The inference model ID is missing.";
                return false;
            }

            try
            {
                DinoInferenceModelContract contract =
                    DinoInferenceModelContract.CreateFromManifest(
                        ProtocolVersion,
                        ArtifactKind,
                        ModelSha256);
                if (!contract.IsDefaultModelEligible)
                {
                    failureReason = "The configured artifact is not an eligible trained checkpoint.";
                    return false;
                }

                contract.ValidateModel(
                    InputNames ?? Array.Empty<string>(),
                    OutputName,
                    new[] { 1, DinoInferenceDecisionPolicy.ActionSize });
            }
            catch (Exception exception)
            {
                failureReason = exception.Message;
                return false;
            }

            if (DefaultInferenceSeed == int.MinValue)
            {
                failureReason = "The inference seed is invalid.";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        public bool Matches(GameLaunchRequest request, out string failureReason)
        {
            if (!TryValidateForDefault(out failureReason))
            {
                return false;
            }

            if (request.Controller != GameControllerMode.InferenceAI ||
                !string.Equals(request.ModelId, ModelId, StringComparison.Ordinal) ||
                !string.Equals(request.ModelHash, ModelSha256, StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(request.ProtocolVersion, ProtocolVersion, StringComparison.Ordinal))
            {
                failureReason = "The launch request does not match the configured inference model.";
                return false;
            }

            failureReason = string.Empty;
            return true;
        }

        public GameInferenceLaunchOption ToLaunchOption()
        {
            return TryValidateForDefault(out string reason)
                ? GameInferenceLaunchOption.Available(
                    DefaultInferenceSeed,
                    ModelId,
                    ModelSha256,
                    ProtocolVersion)
                : GameInferenceLaunchOption.Unavailable(reason);
        }
    }
}
