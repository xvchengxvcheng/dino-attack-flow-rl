# Dino Attack 地图三“防御战场”实施计划

**日期：** 2026-09-04  
**对应阶段：** 阶段 8 / `TASK-8.11-MAP3`  
**状态：** 进行中（核心实现与字体修正复验完成；矩形用户确认和剩余回归待完成）
**实施工作树：** `dino-attack/.worktrees/ui-enhancement`（分支 `codex/ui-enhancement`）  
**批准规格：** `docs/superpowers/specs/2026-09-04-dino-attack-defense-battlefield-design.md`

## 目标

在独立 `DefenseBattlefield.unity` 场景中实现玩家自由放置3墙、5弓箭手和3法师，并在布防完成后原地启动选定的 PPO/FPO/ReinFlow 地图二模型进攻。地图三必须保持地图二的房屋、五区、50初始肉、战斗规则、摄像机和 v2 观测/动作合同，同时提供防守方胜负、专用 HUD、退出、原布局锁定重试和新布局流程。

## 2026-09-05 实施状态

- [x] 新场景、MapSelect入口、Build Settings、3/5/3自由布防、墙附带墙弓、右键返还、同场景PPO启动、防守方退出失败、结果页与重试上下文均已实现。
- [x] PPO复用地图二正式六流v2模型；FPO/ReinFlow缺模型时可见禁用。
- [x] 聚焦 EditMode `8/8`、聚焦 PlayMode `2/2`、Windows x86_64 Player构建通过；地图二场景hash保持不变。
- [x] 实际 Player 已验证现有首场景→结算→MapSelect→防御战场路径，并发现中文缺字/字号过小问题。
- [x] 已加入 Windows 中文动态字体、放大HUD/卡片/策略/开始/结算字号及对应最小字号断言。
- [x] 在集成工作树重跑字体修正后的聚焦 EditMode、PlayMode、Player构建与 `1426×746` 人工截图流程。
- [ ] 用户确认精确矩形 `X[-24,20] / Z[-49,-11]` 的实际画面。
- [ ] 全量 EditMode 的7个既有失败另行处理或正式豁免后再关闭最终回归门。

执行证据与限制见 `reports/ui-enhancement/map3-validation.md` 和 `reports/ui-enhancement/map3-manifest.json`。下方原始任务清单保留为设计审计记录；本节为当前真实完成状态。

## 实施原则

- 测试先行；每项功能先产生可解释的 RED，再完成最小 GREEN。
- 场景与序列化资产只通过 Unity Editor builder/Unity CLI 创建和保存，不手改 YAML。
- `DefensePlacementController` 拥有世界交互和库存；UI不实例化对象；`EnemyAIController`不接收玩家输入。
- 地图一/二现有 Human、InferenceAI、结算与训练行为是硬回归边界。
- 不修改 `Packages`、算法仓库、训练配置、checkpoint、现有 Player 或正在运行的训练进程。
- 工作树当前已有 ProjectSettings/UserSettings 自动改动；实施前逐项复核，禁止覆盖、还原或混入无关变更。
- FPO/ReinFlow 没有兼容模型时必须保持可见禁用；可交付功能不依赖伪模型。
- 地图三不加入现有地图1/2训练采样。

## Task 0：冻结基线与实施边界

**Files:**

- Read: `docs/PROJECT_PLAN.md`（外层工作区）
- Read: `reports/ui-enhancement/validation.md`
- Create: `reports/ui-enhancement/map3-baseline.md`

- [ ] 记录当前分支、HEAD、dirty/untracked 文件和 Unity 版本。
- [ ] 确认正在运行的训练 PID/命令行，仅只读记录，不暂停或终止。
- [ ] 运行当前 UI 聚焦 EditMode `17/17`、PlayMode `4/4` 基线；若基线失败，先记录为既有失败，不归因于地图三。
- [ ] 记录 `MapSelect.unity`、`LayeredBattlefield.unity`、核心 UXML/USS 和推理配置的 SHA-256，作为防止无关覆盖的审计基线。

**退出条件：** 基线证据可区分本任务回归与既有/用户改动，训练未受影响。

## Task 1：建立地图三纯逻辑合同

**Files:**

- Modify: `Assets/LlamAcademy/Dinos/Session/Core/GameMapId.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/Core/DefensePlacementKind.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/Core/DefenseInventoryState.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/Core/DefensePlacementToken.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/Core/DefenseLayoutSnapshot.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/Core/DefenseBattlePhase.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/Core/DefenseBattleOutcomeRules.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/Core/DinoAttack.DefenseBattle.Core.asmdef`
- Modify: `Assets/Tests/Editor/DinoAttack.Training.Core.Tests.asmdef`
- Create: `Assets/Tests/Editor/DefenseBattleCoreTests.cs`
- Modify: `Assets/Tests/Editor/GameLaunchContextTests.cs`

- [ ] **RED：地图 ID 与库存合同。** 测试 `defense_battlefield / DefenseBattlefield / 防御战场` 可解析；库存固定3墙/5弓/3法，不能欠账或超量返还。
- [ ] **RED：稳定 token。** 放置、移除、重新放置保持 token 身份；墙 token 与墙上弓箭手为一个原子组合。
- [ ] **RED：开始门。** 只有三类库存全部为0且策略可用/已选时才 ready；格式化缺失提示精确覆盖单位和策略。
- [ ] **RED：不可变快照。** 快照必须恰含3墙、5弓、3法，位置/旋转有限，map/seed/config匹配；canonical text/hash 对同一输入稳定。
- [ ] **RED：防守方结果投影。** 恐龙侧胜利映射玩家失败；超时/资源耗尽映射玩家胜利；战斗认输映射玩家失败。
- [ ] 实现最小纯 C# 类型，不依赖 GameObject 或场景对象。
- [ ] 运行聚焦 EditMode tests 并做 public API/序列化风险审查。

**退出条件：** 地图三值对象、库存、阶段、快照与胜负规则完全由纯测试覆盖；地图一/二 `GameMapId` 回归通过。

## Task 2：建立玩家防御对象生成与注册边界

**Files:**

- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/DefenseBattlefieldConfig.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/DefenseSpawnService.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/PlacedDefenseHandle.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/DefenseSetupActivationGate.cs`
- Modify: `Assets/LlamAcademy/Dinos/Enemy/EnemyAIController.cs`
- Create: `Assets/Tests/PlayMode/DefenseSpawnServicePlayModeTests.cs`

- [ ] **RED：生成数量与类型。** 生成服务按 token 创建正确 Prefab，3墙各带1墙弓，5弓/3法类型无误，旋转符合设计。
- [ ] **RED：注册链。** 玩家生成对象通过显式事件注册到 `EnemyAIController`，再触发既有 `OnSpawnWall/OnSpawnDefender`，使 `RoundManager`、观察构建器和奖励链看到同一对象。
- [ ] **RED：Setup移除。** 移除时解除死亡/受伤订阅并从注册集合清除，不触发战斗死亡奖励；墙与墙弓一起释放。
- [ ] **RED：待机门。** Setup中守卫保留待机表现，但寻路、巡逻、索敌和攻击不运行；Running时一次性激活，重复激活无副作用。
- [ ] 从现有 `SessionDefenseLayoutController`/`SessionGroundDefenseController` 复用 Prefab配置、墙弓攻击半径、metadata 和事件语义。只抽取真正共享的窄生成逻辑，地图二仍走原入口。
- [ ] 给 `EnemyAIController` 增加地图三对象注册/取消注册接口或订阅边界；不得加入鼠标输入或地图三 UI 依赖。
- [ ] 验证生成/移除/场景卸载的订阅和对象清理。

**退出条件：** 地图三防御对象与地图二使用相同战斗和观测事件链，Setup编辑不产生奖励或幽灵注册。

## Task 3：实现放置规则、预览和编辑控制器

**Files:**

- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/DefensePlacementValidator.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/DefensePlacementPreview.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/DefenseAreaPresenter.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/DefensePlacementController.cs`
- Create: `Assets/Tests/Editor/DefensePlacementRuleTests.cs`
- Create: `Assets/Tests/PlayMode/DefensePlacementControllerPlayModeTests.cs`

- [ ] **RED：固定矩形。** 对象完整占地在矩形内才合法；矩形与每套地图二 Z1–Z5 预设不相交。
- [ ] **RED：占用与 NavMesh。** 房屋、目标、活动五区、已放置防御实际重叠均拒绝；不增加额外间距；地面守卫必须采样回有效 NavMesh。
- [ ] **RED：输入边界。** UI点击不穿透；左键只在合法位置提交；Esc取消；右键删除并返还；锁定布局后所有编辑无效。
- [ ] **RED：连续放置。** 成功后保持当前类型，库存归零自动取消；失败不扣库存、不更换 token。
- [ ] 预览对象不注册、不运行 AI/NavMeshAgent/攻击；用 MaterialPropertyBlock 或隔离材质显示合法蓝/非法红。
- [ ] 防御区半透明填充和边框在 Editing/Locked setup 可见，Running隐藏。
- [ ] 放置检测按鼠标位置变化/点击刷新，复用 NonAlloc buffer，避免每帧 GC。
- [ ] 中键拖动、边缘/键盘平移、滚轮缩放与左键放置/右键移除做输入冲突测试。

**退出条件：** 玩家可在无 UI 穿透和无库存漂移的情况下完成、修改并冻结一个合法3/5/3布局。

## Task 4：支持不重载场景的 AI 准备与启动

**Files:**

- Modify: `Assets/LlamAcademy/Dinos/Inference/DinoInferenceController.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/DefenseAiStrategyCatalog.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/DefenseBattleCoordinator.cs`
- Modify: `Assets/LlamAcademy/Dinos/Player/DinoSpawner.cs`（仅在缺少现有窄输入门时）
- Create: `Assets/Tests/Editor/DefenseAiStrategyCatalogTests.cs`
- Create: `Assets/Tests/PlayMode/DefenseBattleCoordinatorPlayModeTests.cs`
- Modify: `Assets/Tests/PlayMode/DinoUiEnhancementPlayModeTests.cs`

- [ ] **RED：三策略可用性。** PPO/FPO/ReinFlow 独立验证自身 config/model/hash/protocol/shape；缺失项只禁用自身，不交叉复用模型。
- [ ] **RED：原地准备。** 从 Human地图三启动请求进入场景后，布防期间 `DinoSpawner` 玩家输入关闭且没有推理；点击开始能在 scene handle 不变时创建 runner、准备 v2 观测和25步调度。
- [ ] 将 `DinoInferenceController.TryConfigure` 拆为“准备推理”和“可选启动回合”的兼容接口；地图一/二旧 bootstrap 继续自动开始，地图三由协调器控制启动时点。
- [ ] 协调器按原子顺序执行：冻结编辑 → 创建/验证 runner → 准备 inference → 激活守卫 → `RoundManager.StartRound()`。重复点击不得产生第二 runner 或第二次开始。
- [ ] **RED：加载失败回滚。** runner创建/合同匹配失败时释放临时资源、保留布局、回到 Editing或LockedReplaySetup、取消策略并显示失败，不进入 Running或生成结算。
- [ ] 验证真实六流中 Walls为3、Guards为11、Houses为8、Regions为5，动作仍经现有 codec 与 `DinoSpawner.TryDeploy`。
- [ ] 验证确定性推理、Wait、合法部署、非法部署、资源不足和25步间隔保持地图二语义。

**退出条件：** 地图三可从同一场景 Setup 安全切换到选中模型的 Running；地图一/二旧推理测试全绿。

## Task 5：实现布防 UI、战斗 HUD、退出和地图三结算

**Files:**

- Create: `Assets/LlamAcademy/Dinos/UI/DefenseBattleUI.uxml`
- Create: `Assets/LlamAcademy/Dinos/UI/DefenseBattleStyles.uss`
- Create: `Assets/LlamAcademy/Dinos/UI/DefenseBattleUI.cs`
- Modify: `Assets/LlamAcademy/Dinos/RoundManagement/RoundManager.Forfeit.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/Core/DefenseBattleResultSnapshot.cs`
- Create: `Assets/Tests/Editor/DefenseBattleUIContractTests.cs`
- Create: `Assets/Tests/PlayMode/DefenseBattleUIPlayModeTests.cs`

- [ ] **RED：底部工具栏。** 三卡片、剩余数量、选中态、库存归零禁用、三策略并列、不可用置灰、开始门和缺失提示均可由状态驱动。
- [ ] **RED：锁定重试。** 三卡片保留但禁用，显示“原布局已锁定”；策略默认未选择，只有选策略和开始可操作。
- [ ] **RED：HUD。** Running显示剩余时间、策略、AI肉、恐龙、地面守卫、墙；Setup/Ended不显示错误阶段内容。
- [ ] **RED：结算。** 只显示防守方 Victory/Defeat、策略和坚持时间；文字对比度与结果遮罩不被底部卡片遮挡。
- [ ] **RED：退出。** Setup退出直接去 MapSelect且不结算；Running退出显示确认，取消继续，确认调用防守方权威失败入口。
- [ ] 给 `RoundManager` 增加明确的防守方认输入口/outcome reason；保留现有 `ForfeitSession()` 的地图一/二语义。
- [ ] UI事件在 enable/disable、场景卸载和重复打开确认框时成对注册/注销；键盘/控制器 submit 和取消可用。
- [ ] 在目标横纵比分辨率做至少一次视觉截图检查，避免重现结算文字与单位卡片重叠。

**退出条件：** 地图三全部约定 UI 状态和防守方结果正确；现有 UI enhancement 的退出/结算回归不变。

## Task 6：实现原布局锁定重试、新布局与上下文清理

**Files:**

- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/DefenseBattleReplayContext.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/Core/DefenseBattleResultActionRouter.cs`
- Modify: `Assets/LlamAcademy/Dinos/UI/DefenseBattleUI.cs`
- Modify: `Assets/LlamAcademy/Dinos/RoundManagement/GameSessionRestartService.cs`（仅增加所需的窄调用，不改变现有路由）
- Create: `Assets/Tests/Editor/DefenseBattleResultActionRouterTests.cs`
- Create: `Assets/Tests/PlayMode/DefenseBattleReplayPlayModeTests.cs`

- [ ] **RED：原布局重试路由。** 同 scene/seed/config/hash发布一次性快照并重载；重建满生命房屋/墙/守卫，canonical布局签名完全一致，阶段为LockedReplaySetup。
- [ ] **RED：策略重置。** 重试后无默认策略，必须再次明确选择；防御编辑输入全部锁定。
- [ ] **RED：新布局。** 使用 `GameLaunchContext.DeriveNextLayoutSeed` 得到不同 seed，清除快照并进入 EditingDefense，库存恢复3/5/3。
- [ ] **RED：返回地图。** 清除 replay context并加载MapSelect；布防阶段退出走同一清理规则且不产生结果。
- [ ] replay context只保存值，不保存Unity对象；map/seed/config/version不匹配时fail closed并返回安全菜单。
- [ ] 调度失败时回滚待消费上下文；成功消费、场景不匹配、SubsystemRegistration和返回菜单时清理。
- [ ] 覆盖重复按钮、重载中输入和旧上下文串局。

**退出条件：** 三个结算动作在完整场景重载下具有确定且无串局的语义。

## Task 7：创建并接线独立地图三场景

**Files:**

- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/Editor/DefenseBattlefieldSceneBuilder.cs`
- Create: `Assets/LlamAcademy/Dinos/DefenseBattle/Editor/DefenseBattlefieldValidator.cs`
- Create: `Assets/LlamAcademy/Dinos/Map/Adapters/DefenseBattlefieldLayoutSignatureProvider.cs`
- Create: `Assets/LlamAcademy/Dinos/Scenes/DefenseBattlefield.unity`（Unity Editor生成）
- Modify: `Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity`（Unity Editor生成）
- Modify: `Assets/LlamAcademy/Dinos/UI/MapSelectUI.cs`
- Modify: `ProjectSettings/EditorBuildSettings.asset`（Unity Editor生成；先审查现有用户改动）
- Create: `Assets/Tests/Editor/DefenseBattlefieldSceneTests.cs`
- Create: `Assets/Tests/PlayMode/DefenseBattlefieldPlayModeTests.cs`

- [x] **RED：scene contract。** Map3 stable ID、scene、MapSelect第三按钮、Build Settings入口缺失时测试失败。
- [x] builder从 `LayeredBattlefield.unity` 复制到独立scene，保留地图二房屋/目标/五区/地貌/相机/NavMesh和共享系统。
- [x] 移除或禁用地图三自动 `SessionDefenseLayoutController`/`SessionGroundDefenseController` 生成路径，接入地图三 controller、coordinator、UI和策略配置。
- [x] 固定防御矩形对地图二全部 zone binding无交集，并记录为 `X[-24,20] / Z[-49,-11]`。
- [ ] 运行 Home/俯视截图，把矩形边界提交用户目视确认；确认前 Task 7不得勾选完成。
- [x] MapSelect显示“开阔战场 / 分层战场 / 防御战场 / 恐龙介绍”，点击防御战场直接创建 Human launch request并进入 Setup。
- [ ] validator检查恰好8房、5活动区、唯一目标、0自动防御对象、固定矩形、3/5/3配置、共享相机控制、预烘焙NavMesh和模型配置引用。
- [ ] 比较同 seed 的地图二/地图三房屋、目标、Regions签名完全一致；防御部分只在地图三由玩家生成。

**退出条件：** 独立地图三可从菜单进入并通过 scene validator；矩形获得用户视觉确认；场景保存仅包含预期变化。

## Task 8：端到端验证、回归与交付证据

**Files:**

- Create: `reports/ui-enhancement/map3-editmode.xml`
- Create: `reports/ui-enhancement/map3-playmode.xml`
- Create: `reports/ui-enhancement/map3-validation.md`
- Create: `reports/ui-enhancement/map3-manifest.json`
- Modify: `docs/superpowers/plans/2026-09-04-dino-attack-defense-battlefield.md`
- Modify: 外层 `docs/PROJECT_PLAN.md`

- [x] 运行地图三 Core/Scene/UI/EditMode 聚焦集：8/8通过且无新增编译错误（字体修正后复验仍待许可证恢复）。
- [x] 运行地图三 PlayMode主流程：进入 → 3/5/3恢复 → 选PPO → 不重载开战 → 退出失败 → 原布局锁定重试，2/2通过。
- [ ] 使用缺模型、错协议、runner创建失败各验证一次 fail-closed，布局保留且无胜负。
- [ ] 用同一 Map2模型和同一 map seed 检查 Map2/Map3房屋/Regions/目标一致；Map3六流shape和25步调度不变。
- [x] 原布局重试比较完整布局签名并确认策略重置；新布局/返回菜单由路由实现，完整自动化矩阵仍待补齐。
- [ ] 跑地图一/二现有 MapSelect、UI enhancement、推理、结算、退出、场景聚焦回归。
- [ ] 运行 `git diff --check`、审查scene YAML只读diff摘要、确认没有Packages/训练/checkpoint/算法仓库变更。
- [x] `map3-manifest.json`记录scene/model hash、Unity版本、测试结果和已知模型可用性。
- [x] 更新外层项目计划的任务状态、验证证据、产物路径、阻塞项和决策记录。

**退出条件：** 功能、异常、重试和地图一/二回归证据完整；没有把模型胜率当作功能验收。

## 建议验证命令

以下命令在实施时从 UI 工作树执行，输出路径不得覆盖既有证据：

```powershell
& 'D:\job\6000.3.22f1\Editor\Unity.exe' `
  -batchmode -quit `
  -projectPath 'C:\Users\hp\Documents\New project 2\game_project\dino-attack\.worktrees\ui-enhancement' `
  -runTests -testPlatform EditMode `
  -testFilter 'DefenseBattle' `
  -testResults 'reports\ui-enhancement\map3-editmode.xml' `
  -logFile 'reports\ui-enhancement\map3-editmode.log'

& 'D:\job\6000.3.22f1\Editor\Unity.exe' `
  -batchmode -quit `
  -projectPath 'C:\Users\hp\Documents\New project 2\game_project\dino-attack\.worktrees\ui-enhancement' `
  -runTests -testPlatform PlayMode `
  -testFilter 'DefenseBattle' `
  -testResults 'reports\ui-enhancement\map3-playmode.xml' `
  -logFile 'reports\ui-enhancement\map3-playmode.log'

& 'D:\job\6000.3.22f1\Editor\Unity.exe' `
  -batchmode -quit `
  -projectPath 'C:\Users\hp\Documents\New project 2\game_project\dino-attack\.worktrees\ui-enhancement' `
  -executeMethod LlamAcademy.Dinos.DefenseBattle.Editor.DefenseBattlefieldValidator.ValidateAssets `
  -logFile 'reports\ui-enhancement\map3-validator.log'
```

若项目已有可用 `unity` CLI 包装命令，实施时优先使用它执行同等 builder/compile/test/validator操作；报告必须记录实际命令，而不是本计划中的示例。

## 执行顺序与停点

1. Task 0–1先建立基线和纯合同。
2. Task 2–3完成可独立验证的布防系统，不接模型。
3. Task 4完成原地推理启动和失败回滚。
4. Task 5–6完成全部 UI、胜负和重载事务。
5. Task 7独占 Unity Editor创建场景；固定矩形截图必须等待用户确认。
6. Task 8进行端到端和既有地图回归，之后才可声明完成。

遇到以下情况必须暂停并询问用户：

- 固定矩形无法同时覆盖全部村庄候选且避开所有Z1–Z5预设；
- 动态城墙必须运行时重烘焙NavMesh才能避免恐龙永久无路径；
- 需要修改Packages、ProjectSettings中除Build Settings外的共享配置；
- 地图二模型无法接受玩家布局产生的合法v2观测；
- 需要改变已批准的数量、资源、时限、胜负、UI或重试语义。

## 完成定义

- “防御战场”独立场景和MapSelect入口可用。
- 房屋/目标/Z1–Z5/资源/相机/战斗与地图二一致，只有防御布局改由玩家设置。
- 3墙+3墙弓、5弓、3法自由放置、移除、库存、预览和锁定正确。
- PPO/FPO/ReinFlow真实可用性独立显示，选中模型可在同一场景原地启动；失败安全回滚。
- 防守方 HUD、胜负、退出确认和简化结算正确。
- 原布局重试、新布局和返回地图无串局，布局签名可验证。
- 地图一/二、v2协议、训练采样和正在运行的训练任务不受影响。
- Unity编译、聚焦EditMode/PlayMode和scene validator全绿，文档与manifest包含可复核证据。
