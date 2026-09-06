using System;
using System.IO;
using LlamAcademy.Dinos.Training;
using NUnit.Framework;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class DinoPPOBaselineBuildLayoutTests
    {
        [Test]
        public void UsesExplicitMenuAndTwoMapScenesWithoutReusingTask8Output()
        {
            CollectionAssert.AreEqual(
                new[]
                {
                    "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity",
                    "Assets/LlamAcademy/Dinos/Scenes/Dinos.unity",
                    "Assets/LlamAcademy/Dinos/Scenes/LayeredBattlefield.unity"
                },
                DinoPPOBaselineBuildLayout.CreateSceneList());

            string projectRoot = Path.Combine(Path.GetTempPath(), "dino-ppo-project");
            string output = DinoPPOBaselineBuildLayout.ResolvePlayerPath(projectRoot);
            StringAssert.EndsWith(
                Path.Combine(
                    "reports",
                    "phase8",
                    "ppo-baseline",
                    "build-fixed-clock-v5-20260903",
                    "DinoAttackDualMapPPOV2-FixedClockV5-20260903.exe"),
                output);
            Assert.That(
                output.Contains(
                    Path.Combine("layered-battlefield", "build", "DinoAttackDualMapTask8.exe"),
                    StringComparison.OrdinalIgnoreCase),
                Is.False);
        }
    }
}
