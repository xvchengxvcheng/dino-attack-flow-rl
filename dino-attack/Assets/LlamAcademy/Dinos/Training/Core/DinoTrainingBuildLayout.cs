using System;
using System.IO;

namespace LlamAcademy.Dinos.Training
{
    public static class DinoTrainingBuildLayout
    {
        public const string TrainingScene =
            "Assets/LlamAcademy/Dinos/Scenes/DinoAttackTraining.unity";

        public static string[] CreateSceneList()
        {
            return new[] { TrainingScene };
        }

        public static string ResolvePlayerPath(string projectRoot)
        {
            if (string.IsNullOrWhiteSpace(projectRoot))
            {
                throw new ArgumentException("A Unity project root is required.", nameof(projectRoot));
            }

            return Path.GetFullPath(Path.Combine(
                projectRoot,
                "..",
                "Builds",
                "DinoAttackTrainingOnly",
                "DinoAttackTraining.exe"));
        }
    }
}
