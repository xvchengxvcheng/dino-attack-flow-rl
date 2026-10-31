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
    public sealed class BattlefieldExitPlayModeTests
    {
        [UnityTest]
        public IEnumerator ExitFromAllThreeMaps_ReturnsToSelectionAndRestoresClock()
        {
            GameLaunchContext.Clear();
            foreach (string scene in new[] { "Dinos", "LayeredBattlefield", "JevVillageDemo" })
            {
                yield return Load(scene);
                var exits = Object.FindObjectsByType<UIDocument>(FindObjectsSortMode.None)
                    .Select(d => d.rootVisualElement.Q<Button>("exit-button"))
                    .Where(b => b != null && b.panel != null && b.resolvedStyle.display != DisplayStyle.None).ToArray();
                Assert.That(exits.Length, Is.EqualTo(1), scene);
                Button exit = exits[0];
                Assert.That(exit.text, Is.EqualTo("退出"));
                Assert.That(exit.worldBound.width, Is.GreaterThan(0), scene);
                Assert.That(exit.panel.Pick(exit.worldBound.center), Is.SameAs(exit), "Exit must receive pointer input: " + scene);
                Time.timeScale = 2f;
                using (NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled())
                {
                    submit.target = exit;
                    exit.SendEvent(submit);
                }
                Assert.That(exit.enabledSelf, Is.False, "Prevent repeated exit requests.");
                float deadline = Time.realtimeSinceStartup + 10f;
                while (SceneManager.GetActiveScene().name != "MapSelect" && Time.realtimeSinceStartup < deadline)
                    yield return null;
                Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MapSelect"), scene);
                yield return null;
                Assert.That(Time.timeScale, Is.EqualTo(1f), scene);
                Assert.That(UnityEngine.Cursor.lockState, Is.EqualTo(CursorLockMode.None), scene);
            }
        }

        [UnityTearDown]
        public IEnumerator Cleanup()
        {
            GameLaunchContext.Clear();
            yield return Load("MapSelect");
            Time.timeScale = 1f;
        }

        private static IEnumerator Load(string scene)
        {
            Type.GetType("UnityEditor.SceneManagement.EditorSceneManager, UnityEditor", true)
                .GetMethod("LoadSceneInPlayMode", BindingFlags.Public | BindingFlags.Static, null,
                    new[] { typeof(string), typeof(LoadSceneParameters) }, null)
                .Invoke(null, new object[] { "Assets/LlamAcademy/Dinos/Scenes/" + scene + ".unity", new LoadSceneParameters(LoadSceneMode.Single) });
            for (int frame = 0; frame < 8; frame++) yield return null;
        }
    }
}
