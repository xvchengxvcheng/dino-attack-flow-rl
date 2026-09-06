using System;
using System.IO;

namespace LlamAcademy.Dinos.Training
{
    public static class DinoPPOBaselineBuildLayout
    {
        public const string EntryScene = "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity";
        public const string MainScene = "Assets/LlamAcademy/Dinos/Scenes/Dinos.unity";
        public const string LayeredScene = "Assets/LlamAcademy/Dinos/Scenes/LayeredBattlefield.unity";

        public static string[] CreateSceneList() => new[]
        {
            EntryScene,
            MainScene,
            LayeredScene
        };

        public static string ResolvePlayerPath(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("A Unity project root is required.", nameof(projectRoot));
            }

            return Path.GetFullPath(Path.Combine(
                projectRoot,
                "..",
                "reports",
                "phase8",
                "ppo-baseline",
                "build-fixed-clock-v5-20260903",
                "DinoAttackDualMapPPOV2-FixedClockV5-20260903.exe"));
        }
    }
}
