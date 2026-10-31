# Dino Attack 房屋目标、体积寻路与山体阻挡实施计划

> **For agentic workers:** 实施时必须先使用 `superpowers:executing-plans`；如果用户在执行会话中再次明确要求分代理，再使用 `superpowers:subagent-driven-development`。用户已授权直接实施，ART 与 GAMEPLAY 的最终冻结合并完成。

**Goal:** 在同一 `Dinos.unity` 中加入可攻击房屋与一次性肉奖励、三类恐龙的严格目标类别优先级、真实体积感知寻路、9 段 Rim 的隐藏导航阻挡、每局六选三墙位和单局结算重启，同时保持既有 6D 动作、504 维观测、地貌速度、中央目标胜负与攻击不受遮挡的协议。

**Architecture:** 将纯确定性规则放入 `DinoAttack.Map.Core`，把需要 `Unit`、`Wall`、`Defender`、ML-Agents 或场景生命周期的组件放在默认 `Assembly-CSharp`，由现有 Behavior Graph action、`RoundManager`、训练 Agent 和场景 builder 接线。可见 Rim、房屋和木梁保持美术层；隐藏 Collider、NavMesh Modifier、生命、目标元数据和奖励属于玩法层。正式 `Dinos` 流程改为单局，历史诊断场景只保留显式兼容分支。

**Tech Stack:** Unity 6000.3.22f1、C#、NUnit EditMode/PlayMode、Behavior Graph、Unity AI Navigation/NavMesh、ML-Agents LLAPI、UI Toolkit、现有确定性场景 builder。

**Spec:** `docs/superpowers/specs/2026-08-29-dino-attack-house-targeting-volume-navigation-design.md`

## 全局约束

- 以当前已落地的 ART Transform 为接线基准；不再等待单独的 ART 视觉复核，视觉与玩法在 Task 7 一并复核和冻结。
- 不改变 6D action、504 observation、Agent ID、中央目标胜负、terminal/truncated、五区四边形及 `(u,v)`、地貌速度或攻击 LOS。
- 不修改 Packages；仅允许按真实恐龙体积窄修改 `ProjectSettings/NavMeshAreas.asset`，不得改其他 ProjectSettings。
- 不直接给 9 个复杂 Rim 模型加 MeshCollider；视觉模型继续无 Collider，阻挡使用独立、简化、隐藏的 Layer 6 体积。
- 房屋死亡不重烘 NavMesh、不开放道路；完整 Hut 隐藏，只留 `3–5` 根纯渲染暖色木梁表达仍被占用的范围。
- 正式游戏不维修、不升级、不重生墙或弓箭手；不再执行多回合资源追加、`Scoring`、`EnemyRepairs` 或自动进入下一轮。
- 不添加提交切片、暂存或提交步骤；保留两个仓库全部既有 dirty 内容。
- 采用最终精简验证：只保留目标选择 `6/6`、六选三 `5/5` 两组纯规则测试，一次合并真实场景 smoke，一次最终 Unity 编译，一次 validator（含 15/15 路径、9/9 Barrier、6/6 defender slot），一次 fresh Console 增量和一次 H0/H1/H2 冻结。房屋、布局、单局 UI 不再各建独立测试套件；外部 LLAPI、Windows Player、20-episode、四分辨率矩阵、无关完整套件和独立复审均取消。terminal/truncated 的调用顺序仍保留代码级检查，不改协议。

## 实施前门禁

- [x] 用户已授权不再等待单独 ART 视觉复核，直接以当前房屋、Rim、水体和五区 Transform 开始实施。
- [x] 完整阅读 `AGENTS.md`、`docs/PROJECT_PLAN.md`、批准规格和本计划。
- [x] 只读记录外层与 `dino-attack` 内层 `git status --short`，不清理、不覆盖、不 stash。
- [x] 确认 Unity Editor 已退出 Play Mode、场景无未保存的用户操作；保存当前 ART 场景 hash 和 `NavMeshAreas.asset` hash 作为实施前基线。
- [x] 动态检查并按项目规则核对目标代码；未创建、删除或重建 CodeGraph 索引。

---

## Task 1：建立纯规则契约与确定性选择器

**Files:**

- Create: `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/DinoTargetCategory.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/DinoTargetPriorityProfile.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/DeterministicSlotSelector.cs`
- Modify: `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldMapCoreTests.cs`

- [x] **Step 1：先写聚焦纯规则测试**

覆盖三种恐龙的类别顺序、六选三无放回、同 seed 可复现与不同 seed 可产生不同合法组合。这里不创建场景对象，不运行完整 EditMode。

- [x] **Step 2：实现最小纯规则 API**

```csharp
public enum DinoTargetCategory { House, Defender, Wall }

public static class DeterministicSlotSelector
{
    public static int[] SelectWithoutReplacement(int count, int take, uint seed);
}
```

`DinoTargetPriorityProfile` 固化：Velociraptor `House > Defender > Wall`，Pachy `Defender > House > Wall`，T-Rex `Wall > Defender > House`。

- [x] **Step 3：运行聚焦 core 测试**

只运行 `OpenTropicalBattlefieldMapCoreTests` 中新增的 targeting/slot cases，记录测试数量和结果到 `reports/phase8/house-targeting-navigation/task-1/`。

---

## Task 2：接入严格类别优先级与路径长度选择

**Files:**

- Create: `dino-attack/Assets/LlamAcademy/Dinos/Unit/DinoTargetMetadata.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Utility/DinoTargetSelector.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Behavior/SortAttackablesAction.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Config/AttackConfigSO.cs`
- Create: `dino-attack/Assets/Tests/Editor/DinoTargetSelectorTests.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Dinos/Velociraptor/Velociraptor Attack Config.asset`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Dinos/Pachycephalosaurus/Pachycephalosaur Attack Config.asset`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Dinos/TRex/TRex Attack Config.asset`

- [x] **Step 1：写一个聚焦目标选择测试**

覆盖四项：严格类别优先级高于距离；不可达目标跳过并降级；同类别选择实际 NavMesh 路径最短者；路径长度差在 `0.01m` 内时选择较小的显式 `StableId`。测试不引入 Terrain Zone、通道余量、转弯、缓存或卡住逻辑。

- [x] **Step 2：实现选择器并接入现有 Behavior Graph action**

```csharp
public sealed class DinoTargetMetadata : MonoBehaviour
{
    public DinoTargetCategory Category { get; }
    public int StableId { get; }
}

```

`DinoTargetSelector` 先按 `DinoTargetPriorityProfile` 分组；对当前最高类别的每个候选调用一次 `NavMesh.CalculatePath`，以连续 path corners 的欧氏段长之和比较。invalid、partial、空角点、NaN 或 Infinity 视为不可达；该类别全部不可达才尝试下一类别。候选缺少元数据时只允许显式历史兼容映射，无法映射就忽略并输出一次诊断。`SortAttackablesAction` 调用选择器并写回既有 `ClosestAttackable`；无合法局部目标时保持中央目标回退。不得增加路径缓存、边界采样、转弯惩罚、卡住检测或目标冷却，也不改变攻击/LOS。

- [x] **Step 3：运行聚焦测试**

运行 `DinoTargetSelectorTests` 和受影响的既有 attack action 测试；本 Task 不单独重复 Unity 编译，统一编译留到 Task 7。

---

## Task 3：实现房屋生命、一次性奖励与木梁残骸

**Files:**

- Create: `dino-attack/Assets/LlamAcademy/Dinos/Unit/VillageHouse.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Unit/HouseTier.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/RoundManagement/HouseRewardLedger.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/RoundManagement/RoundManager.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`

- [x] **Step 1：以合并 smoke 覆盖房屋状态与奖励**

按用户要求不再创建独立 `VillageHouseTests`。纯 HP 映射由代码常量检查；真实场景合并 smoke 覆盖死亡恰好一次 `+100` meat、重复伤害不重复奖励、完整 Hut 隐藏、3–5 木梁显示、目标元数据退出候选和占地 Collider 保持启用。

- [x] **Step 2：实现 `VillageHouse` 的一次性死亡转换**

房屋玩法根继续存在。死亡时只执行一次：标记 destroyed、停止接受伤害、移出目标候选、隐藏 intact visual root、显示预制 beam root；不得 `Destroy` 根对象、不得禁用 occupancy collider、不得 rebake NavMesh。

- [x] **Step 3：在 `RoundManager` 集中登记八栋房屋和奖励**

`HouseRewardLedger` 以显式稳定 House ID 去重。`RoundManager` 在 Setup 注册八栋房屋，在死亡事件中先检查 ledger 再加 `100` meat；重载依靠新场景实例自然清空。不得把奖励逻辑写进视觉 prefab，也不得让房屋直接寻找 UI。

- [x] **Step 4：builder 为每栋房屋生成/接线木梁表现**

每栋房屋固定生成 `3–5` 根暖色木梁，横倒或交叉，分布在隐藏占地的主要边缘。梁对象只允许 `Transform`、`MeshFilter`、`MeshRenderer`；禁止 Collider、Rigidbody、NavMeshObstacle、`IDamageable` 或其他玩法 MonoBehaviour。使用确定性局部坐标，不调用运行时随机数。

- [x] **Step 5：并入一次合并场景 smoke**

不再单跑房屋套件；真实八栋房屋接线、木梁数量、占地保留和奖励增量统一由 Task 7 的一次合并场景 smoke 验证。

---

## Task 4：校正三种恐龙体积并为 9 段 Rim 建立隐藏 Barrier

**Files:**

- Modify: `dino-attack/ProjectSettings/NavMeshAreas.asset`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/RoundManagement/NavMeshManager.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Deployment/DinoDeploymentService.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- Modify: `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`

- [x] **Step 1：窄修改 NavMesh agent types**

将 Velociraptor 设为 radius `0.50`、height `2.0`；Pachy/Small Dino 设为 radius `1.17`、height `2.0`；T-Rex 设为 radius `2.0`、height `5.0`。保持 agent type IDs、坡度、台阶和其他 ProjectSettings 不变；Humanoid 只有在实际 defender prefab 证明确需时才改，否则保持原值。

- [x] **Step 2：创建独立 `Canyon Rim/Navigation Barriers` 层级**

为 9 段主要 Rim 各创建一个或多个简单 Box/Capsule 组合，Layer 固定为 `Grass`（6），添加 Collider 与 `NavMeshModifier`，area override 固定为 `Not Walkable`。Barrier 不含 Renderer、Rigidbody、NavMeshObstacle、目标元数据、伤害组件或攻击阻挡逻辑；Rim 可见模型仍保持 Layer 0 纯视觉。

- [x] **Step 3：按三种体积重建静态 NavMesh**

复用 `NavMeshManager` 的现有四 Surface 结构；只在 builder/场景初始化阶段烘焙，不在房屋或墙死亡时重烘。确认每个 DinoSurface 的 agent type 与 layer mask `(1<<6)|(1<<7)` 正确，Barrier 被收集且小装饰未被收集。

部署合法性检查增加 Barrier occupancy 查询：即使点仍位于某个四边形部署区内，只要恐龙 footprint 与 Layer 6 Barrier 相交就返回既有不可部署结果；不改变 6D 动作或 `(u,v)` 换算。

- [x] **Step 4：运行场景后置导航验证**

Validator 必须检查：9/9 Barrier 身份、Layer 6、Not Walkable、无 Renderer/玩法组件；每段 Rim 的地面采样点和顶面采样点对三种恐龙均不可行走；五区公共点到中央目标仍为 15/15 完整路径。失败时只调 Barrier 几何，不改五区、村庄或攻击规则。

- [x] **Step 5：并入最终 validator**

不再另跑一套 Barrier 场景测试；最终 validator 统一记录三种半径/高度、9/9 Barrier 身份、15/15 恐龙路径和 6/6 defender slot 路径。

---

## Task 5：把正式防御布局改为六选三固定墙位

**Files:**

- Create: `dino-attack/Assets/LlamAcademy/Dinos/Enemy/Defense/WallSpawnSlot.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Enemy/Defense/WallGuardPost.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Enemy/Defense/SessionDefenseLayoutController.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Enemy/EnemyAIController.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Map/Runtime/VillageDefenseLayoutController.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`

- [x] **Step 1：以纯选择器测试和合并 smoke 覆盖六选三生命周期**

覆盖同 start seed 选择相同三个唯一 slot、仅所选 slot 生成基础满血墙、每墙恰好一个固定弓箭手、未选 slot 为空、弓箭手不巡逻不离岗、墙死后其弓箭手随之退出，以及本局无维修/升级/重生。

- [x] **Step 2：实现正式 `Dinos` 的 session layout controller**

```csharp
public bool InitializeForSession(int startSeed);
public IReadOnlyList<int> ActiveWallSlotIndices { get; }
public event Action<IReadOnlyList<int>> LayoutInitialized;
```

controller 读取 6 个显式稳定编号的 `WallSpawnSlot`，调用 Task 1 selector 后只实例化 3 组 wall/guard。初始化有单次保护；配置不是 exactly six、ID 重复或 prefab 缺失时 fail closed 并产生明确错误。

- [x] **Step 3：固定弓箭手职责与墙生命周期**

`WallGuardPost` 保存墙和弓箭手引用，将弓箭手限制在固定岗位；墙死亡时清理该弓箭手。墙只使用基础 Tier 且初始满血。禁止调用旧 repair/upgrade API；不得把固定岗位加入一般地面 defender spawn slots。

- [x] **Step 4：隔离历史预设兼容路径**

`EnemyAIController` 在正式 `Dinos` 明确使用 `SessionDefenseLayoutController`；旧 `VillageDefenseLayoutController` 只允许历史 Training/诊断场景通过显式 guard 使用，不能同时初始化两套布局。保留旧代码资产但从正式生命周期移除维修与升级调用。

- [x] **Step 5：builder 接线六个墙位并并入合并 smoke**

六个墙位使用批准的固定位置，不侵入五个部署区或中央目标。只运行 selector/layout 聚焦测试；真实墙/弓箭手数量、满血值和死亡清理统一由 Task 7 的一次合并场景 smoke 验证。

---

## Task 6：改为单局状态机、结算重启和正确的 AI terminal 顺序

**Files:**

- Modify: `dino-attack/Assets/LlamAcademy/Dinos/RoundManagement/GameState.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/RoundManagement/RoundManager.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/RoundManagement/GameSessionRestartService.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeUI.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeUI.uxml`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeStyles.uss`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Deployment/RuntimeUIStateFormatter.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Player/DinoSpawner.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingAgent.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingSceneReloader.cs`
- Modify: `dino-attack/Assets/Tests/Editor/RuntimeUIStateFormatterTests.cs`

- [x] **Step 1：收敛为代码级生命周期检查**

按用户要求不创建独立 `SingleSessionLifecycleTests`。检查正式路径为 `Setup → Running → Ending → Ended`，不进入 Scoring/EnemyRepairs/下一轮；共享 reload service 保持单次调度，terminal/truncated 仍先提交再请求重载。

- [x] **Step 2：将 `RoundManager` 收敛到单局正式路径**

移除正式 `Dinos` 的 `Round++`、回合食物追加、存活恐龙返还后继续下一轮、墙维修和升级转换。胜负事件只触发一次 Ending，展示完成后进入 Ended；Round/Wave 文本在正式 UI 中替换为单局状态，不改变中央目标胜负判定。

- [x] **Step 3：实现共享 reload service 和 Human 结算面板**

`GameSessionRestartService.TryScheduleReload(seedPolicy)` 提供单次调度保护；Human 点击后生成新 start seed 并完整重载当前场景。结算面板显示胜负、摧毁房屋数/奖励肉、存活单位等已存在且可靠的数据；按钮在第一次点击后立即 disabled。

- [x] **Step 4：修正 AI 终止与 reload 顺序**

`DinoTrainingAgent` 的顺序固定为：结算奖励 → `EndEpisode()` 或 `EpisodeInterrupted()` → 调用共享 reload service。`DinoTrainingSceneReloader` 保留至少 `0.15s` realtime 延迟和 `IsReloadPending` 单次保护。不得在 terminal 提交前销毁场景或重载。

- [x] **Step 5：并入最终编译与合并 smoke**

取消额外 formatter/lifecycle 套件和外部 LLAPI 启动；最终编译检查调用顺序，合并 smoke 检查 restart service 已接线。没有宣称完成外部 trainer 的 TerminalSteps 实测。

---

## Task 7：统一场景接线、校验器与最终精简冻结

**Files:**

- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- Modify: `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`
- Modify: `docs/PROJECT_PLAN.md`
- Create: `reports/phase8/house-targeting-navigation/validation.md`
- Create: `reports/phase8/house-targeting-navigation/manifest.json`

- [x] **Step 1：执行一次 canonical builder 集成**

统一接线八栋房屋、3–5 木梁、目标元数据、六墙位、三种导航 profile、9 段 Barrier、restart service 与 UI。Builder 第二次执行必须无额外对象、无重复订阅、无随机位置漂移；Z3 Riverbank 继续 `childCount=0`，水面上不得新增物体。

- [x] **Step 2：运行一次合并的真实场景 smoke**

真实 Play Mode smoke 覆盖一栋房屋死亡只加 `100` 且只剩 3 根木梁、占地保留、3 面墙/3 个固定弓箭手、墙死后守卫退出和 restart service 接线。类别优先级由 `6/6` 纯规则测试覆盖；Rim、15/15 与六墙位路径由同一次 validator 覆盖，不再重复逐区坐标 smoke。

- [x] **Step 3：运行最终保留验证**

实际只运行：Unity compile；目标选择 `6/6`；六选三 `5/5`；一个合并 PlayMode smoke；validator（15/15、9/9、6/6）；fresh Console 增量。未运行 LLAPI、Windows Player、完整套件、20-episode、四分辨率矩阵或独立复审。

- [x] **Step 4：生成合并 manifest 并冻结**

manifest 记录 Unity 版本、场景 hash、ART 继承冻结、房屋 HP/ID/beam 规则、目标优先级、三种 agent 体积、9 段 Barrier、六墙位与所用 seed、6D/504、聚焦测试、合并 smoke、取消项及 Console cursor。修正 canonical preflight 后 H0/H1/H2 均为 `F70CC1FFC6CE005477CD9AFCDC925C9D488515DA395016BF8DA3D2A8999FED39`。

- [ ] **Step 5：更新主计划状态**

只有所有硬门通过且用户完成最终视觉/玩法复核后，才能将 `TASK-8.10-ART` 与 `TASK-8.10-GAMEPLAY` 标记已完成、把合并 manifest 设为新冻结并恢复 `TASK-8.11`。若任何硬门失败，保持 GAMEPLAY 进行中并记录具体阻塞，不得以计划完成代替实施完成。

## 规格覆盖检查

| 规格要求 | 实施任务 | 最终证据 |
|---|---|---|
| 房屋 Tier HP、一次 `+100`、死亡后 3–5 木梁且占地保留 | Task 3 | 房屋聚焦测试、真实死亡 smoke、manifest |
| 三类恐龙严格目标类别及同类实际路径长度选择 | Task 1–2 | selector tests、合并场景 smoke |
| 真实半径/高度、避让优先级、15/15 | Task 4 | agent 配置审计、导航采样、15/15 |
| 9 段 Rim Layer 6 Not Walkable 且不阻挡攻击 | Task 4、7 | Barrier validator、attack smoke |
| 6 个墙位每局选 3，固定弓箭手，无维修升级 | Task 1、5 | layout tests、真实场景 smoke |
| 单局状态、Human 重启、AI TerminalSteps 后重载 | Task 6 | lifecycle tests、LLAPI smoke |
| 6D/504、地貌、中央目标和 Z3 空水面不变 | Task 7 | manifest、合并 smoke、scene validator |

## 完成定义

- [ ] 所有实施 Task 已完成；等待用户完成最终视觉/玩法复核后关闭 Task 7 Step 5。
- [x] 精简后的证据均写入新的 validation/manifest；取消项明确披露，不以旧冻结冒充新证据。
- [x] Unity compile、两组聚焦测试、一次合并 smoke、15/15、9/9 Barrier、6/6 defender slot、fresh Console 增量与 H0/H1/H2 均通过。
- [ ] 用户审查最终场景后明确批准；此前不恢复 `TASK-8.11`。
