using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.UIElements;
using UguiButton = UnityEngine.UI.Button;

namespace LlamAcademy.Dinos.Tests.Editor
{
    public sealed class DinoUiEnhancementEditorTests
    {
        private const string RuntimeUxml = "Assets/LlamAcademy/Dinos/UI/RuntimeUI.uxml";
        private const string RuntimeUss = "Assets/LlamAcademy/Dinos/UI/RuntimeStyles.uss";
        private const string MapSelectScene = "Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity";

        [Test]
        public void RuntimeUi_ProvidesForfeitAndThreeStrategyChoicesAboveTheBattleHud()
        {
            VisualTreeAsset asset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(RuntimeUxml);
            Assert.That(asset, Is.Not.Null);
            VisualElement root = asset.CloneTree();

            foreach (string id in new[]
                     {
                         "forfeit-button", "strategy-panel", "ppo-strategy-button",
                         "fpo-strategy-button", "reinflow-strategy-button", "close-strategy-button"
                     })
            {
                Assert.That(root.Q(id), Is.Not.Null, $"Runtime UI is missing '{id}'.");
            }

            string uxml = File.ReadAllText(RuntimeUxml);
            Assert.That(uxml.IndexOf("name=\"result-panel\"", StringComparison.Ordinal),
                Is.GreaterThan(uxml.IndexOf("name=\"bottom-bar\"", StringComparison.Ordinal)),
                "The full-screen result overlay must be declared after the battle HUD so it renders above dino cards.");

            string uss = File.ReadAllText(RuntimeUss);
            StringAssert.Contains(".comparison-result", uss);
            StringAssert.Contains(".comparison-status", uss);
            StringAssert.Contains("color: var(--light)", uss);
            StringAssert.Contains("#forfeit-button", uss);
            StringAssert.Contains(".strategy-panel", uss);
        }

        [Test]
        public void MapSelect_ContainsClosedDinosaurIntroductionWithThreeStatCards()
        {
            Scene scene = EditorSceneManager.OpenScene(MapSelectScene, OpenSceneMode.Single);
            Component controller = UnityEngine.Object.FindObjectsByType<Component>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Single(value => value != null && value.GetType().FullName ==
                    "LlamAcademy.Dinos.UI.MapSelectUI");
            SerializedObject serialized = new(controller);
            foreach (string field in new[]
                     {
                         "DinosaurIntroductionButton", "DinosaurIntroductionPanel", "CloseIntroductionButton"
                     })
            {
                Assert.That(serialized.FindProperty(field)?.objectReferenceValue, Is.Not.Null,
                    $"MapSelectUI field '{field}' must be wired.");
            }

            UguiButton introductionButton = UnityEngine.Object.FindObjectsByType<UguiButton>(
                    FindObjectsInactive.Include,
                    FindObjectsSortMode.None)
                .Single(value => value.gameObject.scene == scene && value.name == "Dinosaur Introduction");
            Assert.That(introductionButton.onClick.GetPersistentEventCount(), Is.EqualTo(1));
            Assert.That(introductionButton.onClick.GetPersistentMethodName(0), Is.EqualTo("ShowDinosaurIntroduction"));

            GameObject panel = (GameObject)serialized.FindProperty("DinosaurIntroductionPanel").objectReferenceValue;
            Assert.That(panel.activeSelf, Is.False, "The introduction must not cover map choices at startup.");
            string allText = string.Join("\n", panel.GetComponentsInChildren<Text>(true).Select(value => value.text));
            foreach (string expected in new[] { "肿头龙", "迅猛龙", "霸王龙", "25", "50", "100" })
            {
                StringAssert.Contains(expected, allText);
            }
        }
    }
}
