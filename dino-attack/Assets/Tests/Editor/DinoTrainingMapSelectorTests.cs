using System;
using System.IO;
using System.Linq;
using System.Reflection;
using LlamAcademy.Dinos.Training;
using NUnit.Framework;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace DinoAttack.Tests.Editor
{
    public class DinoTrainingMapSelectorTests
    {
        [Test]
        public void ConsecutiveEpisodeSeedsSelectEachMapExactlyOnce()
        {
            for (int seed = -128; seed < 128; seed += 2)
            {
                DinoTrainingMap first = DinoTrainingMapSelector.Select(seed);
                DinoTrainingMap second = DinoTrainingMapSelector.Select(seed + 1);

                Assert.That(first, Is.Not.EqualTo(second));
                Assert.That(DinoTrainingMapSelector.Select(seed), Is.EqualTo(first));
            }
        }

        [TestCase(0, "Dinos")]
        [TestCase(1, "LayeredBattlefield")]
        public void SceneReloaderUsesTheSameEpisodeSeedSelector(int episodeSeed, string expectedScene)
        {
            Type reloader = RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingSceneReloader");
            MethodInfo resolve = reloader.GetMethod(
                "ResolveTargetSceneName",
                BindingFlags.Static | BindingFlags.Public);

            Assert.That(resolve, Is.Not.Null);
            Assert.That(resolve.Invoke(null, new object[] { episodeSeed }), Is.EqualTo(expectedScene));
        }

        [Test]
        public void FormalBuildUsesMenuThenBothPlayableMapsAndUniqueTask8Output()
        {
            Assert.That(DinoFormalTrainingBuildLayout.CreateSceneList(), Is.EqualTo(new[]
            {
                "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity",
                "Assets/LlamAcademy/Dinos/Scenes/Dinos.unity",
                "Assets/LlamAcademy/Dinos/Scenes/LayeredBattlefield.unity"
            }));

            string playerPath = DinoFormalTrainingBuildLayout.ResolvePlayerPath(
                Path.Combine("C:\\workspace", "dino-attack"));
            Assert.That(playerPath, Is.EqualTo(Path.GetFullPath(Path.Combine(
                "C:\\workspace",
                "reports",
                "phase8",
                "layered-battlefield",
                "build",
                "DinoAttackDualMapTask8.exe"))));
        }

        [Test]
        public void MapSelectSceneContainsExactlyOneEarlyTrainingEntryBootstrap()
        {
            const string scenePath = "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity";
            Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
            Type bootstrapType = RuntimeType("LlamAcademy.Dinos.Training.DinoTrainingEntryBootstrap");
            Component[] bootstraps = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren(bootstrapType, true).Cast<Component>())
                .ToArray();

            Assert.That(bootstraps, Has.Length.EqualTo(1));
            DefaultExecutionOrder order = bootstrapType.GetCustomAttribute<DefaultExecutionOrder>();
            Assert.That(order, Is.Not.Null);
            Assert.That(order.order, Is.LessThan(0));
        }

        private static Type RuntimeType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .FirstOrDefault(type => type != null)
            ?? throw new TypeLoadException(fullName);
    }
}
