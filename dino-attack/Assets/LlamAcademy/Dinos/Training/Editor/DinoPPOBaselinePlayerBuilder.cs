using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LlamAcademy.Dinos.Training.Editor
{
    public static class DinoPPOBaselinePlayerBuilder
    {
        [MenuItem("Dino Attack/Training/Build PPO Baseline Player")]
        public static void Build()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the Unity project root.");
            string outputPath = DinoPPOBaselineBuildLayout.ResolvePlayerPath(projectRoot);
            if (File.Exists(outputPath))
            {
                throw new InvalidOperationException(
                    $"Refusing to overwrite an existing PPO baseline Player: {outputPath}");
            }

            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
                ?? throw new InvalidOperationException($"Unable to resolve build directory for {outputPath}."));
            BuildPlayerOptions options = new()
            {
                scenes = DinoPPOBaselineBuildLayout.CreateSceneList(),
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"PPO baseline Player build failed with {report.summary.totalErrors} errors.");
            }

            Debug.Log(
                $"Built PPO baseline Player at {outputPath}; " +
                $"entry={options.scenes[0]}; scenes={options.scenes.Length}.");
        }
    }
}
