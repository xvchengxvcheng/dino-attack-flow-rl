# Dino Attack 玩家持续决策、战略地图与同场景 AI 设计

**日期：** 2026-08-28
**状态：** 待用户确认；确认前禁止 Unity 实现
**依据：** `reports/phase8/player-audit/analysis.md`

## 设计结论

采用保守式三入口强化：保留现有村庄、蛋目标、三处入口墙、回合状态机和资源公式，将中央、左侧、右侧塑造成可辨识且风险不同的进攻路线；玩家和 AI 在同一 `Dinos.unity` 上调用同一部署服务，差异仅是决策输入来源。

## 11. 玩家持续决策流程

1. 回合进入 Setup，玩家获得食物，可选择恐龙、预览位置、取消或等待；至少部署一只后可开始。
2. 进入 Running 后战斗持续进行，恐龙按钮继续可用。玩家观察路线压力与食物变化，可随时选择恐龙并在合法部署区放置，也可不做任何操作。
3. 击杀 Defender 或摧毁 Wall 后食物立即增加并显示来源；玩家可把返还资源投入增援。
4. 恐龙到达蛋目标为胜利终止；场上恐龙全部死亡且无可继续战斗单位时为失败终止。进入 Ending 后冻结所有部署输入。
5. 结算、敌方维修和下一回合沿用现有流程。玩家没有最大部署次数或固定决策间隔；AI 才按 scheduler 请求 Wait/Place 动作。

## 12. 状态交互规则

| 状态 | 玩家 | AI | UI/部署服务 |
|---|---|---|---|
| Setup | 选择、预览、确认、取消、等待、Start | 可 Wait/Place；首次合法 Place 后可启动回合 | 允许部署；显示准备状态和 Start |
| Running | 选择、预览、确认、取消、等待；战斗不暂停 | 按固定间隔 Wait/Place | 允许部署；显示食物变化、路线压力、非法原因 |
| Ending | 不允许部署 | 不请求新决策 | 清空预览、锁定按钮、显示胜负 |
| Scoring / EnemyRepairs / Ended | 不允许部署 | 不请求新决策 | 显示结算/维修/下一回合，不接受世界点击 |

部署状态判断必须集中在共享服务，不能由 UI、Human 控制器和 Agent 各自复制。

## 13. 放置、扣费和食物返还流程

新增纯结果式 API（名称可在实现时按项目规范微调）：

`Evaluate(DeploymentRequest) -> DeploymentResult`：返回 Allowed、InvalidState、UnknownType、InsufficientFood、OutsideBounds、NoGround、NoNavMesh、Overlap、BlockedByUI 等明确结果，不改变状态。

`TryDeploy(DeploymentRequest) -> DeploymentResult + spawnedUnit`：在再次校验成功后原子扣费、生成、订阅死亡事件并发布一次部署事件。Human 与 AI 不得直接调用 `Instantiate` 或自行扣费。

记账规则保持：

- 合法部署：食物减 `dino.Cost`，恰好一次。
- Running 击杀 Defender：加 `floor(defender.Cost / 2)`，恰好一次。
- Running 摧毁 Wall：加 `floor(wall.Cost / 2)`，恰好一次。
- 回合末幸存恐龙：返还完整成本；已死亡恐龙不返还。
- Wait、非法动作和取消：不扣食物；Human 显示原因，AI 由环境施加既定非法动作惩罚。

## 14. 地图区域和战略节点

保留现有蛋目标约 `(-2.23, -0.10, -38.26)` 与村庄整体朝向，把三处入口墙组织为三个清晰战线：

- **中央正门：** 距离短、视野开阔、敌方火力最强；适合高生命/突破型恐龙。
- **西侧峡口：** 通道窄、转向和聚集明显；适合近战集群，但易被范围/控制单位压制。
- **东侧林缘：** 路程较长、掩体和侧翼空间更多；部署安全但增援到达慢。
- **后方部署带：** 安全、可读、成本低风险，但到目标距离远。
- **前沿部署袋：** 靠近入口，可快速增援，但可放置面积小且更容易重叠/暴露。

地图通过道路颜色、地表 patch、稀疏植被、旗帜/火炬和选择标记表达路线；纯装饰物不参与合法性或 NavMesh，除非显式加入阻挡设计。第一版不新增可破坏桥或复杂河流，以免同时改变导航与胜负语义。

建议灰盒草图（最终坐标由 Editor 审计和可达性验证确定）：

```text
              [敌方村庄 / 蛋目标]
        [西门]      [中央正门]      [东门]
          │           │             │
      西侧峡口     短路高压区      东侧林缘
          │           │             │
       前沿袋 W     前沿袋 C       前沿袋 E
          └────── 后方安全部署带 ──────┘
                    [玩家视角]
```

| 节点 | 视觉编码 | 玩法取舍 | 导航验收 |
|---|---|---|---|
| 中央正门 | 宽路、暖色旗帜、最清晰视线 | 路短、火力密 | 三种恐龙都可达目标 |
| 西侧峡口 | 岩壁、窄地表带 | 聚集强、易受控制 | 无卡角，动态墙毁后可重算 |
| 东侧林缘 | 稀疏树列、冷色地标 | 路长、前沿面积较大 | 树木装饰不误阻 NavMesh |
| 后方部署带 | 连续浅色边界 | 安全但增援慢 | 全带可投影且无遮挡 |
| 前沿部署袋 | 选择标记/地表 patch | 响应快但空间有限 | bounds、重叠和可达性一致 |

## 15. UI 与反馈设计

- 顶栏：食物余额、最近一次 `+/-` 变化及来源、存活恐龙、当前阶段/回合。
- 底栏：三种恐龙卡片、成本、快捷键、可用/食物不足状态、当前选择；Running 中不隐藏。
- 世界反馈：合法预览使用颜色加轮廓/图标；非法预览显示简短原因，不能只用红绿颜色。
- 战术反馈：三条路线使用克制的地标和威胁提示，不显示会泄露训练内部状态的调试数据。
- 结算：胜负、关键资源变化和继续提示；Ending 时阻止重复点击。
- 适配：验证 16:9、16:10、21:9 和 1280x720；UI Toolkit picking 区域不得覆盖主要部署区。

## 16. 兼容现有玩法的最小修改方案

- 不新增 GameState，不改 Setup -> Running -> Ending -> Scoring -> EnemyRepairs -> Ended 的主序列。
- 不改三种恐龙、敌方 Balanced 配置、Hard 初始难度、现有成本和返还公式。
- 把 `DinoSpawner` 拆为/委托给共享部署服务；保留旧事件签名，减少 RoundManager 和 UI 影响面。
- `HumanDeploymentController` 负责鼠标/键盘/UI；`DinoTrainingAgent` 只做 observation/action/reward；二者调用相同服务。
- `DinoGameModeBootstrap` 显式选择 Human 或 AI：Human 开启玩家输入和可见 UI；AI 关闭 Human 提交路径并启用 Agent。任何时刻只允许一个决策者提交。
- 正式训练 Player 直接加载 `Dinos.unity`。历史 `DinoAttackTraining.unity` 保留为诊断夹具，不进入正式结果。
- 阶段 4–7 算法模块直接复用，只新增 Dino Attack 的网络输入/输出、配置和 checkpoint；全部重新训练，不加载 3DBall 权重。

### 16.1 精确影响面与预期新增文件

现有文件预计只修改：

- `Assets/LlamAcademy/Dinos/Player/DinoSpawner.cs`
- `Assets/LlamAcademy/Dinos/Player/PlaceDinoVisualization.cs`
- `Assets/LlamAcademy/Dinos/UI/RuntimeUI.cs`
- `Assets/LlamAcademy/Dinos/UI/RuntimeUI.uxml`
- `Assets/LlamAcademy/Dinos/UI/RuntimeStyles.uss`
- `Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingAgent.cs`
- `Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingArea.cs`
- `Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`

预期新增（最终命名须经测试与项目命名规范复核）：

- `Assets/LlamAcademy/Dinos/Deployment/DeploymentRequest.cs`
- `Assets/LlamAcademy/Dinos/Deployment/DeploymentResult.cs`
- `Assets/LlamAcademy/Dinos/Deployment/DinoDeploymentService.cs`
- `Assets/LlamAcademy/Dinos/Player/HumanDeploymentController.cs`
- `Assets/LlamAcademy/Dinos/Training/Runtime/DinoGameModeBootstrap.cs`
- `Assets/LlamAcademy/Dinos/Editor/Stage8SceneBackup.cs`
- `Assets/Tests/Editor/DeploymentServiceTests.cs`
- `Assets/Tests/PlayMode/DinoContinuousDeploymentTests.cs`
- `Assets/ThirdParty/Kenney/` 下审批清单中的模型、贴图、许可证和来源 manifest。

除非测试证明必要，`RoundManager.cs`、`EnemyAIController.cs`、Packages 和 ProjectSettings 不在第一轮修改面。

### 16.2 UI 信息层级

1. 最高优先：食物余额/变化、阶段、胜负和非法原因。
2. 决策优先：当前恐龙、成本、可用性、合法预览、取消。
3. 战术辅助：三路线地标、敌人压力和前沿/后方部署区。
4. 低优先：品牌 Logo、装饰动画和非交互氛围元素。

### 16.3 分阶段测试

- 8.4：备份 hash、依赖清单、只读恢复打开、Git 文件归属和 selected-asset manifest。
- 8.5：先做 DeploymentService EditMode 测试，再做 Setup/Running/Ending、扣费/返还、Human/AI 契约 PlayMode 测试。
- 8.6：每入口可达性、动态墙毁/维修重烘焙、waypoint、点击投影和摄像机边界。
- 8.7：前后截图、材质/阴影/碰撞体审计及运行时 CPU/GPU/内存/三角形对比。
- 8.8：多分辨率、键鼠、UI picking、非颜色反馈和 Ending 防重复输入。
- 8.9：完整玩家验收矩阵、长回合、重置、Console/NaN/重复对象/泄漏扫描。

## 17. 备份、Git 和回滚方案

首次 Unity 写入前执行 TASK-8.4：

1. 记录外层/内层 HEAD、分支、完整 status、关键文件 SHA-256、Unity 版本、场景层级、截图、Console 和运行时性能基线。
2. 用 Unity Editor API 把 `Dinos.unity` 复制到不参与 Build 的版本化备份路径，并生成 hash/依赖清单；做一次只读打开验证。
3. 建立阶段 8 文件归属表：用户既有修改、阶段 8 修改、生成物、不入库文件四类。
4. Git 只使用显式路径 `git add -- <file>`；每次提交前检查 cached diff。不得 `git add -A`、自动 stash、reset、checkout 或 clean。
5. 外层提交只含 docs/flow_rl/小型报告；内层提交只含经确认的 Unity 源文件、资源与 `.meta`。不把 Library、Logs、Temp、UserSettings、训练大产物或用户既有脏文件混入。
6. 每个子任务形成可独立回滚的 checkpoint；场景恢复通过备份/阶段提交执行，不覆盖未归属的用户修改。

用户要求“对项目进行 .git 维护”在本设计中解释为维护两个现有仓库的状态、归属、显式暂存和可恢复提交；不把仓库合并、不新建嵌套仓库、不擅自提交当前既有修改。

## 18. 验收标准

### 玩家玩法

- Setup 与 Running 均可选择、预览、合法放置和取消；玩家可无限次等待而不产生隐式动作。
- Ending 及后续结算状态拒绝部署；Human/AI 不会双重提交。
- 食物不足、越界、无地面、无 NavMesh、重叠和 UI 遮挡都有正确拒绝与可读反馈。
- 部署扣费、敌人/墙体返还、幸存者返还均恰好一次；胜利、失败、结算、维修和重置正常。

### 地图与视觉

- 三条战线和至少两个风险不同的部署层级可由玩家在 5 秒内辨认；每条路线都有可达性和战术取舍。
- 所有恐龙/敌人 agent 类型可到达其目标；动态墙体变化后 NavMesh 和 waypoint 正常，无新增卡死或脱网警告。
- 摄像机边界覆盖全部关键区域；树冠、山体、UI 不遮挡部署预览与关键战斗。
- 地面、山体、灯光和后处理风格统一；新增 Kenney 资源有 CC0 来源清单且仅导入选定 FBX/贴图。
- 目标质量档位下无明显性能回退；运行时建立 CPU/GPU/内存/三角形基线后再确定数值预算。

### 同场景 AI 与算法

- Human 与 AI 对同一状态和部署请求得到相同 DeploymentResult、资源变化和生成结果。
- 正式 AI 模式仅加载同一 `Dinos.unity`；504/6 schema、Agent ID、terminated/truncated、reward 分解和固定 seed 测试通过。
- PPO 先证明任务可学习；FPO、ReinFlow、PolicyFlow 复用现有实现但使用 Dino Attack 新配置/数据/权重。
- Unity Console 无新增 Error、异常、NaN、重复对象或资源泄漏；历史 HealthBar/waypoint 警告被归因并修复或形成明确豁免。

## 用户确认点

确认本设计即表示同意：第一版采用 M1 三入口强化，不做大范围河流/双层环路；保留 Hard 难度、原状态机、成本与返还公式；首次实现从 TASK-8.4 的备份/Git 基线开始，然后按 8.5–8.15 顺序推进。
