using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using LlamAcademy.Dinos.Map;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace LlamAcademy.Dinos.Tests.PlayMode
{
    public sealed class NavigationReadinessPlayModeTests
    {
        [Test]
        public void IsAtTargetLocation_AgentWithoutPathIsNotConsideredArrived()
        {
            GameObject host = new("navigation-arrival-test");
            try
            {
                NavMeshAgent agent = host.AddComponent<NavMeshAgent>();
                agent.enabled = false;

                Type utilities = RuntimeType("LlamAcademy.Dinos.Utility.NavMeshUtilities");
                MethodInfo isAtTarget = utilities.GetMethod(
                    "IsAtTargetLocation",
                    BindingFlags.Public | BindingFlags.Static,
                    null,
                    new[] { typeof(NavMeshAgent) },
                    null);

                Assert.That(isAtTarget, Is.Not.Null);
                Assert.That((bool)isAtTarget.Invoke(null, new object[] { agent }), Is.False,
                    "an invalid or pathless agent must not be reported as having reached its target");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [Test]
        public void MoveToTargetLocation_PathSubmissionFailureReturnsFailure()
        {
            GameObject host = new("navigation-action-test");
            try
            {
                host.AddComponent<NavMeshAgent>().enabled = false;
                Type actionType = RuntimeType(
                    "LlamAcademy.Dinos.Behavior.MoveToTargetLocationAction");
                object action = Activator.CreateInstance(actionType);
                actionType.GetField("Self")?.SetValue(
                    action,
                    CreateBlackboardVariable(typeof(GameObject), host));
                actionType.GetField("TargetLocation")?.SetValue(
                    action,
                    CreateBlackboardVariable(typeof(Vector3), Vector3.one));
                actionType.GetField("IsAbleToSetDestination")?.SetValue(
                    action,
                    CreateBlackboardVariable(typeof(bool), false));

                object status = actionType.GetMethod(
                        "OnStart",
                        BindingFlags.Instance | BindingFlags.NonPublic)
                    ?.Invoke(action, null);

                Assert.That(status?.ToString(), Is.EqualTo("Failure"),
                    "a rejected path must not advance the behavior graph as success");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(host);
            }
        }

        [UnityTest]
        public IEnumerator StartRound_WaitsForNavigationReadinessOnEveryEpisode()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 3; frame++) yield return null;

            Component round = FindRuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
            Component navigation = FindRuntimeComponent("LlamAcademy.Dinos.RoundManagement.NavMeshManager");
            PropertyInfo isReady = navigation.GetType().GetProperty(
                "IsReady",
                BindingFlags.Instance | BindingFlags.Public);

            Assert.That(isReady, Is.Not.Null, "NavMeshManager must publish episode-start readiness");
            isReady.SetValue(navigation, false);
            round.GetType().GetMethod("StartRound", BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(round, null);
            Assert.That(round.GetType().GetProperty("State")?.GetValue(round)?.ToString(),
                Is.EqualTo("Setup"), "the episode must not run before navigation is ready");

            isReady.SetValue(navigation, true);
            float deadline = Time.realtimeSinceStartup + 2f;
            while (round.GetType().GetProperty("State")?.GetValue(round)?.ToString() != "Running" &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.That(round.GetType().GetProperty("State")?.GetValue(round)?.ToString(),
                Is.EqualTo("Running"), "the pending episode must start after the same readiness gate passes");
        }

        [UnityTest]
        public IEnumerator OpenTropical_UsesInstalledNavMeshDataWithoutRuntimeBuild()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 3; frame++) yield return null;

            Component navigation = FindRuntimeComponent(
                "LlamAcademy.Dinos.RoundManagement.NavMeshManager");
            Assert.That(navigation.GetType().GetProperty("IsReady")?.GetValue(navigation), Is.True);
            Assert.That(navigation.GetType().GetProperty("RuntimeBuildCount")?.GetValue(navigation),
                Is.EqualTo(0),
                "Open Tropical must install pre-baked NavMeshData instead of rebuilding at episode startup");
        }

        [UnityTest]
        public IEnumerator OpenTropical_DeployedDinoPassesTheSameEpisodeReadinessGate()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 3; frame++) yield return null;

            Component round = FindRuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
            Component spawner = FindRuntimeComponent("LlamAcademy.Dinos.Player.DinoSpawner");
            Component runtimeUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
            Array dinoTypes = (Array)runtimeUi.GetType()
                .GetField("Dinos", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.GetValue(runtimeUi);
            UnityEngine.Object tRex = dinoTypes.Cast<UnityEngine.Object>()
                .Single(dino => dino.name.Equals("TRex", StringComparison.OrdinalIgnoreCase));
            DinoDeploymentZone z1 = UnityEngine.Object
                .FindObjectsByType<DinoDeploymentZone>(FindObjectsInactive.Exclude, FindObjectsSortMode.None)
                .Single(zone => zone.Id == DeploymentZoneId.Z1RuinsForecourt);
            Assert.That(z1.TryResolveRelative(new Vector2(0.5f, 0.5f), out Vector3 position), Is.True);
            spawner.GetType().GetProperty("ResourcesToSpend")?.SetValue(spawner, 9999);
            object[] deployArguments = { tRex, position, false, null, null };
            Assert.That((bool)spawner.GetType().GetMethod("TryDeploy")
                ?.Invoke(spawner, deployArguments), Is.True);

            round.GetType().GetMethod("StartRound")?.Invoke(round, null);
            float deadline = Time.realtimeSinceStartup + 2f;
            while (round.GetType().GetProperty("State")?.GetValue(round)?.ToString() != "Running" &&
                   Time.realtimeSinceStartup < deadline)
            {
                yield return new WaitForFixedUpdate();
            }

            string failure = round.GetType().GetProperty("NavigationReadinessFailure")
                ?.GetValue(round)?.ToString();
            Assert.That(round.GetType().GetProperty("State")?.GetValue(round)?.ToString(),
                Is.EqualTo("Running"), failure);
        }

        private static Component FindRuntimeComponent(string fullName) =>
            UnityEngine.Object.FindObjectsByType<Component>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Single(component => component != null && component.GetType().FullName == fullName);

        private static Type RuntimeType(string fullName) =>
            AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType(fullName, false))
                .First(type => type != null);

        private static object CreateBlackboardVariable(Type valueType, object value)
        {
            Type variableType = RuntimeType("Unity.Behavior.BlackboardVariable`1")
                .MakeGenericType(valueType);
            object variable = Activator.CreateInstance(variableType);
            variableType.GetProperty("Value")?.SetValue(variable, value);
            return variable;
        }
    }
}
