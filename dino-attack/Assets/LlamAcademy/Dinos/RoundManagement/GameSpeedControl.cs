using LlamAcademy.Dinos.Session;
using LlamAcademy.Dinos.Training;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.RoundManagement
{
    // Scene-owned, so no saved preference can leak into training or another map.
    [DefaultExecutionOrder(100), DisallowMultipleComponent]
    public sealed class GameSpeedControl : MonoBehaviour
    {
        private readonly float[] Speeds = { 0.5f, 1f, 2f };
        private UIDocument Document;
        private VisualElement Panel;
        private Button[] Buttons;
        private bool OwnsClock;
        public float SelectedSpeed { get; private set; } = 1f;

        private void Start()
        {
            var session = GetComponent<GameSessionRestartService>();
            var training = FindFirstObjectByType<DinoTrainingBootstrap>();
            if (session == null || session.CurrentLaunchRequest.Controller == GameControllerMode.TrainingAI ||
                (training != null && training.IsTrainingMode)) { enabled = false; return; }
            OwnsClock = true;
            TrySetSpeed(session.CurrentLaunchRequest.MapId == GameMapId.FirstPersonDefenseBattlefield ? 0.5f : 1f);
        }

        public bool TrySetSpeed(float speed)
        {
            if (!OwnsClock || (speed != 0.5f && speed != 1f && speed != 2f)) return false;
            SelectedSpeed = speed;
            Time.timeScale = speed;
            return true;
        }

        private void Update()
        {
            if (!OwnsClock) return;
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;
            if (keyboard.f1Key.wasPressedThisFrame) TrySetSpeed(0.5f);
            if (keyboard.f2Key.wasPressedThisFrame) TrySetSpeed(1f);
            if (keyboard.f3Key.wasPressedThisFrame) TrySetSpeed(2f);
        }

        private void LateUpdate()
        {
            if (!OwnsClock) return;
            if (Document == null) Document = FindFirstObjectByType<UIDocument>();
            VisualElement root = Document != null ? Document.rootVisualElement : null;
            if (root == null) return;
            if (Panel == null || Panel.parent != root) Build(root);
            for (int i = 0; i < Buttons.Length; i++)
                Buttons[i].style.backgroundColor = Speeds[i] == SelectedSpeed
                    ? new Color(0.13f, 0.48f, 0.59f) : new Color(0.1f, 0.15f, 0.2f);
        }

        private void Build(VisualElement root)
        {
            Panel?.RemoveFromHierarchy();
            Panel = new VisualElement { name = "game-speed-settings" };
            Panel.style.position = Position.Absolute;
            Panel.style.right = 18;
            Panel.style.top = 82;
            Panel.style.flexDirection = FlexDirection.Row;
            Panel.style.backgroundColor = new Color(0.035f, 0.055f, 0.09f, 0.9f);
            Panel.style.paddingLeft = Panel.style.paddingRight = 8;
            Panel.style.alignItems = Align.Center;
            Panel.Add(new Label("倍速") { style = { fontSize = 19, color = Color.white, marginRight = 8 } });
            Buttons = new Button[Speeds.Length];
            for (int i = 0; i < Speeds.Length; i++)
            {
                float value = Speeds[i];
                Buttons[i] = new Button(() => TrySetSpeed(value)) { name = "game-speed-" + i, text = $"{value:0.#}×  F{i + 1}" };
                Buttons[i].style.fontSize = 17;
                Buttons[i].style.color = Color.white;
                Buttons[i].style.height = 36;
                Buttons[i].style.width = 91;
                Panel.Add(Buttons[i]);
            }
            root.Add(Panel);
        }

        private void OnDisable()
        {
            Panel?.RemoveFromHierarchy();
            if (!OwnsClock) return;
            OwnsClock = false;
            Time.timeScale = 1f;
        }
    }
}
