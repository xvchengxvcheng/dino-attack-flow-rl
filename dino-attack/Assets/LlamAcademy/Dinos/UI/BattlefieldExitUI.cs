using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Session;
using LlamAcademy.Dinos.Training;
using UnityEngine;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.UI
{
    // Kept separate from the battle HUD because the JEV demo hides that HUD.
    [DisallowMultipleComponent]
    public sealed class BattlefieldExitUI : MonoBehaviour
    {
        private GameSessionRestartService Session;
        private UIDocument Document;
        private Button ExitButton;

        private void Start()
        {
            Session = GetComponent<GameSessionRestartService>();
            var training = FindFirstObjectByType<DinoTrainingBootstrap>();
            if (Session == null || Session.CurrentLaunchRequest.Controller == GameControllerMode.TrainingAI ||
                (training != null && training.IsTrainingMode)) return;

            PanelSettings settings = null;
            foreach (UIDocument source in FindObjectsByType<UIDocument>(FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (source.gameObject.scene != gameObject.scene || source.panelSettings == null) continue;
                settings = source.panelSettings;
                break;
            }
            if (settings == null)
            {
                Debug.LogError("Battlefield exit UI requires a scene UI panel.", this);
                return;
            }

            var overlay = new GameObject("Battlefield Exit UI");
            overlay.transform.SetParent(transform, false);
            Document = overlay.AddComponent<UIDocument>();
            Document.panelSettings = settings;
            Document.sortingOrder = 1000;
            VisualElement root = Document.rootVisualElement;
            root.pickingMode = PickingMode.Ignore;
            root.style.flexGrow = 1;

            ExitButton = new Button(ReturnToMapSelect) { name = "exit-button", text = "退出", tooltip = "返回地图选择" };
            ExitButton.style.position = Position.Absolute;
            ExitButton.style.top = 26;
            ExitButton.style.right = 18;
            ExitButton.style.width = 110;
            ExitButton.style.height = 42;
            ExitButton.style.fontSize = 22;
            ExitButton.style.unityFontStyleAndWeight = FontStyle.Bold;
            ExitButton.style.color = Color.white;
            ExitButton.style.backgroundColor = new Color(0.25f, 0.14f, 0.08f, 0.96f);
            ExitButton.style.borderTopColor = ExitButton.style.borderBottomColor =
                ExitButton.style.borderLeftColor = ExitButton.style.borderRightColor = new Color(0.75f, 0.5f, 0.25f);
            Font font = Resources.Load<Font>("CompanionFonts/NotoSansCJKsc-Regular");
            if (font != null) ExitButton.style.unityFontDefinition = FontDefinition.FromFont(font);
            root.Add(ExitButton);
        }

        private void ReturnToMapSelect()
        {
            if (Session != null && Session.TryScheduleLaunch(GameLaunchContext.CreateMapSelectionRequest()))
                ExitButton.SetEnabled(false);
        }

        private void OnDestroy()
        {
            if (ExitButton != null) ExitButton.clicked -= ReturnToMapSelect;
        }
    }
}
