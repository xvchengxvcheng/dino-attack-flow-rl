using LlamAcademy.Dinos.Player;
using LlamAcademy.Dinos.RoundManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.DefenseBattle
{
    [DefaultExecutionOrder(50), RequireComponent(typeof(UIDocument)), DisallowMultipleComponent]
    public sealed class DefenseBattleUI : MonoBehaviour
    {
        [SerializeField] private DefenseBattleCoordinator Coordinator;
        [SerializeField] private DefensePlacementController Placement;

        private Label PhaseLabel;
        private Label HudLabel;
        private Label MissingLabel;
        private Label StatusLabel;
        private Label LockLabel;
        private Label ResultTitle;
        private Label ResultSummary;
        private Button WallButton;
        private Button ArcherButton;
        private Button MageButton;
        private Button PpoButton;
        private Button FpoButton;
        private Button ReinFlowButton;
        private Button PolicyFlowButton;
        private Button StartButton;
        private VisualElement SetupBar;
        private VisualElement ResultOverlay;
        private VisualElement ConfirmOverlay;
        private DefenseBattlePhase LastPhase = (DefenseBattlePhase)(-1);
        private Font RuntimeFont;

        private void Awake()
        {
            Coordinator ??= FindFirstObjectByType<DefenseBattleCoordinator>(FindObjectsInactive.Include);
            Placement ??= FindFirstObjectByType<DefensePlacementController>(FindObjectsInactive.Include);
            Build(GetComponent<UIDocument>().rootVisualElement);
        }

        private void Start()
        {
            if (Coordinator != null) Coordinator.StateChanged += Refresh;
            if (Placement != null) Placement.StateChanged += Refresh;
            Refresh();
        }

        private void Update()
        {
            if (Coordinator == null || RoundManager.Instance == null) return;
            float remaining = Mathf.Max(0f, RoundManager.SessionDurationSeconds - RoundManager.Instance.RunningElapsedSeconds);
            HudLabel.text = Coordinator.Phase == DefenseBattlePhase.Battle
                ? $"剩余 {Mathf.CeilToInt(remaining)}s   Strategy {Coordinator.StrategyDisplayName}   " +
                  $"AI 肉量 {DinoSpawner.Instance.ResourcesToSpend}   恐龙 {RoundManager.Instance.AliveDinos}   " +
                  $"地面守卫 {Placement.AliveGroundGuards}   城墙 {Placement.AliveWalls}"
                : $"Strategy {Coordinator.StrategyDisplayName}   城墙 {Placement.Inventory.Walls}   " +
                  $"弓箭手 {Placement.Inventory.Archers}   法师 {Placement.Inventory.Mages}";
            if (LastPhase != Coordinator.Phase) Refresh();
        }

        private void Refresh()
        {
            if (Coordinator == null || Placement == null) return;
            LastPhase = Coordinator.Phase;
            PhaseLabel.text = Coordinator.Phase switch
            {
                DefenseBattlePhase.Setup => Coordinator.IsRetryLayoutLocked ? "防御战场 · 选择 AI Strategy" : "防御战场 · 玩家布防",
                DefenseBattlePhase.Battle => "防御战场 · AI 进攻中",
                _ => "防御战场 · 结算",
            };
            bool setup = Coordinator.Phase == DefenseBattlePhase.Setup;
            SetupBar.style.display = setup ? DisplayStyle.Flex : DisplayStyle.None;
            ResultOverlay.style.display = Coordinator.Phase == DefenseBattlePhase.Result ? DisplayStyle.Flex : DisplayStyle.None;

            WallButton.text = $"城墙\n剩余 {Placement.Inventory.Walls}";
            ArcherButton.text = $"弓箭手\n剩余 {Placement.Inventory.Archers}";
            MageButton.text = $"法师\n剩余 {Placement.Inventory.Mages}";
            WallButton.SetEnabled(setup && !Placement.IsLocked && Placement.Inventory.Walls > 0);
            ArcherButton.SetEnabled(setup && !Placement.IsLocked && Placement.Inventory.Archers > 0);
            MageButton.SetEnabled(setup && !Placement.IsLocked && Placement.Inventory.Mages > 0);
            PpoButton.SetEnabled(setup && Coordinator.PpoOption.IsAvailable);
            FpoButton.SetEnabled(setup && Coordinator.FpoOption.IsAvailable);
            PolicyFlowButton.SetEnabled(setup && Coordinator.PolicyFlowOption.IsAvailable);
            ReinFlowButton.SetEnabled(setup && Coordinator.ReinFlowOption.IsAvailable);
            PpoButton.text = Coordinator.PpoOption.IsAvailable ? "PPO" : "PPO · 不可用";
            FpoButton.text = Coordinator.FpoOption.IsAvailable ? "FPO" : "FPO · 不可用";
            ReinFlowButton.text = Coordinator.ReinFlowOption.IsAvailable ? "ReinFlow" : "ReinFlow · 不可用";
            StartButton.SetEnabled(Coordinator.CanStart);
            MissingLabel.text = Coordinator.BuildMissingMessage();
            MissingLabel.style.display = string.IsNullOrEmpty(MissingLabel.text) ? DisplayStyle.None : DisplayStyle.Flex;
            StatusLabel.text = Coordinator.StatusMessage;
            StatusLabel.style.display = string.IsNullOrEmpty(StatusLabel.text) ? DisplayStyle.None : DisplayStyle.Flex;
            LockLabel.style.display = Coordinator.IsRetryLayoutLocked ? DisplayStyle.Flex : DisplayStyle.None;

            if (Coordinator.Phase == DefenseBattlePhase.Result && RoundManager.Instance != null)
            {
                ResultTitle.text = Coordinator.DefenderWon ? "胜利" : "失败";
                ResultTitle.style.color = Coordinator.DefenderWon
                    ? new Color(0.35f, 0.95f, 0.45f)
                    : new Color(1f, 0.35f, 0.3f);
                ResultSummary.text = $"Strategy：{Coordinator.StrategyDisplayName}\n坚持时间：{RoundManager.Instance.RunningElapsedSeconds:0.0}s";
            }
        }

        private void Build(VisualElement root)
        {
            root.Clear();
            root.pickingMode = PickingMode.Ignore;
            root.style.flexGrow = 1;
            RuntimeFont = CreateChineseFont();
            if (RuntimeFont != null)
            {
                root.style.unityFont = RuntimeFont;
                root.style.unityFontDefinition = FontDefinition.FromFont(RuntimeFont);
            }

            VisualElement top = Panel();
            top.style.position = Position.Absolute;
            top.style.left = 18;
            top.style.right = 18;
            top.style.top = 14;
            top.style.height = 68;
            top.style.flexDirection = FlexDirection.Row;
            top.style.alignItems = Align.Center;
            PhaseLabel = LabelText("防御战场");
            PhaseLabel.name = "phase-label";
            PhaseLabel.style.fontSize = 28;
            PhaseLabel.style.unityFontStyleAndWeight = FontStyle.Bold;
            PhaseLabel.style.marginLeft = 14;
            PhaseLabel.style.minWidth = 310;
            HudLabel = LabelText(string.Empty);
            HudLabel.name = "hud-label";
            HudLabel.style.fontSize = 22;
            HudLabel.style.flexGrow = 1;
            HudLabel.style.unityTextAlign = TextAnchor.MiddleCenter;
            Button exit = ButtonText("退出", () =>
            {
                if (Coordinator.Phase == DefenseBattlePhase.Battle)
                    ConfirmOverlay.style.display = DisplayStyle.Flex;
                else
                    Coordinator.ExitSetupToMapSelect();
            });
            exit.name = "exit-button";
            exit.style.width = 110;
            top.Add(PhaseLabel);
            top.Add(HudLabel);
            top.Add(exit);
            root.Add(top);

            SetupBar = Panel();
            SetupBar.style.position = Position.Absolute;
            SetupBar.style.left = 18;
            SetupBar.style.right = 18;
            SetupBar.style.bottom = 16;
            SetupBar.style.minHeight = 174;
            SetupBar.style.flexDirection = FlexDirection.Row;
            SetupBar.style.alignItems = Align.Center;
            SetupBar.style.paddingLeft = 12;
            SetupBar.style.paddingRight = 12;
            WallButton = Card("城墙", () => Placement.Select(DefensePlacementKind.Wall));
            ArcherButton = Card("弓箭手", () => Placement.Select(DefensePlacementKind.Archer));
            MageButton = Card("法师", () => Placement.Select(DefensePlacementKind.Mage));
            WallButton.name = "wall-button";
            ArcherButton.name = "archer-button";
            MageButton.name = "mage-button";
            SetupBar.Add(WallButton);
            SetupBar.Add(ArcherButton);
            SetupBar.Add(MageButton);

            VisualElement strategies = new();
            strategies.style.flexDirection = FlexDirection.Row;
            strategies.style.marginLeft = 14;
            PpoButton = Strategy("PPO", () => Coordinator.SelectStrategy(DefenseAiStrategy.Ppo));
            FpoButton = Strategy("FPO", () => Coordinator.SelectStrategy(DefenseAiStrategy.Fpo));
            ReinFlowButton = Strategy("ReinFlow", () => Coordinator.SelectStrategy(DefenseAiStrategy.ReinFlow));
            PpoButton.name = "ppo-button";
            FpoButton.name = "fpo-button";
            ReinFlowButton.name = "reinflow-button";
            strategies.Add(PpoButton);
            strategies.Add(FpoButton);
            PolicyFlowButton = Strategy("PolicyFlow", () => Coordinator.SelectStrategy(DefenseAiStrategy.PolicyFlow));
            PolicyFlowButton.name = "policyflow-button";
            strategies.Add(PolicyFlowButton);
            strategies.Add(ReinFlowButton);
            SetupBar.Add(strategies);

            VisualElement startColumn = new();
            startColumn.style.flexGrow = 1;
            startColumn.style.alignItems = Align.FlexEnd;
            MissingLabel = LabelText(string.Empty);
            MissingLabel.name = "missing-label";
            MissingLabel.style.fontSize = 21;
            MissingLabel.style.color = new Color(1f, 0.82f, 0.36f);
            MissingLabel.style.marginBottom = 5;
            LockLabel = LabelText("原布局已锁定");
            LockLabel.style.fontSize = 21;
            LockLabel.style.color = new Color(0.5f, 0.85f, 1f);
            StartButton = ButtonText("开始 AI 进攻", () => Coordinator.TryStartBattle());
            StartButton.name = "start-button";
            StartButton.style.width = 220;
            StartButton.style.height = 58;
            startColumn.Add(LockLabel);
            startColumn.Add(MissingLabel);
            startColumn.Add(StartButton);
            SetupBar.Add(startColumn);
            root.Add(SetupBar);

            StatusLabel = LabelText(string.Empty);
            StatusLabel.style.position = Position.Absolute;
            StatusLabel.style.left = 18;
            StatusLabel.style.bottom = 196;
            StatusLabel.style.color = new Color(1f, 0.45f, 0.35f);
            StatusLabel.style.backgroundColor = new Color(0.08f, 0.04f, 0.02f, 0.92f);
            StatusLabel.style.paddingLeft = 12;
            StatusLabel.style.paddingRight = 12;
            StatusLabel.style.paddingTop = 7;
            StatusLabel.style.paddingBottom = 7;
            root.Add(StatusLabel);

            ResultOverlay = Overlay();
            VisualElement resultCard = Panel();
            resultCard.style.width = 410;
            resultCard.style.paddingLeft = 28;
            resultCard.style.paddingRight = 28;
            resultCard.style.paddingTop = 24;
            resultCard.style.paddingBottom = 24;
            resultCard.style.alignItems = Align.Center;
            ResultTitle = LabelText("胜利");
            ResultTitle.style.fontSize = 52;
            ResultTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            ResultSummary = LabelText(string.Empty);
            ResultSummary.style.fontSize = 24;
            ResultSummary.style.marginTop = 12;
            ResultSummary.style.marginBottom = 18;
            resultCard.Add(ResultTitle);
            resultCard.Add(ResultSummary);
            resultCard.Add(ButtonText("重试相同布局", () => Coordinator.RetrySameLayout()));
            resultCard.Add(ButtonText("新布局", () => Coordinator.CreateNewLayout()));
            resultCard.Add(ButtonText("返回场景选择", () => Coordinator.ReturnToMapSelect()));
            ResultOverlay.Add(resultCard);
            root.Add(ResultOverlay);

            ConfirmOverlay = Overlay();
            VisualElement confirmCard = Panel();
            confirmCard.style.width = 380;
            confirmCard.style.paddingLeft = 24;
            confirmCard.style.paddingRight = 24;
            confirmCard.style.paddingTop = 22;
            confirmCard.style.paddingBottom = 22;
            confirmCard.Add(LabelText("退出将立即判定本局失败，确定退出吗？"));
            Button confirm = ButtonText("确定退出", () =>
            {
                ConfirmOverlay.style.display = DisplayStyle.None;
                Coordinator.ForfeitBattle();
            });
            Button cancel = ButtonText("继续战斗", () => ConfirmOverlay.style.display = DisplayStyle.None);
            confirmCard.Add(confirm);
            confirmCard.Add(cancel);
            ConfirmOverlay.Add(confirmCard);
            root.Add(ConfirmOverlay);
            ResultOverlay.style.display = DisplayStyle.None;
            ConfirmOverlay.style.display = DisplayStyle.None;
        }

        private static Font CreateChineseFont()
        {
            string[] families =
            {
                "Microsoft YaHei UI",
                "Microsoft YaHei",
                "SimHei",
                "Arial Unicode MS"
            };
            foreach (string family in families)
            {
                Font font = Font.CreateDynamicFontFromOSFont(family, 32);
                if (font != null && font.HasCharacter('墙') && font.HasCharacter('法')) return font;
                if (font != null) Destroy(font);
            }
            return Font.CreateDynamicFontFromOSFont("Arial", 32);
        }

        private static VisualElement Panel()
        {
            VisualElement element = new();
            element.pickingMode = PickingMode.Position;
            element.style.backgroundColor = new Color(0.13f, 0.07f, 0.035f, 0.94f);
            element.style.borderTopLeftRadius = 9;
            element.style.borderTopRightRadius = 9;
            element.style.borderBottomLeftRadius = 9;
            element.style.borderBottomRightRadius = 9;
            element.style.borderTopWidth = 1;
            element.style.borderRightWidth = 1;
            element.style.borderBottomWidth = 1;
            element.style.borderLeftWidth = 1;
            element.style.borderTopColor = new Color(0.88f, 0.55f, 0.18f, 0.85f);
            element.style.borderRightColor = new Color(0.88f, 0.55f, 0.18f, 0.85f);
            element.style.borderBottomColor = new Color(0.88f, 0.55f, 0.18f, 0.85f);
            element.style.borderLeftColor = new Color(0.88f, 0.55f, 0.18f, 0.85f);
            return element;
        }

        private static VisualElement Overlay()
        {
            VisualElement overlay = new();
            overlay.pickingMode = PickingMode.Position;
            overlay.style.position = Position.Absolute;
            overlay.style.left = 0;
            overlay.style.right = 0;
            overlay.style.top = 0;
            overlay.style.bottom = 0;
            overlay.style.justifyContent = Justify.Center;
            overlay.style.alignItems = Align.Center;
            overlay.style.backgroundColor = new Color(0.02f, 0.015f, 0.01f, 0.72f);
            return overlay;
        }

        private static Label LabelText(string value)
        {
            Label label = new(value);
            label.style.color = Color.white;
            label.style.fontSize = 22;
            label.style.whiteSpace = WhiteSpace.Normal;
            return label;
        }

        private static Button ButtonText(string value, System.Action clicked)
        {
            Button button = new(clicked) { text = value };
            button.style.height = 38;
            button.style.minWidth = 130;
            button.style.marginTop = 4;
            button.style.marginBottom = 4;
            button.style.backgroundColor = new Color(0.38f, 0.16f, 0.055f, 1f);
            button.style.color = Color.white;
            button.style.fontSize = 22;
            button.style.unityFontStyleAndWeight = FontStyle.Bold;
            return button;
        }

        private static Button Card(string value, System.Action clicked)
        {
            Button button = ButtonText(value, clicked);
            button.style.width = 138;
            button.style.height = 108;
            button.style.marginRight = 8;
            return button;
        }

        private static Button Strategy(string value, System.Action clicked)
        {
            Button button = ButtonText(value, clicked);
            button.style.minWidth = 122;
            button.style.marginRight = 6;
            return button;
        }

        private void OnDestroy()
        {
            if (Coordinator != null) Coordinator.StateChanged -= Refresh;
            if (Placement != null) Placement.StateChanged -= Refresh;
            if (RuntimeFont != null) Destroy(RuntimeFont);
        }
    }
}
