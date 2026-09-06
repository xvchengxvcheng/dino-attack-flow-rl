using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace LlamAcademy.Dinos.Training.Editor
{
    public static class DinoFormalTrainingPlayerBuilder
    {
        [MenuItem("Dino Attack/Training/Build Formal Main-Scene Player")]
        public static void Build()
        {
            string projectRoot = Directory.GetParent(Application.dataPath)?.FullName
                ?? throw new InvalidOperationException("Unable to resolve the Unity project root.");
            string outputPath = DinoFormalTrainingBuildLayout.ResolvePlayerPath(projectRoot);
            Directory.CreateDirectory(Path.GetDirectoryName(outputPath)
                ?? throw new InvalidOperationException($"Unable to resolve build directory for {outputPath}."));

            BuildPlayerOptions options = new()
            {
                scenes = DinoFormalTrainingBuildLayout.CreateSceneList(),
                locationPathName = outputPath,
                target = BuildTarget.StandaloneWindows64,
                options = BuildOptions.None
            };
            BuildReport report = BuildPipeline.BuildPlayer(options);
            if (report.summary.result != BuildResult.Succeeded)
            {
                throw new InvalidOperationException(
                    $"Formal Dino Attack Player build failed with {report.summary.totalErrors} errors.");
            }

            Debug.Log(
                $"Built formal Dino Attack Player at {outputPath}; " +
                $"entry={options.scenes[0]}; scenes={options.scenes.Length}.");
        }
    }
}
