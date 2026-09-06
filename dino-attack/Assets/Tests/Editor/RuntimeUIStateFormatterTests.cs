using LlamAcademy.Dinos.UI;
using LlamAcademy.Dinos.Deployment;
using NUnit.Framework;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public class RuntimeUIStateFormatterTests
    {
        [Test]
        public void FormatPhaseUsesSingleSessionRunningLabel()
        {
            Assert.That(RuntimeUIStateFormatter.FormatPhase(3, "Running"), Is.EqualTo("Session · Battle"));
            Assert.That(RuntimeUIStateFormatter.FormatPhase(3, "Ended"), Is.EqualTo("Session · Complete"));
        }

        [Test]
        public void FormatFoodShowsCurrentAndDelta()
        {
            Assert.That(RuntimeUIStateFormatter.FormatFood(12, 4), Is.EqualTo("Food 12  (+4)"));
            Assert.That(RuntimeUIStateFormatter.FormatFood(12, -3), Is.EqualTo("Food 12  (-3)"));
        }

        [Test]
        public void FormatPressureUsesHumanReadableBuckets()
        {
            Assert.That(RuntimeUIStateFormatter.FormatPressure(0, 0), Is.EqualTo("Threat · Clear"));
            Assert.That(RuntimeUIStateFormatter.FormatPressure(2, 5), Is.EqualTo("Threat · 2 enemies / 5 structures"));
        }

        [Test]
        public void FormatTerrainShowsZoneSurfaceAndExactMultiplier()
        {
            Assert.That(RuntimeUIStateFormatter.FormatTerrain("River Terrace", "Shallow Water", 0.70f),
                Is.EqualTo("River Terrace · Shallow Water 0.70×"));
        }

        [Test]
        public void FormatDeploymentFailureIncludesIconAndReadableReason()
        {
            Assert.That(RuntimeUIStateFormatter.FormatDeploymentFailure(DeploymentFailureReason.InsufficientFood),
                Is.EqualTo("[!] Not enough food"));
            Assert.That(RuntimeUIStateFormatter.FormatDeploymentFailure(DeploymentFailureReason.NoNavMesh),
                Is.EqualTo("[!] No reachable ground"));
        }
    }
}
