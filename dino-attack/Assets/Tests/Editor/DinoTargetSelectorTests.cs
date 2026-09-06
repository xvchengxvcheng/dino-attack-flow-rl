using LlamAcademy.Dinos.Map;
using NUnit.Framework;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class DinoTargetSelectorTests
    {
        [Test]
        public void SelectIndex_StrictCategoryPriorityWinsOverDistance()
        {
            DinoTargetSelectionCandidate[] candidates =
            {
                new(DinoTargetCategory.Wall, 10, 1f, true),
                new(DinoTargetCategory.House, 20, 40f, true)
            };

            int selected = DinoTargetSelectionRules.SelectIndex(
                DinoTargetProfileId.Velociraptor,
                candidates);

            Assert.That(selected, Is.EqualTo(1));
        }

        [Test]
        public void SelectIndex_FallsBackWhenHigherCategoryIsUnreachable()
        {
            DinoTargetSelectionCandidate[] candidates =
            {
                new(DinoTargetCategory.House, 10, float.PositiveInfinity, false),
                new(DinoTargetCategory.Defender, 20, 12f, true)
            };

            int selected = DinoTargetSelectionRules.SelectIndex(
                DinoTargetProfileId.Velociraptor,
                candidates);

            Assert.That(selected, Is.EqualTo(1));
        }

        [Test]
        public void SelectIndex_UsesActualPathLengthWithinTheSameCategory()
        {
            DinoTargetSelectionCandidate[] candidates =
            {
                new(DinoTargetCategory.Defender, 30, 18f, true),
                new(DinoTargetCategory.Defender, 20, 12f, true)
            };

            int selected = DinoTargetSelectionRules.SelectIndex(
                DinoTargetProfileId.Pachycephalosaurus,
                candidates);

            Assert.That(selected, Is.EqualTo(1));
        }

        [Test]
        public void SelectIndex_UsesStableIdWhenPathLengthsAreWithinTolerance()
        {
            DinoTargetSelectionCandidate[] candidates =
            {
                new(DinoTargetCategory.Wall, 40, 10f, true),
                new(DinoTargetCategory.Wall, 12, 10.009f, true)
            };

            int selected = DinoTargetSelectionRules.SelectIndex(
                DinoTargetProfileId.TRex,
                candidates);

            Assert.That(selected, Is.EqualTo(1));
        }

        [Test]
        public void SelectIndex_TieToleranceIsMeasuredFromTheActualShortestPath()
        {
            DinoTargetSelectionCandidate[] candidates =
            {
                new(DinoTargetCategory.Wall, 30, 10f, true),
                new(DinoTargetCategory.Wall, 20, 10.009f, true),
                new(DinoTargetCategory.Wall, 10, 10.018f, true)
            };

            int selected = DinoTargetSelectionRules.SelectIndex(
                DinoTargetProfileId.TRex,
                candidates);

            Assert.That(selected, Is.EqualTo(1));
        }

        [Test]
        public void SelectIndex_ReturnsMinusOneWhenNoCandidateIsReachableAndFinite()
        {
            DinoTargetSelectionCandidate[] candidates =
            {
                new(DinoTargetCategory.House, 1, float.NaN, true),
                new(DinoTargetCategory.Defender, 2, 5f, false)
            };

            Assert.That(
                DinoTargetSelectionRules.SelectIndex(DinoTargetProfileId.Velociraptor, candidates),
                Is.EqualTo(-1));
        }
    }
}
