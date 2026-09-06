using System;
using System.Reflection;
using LlamAcademy.Dinos.Session;
using NUnit.Framework;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class GameSessionRestartServiceTests
    {
        [SetUp]
        public void SetUp()
        {
            GameLaunchContext.Clear();
        }

        [TearDown]
        public void TearDown()
        {
            GameLaunchContext.Clear();
        }

        [Test]
        public void DualMapDestinations_AreAvailableForLaunch()
        {
            Type serviceType = Type.GetType(
                "LlamAcademy.Dinos.RoundManagement.GameSessionRestartService, Assembly-CSharp",
                true);
            MethodInfo availability = serviceType.GetMethod(
                "IsSceneAvailableForLaunch",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(availability, Is.Not.Null,
                "The restart service must expose its fail-closed build-scene availability gate.");

            Assert.That(
                availability.Invoke(null, new object[] { GameMapId.OpenTropicalBattlefield.SceneName }),
                Is.True);
            Assert.That(
                availability.Invoke(null, new object[] { GameMapId.MapSelect.SceneName }),
                Is.True,
                "Select Map must be able to load the menu from either gameplay scene.");
            Assert.That(
                availability.Invoke(null, new object[] { GameMapId.LayeredBattlefield.SceneName }),
                Is.True,
                "MapSelect must be able to launch the layered battlefield.");
        }

        [Test]
        public void UnknownBuildScene_IsRejectedBeforePublishingOrScheduling()
        {
            Type serviceType = Type.GetType(
                "LlamAcademy.Dinos.RoundManagement.GameSessionRestartService, Assembly-CSharp",
                true);
            MethodInfo availability = serviceType.GetMethod(
                "IsSceneAvailableForLaunch",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(availability, Is.Not.Null);
            Assert.That(
                availability.Invoke(null, new object[] { "MissingDinoAttackScene" }),
                Is.False);
        }
    }
}
