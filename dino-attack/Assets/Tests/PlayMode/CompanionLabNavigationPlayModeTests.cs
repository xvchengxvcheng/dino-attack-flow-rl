using System.Collections;
using System.Linq;
using System.Reflection;
using LlamAcademy.Dinos.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace LlamAcademy.Dinos.Tests.PlayMode
{
    public sealed class CompanionLabNavigationPlayModeTests
    {
        [UnityTest]
        public IEnumerator LabRoundTrip_AllFiveMapsCanStillLaunch()
        {
            string[] buttons = { "Open Tropical Battlefield", "Layered Battlefield", "Defense Battlefield", "First Person Defense Battlefield", "JEV Village Demo" };
            string[] scenes = { "Dinos", "LayeredBattlefield", "DefenseBattlefield", "FirstPersonDefenseBattlefield", "JevVillageDemo" };
            for (int i = 0; i < scenes.Length; i++)
            {
                SceneManager.LoadScene("MapSelect");
                yield return null;
                yield return null;
                GameObject.Find("Companion Relationship Lab").GetComponent<Button>().onClick.Invoke();
                yield return WaitForScene("CompanionLab");
                Assert.That(GameLaunchContext.HasPending, Is.False,
                    "Entering the lab must not leave an abandoned battlefield launch request.");

                Component lab = Object.FindObjectsByType<Component>(FindObjectsSortMode.None)
                    .First(c => c != null && c.GetType().FullName == "LlamAcademy.Dinos.Companion.CompanionLabController");
                lab.GetType().GetMethod("ReturnToMapSelect", BindingFlags.NonPublic | BindingFlags.Instance).Invoke(lab, null);
                yield return WaitForScene("MapSelect");
                GameObject.Find(buttons[i]).GetComponent<Button>().onClick.Invoke();
                yield return WaitForScene(scenes[i]);
                Assert.That(GameLaunchContext.HasPending, Is.False,
                    scenes[i] + " must consume its launch request after returning from the lab.");
            }
        }

        private static IEnumerator WaitForScene(string expected)
        {
            float deadline = Time.realtimeSinceStartup + 15f;
            while (SceneManager.GetActiveScene().name != expected && Time.realtimeSinceStartup < deadline)
                yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo(expected));
            yield return null;
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator RestoreMapSelection()
        {
            GameLaunchContext.Clear();
            SceneManager.LoadScene("MapSelect");
            yield return null;
        }
    }
}
