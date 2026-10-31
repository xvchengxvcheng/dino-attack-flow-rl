# 阶段 8：Dino Attack 玩家场景、持续决策与 Flow-RL 对比详细实施计划

**日期：** 2026-08-28
**状态：** 重做后的 TASK-8.6–8.10 已完成；当前执行 TASK-8.11
**所属主计划：** `docs/PROJECT_PLAN.md` / 阶段 8
**设计规格：** `docs/superpowers/specs/2026-08-27-dino-attack-scheme-b-design.md`

## 1. 阶段目标

本阶段包含两个相互隔离但共享玩法语义的工作流：

1. **玩家游戏工作流：** 将主场景 `Assets/LlamAcademy/Dinos/Scenes/Dinos.unity` 改造成更完整、美观、易理解且具有策略深度的持续决策游戏。玩家可在 Setup 和 Running 阶段部署恐龙，并通过清晰 UI/视觉反馈理解资源、合法区域、路线、威胁和回合状态。
2. **训练与算法工作流：** 正式训练与评估使用和玩家完全相同的 `Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`、地图、敌人、建筑、路径、资源、战斗、胜负和持续部署规则；区别只在决策者。玩家模式启用玩家输入与 UI，AI 模式禁用玩家部署输入并由 ML-Agents Agent 通过同一部署服务执行 Wait/Place。战斗随仿真时间推进，AI 根据战场、资源和单位状态持续决策；恐龙击杀敌人或摧毁建筑后返还食物，使后续决策依赖前序结果。

玩家控制器与训练 Agent 必须分离，但不再维护两套正式场景：`Dinos.unity` 是唯一正式场景事实来源。Player/AI 模式只切换控制器、训练观测和无需渲染的玩家 UI，不复制地图或玩法对象。玩家输入、UI 和演出不能泄漏进训练策略接口；部署、资源、战斗、回合状态和胜负必须调用同一公共 API。

最终在同一冻结环境、训练预算和评估协议下比较：

1. Gaussian PPO；
2. FPO；
3. ReinFlow；
4. PolicyFlow。

玩家工作流以可玩性、可读性、策略深度、视觉一致性和功能稳定性验收；训练工作流以单回合胜率为主要指标。episode return、样本效率、wall-clock、NFE、推理延迟、显存、非法动作率、食物利用率和布局模式覆盖用于解释算法差异，不替代胜率目标。

## 2. 已确认事实与当前基线

### 2.1 固定路径与版本

- Unity 项目：`C:\Users\hp\Documents\New project 2\game_project\dino-attack`
- Unity Editor：`D:\job\6000.3.22f1\Editor\Unity.exe`
- Unity 版本：`6000.3.22f1`
- ML-Agents 本地源码：`C:\Users\hp\Documents\New project 2\game_project\ml-agents`
- 外部训练包：`flow_rl/`
- Python 环境：`.conda/flow-rl`
- FPO 官方参考：`fpo/` @ `418c2554f7cd22d52e14c07d951280929d73bf2f`
- ReinFlow 官方参考：`ReinFlow/` @ `e722e151bed767f3ffef47527cf697f2358af55d`
- PolicyFlow 官方参考：`PolicyFlow/` @ `7f304b96e93b2804bdcc943710db885adddd06b8`

### 2.2 已完成的环境接入

- 原始玩家场景 `Dinos.unity` 保持不变。
- 已创建历史验证用训练场景 `DinoAttackTraining.unity`；它证明了 LLAPI 接入和持续决策接口可行，但不再作为阶段 8 正式训练场景。
- 玩家输入仍只允许 Setup 阶段放置；训练 API 显式允许 Running 阶段继续部署。
- 当前 observation 固定为 504 维。
- 当前 action 固定为 6 维连续值：Place 门控、三个类型分数、归一化 x/z。
- 当前决策由固定仿真时间间隔触发。
- 已接入胜负、击杀、建筑摧毁、部署成本、非法动作和时间 shaping reward。
- 已区分自然终止与安全上限截断，并通过场景重载开始新 episode。
- 已配置项目专用 Unity MCP。
- 已有只包含历史训练场景的独立 Windows Player；后续需要改为从同一 `Dinos.unity` 以 AI 模式启动的版本化训练 Player。

### 2.3 已有验证证据

- Unity 编译无 C# error。
- EditMode：9/9 passed。
- Python 非集成测试：198 passed，3 deselected。
- LLAPI 600-step smoke 跨越 2 个 truncated episode，Agent ID 从 0 更新到 2，动作维度为 6，数值有限。
- 原始 `Dinos.unity` 无 Git diff；Build Settings 工作树内容与 `HEAD` 哈希相同。

### 2.4 尚未完成

- 尚未按用户要求完整输出玩家主场景的 10 项现状分析。
- 尚未输出并确认玩家持续决策、状态交互、地图节点、UI、兼容、回滚和验收设计。
- 尚未修改玩家主场景；当前对 `Dinos.unity` 的授权尚未进入实现阶段。
- 尚未建立玩家场景的基线截图、层级/材质/灯光/UI 清单和性能基线。
- 尚未选择性导入 Kenney 美术资源或完成地图、地表、山体、灯光、后处理与 UI 改造。
- 尚未冻结正式训练 build、配置、场景哈希和奖励版本。
- 尚未把 ML-Agents Agent 以 AI 控制模式接入改造后的同一 `Dinos.unity`，也尚未建立玩家/AI 模式的场景一致性测试。
- NavMesh waypoint、unit cleanup 和短暂 NavMeshAgent 警告尚未全部归类。
- 自然胜负终止、食物返还和奖励分解尚缺完整自动化/运行证据矩阵。
- 尚未建立 Dino Attack 的 Gaussian PPO 正式基线。
- 尚未在 Dino Attack 上训练 FPO、ReinFlow、PolicyFlow。
- 尚未证明当前环境确实存在值得 PolicyFlow 建模的多峰成功布局。

### 2.5 FPO、ReinFlow、PolicyFlow 的可复用状态

三种算法的**通用实现已经完成，可以直接复用代码基础设施，但不能直接使用 3DBall 模型完成 Dino Attack 决策**：

| 项目 | 可否直接复用 | 说明 |
|---|---|---|
| `flow_rl` 的 Unity LLAPI adapter、Agent ID 对齐、rollout、GAE、critic、checkpoint、日志和 evaluator | 可以 | 接口已在 3DBall 和历史 Dino Attack LLAPI smoke 中验证；仍需对同一 `Dinos.unity` 的 behavior、空 batch、终止和重载做集成验证 |
| FPO、ReinFlow、PolicyFlow 的模型类、采样器、更新器和数值测试 | 可以 | 阶段 4–7 已完成，可作为 Dino Attack trainer 的实现基础，不需要重新从论文重写 |
| 阶段 4–7 的训练配置 | 部分可以 | 优化器、NFE、checkpoint schema 等可作为初始值；observation/action 维度、网络容量、预算和奖励相关配置必须重新建立 |
| 3DBall 的 FPO/ReinFlow/PolicyFlow checkpoint | 不可以直接部署 | 3DBall 是 8 维 observation、2 维动作；Dino Attack 当前协议是 504 维 observation、6 维动作，且任务和奖励完全不同，模型输入输出不兼容 |
| ReinFlow 的 3DBall BC 数据与 checkpoint | 不可以 | ReinFlow 需要针对 Dino Attack 收集成功轨迹并重新完成 Flow BC；不能把 3DBall 行为数据当作该游戏的预训练数据 |
| 官方参考仓库中的模型 | 不可以直接替代项目训练 | 官方 clone 用于核对数学和实现来源；真正接入仍使用已经移植到 `flow_rl` 的代码并在 Dino Attack 上重新训练、评估 |

结论：算法工程主体已经完成，阶段 8 不再“重新实现算法”，而是完成同场景 AI 控制接入、Dino Attack 专用配置、必要的维度/容量适配、smoke、重新训练和公平评估。若 observation/action schema 维持 504/6，旧 checkpoint 仅可用于验证 checkpoint 兼容错误能被明确拒绝，不能作为初始化策略。

### 2.6 Git 仓库现状

- 外层 `game_project` 是 Git 仓库，当前分支 `main`，`dino-attack/` 被外层 `.gitignore` 排除。
- `dino-attack` 自身是独立 Git 仓库，当前分支 `main`；Unity 项目修改必须在此仓库单独维护。
- 2026-08-28 只读健康审计：外层 HEAD 为 `2fcbb8892783235c57f7f95898f4c11de2d4deda`，未配置 remote；内层 HEAD 为 `72c45e60c6b1ae7aa933c8dfb6047426437a0275`，`origin` 为官方 `llamacademy/dino-attack`。两个仓库的 `git fsck --connectivity-only --no-dangling` 均通过。
- 审计时外层有 97 条、内层有 33 条 porcelain 状态记录；数量会随工作变化，不能据此认定文件归属或直接提交。
- 两个仓库当前均有大量已有修改和未跟踪文件。它们全部视为用户工作；在完成来源归类前，不允许批量暂存、提交、清理、stash 或重写历史。
- 阶段 8 文档和 Python 训练器由外层仓库维护；Unity 场景、脚本、Prefab、材质和导入资源由 `dino-attack` 仓库维护。每个验证任务分别记录两个仓库的 HEAD、branch、status 和相关 diff。

### 2.7 已提供的外部美术资源

- `C:\Users\hp\Documents\New project 2\kenney_mini-forest_1.0`
  - 22 组 FBX/GLB/OBJ 模型；包含 bridge、platform、fence、grass/dirt patch、plant、rocks、stones、tent、tree 等。
  - 许可证：Creative Commons Zero（CC0）。
- `C:\Users\hp\Documents\New project 2\kenney_tower-defense-kit`
  - 160 组 FBX/GLB/OBJ 模型；包含地形 tile、河流/桥梁/坡地、树木/岩石细节、塔楼结构、武器和选择标记等。
  - 许可证：Creative Commons Zero（CC0）。

资源只允许在 TASK-8.4 根据批准清单选择性导入 FBX 和必要贴图。不得整包复制，不导入重复的 GLB/OBJ/MTL 版本，不直接覆盖项目现有同名资源；导入后保留来源和 license 记录。

## 3. 不可变边界

以下约束适用于整个阶段 8：

- `Dinos.unity` 仅可在 TASK-8.2 分析、TASK-8.3 设计和用户确认完成后修改；必须先通过 Unity Editor API 创建版本化备份并记录原始 hash，禁止手写复杂场景 YAML。
- 玩家场景修改优先通过项目专用 Unity MCP、Unity Editor 和版本化 Editor 脚本完成；不得通过批量文本替换操作场景、Prefab 或 `.asset` YAML。
- Unity 侧统一优先使用本机 Unity Workbench `C:\Users\hp\.codex\plugins\cache\openai-curated-remote\unity-workbench\0.1.3`：项目检查使用 onboarding/health-check，MCP 连接和场景操作使用 unity-mcp-workflow，玩法修改使用 unity-feature-implementation，缺陷定位使用 unity-bug-investigation，最终验证使用 unity-build-validation；能力不可用时记录原因并使用 Unity Editor/MCP 等价验证。
- ProjectSettings、Packages 和既有训练结果默认不可改；确需修改时必须先在主计划新增决策记录并停止等待授权。
- 玩家控制逻辑与训练 Agent 逻辑保持分离，但正式训练和玩家必须使用同一 `Dinos.unity` 及同一玩法服务；不得维护会漂移的正式训练场景副本，也不得为了玩家 UI 或演出修改 observation/action/reward 语义而不升级训练 schema。
- 不修改 `fpo/`、`ReinFlow/`、`PolicyFlow/` 官方参考仓库。
- 不侵入 `mlagents-learn`；算法接入保留在 `flow_rl/`。
- 不向冻结环境 `.conda/mlagents-r23` 安装研究依赖。
- 不删除早期失败运行、历史 checkpoint 或用户已有 dirty worktree 修改。
- 不在阶段 8 引入 action chunk；所有算法使用单步固定 6 维动作。
- 不将训练曲线、单个 seed 或最佳 checkpoint 当成正式结论。
- 未运行的测试不得记录为通过；没有实际证据不得勾选任务。
- Git 操作只允许显式路径：禁止 `git add -A`、`git add .`、`git commit -a`、自动 stash、reset、checkout 覆盖、clean、rebase 或强制推送；提交前必须先展示 staged diff 和文件清单。

## 4. 冻结的第一版环境协议

### 4.1 Episode

- 一个 episode 对应一个游戏回合。
- 第一次合法部署后由 Setup 进入 Running。
- Running 期间仍可等待或继续部署。
- 胜利和失败为自然 `terminated`。
- 达到 `max_episode_seconds` 或 `max_decision_steps` 为 `truncated`。
- AI 模式 episode 结束后允许重载同一 `Dinos.unity`；场景重载可创建新 Agent ID，adapter 必须按既有动态 ID 语义处理。

### 4.2 Observation

第一版保持 504 维固定槽位表示。任务 8.3 必须生成字段布局表，至少记录：

- 字段名称、切片范围、dtype、归一化范围；
- 全局时间、回合状态、食物和近期资源变化；
- 恐龙、敌人和墙体槽位的排序规则、最大数量和零填充；
- 场景变体、上次动作是否合法、目标距离等摘要；
- 超出槽位上限时的裁剪策略和诊断计数。

任何 observation 维度或语义修改都会使旧 checkpoint 不兼容，必须升级 schema 并重建所有阶段 8 正式基线。

### 4.3 Action

固定连续动作：

```text
[0] place_gate
[1] dino_type_score_0
[2] dino_type_score_1
[3] dino_type_score_2
[4] normalized_x
[5] normalized_z
```

- `place_gate <= 0`：Wait。
- `place_gate > 0`：Place。
- 恐龙类型由三个 score 的 argmax 选择。
- x/z 输入限制在 `[-1, 1]`，映射到训练区域后执行地面、NavMesh、越界、重叠和危险层检查。
- 不可执行 Place 返回明确失败原因并计入非法动作率；不能静默变成另一种恐龙或任意远距离投影。

采用纯连续编码是为了保持 Gaussian PPO、FPO、ReinFlow、PolicyFlow 的统一动作边界。若以后改为真正混合离散/连续策略头，属于架构变更，需另立决策并重跑全部基线。

### 4.4 Reward 与任务指标

训练 reward 由可配置分量组成：

```text
terminal win/loss
+ defender kill shaping
+ building destruction shaping
- normalized placement cost
- invalid action penalty
- per-decision time penalty
```

正式比较必须同时记录：

- round win rate；
- episode return 和 length；
- terminated/truncated 数量；
- 击杀数、建筑摧毁数、最终/累计食物；
- 成功部署数、等待数、非法动作数及非法原因；
- 每种恐龙使用频率和部署坐标分布；
- environment steps、Unity steps、wall-clock、steps/s；
- NFE、策略推理延迟、峰值显存和参数量。

胜率是模型选择的首要依据。shaping reward 只用于改善信用分配，不能通过增大 shaping 权重使高 return 与失败回合并存。

### 4.5 玩家交互状态规则

- **Setup：** 玩家可选择恐龙、预览位置、确认或取消部署，并主动开始战斗；合法部署扣除食物，非法部署不扣除资源并显示原因。
- **Running：** 战斗持续推进，玩家仍可选择、预览、确认、取消或等待；部署不会暂停敌人和恐龙的正常行为，除非设计阶段明确采用短暂战术暂停并获得用户确认。
- **Ending：** 禁止新部署，锁定战斗输入，显示胜负、关键统计和重新开始入口；场景重置不得残留单位、预览对象、订阅或资源状态。
- 所有状态转换必须由单一游戏状态机驱动；UI 可见性、输入许可和部署许可不得各自维护互相冲突的布尔状态。

### 4.6 地图与视觉设计原则

- 当前唯一有效地图规格为 `docs/superpowers/specs/2026-08-28-dino-attack-open-tropical-battlefield-design.md`；旧三入口、固定三路线、四区/六区和雨林空地方案均已取代。
- 地图采用约 240° 马蹄形五区部署结构：Z1 废墟前庭、Z2 开阔草地、Z3 河岸台地、Z4 棕榈林缘、Z5 岩石高台；五区始终开放且所有恐龙类型通用。
- 村落扩大为四个可进入聚落区围绕中央广场，现有胜利目标语义整合到广场；统一 NavMesh 自由机动，不设置固定路线或隐藏 waypoint 走廊。
- 地表速度系数固定为石路 1.10、草地 1.00、泥地 0.85、坡地 0.80、浅水 0.70；所有恐龙使用同一比例。
- 环境物只影响部署、移动、绕行和站位，不得阻挡守卫或恐龙攻击，也不得显示掩体/LOS/防御加成 UI。
- 开局和 Home 复位显示完整岛屿与外围景观；约 55° RTS 斜俯视支持平移、滚轮缩放和中键拖动。
- 视觉采用暖色峡谷热带方向；现有 Dino Attack 美术为主体，已批准 Kenney CC0 资源只作为模块化补充。
- 具体 TDD 顺序、文件影响面和验收命令见 `docs/superpowers/plans/2026-08-28-dino-attack-open-tropical-battlefield.md`。

## 5. 子任务和执行顺序

任一时刻只允许一个任务处于 Active。每个任务完成后先更新 `docs/PROJECT_PLAN.md` 的证据和状态，再激活下一项。

### TASK-8.0：计划固化

**状态：** 已完成；用户于 2026-08-28 要求“执行阶段8”。
**允许修改：** 两份计划文档。
**产物：** 本文件和主计划阶段 8 展开内容。

完成条件：

- [x] 阶段范围、接口、任务顺序和禁止事项已写明。
- [x] 任务 8.1 的已完成范围和遗留问题已分开记录。
- [x] 每个后续任务都有输入、产物、验证和退出条件。
- [x] 正式训练改为同一 `Dinos.unity` 的 AI 模式；历史训练场景不进入正式主结果。
- [x] 已明确阶段 4–7 算法代码可复用、3DBall checkpoint 和 ReinFlow 数据不可直接复用。
- [x] 已记录双 Git 仓库边界、健康审计和安全维护规则。
- [x] 用户确认计划并要求开始执行阶段 8。

### TASK-8.1：历史已完成的训练场景接入

**状态：** 已完成（仅环境接入范围）。

已完成历史隔离训练场景、持续部署训练 API、固定 504 维 observation、6 维 Wait/Place 动作、项目专用 Unity MCP、训练专用 Player、EditMode 测试和 LLAPI 生命周期 smoke。此任务只证明训练接口可用；正式训练将迁移到同一 `Dinos.unity` 的 AI 模式。此任务不代表玩家主场景、正式环境冻结或算法比较已完成；遗留警告在 TASK-8.10 处理。

### TASK-8.2：玩家主场景只读审计与分析

**状态：** 已完成（2026-08-28）。

**目标：** 在不修改任何场景、资源、Prefab、ProjectSettings 或 Packages 的前提下，建立玩家场景和现有玩法的可审计基线。

**检查范围：** 项目结构和 Git dirty worktree；Unity 版本；`Dinos.unity` 层级；摄像机、灯光、材质、后处理；Setup/Running/Ending 状态机；恐龙选择、预览、放置和资源扣除；敌人生成、建筑、路径、胜负和重置；输入、UI、地图边界、NavMesh、碰撞层、可放置区域；可复用脚本、Prefab、材质和场景对象。优先使用项目专用 Unity MCP 和只读 Editor 查询；理解代码时若仓库存在 `.codegraph/`，先使用 CodeGraph。

必须输出以下 10 项分析报告：

1. 当前项目结构摘要；
2. 当前玩家场景和游戏流程；
3. 当前只能在开局放置的具体代码/状态原因；
4. 持续决策改造方案及最小切入点；
5. 当前地图的主要问题；
6. 地图改造选项及策略价值；
7. 可复用资源清单；
8. 需要新增或选择性导入的资源清单；
9. 可能影响的脚本、Prefab、场景和设置；
10. 风险、阻塞项和需要用户确认的事项。

**产物：** `reports/phase8/player-audit/` 下的分析报告、层级/组件/依赖清单、基线截图索引和只读检查命令记录。

**退出条件：** 10 项输出都有源码、场景或运行证据；明确 Setup-only 的真实调用链；未发生 Unity 写入、资源导入或自动保存；主计划记录证据和未决问题。

**完成证据：**

- 分析报告：`reports/phase8/player-audit/analysis.md`。
- 持久项目上下文：`reports/phase8/player-audit/UnityProjectContext.md`。
- 使用 Unity MCP 只读检查活动场景层级、组件、构建设置、渲染/质量/灯光/NavMesh/输入/层、Console 和当前画面；使用 CodeGraph 核对玩家输入、状态机、敌人、资源、墙体、NavMesh 和单位生命周期。
- 当前活动场景保持 `isDirty=false`，Editor 未进入 Play Mode；未写入 Unity 资源、ProjectSettings、Packages 或 Git index。

### TASK-8.3：玩家持续决策、地图、视觉与 UI 设计

**状态：** 已完成；旧玩家流程设计保留，地图部分已由新规格取代。

**目标：** 把 TASK-8.2 的事实转成可实施设计，并在任何 Unity 修改前取得用户确认。

必须输出以下 8 项设计：

11. 玩家持续决策流程；
12. Setup、Running、Ending 状态下的交互规则；
13. 恐龙放置、资源扣除、击杀/摧毁建筑后的食物返还流程；
14. 地图区域、主/支路线、瓶颈、战略节点和部署区设计；
15. UI、合法/非法预览、资源变化、威胁和回合反馈设计；
16. 兼容现有玩法且分离玩家控制与训练 Agent 的最小修改方案；
17. 场景备份、版本化产物和回滚方案；
18. 功能、视觉、NavMesh、性能和回归验收标准。

设计必须列出精确影响面、共享 API 边界、预期新增文件、是否需要额外美术资源、地图草图/节点表、UI 信息层级和分阶段测试。不得直接修改 `Dinos.unity`、Prefab 或运行时代码。

**退出条件：** 8 项设计完整、风险和取舍可评审；用户明确确认后才能激活 TASK-8.4。未确认时阶段停留在 8.3，不以“计划已写完”视为实现授权。

**设计产物：** `docs/superpowers/specs/2026-08-28-dino-attack-player-scene-design.md` 保留持续部署、共享 Deployment 服务、Human/AI 同场景与 Git 回滚设计；其三入口地图部分已被用户批准的 `docs/superpowers/specs/2026-08-28-dino-attack-open-tropical-battlefield-design.md` 取代。新地图实施计划为 `docs/superpowers/plans/2026-08-28-dino-attack-open-tropical-battlefield.md`。

### TASK-8.4：备份、Git 基线与美术资源隔离导入

**状态：** 已完成。

**目标：** 在首次 Unity 写入前建立可恢复基线，只导入已批准且实际需要的 CC0 资源。

执行步骤：

1. 保存外层和 `dino-attack` 的 Git HEAD、分支、status、目标场景/关键脚本 hash、Unity 版本、层级快照、基线截图和性能数据。
2. 对两个仓库的已有 dirty 内容按“用户既有修改 / 阶段 8 新修改 / 生成产物 / 不应入库”分类；不得擅自替用户提交既有修改。
3. 建立显式路径暂存和阶段 checkpoint 规则：外层只维护计划、`flow_rl`、小型报告摘要；内层只维护批准的 Unity 源文件和 `.meta`。每次提交前审查 `git diff --cached --stat` 与 `git diff --cached`，禁止批量暂存。
4. 通过 Unity Editor API 创建 `Dinos.unity` 的版本化备份；验证备份可被 Editor 打开，禁止手写复制后直接编辑 YAML。
5. 在例如 `Assets/ThirdParty/Kenney/` 的隔离目录建立 Mini Forest 与 Tower Defense 子目录。
6. 只复制设计批准清单中的 FBX 和必要贴图；保留 `License.txt`、来源路径、包版本和导入 manifest，不复制重复 GLB/OBJ/MTL 或整包未用资产。
7. 冻结模型 scale、材质映射、碰撞体、Static/NavMesh 标记、命名和 Prefab 包装规范。

**退出条件：** 备份、hash 和基线可复核；双仓库文件归属和提交规则明确；资源 provenance 完整；导入无 error、重名覆盖或大规模无用资产；尚未改变玩家玩法。若用户要求实际提交，只提交经审查的显式阶段文件，提交后两个仓库分别记录 commit ID；否则只保留可审计 diff，不自行提交。

**验证证据（2026-08-28）：** Unity Editor API 创建、打开并恢复 `Dinos.pre-stage8-20260828.unity`；原场景和备份 SHA-256 均为 `228FF0B286917F51750E73E6089E8E27CD0B446D828C0C1E81B548DAB6FFF45C`，恢复后原场景 `isDirty=false`。`reports/phase8/git-baseline/2026-08-28-pre-implementation.md` 记录两个仓库 HEAD/status/hash/文件归属；`Assets/ThirdParty/Kenney/import-manifest.json` 记录 CC0 来源和逐文件 hash。导入内容只有 10 个批准的 FBX、必要 colormap、license 和 manifest，检索不到 GLB/OBJ/MTL；清空基线日志后 Unity error 数为 0。用户未要求 commit，因此未创建 commit、未批量暂存。

### TASK-8.5：玩家持续部署与资源反馈

**状态：** 已完成。

**目标：** 用最小代码改动让玩家在 Setup 和 Running 阶段持续做 Wait/Place 决策，并保持 Ending 阶段安全锁定。

执行步骤：

1. 先为状态许可、资源扣除/返还、合法性结果和重置编写 EditMode/PlayMode 测试。
2. 抽取或复用共享部署服务，使玩家输入和训练 Agent 只调用公共验证/执行 API，不互相依赖。
3. 允许 Running 中选择、预览、确认、取消和等待；战斗继续推进。
4. 食物不足、越界、重叠、无 NavMesh、危险层和错误状态必须拒绝，并返回结构化失败原因。
5. 恐龙击杀敌人或摧毁建筑后的食物返还只结算一次，UI 与事件日志同步更新。
6. Ending/重置时清理预览、选择、事件订阅和临时对象。

**退出条件：** Setup/Running 可部署，Wait 可用，资源不足和非法位置拒绝且不扣费，食物返还正确，Ending 禁止部署，胜负和重置无回归；所有修改可由 TASK-8.4 备份和 Git diff 恢复。

**验证证据（2026-08-28）：** 新 Deployment 规则先红后绿，EditMode `22/22`；Play Mode 验证 Setup/Running 部署、精确扣费、Wait、InsufficientFood、OutsideBounds、Ending、Running 新恐龙 BehaviorGraph 激活、返还 UI 恢复、Defender/Wall 去重和完整回合重置，最终 Unity error 为 0。详细命令、数值和已知历史警告见 `reports/phase8/player-runtime/task-8.5-validation.md`。

### TASK-8.6：开阔热带战场灰盒、五区部署与 NavMesh 重做

**状态：** 已完成。

**目标：** 用五个始终开放的部署区、四区村落、中央广场、自由 NavMesh 和确定性防御预设取代旧四区/三路线灰盒，并先证明新空间结构可玩。

执行步骤：

1. 按独立地图计划 Task 1 刷新可恢复基线，并将旧 8.6–8.9 证据标记为被新验收取代的历史证据。
2. 按独立地图计划 Task 2–4 先以 TDD 建立五区、地表速度、镜头数学、防御预设和攻击不遮挡回归规则。
3. 按独立地图计划 Task 5 使用幂等 Editor builder 重建灰盒、烘焙 NavMesh，并验证三种恐龙从五区到中央广场的 15 个组合。
4. 不允许用固定三路、路线分隔脊、锁区、槽位或隐藏 waypoint 替代自由机动。
5. 保存新灰盒层级、区域 Bounds、路径长度、防御 preset、截图和 Console 证据。

**退出条件：** 五区/三恐龙共 15 个组合均 `PathComplete`，四个村落区和中央广场可进入，无北侧/雨林部署区、固定路线、卡死或隐藏强制导航；灰盒通过后才进入新 8.7。

**历史证据（已被新地图验收取代）：** `/World/Stage8 Strategy` 的 4 区、4 条分隔脊和 West/Center/East 路径验证仍保留在 `reports/phase8/player-runtime/task-8.6-validation.md`，但不证明新五区地图完成。

**新实施证据（Task 1）：** 已刷新 `reports/phase8/open-tropical-battlefield/baseline.md`，记录外层 101/内层 51 条 dirty 路径归属、`/World` 逐节点层级、1920×1080 Game/Scene 截图和 EditMode 性能；通过 Unity Editor API 创建 `Dinos.pre-open-tropical-20260828.unity`。原场景与备份 SHA-256 均为 `9A983C1840ACB7B4B988F85DFE9314CDBEE0D613C2B204E60E82AE943C161C20`，备份加法打开验证 15 roots/749 component slots/0 missing，EditMode 27/27，原场景恢复为 active/clean。任务级评审经两轮修复后通过。

**新实施证据（Task 2）：** 已建立纯规则程序集 `DinoAttack.Map.Core`，固化五个部署区 ID、五类地表精确倍率与单一优先级解析、可逆计数重叠栈、镜头边界数学和按 seed/round 确定的防御预设选择器。连接中的 Unity Editor 聚焦 EditMode 19/19、完整 EditMode 46/46，RED/GREEN JSON 位于 `reports/phase8/open-tropical-battlefield/task-2/`；独立规格/质量评审及 `.meta` GUID 窄复核均通过。

**新实施证据（Task 3）：** 已实现五区对象、显式 legacy fallback、服务级 exact-five/unique-ID fail-closed 校验、共享 zone lookup、部署期边界 presenter 与现有放置预览接入。两轮独立评审推动修复了 presenter/renderer 平行数组错配和错误 ID 集可通过的问题；最终聚焦 EditMode 34/34、完整 EditMode 65/65、recompile `up_to_date`、GREEN 后 Console 新错误 0。证据位于 `reports/phase8/open-tropical-battlefield/task-3/`；实际场景五区接线和 15 组合矩阵仍由 Task 5 关闭。

**新实施证据（Task 4）：** 已实现按 collider 身份计数、单一优先级解析且可逆恢复的恐龙地表速度控制器，并通过 Unity Prefab API 为三种 gameplay 恐龙各添加一个组件；NavMesh 基础速度保持 5/4/3.5。真实反射 harness 执行 Velociraptor 的 `AttackClosestObjectAction.OnStart/OnUpdate`，实体 blocker 位于中间时 `AttackEventChannel` 仍送达；未声称 health/effect 已在本任务完成。最终 PlayMode 17/17、EditMode 65/65、GREEN 后 Console 新错误 0，独立评审通过；物理遍历比率留待 Task 5/8 的场景地表条带。

**新实施证据（Task 5）：** 已完成 2–2–1 五区灰盒、四个可进入聚落区、中央广场、+30.56% 有效边界、两套确定性防御和项目本地安全墙升级链。三轮独立审查推动修复旧 waypoint、死墙跨预设维修、升级/事件链、历史诊断 fallback、双 preset 导航与 HealthBar 生命周期；最终 Preset 0/1 各有三种恐龙 15/15、实际守卫槽 3/3，三 Prefab×五地表真实 4 m 移动最大误差 0.3507%。EditMode 73/73、PlayMode 20/20、recompile up to date、fresh Console 0，`Dinos.unity` active/stopped/clean，SHA-256 `2DFF1135F5161BB824B5D44BB380DAD467A474C752BD65857BC5A6C6B9B47D8C`；独立最终复审 PASS。证据位于 `reports/phase8/open-tropical-battlefield/task-5/`。

### TASK-8.7：地表、山体、环境美术、灯光与后处理

**状态：** 已完成；Task 7 最终只读复审 PASS。

**目标：** 在不改变新五区灰盒语义的前提下，实现五类地表速度、完整岛屿镜头和暖色峡谷热带视觉。

执行步骤：

1. 执行独立地图计划 Task 4 的速度系统和精确倍率测试。
2. 执行 Task 6 的 RTS 镜头与完整岛屿四分辨率构图验证。
3. 执行 Task 7 的暖色峡谷热带美术、五区地标、村落细节与 Kenney manifest 审计。
4. 在相同分辨率、质量、Play 状态、镜头和采样窗口下比较性能；不可比数据不得宣称无回退。

**退出条件：** 五类地表视觉与判定一致，速度倍率实测通过，完整岛屿镜头通过四种分辨率，暖色峡谷热带风格一致，NavMesh/碰撞/摄像机无退化，且无粉色材质、丢失引用或不可解释性能回退。

**历史证据（已被新地图验收取代）：** 旧视觉的 1280×720 截图和性能快照保留在 `reports/phase8/player-runtime/task-8.7-validation.md`，仅作为新旧对比基线。

**新实施证据（独立地图 Task 6，2026-08-29 最终 scoped review PASS）：** 已实现完整岛屿 RTS 镜头的边缘/方向键平移、滚轮缩放、中键世界平面拖动与 Home 精确复位，并保持 `55.01°` 透视斜俯视。三个鼠标路径统一要求应用焦点与实际 `OutputCamera.pixelRect`/对应 display，使用 exclusive 上界，失焦或越界会取消活动拖拽且重入不跳变；真实 Input System 已覆盖 edge、outside 和 unfocused 状态。聚焦 `OpenTropicalBattlefieldSceneTests` `11/11`、full EditMode `81/81`、full PlayMode `26/26`、recompile 0 error、cursor `6103` 后 fresh Console 0。连续两次 canonical `Build(true)` 的 before/after/after SHA-256 均为 `81EA6DB7DC96EFE464BDC78A3CD6E0E9DAC109DC745ADFB6A994CBBE785823E8`，场景 byte-stable 且 NavMeshData 引用在同一 Editor 会话内不变。证据位于 `reports/phase8/open-tropical-battlefield/task-6/`；Task 6 已完成。

### TASK-8.8：玩家 UI 与交互反馈

**状态：** 已完成；Task 7 最终只读复审 PASS。

**目标：** 在保留现有持续部署 HUD 的基础上，实现“自然地标常驻、部署时显界”的五区反馈，并显示区域、地表和速度倍率。

执行步骤：

1. 未选择恐龙时仅显示自然地标；选择后五区边界低强度显示，悬停只高亮一个区域。
2. 显示区域名、地表名、速度倍率和结构化非法原因；不能只用红绿颜色。
3. 删除或禁止掩体、LOS、攻击受阻和防御加成反馈。
4. 保留食物、阶段、Threat、恐龙成本、Start、Ending 和重置功能。
5. 按独立地图计划 Task 7 验证四种分辨率、安全区、UI picking 和订阅清理。

**退出条件：** Setup/Running/Ending 信息清晰；资源不足和非法位置反馈可辨；UI 缩放、输入和重置正常；无重复 UI、遮挡或异常日志。

**历史证据（继续作为功能基线）：** 现有 `status-panel`、Food/Threat/阶段功能和 27 项 EditMode 证据保留在 `reports/phase8/player-runtime/task-8.8-validation.md`；当时新五区反馈尚未实现，现由下方 Task 7 证据取代该缺口。

**新实施证据（独立地图 Task 7，最终只读复审 PASS）：** 已新增五区自然地标、部署时边界、悬停区域/地表/精确倍率，以及 Labrada 可见的 ASCII `[!]` 加文字非法原因；没有掩体、LOS、blocked-shot、防御加成或路线标签。真实 `Dinos` PlayMode 生命周期 `1/1` 证明 RuntimeUI 与 PlaceDinoVisualization 共用同一 presenter、无选择/选择后五区显隐、Z3 hover、非法 `[!]` 加文字，以及 Ending 清两条 HUD/预览/SafeZone/全部边界。场景美术使用七个项目侧 URP/Lit 材质，Art Pass 为 42 renderer/0 collider；active CC0 manifest 8/8 hash、每项有 active MeshFilter、0 重复格式。四比例 composited 的 `[!]` 像素前缀为 135/243/269/304。Task 7 证据位于 `reports/phase8/open-tropical-battlefield/task-7/`；Task 8/TASK-8.9 的最终证据见下。

### TASK-8.9：玩家场景综合验证与回归修复

**状态：** 已完成（2026-08-29）。

**目标：** 在重新同步训练环境前，完整证明玩家场景改造可玩、稳定且可恢复。

至少验证：

- Setup 和 Running 均可部署；Wait、取消、资源不足和非法位置行为正确；
- 击杀敌人或摧毁建筑后食物正确返还且不重复；
- 胜利、失败、Ending、重新开始和场景重置正常；
- 主要路线、战略区域、NavMesh、碰撞、边界、摄像机、灯光、后处理和 UI 正常；
- 长运行无 exception、NaN、重复对象、订阅/资源泄漏或持续增长的临时对象；
- 原有玩家核心功能未被破坏，ProjectSettings、Packages 和既有训练结果保持受保护状态。

**产物：** `reports/phase8/player-validation/` 下的测试矩阵、日志、前后截图、性能对比、修改清单、已知问题和回滚说明。

**退出条件：** 所有硬性玩家验收项通过；未通过项必须保留证据并阻塞 TASK-8.10，不能以训练工作绕过玩家场景回归。

**新验收范围：** 执行独立地图计划 Task 8，覆盖 15 个恐龙/区域组合、五类地表速度、确定性防御、攻击不遮挡、完整岛屿镜头、四分辨率、完整玩家矩阵、至少 20 回合/episode 长运行和冻结 manifest。

**历史证据（不足以通过新验收）：** 原 Setup→Running、HUD/Threat、Reload smoke 和 27 项 EditMode 结果保留在 `reports/phase8/player-runtime/task-8.9-validation.md`，但旧状态 `Ready with limitations` 已被新地图范围取代。

**最终证据：** full EditMode `85/85`、full PlayMode `28/28`、fresh Console error `0`；20 次真实 `Dinos` 重载 episode 为 10 胜/10 负，五区各使用 4 次，完整覆盖 `Setup -> Running -> Ending -> Scoring -> EnemyRepairs -> Ended -> Setup`、真实胜负/下一回合 UI、存活恐龙全额返还、死亡不返还和 next round。逐轮记录对象、订阅、材质、速度、NavMesh、managed memory 与 Console，订阅恒为 4，NavMesh 恒为 2548/3414，Console error 0，观察到的 managed 增长 2,301,952 bytes。Windows64 build `build_6bc19e48b9a2` 0 error；H0/H1/H2 均为 `DF751103EF721897F5F753CB1AA0EAA7758A2A52B56AE2A6D3935504B86173E5`。证据：`reports/phase8/open-tropical-battlefield/validation.md`、`manifest.json`、`longevity-20-episodes.json`。

### TASK-8.10：同场景 AI 控制接入、环境警告审计与版本冻结

**状态：** 已完成（2026-08-29）；环境已冻结并 handoff 至 TASK-8.11。

**目标：** 在改造后的同一 `Dinos.unity` 中加入 AI 控制模式，使玩家模式和训练模式只更换决策者，并在正式训练前冻结可复现版本。

执行步骤：

1. 为 `Dinos.unity` 定义显式 `Human` / `AI` 启动模式；Human 启用玩家输入和 UI，AI 禁用玩家部署输入并启用 ML-Agents Agent，二者不能同时提交部署动作。
2. 玩家和 AI 调用相同的部署、扣费、返还、合法性、状态机、战斗和胜负服务；新增契约测试证明相同输入产生相同游戏结果。
3. 正式训练 Player 直接包含 `Dinos.unity`，通过受测的启动参数或 bootstrap 进入 AI 模式；不得维护第二份正式地图。历史 `DinoAttackTraining.unity` 只保留为接入证据或诊断夹具，不进入正式比较。
4. 用 CodeGraph 追踪 waypoint、NavMeshAgent 创建、unit cleanup 和场景重载调用链，分类并修复会污染训练的警告。
5. 固定动作触发 Wait-only、合法/非法部署、超时、胜利、失败和重载；运行至少 20 episode 或等价长 smoke。
6. 构建版本化 AI Player，不覆盖已验证旧 build；保存 Unity、`Dinos.unity`、Player、Packages lock、关键脚本、配置和行为规格 hash。

**最终冻结证据：** Windows64 build 0 error；486 warnings 严格分类为 485 条 Unity AI Inference/Sentis shader warning 与 1 条未配置可选 RuntimePipelineManager/Pipeline disabled。真实图形 D3D11 Player 稳定运行 12 秒，加载 Dinos 并执行开局防御 turn，经标准 `WM_CLOSE` 正常 exit code 0；日志无 Crash/Exception/NullReference/MissingReference/Assert/NaN/NavMesh/waypoint/armor/error/failed。manifest 已补齐 2 个防御 Prefab、2 个 UnitSO 和 7 个 active 材质 SHA-256。证据见 `reports/phase8/open-tropical-battlefield/task-8.10-final-freeze.md`、`task-8.10-player-smoke.log` 和 `manifest.json`。

**退出条件：** Human 与 AI 模式加载的是同一个 `Dinos.unity` hash，地图和玩法对象一致且控制器互斥；Player log 无 exception、NullReference、NaN、communicator error 或不可恢复 NavMesh error；Agent ID、终止/截断和端口释放正常。原计划路径 `reports/phase8/environment-freeze/{manifest.json,validation.md}` 已由唯一权威产物 `reports/phase8/open-tropical-battlefield/{manifest.json,validation.md,task-8.10-final-freeze.md,task-8.10-player-smoke.log}` 取代；前者只保留非重复指针说明。若必须修改 ProjectSettings/Packages，停止并请求授权。

### TASK-8.11：环境协议、奖励和轨迹正确性

**目标：** 证明训练数据准确表达持续游戏回合，而不只是 LLAPI 可通信。

执行步骤：为 504 维 observation 生成字段 schema 与切片测试；覆盖 Wait、三种恐龙、坐标边界、资源不足、重叠、越界和无 NavMesh；验证扣费、`floor(cost / 2)` 返还和 reward 事件；构造胜利、失败、时间截断和决策步截断；验证 `TerminalSteps.interrupted`、GAE bootstrap、新 Agent ID、episode summary 和诊断指标。

**退出条件：** shape、dtype、有限值、边界、固定 seed、Agent ID、terminal/truncated 和 reward 分解测试通过；保存胜利、失败和截断的完整轨迹；明确环境步、决策步和仿真秒关系。

### TASK-8.12：Gaussian PPO 胜率基线

**目标：** 验证 observation、reward 和决策节奏能被标准 on-policy 方法学习，并冻结正式算法预算。

新增 Dino Attack 专用配置；完成 CPU/CUDA 单更新、save/resume 和 evaluator；先做小预算学习性检查，再运行 seed 0/1/2。若胜率和回报在学习性检查中没有可重复的上升趋势，暂停 Flow 正式训练并审查环境、奖励、容量和有效动作密度。报告未训练初始化表现、最终 checkpoint、搜索次数、失败 seed 和完整成本；未训练初始化仅作为训练诊断，不建立独立非学习基线任务。

### TASK-8.13：FPO

**目标：** 在相同环境、预算和评估 seed 下评估 Flow Matching PPO-style 更新。

先在固定 `fpo/` commit 中使用 CodeGraph 复核核心实现；复用阶段 5 模块，只增加 Dino Attack 配置和有证据的容量调整；完成数值测试、短 smoke、resume、三个正式 seed 和 NFE 1/2/4/8 受控消融。所有 loss、ratio、KL、gradient 和动作必须有限，并报告胜率、样本效率、延迟和相对 PPO 的实际收益。

### TASK-8.14：ReinFlow 与 PolicyFlow

**ReinFlow：** 以达标 PPO 的 Dino Attack 成功轨迹建立冻结数据集；如果以后加入玩家示范，必须单独记录来源、占比和采集协议，不能与 PPO 数据静默混合。先训练并评估 Flow BC，未达到预设胜率门槛不启动正式微调；再做 critic warmup、noise、entropy 最小消融和三个 seed 在线微调。预训练成本与在线成本分开，微调不得低于同一 BC checkpoint。

**PolicyFlow：** 仅在成功轨迹存在预先定义且可量化的多峰部署模式时做优势评估；模式可由路线、远近部署、类型组合或部署时序定义，判据必须训练前冻结。先在固定官方 commit 中用 CodeGraph 复核 velocity variation ratio 和 Brownian entropy，再复用阶段 7 修正后的 snapshot、restart boundary 与 checkpoint schema，完成 smoke、三个 seed、模式覆盖评估和 NFE 消融。若成功解单峰，只作为成本/稳定性对照，不宣称多峰优势。

### TASK-8.15：统一评估、鲁棒性与交付

**目标：** 形成可重算、可复查和可复现的阶段结论。

评估矩阵至少包含 PPO、FPO、ReinFlow BC、ReinFlow、PolicyFlow；所有正式主结果使用与玩家相同的冻结 `Dinos.unity`；训练 seed 0/1/2 和固定 evaluation seed 集。open、dual-lane、bottleneck 等历史或派生变体只能作为明确标记的附加鲁棒性实验，不能替代同场景主结果。当前固定 Hard 难度为初始训练协议，其他难度只作为独立 OOD 结果，除非另有决策。

产物包括 `reports/phase8/comparison.json`、`validation.md`、`reproduction.md`、正式 run 的 config/summary/metrics/checkpoint provenance/Unity log，以及失败实验与排除原因。报告每 seed 原始值、mean/std、胜率区间、环境步、wall-clock、NFE、延迟、显存和预训练成本，并明确回答胜率、稳定性、多峰覆盖和计算代价差异。

## 6. 统一实验协议

### 6.1 公平性固定项

- 同一比较组使用完全相同的 Unity Player hash、场景变体分布和奖励配置。
- 同一比较组使用相同 environment-step 预算和 evaluation seed。
- 训练 seed 固定为 0、1、2；任何额外 seed 必须完整报告。
- 网络容量尽量对齐；结构差异必须记录参数量和原因。
- observation normalizer 不得在评估中更新。
- ReinFlow 的数据收集和 BC 计算不计入在线 environment steps，但必须单独报告。
- PolicyFlow/FPO 的 NFE 以实际 velocity-field 调用次数统计。
- checkpoint 只在完整 rollout/update 边界保存；Unity 重启不声称恢复物理 tick。

### 6.2 分阶段预算

正式数字在 TASK-8.12 的 PPO 学习性检查后冻结。在此之前使用以下预算类别，不提前承诺具体步数：

- 单元/合成测试：不启动长训练；
- LLAPI smoke：数百至数千 decision steps；
- 算法 smoke：至少产生 1–2 次真实更新；
- 学习性检查：足以判断胜率是否上升，但不用于最终结论；
- 正式训练：三个 seed、固定预算；
- 独立评估：每 checkpoint 固定回合数和 seed 集。

如果根据吞吐直接沿用 3DBall 的 500k 步会导致明显不足或浪费，必须在决策记录中说明 Dino Attack 的 episode 长度、有效部署频率和预算换算依据。

## 7. 测试矩阵

### Unity EditMode/PlayMode

- action codec shape、阈值、argmax、有限性和边界；
- observation schema、槽位排序、零填充、裁剪计数；
- 合法位置、越界、重叠、危险层、NavMesh；
- 资源扣除与返还；
- reward 分量；
- Setup→Running 和 Running 中继续部署；
- 胜利、失败、时间截断、决策截断和 reset；
- 同一 `Dinos.unity` 的地图、敌人、建筑、路径、资源和战斗初始条件固定 seed 复现；派生变体单独标记为附加实验。
- 同一 `Dinos.unity` 的 Human/AI 模式对象、地图、资源和战斗规则一致，且控制器互斥。

### Python 单元/集成

- behavior 基础名与 `?team=0` 解析；
- DecisionSteps 顺序和 Agent ID 替换；
- terminal/truncated 与 GAE；
- 空 Decision batch；
- 动态 Agent ID retirement；
- action shape/dtype/range；
- episode summary、胜率、事件指标和 checkpoint schema；
- 四种算法单更新数值有限性；
- resume 后 schedule、optimizer、RNG 和 normalizer 恢复。

### 长运行检查

- Player/端口释放；
- Unity log 的 exception/error/NaN 扫描；
- 至少一次跨多 episode 场景重载；
- checkpoint 写入中断后不损坏已有 checkpoint；
- 运行目录拒绝静默覆盖。

## 8. 风险与停止条件

| 风险 | 观察信号 | 处理或停止条件 |
|---|---|---|
| 场景备份或回滚不可验证 | 备份无法独立打开、hash/依赖不完整 | 禁止进入首次场景修改；先修复版本化备份和恢复演练 |
| Kenney 资源整包导入或风格混杂 | 项目体积激增、重复格式、材质/比例不一致 | 停止导入，只保留审批清单中的 FBX/贴图并统一导入规范 |
| 地图改造破坏 NavMesh/路线 | 单位卡死、目标不可达、waypoint 报警 | 阻塞美术和训练同步，回到灰盒阶段修复路线与烘焙 |
| 高差/装饰遮挡摄像机 | 战斗、部署预览或关键节点不可见 | 降低轮廓、调整构图或移除装饰；不得用 UI 掩盖可视性问题 |
| UI 缩放或输入拦截 | 不同分辨率错位、点击落在 UI、重复 EventSystem | 阻塞玩家验收，修复 Canvas/input/event 生命周期 |
| 玩家与训练语义分叉 | 扣费、返还、合法性或状态规则结果不同 | 抽回共享服务并增加双路径契约测试，未统一前不冻结训练环境 |
| 玩家与训练场景漂移 | 两个 scene hash、层级或地图对象不同 | 正式训练改为同一 `Dinos.unity` 的 AI 模式；独立训练场景只作历史诊断，不进入主结果 |
| 视觉改造导致性能回退 | 帧时间、draw call、阴影或内存显著增长 | 使用基线预算削减材质、阴影、碰撞体或装饰密度 |
| 现有资源不足 | 缺少满足已批准视觉方案的模型、纹理或 UI 元素 | 列出精确资源需求和格式，由用户提供或另行批准来源后再继续 |
| 奖励泄漏或 shaping 主导 | 高 return 但低胜率 | 暂停训练，重审 reward；不调算法掩盖环境问题 |
| 非法动作过多 | 策略长期在无效位置部署 | 检查 observation/合法性，比较连续投影与候选点方案；架构变更需决策记录 |
| 场景重载不稳定 | Agent ID 永久缺失、端口泄漏、异常日志 | 阻塞 8.11 以后任务，先修复环境生命周期 |
| PPO 无法学习 | 多个 seed 的胜率/回报无上升趋势或持续接近零 | 暂停 Flow 正式训练，检查奖励、时间尺度、网络和有效动作密度 |
| ReinFlow 数据质量不足 | BC 胜率低或模式单一 | 改善数据源，不以在线微调替代失败的预训练 |
| PolicyFlow 无可量化多峰性 | 成功轨迹集中为单一模式 | 仅作成本对照，不宣称多峰收益 |
| 计算预算不可控 | 单 seed 超出预计 wall-clock/GPU 预算 | 保存 checkpoint，记录吞吐，等待用户确认新预算 |
| 需要修改 ProjectSettings/Packages 或覆盖原资产 | 现有能力无法实现已批准设计 | 标记阻塞，列出精确文件、必要性和替代方案，等待单独授权 |

## 9. 状态推进规则

1. **历史推进规则（已完成）：** 独立地图 Task 7 当时需先完成只读 re-review，随后才进入 8.9/8.10；现 Task 7、Task 8/TASK-8.9 和 TASK-8.10 均已 PASS，当前唯一活动任务为 TASK-8.11。
2. 每个任务开始前重新读取 `AGENTS.md`、`docs/PROJECT_PLAN.md` 和本文件。
3. 任务开始前检查 dirty worktree，已有修改全部视为用户工作。
4. 代码任务测试先行；实验任务先冻结配置和输出目录。
5. 每个任务结束后立即写回命令、结果、产物和阻塞项。
6. 验收失败时保留诊断产物，不覆盖成功证据，不自动进入下一任务。
7. 任何目标、架构、依赖、预算或验收标准变更先更新决策记录。
8. 每个实现任务分别维护外层和 `dino-attack` Git 状态；只暂存本任务显式文件。用户未明确要求提交时，不自动创建 commit；用户要求提交时先审查 staged diff，再分别提交到所属仓库。

## 10. 本轮计划任务的验收

**历史本轮验收说明（已取代）：** 当时阶段 8 仅完成至重做 TASK-8.6，并计划执行 TASK-8.7；现重做后的 TASK-8.6–8.10 均已完成，冻结证据已通过，TASK-8.11 为唯一活动任务。

```powershell
Get-Content -Raw -Encoding UTF8 docs\PROJECT_PLAN.md
Get-Content -Raw -Encoding UTF8 docs\superpowers\plans\2026-08-28-phase-8-dino-attack.md
Get-Content -Raw -Encoding UTF8 docs\superpowers\plans\2026-08-28-dino-attack-open-tropical-battlefield.md
git diff --check -- docs\PROJECT_PLAN.md docs\superpowers\plans\2026-08-28-phase-8-dino-attack.md docs\superpowers\plans\2026-08-28-dino-attack-open-tropical-battlefield.md
git fsck --connectivity-only --no-dangling
git -C dino-attack fsck --connectivity-only --no-dangling
```

独立地图计划 Task 7、Task 8/TASK-8.9 和 TASK-8.10 最终冻结均 PASS；TASK-8.11 现为唯一活动任务。
