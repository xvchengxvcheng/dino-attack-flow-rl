using LlamAcademy.Dinos.RoundManagement;
using LlamAcademy.Dinos.Companion;
using UnityEngine;
using UnityEngine.UIElements;

namespace LlamAcademy.Dinos.DefenseBattle
{
    // Project world-space feedback after the follow camera (order 90) has moved.
    [DefaultExecutionOrder(95), RequireComponent(typeof(UIDocument))]
    public sealed class FirstPersonDefenseHud : MonoBehaviour
    {
        [SerializeField] private FirstPersonDefender Player;
        private VisualElement LiveRoot;
        private Label Reticle;
        private Label Instructions;
        private Label Health;
        private VisualElement DamageTint;
        private VisualElement SkillBar;
        private Button Burst, Shield, Dash;
        private DefenderAbilities Abilities;
        private DefenseMinimap Minimap;
        private UIDocument Document;
        private DefenderDamageNumbers DamageNumbers;

        private void LateUpdate()
        {
            if (Player == null) return;
            if (Document == null) Document = GetComponent<UIDocument>();
            VisualElement root = Document.rootVisualElement;
            if (root == null) return;
            if (root != LiveRoot || Reticle == null || Reticle.parent != root) Build(root);
            if (Minimap == null || Minimap.parent != root)
            {
                Minimap?.Dispose();
                var round = RoundManager.Instance;
                if (round != null)
                {
                    var placement = FindFirstObjectByType<DefensePlacementController>();
                    var deployment = FindFirstObjectByType<LlamAcademy.Dinos.Deployment.DinoDeploymentService>();
                    Minimap = new DefenseMinimap(Player, round, placement, deployment?.ConfiguredZones);
                    root.Add(Minimap);
                }
            }
            Minimap?.Refresh(root.resolvedStyle.width, root.resolvedStyle.height);
            bool running = RoundManager.Instance != null && RoundManager.Instance.State == GameState.Running;
            bool setup = RoundManager.Instance != null && RoundManager.Instance.State == GameState.Setup;
            if (DamageNumbers == null || DamageNumbers.parent != root)
            {
                DamageNumbers?.Dispose();
                DamageNumbers = new DefenderDamageNumbers(Player);
                root.Insert(0, DamageNumbers); // Keep combat numbers beneath menus, the minimap and other HUD controls.
            }
            DamageNumbers.Refresh(Camera.main, new Vector2(root.resolvedStyle.width, root.resolvedStyle.height), running);
            bool active = Player.IsControlling;
            if (Abilities == null) Abilities = Player.GetComponent<DefenderAbilities>();
            SkillBar.style.display = active ? DisplayStyle.Flex : DisplayStyle.None;
            if (Abilities != null)
            {
                SetSkill(Burst, "Q  星陨冲击", "范围伤害", Abilities.BurstCooldownRemaining, Player.BlockCombatInput);
                SetSkill(Shield, "E  星光护盾", "吸收60伤害 / 4秒", Abilities.ShieldCooldownRemaining, Player.BlockCombatInput);
                SetSkill(Dash, "R  疾风闪避", "向移动方向突进", Abilities.DashCooldownRemaining, Player.BlockCombatInput);
            }
            // Keep the aim marker visible in both regular mouse-look and the
            // panel's right-drag look mode, but hide it while text editing pauses combat.
            Reticle.style.display = active && !Player.BlockCombatInput ? DisplayStyle.Flex : DisplayStyle.None;
            Reticle.text = Time.unscaledTime < Player.HitFlashUntil ? "×" : "+";
            Reticle.style.color = Time.unscaledTime < Player.HitFlashUntil ? new Color(1f, 0.8f, 0.25f) : Color.white;
            Health.style.display = running ? DisplayStyle.Flex : DisplayStyle.None;
            Health.text = $"生命 {Player.Health} / {Player.MaxHealth}   ·   护盾 {Abilities?.ShieldRemaining ?? 0}";
            Health.style.color = Player.Health <= 30 ? new Color(1, 0.3f, 0.25f) : Color.white;
            DamageTint.style.display = active && Time.unscaledTime < Player.DamageFlashUntil ? DisplayStyle.Flex : DisplayStyle.None;
            Instructions.style.display = running || setup ? DisplayStyle.Flex : DisplayStyle.None;
            Instructions.style.bottom = setup ? 205 : 20;
            Instructions.text = setup
                ? "地图4 · 星光守卫  |  布防后操控角色保卫村庄 · 默认 0.5 倍速"
                : active ? (Player.PersistentInteractionEnabled
                    ? "WASD 移动 · Shift 奔跑 · 空格跳跃 · 左键攻击 · 右键拖动视角 · 交互面板常驻"
                    : Player.IsLookCaptured
                    ? "WASD 移动 · Shift 奔跑 · 空格跳跃 · 左键法杖 · 滚轮缩放 · Esc 释放鼠标"
                    : "鼠标已释放 · 按 Enter 返回人物控制（战斗仍在继续）")
                : Player.StatusMessage;
        }

        private void Build(VisualElement root)
        {
            LiveRoot = root;
            CompanionUiStyle.ApplyFont(root);
            DamageTint = new VisualElement { pickingMode = PickingMode.Ignore };
            DamageTint.style.position = Position.Absolute;
            DamageTint.style.left = DamageTint.style.right = DamageTint.style.top = DamageTint.style.bottom = 0;
            DamageTint.style.backgroundColor = new Color(0.7f, 0f, 0f, 0.18f);
            root.Add(DamageTint);
            Reticle = MakeLabel(root, "first-person-reticle", 34);
            Reticle.style.left = Length.Percent(50);
            Reticle.style.top = Length.Percent(50);
            Reticle.style.width = 40;
            Reticle.style.height = 40;
            Reticle.style.marginLeft = -20;
            Reticle.style.marginTop = -20;
            Reticle.style.unityTextAlign = TextAnchor.MiddleCenter;
            Health = MakeLabel(root, "first-person-health", 24);
            Health.style.left = 24;
            Health.style.bottom = 142;
            SkillBar = new VisualElement { name = "defender-skills" };
            SkillBar.style.position = Position.Absolute;
            SkillBar.style.left = 24;
            SkillBar.style.bottom = 65;
            SkillBar.style.flexDirection = FlexDirection.Row;
            root.Add(SkillBar);
            Burst = MakeSkill("skill-burst", () => Abilities?.TryBurst());
            Shield = MakeSkill("skill-shield", () => Abilities?.TryShield());
            Dash = MakeSkill("skill-dash", () => Abilities?.TryDash());
            Instructions = MakeLabel(root, "first-person-instructions", 18);
            Instructions.style.left = 24;
            Instructions.style.right = 24;
            Instructions.style.backgroundColor = new Color(0.055f, 0.09f, 0.15f, 0.94f);
            Instructions.style.paddingLeft = 12;
            Instructions.style.paddingTop = Instructions.style.paddingBottom = 8;
        }

        private void OnDisable()
        {
            DamageNumbers?.Dispose();
            DamageNumbers = null;
            Minimap?.Dispose();
            Minimap = null;
        }

        private Button MakeSkill(string name, System.Action action)
        {
            var button = new Button(action) { name = name };
            button.style.width = 210;
            button.style.height = 66;
            button.style.fontSize = 19;
            button.style.whiteSpace = WhiteSpace.Normal;
            button.style.color = Color.white;
            button.style.backgroundColor = new Color(0.16f, 0.26f, 0.39f, 0.98f);
            button.style.borderBottomColor = new Color(0.56f, 0.60f, 0.77f);
            button.style.borderBottomWidth = 1;
            SkillBar.Add(button);
            return button;
        }

        private static void SetSkill(Button button, string title, string detail, float cooldown, bool inputBlocked)
        {
            button.text = title + "\n" + (cooldown > 0 ? $"冷却 {cooldown:0.0} 秒" : detail);
            button.SetEnabled(cooldown <= 0f && !inputBlocked);
        }

        private static Label MakeLabel(VisualElement root, string name, int size)
        {
            var label = new Label { name = name, pickingMode = PickingMode.Ignore };
            label.style.position = Position.Absolute;
            label.style.fontSize = size;
            label.style.color = Color.white;
            label.style.whiteSpace = WhiteSpace.Normal;
            root.Add(label);
            return label;
        }
    }
}
