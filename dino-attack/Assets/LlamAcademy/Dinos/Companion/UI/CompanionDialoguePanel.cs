using System;
using LlamAcademy.Dinos.DefenseBattle;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.Companion
{
    /// <summary>
    /// The JEV command panel and independent reply popup own presentation and focus only;
    /// CompanionController remains the authority for commands and relationship rules.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    [DefaultExecutionOrder(96)]
    public sealed class CompanionDialoguePanel : MonoBehaviour
    {
        [SerializeField] private CompanionController Companion;
        [SerializeField] private FirstPersonDefender Player;

        private UIDocument Document;
        private VisualElement Root;
        private VisualElement Panel;
        private TextField Input;
        private Label Relationship;
        private Label State;
        private Label FocusHint;
        private VisualElement ReplyPopup;
        private Label ReplyPopupStatus;
        private Label ReplyPopupMessage;
        private Label ReplyPopupHint;
        private Button ReplayReplyButton;
        private CompanionIntroductionView Introduction;
        private float ReplyHideAt;
        private bool ReplyPending;
        private DefenseBattleCoordinator DefenseCoordinator;
        private bool PointerOverPanel;
        private bool PanelVisible = true;
        private int LastPanelToggleFrame = -1;
        private CompanionController SubscribedCompanion;

        public bool IsPanelVisible => PanelVisible;

        private void Awake()
        {
            Document = GetComponent<UIDocument>();
            Companion ??= FindFirstObjectByType<CompanionController>(FindObjectsInactive.Include);
            Player ??= FindFirstObjectByType<FirstPersonDefender>(FindObjectsInactive.Include);
            DefenseCoordinator = FindFirstObjectByType<DefenseBattleCoordinator>(FindObjectsInactive.Include);
            Build(Document.rootVisualElement);
            SubscribeToCompanionEvents();
        }

        private void Update()
        {
            if (Companion == null)
                Companion = FindFirstObjectByType<CompanionController>(FindObjectsInactive.Include);
            if (Player == null)
                Player = FindFirstObjectByType<FirstPersonDefender>(FindObjectsInactive.Include);
            if (DefenseCoordinator == null)
                DefenseCoordinator = FindFirstObjectByType<DefenseBattleCoordinator>(FindObjectsInactive.Include);
            if (Player == null) return;

            SubscribeToCompanionEvents();

            HandlePanelToggleInput();
            ApplyPanelInputMode();
            RefreshPanelPlacement();
            RefreshReplyPopupPlacement();
            if (ReplyPopup != null && !ReplyPending && Time.unscaledTime >= ReplyHideAt)
                ReplyPopup.style.display = DisplayStyle.None;
            RefreshState();
        }

        private void HandlePanelToggleInput()
        {
            Keyboard keyboard = Keyboard.current;
            if (Introduction != null && Introduction.IsVisible)
            {
                if (keyboard != null && (keyboard.escapeKey.wasPressedThisFrame || keyboard.tabKey.wasPressedThisFrame))
                    Introduction.Hide();
                return;
            }
            if (keyboard != null && keyboard.tabKey.wasPressedThisFrame)
            {
                TogglePanel();
            }
        }

        private void SubscribeToCompanionEvents()
        {
            if (SubscribedCompanion == Companion) return;
            if (SubscribedCompanion != null)
                SubscribedCompanion.ReplyReceived -= HandleReplyReceived;
            SubscribedCompanion = Companion;
            if (SubscribedCompanion != null)
                SubscribedCompanion.ReplyReceived += HandleReplyReceived;
        }

        private void HandleReplyReceived(string message, OrderAcceptance acceptance)
        {
            ReplyPending = false;
            ShowReply(message, AcceptanceLabel(acceptance), ReplyColor(acceptance));
            ReplayReplyButton.SetEnabled(true);
            ReplyPopupHint.text = "片刻后自动收起 · 可在交互面板再次查看回复";
            ReplyHideAt = Time.unscaledTime + ReplyReadingSeconds();
        }

        public void TogglePanel()
        {
            if (LastPanelToggleFrame == Time.frameCount) return;
            LastPanelToggleFrame = Time.frameCount;
            if (PanelVisible && IsInputFocused())
            {
                ReleaseTextFocus();
            }
            PanelVisible = !PanelVisible;
            PointerOverPanel = false;
            ApplyPanelInputMode();
        }

        public void SetPanelVisible(bool visible)
        {
            if (!visible) Introduction?.Hide();
            if (PanelVisible == visible) return;
            if (!visible && IsInputFocused()) ReleaseTextFocus();
            PanelVisible = visible;
            PointerOverPanel = false;
            ApplyPanelInputMode();
        }

        private void ApplyPanelInputMode()
        {
            if (Panel == null || Player == null) return;
            Panel.style.display = PanelVisible ? DisplayStyle.Flex : DisplayStyle.None;
            if (!PanelVisible)
            {
                // Hiding the panel must also leave the persistent interaction mode;
                // otherwise the cursor stays unlocked and normal mouse-look never resumes.
                Player.SetDialogueInputBlocked(false);
                if (Player.PersistentInteractionEnabled)
                    Player.ConfigurePersistentInteraction(false);
                Player.SetInteractionPointerOverUi(false);
                return;
            }

            // The game remains playable while the panel is visible. Only the text field
            // owns the keyboard while editing, and clicks that start on the panel never
            // become attacks or camera drags.
            if (!Player.PersistentInteractionEnabled)
                Player.ConfigurePersistentInteraction(true);
            bool readingIntroduction = Introduction != null && Introduction.IsVisible;
            Player.SetInteractionPointerOverUi(PointerOverPanel || readingIntroduction);
            Player.SetDialogueInputBlocked(IsInputFocused() || readingIntroduction);
        }

        private void RefreshPanelPlacement()
        {
            if (Panel == null) return;
            bool setup = DefenseCoordinator != null && DefenseCoordinator.Phase == DefenseBattlePhase.Setup;
            if (setup)
            {
                // The setup bar occupies the bottom of the defense battlefield. Dock
                // the persistent conversation panel to the upper-left so it cannot
                // cover the placement cards or the start button.
                Panel.style.left = 22;
                Panel.style.right = StyleKeyword.Auto;
                Panel.style.top = 86;
                Panel.style.bottom = StyleKeyword.Auto;
                Panel.style.maxHeight = Length.Percent(42);
            }
            else
            {
                Panel.style.left = StyleKeyword.Auto;
                Panel.style.right = 22;
                Panel.style.top = StyleKeyword.Auto;
                // Keep the persistent panel above the first-person instruction strip
                // during the active battle, so it does not hide controls or status text.
                Panel.style.bottom = 150;
                Panel.style.maxHeight = Length.Percent(46);
            }
        }

        private void RefreshReplyPopupPlacement()
        {
            if (ReplyPopup == null) return;
            float rootWidth = Root.resolvedStyle.width;
            if (float.IsNaN(rootWidth) || rootWidth <= 0) return;
            float width = Mathf.Min(720, rootWidth - 44);
            float left = (rootWidth - width) * 0.5f;
            float top = 94;
            bool setup = DefenseCoordinator != null && DefenseCoordinator.Phase == DefenseBattlePhase.Setup;
            if (setup && PanelVisible)
            {
                // Leave room for both the setup command panel and the right minimap.
                left = Mathf.Max(left, Panel.layout.xMax + 20);
                width = Mathf.Min(width, rootWidth - 300 - left);
                if (width < 360)
                {
                    width = Mathf.Min(720, rootWidth - 44);
                    left = (rootWidth - width) * 0.5f;
                    top = Panel.layout.yMax + 16;
                }
            }
            ReplyPopup.style.left = left;
            ReplyPopup.style.width = width;
            ReplyPopup.style.top = top;
        }

        private void Build(VisualElement root)
        {
            Root = root;
            Root.Clear();
            Root.style.flexGrow = 1;
            // Keep the full-screen document transparent to placement raycasts. Only
            // the panel itself should count as UI under the pointer; otherwise the
            // setup controller rejects every ground click as "over UI".
            Root.pickingMode = PickingMode.Ignore;
            CompanionUiStyle.ApplyFont(Root);

            Panel = new VisualElement { name = "companion-dialogue-panel" };
            CompanionUiStyle.Panel(Panel, CompanionUiStyle.Background);
            Panel.style.position = Position.Absolute;
            Panel.style.right = 22;
            Panel.style.bottom = 20;
            Panel.style.width = 520;
            Panel.style.minHeight = 264;
            Panel.style.maxHeight = Length.Percent(46);
            Panel.style.paddingLeft = Panel.style.paddingRight = 18;
            Panel.style.paddingTop = Panel.style.paddingBottom = 14;
            Panel.pickingMode = PickingMode.Position;
            Panel.RegisterCallback<PointerEnterEvent>(_ => PointerOverPanel = true);
            Panel.RegisterCallback<PointerLeaveEvent>(_ => PointerOverPanel = false);
            Panel.RegisterCallback<PointerMoveEvent>(evt => PointerOverPanel = Panel.worldBound.Contains(evt.position));

            var header = new VisualElement { name = "companion-dialogue-header" };
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            var title = CompanionUiStyle.Label(CompanionCharacterInfo.DisplayName + " · JEV 交互", 22);
            title.style.flexGrow = 1;
            header.Add(title);
            var about = CompanionUiStyle.Button("角色介绍", () => Introduction?.Show());
            about.name = "companion-show-introduction";
            about.style.fontSize = 14;
            about.style.minHeight = 28;
            about.style.marginRight = 8;
            header.Add(about);
            ReplayReplyButton = CompanionUiStyle.Button("查看回复", ReplayReply);
            ReplayReplyButton.name = "companion-replay-reply";
            ReplayReplyButton.style.fontSize = 14;
            ReplayReplyButton.style.minHeight = 28;
            ReplayReplyButton.SetEnabled(false);
            header.Add(ReplayReplyButton);
            Panel.Add(header);

            State = CompanionUiStyle.Label(string.Empty, 16, true);
            State.name = "companion-state";
            State.style.marginTop = 8;
            Panel.Add(State);

            Relationship = CompanionUiStyle.Label(string.Empty, 15, true);
            Relationship.name = "companion-relationship";
            Relationship.style.marginTop = 5;
            Panel.Add(Relationship);

            var quickTitle = CompanionUiStyle.Label("快捷命令", 14, true);
            quickTitle.style.marginTop = 8;
            Panel.Add(quickTitle);
            var quick = new VisualElement { name = "companion-quick-orders" };
            quick.style.flexDirection = FlexDirection.Row;
            quick.style.flexWrap = Wrap.Wrap;
            AddModeButton(quick, "跟随我", "FollowPlayer", 0);
            AddModeButton(quick, "自主防卫", "AutonomousDefense", 1);
            AddModeButton(quick, "守住这里", "HoldPosition", 2);
            AddModeButton(quick, "集合", "Regroup", 4);
            Panel.Add(quick);

            var inputTitle = CompanionUiStyle.Label("自然语言指令（例如：待在我身边）", 14, true);
            inputTitle.style.marginTop = 5;
            Panel.Add(inputTitle);
            var inputRow = new VisualElement { name = "companion-input-row" };
            inputRow.style.flexDirection = FlexDirection.Row;
            inputRow.style.alignItems = Align.Center;
            inputRow.style.marginTop = 5;
            Input = new TextField { name = "companion-dialogue-input" };
            Input.multiline = false;
            Input.isDelayed = false;
            Input.style.flexGrow = 1;
            Input.style.minHeight = 40;
            Input.style.marginRight = 8;
            Input.RegisterCallback<KeyDownEvent>(HandleInputKeyDown);
            inputRow.Add(Input);
            var send = CompanionUiStyle.Button("发送", SendText, true);
            send.name = "companion-dialogue-send";
            send.style.width = 86;
            send.style.marginTop = 0;
            send.style.marginBottom = 0;
            inputRow.Add(send);
            Panel.Add(inputRow);

            FocusHint = CompanionUiStyle.Label("Tab 显示/隐藏面板 · 输入框 Enter 发送并返回操控 · Esc 取消输入焦点", 13, true);
            FocusHint.name = "companion-focus-hint";
            FocusHint.style.marginTop = 3;
            Panel.Add(FocusHint);

            Root.Add(Panel);
            BuildReplyPopup(root);
            Introduction = new CompanionIntroductionView(root);
            RefreshState();
        }

        private void BuildReplyPopup(VisualElement root)
        {
            ReplyPopup = new VisualElement { name = "companion-reply-popup", pickingMode = PickingMode.Ignore };
            ReplyPopup.style.position = Position.Absolute;
            ReplyPopup.style.width = 720;
            ReplyPopup.style.top = 94;
            ReplyPopup.style.minHeight = 104;
            ReplyPopup.style.paddingLeft = ReplyPopup.style.paddingRight = 24;
            ReplyPopup.style.paddingTop = ReplyPopup.style.paddingBottom = 16;
            ReplyPopup.style.backgroundColor = new Color(0.025f, 0.04f, 0.12f, 0.98f);
            ReplyPopup.style.borderTopWidth = ReplyPopup.style.borderRightWidth =
                ReplyPopup.style.borderBottomWidth = ReplyPopup.style.borderLeftWidth = 2;
            ReplyPopup.style.borderTopColor = ReplyPopup.style.borderRightColor =
                ReplyPopup.style.borderBottomColor = ReplyPopup.style.borderLeftColor = CompanionUiStyle.Accent;
            ReplyPopup.style.borderTopLeftRadius = ReplyPopup.style.borderTopRightRadius =
                ReplyPopup.style.borderBottomLeftRadius = ReplyPopup.style.borderBottomRightRadius = 14;

            VisualElement header = new VisualElement { name = "companion-reply-popup-header", pickingMode = PickingMode.Ignore };
            header.style.flexDirection = FlexDirection.Row;
            header.style.alignItems = Align.Center;
            header.style.justifyContent = Justify.SpaceBetween;
            Label title = CompanionUiStyle.Label(CompanionCharacterInfo.DisplayName + " · 回复", 20);
            title.pickingMode = PickingMode.Ignore;
            header.Add(title);
            ReplyPopupStatus = CompanionUiStyle.Label("最新消息", 15, true);
            ReplyPopupStatus.name = "companion-reply-popup-status";
            ReplyPopupStatus.pickingMode = PickingMode.Ignore;
            ReplyPopupStatus.style.color = CompanionUiStyle.Accent;
            header.Add(ReplyPopupStatus);
            ReplyPopup.Add(header);

            ReplyPopupMessage = CompanionUiStyle.Label(string.Empty, 26);
            ReplyPopupMessage.name = "companion-reply-popup-message";
            ReplyPopupMessage.pickingMode = PickingMode.Ignore;
            ReplyPopupMessage.style.marginTop = 8;
            ReplyPopupMessage.style.minHeight = 42;
            ReplyPopupMessage.style.flexShrink = 0;
            ReplyPopupMessage.enableRichText = false;
            ReplyPopup.Add(ReplyPopupMessage);
            ReplyPopupHint = CompanionUiStyle.Label(string.Empty, 13, true);
            ReplyPopupHint.pickingMode = PickingMode.Ignore;
            ReplyPopupHint.style.marginTop = 8;
            ReplyPopup.Add(ReplyPopupHint);

            root.Add(ReplyPopup);
            ReplyPopup.style.display = DisplayStyle.None;
        }

        private void AddModeButton(VisualElement parent, string label, string modeName, int fallback)
        {
            var button = CompanionUiStyle.Button(label, () => SubmitMode(modeName, fallback));
            button.style.flexGrow = 1;
            button.style.minWidth = 100;
            button.style.marginRight = 5;
            parent.Add(button);
        }

        private bool IsInputFocused()
        {
            if (Input == null || Input.panel == null) return false;
            VisualElement focused = Input.panel.focusController?.focusedElement as VisualElement;
            for (VisualElement current = focused; current != null; current = current.parent)
                if (current == Input) return true;
            return false;
        }

        private void HandleInputKeyDown(KeyDownEvent evt)
        {
            if (evt.keyCode == KeyCode.Tab)
            {
                evt.StopPropagation();
                TogglePanel();
                return;
            }
            if (evt.keyCode == KeyCode.Escape)
            {
                evt.StopPropagation();
                ReleaseTextFocus();
                return;
            }
            // Enter submits the command and gives the keyboard back to the game while
            // leaving the panel available for the next order.
            if (evt.keyCode != KeyCode.Return && evt.keyCode != KeyCode.KeypadEnter) return;
            evt.StopPropagation();
            SendText();
            ReleaseTextFocus();
        }

        private void SendText()
        {
            if (Companion == null || Input == null || string.IsNullOrWhiteSpace(Input.value)) return;
            string text = Input.value.Trim();
            Input.value = string.Empty;
            ReplyPending = true;
            ReplayReplyButton.SetEnabled(false);
            ShowReply("正在理解你的指令，请稍候…", "思考中", CompanionUiStyle.Warning);
            ReplyPopupHint.text = "可以继续移动和观察战场";
            Companion.SubmitText(text);
            RefreshState();
        }

        private void SubmitMode(string name, int fallback)
        {
            if (Companion == null) return;
            Companion.SubmitMode(ParseMode(name, fallback));
            ReleaseTextFocus();
            RefreshState();
        }

        private static CompanionMode ParseMode(string name, int fallback)
        {
            if (Enum.TryParse(name, true, out CompanionMode mode)) return mode;
            return (CompanionMode)fallback;
        }

        private void ReleaseTextFocus()
        {
            Input?.Blur();
            Player?.SetDialogueInputBlocked(false);
        }

        private void RefreshState()
        {
            if (Companion == null || State == null) return;
            string order = Companion.CurrentOrder == null
                ? "未下达"
                : FormatMode(Companion.CurrentOrder.Mode);
            State.text = $"生命 {Companion.PlayerHealthRatio:P0}　行为：{Companion.BehaviorLabel}\n当前命令：{order}";
            Relationship.text = Companion.Profile == null
                ? "关系数据不可用"
                : $"好感 {Companion.Profile.Affinity:0} · 熟悉 {Companion.Profile.Familiarity:0} · 信任 {Companion.Profile.Trust:0} · 尊重 {Companion.Profile.Respect:0} · 压力 {Companion.Profile.Stress:0}";
        }

        private static string AcceptanceLabel(OrderAcceptance acceptance)
        {
            return acceptance switch
            {
                OrderAcceptance.Accepted => "已接受",
                OrderAcceptance.Partial => "部分接受",
                OrderAcceptance.Deferred => "暂缓执行",
                OrderAcceptance.Rejected => "未接受",
                _ => "最新消息",
            };
        }

        private void ShowReply(string message, string status, Color statusColor)
        {
            if (ReplyPopup == null || ReplyPopupMessage == null) return;
            ReplyPopup.style.display = DisplayStyle.Flex;
            ReplyPopupStatus.text = string.IsNullOrWhiteSpace(status) ? "最新消息" : status;
            ReplyPopupStatus.style.color = statusColor;
            ReplyPopupMessage.text = string.IsNullOrWhiteSpace(message) ? "（没有可显示的文字回复）" : message;
            ReplyPopupMessage.style.color = CompanionUiStyle.Text;
            RefreshReplyPopupPlacement();
        }

        private float ReplyReadingSeconds() => Mathf.Clamp(8f + ReplyPopupMessage.text.Length * 0.12f, 12f, 30f);

        private void ReplayReply()
        {
            if (ReplyPending || string.IsNullOrEmpty(ReplyPopupMessage.text)) return;
            ReplyPopup.style.display = DisplayStyle.Flex;
            ReplyHideAt = Time.unscaledTime + ReplyReadingSeconds();
        }

        private static Color ReplyColor(OrderAcceptance acceptance)
        {
            return acceptance switch
            {
                OrderAcceptance.Accepted => CompanionUiStyle.Accent,
                OrderAcceptance.Partial => CompanionUiStyle.Warning,
                OrderAcceptance.Deferred => new Color(1f, 0.8f, 0.35f, 1f),
                OrderAcceptance.Rejected => new Color(1f, 0.35f, 0.42f, 1f),
                _ => CompanionUiStyle.Accent,
            };
        }

        private static string FormatMode(CompanionMode mode) => mode switch
        {
            CompanionMode.AutonomousDefense => "自主防卫",
            CompanionMode.HoldPosition => "守点",
            CompanionMode.ProtectPlayer => "保护玩家",
            CompanionMode.Regroup => "集合",
            _ => "跟随玩家",
        };

        private void OnDisable()
        {
            if (SubscribedCompanion != null)
                SubscribedCompanion.ReplyReceived -= HandleReplyReceived;
            SubscribedCompanion = null;
            Player?.SetInteractionPointerOverUi(false);
            Player?.ConfigurePersistentInteraction(false);
            Player?.SetDialogueInputBlocked(false);
            if (Input != null) Input.UnregisterCallback<KeyDownEvent>(HandleInputKeyDown);
        }
    }
}
