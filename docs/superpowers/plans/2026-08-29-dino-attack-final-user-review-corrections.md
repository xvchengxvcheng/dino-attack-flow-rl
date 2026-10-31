# Dino Attack 最终用户复核修正实施计划

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 修正最终用户复核中确认的山体占区、房屋血条、墙上弓箭手攻击覆盖、重复 Victory 和 Restart 后重复开场问题；每个新局增加 `3 Archer + 2 Mage` 地面守卫，并在胜利时清除全部存活恐龙，同时保持五区坐标与训练协议不变。

**Architecture:** 山体只保留美术层，不再通过 Collider 或 NavMeshModifier 影响移动；房屋和守卫复用现有世界空间 HealthBar 管线；墙上弓箭手继续使用地面 NavMesh 逻辑根，仅把可视子树对齐到墙顶中心，并只对固定墙上弓箭手把索敌距离配置为 `30`。新增会话级地面守卫布局，在首次开局和 Restart 后的新局按 start seed 从村落公共可行走地面采样五个互斥位置，固定生成 `3 Archer + 2 Mage`。重启服务保存一次性“由 Restart 进入”状态，RoundManager 和 RuntimeUI 读取同一实例状态，从而在重载后自动进入 Running 并跳过开场图标与 Start；胜利进入 Ending 时统一令存活恐龙死亡。

**Tech Stack:** Unity 6000.3.22f1、C#、UI Toolkit、UGUI 世界空间 HealthBar、Unity AI Navigation、项目现有确定性 Scene Builder/Validator。

**Spec:** `docs/PROJECT_PLAN.md` 的阶段 8、决策记录与“2026-08-29：最终用户复核修正计划”条目；历史规格 `docs/superpowers/specs/2026-08-29-dino-attack-house-targeting-volume-navigation-design.md` 中的 Rim Barrier 规则由本计划明确取代。

## Global Constraints

- 两座大山分别只与 Z1、Z5 相邻；不得再写成覆盖 Z1/Z2 或 Z4/Z5。
- Z1–Z5 的 A/B/C/D 顶点、双线性 `(u,v)` 映射、部署 Trigger、地貌类型和速度倍率保持不变。
- 山体不考虑碰撞或 NavMesh 阻挡；攻击仍不使用 LOS/掩体阻挡。
- 首次进入仍显示中央 Dino Attack 图标和 Start；点击 Start 后两者消失并开始战斗。
- Restart 重载后直接进入 Running，不显示图标或 Start，也不要求再次点击。
- Victory/Defeat 只在 Ended 结果面板显示一次。
- 墙上固定弓箭手索敌距离为 `30`；地面弓箭手继续使用原 prefab 的 `15`，不得通过修改共享 prefab 把两者一起扩大。
- 每次首次开局或 Restart 后的新一局恰好生成五名地面守卫，构成为 `3 Archer + 2 Mage`；同一局内不追加生成。
- 地面守卫只能落在村落内部或外围的公共可行走地面，排除 Z1–Z5 部署区、Z3 水面、房屋、墙、中央目标和其他守卫的最小间距范围。
- 胜利进入 Ending 后，场上全部存活恐龙播放正常死亡流程；不得因此重复触发 Victory、Defeat 或返还部署肉。
- 不修改 6D action、504 observation、reward、terminal/truncated、Packages 或其他 ProjectSettings。
- 不添加修改场景前的失败契约测试，不加入提交切片；最终只做与改动面对应的聚焦检查。

---

### Task 1: 统一首次开始、结算和 Restart 后自动开始

**Files:**
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/RoundManagement/GameSessionRestartService.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/RoundManagement/RoundManager.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeUI.cs`
- Modify if styling is needed: `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeStyles.uss`

**Interfaces:**
- Produces: `GameSessionRestartService.IsRestartLaunch : bool`
- Consumes: `RoundManager.StartRound()`、`GameState.Setup/Running/Ending/Ended`
- Invariant: 初次加载与 Restart 重载必须可区分；标记只跨一次场景重载，不跨第二次普通加载。

- [x] **Step 1: 记录当前 UI 复现证据**

  在连接中的 Unity Editor 只运行一次当前场景：确认首次 Start/Logo、Ending/Ended 的 Victory 显示和 Restart 重载后的开场状态。记录状态序列和可见节点，不新增失败测试资产。

- [x] **Step 2: 为 Restart 保存一次性启动来源**

  在 `GameSessionRestartService` 中增加静态待消费标记，并在新场景实例 `Awake` 时转存到实例属性后清空：

  ```csharp
  private static bool PendingRestartLaunch;
  public bool IsRestartLaunch { get; private set; }

  private void Awake()
  {
      // 保留现有 singleton 与 seed 初始化。
      IsRestartLaunch = PendingRestartLaunch;
      PendingRestartLaunch = false;
  }
  ```

  `TryScheduleReload(...)` 只有在成功取得单次调度权后才设置 `PendingRestartLaunch = true`，重复点击不得改变状态。

- [x] **Step 3: Restart 重载后自动进入 Running**

  在 `RoundManager.Start()` 完成现有注册、资源初始化并进入 Setup 后，读取同一服务实例：

  ```csharp
  State = GameState.Setup;
  if (GameSessionRestartService.Instance != null &&
      GameSessionRestartService.Instance.IsRestartLaunch)
  {
      StartRound();
  }
  ```

  首次加载不满足该条件，仍等待用户点击 Start。

- [x] **Step 4: Start/Logo 只属于首次开场**

  删除 Logo 固定等待两秒后自动淡出的逻辑。首次加载时保留 Logo 和 Start；`HandleStartClick` 调用 `StartRound()` 后立即给 Logo 添加 `out`，而 `OnGameStateChange` 只在 Setup 显示 Start，在 Running/Ending/Ended 隐藏 Start。Restart 加载时在 UI 初始化阶段立即隐藏 Logo 和 Start，避免一帧闪现。

- [x] **Step 5: Victory/Defeat 只渲染一次**

  `GameState.Ending` 只清理部署反馈并保持结果面板隐藏；`GameState.Ended` 才设置一次结果文字、显示结果面板、填充摘要并启用 Restart：

  ```csharp
  RoundEndText.text = RoundManager.Instance.PlayerWon
      ? "<color=#58d26f>Victory</color>"
      : "<color=#e05a4f>Defeat</color>";
  ResultPanel.style.display = DisplayStyle.Flex;
  ```

- [x] **Step 6: 聚焦验证 UI 生命周期**

  只验证两条人工路径：

  1. 首次加载 → Logo/Start 可见 → 点击 Start → 两者隐藏 → Victory/Defeat 在 Ended 仅出现一次。
  2. 点击 Restart → 重载 → Logo/Start 从首帧起隐藏 → 状态自动为 Running → 可继续部署恐龙。

---

### Task 2: 为房屋接入血条，并修正墙上弓箭手的视觉、血条与攻击覆盖

**Files:**
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Utility/HealthBarCanvas.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Unit/VillageHouse.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Enemy/Defense/WallGuardPost.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Enemy/Defense/SessionDefenseLayoutController.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Enemy/EnemyAIController.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- Scene through builder: `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`
- Reuse: `dino-attack/Assets/LlamAcademy/Dinos/Prefabs/Health Bar.prefab`

**Interfaces:**
- Changes: `HealthBarCanvas.Register(HealthBar, IDamageable)` and `Unregister(IDamageable, bool)`
- Produces: `HealthBarCanvas.SetFollowTarget(IDamageable, Transform)`，未设置时仍跟随 `IDamageable.Transform`
- Produces: each `VillageHouse` has exactly one configured `HealthBar`
- Invariant: archer gameplay root/NavMeshAgent remains at the ground anchor; only renderer-bearing visual children move to the wall top；墙上固定弓箭手索敌距离为 `30`，地面弓箭手保持原始 `15`。

- [x] **Step 1: 泛化 HealthBarCanvas 的目标类型**

  将字典键从 `Unit.Unit` 改为 `IDamageable`，死亡处理直接使用事件参数，不再通过 Transform 反查 Unit：

  ```csharp
  private readonly Dictionary<IDamageable, HealthBar> HealthBars = new();

  private void HandleDamageableDeath(IDamageable damageable)
  {
      damageable.OnDeath -= HandleDamageableDeath;
      // 按 HealthBar.OnDeathBehavior 销毁或隐藏对应条目。
  }
  ```

  现有 `Unit.Unit` 实现 `IDamageable`，调用点无需建立第二套画布。

- [x] **Step 2: VillageHouse 驱动血条进度与生命周期**

  给 `VillageHouse` 增加序列化 `HealthBar` 引用；初始化时设为满值并注册，受伤后调用：

  ```csharp
  HealthBar.SetProgress(MaxHealth > 0 ? (float)Health / MaxHealth : 0f);
  ```

  房屋死亡时由统一画布按 `Disable` 行为隐藏血条；房屋占地与 3–5 根木梁规则保持不变。

- [x] **Step 3: Builder 为八栋房屋创建同款血条**

  从 `Health Bar.prefab` 为每个 gameplay house 实例化一个唯一子对象，设置能越过最高 Tier 屋顶的 `FollowOffset`，并写入 `VillageHouse.HealthBar`。重复运行 builder 必须复用同名对象，不产生第二条血条。

- [x] **Step 4: 弓箭手视觉同时对齐墙顶高度和水平中心**

  `WallGuardPost.TryPlaceGuardVisualOnWallTop()` 创建 `GuardVisualElevation` 后，用墙体 renderer bounds 中心计算世界位移：

  ```csharp
  Vector3 desired = new(
      wallBounds.center.x,
      wallBounds.max.y + 0.35f,
      wallBounds.center.z);
  Vector3 current = new(
      guardBounds.center.x,
      guardBounds.min.y,
      guardBounds.center.z);
  visualContainer.position += desired - current;
  ```

  `LateUpdate` 继续把隐藏的 gameplay 根锁定/warp 到地面 Anchor，避免破坏 NavMesh 与攻击行为树。

- [x] **Step 5: 墙上固定弓箭手使用 30 米索敌距离**

  在 `SessionDefenseLayoutController` 生成固定守卫后，只对该实例的 `AttackRadius` 配置 `30`，不得修改 Archer prefab 的共享 `SphereCollider.radius=15`。`EnemyAIController.HandleWallDamage` 不再向 `SessionDefenseLayout.ActiveGuards` 下达 `CommandedMove`，避免固定墙位与移动命令冲突；墙上弓箭手仍通过现有 `NearbyAttackables → Attacking → AttackClosestObjectAction` 攻击进入 30 米范围的霸王龙或其他恐龙，不增加 LOS 阻挡。

- [x] **Step 6: 血条跟随可见头顶而非隐藏逻辑根**

  当前根因是 `HealthBarCanvas.Update()` 固定使用 `unit.Transform.position + FollowOffset`，而 `WallGuardPost` 只抬高 renderer-bearing 子树，导致墙上弓箭手血条继续跟随地面 gameplay 根。将画布条目扩展为可选 follow target；默认值仍为 `IDamageable.Transform`，不改变地面守卫、恐龙和墙体行为。

  `WallGuardPost.TryPlaceGuardVisualOnWallTop()` 完成可视 bounds 对齐后，在 `GuardVisualElevation` 下创建或复用 `HealthBarAnchor`，将其世界位置设置为可见 renderer bounds 的 `center.x / max.y + 0.5 / center.z`，再调用：

  ```csharp
  HealthBarCanvas.Instance.SetFollowTarget(BoundGuard, healthBarAnchor);
  ```

  墙上血条直接跟随该锚点，不再叠加 prefab 的 `(0,3,0)` 地面角色偏移；动画造成的 bounds 小幅变化不要求逐帧重算。守卫死亡、墙死联动或对象销毁时，由现有 `Unregister` 一并清理映射。地面守卫继续跟随 gameplay 根并使用 prefab 默认头顶偏移。

- [x] **Step 7: Validator 增加最小接线检查**

  八栋房屋必须各有一个有效 HealthBar 引用；三个存活墙位守卫的可视 bounds 必须位于对应墙顶以上并接近墙体 XZ 中心，血条位于可见头顶，运行时索敌球半径为 `30`。只检查接线和几何结果，不增加独立大型测试套件。

- [x] **Step 8: 聚焦运行时检查**

  进入 Play Mode 后分别伤害一栋 Tier 1 与 Tier 4 房屋，确认血条按比例下降、死亡后隐藏且木梁/占地仍正确；检查三个墙位的弓箭手可见、血条在可见头顶、逻辑根仍在 NavMesh、墙死后守卫仍同步死亡，并确认 15–30 米区间内的霸王龙能进入目标列表并受到墙上弓箭手攻击。

---

### Task 3: 每个新局生成 3 名地面弓箭手和 2 名地面法师，并在胜利时清除恐龙

**Files:**
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Enemy/Defense/SessionGroundDefenseController.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Enemy/EnemyAIController.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/RoundManagement/RoundManager.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- Scene through builder: `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`

**Interfaces:**
- Produces: `SessionGroundDefenseController.ActiveGroundGuards : IReadOnlyList<Defender>`
- Consumes: session start seed、Archer prefab、Mage prefab、敌方 NavMesh、Z1–Z5 与村落占用体积。
- Invariant: 每个场景会话只初始化一次，固定构成为三个 Archer 和两个 Mage；Restart 完整重载后重新采样，局内不补员。

- [x] **Step 1: 建立确定性的村落公共地面采样器**

  使用当前 start seed 在村落内部/外围的配置边界内做有界随机采样，再用敌方 `NavMesh.SamplePosition` 落地。逐点拒绝：任一 Z1–Z5 部署四边形、Z3 水面、房屋/废墟占地、活动墙体、中央目标和已选守卫最小间距。设置有限尝试次数；无法得到五点时输出明确错误并中止该局布局，不静默生成到禁区。

- [x] **Step 2: 固定生成 3 Archer + 2 Mage**

  将三个 Archer 与两个 Mage 组成固定列表，再使用同一 start seed 打乱“兵种—位置”对应关系；实例统一注册到 `EnemyAIController` 的现有 Defender 列表、死亡事件和 HealthBar 管线。地面 Archer 保持 prefab 原始 15 米索敌范围，Mage 保持自身既有攻击参数。

- [x] **Step 3: 首次开局与 Restart 新局各初始化一次**

  地面守卫随场景会话初始化，因此首次进入 Setup 时已经存在；Restart 重载产生新的会话并重新初始化五人。Restart 自动进入 Running 只改变开场 UI/状态，不额外调用第二次生成；同局内守卫死亡后不补充。

- [x] **Step 4: 胜利时让全部存活恐龙进入正常死亡流程**

  `RoundManager.BeginEnding(true)` 取得 `ActiveDinos` 快照并令仍存活对象死亡；先把状态锁定为 Ending，再处理死亡事件，防止清理过程重复结算。胜利清理不计作玩家主动击杀、不返还部署肉；现有 2.5 秒 Ending 窗口用于播放死亡动画，Ended 仍只显示一次 Victory。

- [x] **Step 5: 聚焦运行时检查**

  首次开局和一次 Restart 后分别检查：地面守卫总数均为 5、构成为 `3 Archer + 2 Mage`、位置全在允许公共地面且两两满足最小间距；墙上三个 Archer 另计。触发胜利后确认存活恐龙全部进入死亡流程、Victory 仅出现一次且肉资源不因清理增加。

---

### Task 4: 缩小并后移 Z1/Z5 两侧山体，取消全部 Rim 移动阻挡

**Files:**
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- Scene through builder: `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`

**Interfaces:**
- Preserves: `CanonicalZoneSpecs` 的全部 Z1–Z5 顶点与区域内 `(u,v)` 换算。
- Removes: `/World/Open Tropical Battlefield/Art Pass/Canyon Rim/Navigation Barriers` 下的九个阻挡子对象。
- Invariant: 外岛边界可继续限制相机/世界范围，但 Rim 模型本身没有 Collider、NavMeshObstacle 或启用的 NavMeshModifier。

- [x] **Step 1: 调整两座大山 Transform**

  将西北山体改为 `position=(-39,-2.7,-69)`、`scale=(0.61,0.44,0.61)`；将东北山体改为 `position=(33,-2.7,-69)`、`scale=(0.58,0.40,0.61)`。这两座山分别只位于 Z1、Z5 后方，并与两区最北边界保留可见间隙。

- [x] **Step 2: 清空 Navigation Barriers**

  Builder 保留空的 `Navigation Barriers` 身份根以兼容层级定位，但删除其全部子对象；不再创建 BoxCollider 或 NavMeshModifier，也不因视觉山体重新烘焙 Not Walkable 区域。

- [x] **Step 3: 更新 canonical preflight 与 validator**

  canonical 条件改为 Barrier 根存在且 `childCount == 0`。Validator 报告 `NavigationBarrierCount == 0`，并确认 Canyon Rim 所有 renderer 所在视觉树没有启用的非 Trigger Collider、NavMeshObstacle 或 NavMeshModifier。

- [x] **Step 4: 通过连接中的 Unity Editor 重建场景**

  退出 Play Mode后调用确定性 builder 一次，保存 `Dinos.unity`；不得手改 scene YAML。重建后确认 Z1/Z5 四边形对象和顶点序列未改变。

- [x] **Step 5: 聚焦地图检查**

  从 Home 视角确认两座山不覆盖 Z1/Z5 的可部署色块；在 Z1、Z5 的 A/B/C/D 附近各放置一次恐龙，确认部署允许且恐龙不会被山体 Collider 或 NavMesh 切口影响。

---

### Task 5: 合并验证、截图与主计划回写

**Files:**
- Modify: `docs/PROJECT_PLAN.md`
- Modify: `docs/superpowers/plans/2026-08-29-dino-attack-final-user-review-corrections.md`
- Create: `reports/phase8/final-user-review-corrections/validation.md`
- Create: `dino-attack/Assets/report/final-user-review-corrections.png`

**Interfaces:**
- Consumes: Tasks 1–4 的最终场景和代码。
- Produces: 可由用户直接复核的单张运行时截图与精简验证记录。

- [x] **Step 1: 最终编译与 Console 检查**

  只运行一次 Unity script recompile，要求 `failed=false`、compile error 0；清空或记录 cursor 后运行本轮 smoke，要求新增 Error/Exception 0。

- [x] **Step 2: 运行一次合并场景 smoke**

  同一 Play Mode 会话核对：`houses=8` 且每栋有血条；`walls=3`、`wall guards=3` 且守卫在墙顶、血条位于可见头顶、攻击半径为 30；`ground guards=5` 且为 `3 Archer + 2 Mage`；`barriers=0`；Z1/Z5 可部署；首次 Start 隐藏开场 UI；胜利时恐龙全部死亡且只显示一次结果；Restart 后直接 Running、没有 Logo/Start，并重新得到且只得到五名地面守卫。

- [x] **Step 3: 运行 validator 与 15/15 路径检查**

  Validator 必须 valid，五区/五地貌/八房/六墙位保持完整，Navigation Barrier 为 0；三种恐龙 × 五区的 15 个起点仍存在完整 NavMesh 路径。无需恢复 20-episode、Player build、四分辨率或完整 Unity 测试套件。

- [x] **Step 4: 保存用户复核截图**

  保存一张 Home 运行时截图，能同时看清 Z1/Z5 后移山体、村落房屋血条和墙上弓箭手；截图路径固定为 `Assets/report/final-user-review-corrections.png`。

- [x] **Step 5: 更新文档状态**

  在本计划勾选真实完成项；把实际命令、结果、截图和场景 hash 写入 `reports/phase8/final-user-review-corrections/validation.md` 与 `docs/PROJECT_PLAN.md`。在用户目视复核前，`TASK-8.10-ART` 和 `TASK-8.10-GAMEPLAY` 仍保持“进行中”。

---

## 明确不执行的验收

- 不添加修改场景前的失败契约测试。
- 不运行完整 EditMode/PlayMode 套件。
- 不运行 20 回合耐久测试、四分辨率矩阵、300 帧性能对比或 Windows Player build/smoke。
- 不执行独立代码复审、提交切片、自动提交、stash 或工作区清理。
- 不重新验证未受影响的 Flow RL 算法、6D/504 数学或既有地貌速度数值套件。
