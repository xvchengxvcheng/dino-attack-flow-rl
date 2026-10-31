# Dino-Attack Unity 工程上下文

<!-- unity-onboarding:generated:start -->

分析日期：2026-09-26。只读检查配置、代码、构建场景列表及资源来源说明；未打开/保存 Unity 场景、进入 Play Mode 或运行测试。

## 工程与环境

- 已确认工程根目录：`C:\Users\hp\Documents\New project 2\game_project\dino-attack`。用户文字中的 `game\_project` 路径不存在，以当前可验证工程为准。
- HEAD：`7a0c2001ca349b146d7a74becaa31a133f7e1613`。工作区存在大量未提交代码、场景及资源，本文依据当前磁盘内容，不代表该提交的纯净状态。
- Unity：`6000.3.22f1 (1c726e1fb402)`；`D:\job\6000.3.22f1\Editor\Unity.exe` 存在，未启动验证。
- 渲染：URP 17.3.0。输入：Input System 1.20.0，`activeInputHandler: 1`；地图4直接读取 Keyboard/Mouse。
- 本次设计以本地单人桌面体验为基线；未发现已接入的第一方联机流程，最终发行平台仍待实施时确认。

| 能力 | 已确认配置/证据 |
| --- | --- |
| 行为图 | `com.unity.behavior` 1.0.13；`Dinos/Dino Graph.asset`、`Enemy AI/Enemy AI Graph.asset` |
| 导航 | `com.unity.ai.navigation` 2.0.14；NavMeshManager、NavMeshUtilities |
| 动画/相机 | Animation Rigging 1.4.1、Cinemachine 3.1.3 已声明；地图4实际使用自定义 ThirdPersonFollowCamera |
| 强化学习 | ML-Agents 本地依赖 `file:../../ml-agents/com.unity.ml-agents`；Training/ 下存在训练代码 |
| 推理 | packages-lock 解析出 AI Inference 2.6.1；Inference/ 下有 DinoSentisPolicyRunner 与模型契约 |
| UI | UI Toolkit 用于主要运行界面和地图4 HUD；地图选择/血条还使用 uGUI |
| 测试 | Unity Test Framework 1.6.0；Assets/Tests/Editor、Assets/Tests/PlayMode |

## 目录与程序集

主要代码位于 `Assets/LlamAcademy/Dinos/`：DefenseBattle 管布防与地图4玩家，Enemy/Unit/Behavior 管单位战斗，RoundManagement/Session 管会话与重启，Map/Deployment 管地图与部署，Training/Inference 管恐龙部署训练和推理，UI 管界面。

纯规则/数据程序集包括 `DinoAttack.Map.Core`、`Deployment.Core`、`Session.Core`、`DefenseBattle.Core`、`Training.Core`、`Inference.Core`。其中 `DefenseBattle.Core` 引用 `Session.Core`。另有 Map.Runtime、Map.Editor 和测试程序集。相当多 MonoBehaviour（含 DefenseBattle 运行组件及行为节点）仍在预定义 `Assembly-CSharp` 中；新 asmdef 不能直接引用这些预定义程序集类型，扩展时要保持依赖方向。

第三方角色/动作在 `Assets/ThirdParty/VRoid/SendagayaShino` 和 `Assets/ThirdParty/UnityFemaleMotion`；各自 `SOURCE.md` 记录资源来源。原 polyperfect 素材另有教育用途约束，见 README 与 Vendor 下许可。

## 场景与启动

启用构建场景依次为：MapSelect、Dinos、LayeredBattlefield、DefenseBattlefield、FirstPersonDefenseBattlefield。另有未列入正式构建的 DinoAttackTraining 场景。

地图4为 `GameMapId.FirstPersonDefenseBattlefield`，稳定 ID `first_person_defense_battlefield`，显示名“星光守卫”。场景名称和 `FirstPersonDefender` 类型保留历史命名，代码已改为第三人称。`MapSelectUI` 发起选择，`GameSessionRestartService` 负责场景切换。`DefenseBattleCoordinator` 管布防、进攻模型选择、开战、结果和同布局重试。

## 架构与约束

- MonoBehaviour 组合、ScriptableObject 配置、事件回调与若干单例并存；纯规则已逐步拆入 Core 程序集。
- `Unit.Awake` 为行为单位配置 `DinoFixedBehaviorGraphDriver`，在 FixedUpdate 手动驱动 BehaviorGraphAgent。新单位必须避免帧更新和固定步双重执行。
- `Defender` 的感知回调会直接写 `Command=Attacking`，不适合原样承载“跟随但不远追”的长期队友指令。
- 地图4玩家实现 `IDamageable`，模型为已有 VRoid Shino，使用 Humanoid、CharacterController、法杖技能、独立 HUD 和小地图。
- 正式结构化观测守卫容量固定为 11；`DinoStructuredObservationBuilder` 会拒绝超额守卫。现有玩家明确不注册进此观测集合，队友扩展也应首先保持该契约。
- 当前 PPO/FPO/PolicyFlow 推理控制恐龙部署，不是逐单位队友运动策略；ReinFlow 有选项但当前 Coordinator 标为不可用。
- 地图4默认 0.5 倍速。角色技能/移动输入硬编码，新增聊天需统一管理焦点与输入占用，尤其 Enter、Esc、Q/E/R。
- `RoundManager.PlayerWon` 表示原进攻方获胜，防守方结果需经 `DefenseBattleCoordinator.DefenderWon` 解读，不能直接套用字段名。
- 地图4场景为二进制序列化，文件搜索不能证明完整场景状态；本次场景结构判断主要依据构建器、组件、测试代码与 Build Settings。

命名沿用 `LlamAcademy.Dinos.*`，常见 `[SerializeField] private PascalCase`、Allman 花括号、事件订阅/注销；现有代码混有换行风格，后续只调整必要文件。

## 验证与工具状态

相关现有测试：FirstPersonDefenseSceneTests、FirstPersonDefensePlayModeTests、DinoCombatBehaviorPlayModeTests、NavigationReadinessPlayModeTests、DefenseMinimapPlayModeTests、GameSpeedControlPlayModeTests，以及推理模型契约测试。本次只确认测试代码存在，未宣称通过。

CodeGraph 已存在并可通过 CLI 查询；对未索引/新文件需在 CodeGraph 查询后用普通文件读取补充。当前可用工具列表未暴露 Unity MCP；manifest 未声明 Unity MCP 包，连接与编辑器运行态均未验证。仓库读取足以支持本次方案设计，无需安装工具或包。

## 主要证据文件

`ProjectSettings/ProjectVersion.txt`、`ProjectSettings/ProjectSettings.asset`、`ProjectSettings/EditorBuildSettings.asset`、`Packages/manifest.json`、`Packages/packages-lock.json`、`README.MD`；以及 `Assets/LlamAcademy/Dinos/` 下的：

- `Session/Core/GameMapId.cs`、`UI/MapSelectUI.cs`、`RoundManagement/GameSessionRestartService.cs`
- `DefenseBattle/DefenseBattleCoordinator.cs`、`FirstPersonDefender.cs`、`FirstPersonDefenseHud.cs`、`Editor/FirstPersonDefenseSceneBuilder.cs`
- `Enemy/Defender.cs`、`Enemy/EnemyGraphConstants.cs`、`Unit/Unit.cs`、`Simulation/DinoFixedBehaviorGraphDriver.cs`
- `Behavior/MoveToTargetLocationAction.cs`、`MoveToTargetObjectAction.cs`、`AttackClosestObjectAction.cs`
- `Training/DinoStructuredObservationBuilder.cs`、`Inference/DinoInferenceController.cs`

<!-- unity-onboarding:generated:end -->
