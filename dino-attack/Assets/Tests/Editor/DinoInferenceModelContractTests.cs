using System;
using System.Linq;
using LlamAcademy.Dinos.Inference;
using NUnit.Framework;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public class DinoInferenceModelContractTests
    {
        [Test]
        public void FeasibilityContractFreezesV2SixInputAction4Shape()
        {
            DinoInferenceModelContract contract =
                DinoInferenceModelContract.CreateFeasibility(new string('a', 64));

            Assert.That(contract.ProtocolVersion, Is.EqualTo("dino_attack_structured_set_v2"));
            Assert.That(contract.ArtifactKind, Is.EqualTo("feasibility_only"));
            Assert.That(contract.IsDefaultModelEligible, Is.False);
            Assert.That(
                contract.Inputs.Select(input => input.Name),
                Is.EqualTo(new[] { "global", "regions", "walls", "guards", "houses", "dinos" }));
            Assert.That(
                contract.Inputs.Select(input => input.Shape),
                Is.EqualTo(new[]
                {
                    new[] { 5 }, new[] { 5, 8 }, new[] { 6, 5 },
                    new[] { 11, 7 }, new[] { 8, 6 }, new[] { 10, 7 }
                }));
            Assert.That(contract.Output.Name, Is.EqualTo("actions"));
            Assert.That(contract.Output.Shape, Is.EqualTo(new[] { 4 }));
        }

        [Test]
        public void DefaultEligibilityAllowsOnlyExplicitTrainedCheckpointArtifact()
        {
            string hash = new string('c', 64);

            DinoInferenceModelContract feasibility =
                DinoInferenceModelContract.CreateFromManifest(
                    DinoInferenceModelContract.V2ProtocolVersion,
                    DinoInferenceModelContract.FeasibilityArtifactKind,
                    hash);
            DinoInferenceModelContract unknown =
                DinoInferenceModelContract.CreateFromManifest(
                    DinoInferenceModelContract.V2ProtocolVersion,
                    "future_or_misspelled_kind",
                    hash);
            DinoInferenceModelContract trained =
                DinoInferenceModelContract.CreateFromManifest(
                    DinoInferenceModelContract.V2ProtocolVersion,
                    DinoInferenceModelContract.TrainedCheckpointArtifactKind,
                    hash);

            Assert.That(feasibility.IsDefaultModelEligible, Is.False);
            Assert.That(unknown.IsDefaultModelEligible, Is.False);
            Assert.That(trained.IsDefaultModelEligible, Is.True);
        }

        [Test]
        public void ContractRejectsWrongModelSignatureAndInvalidActions()
        {
            DinoInferenceModelContract contract =
                DinoInferenceModelContract.CreateFeasibility(new string('b', 64));

            Assert.Throws<ArgumentException>(() =>
                DinoInferenceModelContract.CreateFeasibility("not-a-sha256"));
            Assert.Throws<InvalidOperationException>(() => contract.ValidateModel(
                new[] { "global", "regions", "walls", "guards", "houses", "wrong" },
                "actions",
                new[] { 1, 4 }));
            Assert.Throws<InvalidOperationException>(() => contract.ValidateActions(
                new[] { 0f, float.NaN, 0f, 0f }));
            Assert.Throws<InvalidOperationException>(() => contract.ValidateActions(
                new[] { 0f, 0f, 0f, 1.01f }));
            Assert.DoesNotThrow(() => contract.ValidateActions(
                new[] { -1f, 0f, 0.5f, 1f }));
        }
    }
}
