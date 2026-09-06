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
using UiButton = UnityEngine.UI.Button;

namespace LlamAcademy.Dinos.Tests.PlayMode
{
    public sealed class DinoUiEnhancementPlayModeTests
    {
        [UnityTest]
        public IEnumerator ForfeitDuringBattle_RecordsManualDefeatAndThenOffersStrategyPicker()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("Dinos", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 3; frame++) yield return null;

            Component round = FindRuntimeComponent("LlamAcademy.Dinos.RoundManagement.RoundManager");
            Component runtimeUi = FindRuntimeComponent("LlamAcademy.Dinos.UI.RuntimeUI");
            UIDocument document = runtimeUi.GetComponent<UIDocument>();
            UnityEngine.UIElements.Button forfeit = document.rootVisualElement.Q<UnityEngine.UIElements.Button>("forfeit-button");
            Assert.That(forfeit, Is.Not.Null);
            Assert.That(forfeit.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            round.GetType().GetMethod("StartRound", BindingFlags.Instance | BindingFlags.Public)
                ?.Invoke(round, null);
            float deadline = Time.realtimeSinceStartup + 5f;
            while (ReadProperty(round, "State").ToString() != "Running" && Time.realtimeSinceStartup < deadline)
            {
                yield return null;
            }

            Assert.That(ReadProperty(round, "State").ToString(), Is.EqualTo("Running"));
            Assert.That(forfeit.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Submit(forfeit);
            yield return null;

            Assert.That(ReadProperty(round, "State").ToString(), Is.EqualTo("Ending"));
            Assert.That(ReadProperty(round, "PlayerWon"), Is.False);
            Assert.That(forfeit.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));

            round.GetType().GetMethod("FinishSession", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.Invoke(round, null);
            yield return null;
            Assert.That(ReadProperty(round, "State").ToString(), Is.EqualTo("Ended"));

            UnityEngine.UIElements.Button chooseStrategy =
                document.rootVisualElement.Q<UnityEngine.UIElements.Button>("ai-strategy-button");
            Submit(chooseStrategy);
            yield return null;

            VisualElement strategyPanel = document.rootVisualElement.Q("strategy-panel");
            Assert.That(strategyPanel.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(document.rootVisualElement.Q<UnityEngine.UIElements.Button>("ppo-strategy-button").enabledSelf,
                Is.True);
            Assert.That(document.rootVisualElement.Q<UnityEngine.UIElements.Button>("fpo-strategy-button").enabledSelf,
                Is.False);
            Assert.That(document.rootVisualElement.Q<UnityEngine.UIElements.Button>("reinflow-strategy-button").enabledSelf,
                Is.False);
        }

        [UnityTest]
        public IEnumerator MapSelectIntroduction_OpensAndClosesWithoutLaunchingAMap()
        {
            AsyncOperation load = SceneManager.LoadSceneAsync("MapSelect", LoadSceneMode.Single);
            while (!load.isDone) yield return null;
            for (int frame = 0; frame < 2; frame++) yield return null;

            Component controller = FindRuntimeComponent("LlamAcademy.Dinos.UI.MapSelectUI");
            UiButton introduction = (UiButton)ReadField(controller, "DinosaurIntroductionButton");
            GameObject panel = (GameObject)ReadField(controller, "DinosaurIntroductionPanel");
            UiButton close = (UiButton)ReadField(controller, "CloseIntroductionButton");

            Assert.That(panel.activeSelf, Is.False);
            introduction.onClick.Invoke();
            yield return null;
            Assert.That(SceneManager.GetActiveScene().name, Is.EqualTo("MapSelect"));
            Assert.That(panel.activeSelf, Is.True);
            close.onClick.Invoke();
            yield return null;
            Assert.That(panel.activeSelf, Is.False);

            GameLaunchContext.Clear();
            Component restartService = FindRuntimeComponent(
                "LlamAcademy.Dinos.RoundManagement.GameSessionRestartService");
            UnityEngine.Object.Destroy(restartService.gameObject);
            yield return null;
        }

        private static object ReadProperty(Component component, string name) =>
            component.GetType().GetProperty(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                ?.GetValue(component);

        private static object ReadField(Component component, string name) =>
            component.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)?.GetValue(component);

        private static Component FindRuntimeComponent(string fullName) =>
            UnityEngine.Object.FindObjectsByType<Component>(FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Single(value => value != null && value.GetType().FullName == fullName);

        private static void Submit(UnityEngine.UIElements.Button button)
        {
            Assert.That(button, Is.Not.Null);
            using NavigationSubmitEvent submit = NavigationSubmitEvent.GetPooled();
            submit.target = button;
            button.SendEvent(submit);
        }
    }
}
