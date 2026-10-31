using System.Collections;
using System.Linq;
using LlamAcademy.Dinos.Session;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LlamAcademy.Dinos.Tests.PlayMode
{
    public sealed class JevVillageAbilityPlayModeTests
    {
        [UnityTest]
        public IEnumerator DemoDash_StopsAndAbilitiesRecoverWhileRoundStaysInSetup()
        {
            GameLaunchContext.Clear();
            SceneManager.LoadScene("JevVillageDemo");
            yield return null;
            yield return null;
            Time.timeScale = 1f;
            Component player = Find("DefenseBattle.FirstPersonDefender");
            Component abilities = Find("DefenseBattle.DefenderAbilities");
            Component round = Find("RoundManagement.RoundManager");
            Assert.That(Read<bool>(player, "IsControlling"), Is.True);
            Assert.That(Read<object>(round, "State").ToString(), Is.EqualTo("Setup"));
            Assert.That(Call(abilities, "TryDash"), Is.True);
            Assert.That(Read<bool>(abilities, "IsDashing"), Is.True);
            Assert.That(Call(abilities, "TryDash"), Is.False);
            Assert.That(Call(abilities, "TryShield"), Is.True);
            Assert.That(Call(player, "TryCast"), Is.True);
            Assert.That(Call(player, "TryCast"), Is.False, "Repeated attacks must respect their cooldown.");

            yield return new WaitForSeconds(0.35f);
            Assert.That(Read<bool>(abilities, "IsDashing"), Is.False,
                "The village dash must expire even though no formal round is running.");
            Vector3 stopped = player.transform.position;
            float cooldown = Read<float>(abilities, "DashCooldownRemaining");
            Assert.That(cooldown, Is.InRange(5f, 6f));
            Time.timeScale = 0f;
            yield return new WaitForSecondsRealtime(0.2f);
            Assert.That(Read<float>(abilities, "DashCooldownRemaining"), Is.EqualTo(cooldown).Within(0.02f));
            Time.timeScale = 1f;
            yield return new WaitForSeconds(0.3f);
            Assert.That(Vector2.Distance(new Vector2(stopped.x, stopped.z),
                new Vector2(player.transform.position.x, player.transform.position.z)), Is.LessThan(0.05f),
                "The player must stop drifting after the dash with no movement input.");
            Assert.That(Read<float>(abilities, "DashCooldownRemaining"), Is.LessThan(cooldown));
            Assert.That(Call(player, "TryCast"), Is.True, "Basic attacks must recover in the demo too.");
            yield return new WaitForSeconds(5.5f);
            Assert.That(Read<int>(abilities, "ShieldRemaining"), Is.Zero);
            Assert.That(Call(abilities, "TryDash"), Is.True, "Dash must become available again.");
            Assert.That(Read<float>(round, "RunningElapsedSeconds"), Is.Zero);
        }

        private static Component Find(string suffix) => Object.FindObjectsByType<Component>(FindObjectsSortMode.None)
            .First(c => c != null && c.GetType().FullName == "LlamAcademy.Dinos." + suffix);

        private static T Read<T>(Component target, string property) =>
            (T)target.GetType().GetProperty(property).GetValue(target);

        private static bool Call(Component target, string method) =>
            (bool)target.GetType().GetMethod(method).Invoke(target, null);

        [UnityTearDown]
        public IEnumerator RestoreMapSelection()
        {
            Time.timeScale = 1f;
            GameLaunchContext.Clear();
            SceneManager.LoadScene("MapSelect");
            yield return null;
        }
    }
}
