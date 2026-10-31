using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using LlamAcademy.Dinos.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace LlamAcademy.Dinos.Tests.PlayMode
{
    public sealed class GameSpeedControlPlayModeTests
    {
        [UnityTest]
        public IEnumerator SpeedChangesClockAndUi_ResetsAcrossMaps_RejectsTrainingOwnership()
        {
            GameLaunchContext.Clear();
            yield return Load("MapSelect");
            float step = Time.fixedDeltaTime;
            foreach (string map in new[] { "Dinos", "LayeredBattlefield", "DefenseBattlefield", "FirstPersonDefenseBattlefield" })
            {
                yield return Load(map);
                Component speed = FindSpeed();
                Assert.That(speed, Is.Not.Null);
                Assert.That(Time.timeScale, Is.EqualTo(map == "FirstPersonDefenseBattlefield" ? 0.5f : 1f), map);
                MethodInfo setSpeed = speed.GetType().GetMethod("TrySetSpeed");
                foreach (float invalid in new[] { float.NaN, float.PositiveInfinity, -1f, 0f, 20f })
                    Assert.That((bool)setSpeed.Invoke(speed, new object[] { invalid }), Is.False);
                UIDocument document = Object.FindFirstObjectByType<UIDocument>();
                Assert.That(document.rootVisualElement.Q<VisualElement>("game-speed-settings"), Is.Not.Null);
                setSpeed.Invoke(speed, new object[] { 2f });
                Assert.That(Time.timeScale, Is.EqualTo(2f));
                double realStart = Time.realtimeSinceStartupAsDouble, gameStart = Time.timeAsDouble;
                yield return new WaitForSecondsRealtime(0.25f);
                double rate = (Time.timeAsDouble - gameStart) / (Time.realtimeSinceStartupAsDouble - realStart);
                Assert.That(rate, Is.InRange(1.6, 2.4));
                setSpeed.Invoke(speed, new object[] { 0.5f });
                realStart = Time.realtimeSinceStartupAsDouble; gameStart = Time.timeAsDouble;
                yield return new WaitForSecondsRealtime(0.3f);
                rate = (Time.timeAsDouble - gameStart) / (Time.realtimeSinceStartupAsDouble - realStart);
                Assert.That(rate, Is.InRange(0.35, 0.65));
                Assert.That(Time.fixedDeltaTime, Is.EqualTo(step));
                setSpeed.Invoke(speed, new object[] { 2f });
            }
            yield return Load("MapSelect");
            Assert.That(Time.timeScale, Is.EqualTo(1f));
            Time.timeScale = 3f;
            Assert.That(GameLaunchContext.TryPublish(GameLaunchRequest.CreateTraining(GameMapId.OpenTropicalBattlefield, 123), null), Is.True);
            yield return Load("Dinos");
            Component trainingSpeed = FindSpeed();
            Assert.That(((Behaviour)trainingSpeed).enabled, Is.False);
            Assert.That((bool)trainingSpeed.GetType().GetMethod("TrySetSpeed").Invoke(trainingSpeed, new object[] { 0.5f }), Is.False);
            Assert.That(Time.timeScale, Is.EqualTo(3f), "The player setting must not own a TrainingAI launch clock.");
            Assert.That(Time.fixedDeltaTime, Is.EqualTo(step));
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            GameLaunchContext.Clear();
            yield return Load("MapSelect");
            Time.timeScale = 1f;
        }

        private static Component FindSpeed() => Object.FindObjectsByType<Component>(FindObjectsSortMode.None)
            .SingleOrDefault(c => c != null && c.GetType().Name == "GameSpeedControl");

        private static IEnumerator Load(string scene)
        {
            Type.GetType("UnityEditor.SceneManagement.EditorSceneManager, UnityEditor", true)
                .GetMethod("LoadSceneInPlayMode", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(LoadSceneParameters) }, null)
                .Invoke(null, new object[] { "Assets/LlamAcademy/Dinos/Scenes/" + scene + ".unity", new LoadSceneParameters(LoadSceneMode.Single) });
            for (int i = 0; i < 6; i++) yield return null;
        }
    }
}
