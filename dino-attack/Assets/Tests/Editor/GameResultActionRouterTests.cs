using System;
using System.Reflection;
using LlamAcademy.Dinos.Session;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class GameResultActionRouterTests
    {
        private static readonly GameLaunchRequest Current = GameLaunchRequest.CreateHuman(
            GameMapId.LayeredBattlefield,
            314159);

        [TestCase(0, 24, true)]
        [TestCase(0, 25, false)]
        [TestCase(1, 0, false)]
        public void ResourceExhaustionDefeat_RequiresNoDinosAndLessThanCheapestDino(
            int aliveDinos,
            int food,
            bool expected)
        {
            Type roundManagerType = Type.GetType(
                "LlamAcademy.Dinos.RoundManagement.RoundManager, Assembly-CSharp",
                true);
            MethodInfo rule = roundManagerType.GetMethod(
                "ShouldEndForResourceExhaustion",
                BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);

            Assert.That(rule, Is.Not.Null,
                "RoundManager must expose one deterministic resource-exhaustion rule for runtime and tests.");
            Assert.That((bool)rule.Invoke(null, new object[] { aliveDinos, food }), Is.EqualTo(expected));
        }

        [Test]
        public void Retry_KeepsMapAndSeedAndAutoStartsHumanSession()
        {
            GameResultActionRoute route = GameResultActionRouter.Resolve(
                GameResultAction.RetryCurrentLayout,
                Current,
                GameInferenceLaunchOption.Unavailable("No trained AI model is configured."));

            Assert.That(route.IsAvailable, Is.True);
            Assert.That(route.Request.MapId, Is.EqualTo(GameMapId.LayeredBattlefield));
            Assert.That(route.Request.LayoutSeed, Is.EqualTo(314159));
            Assert.That(route.Request.Controller, Is.EqualTo(GameControllerMode.Human));
            Assert.That(route.Request.ReuseLayoutSeed, Is.True);
            Assert.That(route.Request.StartAutomatically, Is.True);
            Assert.That(route.PreservePlayerResult, Is.False);
        }

        [Test]
        public void NewLayout_KeepsMapChangesSeedAndAutoStartsHumanSession()
        {
            GameResultActionRoute route = GameResultActionRouter.Resolve(
                GameResultAction.GenerateNewLayout,
                Current,
                GameInferenceLaunchOption.Unavailable("No trained AI model is configured."));

            Assert.That(route.IsAvailable, Is.True);
            Assert.That(route.Request.MapId, Is.EqualTo(GameMapId.LayeredBattlefield));
            Assert.That(route.Request.LayoutSeed, Is.Not.EqualTo(314159));
            Assert.That(route.Request.LayoutSeed, Is.Not.EqualTo(int.MinValue));
            Assert.That(route.Request.Controller, Is.EqualTo(GameControllerMode.Human));
            Assert.That(route.Request.ReuseLayoutSeed, Is.False);
            Assert.That(route.Request.StartAutomatically, Is.True);
            Assert.That(route.PreservePlayerResult, Is.False);
        }

        [Test]
        public void AiStrategy_KeepsMapAndSeedAndCarriesPlayerResult()
        {
            GameInferenceLaunchOption inference = GameInferenceLaunchOption.Available(
                inferenceSeed: 2718,
                modelId: "default-v2",
                modelHash: new string('a', 64),
                protocolVersion: GameLaunchRequest.CurrentProtocolVersion);

            GameResultActionRoute route = GameResultActionRouter.Resolve(
                GameResultAction.ReplayWithAi,
                Current,
                inference);

            Assert.That(route.IsAvailable, Is.True);
            Assert.That(route.Request.MapId, Is.EqualTo(GameMapId.LayeredBattlefield));
            Assert.That(route.Request.LayoutSeed, Is.EqualTo(314159));
            Assert.That(route.Request.Controller, Is.EqualTo(GameControllerMode.InferenceAI));
            Assert.That(route.Request.ReuseLayoutSeed, Is.True);
            Assert.That(route.Request.StartAutomatically, Is.True);
            Assert.That(route.PreservePlayerResult, Is.True);
        }

        [Test]
        public void SelectMap_ClearsComparisonAndReturnsToMenuWithoutAutoStart()
        {
            GameResultActionRoute route = GameResultActionRouter.Resolve(
                GameResultAction.SelectMap,
                Current,
                GameInferenceLaunchOption.Unavailable("No trained AI model is configured."));

            Assert.That(route.IsAvailable, Is.True);
            Assert.That(route.Request.MapId, Is.EqualTo(GameMapId.MapSelect));
            Assert.That(route.Request.Controller, Is.EqualTo(GameControllerMode.Human));
            Assert.That(route.Request.StartAutomatically, Is.False);
            Assert.That(route.PreservePlayerResult, Is.False);
        }

        [Test]
        public void AiStrategyWithoutCompatibleModel_IsUnavailableWithoutBlockingOtherRoutes()
        {
            const string reason = "No trained AI model is configured.";
            GameInferenceLaunchOption unavailable = GameInferenceLaunchOption.Unavailable(reason);

            GameResultActionRoute ai = GameResultActionRouter.Resolve(
                GameResultAction.ReplayWithAi,
                Current,
                unavailable);
            GameResultActionRoute retry = GameResultActionRouter.Resolve(
                GameResultAction.RetryCurrentLayout,
                Current,
                unavailable);

            Assert.That(ai.IsAvailable, Is.False);
            Assert.That(ai.UnavailableReason, Is.EqualTo(reason));
            Assert.That(retry.IsAvailable, Is.True);
        }

        [Test]
        public void MenuLaunchWaitsForCentralStartWhileEveryResultReplayAutoStarts()
        {
            GameLaunchRequest menuLaunch = GameLaunchRequest.CreateHuman(
                GameMapId.OpenTropicalBattlefield,
                42);
            GameInferenceLaunchOption inference = GameInferenceLaunchOption.Available(
                7,
                "default-v2",
                new string('b', 64),
                GameLaunchRequest.CurrentProtocolVersion);

            Assert.That(menuLaunch.StartAutomatically, Is.False);
            Assert.That(GameResultActionRouter.Resolve(
                GameResultAction.RetryCurrentLayout, menuLaunch, inference).Request.StartAutomatically, Is.True);
            Assert.That(GameResultActionRouter.Resolve(
                GameResultAction.GenerateNewLayout, menuLaunch, inference).Request.StartAutomatically, Is.True);
            Assert.That(GameResultActionRouter.Resolve(
                GameResultAction.ReplayWithAi, menuLaunch, inference).Request.StartAutomatically, Is.True);
            Assert.That(GameMapId.OpenTropicalBattlefield.DisplayName, Is.EqualTo("开阔战场"));
            Assert.That(GameMapId.LayeredBattlefield.DisplayName, Is.EqualTo("分层战场"));
        }

        [Test]
        public void RuntimeResultController_AlwaysOpensStrategyPickerAndDisablesUnavailableModels()
        {
            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/LlamAcademy/Dinos/UI/RuntimeUI.uxml");
            Assert.That(tree, Is.Not.Null);
            TemplateContainer root = tree.CloneTree();
            string[] requiredButtons =
            {
                "restart-button",
                "new-layout-button",
                "ai-strategy-button",
                "select-map-button",
                "ppo-strategy-button",
                "fpo-strategy-button",
                "reinflow-strategy-button",
            };
            foreach (string buttonName in requiredButtons)
                Assert.That(root.Q<Button>(buttonName), Is.Not.Null, buttonName);

            Type controllerType = Type.GetType(
                "LlamAcademy.Dinos.UI.GameResultActionController, Assembly-CSharp",
                true);
            GameObject owner = new("GameResultActionControllerTests.Owner");
            Component controller = owner.AddComponent(controllerType);
            try
            {
                MethodInfo configure = controllerType.GetMethod(
                    "Configure",
                    BindingFlags.Instance | BindingFlags.Public);
                Assert.That(configure, Is.Not.Null);
                configure.Invoke(controller, new object[]
                {
                    root,
                    Current,
                    null,
                    GameInferenceLaunchOption.Unavailable("No trained AI model is configured."),
                    null,
                });

                Assert.That(root.Q<Button>("restart-button").enabledSelf, Is.True);
                Assert.That(root.Q<Button>("new-layout-button").enabledSelf, Is.True);
                Assert.That(root.Q<Button>("select-map-button").enabledSelf, Is.True);
                Assert.That(root.Q<Button>("ai-strategy-button").enabledSelf, Is.True);
                Assert.That(root.Q<Button>("ppo-strategy-button").enabledSelf, Is.False);
                Assert.That(root.Q<Button>("fpo-strategy-button").enabledSelf, Is.False);
                Assert.That(root.Q<Button>("reinflow-strategy-button").enabledSelf, Is.False);

                MethodInfo openStrategyPicker = controllerType.GetMethod(
                    "OpenStrategyPicker",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(openStrategyPicker, Is.Not.Null);
                openStrategyPicker.Invoke(controller, null);
                Assert.That(root.Q("strategy-panel").style.display.value, Is.EqualTo(DisplayStyle.Flex));
                Assert.That(root.Q<Label>("ai-strategy-reason").text,
                    Is.EqualTo("No compatible trained AI model is available."));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        [Test]
        public void ComparisonPresenter_ShowsBothFormalSnapshotsAndExplicitLayoutRejection()
        {
            VisualTreeAsset tree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
                "Assets/LlamAcademy/Dinos/UI/RuntimeUI.uxml");
            TemplateContainer root = tree.CloneTree();
            Assert.That(root.Q("comparison-panel"), Is.Not.Null);
            Assert.That(root.Q<Label>("human-comparison-result"), Is.Not.Null);
            Assert.That(root.Q<Label>("ai-comparison-result"), Is.Not.Null);
            Assert.That(root.Q<Label>("comparison-status"), Is.Not.Null);

            GameResultSnapshot human = ComparisonSnapshot(
                GameControllerMode.Human,
                true,
                18.5f,
                225,
                12.25f,
                string.Empty,
                string.Empty);
            GameResultSnapshot ai = ComparisonSnapshot(
                GameControllerMode.InferenceAI,
                false,
                40f,
                25,
                -0.93f,
                "default-v2",
                new string('d', 64));
            GameResultComparison comparison = new(human, ai);

            Type presenterType = Type.GetType(
                "LlamAcademy.Dinos.UI.GameComparisonPresenter, Assembly-CSharp",
                true);
            GameObject owner = new("GameComparisonPresenterTests.Owner");
            Component presenter = owner.AddComponent(presenterType);
            try
            {
                presenterType.GetMethod("Configure").Invoke(presenter, new object[] { root });
                presenterType.GetMethod("PresentComparison").Invoke(presenter, new object[] { comparison });

                Assert.That(root.Q<Label>("human-comparison-result").text,
                    Does.Contain("Victory").And.Contain("18.50s").And.Contain("225").And.Contain("12.25"));
                Assert.That(root.Q<Label>("ai-comparison-result").text,
                    Does.Contain("Defeat").And.Contain("40.00s").And.Contain("25").And.Contain("-0.93"));
                Assert.That(root.Q<Label>("comparison-status").text, Does.Contain("Same layout"));

                presenterType.GetMethod("PresentRejected").Invoke(
                    presenter,
                    new object[] { GameResultPairingFailure.LayoutSignatureMismatch });
                Assert.That(root.Q<Label>("comparison-status").text,
                    Does.Contain("layout signature").IgnoreCase);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(owner);
            }
        }

        private static GameResultSnapshot ComparisonSnapshot(
            GameControllerMode controller,
            bool won,
            float elapsedSeconds,
            int finalMeat,
            float totalReward,
            string modelId,
            string modelHash) =>
            new(
                GameMapId.LayeredBattlefield,
                GameMapId.LayeredBattlefield.SceneStableId,
                314159,
                controller,
                won,
                elapsedSeconds,
                finalMeat,
                totalReward,
                "layered_battlefield|314159|comparison-layout",
                modelId,
                modelHash,
                GameLaunchRequest.CurrentProtocolVersion);
    }
}
