# Dino Attack 第二地图“分层战场”实施计划

> **执行要求：** 实施时使用 `superpowers:executing-plans`；若采用当前会话分任务执行，则使用 `superpowers:subagent-driven-development`。每个任务完成后先审查范围与证据，再进入下一任务。

**目标：** 在不新增美术资源包、不改变 `dino_attack_structured_set_v2` shape 的前提下，新增玩法不同的 `LayeredBattlefield.unity`、地图选择、确定性中度随机布局、同 seed 的默认 AI 重赛与玩家/AI 结果对比，并在地图完成后接入双地图 50%/50% 训练选择。

**架构：** 地图规则与随机选择使用纯 C# 数据/算法层，场景只保存预烘焙的合法候选点和 NavMesh。`GameLaunchContext` 作为跨场景值对象保存地图、seed、控制方式和结算快照；Human、Sentis 推理和外部训练最终都调用同一个 4D 动作解码与 `DinoSpawner.TryDeploy`。AI 重赛完整重载同一场景并重建相同布局，不复用已销毁的场景对象。

**技术栈：** Unity 6000.3.22f1、C#、UI Toolkit、AI Navigation、ML-Agents、Unity AI Inference/Sentis、ONNX、Python/PyTorch、pytest、Unity Test Framework。

**批准规格：** `docs/superpowers/specs/2026-08-30-dino-attack-layered-battlefield-design.md`

## 全局约束

- 本计划依赖 `docs/superpowers/plans/2026-08-30-dino-attack-multimap-structured-observation-v2.md` 完成并冻结：六路输入必须为 Global `(5,)`、Regions `(5,8)`、Walls `(6,5)`、Guards `(11,7)`、Houses `(8,6)`、Dinos `(10,7)`，合计 270 标量；不得增加 map one-hot。
- UI 显示的总奖励依赖 `docs/superpowers/specs/2026-08-30-dino-attack-reward-design.md` 的共享结算实现；不得在 UI 或 AI 控制器内复制第二套奖励公式。
- 地图1仍为 `Dinos.unity`（显示名“开阔战场”）；地图2为 `LayeredBattlefield.unity`（显示名“分层战场”）；菜单为 `MapSelect.unity`。两张地图使用同一世界原点和边界 X `[-55.5,48.5]`、Z `[-53,29]`。
- 地图2完全复用地图1已经导入的模型、材质、贴图、Shader、音频和 UI 风格；不复制外部资源，不导入新美术包。
- 只允许 Unity Editor/Unity CLI/确定性 Editor builder 创建或保存 `.unity`、Prefab、NavMeshData 和序列化引用；禁止手改 scene YAML。
- 地图2不在运行时烘焙 NavMesh。房屋、墙和关键目标只从有限合法组合中选择；任意组合都必须与预烘焙导航兼容。
- 默认 AI 观看模式只运行嵌入式 ONNX 推理，不启动 Python、不采样训练数据、不更新参数；模型不可用时禁用按钮并显示原因，不允许静默回退到随机或启发式策略。
- Task 1 可用确定性初始化的正式网络结构验证 ONNX/Sentis算子与shape，但该 fixture 不得作为“默认AI策略”交付。启用按钮必须绑定一个实际训练过、由用户指定或阶段8.12产出的兼容 v2 checkpoint；当前仓库没有该产物，因此地图、菜单和安全禁用态可先完成，但 Task 6 的按钮启用与本计划完成声明必须等待 `TASK-8.12` 至少产出一个正式 v2 Gaussian PPO Set Transformer checkpoint。
- 不修改 `ml-agents/`、`fpo/`、`ReinFlow/`、`PolicyFlow/`；不覆盖 checkpoint、构建或既有报告。若 AI Inference 包未安装，需单独向用户说明 Packages 变更后再执行。
- 保留用户 dirty worktree；本计划不包含提交切片、`git add` 或 `git commit`。
- 验证只保留规格列出的必要门禁，不运行全量测试、100 局、长训练、完整性能矩阵或重复 Player 构建。

## Task 1：完成 v2/奖励前置门并验证 ONNX → Sentis 可行性

**依赖：** multimap v2 Tasks 1–5 已完成；正式奖励结算接口已完成。

**Files:**
- Create: `flow_rl/src/flow_rl/export/dino_policy_onnx.py`
- Create: `flow_rl/tests/export/test_dino_policy_onnx.py`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Inference/Core/DinoInferenceModelContract.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Inference/Core/DinoAttack.Inference.Core.asmdef`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Inference/Editor/DinoSentisFeasibilityCheck.cs`
- Create: `dino-attack/Assets/Tests/Editor/DinoInferenceModelContractTests.cs`
- Modify: `dino-attack/Assets/Tests/Editor/DinoAttack.Training.Core.Tests.asmdef`
- Create: `reports/phase8/layered-battlefield/sentis-feasibility.md`

- [x] **Step 1：记录前置协议状态**

  核对 v2 的六路 shape、4D 动作顺序、模型 metadata 和正式奖励结算已经冻结；任一项未完成则停止本计划，不通过兼容垫片掩盖旧 v1。

- [x] **Step 2：先写最小导出契约测试**

  用一个固定权重的小型 v2 Set Transformer actor 测试 ONNX 输入名、shape 和输出：

  ```python
  assert set(input_names) == {"global", "regions", "walls", "guards", "houses", "dinos"}
  assert tuple(action.shape) == (1, 4)
  assert torch.isfinite(action).all()
  assert torch.all(action.abs() <= 1.0)
  ```

  只运行 `flow_rl/tests/export/test_dino_policy_onnx.py`，确认测试先失败于缺失导出器。

- [x] **Step 3：实现可复现 ONNX 导出**

  导出器既支持确定性 feasibility fixture，也支持正式 checkpoint；两者都必须携带 v2 metadata，固定 eval 模式、输入动态 batch 维和六个稳定输入名，输出单一 `actions` `(B,4)`，并把 artifact kind、protocol/model hash 写入同目录 manifest。fixture 明确标记 `feasibility_only`，运行时默认模型配置拒绝它。

- [x] **Step 4：实现 Unity 侧契约检查与一次 Sentis 加载**

  `DinoInferenceModelContract` 放入独立 `DinoAttack.Inference.Core` assembly，只负责协议、输入名/shape、输出 shape、模型 hash 和4D有限边界检查；测试 assembly 显式引用它。Editor 检查实际加载 ONNX、用零观测执行一次推理，并记录支持/不支持的算子与错误。

- [x] **Step 5：形成硬门结论**

  运行聚焦 Python 测试、Unity 契约测试和一次 Editor 推理。只有六输入、4D 输出、有限且位于 `[-1,1]` 时继续；失败时只调整导出图或明确适配层，并更新 `sentis-feasibility.md`。

## Task 2：实现地图、seed、控制方式与结算的共享启动契约

**Files:**
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Session/Core/DinoAttack.Session.Core.asmdef`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Session/Core/GameMapId.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Session/Core/GameControllerMode.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Session/Core/GameLaunchRequest.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Session/Core/GameResultSnapshot.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Session/Core/GameLaunchContext.cs`
- Create: `dino-attack/Assets/Tests/Editor/GameLaunchContextTests.cs`
- Modify: `dino-attack/Assets/Tests/Editor/DinoAttack.Training.Core.Tests.asmdef`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/RoundManagement/GameSessionRestartService.cs`

- [x] **Step 1：写跨场景值对象测试**

  覆盖四个操作：同 seed Human、新 seed Human、同 seed InferenceAI、返回地图选择；验证上下文只包含稳定 ID、数值、enum、hash 和不可变结果快照，不保存 `GameObject`/`Component` 引用。

- [x] **Step 2：实现显式启动请求**

  ```csharp
  public readonly struct GameLaunchRequest : IEquatable<GameLaunchRequest>
  {
      public GameMapId MapId { get; }
      public int LayoutSeed { get; }
      public GameControllerMode Controller { get; }
      public int InferenceSeed { get; }
      public string ModelId { get; }
      public string ModelHash { get; }
      public string ProtocolVersion { get; }

      public GameLaunchRequest(
          GameMapId mapId, int layoutSeed, GameControllerMode controller,
          int inferenceSeed, string modelId, string modelHash, string protocolVersion)
      {
          MapId = mapId;
          LayoutSeed = layoutSeed;
          Controller = controller;
          InferenceSeed = inferenceSeed;
          ModelId = modelId;
          ModelHash = modelHash;
          ProtocolVersion = protocolVersion;
      }

      public bool Equals(GameLaunchRequest other) =>
          MapId == other.MapId && LayoutSeed == other.LayoutSeed &&
          Controller == other.Controller && InferenceSeed == other.InferenceSeed &&
          ModelId == other.ModelId && ModelHash == other.ModelHash &&
          ProtocolVersion == other.ProtocolVersion;
  }
  ```

  `DinoAttack.Session.Core` 显式引用 `DinoAttack.Map.Core`，测试 assembly 显式引用 Session Core；`GameLaunchContext` 提供一次性设置/读取、同布局派生、新布局派生和清空 API。Map Core 不反向引用 Session 类型，布局签名使用自己的稳定地图字符串；非法 scene/seed/protocol fail closed。

- [x] **Step 3：把重启服务改为场景无关加载器**

  保留现有防重复调度和 realtime 延迟，增加显式 scene stable ID 与 launch request；旧 `TryScheduleReload(bool)` 仅作为地图1 Human 兼容入口，内部转到新 API。

- [x] **Step 4：运行聚焦 EditMode 测试**

  仅运行 `GameLaunchContextTests` 及现有 restart/UI formatter 相关测试，证明 seed 派生确定、请求不串局、重复点击只调度一次。

  审查修正：`TryScheduleLaunch` 在发布 `GameLaunchContext` 或设置 `IsReloadPending` 前，按 Build Settings 中的精确 scene name fail closed 验证目标可加载。当前仅 `Dinos` 可用；尚未由 Task 4 创建/加入的 `MapSelect` 与 `LayeredBattlefield` 请求返回 `false`，且不残留 pending request。

## Task 3：实现“分层战场”确定性中度随机规则

**Files:**
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/LayeredBattlefieldLayoutConfig.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/LayeredBattlefieldLayoutSelector.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/BattlefieldLayoutSignature.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Map/Core/IBattlefieldLayoutSignatureProvider.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Map/Adapters/LayeredBattlefieldLayoutController.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Map/Adapters/OpenTropicalBattlefieldLayoutSignatureProvider.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Training/Core/DinoTrainingMapSelector.cs`
- Create: `dino-attack/Assets/Tests/Editor/LayeredBattlefieldLayoutSelectorTests.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`

- [x] **Step 1：先写确定性与数量测试**

  EditMode先测试地图2纯选择器：同seed必须产生逐字段相同签名，不同测试seed至少产生两个签名；每个结果固定为3套zone preset之一、2 T1 + 2 T2 + 2 T3 + 2 T4、3面墙、5 Archer + 3 Mage、1个核心目标。地图1适配器需要读取现有Assembly-CSharp玩法控制器，因此用聚焦PlayMode测试：相同`CurrentStartSeed`必须把实际六选三墙槽、5名地面守卫候选、Z1–Z5和唯一目标编码为相同共享签名。

- [x] **Step 2：定义有限候选与兼容表**

  配置明确列出三套 Z1–Z5 四边形、外围房屋候选、中层四个 tier 槽位、四个中层墙候选、两个核心墙候选、各层守卫候选、三个核心目标候选和合法组合索引。原“禁止连续随机 Transform”已由 2026-09-01 用户批准的外围房屋矩形随机规则取代；墙、核心房屋、核心守卫和目标仍使用有限候选。

- [x] **Step 3：实现单 RNG 选择器和稳定签名**

  所有选择只消费由layout seed初始化的局部RNG，不读`UnityEngine.Random`全局状态。共享签名包含稳定地图字符串、seed、zone preset/五区顶点，以及每个房屋/墙/守卫/目标的stable ID和最终实际x/z量化坐标；地图2可附带候选索引但不能用索引代替坐标。地图1地面守卫是连续确定性采样，provider必须在生成完成后记录其实际坐标，不能假设存在候选索引，也不能只对seed做hash。坐标统一按约定毫米级或更宽的固定容差量化，避免浮点序列化噪声。

- [x] **Step 4：实现运行时一次性应用**

  两个adapter放在无asmdef的`Map/Adapters`中，由Assembly-CSharp编译，因而可读取现有墙、地面守卫、房屋和RoundManager。`LayeredBattlefieldLayoutController`在其他玩法系统注册前应用布局，启用所选候选并禁用其他候选。2026-09-01 起，六栋外围房屋允许在启动时移动到 seed 决定的矩形位置；不保存场景或重烘焙NavMeshData。地图1provider和地图2controller都实现Map Core中的`IBattlefieldLayoutSignatureProvider`，应用完成后发布只读签名供v2 builder、UI和对比系统读取。

- [x] **Step 5：运行纯规则测试**

  同时实现纯`DinoTrainingMapSelector`：对episode seed做稳定奇偶/哈希映射，不依赖scene状态。运行`LayeredBattlefieldLayoutSelectorTests`、selector聚焦测试和地图1签名的单项PlayMode测试；额外抽取一组有限seed检查计数、层级和索引边界，不把大规模随机统计作为门禁。

**2026-09-01 外围房屋与随带守卫规则（取代旧固定外围点）：** 六栋非核心房屋（2×T1、2×T2、2×T3）按本地 RNG 在 `x[-45,39] / z[-25,-3]` 内确定性采样；房屋间距至少 9，房屋/守卫与当前部署区及固定村落对象至少保持配置的安全间距，达到有界尝试上限则 fail closed。部署区避让按房屋 3.5m、守卫 1.5m footprint 检查，不只判断中心点。每栋房屋的守卫固定为房屋位置加 `(0,-3.5)`，T1/T2 为弓箭手、T3 为法师；两栋 T4、核心弓箭手/法师保持 authored 坐标。运行时控制器只移动所选房屋，现有 `SessionGroundDefenseController` 生成六名派生守卫与两名核心守卫；展示预检失败不提交 active/transform 状态并允许修复后重试。TDD 证据：selector 初始 RED `7/10`，固定墙卫、footprint、Tier/Core 约束均先 RED 后最终 `12/12`；controller 派生签名和事务性失败均先 RED 后最终 `5/5`；地图2定向 PlayMode `4/4`。完整 PlayMode `57/70` 的 13 个失败与改动前基线逐项一致；完整 EditMode `204/210`，其中 5 个失败与基线一致，隔离工作树另有第三方 License CRLF 哈希差异。

## Task 4：用既有资源构建并接线两个正式场景

**Files:**
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Scenes/LayeredBattlefield.unity`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingEntryBootstrap.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/LayeredBattlefield/Editor/LayeredBattlefieldSceneBuilder.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/LayeredBattlefield/Editor/LayeredBattlefieldValidator.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Editor/DinoFormalTrainingSceneInstaller.cs`
- Create: `dino-attack/Assets/Tests/Editor/LayeredBattlefieldSceneTests.cs`
- Create: `dino-attack/Assets/Tests/PlayMode/LayeredBattlefieldPlayModeTests.cs`

- [x] **Step 1：记录场景写入基线并取得 Editor 独占**

  确认 Unity stopped/clean、无编译、当前 `Dinos.unity` hash 和已有 dirty 清单；其他任务停止保存场景后再运行 builder。

- [x] **Step 2：创建地图2而不复制新资源**

  builder/validator放在无asmdef的独立Editor目录，由Assembly-CSharp-Editor编译并安全引用现有Assembly-CSharp玩法类型；它复用地图1的World、镜头、地表材质、水Shader、Hut、墙、守卫、恐龙、UI和音频引用，创建独立地图2层级。布置8栋候选房屋、3面激活墙、11名激活Guards的合法候选集合；外围无墙，中层两墙，核心一墙，每墙一名墙上弓箭手。

- [x] **Step 3：创建三套五区预设与预烘焙导航**

  Z1/Z5 位于外围资源侧，Z2/Z4形成两侧推进面，Z3位于中央近核心区；保持既有五种地貌与倍率。所有候选布局共享一份能覆盖合法组合的预烘焙 NavMeshData，不在 Play Mode 调用 `BuildNavMesh`。

- [x] **Step 4：接入地图专属规则**

  设置初始肉 50、50秒、8栋房屋各死一次返100、Tier HP 100/150/225/325、肿头龙/迅猛龙/霸王龙成本 25/50/100、同一 `RoundManager.DinoTarget`、同一正式奖励源和 v2 六流 builder。地图1保持既有布局和玩法数据。

- [x] **Step 5：创建轻量选图场景**

  `MapSelect.unity`显示 Dino Attack Logo、“开阔战场”和“分层战场”，不保留独立开始按钮；点击任一地图立即生成新的 Human launch request 并加载对应 scene，首屏不提供 AI。EventSystem 使用 `InputSystemUIInputModule`。builder 同时创建并接线早期执行的 `DinoTrainingEntryBootstrap`：普通启动保持菜单，检测到精确 `--dino-training` 时使用 Task3 selector 和首 episode seed 直接加载正式地图；Task4保存场景前验证该组件引用完整。

- [x] **Step 6：运行必要场景门禁**

  Validator/聚焦测试只证明：两图边界/原点一致；地图2三套 zone preset 均为有效四边形且可部署；3 种恐龙从 5 区到核心有路径；固定数量与层级正确；11 Guards 无截断；Play Mode 没有 runtime NavMesh bake。由 Unity Editor 保存并确认 scenes clean。

- [ ] **Step 7：修正并验证房屋/区域的最终可见资源对应**

  2026-08-31 用户截图复核证明原 Step 6 的验证面不足，Task 4 的视觉对应验收重新打开。地图2必须从同一 `BattlefieldZoneDefinition` 为每个 preset 生成并同步切换可见地表 Mesh、`TerrainSurfaceVolume` 速度 Trigger、部署区和边界；不得保留地图1地表来表现地图2。房屋必须按逻辑 Tier 确定性使用 `Hut_Tribal_Tier1–4`，并显式记录旋转、缩放与落地基准。用户随后明确免除房屋/区域资源对应与顶部村落构图的自动视觉失败测试，最终视觉 PASS 由用户依据运行效果判断；实现仍需通过编译、Build Settings、同源几何、部署落点、固定数量、共享推理部署、无 runtime NavMesh bake 和场景 clean 等确定性门。技术修复与代表性截图已完成，复选框保持未勾选直至用户目视确认；证据写入 `reports/phase8/layered-battlefield/visual-layout-audit.md`。

**2026-08-31 选图/地表/UI修复证据：** 以截图复现确认选图无响应来自旧 `StandaloneInputModule` 在“仅新 Input System”项目中抛出 `InvalidOperationException`；已换为 `InputSystemUIInputModule`，删除选图页独立 Start，并把两个地图按钮接为直接启动。地图2不再停用承载 `Neutral Ground` 的父根，地图1显式删除 `ShallowWater/Riverbed`，两图均保持无 Riverbed 且共享同一浅水材质。Logo 从共享玩法 UXML 移至 MapSelect。TDD RED 为 `LayeredBattlefieldSceneTests 2/4`（旧 Start 字段、Map1 Riverbed），修复后 GREEN `4/4`；地图一视觉结构与 UI 合约 `1/1 + 1/1`、restart service `2/2`、Human PlayMode `1/1`，fresh compile 0 error。MapSelect Play Mode 层级仅含两地图按钮、Logo 与 `InputSystemUIInputModule`，fresh Console 无输入异常。房屋/区域对应及顶部村落构图的最终视觉判断仍由用户完成。

**2026-08-31 只读视觉审计：** 当前实现未满足 Step 7。builder 只创建裸 `DinoDeploymentZone`，没有为地图2创建对应地表 Mesh/速度 Trigger/边界；截图中的纵向浅水仍对应地图1 Z3，而批准的地图2 Z3 是 Z `[-51,-37]` 的横向带。房屋按无排序保证的地图1实例循环克隆后只覆盖逻辑 Tier/HP，没有按 Tier 绑定 Hut、校正旋转/枢轴或验证 Renderer bounds。既有 validator/tests 只验证数量、逻辑几何、路径和无 runtime bake，不能证明美术对应。详见 `reports/phase8/layered-battlefield/visual-layout-audit.md`；本次未修改 Unity 代码或场景。

## Task 5：实现地图选择和四种结算后动作

**Files:**
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeUI.uxml`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeUI.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/UI/MapSelectUI.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Session/Core/GameResultActionRouter.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/UI/GameResultActionController.cs`
- Create: `dino-attack/Assets/Tests/Editor/GameResultActionRouterTests.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/LayeredBattlefieldPlayModeTests.cs`

- [x] **Step 1：写四操作路由测试**

  在 `DinoAttack.Session.Core` 的纯 `GameResultActionRouter` 上验证：Retry保持scene/seed且Human，New Layout保持scene/生成新seed且Human，AI Strategy保持scene/seed且InferenceAI，Select Map清空对比并进入菜单。`GameResultActionController` 只把UI点击转给该路由，不要求EditMode tests引用Assembly-CSharp。

- [x] **Step 2：替换单一 Restart 结算入口**

  Ended 面板显示四按钮；保留原本一次 Victory/Defeat、阻止重复点击和完整场景重载。首次从菜单进入仍显示中央 Start；Retry/New Layout 重载后自动 Running，不再显示 Logo/Start。

- [x] **Step 3：让两张地图共用 UI 控制器**

  UI 只从当前场景配置读取显示名和可用动作，不用 scene 名字符串散落判断。AI 模型缺失或不兼容时仅 AI Strategy 禁用，其他三个按钮继续工作并显示简短原因。

- [x] **Step 4：运行聚焦路由与 PlayMode 测试**

  每个按钮各走一次，验证目标 scene、seed、controller mode 和开局状态；不在本任务重复地图路径或全量 UI 分辨率矩阵。

**2026-08-31 实施证据：** 纯路由先以缺少 `GameResultActionRouter` 的编译错误 RED，再以四路由、AI fail-closed、首次菜单 Start 与结果重载自动开局共 `7/7` EditMode GREEN；精确 PlayMode `ResultPanelRoutesEveryButtonAndSuppressesPendingDuplicates` 为 `1/1`，实际触发四个 UI Toolkit 按钮并验证 map/seed/controller/`StartAutomatically`、重复点击抑制和重复 Ended 不重复 outcome。fresh compile 0 error、Console error 0。未保存场景；`Dinos`/备份、`LayeredBattlefield`、`MapSelect` SHA-256 保持授权基线。实际训练 v2 checkpoint 不存在，因此 AI Strategy 单独禁用并显示原因；Task 8 加入 Build Settings 前，不可用目标由现有 availability gate 拒绝且不发布上下文。Task 5 实现完成；独立实现审查没有剩余代码、测试或场景修改项，仅本文档状态同步等待复核，复核通过后再记录 `Approved`。详见 `reports/phase8/layered-battlefield/task-5-result-actions.md`。

## Task 6：实现默认 AI 同布局重赛和确定性推理

**额外依赖：** `TASK-8.12` 已产出至少一个实际训练过且 metadata 匹配的 v2 Gaussian PPO Set Transformer checkpoint。若尚未产出，只实现推理适配器和安全禁用态，本Task保持未完成。

**Files:**
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Inference/DinoInferenceModelConfig.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Inference/DinoSentisPolicyRunner.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Inference/Core/DinoInferenceDecisionPolicy.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Inference/DinoInferenceController.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Player/DinoSpawner.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoStructuredObservationBuilder.cs`
- Create: `dino-attack/Assets/Tests/Editor/DinoInferenceDecisionPolicyTests.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/LayeredBattlefieldPlayModeTests.cs`

- [x] **Step 1：写控制器边界测试**

  把无场景依赖的25-step调度、4D shape、finite/bounds和重复step去重抽入`DinoAttack.Inference.Core`的`DinoInferenceDecisionPolicy`，由现有EditMode asmdef直接测试。`DinoInferenceController`位于Assembly-CSharp，只在聚焦PlayMode验证：输出经过现有codec、部署经过`DinoSpawner.TryDeploy`、InferenceAI禁用鼠标/快捷键、场景自动进入Running。

- [x] **Step 2：实现六流张量适配器**

  直接消费 v2 builder 的六个 frame streams，按训练时相同 shape/mask/归一化创建张量；不重新编码区域、目标或实体顺序。输出先做有限性检查，再 clamp 到 `[-1,1]` 并交给共享 action path。

- [x] **Step 3：实现可配置默认模型**

  ScriptableObject 保存 ModelAsset、model ID、hash、protocol、输入输出契约、artifact kind 和默认 inference seed。UI 不写死 PPO/算法名；运行时再次验证配置与 launch request 匹配，并拒绝 `feasibility_only` 或未训练 artifact。

- [ ] **Step 4：实现确定性同布局运行**

  同 model hash + scene + layout seed + inference seed 必须产生相同动作序列。AI观看正常渲染、正常 Human 速度，禁用外部 communicator 和训练 episode 回调；胜负仍由 `RoundManager` 决定。

- [ ] **Step 5：运行最小推理门禁**

  用默认模型分别在地图1和地图2各选一个seed连续执行两次短重赛，比较共享布局签名和动作序列hash；另验证缺模型/错协议/`feasibility_only`时按钮禁用且玩家流程不受影响。

**2026-08-31 可实施范围证据：** Steps 1–3 已完成。Core 以缺少 `DinoInferenceDecisionPolicy` 的 `CS0246 ×2` RED 起步；controller 聚焦 PlayMode 以缺少 `DinoInferenceController` 的 `0/1` RED 起步。最终 fresh compile 0 error，`DinoInferenceDecisionPolicyTests` EditMode `7/7`、`InferenceControllerDisablesPlayerInputStartsRoundAndDeploysDecodedActionOnce` PlayMode `1/1`。六流直接消费 `DinoStructuredObservationBuilder` 已生成 frame，按 `(1,5)/(1,5,8)/(1,6,5)/(1,11,7)/(1,8,6)/(1,10,7)` 调度 AI Inference CPU Worker；输出先拒绝非有限值，再 clamp `[-1,1]`，经既有 codec 与 `DinoSpawner.TryDeploy`。InferenceAI 禁用玩家鼠标/快捷键、训练 Agent/调度器并自动进入 Running；Worker、输入 Tensor 与 CPU readback 均有显式释放路径。模型配置验证 ID、SHA-256、v2 protocol、`trained_checkpoint`、六输入、单一 `actions (1,4)` 输出与 inference seed；缺模型、错协议/输入/输出、已知 feasibility fixture 均 fail closed。审查修正先以旧fixture复用结构得到精确EditMode `3/4` RED（wrong protocol断言实际收到feasibility早退reason），再用非fixture内存ModelAsset、非fixture hash与`trained_checkpoint`建立eligible base，分别破坏protocol、input name/order和output，确认每个分支返回对应reason；未暴露production bug，故只改测试。`RuntimeUI` 的 AI availability 已接真实配置/模型检查，仓库没有默认 config 或正式 checkpoint，因此按钮保持禁用且未创建伪模型/启发式回退。Steps 4–5 与 Task 6 总体仍受 `TASK-8.12` 阻塞：没有实际训练 checkpoint，不能执行两地图同 seed 动作 hash 重赛门。未保存场景；四个保护场景 SHA-256 与 Task 5 基线一致。详见 `reports/phase8/layered-battlefield/task-6-inference-adapter.md`。

## Task 7：实现玩家/AI结算对比

**Files:**
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Session/GameResultCollector.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Session/Core/GameResultPairingRules.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/UI/GameComparisonPresenter.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeUI.uxml`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeUI.cs`
- Create: `dino-attack/Assets/Tests/Editor/GameResultPairingRulesTests.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/LayeredBattlefieldPlayModeTests.cs`

- [x] **Step 1：写快照配对与清理测试**

  快照包含win/loss、结束时间、最终肉、正式总奖励、scene ID、共享layout signature、model hash。`GameResultPairingRules` 位于 Session Core 并由现有测试asmdef直接测试；只有scene + signature匹配的Human/InferenceAI才能配对，新地图、新seed、新Human run都清除旧对比。`GameResultCollector` 的场景接线只做聚焦PlayMode验证，不让EditMode assembly直接引用Assembly-CSharp。

- [x] **Step 2：接入共享结算源**

  `GameResultCollector` 在 `Ended` 恰好冻结一次数据，直接读取正式 reward accumulator/summary；不得用 UI 文本反推，也不得重新计算胜利肉奖励。

- [x] **Step 3：实现并排展示**

  AI重赛结束后显示玩家与AI的胜负、完成/结束时间、最终肉、总奖励；布局签名不一致时明确拒绝“同布局对比”，但保留返回地图与新局按钮。

- [x] **Step 4：运行聚焦 EditMode 测试**

  覆盖一次正常配对、一次签名不匹配、一次新局清理和一次重复 Ended；不做额外视觉矩阵。

**2026-08-31 实施证据：** `GameResultSnapshot` 已扩展为不可变 scene stable ID、完整共享 layout signature、controller、model ID/hash、protocol、胜负、结束时间、最终肉和正式总奖励；`GameResultPairingRules` 只允许 Human/InferenceAI 在 scene stable ID 与完整 signature 同时一致时配对。新地图、新seed及任何新Human局均清除旧玩家快照。`GameResultCollector` 仅在 `Ended` 首次冻结，直接读取 `DinoTrainingAgent.LastCompletedEpisodeReward`、`RoundManager`时间/胜负及`DinoSpawner`最终肉，不读取UI文本或重算奖励；地图1缺少序列化provider时在RuntimeUI对象上动态补充只读provider，不保存场景。并排UI显示双方胜负、时间、肉与总奖励；signature不一致显示明确拒绝文案且四个结果按钮仍保留。RED为缺失pairing类型/扩展snapshot构造、缺失collector runtime type、缺失comparison panel和RuntimeUI bridge；GREEN为EditMode `GameResult*` `11/11`、`GameLaunchContextTests` `5/5`，PlayMode collector `3/3`及Task5四按钮回归`1/1`。真实Dinos synthetic Ending证明冻结reward与正式summary精确相等、重复Ended不改变快照。未保存场景；三场景hash保持Task4授权基线。实际AI按钮仍因缺少正式v2 checkpoint安全禁用，故玩家→真实AI最终目视对比继续依赖Task6 Steps 4–5，Task7代码/捕获/展示本身完成。证据见`reports/phase8/layered-battlefield/task-7-result-comparison.md`。

## Task 8：接入双地图 50%/50% 训练并做一次聚焦集成

**Files:**
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingLaunchOptions.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingSceneReloader.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Editor/DinoFormalTrainingPlayerBuilder.cs`
- Modify: `flow_rl/configs/phase8_dino_ppo_smoke.yaml`
- Create: `dino-attack/Assets/Tests/Editor/DinoTrainingMapSelectorTests.cs`
- Create: `reports/phase8/layered-battlefield/validation.md`
- Create: `reports/phase8/layered-battlefield/manifest.json`
- Modify: `docs/PROJECT_PLAN.md`
- Modify: this plan file

- [x] **Step 1：实现按 episode seed 的确定性地图选择**

  训练模式通过 seed 的稳定映射选择地图1/地图2，目标长期比例50%/50%；Human和InferenceAI不使用该选择器。首个episode和后续episode都调用同一选择器，单个episode仍完整重载所选scene。

- [x] **Step 2：让 Player 构建包含三场景**

  Human build以`MapSelect.unity`为index 0。Task4已把`DinoTrainingEntryBootstrap`实际挂入并保存菜单场景；本Step用场景检查确认组件存在且引用Task3 selector语义。bootstrap以早期执行顺序读取明确`--dino-training`和首episode seed，直接加载第一张正式地图，不创建Human UI会话；后续reloader不再只重载当前scene，而是用next episode seed选择并加载目标scene。正式builder包含`MapSelect.unity`、`Dinos.unity`、`LayeredBattlefield.unity`。除确有必要，不修改ProjectSettings/Packages。

- [x] **Step 3：运行一次必要双地图协议检查**

  各地图启动一个训练 episode，验证六流 shape、Regions/target 取自当前场景、地图2恰好填满11个 Guard 行且不截断、terminal/truncated 和重载 seed 正确。

- [x] **Step 4：运行一次混合 smoke**

  用单一 v2 模型/配置跑一个短混合进程，至少实际观察到地图1和地图2各一个 episode；分别记录 map1、map2、mixed 的基本结果。此门禁不是训练收敛、胜率或多进程性能验收。

- [x] **Step 5：生成最小证据并更新项目计划**

  `manifest.json` 记录两场景 hash、三套 zone preset、布局配置 hash、默认模型 hash/protocol、NavMeshData、必要测试和 smoke 命令。`validation.md` 只汇总上述硬门、已知限制和用户目视复核待办；完成后更新 `docs/PROJECT_PLAN.md` 的状态、复选框和证据路径。

**2026-08-31 实施证据：** episode-seed selector 已同时接入首局 `DinoTrainingEntryBootstrap` 与后续 `DinoTrainingSceneReloader`，只服务精确 TrainingAI 路径；formal builder 使用显式 `MapSelect / Dinos / LayeredBattlefield` 三场景数组和全新 Task8 输出路径，未改 Editor Build Settings。TDD RED/GREEN 分别为 Unity `2/5 → 5/5`、Python runner `2/4 → 4/4`，既有 formal launch 聚焦回归 `3/3`。唯一最终 mixed PPO smoke 为四环境 `515` transitions、map1/map2 episode starts `6/7`、natural terminals `5/4`、两图 truncated `0/0`、一次 finite update；map2 实际观察11个有效Guard行，六流shape精确且两图 Regions+target fingerprint不同。Player/端口清理为0，三场景hash保持Task4授权基线。证据见 `reports/phase8/layered-battlefield/validation.md` 与 `manifest.json`。Task 8 完成，但 Task 6 Steps 4–5仍因缺少正式v2 checkpoint阻塞，故MAP2总体保持进行中。

## 执行顺序与审查门

1. Task 1 是技术硬门；v2、正式奖励或Sentis可行性任一未通过，不进入地图/UI实现。feasibility fixture只验证技术链，不满足默认策略交付。
2. Task 2–3 是纯逻辑基础；可在不打开 Unity 的情况下完成，但共享文件必须串行审查。
3. Task 4 独占 Unity Editor并创建两个场景；完成 stopped/clean 交回后再做 UI/推理接线。
4. Task 5依赖Task 2/3/4。若此时没有正式checkpoint，暂停`TASK-8.11-MAP2`并让`TASK-8.12`至少产出一个正式训练的v2 checkpoint，再回来执行Task 6；Task 7依赖Task 6。没有正式checkpoint时可交付地图和安全禁用态，但本计划保持未完成。
5. Task 8 最后接入训练选择，只运行一次必要混合 smoke；不提前宣称算法训练或双地图泛化完成。

## 完成定义

- 两张正式地图可由 MapSelect 进入；地图2按 seed 产生合法、确定的中度随机分层战场，且不运行时烘焙 NavMesh。
- 地图2当前 preset 的可见地表、速度 Trigger、部署区、v2 Regions 与边界共享同一组四边形；房屋逻辑 Tier 与对应 Hut 美术、旋转和落地包围盒一致；Home 视角村落核心位于活动部署区上方。
- 两图共享 v2 六路270标量和4D动作，不增加 map ID；地图2的11名守卫完整可观测。
- 结算后四个操作符合 scene/seed/controller 语义；AI按钮在默认模型不可用时安全禁用。
- 默认 AI 使用实际训练过的正式v2 checkpoint，可在两张地图的相同布局seed下本地确定性重赛；Human与AI共用动作/部署/胜负/奖励路径，feasibility fixture永不作为默认策略。
- 玩家/AI结果只在相同布局下配对，并正确显示胜负、时间、最终肉和总奖励。
- 训练模式完成一次两地图50%/50%选择的聚焦 smoke；未引入新美术包、外部AI观看进程、runtime NavMesh bake、提交切片或额外验收矩阵。
