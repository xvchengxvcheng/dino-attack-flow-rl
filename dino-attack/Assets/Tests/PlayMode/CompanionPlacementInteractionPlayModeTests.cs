using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using InputMouseButton = UnityEngine.InputSystem.LowLevel.MouseButton;

namespace LlamAcademy.Dinos.Tests.PlayMode
{
    /// <summary>
    /// Regression coverage for Map 4's persistent companion UI and defense placement.
    /// The production assemblies are intentionally reached through reflection so this
    /// test remains usable by the existing PlayMode test assembly definition.
    /// </summary>
    public sealed class CompanionPlacementInteractionPlayModeTests
    {
        [UnityTest]
        public IEnumerator PersistentCompanionPanel_DoesNotBlockGround_AndDefenseCanPlaceAndRemove()
        {
            LoadMap4();
            for (int frame = 0; frame < 6; frame++) yield return null;

            Component placement = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.DefensePlacementController");
            Assert.That(placement, Is.Not.Null, "Map 4 must contain the defense placement controller.");
            Assert.That(placement.GetType().GetProperty("IsLocked").GetValue(placement), Is.EqualTo(false));

            ValidateCompanionPanelLayout();

            Mouse mouse = Mouse.current;
            bool createdMouse = false;
            if (mouse == null)
            {
                mouse = InputSystem.AddDevice<Mouse>();
                createdMouse = true;
            }
            InputSystem.EnableDevice(mouse);

            try
            {
                Camera camera = GameObject.Find("Main Camera")?.GetComponent<Camera>() ?? Camera.main;
                Assert.That(camera, Is.Not.Null, "Map 4 setup must expose the placement camera.");

                Type placementKind = AppDomain.CurrentDomain.GetAssemblies()
                    .Select(assembly => assembly.GetType(
                        "LlamAcademy.Dinos.DefenseBattle.DefensePlacementKind", false))
                    .FirstOrDefault(value => value != null);
                Assert.That(placementKind, Is.Not.Null, "Map 4 defense placement enum must be loaded.");
                Vector3[] positions =
                {
                    new(-15f, 0f, -25f),
                    new(10f, 0f, -25f),
                    new(10f, 0f, -45f),
                };
                string[] kinds = { "Wall", "Archer", "Mage" };
                string[] inventory = { "Walls", "Archers", "Mages" };
                string[] objectPrefixes = { "Player Wall", "Player Archer", "Player Mage" };
                MethodInfo placeMethod = placement.GetType().GetMethod(
                    "PlaceSelectedAt", BindingFlags.Instance | BindingFlags.NonPublic);
                MethodInfo removeMethod = placement.GetType().GetMethod(
                    "TryRemoveAtPointer", BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(placeMethod, Is.Not.Null);
                Assert.That(removeMethod, Is.Not.Null);

                for (int index = 0; index < kinds.Length; index++)
                {
                    Vector2 screen = camera.WorldToScreenPoint(positions[index]);
                    Assert.That(screen.x, Is.InRange(0f, camera.pixelWidth), $"{kinds[index]} point must be visible.");
                    Assert.That(screen.y, Is.InRange(0f, camera.pixelHeight), $"{kinds[index]} point must be visible.");

                    object beforeInventory = placement.GetType().GetProperty("Inventory").GetValue(placement);
                    int before = ReadInt(beforeInventory, inventory[index]);
                    int beforeObjects = CountNamed(objectPrefixes[index]);
                    object kind = Enum.Parse(placementKind, kinds[index]);
                    Assert.That((bool)placement.GetType().GetMethod("Select").Invoke(placement, new[] { kind }), Is.True,
                        $"Selecting {kinds[index]} should be accepted while setup is active.");
                    yield return null;

                    // Invoke the same placement commit used by the pointer handler after
                    // the panel and ground hit have been checked above. This keeps the
                    // regression test deterministic across editor input focus modes.
                    placeMethod.Invoke(placement, new object[] { positions[index] });
                    yield return null;
                    object afterInventory = placement.GetType().GetProperty("Inventory").GetValue(placement);
                    Assert.That(ReadInt(afterInventory, inventory[index]), Is.EqualTo(before - 1),
                        $"Placing {kinds[index]} must consume one inventory entry.");
                    Assert.That(CountNamed(objectPrefixes[index]), Is.EqualTo(beforeObjects + 1),
                        $"Placing {kinds[index]} must create a live defense object.");

                    InputSystem.QueueStateEvent(mouse, new MouseState { position = screen });
                    InputSystem.Update();
                    removeMethod.Invoke(placement, null);
                    yield return null;
                    object restoredInventory = placement.GetType().GetProperty("Inventory").GetValue(placement);
                    Assert.That(ReadInt(restoredInventory, inventory[index]), Is.EqualTo(before),
                        $"Removing {kinds[index]} must restore its inventory entry.");
                    Assert.That(CountNamed(objectPrefixes[index]), Is.EqualTo(beforeObjects),
                        $"Removing {kinds[index]} must destroy the placed {kinds[index]} object.");
                }
            }
            finally
            {
                if (createdMouse && mouse != null) InputSystem.RemoveDevice(mouse);
            }
        }

        [UnityTest]
        public IEnumerator TabToggle_ReleasesTextFocusAndRestoresMouseLook()
        {
            LoadMap4();
            for (int frame = 0; frame < 6; frame++) yield return null;

            Component player = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.FirstPersonDefender");
            Component companionPanel = RuntimeComponent("LlamAcademy.Dinos.Companion.CompanionDialoguePanel");
            Assert.That(player, Is.Not.Null, "Map 4 must contain the player defender.");
            Assert.That(companionPanel, Is.Not.Null, "Map 4 must contain the JEV interaction panel.");

            MethodInfo beginDemo = player.GetType().GetMethod("BeginDemoMode");
            MethodInfo togglePanel = companionPanel.GetType().GetMethod("TogglePanel");
            Assert.That(beginDemo, Is.Not.Null);
            Assert.That(togglePanel, Is.Not.Null);
            beginDemo.Invoke(player, null);
            yield return null;
            yield return null;

            UIDocument companionDocument = UnityEngine.Object.FindObjectsByType<UIDocument>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(value => value.rootVisualElement.Q<TextField>("companion-dialogue-input") != null);
            Assert.That(companionDocument, Is.Not.Null);
            TextField input = companionDocument.rootVisualElement.Q<TextField>("companion-dialogue-input");
            input.Focus();
            yield return null;
            Assert.That(player.GetType().GetProperty("BlockCombatInput").GetValue(player), Is.EqualTo(true),
                "Focusing the command input should enter text editing mode.");

            togglePanel.Invoke(companionPanel, null);
            yield return null;
            VisualElement panel = companionDocument.rootVisualElement.Q<VisualElement>("companion-dialogue-panel");
            Assert.That(panel.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(player.GetType().GetProperty("PersistentInteractionEnabled").GetValue(player), Is.EqualTo(false));
            Assert.That(player.GetType().GetProperty("BlockCombatInput").GetValue(player), Is.EqualTo(false));
            Assert.That(player.GetType().GetProperty("IsLookCaptured").GetValue(player), Is.EqualTo(true),
                "Hiding the panel should restore the regular locked mouse-look mode.");

            togglePanel.Invoke(companionPanel, null);
            yield return null;
            Assert.That(panel.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Assert.That(player.GetType().GetProperty("PersistentInteractionEnabled").GetValue(player), Is.EqualTo(true));
            Assert.That(player.GetType().GetProperty("IsLookCaptured").GetValue(player), Is.EqualTo(false),
                "Showing the panel should return to the explicit right-drag interaction mode.");
        }

        [UnityTest]
        public IEnumerator CompanionReply_UsesIndependentNonBlockingPopup()
        {
            LoadMap4();
            for (int frame = 0; frame < 6; frame++) yield return null;

            Component companion = RuntimeComponent("LlamAcademy.Dinos.Companion.CompanionController");
            Assert.That(companion, Is.Not.Null, "Map 4 must contain the companion controller.");
            UIDocument document = UnityEngine.Object.FindObjectsByType<UIDocument>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
                .FirstOrDefault(value => value.rootVisualElement.Q<VisualElement>("companion-dialogue-panel") != null);
            Assert.That(document, Is.Not.Null);
            VisualElement root = document.rootVisualElement;
            VisualElement panel = root.Q<VisualElement>("companion-dialogue-panel");
            VisualElement popup = root.Q<VisualElement>("companion-reply-popup");
            Assert.That(popup, Is.Not.Null, "The companion reply must have its own popup element.");
            Assert.That(popup.parent, Is.EqualTo(root));
            Assert.That(IsWithin(popup, panel), Is.False, "The reply popup must not be nested in the command panel.");
            Assert.That(popup.pickingMode, Is.EqualTo(PickingMode.Ignore),
                "The reply popup must not intercept movement, aiming, or placement input.");

            Type modeType = AppDomain.CurrentDomain.GetAssemblies()
                .Select(assembly => assembly.GetType("LlamAcademy.Dinos.Companion.CompanionMode", false))
                .FirstOrDefault(value => value != null);
            Assert.That(modeType, Is.Not.Null);
            companion.GetType().GetMethod("SubmitMode").Invoke(companion,
                new[] { Enum.Parse(modeType, "AutonomousDefense") });
            yield return null;
            yield return null;

            Assert.That(popup.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));
            Label message = popup.Q<Label>("companion-reply-popup-message");
            Assert.That(message, Is.Not.Null);
            Assert.That(message.text, Is.Not.Empty);

            // The popup is a sibling overlay, so hiding the command panel must not
            // remove the reply or put the player back into UI input mode.
            Component panelController = RuntimeComponent("LlamAcademy.Dinos.Companion.CompanionDialoguePanel");
            MethodInfo setVisible = panelController.GetType().GetMethod("SetPanelVisible");
            Assert.That(setVisible, Is.Not.Null);
            setVisible.Invoke(panelController, new object[] { false });
            yield return null;
            Assert.That(panel.resolvedStyle.display, Is.EqualTo(DisplayStyle.None));
            Assert.That(popup.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));

            Component player = RuntimeComponent("LlamAcademy.Dinos.DefenseBattle.FirstPersonDefender");
            Assert.That(player.GetType().GetProperty("BlockCombatInput").GetValue(player), Is.EqualTo(false));
        }

        private static void ValidateCompanionPanelLayout()
        {
            UIDocument[] documents = UnityEngine.Object.FindObjectsByType<UIDocument>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            UIDocument defense = documents.FirstOrDefault(value => value.rootVisualElement.Q<Button>("wall-button") != null);
            UIDocument companion = documents.FirstOrDefault(value =>
                value.rootVisualElement.Q<VisualElement>("companion-dialogue-panel") != null);
            Assert.That(defense, Is.Not.Null, "Map 4 defense UI must be present.");
            Assert.That(companion, Is.Not.Null, "Map 4 companion panel must be present.");

            VisualElement root = companion.rootVisualElement;
            VisualElement panel = root.Q<VisualElement>("companion-dialogue-panel");
            Assert.That(root.pickingMode, Is.EqualTo(PickingMode.Ignore),
                "The companion document root must not intercept ground clicks.");
            Assert.That(panel.resolvedStyle.display, Is.EqualTo(DisplayStyle.Flex));

            VisualElement wall = defense.rootVisualElement.Q<Button>("wall-button");
            VisualElement archer = defense.rootVisualElement.Q<Button>("archer-button");
            VisualElement mage = defense.rootVisualElement.Q<Button>("mage-button");
            VisualElement start = defense.rootVisualElement.Q<Button>("start-button");
            Assert.That(wall, Is.Not.Null);
            Assert.That(archer, Is.Not.Null);
            Assert.That(mage, Is.Not.Null);
            Assert.That(start, Is.Not.Null);
            Assert.That(panel.worldBound.Overlaps(wall.worldBound), Is.False,
                "The companion panel must not cover the wall placement card.");
            Assert.That(panel.worldBound.Overlaps(archer.worldBound), Is.False,
                "The companion panel must not cover the archer placement card.");
            Assert.That(panel.worldBound.Overlaps(mage.worldBound), Is.False,
                "The companion panel must not cover the mage placement card.");
            Assert.That(panel.worldBound.Overlaps(start.worldBound), Is.False,
                "The companion panel must not cover the start battle button.");

            Camera camera = GameObject.Find("Main Camera")?.GetComponent<Camera>() ?? Camera.main;
            Vector2 screen = camera.WorldToScreenPoint(new Vector3(-20f, 0f, -30f));
            IPanel companionPanel = companion.rootVisualElement.panel;
            Assert.That(companionPanel, Is.Not.Null, "The companion UI must be attached to a runtime panel.");
            Vector2 panelPosition = RuntimePanelUtils.ScreenToPanel(companionPanel, screen);
            VisualElement picked = companionPanel.Pick(panelPosition);
            Assert.That(IsWithin(picked, panel), Is.False,
                "A defense ground point must not be picked as a child of the companion panel.");
            Assert.That(panel.worldBound.Contains(panelPosition), Is.False,
                "The archer placement point must remain outside the companion panel bounds.");
        }

        private static void LoadMap4()
        {
            Type editorScenes = Type.GetType("UnityEditor.SceneManagement.EditorSceneManager, UnityEditor");
            MethodInfo loadMethod = editorScenes?.GetMethod(
                "LoadSceneInPlayMode",
                BindingFlags.Public | BindingFlags.Static,
                null,
                new[] { typeof(string), typeof(LoadSceneParameters) },
                null);
            Assert.That(loadMethod, Is.Not.Null);
            loadMethod.Invoke(null, new object[]
            {
                "Assets/LlamAcademy/Dinos/Scenes/FirstPersonDefenseBattlefield.unity",
                new LoadSceneParameters(LoadSceneMode.Single)
            });
        }

        private static Component RuntimeComponent(string fullName)
        {
            Type type = Type.GetType($"{fullName}, Assembly-CSharp", true);
            return UnityEngine.Object.FindObjectsByType<Component>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
                .SingleOrDefault(value => value != null && value.GetType() == type);
        }

        private static int ReadInt(object value, string property)
        {
            return (int)value.GetType().GetProperty(property).GetValue(value);
        }

        private static int CountNamed(string prefix)
        {
            return UnityEngine.Object.FindObjectsByType<Transform>(
                FindObjectsInactive.Include, FindObjectsSortMode.None)
                .Count(value => value.name.StartsWith(prefix, StringComparison.Ordinal));
        }

        private static bool IsWithin(VisualElement element, VisualElement ancestor)
        {
            for (VisualElement current = element; current != null; current = current.parent)
                if (current == ancestor) return true;
            return false;
        }
    }
}
