# Unity Flow-RL 项目实施计划

> 本文件是项目范围、当前状态、阶段门槛和关键决策的唯一事实来源。详细设计、逐步实施记录与原始验证数据分别保存在 `docs/superpowers/` 和 `reports/`，不在本文件重复展开。

## 项目概要

**目标：** 将 `dino-attack` 建设为支持持续部署、资源循环、清晰反馈和多地图策略的完整游戏；玩家与 AI 使用相同场景和规则，仅替换决策控制器。在统一 ML-Agents 基础设施上公平比较 Gaussian PPO、FPO、ReinFlow 和 PolicyFlow。

**架构：** Unity 负责物理、观测、奖励和 episode 生命周期；独立 Python 包 `flow_rl` 通过 ML-Agents Low-Level API 驱动环境。四种算法共享环境适配、rollout、GAE、critic、日志和评估协议，算法专用的概率模型、ratio 与正则项保持独立。

**主要版本：**

- 3DBall：Unity `6000.0.72f1`。
- Dino Attack：Unity `6000.3.22f1`，Editor 为 `D:\job\6000.3.22f1\Editor\Unity.exe`。
- ML-Agents：`release_23`；Python `3.10`；PyTorch `2.2.1+cu121`。
- 当前正式 Dino 协议：`dino_attack_structured_set_v2`。

## 当前状态

**当前里程碑：阶段 8，进行中。** 阶段 0–7 已完成；阶段 9–10 尚未开始。

### 地图四攻击伤害数字完成（2026-09-11）

- `TASK-8.11-MAP4-DAMAGE`已完成：玩家法杖显示金色扣血数字，Q范围技能为每个实际受伤目标显示紫色数字；显示实际减少的生命值，上浮淡出，击杀后仍保留反馈。保持原伤害、遮挡、冷却、AI、小地图与默认0.5倍速。
- EditMode20/20、PlayMode10/10通过，实际普攻/范围技能/残血击杀画面已查看。新构建`Builds/DinoAttack-Map4-DamageNumbers-Windows-20260911/DinoAttack.exe`及10秒启动检查成功；1337基线文件中6项预期变化、其余1331项字节一致，新增4个源码/meta文件。完整人工整局未验，证据`reports/phase8/map4-damage-numbers-20260911/README.md`。

### 地图四小地图完成（2026-09-11）

- `TASK-8.11-MAP4-MINIMAP`已完成：右上角固定北向小地图，显示实际部署区域、村庄目标/房屋/城墙/守卫、玩家位置/朝向与来袭敌人；支持倒地观战、结算隐藏和重试重建。保留地图、AI、人物动作/技能与默认0.5倍速。
- EditMode20/20、PlayMode9/9通过，三种分辨率实际渲染已检查，新构建`Builds/DinoAttack-Map4-Minimap-Windows-20260911/DinoAttack.exe`及10秒启动检查成功。1331基线文件中仅3项预期变化，其余1328项（含四地图场景、设置和包）保持字节一致；完整人工整局未验，科研阶段状态不变。证据`reports/phase8/map4-minimap-20260911/README.md`。

### 地图四动作修复（2026-09-11）

- 用户反馈行走与攻击怪异后，新增`TASK-8.11-MAP4-ANIM`：以连续动作和骨骼位移复现问题，替换为Unity官方女性行走/跑步/冲刺并校准步频，身体朝移动方向转身、撞墙停步；右臂单手直立持杖，0.38秒施法替换反复被0.45秒攻击打断的2.3667秒循环动作。
- 定向EditMode20/20、PlayMode8/8通过，三AI、技能、倍速和地图流程回归通过；额外最终Editor源码复验3/3通过。新Windows构建与10秒启动检查成功：`Builds/DinoAttack-Map4-AnimationFix-Windows-20260911/DinoAttack.exe`。实际连续步态/连射/移动施法及完整HUD渲染已查看；完整人工整局仍未验证。任务已完成，285个基线文件中仅8个预期文件改变，地图1–3、项目设置及其余277文件与任务前一致。详见`reports/phase8/map4-animation-fix-20260911/README.md`。

### 地图四第三人称星光守卫完成（2026-09-11）

- `TASK-8.11-MAP4-TP`已完成：接入VRoid二次元女性骨骼模型、第三人称跟随镜头、法杖与Q范围法术/E护盾/R闪避；新增0.5/1/2倍设置，地图四默认0.5倍，其他地图默认1倍。原布防、AI模型和胜负流程保持，地图1–3及项目设置SHA-256与任务前一致。
- 定向EditMode17/17、PlayMode8/8通过，实际角色正面、第三人称完整HUD、行走和护盾渲染已查看。新Windows构建 `Builds/DinoAttack-Map4-Anime-Windows-20260911/DinoAttack.exe` 成功，235个构建文件校验记录齐全，10秒启动有响应且关键错误0。完整人工游玩与正常窗口退出尚未验证，历史全量测试门及阶段8科研状态不变。操作和证据见 `reports/phase8/map4-anime-third-person-20260910/README.md`。

### 地图四第一视角守卫完成（2026-09-10，模型和视角已被上述任务取代）

- `TASK-8.11-MAP4`已完成本轮游戏扩展：复制地图三为地图四，新增Kenney角色模型、第一视角键鼠控制和远程法杖；地图一至三场景SHA-256与修改前一致。沿用布防、三种已安装AI、经济和胜负规则，人物倒地后观战。
- Unity定向EditMode17/17、最终PlayMode7/7通过；Windows新构建及10秒启动检查通过，产物 `Builds/DinoAttack-Map4-Windows-20260910/DinoAttack.exe`。尚未人工完整游玩，隐藏窗口正常退出未验证；历史科研与全量Unity测试门不变。详见 `reports/phase8/map4-first-person-20260910/README.md`。

### 共享训练维护完成（2026-09-10）

- `TASK-8.11-QUALITY`已完成：八类训练配置的非有限数值校验、Dino逐update/逐地图胜率统计与写入前回合校验、历史配置断言及测试cwd依赖已修复。根目录与`flow_rl/`各433 passed、3 deselected，均保留54项既有warning；证据见`reports/phase8/project-improvements-20260910/README.md`。
- 本次只完成Python共享基础设施维护。PPO/FPO完成状态、PolicyFlow后续暂停在`step-3196659`且部署仍为`step-2147452`、v0.1.0已发布等沿用后续历史记录；下方9月5日13:03及9月3日看板是历史快照，不作为新的训练启动指令。严格确定性、ReinFlow Dino微调、统一比较与人工验收仍未完成，阶段8状态不变。

### 历史完成情况复核（2026-09-05 13:03）

- PPO FixedClockV5 seed0 已完成4,721,698 steps / 288 updates，final/summary存在；教师冻结deterministic评估35/40（87.5%）。严格环境确定性和统一比较验收仍未完成，不能把运行完成等同阶段8完成。
- FPO warm-start长期seed0已完成4,721,708 steps / 288 updates，final/summary存在，已非“进程仍运行”；长期崩塌限制保留。update160行为checkpoint的独立AI Strategy冻结两模式均31/40，已接入。
- PolicyFlow成功数据、结构化Flow BC、16-env实现与smoke均已完成；原正式a2最新完整记录836,064 steps / 51 updates（17.7%）。新`resume-step836064-seed0-20260905-a1`已启动，13:03尚无首个完整update及final/summary，恢复验收待证据。
- ReinFlow Dino在线微调、阶段8统一报告、地图/中断/构建指纹等退出门仍未完成；阶段9–10保持未开始。PPO/FPO实际合同以effective config与后续决策为准，下方20-env/1,572,864等旧参数摘要不作为新启动授权。
- 本次Python非集成回归：项目根目录运行` .conda/flow-rl/python.exe -m pytest -q flow_rl/tests -m 'not integration' -p no:cacheprovider --disable-warnings --tb=short --junitxml=reports/phase8/completion-audit-20260905/python-tests.xml`，372 passed、1 failed、3 deselected；失败为已记录的20-env断言与实际16-env配置不一致。另确认Flow BC测试依赖根目录cwd。未改代码或训练进程，未重跑Unity。
- Unity历史集成XML核对为EditMode25/25、PlayMode46/59，13项既有失败保持披露，不代表当前dirty检出全量通过。详细范围、证据与剩余出口：`reports/phase8/completion-audit-20260905/README.md`。阶段状态与未验证复选框不变。

### 执行摘要（2026-09-03）

- 玩家场景、双地图、Human/TrainingAI/InferenceAI 控制切换、六流 v2 观测、4D 动作、正式奖励、并行 PPO/FPO 训练基础设施均已落地。
- 旧 PPO checkpoint 已确认不能用于正式部署：其训练进程被错误接入只允许 Human/InferenceAI 的结果快照链，且冻结评估无法复现训练曲线。旧模型和报告保留，但均不具备 eligible baseline 资格。
- 生命周期与推理非法动作反馈已修复；runtimefix 24-env smoke 完成 `4,118` transitions、24/24 worker、54 个自然终止、0 truncated，24 份日志中相关异常为 0。
- 双地图随机策略自然生命周期矩阵确认：12/12 episode 均到达 `Ended`，但地图1 InferenceAI 终局布局签名依赖存活防御对象，3 个 seed 中 2 个无法冻结结果；地图2为 3/3 正常。该缺陷修复前不得认为游戏推理结算生命周期完整。
- 旧的 24-env runtimefix PPO seed0 已暂停在 `147,577` transitions / 9 updates；`step-131189.pt` 保留为历史审计产物，但恢复安排已被新的 8-env 从头训练决定取代。
- post-victory-guard PPO seed0 8-env run 已按用户决定停止并由 4-env 从头训练取代；旧 run 保留到 `540,843` steps / 33 updates，最后完整 checkpoint 为 `step-524455.pt`，且因 Unity `MoveToTargetObjectAction` 空目标异常不具备直接部署资格。
- 新 4-env seed0 run 已按用户要求停止：最终记录 `147,466` steps / 9 updates，最后完整 checkpoint 为 `step-131080.pt`；Python 与 4 个 Player 均已退出，未生成 final/summary，不能视为完成或用于默认 AI。
- 暂停训练的冷启动胜率异常偏高：首个 update 已为 map1 `72.36%`、map2 `57.14%`。审计确认训练使用 13:05 新构建 Player，Python 未误标失败，1,348 条 Unity 胜利均为存活已登记 Dino 触发 `TargetRadiusEntered`；当前阻塞是确认“恐龙进入目标范围即胜利”的现行语义是否仍符合用户意图，以及是否需要提高玩法难度。该 checkpoint 在结论前不得恢复或参与选模。
- FPO FixedClockV5 seed0 原始 run 已按用户要求在 update 156 后中断并保留；从崩塌前权重完成的25-update探针只是在结束前尚未越过失控点，其 update24/25 已出现KL预警。从曲线候选`step-377087.pt`重置optimizer启动的长期run在update45–46再次真实崩塌：成功率77.9%→57.7%→3.0%，proxy KL升至0.102/0.111，两图同时转为超时；16/16环境在线、generation0、truncated0、时钟和调度无跳变。当前训练仍在运行但不再具备稳定候选资格；最后一个在线高点policy version43对应`step-705001.pt`，需冻结评估。
- PolicyFlow阶段8训练准备审计已完成：官方commit和阶段7数学回归有效，但当前只有单Player扁平3DBall trainer；Dino成功轨迹、结构化Flow BC、Set Transformer PolicyFlow、16-env版本屏障和冻结evaluator均未实现，且没有合格PPO数据源。已建立fail-closed实施计划与16-env单rollout参数草案，依赖满足前不创建正式YAML或启动Player。
- 本轮 PPO 只运行 seed 0，不启动 seed 1/2；FPO seed 2 也按既有决定不运行。单 seed PPO 只能作为用户批准的单种子结果报告，不满足历史多种子退出门。
- Open Tropical 已改为每个 episode 通过同一 NavMesh 就绪门后才进入 `Running`，普通目标移动改用完整路径提交与目标感知到达判定，正式场景固定使用预烘焙 NavMesh（runtime build 计数为 0）。0/75 ms deterministic 对照由旧版 `806/863` 收敛为 `1652/1654` environment steps，20 组中 19 组业务终局完全一致；首个地图1 seed 仍发生胜利/超时分叉，故确定性门仍未通过，PPO 保持停止。
- 已纠正训练 capture clock 的双配置源与覆盖问题：Unity 删除固定 `TrainingTimeScale=20`，读取 Python 实际 `time_scale`，并以持久 `captureFramerate` 驱动 `captureDeltaTime`。FixedClockV5 的 4-env 真实 smoke 中四份日志各仅一次确认 `time_scale=5, captureFramerate=250, captureDeltaTime=0.004`，67 transitions、1 次有限更新、关键 Unity 异常 0；下一轮 seed0 已设计为 20-env、rollout `20,480`，正式 PPO 尚未启动。
- 当前默认 AI Strategy 的“开局长时间不决策”已确认是视觉静默而非 scheduler 停止：Editor 日志记录持续推理后的 `InsufficientFood`、`Overlap`、`UnitCapReached` 累计拒绝，其中一局累计至少30次拒绝且最终 `active_dinos=0` 超时；日志计数是拒绝累计值而非全局决策序号，100食物购买成本100的T-Rex边界正常。当前确定性 `tanh_mean` checkpoint 尚未通过冻结资格门。
- 已审计当前默认 `step-1312449` AI Strategy 与其训练模拟的过程一致性：ONNX provenance 与 checkpoint 匹配，观察/动作 codec/25-step 决策入口共享；但训练是 Python+16 Player+PyTorch 随机采样，AI Strategy 是 Unity 内 Sentis `tanh_mean`。旧训练 Player 与 FixedClockV5 Player 的程序集及实际成员结构明确不同；当前 Editor DLL 因 Editor/Player 编译目标不同不能直接按哈希等价比较，但当前运行时源码属于 V5 版本。因此只能确认模型来源和接口相符，不能确认进程、逐步轨迹或胜率一致。
- 已确认结构化观测的 `global[0]` 是由 `RunningFixedTickCount * fixedDeltaTime` 得到的 50 秒剩余游戏时间比例，不读取墙钟。FixedClockV5 下 Python/Update 主线程阻塞只拉长墙钟耗时，阻塞期间不会增加该计数，因此不会使时间观测脱离权威回合游戏时钟；但其他 NavMesh/PlayerLoop 状态仍可能受调度影响。旧 `step-1312449` 训练 Player 不具备 V5 的持久 capture clock 保证。

### 当前任务看板

| 任务 | 状态 | 剩余出口 |
|---|---|---|
| `TASK-8.10-COMBAT` | 进行中 | 补完整重载门和人工三恐龙连续击杀验证 |
| `TASK-8.11` | 进行中 | 收尾真实 Player 中断矩阵、最小回归与文档状态 |
| `TASK-8.11-MAP2` | 进行中 | 用户视觉确认；两图同 seed 动作序列 hash；安装合格默认模型 |
| `TASK-8.11-MAP3` | 进行中（核心实现与集成复验完成） | 用户目视确认固定矩形；补齐异常与新布局回归矩阵；不得干扰训练 |
| `TASK-8.12` PPO | 进行中（FixedClockV5 smoke 通过，正式 seed0 未启动） | 先消除地图1最终确定性分叉，再在新空目录完成 1,572,864 steps、冻结评估与合格接入 |
| `TASK-8.13` FPO | 进行中（长期warm-start run于update45–46再次策略崩塌，进程仍运行） | 由用户决定是否停止；冻结评估`step-705001.pt`，收紧FPO信赖域并完成至少60-update稳定性门后方可恢复长期训练 |
| `TASK-8.14` ReinFlow / PolicyFlow | 进行中（PolicyFlow训练准备审计完成） | 依赖合格PPO源；实现Dino成功轨迹、结构化Flow BC、并行PolicyFlow与smoke |
| `TASK-8.15` 统一报告 | 未开始 | 依赖 8.12–8.14 |

### 下一步顺序

1. 下一轮 PPO seed0 使用 FixedClockV5 从随机初始化启动：20-env、`time_scale=5`、rollout `20,480`；旧 4-env `step-131080.pt` 只保留历史审计，不混入新训练。
2. 仅在达到 `1,572,864` steps 且 final checkpoint/summary 完整后执行 deterministic/stochastic 冻结评估；训练曲线只作诊断，不作为资格门。
3. 只有通过冻结评估的 checkpoint 才能导出并替换默认 AI Strategy。
4. FPO当前长期run已再次崩塌但尚未获授权停止；不得把崩塌后final作为候选。先由用户决定进程处置，再冻结评估曲线高点对应的`step-705001.pt`，并以收紧clip/增加更新熔断的单变量消融通过至少60-update稳定性门。
5. 收尾 Combat、v2 中断矩阵、MAP2 人工视觉与确定性门。
6. 进入 ReinFlow/PolicyFlow 和统一比较。

---

## 全局约束

### 工作区与源码

- 每次工作前先读取本文件的“当前状态”“全局约束”“对应任务”和“关键决策”。
- `ml-agents/`、`fpo/`、`ReinFlow/`、`PolicyFlow/` 均有 `.codegraph/`；理解这些仓库时必须先在目标仓库运行 `codegraph explore`。
- `fpo/`、`ReinFlow/`、`PolicyFlow/` 默认只读；算法移植到 `flow_rl/`。
- `.conda/mlagents-r23` 是冻结基线环境；研究依赖使用独立环境。
- 不为方便直接侵入 `mlagents-learn`；自定义训练器放在 `flow_rl/`。
- 外层 `game_project` 与内层 `dino-attack` 是独立 Git 仓库。所有既有 dirty/untracked 内容均视为用户工作；禁止批量暂存、stash、清理、破坏性重置或改写历史。
- 大型构建、数据集、checkpoint 和训练日志默认不纳入 Git。

### Unity 与场景

- 正式玩家、训练和推理使用相同玩法场景与对象；控制差异仅来自 Human、TrainingAI、InferenceAI。
- 历史 `DinoAttackTraining.unity` 只保留作接入/诊断证据，不进入正式结果。
- 未经明确授权，不修改 ProjectSettings、Packages、既有构建和冻结产物。
- Unity 场景和资产通过 Editor/Unity 工具修改，不手工编辑场景 YAML。
- 涉及 `dino-attack` 时优先遵循 Unity Workbench 对应技能。

### 实现与验证

- 新功能与 bug 修复测试先行；至少覆盖 shape、dtype、Agent ID、终止/截断、有限性、动作边界和固定 seed 复现。
- `DecisionSteps` 的 Agent 顺序必须保持；`TerminalSteps.interrupted=True` 映射为 `truncated=True`，自然终止映射为 `terminated=True`。
- 四种算法共享环境、rollout、GAE、critic、日志和评估，算法专用概率模型与损失不得混用。
- 先完成单步动作比较；action chunk 仅属于阶段 9。
- 正式结论同时报告 environment steps、wall-clock、NFE、推理延迟、显存、回报/胜率和 seed 方差。
- 完成声明必须附实际验证；若存在已知失败，必须区分“既有失败”和“本次回归”。

---

## 稳定接口与实验契约

### Unity LLAPI

```text
UnityEnvironment.reset()
  → behavior_specs
  → get_steps(behavior_name)
  → DecisionSteps / TerminalSteps
  → set_actions(behavior_name, ActionTuple)
  → UnityEnvironment.step()
```

`UnityEnvAdapter` 保存当前待决策 Agent ID，提交动作前验证顺序、shape、dtype 和范围。rollout 以 Agent ID 为轨迹键，不假定 batch 索引跨 step 稳定。

### Dino v2 协议

| 流 | Shape | 说明 |
|---|---:|---|
| Global | `(5,)` | 全局状态及唯一 `DinoTarget` x/z |
| Regions | `(5,8)` | Z1–Z5 当前四边形 A/B/C/D |
| Walls | `(6,5)` | 墙集合 |
| Guards | `(11,7)` | 最多 8 名地面守卫 + 3 名墙上弓箭手 |
| Houses | `(8,6)` | 房屋集合 |
| Dinos | `(10,7)` | 最多 10 只恐龙 |

六流合计 270 标量。动作是 4D 连续向量：区域、区域内 `u/v`、Wait/三种恐龙选择。Regions 是动态几何，不在 Python 或 checkpoint 中写死；不得混用历史 504/6、v1 207/4、v2 270/4 或 3DBall 8/2 checkpoint。

### 游戏与奖励

- 单局流程：`Setup → Running → Ending → Ended → Reload`。
- 最长模拟时间：50 秒；超时为自然失败。
- 资源耗尽失败：场上恐龙数为 0 且食物 `< 25`；食物等于 25 时仍允许部署最低成本恐龙。
- 正式奖励：胜利 `10 + 0.01 × final_meat`；失败 `-1`；守卫/墙/房屋首次摧毁 `+0.02/+0.05/+0.1`；非法部署 `-0.02`；Wait 和合法部署 `0`。
- 正式算法统一 `gamma=0.995`。
- 胜利、资源耗尽和超时是 natural terminal；进程、通信或重载异常才是 truncated。

### 并行训练与评估

- 下一轮 PPO 正式采样设计：20 个 Unity 环境异步推进，GPU 动态微批上限 20、聚合等待 0；rollout `20,480`，目标平均每环境 1,024 transitions，update 边界同步。FPO 保留独立的 24-env 合同。
- PPO FixedClockV5 tuned 配置：seed 0，目标/schedule `1,572,864`，rollout `20,480`，minibatch `512`，5 epochs，final LR `0.000075`，final clip `0.15`，checkpoint interval `122,880`。
- FPO 配置：24 环境异步微批，rollout `16,384`，minibatch `512`，3 epochs，samples `8`，NFE `4`，总步数 `1,572,864`，seed 0/1。
- 每个 run 目录只允许一次写入；resume 使用新空目录，并重新启动 Unity，不声称恢复物理状态。
- checkpoint 仅在 rollout 清空、无 in-flight 数据的边界保存，并记录 build/protocol/encoder、optimizer、normalizer、RNG 和 provenance。
- 冻结评估必须显式记录 `action_mode=deterministic|stochastic`；stochastic 必须记录 `policy_seed`。
- 训练曲线、训练回合成功率和最终 checkpoint 冻结评估是不同证据，不得互相替代。

---

## 阶段总览

| 阶段 | 名称 | 状态 | 核心结果 / 退出条件 |
|---|---|---|---|
| 0 | 官方 PPO 基线 | 已完成 | 3DBall 500k，最终训练回报约 100 |
| 1 | 项目治理 | 已完成 | `AGENTS.md` 与本计划建立 |
| 2 | Unity 外部训练基础设施 | 已完成 | 100,008 decisions 稳定运行；ID/终止/GAE 验证通过 |
| 3 | Gaussian PPO | 已完成 | 两 seed 独立评估 `99.002/98.007` |
| 4 | Flow Policy 基础 | 已完成 | sampler、边界、梯度、NFE 复现通过 |
| 5 | FPO | 已完成 | 3DBall 三 seed 均值 `97.3603` |
| 6 | ReinFlow | 已完成 | BC `94.022` → 微调 `97.011`（配对评估） |
| 7 | PolicyFlow | 已完成 | 严格 MultiGoal 对照；3DBall 三 seed 均值 `97.0100` |
| 8 | Dino Attack 与算法比较 | 进行中 | 完成环境、PPO/FPO/ReinFlow/PolicyFlow 公平比较 |
| 9 | Action Chunk | 未开始 | `H=1/2/4/8` 公平消融 |
| 10 | 最终评测与交付 | 未开始 | 多种子报告、复现说明、产物索引 |

### 已验证基础产物

- Unity 构建：`Builds/3DBall/3DBall.exe`
- 官方 PPO 模型：`results/ppo-3dball-baseline/3DBall.onnx`
- Python：`.conda/mlagents-r23/python.exe`
- PyTorch：`2.2.1+cu121`，RTX 4080 SUPER 已验证 CUDA
- ML-Agents Python：`1.1.0`；Unity Package：`4.0.0`

### 官方参考源码

| 算法 | 本地目录 | 固定版本 | 主要入口 |
|---|---|---|---|
| FPO | `fpo/` | `main@418c2554` | `gridworld/models/fpo.py`、`gridworld/main.py`、`phc/scripts/train.py` |
| ReinFlow | `ReinFlow/` | `release@e722e151` | `agent/pretrain/`、`agent/finetune/reinflow/` |
| PolicyFlow | `PolicyFlow/` | `main@7f304b96` | `agents/policyflow/`、`modules/flow/`、`scripts/multigoal/` |
| light_mappo（日志参考） | `external/light_mappo/` | `main@c503d89b` | 只读参考训练日志，不作为算法事实来源 |

---

## 阶段 8：Dino Attack

**状态：进行中。** 阶段 8 保持单步动作，不引入 action chunk。

### 已完成基础

- 玩家持续部署、资源反馈、五个四边形区域、地表速度、RTS 镜头、美术/UI、房屋/墙/守卫/单局重启已完成既有实施。
- `Dinos.unity`、`LayeredBattlefield.unity`、`MapSelect.unity` 已建立正式入口；训练按 episode seed 选择双地图，mixed smoke 已覆盖两图。
- v2 六流、4D 动作、Deep Sets/Set Transformer、PPO/FPO 并行 collector、版本屏障、checkpoint 与 evaluator 已完成。
- ONNX → Unity AI Inference `2.6.1` 技术链已通过；缺失或不合格模型时 AI Strategy 必须 fail closed。
- 战斗核心修复已通过 `33/33`，守卫首帧生命初始化专项通过 `24/24`。

详细设计和实施计划索引：

- [阶段 8 总计划](superpowers/plans/2026-08-28-phase-8-dino-attack.md)
- [结构化并行训练](superpowers/plans/2026-08-30-dino-attack-structured-parallel-training.md)
- [双地图 v2](superpowers/plans/2026-08-30-dino-attack-multimap-structured-observation-v2.md)
- [分层战场](superpowers/plans/2026-08-30-dino-attack-layered-battlefield.md)
- [战斗行为重构](superpowers/plans/2026-08-30-dino-attack-combat-behavior-redesign.md)
- [紧凑编码器与训练](superpowers/plans/2026-09-01-dino-compact-structured-encoder-and-training.md)

### `TASK-8.10-COMBAT`：战斗行为收尾

- [x] 固定步出生时重放 `AttackRadius` live overlap。
- [x] per-Dino 目标生命周期、死亡与攻击幂等、延迟命中检查。
- [x] 失去最后目标后恢复村庄导航；Dino Graph 移除并行死亡 teardown。
- [x] `Unit` 生命初始化前移到 `Awake()`，修复守卫首帧漏敌。
- [x] Human、AI Strategy、Training 三模式回归。
- [ ] 完整重载门。
- [ ] 人工验证三种恐龙连续击杀/重新选敌。

证据：`reports/phase8/dino-combat-behavior/validation.md`。

### `TASK-8.11`：结构化协议与轨迹正确性

- [x] v2 六流 270 标量、4D 动作、动态 Regions。
- [x] Set Transformer/Deep Sets、PPO/FPO single-update smoke。
- [x] 正式奖励与 `gamma=0.995`。
- [x] 20 次真实重载和当前地图保留门。
- [x] adapter/collector 的 interrupted→truncated 最小测试。
- [ ] 真实 Player 注入中断矩阵与 Task 10 最小回归收尾。
- [ ] 清理已完成但仍未勾选的陈旧文档门。

证据：`reports/phase8/structured-parallel-training/v2-validation.md`、`reward-validation.md`。

### `TASK-8.11-QUALITY`：共享训练可靠性改进

**状态：已完成。** 属于阶段8的有界维护任务，不推进阶段9–10。

- [x] 修复八类训练配置的非有限数值校验；新增回归逐一覆盖全部88个浮点字段的NaN、正/负Infinity、布尔值、数值字符串，合法参数回读保持一致。
- [x] 新增整体/逐地图自然终局胜率、样本数、平均回报和长度；truncated单独计数，无自然终局不输出虚假胜率。写入前拒绝非有限回报、错误地图/环境、终止标志冲突、非法长度、截断成功和同一update内重复episode，保留原CSV及TensorBoard标签。
- [x] 按9月4日真实合同修正旧20-env断言，使用测试文件位置解析Flow BC协议路径；根目录与`flow_rl/`非集成回归均通过。
- 验证证据（2026-09-10）：根目录基线373 passed/1 failed，包目录基线372 passed/2 failed；新增定向RED为50 failed/9 passed，首轮GREEN为94 passed。最终两目录均433 passed/3 deselected（21.46秒/21.68秒），54项warning数量与基线一致。命令为`.conda/flow-rl/python.exe -m pytest -q flow_rl/tests -m 'not integration' -p no:cacheprovider --disable-warnings --tb=short --junitxml=reports/phase8/project-improvements-20260910/final-root.xml`，包目录使用`../.conda/flow-rl/python.exe`、`tests`及`../reports/.../final-package-cwd.xml`。
- 产物：`reports/phase8/project-improvements-20260910/README.md`、测试XML、原文件快照、`changes.patch`与`verification.json`。退出标准已满足；没有运行Unity集成/构建或正式训练，严格确定性、完整构建指纹和统一比较等既有门仍未完成。

### `TASK-8.11-MAP2`：分层战场与同布局 AI 重赛

- [x] 三场景入口、三套布局、45/45 预烘焙 NavMesh 路径、0 runtime bake。
- [x] 四种结算动作与 Human/InferenceAI 快照严格配对。
- [x] 双地图 50%/50% 选择和 mixed PPO smoke。
- [x] 地图2地表/Trigger/部署区/Regions 同源四边形；Hut Tier、缩放和落地修复。
- [x] 地图2外围房屋按 seed 确定性随机并绑定随带守卫。
- [x] 在独立 `codex/ui-enhancement` 工作树完成结算遮挡/文字可读性、三恐龙介绍、PPO/FPO/ReinFlow 策略选择和战局退出判负 UI，并通过定向 EditMode/PlayMode 与场景验证器。
- [x] 修复地图一 `UIDocument` 启动重建后 Start/Forfeit/结算按钮仍绑定旧可视树的问题，并完成真实鼠标“开始→退出判负→选择地图”回归。
- [ ] 用户依据代表性截图/Editor 运行结果确认最终视觉。
- [ ] 完成两图各两次相同 seed 动作序列 hash 门。
- [ ] 仅在 PPO 冻结评估合格后安装默认 AI Strategy。

证据：`reports/phase8/layered-battlefield/validation.md`、`visual-layout-audit.md`、`dino-attack/reports/ui-enhancement/validation.md`。

### `TASK-8.11-MAP4`：第一视角村庄防守

**状态：已完成。** 阶段8的独立游戏扩展，不推进研究训练阶段。

- [x] 地图三复制为独立地图四，接入选图、重试和构建入口。
- [x] 从网络导入Kenney Mini Dungeon 2.0人类模型并保留CC0来源，按用户选择完成远程法杖、第一视角移动、攻击、受伤及倒地观战。
- [x] 定向EditMode/PlayMode回归、真实AI启动与重载、Windows新构建和运行检查。
- 验证证据（2026-09-10）：`unity test dino-attack --mode EditMode --filter 'FirstPersonDefenseSceneTests|DefenseBattleCoreTests|DefenseBattlefieldSceneTests|GameLaunchContextTests'`为17/17；`--mode PlayMode --filter 'FirstPersonDefensePlayModeTests|DefenseBattlefieldPlayModeTests'`最终7/7，覆盖三策略、真实模拟键盘、恐龙发现/攻击人物、施法伤害/遮挡/冷却、墙体碰撞、数值非法输入拒绝、倒地与选图/重试/新布局。全部使用Unity6000.3.22f1；XML/命令/已失败证据保存在 `reports/phase8/map4-first-person-20260910/`。
- `unity build dino-attack --target StandaloneWindows64 --output-path Builds/DinoAttack-Map4-Windows-20260910/DinoAttack.exe`成功（完整参数见报告）；235个构建文件hash和8个新资产/meta存在性检查通过，原地图1–3哈希保留。新EXE启动10秒有响应，日志关键错误0，仅停止本次检查进程。隐藏窗口不能请求关闭，正常退出未验。
- 实际相机渲染已查看并缩小法杖；不冒充完整UI/人工游玩验收。模拟键盘测试先因批处理GameView焦点失败，随后用测试专用InputSettings副本验证通过，未改游戏输入设置。科研训练、旧构建和全量Unity历史失败均不在本次变更范围。

### `TASK-8.11-MAP4-TP`：地图四第三人称角色与技能

**状态：已完成。** 阶段8游戏扩展；取代地图四的第一视角与旧模型，不改变研究阶段。

- [x] 网络二次元模型、Blender转换与骨骼动作、第三人称跟随和遮挡处理。
- [x] 范围法术、护盾、闪避、技能HUD；可调0.5/1/2倍，地图四默认0.5倍，切图/训练隔离。
- [x] 定向Unity测试、实际相机渲染、新Windows构建与启动检查；更新操作说明和证据。
- 验证证据（2026-09-11）：最终EditMode17/17、PlayMode8/8通过，包含三种AI、范围伤害/护盾/闪避、真实键盘、相机遮挡、模型与脚部落地、四地图默认速度、实际游戏/实时时间比率、训练请求保持外部3倍时钟、倒地及重试。实际1280×720模型正面、第三人称完整HUD、行走和护盾渲染已查看。证据 `reports/phase8/map4-anime-third-person-20260910/`，命令和XML索引见README。
- `unity build dino-attack --target StandaloneWindows64 --output-path Builds/DinoAttack-Map4-Anime-Windows-20260911/DinoAttack.exe`成功（完整Editor参数见已脱敏build.log），235个文件共318,346,306字节，逐文件SHA-256见build-manifest.json。新EXE启动10秒有响应，关键错误模式0，仅停止本次烟测进程。完整人工整局和正常窗口退出尚未验证。
- `.conda/flow-rl/python.exe reports/phase8/map4-anime-third-person-20260910/verify_delivery.py`通过：地图1–3和ProjectSettings.asset与任务前字节一致，模型资源/meta完整，最终测试和构建清单有效。Unity导入时自动移除的原Standalone宏已通过Editor API恢复，临时恢复入口移出产品代码并存档；科研训练、旧构建、既有dirty修改保留。

### `TASK-8.11-MAP4-ANIM`：地图四动画修复

**状态：已完成。** 阶段8内修复，不恢复训练。

- [x] 复现并定位行走与攻击动作异常，建立动作回归。
- [x] 修正人物动作并检查连续渲染，保留原玩法。
- [x] 定向回归、新Windows构建和启动检查，更新验证记录。
- 验证证据（2026-09-11）：攻击恢复回归先RED；新动作首次因未计入Transform缩放而步幅不符，修正后原误差断言通过。`unity test dino-attack --mode EditMode --filter 'DefenderAnimationTests|FirstPersonDefenseSceneTests|DefenseBattleCoreTests|DefenseBattlefieldSceneTests|GameLaunchContextTests'`20/20，PlayMode三组相关fixture8/8；完整参数、XML和实际连续渲染见`reports/phase8/map4-animation-fix-20260911/README.md`。新Editor菜单保护增加后，额外3/3复验通过；其首次未产XML的原生线程清理崩溃保留，具体原因未确定。
- `unity build dino-attack --target StandaloneWindows64 --output-path Builds/DinoAttack-Map4-AnimationFix-Windows-20260911/DinoAttack.exe`成功；235文件、318,416,270字节，逐文件SHA-256保存在build-manifest.json。新EXE10秒有响应，关键错误0，仅结束本次PID6980；完整人工整局及正常窗口关闭未验证。
- `.conda/flow-rl/python.exe reports/phase8/map4-animation-fix-20260911/verify_delivery.py`通过：285个基线文件中8项预期变化、277项原样，21个新资源/meta完整。Unity导入自动删除的Standalone宏通过Editor API恢复，ProjectSettings与任务前字节一致；临时恢复入口已移出源码，原模型/贴图、守卫动画、地图1–3、旧构建和训练保留。

### `TASK-8.11-MAP4-MINIMAP`：地图四小地图

**状态：已完成。** 阶段8有界界面扩展，不恢复训练。

- [x] 增加真实战场与玩家/敌我标记，处理生命周期和布局。
- [x] 完成定向回归与实际界面渲染检查。
- [x] 新Windows构建与启动检查，记录范围校验和使用说明。
- 验证证据（2026-09-11）：新增小地图运行测试先因缺失视图RED；最终EditMode20/20、PlayMode9/9通过，覆盖投影/边界、真实敌人出生移动死亡、暂停刷新、倒地观战、重试/切图及三AI/人物/倍速回归。1280×720、1920×1080、1024×768的实际小地图/HUD渲染已检查，完整人工整局未验；既有低分辨率共享工具栏拥挤不在此次改动范围。日志/XML/截图见`reports/phase8/map4-minimap-20260911/README.md`。
- Windows构建`Builds/DinoAttack-Map4-Minimap-Windows-20260911/DinoAttack.exe`成功，235文件、318,425,710字节，逐文件SHA-256见报告；10秒启动有响应，关键错误0，仅结束本次PID69168。隐藏窗口正常退出未验。`verify_delivery.py`通过，1331基线文件仅3项预期变化、1328项原样，新增3个C#与meta共6文件；Unity导入自动移除的原宏通过Editor API恢复，临时工具已移出Assets，四地图、设置、包及原玩法源码保持字节一致。

### `TASK-8.11-MAP4-DAMAGE`：玩家攻击扣血数字

**状态：已完成。** 阶段8有界反馈功能。

- [x] 普攻/范围攻击实际扣血事件、漂浮数字与生命周期。
- [x] 命中/阻挡/残血/多目标/消退等回归与实际渲染。
- [x] 新Windows构建、启动与范围校验，更新证据。
- 验证证据（2026-09-11）：`play-red.xml`先复现缺少数字；实际金色普攻、紫色范围技能和残血击杀渲染已查看；最终EditMode20/20、PlayMode10/10通过，覆盖命中/阻挡/过量/多目标、上浮、暂停淡出、销毁目标、HUD重建、结算和重试/切图以及三AI/技能/倍速/小地图回归。测试摆位、Physics同步与初始化顺序的中间失败和定位记录保存在`reports/phase8/map4-damage-numbers-20260911/README.md`，未放宽原遮挡和伤害规则。
- Windows构建`Builds/DinoAttack-Map4-DamageNumbers-Windows-20260911/DinoAttack.exe`成功，235文件、318,429,038字节，完整SHA-256清单见报告；10秒启动有响应且关键错误0，仅结束本次PID31340，正常窗口关闭与人工完整游玩未验。`verify_delivery.py`通过，1337基线文件仅6项预期变化、1331项保持字节一致，新增2个C#及meta共4项。Unity自动移除的原宏经Editor API恢复并校验，临时工具已移出Assets；四地图、设置、包及其他已有源码/资源保持。

### `TASK-8.12`：Gaussian PPO 基线

已知历史：

- 旧 16-env seed0/1 和 compact/tuned seed0/1 训练均数值通过，但生命周期污染使其不具备部署资格。
- tuned 训练回合末段曾显示 map1/map2 `97.43%/81.79%`，冻结评估却无法复现；该差异已记录，训练曲线不再作为选模依据。
- 旧默认模型已撤销资格，不得宣称可用。

当前任务：

- [x] 修复 TrainingAI 结果收集生命周期与推理非法动作反馈。
- [x] 建立地图1 TrainingAI/InferenceAI 同 seed、同动作生命周期对照测试。
- [x] 建立双地图各 3 seed 随机策略自然终局矩阵并定位地图1结果冻结缺陷。
- [x] 构建 runtimefix Player 并完成 24-env smoke。
- [ ] 将地图1布局签名改为战斗前冻结，修复自然终局后 InferenceAI 结果无法保存的问题。
- [x] 将旧 runtimefix seed0 按用户要求取消并删除，不再恢复或参与选模。
- [x] 在新空目录重新启动 runtimefix seed0，并验证 24/24 Player、双地图自然终止和有限 PPO 更新。
- [x] 按用户新决定停止 8-env run 并保留 `step-524455.pt`，从随机初始化启动 4-env post-victory-guard seed0；验证 4/4 Player、首个有限 PPO update、双地图自然终止和 0 truncated。
- [x] 完成第一阶段固定时基改造：固定步 Behavior Graph/回合/攻击与移动、训练 capture clock、决策调度对齐、Python stall 指标及 microbatch wait=0。
- [x] 修正 capture clock 的外部覆盖：删除 Unity 固定 20 倍配置，以实际 `Time.timeScale` 计算并持久设置 `captureFramerate`；FixedClockV5 真实 4-env smoke 验证 5 倍时为 250 FPS / 0.004 s，四个 Player 均只修复一次。
- [x] 用冻结初始策略完成 0/75 ms 对照；系统性胜率跃迁由旧版 `2.5% -> 75%` 降至 `0% -> 5%`，两组 stochastic environment steps 均为 `1047`。
- [ ] 消除地图1 Open Tropical 的最后一个首局路径分叉；已完成每 episode NavMesh 就绪门、目标感知到达判定、完整路径拒绝及预烘焙 NavMesh 合同，deterministic 0/75 ms 已由 `806/863` 收敛为 `1652/1654` environment steps，20 组中 19 组业务终局一致，但首个地图1 seed 仍分叉，尚未达到确定性门。
- [ ] 先完成 20-env smoke，再完成 FixedClockV5 20-env seed0 至至少 `1,572,864` environment steps并取得最终 checkpoint/summary。
- [ ] 将已确认的负载敏感性作为环境合同限制披露；8-env 结果不得与历史 24-env 曲线作同动力学比较。
- [ ] 对候选 checkpoint 执行 deterministic/stochastic 冻结评估。
- [ ] 选出合格 checkpoint 后导出 ONNX、安装默认模型并完成两图确定性门。
- [ ] 明确单种子结果与历史多种子门的偏差；本轮禁止启动 seed1/2，除非用户改变决定。

证据：`reports/phase8/ppo-baseline/runtime-failure-investigation-20260902/README.md`、`training-log-audit-20260902/README.md`、`runtimefix-tuned-24env-seed0-retrain-1572864-r16384-b512-e5-20260902/`、`runtimefix-seed0-retrain-launch-20260902-a2/`。

### `TASK-8.13`：FPO

- [x] 24-env 并行 FPO、batched decider、checkpoint 和 CLI 接入。
- [x] 真实 smoke：`2,064` transitions、24/24 worker、1 finite update。
- [x] 完整显存门：peak reserved `1.061 GiB`，保留 minibatch 512。
- [x] 审计现有正式配置、暂停 checkpoint、旧/新 Player 指纹和历史数值稳定性；确认现有 YAML 不可直接启动，且旧 checkpoint 不可作为 FixedClockV5 公平比较恢复源。
- [x] 按当前 PPO 参考 YAML 的实际内容建立 FixedClockV5 FPO seed0 配置：16 env、timeScale1、rollout16384、4,718,592 steps、wait 0、随机初始化和新目录；FPO 保持3 epochs/NFE4/samples8。
- [x] 建立并通过匹配的16-env single-update smoke：16,399 transitions、16/16环境、268自然终止、0 truncated、1次有限更新、GPU peak 1.061 GiB、Unity关键异常0。
- [x] 从随机初始化启动 FixedClockV5 seed0；首个正式 update 为16,394 transitions、16/16环境、270自然终止、0 truncated，KL/flow loss/梯度有限。
- [x] 审计 seed0 中途成功率下降：确认 update 50 优化后发生 actor 策略崩塌，10-update 成功率从64.57%降至8.67%；环境重启、截断、成功误判和地图抽样变化均非首因。
- [x] 增加与精确 resume 互斥的 FPO warm-start 路径：只加载 policy model state，不恢复 optimizer、normalizer、RNG、environment steps、policy version或wall clock；checkpoint 元数据记录源路径与SHA-256。
- [x] 按用户指定中断原始 run，并从 `step-786964.pt` 权重以重置优化器运行25个update：`lr=1e-4`、epochs2、samples16、difference clip1、clip不变且暂不加入proxy-KL early stop；完成409,868 transitions、25 checkpoints和final。
- [x] 按用户要求从训练曲线单点候选 `step-377087.pt` 仅加载模型权重并再次重置optimizer/RNG/计数，启动4,718,592-step长期run；首个update与warm-start provenance验证通过。
- [x] 审计长期warm-start run的第二次突降：确认update44–46按策略版本延迟发生77.9%→57.7%→3.0%真实崩塌；排除统计、worker、生命周期、时钟、地图偏斜与非有限梯度，定位为CFM ratio surrogate未形成硬信赖域，且25-update探针在KL预警刚出现时即结束。
- [x] 按用户指定选择第160个rollout对应的行为策略checkpoint `step-2606819.pt`（policy version 159），完成双图冻结评估并导出为独立FPO AI Strategy；未覆盖PPO默认模型。
- [ ] 增加完整 Unity 构建指纹；不得只用启动器 exe 哈希区分构建。
- [ ] 完成 seed0 后严格串行运行 seed1；不运行 seed2。
- [ ] 按与 PPO 相同的冻结环境和评估口径报告表现与 NFE/延迟成本。

证据：`reports/phase8/fpo-training/`。

### `TASK-8.14`：ReinFlow / PolicyFlow

- [x] 核对官方PolicyFlow固定commit与本地阶段7实现，完成Dino训练准备差距审计、初始参数草案和fail-closed启动门。
- [x] 静态审计 PPO `final.pt` 候选教师：v2协议、Set Transformer、Identity normalizer和FixedClockV5 build provenance兼容；末2000个自然终局为map1 99.85%、map2 73.35%，可进入冻结评估门，但训练曲线不替代独立评估。
- [x] 将旧的暂停FPO监控更新为30分钟接续心跳：监控当前FPO长期run，正常完成后自动执行PolicyFlow全部准备门，并仅在smoke通过后启动正式seed0；首个update健康后自动暂停以防重复启动。
- [x] 从合格 PPO 策略重新收集 Dino Attack 成功轨迹。
- [x] 训练并验证 Flow BC，单独报告预训练成本。
- [ ] 运行 ReinFlow 在线微调并验证不退化。
- [ ] 仅在存在可量化多峰成功部署时评估 PolicyFlow 模式覆盖收益。
- [ ] 使用与 PPO/FPO 相同环境步、评估 seed 和场景协议。

### `TASK-8.15`：统一报告

- [ ] 汇总回报、胜率、样本效率、wall-clock、NFE、延迟、显存、参数量和 seed 方差。
- [ ] 区分任务表现、多峰覆盖和计算成本。
- [ ] 记录失败实验、协议偏差、pre-fix/post-fix 边界和已知限制。
- [ ] 输出复现命令和产物索引。

### 阶段 8 退出标准

- [ ] 玩家、TrainingAI、InferenceAI 使用相同地图、玩法、奖励和 v2 协议。
- [ ] 两张地图的导航、布局、终止、重载和推理确定性门完成。
- [ ] 环境 build、配置、依赖、normalizer、算法 commit 和 checkpoint provenance 冻结。
- [ ] PPO、FPO、ReinFlow、PolicyFlow 完成可审计公平比较；批准的 seed 偏差明确披露。
- [ ] terminal、truncated、Agent ID、空 Decision batch、非法动作、资源返还和重载有自动化或运行证据。
- [ ] 结论同时说明表现收益、多峰收益与 wall-clock/NFE/显存代价。

---

## 阶段 9：Action Chunk

**状态：未开始。** 仅在阶段 8 单步算法完成后启动。

- [ ] 定义 Python 或 Unity 动作队列，以及 macro-transition 的 reward/terminal/truncation/bootstrap 语义。
- [ ] 测试队列耗尽、episode 中途终止和 Agent ID 重排。
- [ ] 实现 `H=1,2,4,8`。
- [ ] 比较 open-loop 与 receding-horizon。
- [ ] 提供相同 chunk 语义的 Gaussian PPO 基线。
- [ ] 验证 `H=1` 与阶段 8 单步结果一致，并同时报告回报、延迟、NFE 和鲁棒性。

## 阶段 10：最终评测与交付

**状态：未开始。**

- [ ] 冻结最终配置、build hash、依赖、源码标识和 checkpoint 元数据。
- [ ] 使用批准的训练 seed 集和隔离评估 worker 完成统一评估。
- [ ] 主报告使用 final checkpoint；最佳 checkpoint 仅作次要结果。
- [ ] 汇总均值、标准差、每 seed 原始值与失败运行。
- [ ] 在新环境中验证复现说明。
- [ ] 更新最终结论、产物索引和未完成事项。

---

## 统一指标与风险

### 必须记录的指标

| 类别 | 指标 |
|---|---|
| 任务表现 | episode return、length、成功率、每地图结果 |
| 样本效率 | 达到阈值所需 environment steps |
| 优化稳定性 | actor/flow loss、critic loss、gradient norm、KL、clip fraction |
| Flow 成本 | NFE、采样时间、单动作延迟 |
| 资源成本 | wall-clock、steps/s、峰值显存、参数量 |
| 分布质量 | action covariance、模式覆盖、熵/算法对应多样性 |

### 主要风险

| 风险 | 缓解 |
|---|---|
| 训练曲线与部署表现不一致 | 强制逐 checkpoint 冻结评估，训练曲线不作资格门 |
| Unity 生命周期污染训练 | TrainingAI 与玩家/推理快照链隔离；扫描 Player 日志 |
| Agent 顺序或空 batch 错配 | adapter 断言 ID/shape；保留 terminal-only 与空动作测试 |
| interrupted 处理错误 | 明确 terminated/truncated 映射和 bootstrap 测试 |
| Flow 计算开销过高 | 同时报 NFE、延迟、wall-clock、显存，并做受控消融 |
| 参考仓库依赖冲突 | 冻结基线环境，官方复现使用隔离环境 |
| 工作区已有修改被覆盖 | 双仓库精确文件范围、只读状态检查、禁止批量清理 |
| 过早引入 action chunk | 阶段 9 独立实施并提供同语义 PPO 基线 |

---

## 关键决策记录

### 2026-09-11：地图四玩家攻击伤害数字

- 用户要求玩家操控角色攻击时显示扣血数字。本轮阶段8新增`TASK-8.11-MAP4-DAMAGE`：普通法杖及Q范围技能成功扣血后，在敌人上方显示实际扣血量，使用金色/紫色区分并上浮淡出；过量伤害仅显示实际剩余血量，未命中/阻挡/零伤害及其他守卫造成的伤害不显示为玩家伤害。
- 沿用角色与HUD，只在玩家伤害入口发送扣血后事件，视图持有命中位置快照，敌人死亡后数字仍可完成淡出；限量复用文字，使用实时时间确保0.5倍下可读，生命周期与重试/结算清理。原数值、AI、技能冷却、小地图和场景保持。测试先行，真实命中/范围/击杀/遮挡/时间与HUD验证后输出独立Windows构建，不启动训练。

### 2026-09-11：地图四小地图

- 用户要求增加小地图；按当前地图四上下文，在人物HUD右上角倍速栏下增加固定北向战场图，显示实际部署区域、村庄目标/房屋、布防单位、玩家朝向和存活敌人，支持布防、战斗和倒地观战，结算隐藏。
- 阶段8新增有界任务`TASK-8.11-MAP4-MINIMAP`，采用现有UI Toolkit和真实运行时注册表，无新增包/相机/场景改写；保持原动画、玩法、AI与默认0.5倍速。以坐标映射、出生/移动/死亡、重试/切图、界面尺寸和实际渲染回归为退出门，输出独立Windows构建。基线见`reports/phase8/map4-minimap-20260911/baseline-hashes.json`。

### 2026-09-11：地图四行走与施法动画修复

- 用户反馈新人物走路和攻击动作怪异。本轮为阶段8的有界缺陷修复，重新检查连续动作，不能以既有单帧截图或伤害逻辑测试替代动画质量验证。
- 保留角色、地图、AI、技能数值、移动速度和默认0.5倍速；优先核查移动方向/步频、Humanoid姿势、持杖层与连续施法过渡。修改限人物动画/控制及相关回归，以Unity API修改动画资产，输出独立修正版构建。
- 退出标准：修复前可重复的动作/骨骼采样，原问题针对性回归通过，站立/多方向行走/连续攻击/移动施法的连续渲染对比，原技能及三AI回归、新Windows构建和启动检查；记录人工体验边界。基线快照 `reports/phase8/map4-animation-fix-20260911/before/`。

### 2026-09-10：地图四二次元角色、第三人称、技能和倍速

- 用户最新决定取代下条地图四记录中的第一视角与Kenney人物选择：从网络获取二次元美少女模型，通过本机Blender处理骨骼/材质并接入跟随第三人称镜头；保留远程法杖，增加范围法术、护盾、闪避与技能冷却提示。
- 阶段8新增有界任务 `TASK-8.11-MAP4-TP`，不推进科研训练。地图四默认0.5倍速，提供0.5/1/2倍选择；敌我游戏时间统一缩放，鼠标与UI保持实时响应，不改固定步长、训练时钟或AI模型。地图1–3默认1倍；切图恢复默认，训练入口不受玩家倍速设置影响。
- 保留地图四稳定ID/场景路径及原有布防、AI、倒地观战、重试、胜负流程；只更新地图四人物资源和镜头、必要运行脚本/入口文字，并在新目录构建Windows游戏。旧dirty内容已快照到 `reports/phase8/map4-anime-third-person-20260910/before/`。
- 退出标准：新测试先复现缺失能力；骨骼模型/贴图和第三人称画面实际渲染检查；技能伤害/冷却/阻挡/护盾/闪避、相机遮挡和倍速/切图隔离通过定向Unity测试；新Windows构建和启动检查通过，记录人工游玩边界。

### 2026-09-10：地图四第一视角村庄防守

第一视角及Kenney模型选型已取代，见上条“地图四二次元角色、第三人称、技能和倍速”；其余地图复制与战斗规则继续生效。

- 用户授权复制地图三并额外加入可由玩家第一视角操控的人物，角色资源从网上寻找。本次为阶段8游戏扩展，直接复用地图三的地形、布防库存、AI模型、时限、经济与胜负规则；不进入训练地图采样、不重训模型、不发布远端。
- 新建独立 `FirstPersonDefenseBattlefield.unity`，保留地图三原场景；布防阶段使用原俯视镜头，开战后启用玩家人物与第一人称镜头。新增人物独立于既有11守卫观测容量，战斗伤害复用IDamageable接口；倒地只停止人物控制、回到观战，不新增整局败北条件。
- 必要入口改动限于地图ID、地图选择按钮、按当前防御地图保留的重试路由，以及通过Unity API追加Build Settings场景。原dirty内容先快照，不覆盖已有构建。输出新的Windows游戏目录。
- 退出标准：定向测试覆盖地图身份/布局隔离、人物移动/碰撞/攻击/死亡与输入退出、真实AI启动及重载；Unity编译、场景引用和Windows构建通过，记录实际视觉/运行证据和未验证边界。

### 2026-09-10：阶段8共享训练代码检查与改进

- 用户授权检查项目并直接改进。本次范围为阶段8共享训练基础设施：修正训练浮点参数对非有限数值的放行、补充逐update真实胜率统计、修复已复现的测试目录依赖及陈旧配置断言。算法公式、实验超参数、Unity玩法/资产、冻结模型与历史运行不在本次修改范围内，不启动正式训练或发布。
- 日志保留逐episode CSV与已有TensorBoard标签；新增独立的逐update汇总标签，胜率分母仅含自然终局，truncated单独计数，无自然终局时不输出虚假的0%胜率。
- `2026-09-03`的20-env设计已被`2026-09-04`实际16-env/timeScale1/rollout16384/4718592合同取代；修正测试预期，不改历史YAML及目录名，不把旧看板当作启动授权。

### 2026-09-06：执行剩余上传计划

- 用户明确要求完成剩余上传计划，授权更新公开源码并创建`xvchengxvcheng/dino-attack-flow-rl`的`v0.1.0` Release及附件。阶段8并列发布任务进行中，不启动训练或扩展科研范围。
- 沿用已验证的新Windows游戏、冻结FixedClockV5训练Player及三部署checkpoint/Flow BC/成功数据；补齐下载校验、独立环境及实际连接验证。桌面工具再次初始化失败，成品人工三地图全流程不得记为通过；以可执行自动验证和明确限制交付研究演示。
- [x] 新增FPO真实地图三PlayMode测试1/1通过；解压后的三checkpoint/ONNX batch6对照通过，最大误差2.63e-6，Flow BC严格checkpoint校验通过。FixedClockV5 LLAPI真实325决策、4自然终局、0截断，六流/4D合同与两图日志通过。证据`reports/windows-rebuild-20260906/release-fpo.xml`、`release-validation/`。
- [x] 独立Python 3.10环境按原requirements.lock安装，pip check通过、374 passed/1 skipped/3 deselected（14.15秒）、三CLI帮助通过、CUDA可用。C盘首次安装空间不足，仅清理本任务失败环境/重复下载，迁至`D:/job/codex-validation/dino-flow-rl-v010/env`后成功；原研究环境未改。
- [x] 五附件打包及原路径/大小/hash清单、下载脚本、四相对路径示例配置、模型卡和发布说明完成。空目录安装/重复安装、损坏ZIP拒绝、解压模型加载和配置解析通过。附件共约248MiB，位于`release-staging/artifacts/v0.1.0/`，可选研究checkpoint包不发布。
- [x] 凭据模式扫描无命中；明确排除Unity自动生成的Timers与SENTIS_ANALYTICS_ENABLED设置差异，原项目未覆盖。源码提交`a47659b2eb239a9cc27a599a20be80f5149cbf66`推送main成功；新clone`release-staging/verify-v010-source`的2,621清单文件/61本地链接全部通过（Git文件连同清单共2,622）。Release草稿附件上传和远端下载复验进行中。

> 历史决策不删除；重复的参数演进合并为一条，并在状态中说明取代关系。详细讨论和逐次验证见对应规格、实施计划与报告。

### 2026-09-06：GitHub首版上传计划

- 用户要求制定上传计划；本次交付计划文档，不创建远端、不上传、不继续训练。首版按当前三地图与PPO/FPO/PolicyFlow已接入策略组织为研究演示，研究阶段8退出门及阶段9–10状态保持不变。
- 计划采用独立发布副本中的单仓库，读取真实磁盘上的已跟踪/未跟踪必要文件并保留源仓库；源码、部署ONNX和小型证据入Git，checkpoint、成功数据、完整运行记录和Player分包作为Release附件。默认部署PolicyFlow仍为step-2147452，step-3196659仅为可选研究附件。
- 按既有用户偏好，本计划聚焦文件、依赖、验证和上传操作，不重复展开此前许可讨论。具体执行清单与退出条件见`docs/release/GITHUB_UPLOAD_PLAN.md`；执行、测试、构建和发布均尚未开始。

### 基础架构

| 日期 | 决策 | 状态 |
|---|---|---|
| 2026-08-24 | 使用 ML-Agents `release_23`；冻结 `.conda/mlagents-r23`；自定义算法通过 Python LLAPI 外置实现 | 生效 |
| 2026-08-24 | 顺序为 Gaussian PPO → Flow 基础 → FPO → ReinFlow → PolicyFlow；3DBall 只作正确性门；action chunk 延后 | 生效 |
| 2026-08-24 | `docs/PROJECT_PLAN.md` 是唯一主计划 | 生效 |
| 2026-08-25 | Decision-first 视图保留动作对齐；episode/liveness 按 LLAPI terminal-first 事件语义处理 | 生效 |
| 2026-08-25 | run 目录一次写入；达到步数后排空到初始 Agent ID 再出现 | 生效 |
| 2026-08-26 | 独立 tanh Gaussian PPO；tanh Jacobian 修正，base Normal 解析熵 | 生效 |
| 2026-08-26 | resume 恢复模型、optimizer、normalizer、schedule 和 RNG，但重启 Unity，不恢复物理状态 | 生效 |
| 2026-08-26 | 策略、critic、评估和采集必须接受零 Decision Agent 空批次 | 生效 |

### 算法事实来源

| 日期 | 决策 | 状态 |
|---|---|---|
| 2026-08-26 | FPO 以本地 commit `418c2554` 的 signed advantage 与 CFM loss-difference ratio 为准 | 生效 |
| 2026-08-26 | ReinFlow 数据来自固定 PPO checkpoint；在线算法使用随机 Euler chain、完整 transition likelihood 和 learnable noise | 生效 |
| 2026-08-27 | ReinFlow 正式配置采用低噪声 `0.04–0.10`、entropy coefficient `0` | 生效 |
| 2026-08-27 | PolicyFlow NFE 按实际 velocity 调用计数；从 Flow BC 初始化，预训练成本单列 | 生效 |
| 2026-08-27 | PolicyFlow 修复 pending-action 行为快照、clean checkpoint 和严格 MultiGoal 判据；Brownian coefficient 使用 `0.001` | 生效 |

### Dino 环境与协议

| 日期 | 决策 | 状态 |
|---|---|---|
| 2026-08-27 | Dino Attack 采用持续时序决策，目标为最大化单局胜率 | 生效 |
| 2026-08-28 | 正式 Human/AI 使用相同 `Dinos.unity` 和玩法对象；历史训练场景降级为诊断夹具 | 生效，取代训练场景隔离方案 |
| 2026-08-28 | 阶段 8 删除 random/greedy/CEM 独立非学习基线 | 生效 |
| 2026-08-28 | 采用五区开阔热带战场、自由 NavMesh、统一地表倍率和不阻挡攻击的地貌语义 | 生效，取代旧三路线/四区方案 |
| 2026-08-29 | 五区改为共享 A/B/C/D 的凸四边形，使用严格 `[0,1]` 双线性 `(u,v)` 映射 | 生效，取代矩形区 |
| 2026-08-29 | 正式游戏改为单局制；6墙位选3墙；房屋/守卫/墙生命周期与重载规则冻结 | 生效，取代多回合维修升级 |
| 2026-08-29 | Rim/山体只作视觉；恐龙相互忽略非 Trigger 身体碰撞并保持 `NoObstacleAvoidance` | 生效，取代 Rim 阻挡 |
| 2026-08-31 | 资源耗尽为无恐龙且食物 `<25`；最长单局 50 秒 | 生效，取代 30/40 秒 |
| 2026-08-30 | v1 改为五流 207/4D；随后升级 v2 六流 270/4D、动态 Regions、Guards 11 | v2 生效，取代 504/6 与 v1 |
| 2026-08-30 | 正式奖励冻结为胜利/失败/守卫/墙/非法动作规则；2026-08-31 增加房屋首次摧毁 `+0.1` | 生效 |
| 2026-08-30 | 第二地图采用“分层战场”、共享模型、无 map one-hot、seed 布局和双地图 episode 采样 | 生效 |
| 2026-08-31 | 地图2外围六栋房屋改为 seed 确定性矩形随机；核心房和核心守卫固定 | 生效，取代有限固定外围候选 |
| 2026-09-02 | 终点胜利只接受本局 `ActiveDinos` 中仍存活的 `Dino`；`null`、非 Dino、已死亡或未登记 Dino 一律拒绝并写审计日志 | 生效；此前 Player/checkpoint 标为 pre-victory-guard，不纳入修复后训练比较 |

### 地图、UI 与推理

| 日期 | 决策 | 状态 |
|---|---|---|
| 2026-08-30 | Home 使用 A 镜头：FOV 58、offset `(0,82,38)`、zoom `0.22`；删除外围五山 | 生效 |
| 2026-08-31 | MapSelect 点击地图直接进入，使用新 Input System；Logo 只在选图页；两图不保留 Riverbed | 生效 |
| 2026-08-31 | Retry/New/AI 重载自动 Running；首次 MapSelect 进入保留中央 Start | 生效 |
| 2026-08-31 | 推理直接消费 v2 六流，每 25 simulation steps 用 AI Inference CPU 推理；模型不合格时仅禁用 AI | 生效 |
| 2026-08-31 | 地图2房屋/区域对应与顶部构图的最终 PASS 由用户目视判断，自动技术门继续保留 | 生效 |
| 2026-09-02 | 仅凭训练曲线选择默认模型的做法撤回；默认模型必须通过冻结评估 | 生效，取代旧选模方式 |
| 2026-09-04 | 训练期间的 UI 改动放在 `codex/ui-enhancement` 独立工作树；AI Strategy 显示 PPO/FPO/ReinFlow，但只有通过兼容性验证并已安装模型的策略可启动；战局内主动退出按自然失败进入现有结算链 | 生效 |
| 2026-09-05 | UI Toolkit 运行时按钮与结算 presenter 必须在 `UIDocument` 启动重建后重绑到 live visual tree；旧树上的回调和元素引用不得继续作为运行时交互依据 | 生效 |
| 2026-09-05 | UI 合并采用正式/UI双快照与独立 integration 工作树；冲突时保留正式 `Dinos.unity`，地图三和 MapSelect 通过 Unity builder 重建后验证，再快进正式分支 | 生效 |
| 2026-09-05 | 用户指定选取FPO约160 updates checkpoint作为AI Strategy；按行为版本偏移选择update160实际采样所用的`step-2606819.pt`（policy version159），采用零latent、Euler NFE4确定性ONNX并作为独立FPO资源安装，不覆盖PPO `DefaultModel` | 生效；单seed中间checkpoint，冻结双图确定性/随机评估均为31/40，地图差异与随机性限制必须披露 |

### 训练参数与运行控制

| 日期 | 决策 | 状态 |
|---|---|---|
| 2026-08-31 | PPO 环境数/预算曾依次为 4→8→12→16；旧规格与 partial run 保留 | 已取代 |
| 2026-09-01 | 取消 PPO seed2；同步实验不得与异步 seed0/1 合并成三种子结果 | 生效 |
| 2026-09-01 | 真同步必须跨环境一次 batched `policy.act()`；正式架构改为异步环境 + 动态 GPU 微批 + update 边界同步 | 生效 |
| 2026-09-01 | 24-env 基准显示异步微批相对同步吞吐高 `4.816%`；正式采用 24-env、batch 24、2 ms 聚合窗 | 已取代（仅 PPO 环境数与推理批大小由 2026-09-02 的 8-env 决策取代） |
| 2026-09-01 | Set Transformer 删除固定区域编号 embedding，使用 `dynamic_geometry_only`；旧拓扑 checkpoint fail closed | 生效 |
| 2026-09-01 | 紧凑网络使用 Defense ISAB、Dinosaur SAB、`d_model=48`、dropout 0；完整 PPO `505,673` 参数 | 生效 |
| 2026-09-02 | 旧 PPO checkpoint 因生命周期污染和冻结评估失败失去资格；修复后必须重训 | 生效 |
| 2026-09-02 | FPO 使用 24-env、rollout 16384、batch 512、3 epochs、samples 8、NFE4、seed0/1；不运行 seed2 | 生效 |
| 2026-09-02 | 新 PPO 正式训练从空目录运行且只运行 seed0；不恢复已取消 checkpoint，不启动 seed1/2 | 生效 |
| 2026-09-02 | PPO 正式训练改为 8 个 Unity 环境、动态推理批上限 8；保持总步数 1,572,864、rollout 16,384、batch 512、5 epochs，并从随机初始化在新空目录训练 | 生效；取代 PPO 24-env 正式运行与恢复 step-131189 的安排 |
| 2026-09-02 | 训练环境采用项目固定步 gameplay driver，并设置 `captureDeltaTime=fixedDeltaTime/timeScale`；正式 PPO 配置禁用 microbatch 等待和旧 carving 障碍。地图1 deterministic 0/75 ms 逐步门通过前不得恢复训练 | 生效 |
| 2026-09-03 | capture clock 由 Python 下发后的实际 `Time.timeScale` 单源驱动；Unity 不再覆盖为20，并通过非零 `captureFramerate=round(timeScale/fixedDeltaTime)` 持久化对应 `captureDeltaTime` | 生效；修正 2026-09-02 实现中会被 `captureFramerate=0` 覆盖的问题 |
| 2026-09-03 | 下一轮 PPO seed0 改为 FixedClockV5 20-env、`time_scale=5`、rollout `20,480`、batch `512`、5 epochs、推理批上限20且等待0；保持 `1,572,864` 比较预算并从随机初始化启动 | 生效；取代尚未启动的 16-env rollout 8,192 配置，正式训练前需通过 20-env smoke |
| 2026-09-04 | FPO 旧 PostFix 配置与 `step-131207.pt` 退出当前 FixedClockV5 正式比较；新 FPO 必须使用 V5、wait 0、随机初始化和新目录，20-env 对齐方案与保留 24-env 方案待用户选择 | 生效；取代“把旧 checkpoint 恢复到当前正式 FPO”的安排，不删除历史产物 |
| 2026-09-04 | 用户指定参考 `phase8_dino_ppo_fixedclock_v5_20env_seed0_timescale5_rollout20480_20260903.yaml` 创建新 FPO；以该文件当前实际内容为准，即 V5、16 env、timeScale1、rollout16384、4,718,592 steps、wait0，FPO 保持3 epochs/NFE4/samples8并从随机初始化开始 | 生效；参考文件名与内容不一致，FPO 新文件名按实际参数命名；取代上一条待选20/24-env方案 |
| 2026-09-04 | 用户要求启动 FPO；先执行同合同16-env single-rollout smoke，通过后立即启动正式 seed0 | 生效；正式训练 PID 63896，旧 checkpoint 不恢复，冻结门保持不变 |
| 2026-09-04 | FixedClockV5 FPO seed0 在 update 50 后发生真实策略崩塌；当前 PPO 同参的 FPO 设置不再视为数值稳定，当前运行保留作诊断但 final 不自动进入正式候选 | 生效；下一轮需单独调低 FPO learning rate/clip/epochs、提高 MC samples 并增加 proxy trust-region early stop，参数改动需先做 smoke/消融 |
| 2026-09-04 | 用户中断原始FPO run，并指定从崩塌前 `step-786964.pt` 只加载模型权重，重置actor/critic optimizer与运行计数；以`lr=1e-4`、epochs2、samples16、difference clip1、原clip完成25个update，暂不加入proxy-KL early stop | 生效；这是稳定性warm-start探针而非精确resume，取代上一条“下一轮必须同时增加proxy trust-region early stop”的部分安排，不改变冻结评估资格门 |
| 2026-09-04 | 用户要求继续FPO训练；依据其既定“看训练曲线，谁高用谁”口径，从25-update探针中单点最高行为策略对应的`step-377087.pt`只加载权重并重置optimizer/RNG/计数，以相同稳定参数继续至4,718,592新环境步 | 生效；不从尾段KL连续升高的final精确resume，仍暂不加入proxy-KL early stop；逐-update checkpoint并保留冻结评估资格门 |
| 2026-09-04 | 开始准备Dino PolicyFlow，但在合格PPO、成功轨迹、多峰报告、结构化Flow BC和16-env PolicyFlow smoke齐备前fail closed；不直接复用3DBall或FPO模型，不创建虚假的正式训练YAML | 生效；初始smoke草案为16env/timeScale1/rollout16384/batch512/epochs2/LR1e-4/NFE4/Brownian0.001，正式预算待PPO/FPO公平合同冻结 |

### 战斗修复

| 日期 | 决策 | 状态 |
|---|---|---|
| 2026-08-30 | 保留 Behavior Graph 作高层编排，C# tracker 统一 per-Dino 目标生命周期 | 生效 |
| 2026-09-01 | 固定步出生必须在启图前同步当前 live overlap；即时/延迟攻击和死亡均幂等 | 生效 |
| 2026-09-01 | Dino Graph 进入既有 gameplay repeat；无 live target 由 C# 恢复村庄导航 | 生效 |
| 2026-09-01 | `Unit` 生命在 `Awake()` 初始化，避免守卫首帧把新生 Dino 当作死亡对象 | 生效 |
| 2026-09-01 | 修复前 checkpoint 统一标为 pre-fix，不与 post-fix 训练等价比较 | 生效 |

---

## 验证证据索引

### 2026-09-05：收尾开源范围咨询（建议，未执行发布）

- 用户询问“PolicyFlow训练与AI Strategy接入后停止后续扩展”的GitHub上传范围。本次仅提供交付建议，不改变现有阶段范围、不停止训练、不发布仓库；若正式采用缩减范围，保留阶段8未完成项和阶段9–10未开始事实，另记录发布范围。
- 只读核对根目录/Unity `.gitignore`、Unity Packages manifest、Python依赖文件、部署ONNX大小、原游戏README及polyperfect许可证。发现根目录当前忽略整个`dino-attack/`，Unity依赖`file:../../ml-agents/com.unity.ml-agents`；公开版本需明确单仓库或子模块组织，并提供固定版本依赖与必要补丁。
- 原游戏代码为MIT；`dino-attack/Assets/LlamAcademy/Dinos/Vendor/polyperfect/LICENSE`明确限制教育用途且禁止再分发和衍生使用，不能据代码MIT许可直接公开整个Assets或含这些资产的Player。建议先排除/替换受限资产及其复制、合并产物，或取得明确授权；模型、数据与其他第三方组件分别核对来源和许可。
- 推荐源码/测试/正式配置/小型评估证据入Git，完整模型checkpoint、成功轨迹和原始日志作为独立版本附件；Player仅在资产分发许可满足后发布。保留失败实验摘要，不上传本机缓存、训练环境、工作树或全部历史运行目录。本次未修改源码、场景、依赖与模型。

| 范围 | 主要证据 |
|---|---|
| 阶段 2 | `reports/phase2/` |
| Gaussian PPO 3DBall | `reports/phase3/` |
| Flow 基础 | `reports/phase4/` |
| FPO 3DBall | `reports/phase5/` |
| ReinFlow 3DBall | `reports/phase6/` |
| PolicyFlow 3DBall | `reports/phase7/` |
| 玩家场景/地图/美术 | `reports/phase8/open-tropical-battlefield/`、`quadrilateral-terrain-zones/`、`visual-polish/` |
| 房屋/导航/战斗 | `reports/phase8/house-targeting-navigation/`、`dino-combat-behavior/` |
| v2 协议 | `reports/phase8/structured-parallel-training/` |
| 双地图 | `reports/phase8/layered-battlefield/` |
| PPO | `reports/phase8/ppo-baseline/` |
| FPO | `reports/phase8/fpo-training/` |
| PolicyFlow准备 | `reports/phase8/policyflow-training/` |

所有原始运行目录、失败运行、旧 checkpoint、旧模型和详细报告继续保留；本文件只维护状态与索引，不复制逐命令流水账。

## 计划维护规则

- 阶段状态仅使用：`未开始`、`进行中`、`阻塞`、`已完成`。
- 原则上只有一个实现阶段为 `进行中`；独立调查可并列，但需说明。
- 勾选任务时在本文件更新状态，并把详细命令、指标和产物写入对应 `reports/` 报告。
- 范围、接口、依赖、指标或顺序变化时，先更新“关键决策记录”，再更新任务。
- 历史决策不删除；被替代内容标记“已取代”并指向新决策。
- 遇到无法安全推断的算法细节，先核对论文和固定官方仓库，再记录结论与来源。

### 追加记录模板

```text
日期：YYYY-MM-DD
阶段/任务：阶段 N / TASK-N.M
结果：PASS/FAIL；关键测试、步数与指标
产物：<相对路径>
状态变化：<复选框/阶段状态/阻塞项>
决策：<若有，写入关键决策记录>
```

### 2026-09-02：主计划简化与运行状态复核

- 将原 307,487-byte、1,732 行的“计划 + 逐次实验流水账”重构为执行看板、稳定契约、活动任务、决策摘要和证据索引。
- 未删除外部规格、实施计划、报告、运行目录、checkpoint 或历史模型；详细证据继续由原路径承载。
- 只读复核 runtimefix PPO：PID 文件为 `58956`，当前无对应进程；`updates.csv` 最后为 `147,557` steps / 9 updates，最后完整 checkpoint 为 `step-131162.pt`，无最终 `summary.json`。任务状态据此改为“中断、待审计/恢复”。

### 2026-09-02：runtimefix PPO 手动恢复后暂停

- 原训练的 24 个 Player 同时记录 `GRPC Stream removed`，Python stderr 无 traceback，判定为训练器被外部终止而非单环境或 PPO 数值失败。
- 新配置 `phase8_dino_ppo_runtimefix_tuned_24env_seed0_resume_step131162.yaml` 已验证从完整 restart boundary 恢复：`131,162` environment steps、policy version 8、8 updates，并恢复动作/更新 generator state。
- 恢复运行完成一个正常 update 至 `147,562` steps 后，用户要求暂停；已向前台训练会话发送中断，确认 Python 与 24 个 Unity Player 残留均为 0。
- 暂停时尚未到下一 checkpoint interval，因此可恢复 checkpoint 仍为原运行的 `step-131162.pt`；再次恢复会重算其后的一个 rollout，不会覆盖现有目录。`PPO runtimefix seed0 每30分钟监控` heartbeat 同步设为 `PAUSED`。

### 2026-09-02：runtimefix PPO seed0 正式训练取消

- 用户明确取消本次 runtimefix seed0 正式训练并要求删除相关产物；该运行不再恢复，也不进入 checkpoint 选择、冻结评估或默认 AI Strategy 导出。
- 已不可恢复删除原正式 run、恢复 run、其中的 `step-131162.pt`、启动 stdout/stderr、PID 文件，以及正式/恢复两份 YAML 配置，总计约 8.23 MB。
- 保留范围：runtimefix 源码修复、独立 Player 构建、Unity RED/GREEN 回归证据、可复现评估接口以及 `runtimefix-24env-smoke-s0-4096-20260902` smoke 产物；这些属于修复验证而非已取消的正式训练。
- `PPO runtimefix seed0 每30分钟监控` 保持 `PAUSED`；确认无 flow-rl Python 训练进程和 runtimefix Unity Player 残留。

### 2026-09-02：终局触发对象与 outcome reason 审计

- 为 `RoundManager` 增加一次性结构化 `DINO_SESSION_OUTCOME`：记录胜负、明确终局原因、地图/布局 seed、控制器、终局资源，以及触发对象的名称、完整运行时类型、instance id、世界坐标和生命值。
- 自然终止原因区分为 `TargetRadiusEntered`、`SessionTimeout`、`ResourceExhaustion`；保留原 `BeginEnding(bool)` 接口和奖励冻结顺序，奖励数值不变。
- 胜利入口已收紧：只有本局 `ActiveDinos` 中 `Health > 0` 的 `Dino` 可触发胜利；`null`、非 Dino、死亡或未登记 Dino 被拒绝并写 `DINO_VICTORY_TRIGGER_REJECTED`。
- RED 证据确认旧逻辑会把 `VillageHouse` 错判为胜利；GREEN 定向测试 `1/1 PASS`。胜利门、奖励冻结、50 秒超时和 20 次重载胜负矩阵合并回归 `4/4 PASS`（11.890 s）。
- 完整解决方案编译 `17` 个项目、`0 error`；9 个既有 warning 未隐藏。修复前 Player/checkpoint 标记为 `pre-victory-guard`，不得与修复后训练等价比较或部署。
- 两次沙箱内 Licensing Client 超时保留作基础设施失败证据；沙箱外测试已取得正式 PASS，不再构成功能阻塞。详细证据：`reports/phase8/ppo-baseline/outcome-audit-20260902/README.md`、`victory-guard-red.xml/.log`、`victory-guard-green.xml/.log`、`victory-guard-regression-green.xml/.log`。

### 2026-09-02：TrainingAI / InferenceAI 生命周期对照

- 新增 PlayMode 对照 `TrainingAndInference_SameSeedAndActionsKeepGameplayLifecycleEquivalent`，训练侧走真实 `--dino-training` 命令行入口，推理侧走 `GameLaunchContext` 与推理控制器。
- 固定同一地图1布局 seed 和同一资源不足部署动作，动作前后六路观察、资源、存活恐龙、非法动作反馈及 `Running -> Ending -> Ended` 状态序列一致。
- 保留并验证设计差异：TrainingAI 不生成玩家对比快照；InferenceAI 生成带相同布局 seed 和控制器身份的快照。
- Unity 6000.3.22f1 定向 PlayMode 连续两次 `1/1 PASS`（5.423 s、5.423 s）；证据为 `reports/phase8/ppo-baseline/lifecycle-equivalence-20260902.md`、`lifecycle-equivalence-green-9.xml` 与 `lifecycle-equivalence-green-10.xml`。
- 当前边界：尚未覆盖地图2、合法部署后的战斗轨迹 hash 和多次真实 Player 重载矩阵；相关任务保持未完成。

### 2026-09-02：双地图随机策略自然生命周期矩阵

- 新增 PlayMode 矩阵 `TrainingAndInference_RandomPolicyAcrossBothMapsKeepsLifecycleEquivalent`：地图1/地图2各 3 seed，TrainingAI 与 InferenceAI 使用相同随机动作，最多 128 次决策并推进真实战斗直到自然终局。
- 12/12 episode 均完成 `Running -> Ending -> Ended`；地图2 InferenceAI 快照 3/3 成功，地图1仅 1/3 成功。
- 已确认地图1 `OpenTropicalBattlefieldLayoutSignatureProvider` 在终局依赖当前 `3墙 + 3墙卫 + 5地面守卫`，战斗减员后 `TryCapture()` 返回 false，进而使 `GameResultCollector` 无法冻结 InferenceAI 结果；地图2使用开局保存的 `AppliedSignature`，不受影响。
- 最终矩阵按聚合缺陷门预期为 FAIL：`InferenceAI result lifecycle failed to freeze 2/6 natural random-policy episodes`；配对终局不同 1/6、决策数不同 3/6，属于独立物理轨迹差异，未据此认定控制器生命周期错误。
- 详细证据：`reports/phase8/ppo-baseline/lifecycle-random-dualmap-20260902.md`、`lifecycle-random-dualmap-natural-matrix-3.xml` 和同名 `.log`。
- 下一步必须把地图1签名改为战斗前冻结并重跑同一矩阵；本次仅调查和建立回归，未修改生产代码。

### 2026-09-02：post-victory-guard Unity Player 重建

- 使用 Unity `6000.3.22f1` 和正式 `DinoPPOBaselinePlayerBuilder` 成功重建 Windows x86_64 Player；入口为 `MapSelect`，包含 `Dinos` 与 `LayeredBattlefield`，BuildPipeline/Unity CLI/provenance 均为成功。
- 正式产物仍位于 `reports/phase8/ppo-baseline/build-runtimefix-20260902/DinoAttackDualMapPPOV2-RuntimeFix-20260902.exe`，SHA-256 为 `4B93B6A66C6A7895AA1390F230FCC2B2C1D0A572F9CE232F10ADBC5E0E8ADDF0`；旧 Player 已可恢复归档到相邻 `build-runtimefix-20260902-pre-victory-guard/`。
- 实际 Player 冒烟在两张地图产生的胜利触发对象均为 `LlamAcademy.Dinos.Unit.Dino`；冻结 ML-Agents Python 环境 LLAPI 握手成功，六路 v2 shape、4D 动作和初始 Decision/Terminal=`1/0` 全部匹配契约。
- 本次只完成环境 Player 重建与启动/协议验证，不恢复旧 checkpoint 资格，也不等同于正式训练完成。详细证据：`reports/phase8/ppo-baseline/victory-guard-build-20260902/README.md`、`build.log`、`provenance.json`、`player-smoke.log`、`llapi-smoke.log`。

### 2026-09-02：新 runtimefix PPO seed0 启动

- 用户批准开启一份新的 PPO 正式训练且只运行 seed0；配置为 `flow_rl/configs/phase8_dino_ppo_runtimefix_tuned_24env_seed0_retrain_20260902.yaml`，`resume_checkpoint: null`，不启动 seed1/2。
- 使用 post-victory-guard runtimefix Player、24 环境、rollout `16,384`、总步数/schedule `1,572,864`、batch `512`、5 epochs、CUDA 与异步动态 GPU microbatch；输出写入新的空目录 `reports/phase8/ppo-baseline/runtimefix-tuned-24env-seed0-retrain-1572864-r16384-b512-e5-20260902/`。
- 配置预检确认 build/CUDA 可用、run 目录启动前不存在；相关 Python 回归 `53 passed in 6.31s`。
- 首次后台命令因 Windows 含空格路径引用错误而在 argparse 阶段自行退出，未创建训练 run；修正引用后正式主进程 PID 为 `34324`，日志位于 `reports/phase8/ppo-baseline/runtimefix-seed0-retrain-launch-20260902-a2/`。
- 启动验收：24/24 runtimefix Player 存活，主进程持续运行；观测到 `32,808` transitions、2 次 PPO 更新，map1/map2 均有自然终止与成功回合，未扫描到 traceback、连接失败或 Unity 运行时异常。训练仍在进行中。

### 2026-09-02：新 runtimefix PPO seed0 暂停

- 用户要求暂停后，先确认完整 checkpoint `checkpoints/step-131189.pt` 已原子落盘（6,329,940 bytes），再停止主进程 PID `34324`。
- 停止时 `updates.csv` 已写到 `147,577` transitions / 9 updates；最后完整 checkpoint 对应 `131,189` steps / 8 updates，因此恢复时会重算其后的不完整区间。
- 已确认原 Python 主进程和 24 个 `DinoAttackDualMapPPOV2-RuntimeFix-20260902` Player 全部退出；运行目录、日志和 checkpoint 完整保留，无最终 `summary.json`，状态为暂停而非完成。
- checkpoint 内容复核通过：`step-131189.pt` 含 `model_state`、`optimizer_state`、`normalizer_state`、`config` 与 `metadata`；模型共 193 个 tensor / 505,673 个参数值，含 actor/critic Set Transformer，优化器保存 191 份参数状态，metadata 为 seed0、131,189 steps、policy version 8。
- 后续必须以 `step-131189.pt` 为源在新的空 run 目录恢复；继续遵守只运行 PPO seed0、不启动 seed1/2 的决定。

### 2026-09-02：暂停训练冷启动高胜率审计

- 首个 PPO update 已记录 map1 `89/123 = 72.36%`、map2 `68/119 = 57.14%` 成功；整体 Unity outcome 为 map1 `792/1,145 = 69.17%`、map2 `556/1,144 = 48.60%`，最早胜利分别发生于 8.59 s 与 7.40 s，确认“开局成功过多”是实际环境结果而非曲线显示误差。
- `effective-config.json` 指向 13:05 重建的 `build-runtimefix-20260902`；该目录 `Assembly-CSharp.dll` 为 204,288 bytes、SHA-256 `AF72CD68A8DF755DC9E18E10129B53EFDA4567E060459880070E2F23C837C921`，与 10:50 pre-victory-guard 版本的 201,216 bytes / `842A0C682EE221AE0CF92B57A58308DAC88D1E64D592149195B4403ACE3AA19B` 不同，确认训练使用了新 Player 游戏代码。
- Python `success` 只在 natural terminal 且 terminal reward `>= 5` 时为真；本次 1,316 条 CSV success 的 terminal reward 为 `9.98–10.23`，923 条失败为 `-1.02–-1.00`，未发现失败误标。
- 24 份 Player 日志中 1,348 条胜利全部为 `reason=TargetRadiusEntered` 且 `trigger_type=LlamAcademy.Dinos.Unit.Dino`，0 条 rejected trigger；触发位置与两图正式 `DinoTarget` 相符。旧的 VillageHouse/非 Dino 错误触发未复现。
- 现行批准规格明确“恐龙到达唯一 DinoTarget 对应范围即胜利”，不要求清空防御或摧毁核心。因此已排除“旧 Player”与“训练器成功分类错误”；尚待确认的是用户是否要改变胜利语义，或保留语义但提高地图、防御、部署与目标可达难度。详细证据：`reports/phase8/ppo-baseline/cold-start-success-audit-20260902.md`。

### 2026-09-02：step-131189 冻结 TrainingAI / InferenceAI 胜率对照

- 对暂停 checkpoint `step-131189.pt` 执行不更新参数的双地图冻结评估：deterministic TrainingAI 为地图1 `0/20`、地图2 `0/20`；stochastic TrainingAI（`policy_seed=0`）为地图1 `3/20`、地图2 `1/20`，总体 `4/40 = 10%`。
- 将同一 checkpoint 导出为仅供评估的 ONNX 副本，通过真实 Unity Sentis `DinoSentisPolicyRunner` 与 `DinoInferenceController`，在 `evaluation_seed=101`、`environment_index=0` 的同一布局 seed 流各跑 20 局；InferenceAI 为地图1 `0/20`、地图2 `0/20`，定向 PlayMode `1/1 PASS`（25.406 s）。未替换 `Resources/DinoInference/DefaultModel.asset`，未恢复训练。
- deterministic TrainingAI 与 InferenceAI 的 40 个配对胜负全部一致，但终止类别仅 38/40 一致；平均绝对终局时间差为地图1约 `7.50 s`、地图2约 `8.90 s`。因此确认的是当前样本胜率一致，而非物理轨迹逐帧等价。
- 训练日志高胜率混合随机采样、历史 policy version 与在线更新，不能替代冻结 checkpoint 评估。`step-131189.pt` 当前不具备默认 AI Strategy 资格。证据：`reports/phase8/ppo-baseline/frozen-step131189-training-vs-inference-win-rate-20260902.md`、同目录 `frozen-step131189-inference-eval-20260902.xml/.log`，以及两份 `step131189-frozen-trainingai-*/evaluation.json`。

### 2026-09-02：冷启动高胜率代码与统计语义复核

- 已排除 update 后立即持久化与跨 update `EpisodeTracker` 造成成功膨胀：`episodes.csv` 2,239 行与 `updates.csv` natural terminal 合计 2,239 完全一致，terminal 键重复 0、success 分类不一致 0、truncated 0；定向 Python 回归 `28 passed in 2.08s`。
- `update_index=1, policy_version=0` 在第一次 optimizer update 完成后落盘，但内容是优化前初始策略的 rollout；因此“第 1 次更新后的胜率”是展示时点误读，正确含义是“用于第 1 次更新的 rollout 胜率”，不影响 89/123 与 68/119 的真实计数。
- 初始 Gaussian `log_std=0`（std=1）产生强随机探索；seed0 初始化的零观测诊断中约 70.23% 动作尝试部署。首 rollout 的 157 个真实胜利中，Pachy 占 117（map1 51、map2 66）；胜局平均约 18.32/20.52 秒进入目标，且触发时约仍有 2.36/1.97 只活恐龙。
- 冷启动高胜率的直接机制是“随机快速耗尽资源批量投放 + 任一存活恐龙进入 DinoTarget 即整局胜利 + 无局部目标时直接向 DinoTarget 导航”，尤其是 map2 50 初始肉可部署两只成本25的 Pachy；不是 PPO 在首 update 前完成学习，也不是 CSV/TensorBoard 误标。
- TensorBoard 当前把同 update 的每局二值 success 以同一 global step 写入，并非预聚合 per-update rate；UI 平滑/抽样可能误导，严格胜率应从 `episodes.csv` 聚合。冻结 checkpoint 与在线日志的巨大差距仍是独立未决问题，下一步需同 seed 恢复 action-generator state 并做逐决策动作 hash。详细证据：`reports/phase8/ppo-baseline/cold-start-success-code-audit-20260902.md`。

### 2026-09-02：TrainingAI 训练与冻结评估步骤等价性复核

- 确认训练与冻结评估均使用同一 runtimefix Player、`--dino-training` 入口、`DinoTrainingAgent`、v2 观察/动作/奖励/自然终止链和 25 Academy-step 决策周期；没有证据表明 Unity gameplay 生命周期分叉。
- 确认当前 stochastic evaluator 不是训练 rollout replay：训练 action generator 从 CUDA seed1000 开始，checkpoint 保存状态已推进 43,392 个计数单位；10% 评估却以 `policy_seed=0` 创建全新 generator，未读取 checkpoint 的 `action_generator_state`。
- 训练为 base seed0、environment index 0–23、24 环境异步动态微批；评估为 base seed101、environment index0、单环境 batch1，因此布局 seed 流、随机动作分配和调用顺序均不同。
- 结论边界：`4/40 = 10%` 是同参数的新随机试验，不是训练曲线复现；当前支持“相同 Unity 生命周期、不同实验轨迹”。严格结论仍需 checkpoint RNG + 原布局/环境/批次顺序的逐决策 observation/action hash replay。训练保持暂停，未修改模型、游戏参数或 checkpoint。证据补充于 `reports/phase8/ppo-baseline/cold-start-success-code-audit-20260902.md`。

### 2026-09-02：TrainingAI 负载敏感性根因复现

- 控制 evaluation/policy seed 后，`step-131189.pt` 三组20×2局结果均为0/40，排除 seed 差异足以解释训练约50%–60%与冻结0%–10%的落差。
- checkpoint Set Transformer 的同观察 batch1/batch24 actor mean 最大差仅 `7.45e-9`、critic差0，排除跨 batch 样本混合；重建 seed0 初始网络的单 Player 冻结评估也仅5/40，证明异常在 PPO 更新前已经存在。
- 决定性复现：相同 seed0 初始网络、environment index7、policy seed1007及相同前10个 layout seed，高并发负载为9/10胜，单独运行为0/10胜；对应 Player 约16与33 decisions/s。原24环境训练每 Player 约12 decisions/s，首 rollout约64.9%胜。
- 当前根因收窄为 Unity gameplay 对并发负载/帧推进速度敏感：`time_scale=20` 下 Academy/FixedUpdate 与 Update/NavMesh/Animator/Behavior Graph 混合时基改变有效动力学。具体首次分叉组件仍需逐 Academy-step state hash 定位。
- `TASK-8.12` 保持暂停并新增前置门：先建立1/4/24 Player同 seed、同动作的负载不变轨迹，再决定时基修复与重训。现 checkpoint、训练曲线和默认模型继续不具备部署/算法比较资格。详细证据：`reports/phase8/ppo-baseline/training-evaluation-load-sensitivity-audit-20260902.md`。

### 2026-09-02：TrainingAI Setup/Start 放置假设排除

- 代码路径确认 `DinoTrainingBootstrap` 等到 Setup 后立即 `StartRound()`；scheduler 在非 Running 时绝不 `RequestDecision()`。ML-Agents Agent 初始化不会自动请求 Decision，因此正式训练不存在“策略先在 Setup 放置、但游戏未 Start”的窗口。
- 既有 PlayMode 已覆盖 Setup 不消费 Decision、Running 首步立即决策和之后每 25 Academy steps 决策，结果 `1/1 PASS`。
- 对原 checkpoint/build、evaluation seed101、policy seed0 再跑 20×2 局并临时计数 evaluator 地图就绪分支：40 次首决策检查全部 ready，强制 Wait `0` 次；该分支没有吞掉首个放置动作。
- 探针复跑胜率 `1/40 = 2.5%`，原同 seed 为 `4/40 = 10%`，且布局 seed 流相同；证明单环境冻结战斗并非逐运行 bitwise deterministic，但两次均低，Setup/Start 不是训练高胜率落差原因。后续应优先做逐决策 observation/action hash 和 1-env/24-env 并发负载对照。产物：`reports/phase8/ppo-baseline/step131189-start-phase-probe-seed101-policy0-20x2-20260902/`；训练保持暂停。

### 2026-09-02：PPO seed0 改为 8-env 从头正式训练

- 用户明确将 PPO 正式训练环境数改为 8 并要求开始训练；该决定取代 24-env 正式合同与恢复 `step-131189.pt` 的安排。新 run 使用随机初始化、`resume_checkpoint: null`，不混入历史 24-env 参数状态。
- 配置为 `flow_rl/configs/phase8_dino_ppo_runtimefix_tuned_8env_seed0_fresh_20260902.yaml`：8 env、动态推理批上限 8、2 ms 聚合窗、总步数/schedule `1,572,864`、rollout `16,384`、batch `512`、5 epochs、CUDA、seed0；不启动 seed1/2。
- 启动前配置测试先 RED 后 GREEN；PPO config 定向测试 `25 passed`，trainer/config/resume/evaluator/parallel-env/tracking 合并回归 `61 passed in 5.30s`。run 目录启动前不存在，Player SHA-256 匹配 post-victory-guard build。
- 后台主进程 PID `20752` 于 15:16:28 启动，8/8 Player 存活。首个 update 为 `16,389` transitions、约 `195.868 steps/s`；map1/map2 自然终局 `104/107`、truncated `0/0`，初始 policy 胜率 `39.42%/38.32%`，KL/clip fraction/actor/critic gradient norm 均有限。
- 8 份 Unity 日志未匹配 traceback、GRPC stream removed、非法胜利触发、CUDA OOM、崩溃或关键运行时异常。详细证据：`reports/phase8/ppo-baseline/runtimefix-8env-seed0-fresh-launch-20260902/launch.md` 及正式运行目录。
- `PPO runtimefix seed0 每30分钟监控` 已更新到本次 8-env run、绑定当前任务并恢复为 ACTIVE；训练完成后按既有授权直接执行冻结评估与合格接入，不以训练曲线替代资格门。

### 2026-09-02：负载敏感性的混合时基机制确认

- 本地 ML-Agents 调用链确认：Dino PPO 训练与评估均显式设置 `time_scale=20, target_frame_rate=-1, capture_frame_rate=0`；Unity 端由 EngineConfigurationChannel 直接写入 `Time.timeScale`、`Application.targetFrameRate` 与 `Time.captureFramerate`。当前配置关闭固定帧间隔，而 ML-Agents 默认 `capture_frame_rate` 原为60。
- 游戏权威逻辑同时依赖多种时基：回合超时使用 `Update/Time.deltaTime`，攻击冷却使用 `Time.time`，延迟伤害使用 `WaitForSeconds`，巡逻与部分移动使用 `Time.deltaTime`，并叠加 NavMesh、Behavior Graph、Animator 与25 Academy-step决策周期。
- 静态代码另发现 `MoveToTargetOrEdgeOfNavMeshAction` 给 `NavMeshAgent.velocity` 赋值时额外乘 `Time.deltaTime`，这是明确的帧率敏感风险，但尚未用逐步 trace 证明它是首次分叉点。
- 修复门更新为：先以 `time_scale=1, capture_frame_rate=60` 建立同 seed/action 的1/4/8 Player轨迹不变性基线；若仍分叉，再把计时、战斗和权威移动统一到 Academy/fixed simulation tick。通过前不恢复训练，旧 checkpoint 和曲线继续判定无效。证据更新于 `reports/phase8/ppo-baseline/training-evaluation-load-sensitivity-audit-20260902.md`。

### 2026-09-02：`fixedDeltaTime=0.02` 仍负载敏感的专项审计

- 核对 `ProjectSettings/TimeManager.asset`、Git HEAD 与全项目赋值点：固定步序列化值确为 `0.02`，Maximum Allowed Timestep 为 `0.33333334`，未发现生产代码运行时改写 `Time.fixedDeltaTime`；现有 Player 日志尚未直接打印运行时值。
- `0.02` 是缩放游戏时间步长而非墙钟执行周期；`timeScale=20` 下理想墙钟固定步间隔约 `0.001s`、约1000 FixedUpdate/s/Player。当前8-env约24.9 decisions/s/Player，历史24-env约12，均低于每25固定步决策时的理想40。
- ML-Agents Academy 每个 FixedUpdate 推进一次；Unity Behavior 1.0.13 的 `BehaviorGraphAgent` 明确每个 `Update` 只 `Tick()` 一次。攻击、协程、回合计时与导航状态检查也含帧驱动入口，因此固定步大小不变仍不能固定每个策略周期内的战斗AI tick数量。
- 决策保持：当前8-env run 本次未被调查探针打断，但结果标记为 `timebase-audit pending`，不得越过负载不变性与冻结评估门。先补运行时一次性时间参数/计数日志，再做 timeScale 1/5/10/20、1/4/8 Player同seed/action矩阵；若高倍不通过，统一 gameplay-authoritative fixed tick 后从头训练。
- 详细报告：`reports/phase8/ppo-baseline/fixed-timestep-runtime-timing-audit-20260902.md`。

### 2026-09-02：PPO seed0 改为 4-env 从头正式训练

- 用户明确要求开启新的训练并将环境数设为 4；该决定取代仍在运行的 8-env seed0 合同。旧训练在完整 `step-524455.pt` 落盘后停止，`updates.csv` 保留到 `540,843` steps / 33 updates，Python PID `20752` 与 8 个 Player 全部退出，未生成 `summary.json`。
- 新配置为 `flow_rl/configs/phase8_dino_ppo_runtimefix_tuned_4env_seed0_fresh_20260902.yaml`：4 env、动态推理批上限 4、2 ms 聚合窗、worker base 1900、总步数/schedule `1,572,864`、rollout `16,384`、batch `512`、5 epochs、CUDA、seed0、`resume_checkpoint: null`；其余 tuned 参数不变。
- 配置测试先 RED 后 GREEN；trainer/config/parallel-env/tracking 定向回归 `55 passed in 3.25s`，Player SHA-256 仍为 `4B93B6A66C6A7895AA1390F230FCC2B2C1D0A572F9CE232F10ADBC5E0E8ADDF0`。
- 正式主进程 PID `54584` 于 16:05 启动，4/4 Player 存活。首个 update 为 `16,385` transitions、约 `129.371 steps/s`；map1/map2 为 `7/158 = 4.43%`、`15/159 = 9.43%`，317 个自然终止、0 truncated，优化指标有限，Unity 关键异常 0。
- 4-env run 仍受既有混合时基/负载敏感性限制，训练曲线不得越过双地图冻结评估门。详细证据：`reports/phase8/ppo-baseline/runtimefix-4env-seed0-fresh-launch-20260902/launch.md`。

### 2026-09-02：4-env PPO seed0 按用户要求停止

- 用户明确要求停止训练；Python PID `54584` 与 4 个 runtimefix Player 已全部退出，匹配 Player 路径的残留进程为 0。对应的每 30 分钟监控任务已删除。
- 停止时 `updates.csv` 为 `147,466` environment steps / 9 updates；最后完整 checkpoint `step-131080.pt` 为 6,329,876 bytes，载入和 metadata 验证通过：rollout/restart boundary、0 in-flight decisions、4 env、policy version 8、optimizer updates 8。
- 累计 2,746 局：map1 `71/1,372 = 5.17%`，map2 `136/1,374 = 9.90%`，truncated 0，Unity 关键异常 0。未生成 `final.pt` 或 `summary.json`，状态为已停止/未完成，不具备默认 AI 导出资格。
- 所有 run、TensorBoard、CSV、Unity 日志和 checkpoint 均保留。若之后恢复，必须以 `step-131080.pt` 为源写入新的空目录。证据：`reports/phase8/ppo-baseline/runtimefix-4env-seed0-fresh-launch-20260902/stop.md`。

### 2026-09-02：FixedUpdate 内同步 Python 等待确认

- 本地 ML-Agents 4.0.0 调用链确认：`AcademyFixedUpdateStepper.FixedUpdate → EnvironmentStep → RemotePolicy.DecideAction → RpcCommunicator.DecideBatch → m_Client.Exchange`；同步gRPC发生在Unity主线程FixedUpdate内。无新observation时会短路，正式Dino主要在首次/每25 Academy step及terminal通信。
- 现存Timer提供直接运行证据：代表性24-env map2 Player的 `DecideAction=490.508/518.051s`（94.68%），map1为 `505.664/533.621s`（94.76%）；单Player env7 map2为 `26.718/59.076s`（45.23%）。按决策+terminal消息估算，通信等待量级从单Player约12.6ms升至24-env约72–74ms。
- `timeScale=20` 把72–74ms墙钟停顿放大为约1.44–1.48游戏秒，超过 `maximumDeltaTime=0.33333334`；下一Update的缩放 `deltaTime` 会被截断，单帧固定步追赶也限制为约16.7步，而 `unscaledDeltaTime` 才保留实际停顿。高并发与单Player因此进入不同时间处理分支。
- 结论升级：本项目确实存在“决策FixedUpdate内等待Python，恢复后形成长墙钟帧”的机制；它是负载敏感性的强支持原因，但首次游戏状态分叉仍需Academy/render/BehaviorGraph/战斗事件trace确认。证据已补入 `reports/phase8/ppo-baseline/fixed-timestep-runtime-timing-audit-20260902.md`。

### 2026-09-02：Python 回包等待因果复现与修复范围冻结

- 在同一初始 seed0 checkpoint、同一 runtimefix Player 哈希、`evaluation_seed=0`、`environment_index=7`、stochastic `policy_seed=1007` 下，仅在 Python 回传动作前插入75 ms等待；两次零等待为 `1/20`、`0/20`，两次75 ms均为 `15/20`。合并胜率由 `1/40 = 2.5%` 变为 `30/40 = 75%`，确认等待本身会改变当前环境结果，不再只是并发CPU负载相关性。
- evaluator 新增默认关闭的 `--python-response-delay-seconds` 诊断参数并写入评估摘要；定向测试先失败后通过，最终 `10 passed in 2.56s`。未修改 checkpoint、网络、PPO参数或 Unity gameplay，未启动训练。
- 修复范围冻结为：项目自有 gameplay-authoritative fixed tick；回合计时、Behavior Graph权威执行、攻击冷却/延迟伤害、巡逻/冻结/权威移动统一到该tick；修正 `NavMeshAgent.velocity` 额外乘 `Time.deltaTime`；Python补齐回包/微批/update stall和Unity tick事件计数。只把微批等待设0、降低环境数/timeScale或改capture帧率均不构成最终修复。
- 在0/25/75 ms、1/4/8 Player以及RemotePolicy/Sentis的同seed/action关键状态与终局不变性通过前，TASK-8.12继续停止，旧timebase checkpoint不得续训或部署。详细报告：`reports/phase8/ppo-baseline/python-wait-causal-audit-20260902.md`。

### 2026-09-02：固定时基第一阶段改造与 0/75 ms 复验

- 新增项目固定步 Behavior Graph driver，并将回合计时、攻击冷却/延迟伤害、巡逻、snare、OffMeshLink 移动统一到 fixed tick；修复 NavMesh velocity 额外乘 deltaTime。TrainingAI/InferenceAI 决策均接入 `Academy.AgentPreStep`。
- TrainingAI 设置 `captureDeltaTime=fixedDeltaTime/timeScale=0.001`，正式配置的 microbatch wait 改为0；Python补齐 ready-to-response、microbatch poll 与 optimizer update 时长。正式结构化训练禁用旧 runtime carving 障碍。
- Python 合并回归 `65 passed in 3.52s`；Dino 战斗 PlayMode 完整 fixture、正式训练入口与同 seed/action 生命周期定向测试 PASS；Windows Player `build-fixedclock-v2-20260902/DinoAttackDualMapPPOV2-FixedClockV2-20260902.exe` 构建成功。
- stochastic 0/75 ms 由旧版 `0/40 vs 30/40` 级别跃迁收敛为 `0/20 vs 1/20`，两组均为1047 environment steps；但 deterministic 地图1仍为806/863 steps，地图2完全一致。因此只完成第一阶段，PPO继续停止，下一步必须定位 Open Tropical NavMesh/路径首次分叉。
- 发现 Player provenance 仅哈希启动器 exe，不覆盖 `_Data`；不同构建 exe hash 相同。后续需改为完整目录或核心数据文件哈希。详细证据：`reports/phase8/ppo-baseline/fixed-clock-remediation-20260902.md`。

### 2026-09-03：Open Tropical NavMesh 就绪与路径语义修复

- 决定所有 episode（包括首局和场景重载后的后续局）都通过同一 `DinoEpisodeNavigationReadinessGate`：只有 `NavMeshManager.IsReady`、活动单位均在 NavMesh 上，且所有部署恐龙类型在各配置区域至少存在一条完整路径时，`RoundManager` 才由 `Setup` 进入 `Running`；未就绪时在后续 fixed tick 重试。
- 普通目标移动改用结构化即时路径提交，拒绝采样失败、计算失败、partial/invalid path 和 `SetPath` 失败；路径提交失败返回 Behavior Graph `Failure`。到达判定同时验证实际目标距离，消除“任意无路径即到达成功”。战斗接近路径保留原有 partial-path 兼容语义，避免扩大玩法改动。
- Open Tropical 正式场景锁定预烘焙 NavMesh，`AllowRuntimeBuild=false`；构建器会维持该合同。PlayMode 验证 `RuntimeBuildCount=0`、NavMesh ready，并验证部署 T-Rex 能通过同一 episode 就绪门。
- 红测记录为 `navigation-readiness-red-20260903.xml`（2/2 失败）和 `open-tropical-prebaked-red-20260903.xml`（runtime build 实际为4）；修复后 `navigation-final-source-tests-20260903.xml` 5/5 PASS、`navigation-final-source-combat-20260903.xml` 1/1 PASS、20局重载与 TrainingAI/InferenceAI 生命周期定向测试 PASS，最终 Player 构建成功。
- 冻结策略 20 局 deterministic 0/75 ms Player 对照为 `1652/1654` environment steps；忽略仅运行时实例 ID 的差异后，19/20 业务终局逐字段一致，地图2全部一致。唯一真实差异为首个地图1 seed `1084202712`：0 ms 在 `49.31998 s` 胜利，75 ms 在 `50.01998 s` 超时。
- 结论：就绪、路径提交、到达语义和 runtime bake 问题已修复，但严格等待不变性仍未通过。按用户决定，本轮不实现自定义固定步路径推进器；下一步只允许先对该首局 seed 增加逐 fixed-tick NavMeshAgent/Behavior Graph 状态 trace，取得首次分叉证据后再决定修复。`TASK-8.12` 继续停止，未启动训练。
- 详细报告：`reports/phase8/ppo-baseline/open-tropical-navmesh-readiness-remediation-20260903.md`。

### 2026-09-03：16-env timeScale=10 PPO 暂停与 AI Strategy 导出

- 按用户要求启动独立 16-env seed0 PPO，配置为 `timeScale=10`、异步采样、`inference_batch_size=16`、`inference_batch_wait_seconds=0`、rollout `16384`、batch `512`、5 epochs、总目标 `1,572,864` steps；使用最终 NavMesh-readiness Player。
- 用户随后要求暂停。进程停止时已完成约 `1,491,955` environment steps、90 updates；最后完整 checkpoint 为 `reports/phase8/ppo-baseline/navmesh-16env-seed0-timescale10-20260903/checkpoints/step-1442765.pt`，未生成 `final.pt`。
- 已将该完整 checkpoint 导出为结构化 v2 ONNX，并切换 `Resources/DinoInference/DefaultModel.asset` 到该 AI Strategy；旧 ONNX 未覆盖。ONNX SHA-256 为 `f2492b9ec85059bc4a05dff0155cfa7c572f0ef2e7b33f391f328f6a640d370b`，Python `onnx.checker` PASS。
- 由于严格 0/75 ms 确定性门仍未通过，该导出仅记录为用户明确要求的可运行 AI Strategy，不等同于 eligible baseline 或正式部署合格模型。Unity 批处理导入因 Licensing Client 超时未完成自动验证，需编辑器刷新后执行 Sentis 加载回归。
- 用户已将下一轮 16-env seed0 配置调整为 `rollout_size=8192`、`batch_size=512`、`epochs=5`、`timeScale=5`；配置文件为 `flow_rl/configs/phase8_dino_ppo_navmesh_16env_seed0_timescale5_rollout8192_20260903.yaml`，已通过 `DinoParallelPPOConfig.from_yaml` 校验。训练仍未启动。

### 2026-09-03：capture clock 覆盖修正与 FixedClockV5 验证

- 实测确认旧 Bootstrap 的 `TrainingTimeScale=20` 与 Python `time_scale=5` 构成双配置源，且 ML-Agents `capture_frame_rate=0` 会把 `captureDeltaTime` 清零；只在 `AgentPreStep` 重写 `captureDeltaTime` 的中间方案又被真实 Player 证明每步重置，未作为最终产物保留。
- 最终删除 Unity 内固定 time scale，读取外部生效后的 `Time.timeScale`，计算并设置持久的非零 `Time.captureFramerate`；当前 5 倍配置实测为 `250`，对应 `captureDeltaTime=0.004`。四个 Player 日志各仅记录一次修复，证明跨步稳定。
- 复核固定时基改动时另修正攻击 cooldown 用 float 保存整数 tick 的精度问题，并同步 Python checkpoint 测试到 v2/48/1/128 网络合同。
- Unity combat/fixed-clock 20/20、场景稳定性 1/1、NavMesh readiness 5/5、Python 345 passed/2 skipped；FixedClockV5 4-env smoke 为 67 transitions、4/4 worker、1 次有限 PPO 更新、关键 Unity 异常 0。
- 最终 Player 为 `reports/phase8/ppo-baseline/build-fixed-clock-v5-20260903/DinoAttackDualMapPPOV2-FixedClockV5-20260903.exe`；下一轮 seed0 配置已指向该 Player 和新空目录，但地图1最终确定性门仍未通过，因此未启动正式训练。详细报告：`reports/phase8/ppo-baseline/capture-clock-correction-audit-20260903.md`。

### 2026-09-03：FixedClockV5 20-env PPO 参数设计

- 按用户指定将下一轮 seed0 设为 20-env、`time_scale=5`、rollout `20,480`；保持 batch `512`、5 epochs、tuned schedule、紧凑 Set Transformer 和总目标 `1,572,864` 不变，`resume_checkpoint=null`。
- rollout 目标平均每环境 1,024 transitions，每 epoch 40 minibatches、每 update 200 minibatch optimizer steps；checkpoint interval 设为 `122,880`，即 6 个 nominal rollout。
- 配置层和训练器层新增 20-env 合法合同；测试先 RED（5 failed / 47 passed）后 GREEN（定向 `52 passed in 2.15s`；全套 `348 passed, 2 skipped in 10.22s`），正式 YAML 解析通过，目标 run 目录尚不存在。
- 配置为 `flow_rl/configs/phase8_dino_ppo_fixedclock_v5_20env_seed0_timescale5_rollout20480_20260903.yaml`；详细设计为 `reports/phase8/ppo-baseline/fixed-clock-v5-20env-parameter-design-20260903.md`。本次未启动训练，正式启动前需先跑 20-env smoke。

### 2026-09-03：AI Strategy 开局视觉静默调查

- 静态调用链确认 InferenceAI 第一次进入 `Running` 时立即决策，后续才按每25 Academy steps执行；不存在固定等待25步后才做第一次推理。
- 当前 Editor 运行日志直接记录多轮 InferenceAI 持续决策后的部署拒绝：`InsufficientFood`、`Overlap` 和 `UnitCapReached`。日志的1/10/20/30是 `RejectedDecisionCount`，不是第几个总决策；一局 `Overlap` 累计至少30次拒绝，最终仍为 `active_dinos=0` 并超时，确认画面无恐龙不等于没有推理。
- 复核食物边界：规则仅在 `FoodAvailable < FoodCost` 时拒绝，Pachy/Velociraptor/T-Rex成本为25/50/100，因此地图1当前食物恰为100时不可能报 `InsufficientFood`；该日志只能发生在食物已被此前成功部署消耗之后。地图2代码配置为50食物开局。
- 当前默认模型为 `ppo-navmesh-16env-seed0-timescale5-step1312449`，ONNX 使用确定性 `tanh_mean`；训练随机策略与部署时确定性 mean 的差异、连续动作离散化且缺少 action mask，强支持重复非法动作机制，但仍需单局 raw action trace 作最终逐动作确认。
- 本次只诊断，未修改代码、模型或场景；不通过缩短25-step间隔掩盖问题。详细报告：`reports/phase8/ppo-baseline/ai-strategy-initial-inactivity-audit-20260903.md`。

### 2026-09-03：step-1312449 TrainingAI / AI Strategy 过程一致性审计

- manifest 的 checkpoint 路径、checkpoint SHA-256 `35481d9...12ad`、environment steps `1,312,449` 与 optimizer updates `160` 均匹配，当前默认 ONNX SHA-256 为 `197ab4d9...51bb`；排除装错模型。
- 两条路径共享结构化观察、`Academy.AgentPreStep`、25-step 决策周期、动作 codec 与 `DinoSpawner.TryDeploy`，但训练使用 PyTorch Gaussian `deterministic=False`，AI Strategy 使用 Sentis `tanh_mean`；宿主进程与外围 episode 生命周期也不同。
- checkpoint 训练 Player 与 FixedClockV5 Player 的 `Assembly-CSharp.dll` SHA-256 分别为 `2F57CC8A...1EC2`、`4D7C2CC0...C778`；Cecil 进一步确认旧版仍有固定 `TrainingTimeScale` 和 float cooldown tick，而 V5 已使用外部实际 time scale/captureFramerate 同步和 long cooldown tick。当前 Editor DLL 的 `9AE573CB...BA2` 不能直接用于 Player 等价判断，因为它额外含 Editor 目标生成类型和 `UnityEditor.CoreModule` 引用；当前运行时源码没有晚于 V5 的修改。结论仍是只能判定模型来源/接口相符，不能判定逐步过程或胜率一致；后续若要求严格结论，需在同一完整构建、同 seed、两端确定性 `tanh_mean` 下做 observation/action/game-state hash 配对。详细报告：`reports/phase8/ppo-baseline/step1312449-training-vs-ai-strategy-process-audit-20260903.md`。

### 2026-09-04：TrainingAI 观测时间与 capture clock 审计

- 结构化 `global[0]` 明确为 `(50 - RunningFixedTickCount * fixedDeltaTime) / 50` 的 clamp 值；当前每 fixed tick 为约 0.02 游戏秒，字段不是 `Time.time`、墙钟或 Python 计时。
- ML-Agents 的同步 gRPC 等待发生在 `AcademyFixedUpdateStepper.FixedUpdate -> EnvironmentStep -> DecideAction -> BlockingUnaryCall` 内；主线程等待期间没有新的 `RoundManager.FixedUpdate`，因此只增加墙钟耗时，不增加权威回合计时或时间观测。
- FixedClockV5 在 5 倍时维持 `captureFramerate=250/captureDeltaTime=0.004`，使 capture 游戏时钟与 fixed tick 对齐；同一 fixed phase 的无显式脚本顺序最多带来一个 tick 的观测相位，不会随等待长度累计漂移。
- 该结论只覆盖时间标量；NavMesh/PlayerLoop 等其他状态的严格等待不变性仍未通过。`step-1312449` 使用的旧 Player 会被 Python `capture_frame_rate=0` 清除 capture clock，且没有 V5 修复，因此不具备同等保证。详细报告：`reports/phase8/ppo-baseline/training-observation-time-clock-audit-20260904.md`。

### 2026-09-04：FPO 训练配置审计

- seed0/seed1/smoke/benchmark 全部仍指向 `build-postfix-20260901`，使用 24 env、`time_scale=20`、rollout 16,384 和 2 ms inference batch wait；旧 Player 的核心程序集为 `DCD4DBA6...F673`，不包含 NavMesh readiness、`DinoFixedStepTime` 或 fixed Behavior Graph driver，并保留固定 `TrainingTimeScale`。
- seed0 YAML 为 `resume_checkpoint:null` 且目标目录非空，解析按设计返回 `FileExistsError`；仓库没有可直接使用的新目录恢复配置。`step-131207.pt` 虽是完整 update/restart boundary，但属于旧动力学，不得恢复进 FixedClockV5 正式比较。
- provenance 仅哈希启动器 exe，而 PostFix 与 FixedClockV5 启动器哈希都为 `4B93B6A...ADDF0`，现有恢复检查可能无法识别实际 `_Data` 不同；后续需加入完整构建指纹。
- 算法/容量合同通过：Set Transformer 48/4/8/1/0/128、FPO state128、NFE4、samples8、signed advantage、batch512、epochs3；历史 benchmark peak reserved 1.061 GiB，暂停运行9个update数值有限。定向测试 `27 passed in 3.34s`。
- 当前未改 YAML 或训练代码，未启动训练。下一步需在 V5+wait0+fresh init 前提下，由用户决定 20-env/timeScale5/rollout20480 对齐方案，或保留 24-env/rollout16384 并接受负载偏差。详细报告：`reports/phase8/fpo-training/fpo-training-config-audit-20260904.md`。

### 2026-09-04：FixedClockV5 FPO seed0 新配置

- 用户指定参考现有 PPO YAML 创建 FPO 配置；读取发现参考文件名仍标注20env/timeScale5/rollout20480，但实际内容已变为16env/timeScale1/rollout16384、总步数4,718,592。按文件当前内容作为事实来源，新 FPO 文件名按真实参数命名。
- 新配置为 `flow_rl/configs/phase8_dino_fpo_fixedclock_v5_16env_seed0_timescale1_rollout16384_20260904.yaml`：FixedClockV5、16 env、rollout 16,384、batch512、timeScale1、wait0、seed0、fresh init、独立 worker base 2800和新空 run目录；公共环境/预算/优化/编码器字段与参考 PPO 一致，FPO 保持3 epochs、NFE4、samples8、signed advantage和difference clip3。
- 新增跨配置合同测试，先因 FPO YAML 尚不存在而失败，再在建档后通过；FPO config/algorithm/model/collector/trainer/CLI 合并回归 `34 passed in 3.68s`。本次没有启动 smoke 或正式训练。

### 2026-09-04：FixedClockV5 FPO seed0 启动

- 建立匹配的 single-rollout smoke YAML；合同测试先因文件不存在失败，建档后 FPO config/algorithm/model/collector/trainer/CLI 合并回归 `35 passed in 3.35s`。
- 16-env smoke PASS：16,399 transitions、16/16环境贡献、map1/map2自然终止133/135、truncated 0、1次有限更新，KL 0.001015、flow loss 1.996857、GPU peak reserved 1.060547 GiB、Unity关键异常0；16个Player各一次初始时钟修复，后续日志均为场景重载状态确认。
- 正式 seed0 从随机初始化启动，Python PID 63896、16/16 Player在线。首个 update 为16,394 transitions、270自然终止、0 truncated，初始policy胜率map1 31/134、map2 19/136，KL 0.002488、flow loss 2.007992、梯度有限、关键Unity异常0。
- 首个 `updates.csv` 行是在完成优化后持久化优化前 policy version 0 的rollout，不解释为更新后冻结胜率。地图1严格负载不变性和默认AI冻结资格门保持不变。详细证据：`reports/phase8/fpo-training/fixed-clock-v5-16env-seed0-launch-20260904.md`。

### 2026-09-04：FixedClockV5 FPO seed0 中途策略崩塌审计

- 截至 update 115（1,885,465 steps，39.96%预算），10-update 加权成功率在 update 45 达64.57%，随后在 update 61 降至8.67%；update 50 rollout 仍为64.91%，使用其优化后的 policy version 50 在 update 51 立即降为33.68%，update 52为10.87%。
- 同期 proxy KL 从0.065升至0.320–0.530、clip fraction从18.2%升至约30%–35%、flow loss从1.54升至19.91；ratio min/max 扩张到0.06/17.19。checkpoint 显示 policy version 48→56 actor 相对L2位移17.43%，前一周期40→48仅6.85%。
- 16个环境均为generation 0、truncated 0、采样/等待稳定；CSV与Unity terminal序列逐环境一致。崩塌后SessionTimeout占比从update 50的16.6%升到update 51–55的58.1%和56–61的60.9%，确认是真实行为退化。
- Unity另有胜利后场景卸载窗口的`HealthBarCanvas.Update -> Unit.Transform`空引用，最早update 6出现且不与update 50同步，列为独立缺陷，不作为策略崩塌根因。完整报告：`reports/phase8/fpo-training/fixed-clock-v5-16env-seed0-midrun-collapse-analysis-20260904.md`。

### 2026-09-04：FPO崩塌前权重的25-update稳定性探针

- 原始 seed0 训练按用户要求在 update156 / 2,557,657 steps 后强制中断；16个Player一并退出，原目录和最后完整周期checkpoint `step-2492076.pt`保留，未生成final/summary。
- 新增 `warm_start_checkpoint`：与`resume_checkpoint`互斥，只严格加载兼容policy权重；optimizer、normalizer、全局/专用RNG、environment steps、policy version和wall clock均保持新建状态。首个checkpoint证实`environment_steps=16,390`、`policy_version=1`、`optimizer_updates=1`、`resume_source=null`，warm-start SHA-256为`6554a233a88366c53ae136855774c17fc05b82107fc42069f8d5bc4e557aef31`。定向回归共`41 passed`。
- 探针配置 `phase8_dino_fpo_fixedclock_v5_16env_seed0_warmstart_step786964_25updates_20260904.yaml`：25×16,384目标、实际409,868 transitions，`lr=1e-4`、final lr2.5e-5、epochs2、samples16、difference clip1、clip0.2→0.15；按用户决定不加入proxy-KL early stop。
- 运行PASS：25/25 update、6,781自然终局、0 truncated、16/16环境持续贡献、GPU peak reserved 2.055 GiB、参数全有限；整体成功率65.08%，map1 99.29%，map2 30.84%。五段5-update成功率64.94%/63.40%/65.00%/67.17%/64.87%，未复现旧run update51–55跌至33.7%→10.9%→7%–10%的灾难性崩塌。
- 稳定性仍非完全解决：update24/25 proxy KL 连续升至0.0425/0.0635，clip fraction为12.84%/11.07%，但全程ratio范围0.4612–1.5916，明显小于旧崩塌的约0.06–17.19。训练曲线单点最高为update24的69.96%，对应其行为policy version23 / `step-377087.pt`；最佳连续5-update窗口为16–20（67.17%），窗口末行为对应`step-311501.pt`。二者均需冻结评估，不能直接把final视为最佳模型。完整报告：`reports/phase8/fpo-training/fixed-clock-v5-16env-seed0-warmstart-step786964-lr1e4-e2-s16-dc1-25updates-20260904/warmstart-25update-analysis.md`。

### 2026-09-04：FPO曲线候选长期续训启动

- 用户要求继续FPO训练；按其既定训练曲线口径选用25-update探针中单点最高行为策略对应的`step-377087.pt`，继续采用只加载policy权重、重置actor/critic optimizer、normalizer、RNG和全部运行计数的warm-start语义，不从尾段KL连续上升的`final.pt`精确resume。
- 新配置目标4,718,592 steps（288个nominal rollout），16 env、timeScale1、rollout/checkpoint interval16,384、batch512、`lr=1e-4→2.5e-5`、epochs2、NFE4、samples16、difference clip1、clip0.2→0.15、wait0；按此前决定仍不加入proxy-KL early stop。定向回归`42 passed`。
- 第一次启动使用worker2880–2895，在第13个环境因独立PID27768占用worker2892端口而在首个update前退出；0 Player残留，失败目录保留。改用已确认空闲的worker3000–3015和新`-a2`目录后启动成功，Python PID60244、16/16 Player在线。
- 首个update验证：16,392 transitions、267自然终局、0 truncated、成功率65.17%（map1 100%、map2 30.60%），KL0.006607、clip fraction0.016502、ratio mean0.995526。首checkpoint为policy version1/optimizer update1、`resume_source=null`，warm-start源SHA-256为`a6d3876eecaa3199d436baefcbfcc45b7db29f7f20011963557e5725bf39b507`。训练当前继续运行。证据：`reports/phase8/fpo-training/fixed-clock-v5-16env-seed0-warmstart-step377087-continue4718592-lr1e4-e2-s16-dc1-20260904-a2/launch-validation.md`。

### 2026-09-04：FPO长期续训第二次策略崩塌审计

- update44使用policy version43取得77.9%（map1 100%、map2 55.8%）；其优化后policy version44在update45降至57.7%，update45的proxy KL升至0.1023/clip fraction19.15%，产生的policy version45在update46降至3.0%。update46–53持续只有0.6%–6.5%，两图同时退化。
- 16/16 worker持续贡献、generation均为0、truncated为0；Unity固定时钟保持`time_scale=1/capture_delta_time=0.0199999921`，日志无关键异常。episode length约64→100且长度≥99占比8.5%→88.6%，与大量真实`SessionTimeout`一致。
- 相邻actor checkpoint相对L2位移由正常约0.75%–0.86%增至1.33%和2.78%；学习率/clip调度平滑、梯度和参数有限。直接原因是actor更新越过可用策略区域，算法层原因是CFM loss-difference ratio加PPO clipping不构成硬信赖域，且当前约0.192的clip明显宽于官方playground默认0.05。
- 25-update探针的update24/25已出现KL0.0425/0.0635预警，实验在临界点前结束，故不能证明长期稳定。按日志版本语义，当前77.9%曲线点对应`step-705001.pt`而非`step-721398.pt`。训练未经用户授权未中断。完整报告：`reports/phase8/fpo-training/fixed-clock-v5-16env-seed0-warmstart-step377087-continue4718592-lr1e4-e2-s16-dc1-20260904-a2/sudden-collapse-analysis-20260904.md`。
- 梯度语义复核：`difference_clip`在逐MC `old_loss-new_loss` 严格越界时只令该MC项梯度归零，未越界项仍以固定`1/samples`权重反传；其后的signed PPO clipping再按advantage方向选择性归零。故`difference_clip=1`仅限制估计log-ratio到`[-1,1]`，不构成整个transition或actor参数更新的硬信赖域。

### 2026-09-04：训练并行期间的 UI 完善

- 为避免干扰正在运行的训练，在 `dino-attack/.worktrees/ui-enhancement`、分支 `codex/ui-enhancement` 独立实施；训练主检出中的 UI 文件未修改，训练进程未停止。
- 结算 UI 改为位于恐龙卡片之后渲染的全屏遮罩，比较状态与结果文字使用显式浅色和阴影；新增战局内退出按钮，只有 `Running` 可见，选择后进入失败结算。
- MapSelect 新增三恐龙介绍入口与肿头龙/迅猛龙/霸王龙卡片；结算页新增 PPO/FPO/ReinFlow 策略选择。当前 PPO 仅按现有模型合同判定可运行，FPO/ReinFlow 在未安装兼容模型时可见但禁用，不伪装复用 PPO。
- Unity 6000.3.22f1 场景修复与场景验证器成功；EditMode `17/17 PASS`，PlayMode `4/4 PASS`。详细证据：`dino-attack/reports/ui-enhancement/validation.md`。

### 2026-09-05：地图一结算 UI 点击修复

- 真实复现地图一 Start 按钮有悬停反馈但点击不进入战斗；定位为 `RuntimeUI.Awake()` 将 Start、Forfeit 和结算控制器绑定到启动阶段旧 `UIDocument` 可视树，而 Unity 随后重建了实际显示树。
- `RuntimeUI` 现缓存启动上下文，并在首帧后移除旧回调、重新查询 live root，统一重绑 Start/Forfeit、结算动作、比较 presenter、资源和恐龙卡片；新增 PlayMode 回归，要求已注册 Start 与 live-tree 实例相同，并用真实 Input System 鼠标点击开始。
- Unity 6000.3.22f1 Editor 真实鼠标回归通过：`Setup → Start → Battle → Exit Battle · Defeat → Defeat overlay → Select Map → MapSelect`；运行后 Editor.log 未发现编译错误、空引用、缺失引用或断言异常。胜利/失败共用同一重绑后的结算控制器；本次实际点测为失败路径，胜利路径等价性属于基于共同实现的推断。
- 新增定向 PlayMode 测试本次未执行：CLI 被 Licensing Client IPC 与缺失 headless editor module 阻塞，切换 GUI Test Runner 前用户按 Escape 停止窗口自动控制。此前 `17/17 EditMode`、`4/4 PlayMode` 仅作为旧 UI 基线，不冒充本次新测试结果。训练进程与训练检出均未停止或修改。详细证据：`dino-attack/.worktrees/ui-enhancement/reports/ui-enhancement/validation.md`。

### 2026-09-04：Dino PolicyFlow训练准备

- 使用CodeGraph核对只读官方`PolicyFlow/` commit `7f304b96e93b2804bdcc943710db885adddd06b8`的flow prior、additive Gaussian delta、clipped surrogate、KL调度和Brownian路径；官方仓库源码未修改。
- 本地阶段7 PolicyFlow model/algorithm/collector/config/trainer/evaluator/checkpoint定向回归`35 passed in 3.11s`，确认通用数学基础仍有效。
- 审计确认当前入口不能直接训练Dino：它是单Player、扁平MLP、running normalizer，不传Dino启动参数；现有dataset collector不按成功episode筛选，Flow BC不支持v2 Identity normalizer或Set Transformer。当前也没有Dino dataset、Dino Flow BC或通过统一冻结评估的FixedClockV5 PPO源。
- 已建立准备计划和初始smoke草案：16 env、timeScale1、rollout16384、batch512、epochs2、actor/critic LR `1e-4`、clip0.2、Brownian0.001、midpoint NFE4、wait0。正式预算因PPO/FPO当前目标不一致而保持未冻结。
- 候选教师 `reports/phase8/ppo-baseline/fixed-clock-v5-16env-seed0-timescale5-rollout20480-20260903/checkpoints/final.pt` 静态审计通过结构兼容门：算法`dino_parallel_ppo`、4,721,698 steps、v2 manifest `d322c4de...6cd7`、Set Transformer 128/48/4/8/1、Identity normalizer、FixedClockV5 build SHA-256 `4b93b6a6...addf0`。episode日志末2000局为map1 `1997/2000=99.85%`、map2 `1467/2000=73.35%`；末500局为100%/76.8%。它可作为候选成功轨迹教师，但须先通过相同build/协议的冻结独立评估，并量化地图、区域、恐龙类型和坐标模式覆盖。目录名含timescale5/rollout20480，但checkpoint及effective config实际为timeScale1/rollout16384，后续provenance必须按内部配置记录。checkpoint SHA-256为`e5be0e8f4f972ba6d133fcd099b4dbfb97cdd171a3c02483c6388fe2e0aef1f7`。

### 2026-09-05：FPO完成后自动接续PolicyFlow

- 将既有暂停自动化`fpo-seed0-30`更新并激活为`FPO结束后启动PolicyFlow`，频率30分钟。监控目标固定为`reports/phase8/fpo-training/fixed-clock-v5-16env-seed0-warmstart-step377087-continue4718592-lr1e4-e2-s16-dc1-20260904-a2`；创建时最近持久化update为212、3,475,749 environment steps，训练进程仍在，故未提前启动PolicyFlow。
- 正常完成门要求最终checkpoint/summary、达到配置预算且训练进程退出；之后必须依次完成PPO教师冻结评估、按地图平衡的成功episode采集与多峰覆盖报告、结构化Flow BC、16环境PolicyFlow、single-rollout smoke。禁止旧3DBall扁平入口、禁止以FPO checkpoint冒充Flow BC、禁止依据误导性目录名使用教师参数。
- 正式PolicyFlow seed0只有在全部门通过后启动；首个update健康后记录PID、配置、目录、指标并自动暂停心跳，防止重复启动。
- 本次未创建正式YAML、未启动训练/Player、未把3DBall或FPO checkpoint伪装成PolicyFlow初始化。详细计划：`docs/superpowers/plans/2026-09-04-dino-policyflow-training-preparation.md`；审计：`reports/phase8/policyflow-training/preparation-audit-20260904.md`。

### 2026-09-05：FPO正式完成与PolicyFlow教师冻结评估

- FPO长期run正常完成：`4,721,708 / 4,718,592` environment steps、288 updates、`summary.json status=PASS`、参数全有限、peak reserved `2.055 GiB`，最终checkpoint存在且训练器/16个Player均退出。后期proxy KL与clip fraction偏高、策略退化仍按既有分析披露，不以`PASS`等同策略质量合格。
- PolicyFlow候选PPO教师按checkpoint内部真实合同执行FixedClockV5、v2、Identity normalizer、timeScale1/rollout16384的冻结deterministic评估；seed101、每图20局得到map1 `20/20=100%`、map2 `15/20=75%`、总体`35/40=87.5%`、0 truncated、all finite。checkpoint/build/protocol SHA-256分别为`e5be0e8...ef1f7`、`4b93b6a6...addf0`、`d322c4de...6cd7`。
- 评估产物：`reports/phase8/policyflow-training/ppo-teacher-frozen-eval-seed101-20x2-20260905/evaluation.json`。教师通过冻结评估准入门；下一步只能从该PPO策略采集按地图平衡的完整自然成功episode，不得使用FPO checkpoint或旧3DBall入口。
- 成功轨迹数据层已按TDD启动：新增`DinoSuccessfulEpisodeBuffer`和逐transition的environment/process-generation/episode/map provenance；只接受terminal reward≥5的自然终止完整episode，失败、truncated、partial和超出地图配额均不进入BC数据。现已增加原子扩展NPZ/JSON保存与精确恢复、4D动作的map×zone×dino-choice及4×4坐标覆盖报告，并把VersionedBatch按Guards mask分类后桥接为完整episode；新旧dataset聚焦回归`9 passed`。16-env实际采集入口仍待完成，故不提前勾选Task 2。
- 模式报告的数据语义进一步收紧：每个transition持久化冻结`policy_version`和由下一观测`Global.last_action_valid`导出的动作合法性，非Wait部署分别统计合法/非法次数，避免把“尝试频率”误报为“合法部署频率”；扩展格式schema升为v2。两轮测试均先以5项失败确认旧接口缺口，随后新旧dataset聚焦回归`9 passed`。
- 新增`flow_rl.cli.collect_dino_ppo_demonstrations`：严格校验教师算法/build/protocol/encoder/Identity normalizer/timeScale1，使用16-env异步微批和显式stochastic policy/layout seeds，在冻结policy version上收集单一131072-transition rollout；仅输出地图平衡的完整自然成功episode，并要求16/16环境贡献、有限指标及Unity日志无关键模式。CLI先RED（模块不存在）后GREEN，collector/data/parallel trainer聚焦回归`34 passed`。
- 已启动真实采集run `reports/phase8/policyflow-training/ppo-teacher-success-dataset-seed20260905-500x2-16env-20260905-a1`：PID `47084`，worker `3400..3415`，layout/policy seed均为`20260905`，目标每图500个成功episode；启动检查确认主进程存活并开始顺序创建Player，最终dataset/summary尚未生成，故Task 2仍未完成。
- 首次真实采集在完整131072-transition rollout结束后的筛选阶段安全失败：旧桥接错误地用每个transition的当前有效Guard数反复判图，防御单位被摧毁后map1出现7个有效Guard并触发fail closed；没有发布不完整dataset，a1日志和配置原样保留。新增“回合初始8个Guard、终局7个Guard仍保持map1 provenance”回归先RED后GREEN，桥接现仅用完整episode首个观测的初始布局判图；聚焦回归仍为`34 passed`。
- 修复后以相同教师/build、worker、layout/policy seed、131072 rollout和500×2目标启动独立a2 run `reports/phase8/policyflow-training/ppo-teacher-success-dataset-seed20260905-500x2-16env-20260905-a2`，PID `24032`；禁止覆盖或把失败a1误标为完成。
- a2完成rollout后在Unity日志门安全停止：扫描器把4个Player在scene unload后的`HealthBarCanvas.Update`空引用（191次）和1次进程关闭时`DinoStructuredObservationBuilder → Agent.OnDisable`误作关键异常；堆栈审计确认仅有这两类、无rollout关键异常。新增日志分类回归先RED后GREEN，只豁免精确UI scene-unload及sensor OnDisable堆栈，其余Exception/crash/fatal/NaN/OOM继续fail closed；聚焦回归`35 passed`，对a2重放得到known benign `192`、critical `0`。
- 不覆盖a2，已以同一完整采集合同启动a3 `reports/phase8/policyflow-training/ppo-teacher-success-dataset-seed20260905-500x2-16env-20260905-a3`，PID `53516`；完成并发布dataset/summary前Task 2继续保持未完成。
- a3正常完成并退出：131079 rollout transitions、约188.23 steps/s，16/16环境各贡献8160–8230条；发布地图1/2各500个完整自然成功episode，共57459样本、1000个natural terminal row、0 truncated，六流观测/动作全有限，冻结policy version唯一为288。未纳入数据的失败episode为map1/map2 `3/304`，rollout边界partial为`13/3`。
- dataset NPZ、metadata、合法模式报告SHA-256分别为`f7d8666e...43034`、`f5270f1f...74ff1`、`700c5a73...d01e`；另存`provenance-v2.json`（SHA `2cf7413e...d22a`），其中encoder metadata和Identity normalizer state规范hash分别为`b63d1aa5...a043`、`b550818c...04fc`。合法部署map1/map2为6279/6305，覆盖合法zone×choice模式13/14、4×4坐标bin 50/77；T-Rex模式稀疏但不影响Velociraptor/Pachy多峰证据。
- 模式报告补充逐模式合法/非法计数及仅合法坐标覆盖，测试先RED后GREEN；collector/data/parallel聚焦回归现为`36 passed`。Task 2前四项完成，动作边界与固定seed复现等最终测试矩阵仍待补齐后再勾总门。
- 结构化Flow BC进入实现与训练：配置层兼容旧flat数据，同时可严格加载schema-v2 Dino数据，校验build/protocol/encoder/Identity normalizer及真实动作边界；train/validation改为按完整episode分组并逐地图分层，防止同一成功回合transition泄漏到两侧。Set Transformer 48/4/8/1/0/128作为state encoder，连接32维time embedding与`[128,128]` velocity head，动作维4、NFE4；结构化checkpoint使用schema3并保存protocol/encoder/dataset provenance。
- TDD证据：episode分层测试先RED后GREEN，Dino配置加载先因未知结构化字段RED后GREEN；Set Transformer+Identity normalizer前向flow-matching loss有限，legacy Flow BC/checkpoint回归保持通过，相关定向套件`36 passed`。正式配置`flow_rl/configs/dino_flow_bc_set_transformer_seed0.yaml`解析得到900/100个train/validation episode、51734/5725 transitions、两图均进入validation。
- 已启动50-epoch结构化Flow BC seed0：`reports/phase8/policyflow-training/flow-bc-set-transformer-seed0-20260905-a1`，PID `73660`，CUDA；启动后epoch1–3 train loss `1.3208→0.7890`、validation loss `1.2616→0.7259`，均有限。训练完成和冻结评估前不得作为PolicyFlow初始化。
- a1训练正常跑完50 epochs（train/validation loss `0.4921/0.4843`、全有限），但训练后严格checkpoint验证发现config未显式持久化通用结构化合同要求的`encoder_output_size`，因此a1标记为不合格且保留，不作为初始化。配置、resume稳定键和验证补齐`encoder_output_size=128=state_size`；legacy/structured Flow BC与checkpoint定向回归`25 passed`。
- 已在新目录`reports/phase8/policyflow-training/flow-bc-set-transformer-seed0-20260905-a2`重新启动相同50-epoch seed0训练，PID `69268`；只有a2通过严格`validate_flow_bc_checkpoint`及冻结Player评估后才可进入PolicyFlow。
- a2正常完成并退出：50 epochs、51734/5725 train/validation transitions、train/validation loss `0.492081/0.484306`、258692参数、58.46秒、全有限。`best.pt`严格`validate_flow_bc_checkpoint`通过，schema3、Set Transformer、`encoder_output_size=state_size=128`、Identity normalizer、epoch50，全部参数有限；SHA-256为`667fb23bcaeeba65db904cfcae42aafb6e68a8f2c4781ebf224edf95a6401d38`。下一门为FixedClockV5双图冻结Player评估，未评估前仍不初始化PolicyFlow。

### 2026-09-05：Dino PolicyFlow实现与正式启动门

- 用户再次要求停止训练：已通过命令行核实并停止PolicyFlow PID75236及其16个直属FixedClockV5 Player，复查目标PID均退出。最后完整保存为`reports/phase8/policyflow-training/resume-step2147452-seed0-20260905-a1/checkpoints/step-3196659.pt`，累计3196659 steps/update195。所有checkpoint与日志保留，未保存rollout丢弃；接续自动化保持PAUSED，未经用户再次要求不恢复训练，已安装AI Strategy保持原模型。

- 从step2147452恢复后的首update验证通过：update132累计2163844 steps，本批16392 transitions、16/16贡献、map1/map2自然终止137/135、0 truncated；analytic KL0.003255、approximate KL0.003248、clip fraction2.75%，所有日志数值有限，`step-2163844.pt`已保存。PID75236继续运行；上一条首update待验证已完成。

- 用户在PolicyFlow导出与地图三开始修复后授权继续训练：从`resume-step836064-seed0-20260905-a1/checkpoints/step-2147452.pt`恢复model、双optimizer、Identity normalizer、RNG及update/version131；checkpoint manager恢复与全参数有限性检查通过。参数、16env/timeScale1/rollout16384/总预算4718592不变，worker4500–4515端口检查无占用。新配置`flow_rl/configs/phase8_dino_policyflow_resume_step2147452_20260905_a1.yaml`，新run `reports/phase8/policyflow-training/resume-step2147452-seed0-20260905-a1`，启动日志目录`resume-step2147452-launch-20260905-a1`；19:13启动Python PID75236。Unity重新开始episode，首个续训update待验证。

- 修复地图三PolicyFlow选择后开始失败：上次接入遗漏`DefenseBattleCoordinator.TryStartBattle`的PolicyFlow模型分支，选择阶段可用但开始阶段config=null，TryCreate失败后SelectedStrategy被清为None。现补齐LoadPolicyFlow分支；扩展原PPO真实场景测试为共享开始/退出/原布局重试流程，新增PolicyFlow用例。Unity6000.3.22f1 PlayMode两项2/2通过，覆盖真实模型开始Running、锁定布防、退出结算及重试回到Setup。证据`reports/phase8/policyflow-training/policyflow-start-fix.xml`及同名log。此前仅模型路由验证未覆盖地图三独立开始分支，已补足；训练保持暂停。

- 用户要求暂停并导出AI Strategy：已核实并停止PID53584及16个子Player，最后完整checkpoint为`step-2147452.pt`（update/policy version131）；保留所有训练产物，未持久化rollout丢弃，自动接续仍PAUSED。增加PolicyFlow独立midpoint NFE4确定性ONNX导出（zero latent/zero delta），安装`PolicyFlowModel.asset`并接入地图一/二和地图三策略选择。FixedClockV5冻结seed20260905各20局：map1 100%、map2 50%、总体75%、0 truncated/all finite。Python导出6 passed，ONNX ReferenceEvaluator batch1/3数值对照通过；Unity真实Sentis模型导入推理PASS，独立请求路由EditMode1/1通过。报告`reports/phase8/policyflow-training/policyflow-ai-strategy-step2147452.md`。源码项目已更新，既有Standalone Player尚未重建，不声称旧exe包含新策略。

- 续训首update验证通过：update52累计852459 steps，本批16395 transitions、16/16环境贡献、两图各139自然终止、0 truncated；analytic KL0.004385、approximate KL0.004429、clip fraction4.52%、所有日志数值有限。`step-852459.pt`确认policy version52/optimizer updates52、原checkpoint恢复路径和actor/critic optimizer状态均存在。PID53584继续训练，原“首update待完成”记录已由本证据完成。

- 续训决定：用户在正式a2于update51后停止、未生成final/summary时授权继续。从`step-836064.pt`恢复model、actor/critic optimizer、Identity normalizer、RNG、policy version51及累计836064 steps，实际checkpoint manager恢复验证通过且参数全有限。保持原超参数、worker4500–4515及总预算4718592，在新目录`reports/phase8/policyflow-training/resume-step836064-seed0-20260905-a1`恢复；配置`flow_rl/configs/phase8_dino_policyflow_resume_step836064_20260905_a1.yaml`。2026-09-05 13:01启动Python PID53584，16个FixedClockV5 Player连接；Unity场景重新开始，不声明恢复原物理轨迹。首update验证待完成。

- Flow BC `best.pt` 冻结deterministic与stochastic评估均为map1 100%、map2 80%、总体90%，各40局、0 truncated、all finite、Unity日志异常0；预训练门通过。
- 新增结构化PolicyFlow三套独立Set Transformer encoder、transition-owned行为速度网格、16-env batched推理、版本隔离并行训练、严格checkpoint provenance和Dino双图冻结评估。全套回归除历史PPO配置名/内容不一致断言外为`371 passed, 2 skipped, 1 deselected`；该既有失败不是本次回归。
- 两个失败smoke保留：`std=1`随机冻结仅32.5%；`std=0.1, actor LR=1e-4`的KL 63.74、clip 94.38%、ratio `0–594.96`。二者均禁止作为正式参数。
- 合格single-rollout smoke使用`std=0.3, actor LR=5e-6, critic LR=1e-4`：16,399 transitions、16/16环境、双图各145自然终止、0 truncated、KL 0.00353、clip 3.16%、ratio `0.557–1.695`、Brownian 0.00248、显存0.184 GiB、Unity异常0。冻结deterministic/stochastic分别87.5%/75%。
- PPO与FPO的正式配置目标预算均为4,718,592环境步，PolicyFlow正式预算据此冻结；当前正式配置为`flow_rl/configs/phase8_dino_policyflow_fixedclock_v5_16env_seed0_4718592_std03_actorlr5e6_20260905_a2.yaml`，仅允许全新run目录启动。
- 正式首次`a1`启动因worker 4005端口被外部PID 28712占用，在任何update前失败退出并保留审计目录。`a2`使用worker 4500–4515成功启动，Python PID `58564`；首update 16,393 transitions、16/16环境、map1/map2自然终止144/143、0 truncated、KL 0.00355、clip 3.20%、ratio `0.551–1.563`、Brownian 0.00247，首checkpoint SHA-256 `fc079b2f...ec53af`。配置、stdout/stderr与证据位于`reports/phase8/policyflow-training/formal-seed0-launch-20260905-a2/`；FPO接续心跳已暂停，防止重复启动。
- 截止交接时正式run已完成第二update并持续运行：累计32,791 steps，第二批16,398 transitions、双图自然终止143/145、0 truncated、KL 0.00357、clip 3.27%、ratio `0.521–1.937`、delta std 0.299996，全部有限。

### 2026-09-04：地图三“防御战场”设计与实施计划

- 用户批准在 `dino-attack/.worktrees/ui-enhancement` 新增独立 `DefenseBattlefield.unity`，显示名“防御战场”；场景以地图二为模板，房屋生成、目标、Z1–Z5、地貌、摄像机、初始肉50、房屋返肉100、50秒时限、恐龙成本、v2观测和25步确定性推理均与地图二一致。地图三不加入现有训练采样，直接复用对应的地图二 PPO/FPO/ReinFlow模型。
- 玩家必须在固定、常显、与所有Z1–Z5不相交的矩形内自由放置3墙、5地面弓箭手和3法师；每墙自动附带1墙弓。连续放置、蓝/红预览、Esc取消、Setup右键移除返还库存、墙与墙弓原子移除、无额外安全间距、墙沿世界Z轴和地面守卫默认0°均已冻结。矩形精确坐标须在实现场景量测后由用户目视确认，禁止静默决定。
- 底部工具栏显示三类防御卡片、剩余数量、PPO/FPO/ReinFlow并列策略按钮、缺失条件提示和开始按钮；默认不选策略，不可用模型可见禁用。布防完成且显式选择可用策略后，在同一场景原地启动AI；模型实际加载失败时保留布局、取消选择、不计胜负。
- 地图三从防守方视角判定：恐龙进入目标或战斗中确认退出为玩家失败，50秒或AI资源耗尽为玩家胜利。HUD显示时间、策略、AI肉量、恐龙、地面守卫和墙；结算只显示胜负、策略和坚持时间，并提供原布局锁定重试、新布局和返回地图。原布局通过scene重载+相同seed+不可变防御快照重建，策略重置为未选择；布防阶段退出不计胜负，布局不写磁盘。
- 已完成规格与TDD实施计划，尚未修改Unity代码或场景：`dino-attack/.worktrees/ui-enhancement/docs/superpowers/specs/2026-09-04-dino-attack-defense-battlefield-design.md`、`dino-attack/.worktrees/ui-enhancement/docs/superpowers/plans/2026-09-04-dino-attack-defense-battlefield.md`。实施必须保留地图一/二、训练与现有dirty状态，并在矩形坐标、动态墙NavMesh、Packages/共享ProjectSettings或模型合同发生冲突时暂停询问用户。

### 2026-09-05：地图三核心实现与集成复验

- 已在独立 UI 工作树实现 `DefenseBattlefield.unity`、MapSelect第三地图入口、3墙/5地面弓箭手/3法师自由布防、每墙附带墙弓、蓝红预览、Setup右键返还、同场景PPO进攻、防守方退出失败、专用HUD/结算、原布局锁定重试与新布局/返回地图路由。PPO复用地图二默认正式六流v2模型；FPO/ReinFlow缺少兼容模型时保持可见禁用。开始按钮只有布防完成且选择可用策略后启用，旁边实时显示缺少单位和策略。
- 固定矩形落实为 `X[-24,20] / Z[-49,-11]`。实际 Windows Player 已走通首场景→退出结算→MapSelect→防御战场，确认入口、场景加载和矩形与Z1–Z5分离；精确矩形仍等待用户目视冻结。
- 验证证据：地图三聚焦 EditMode `8/8`，聚焦 PlayMode `2/2`（含真实PPO、同场景启动、3墙/11守卫注册、退出失败、相同布局重试），Windows x86_64 Player构建成功。全量 EditMode 为 `214/221`，7项均分类为地图三以外既有环境美术hash、旧v1协议和 OpenTropical旧断言失败。地图二场景 SHA-256 保持 `49857FB6EF1352F2973B699A5FA8613598D247E8D38BA39D7E124CD737E72560`。
- 实际 Player 曾发现地图三 UI Toolkit 默认字体缺中文字形且字号偏小，已改为优先使用 `Microsoft YaHei UI / Microsoft YaHei / SimHei` 并放大HUD、卡片、策略、开始和结算字号，同时新增字体/最小字号断言。随后已在集成工作树完成聚焦 EditMode/PlayMode、Windows Player构建和 `1426×746` 人工复验，中文、字号、底栏布局与透明蓝色布防区均正常。
- 验证报告：`dino-attack/.worktrees/ui-enhancement/reports/ui-enhancement/map3-validation.md`；manifest：`dino-attack/.worktrees/ui-enhancement/reports/ui-enhancement/map3-manifest.json`；实施计划已同步为“进行中”。正在运行的训练任务未被暂停、终止或修改。

### 2026-09-05：UI工作树安全整合

- 正式项目提交 `6172369` 并创建标签 `pre-ui-integration-20260905`；UI/地图三提交 `9c552ec` 并创建标签 `ui-map3-pre-integration-20260905`。独立 `codex/ui-integration` 工作树完成合并，`Dinos.unity` 保留正式 blob `f77aabcb...`，地图三由 Unity builder 重建。
- 集成复验：聚焦 EditMode `25/25`；地图三/UI PlayMode `4/4`；合并 PlayMode `46/59`，13项失败全部与正式快照 OpenTropical 基线同名，集成独有失败0；场景 validator 和 Windows x86_64 Player 构建通过。
- `1426×746` 人工流程确认地图一退出后 Defeat UI 可点击、恐龙介绍完整、地图三三类布防按钮文字与库存清晰、部署矩形为透明蓝色、地图二正常进入 Setup。Player日志未发现运行时异常。
- 集成证据：`dino-attack/.worktrees/ui-integration/reports/ui-integration/validation.md`。训练进程未暂停、终止或修改；本地 ProjectSettings/UserSettings、编辑器布局、`.vscode`、ML-Agents副本与原始日志未纳入提交。

### 2026-09-05：Player 默认进入地图选择

- 按用户要求将 `ProjectSettings/EditorBuildSettings.asset` 的首个启用场景由 `Dinos.unity` 调整为 `MapSelect.unity`；地图一、地图二和地图三仍全部保留在 Build Settings 中，训练侧显式场景/启动上下文不变。
- 新增 `DefenseBattlefieldSceneTests.PlayerBuild_StartsAtMapSelect` 回归断言。静态顺序检查通过且目标文件 `git diff --check` 无错误；Unity定向测试两次被 Licensing Client 初始化阻塞，未产生测试结果，因此不冒充已运行通过。由本任务启动的两个测试 Editor 均已终止，训练进程未触碰。

### 2026-09-05：FPO update 160 AI Strategy

- 按用户指定在约160 updates处选择checkpoint。`updates.csv`的update160由`policy_version=159`采样，因此使用优化前匹配checkpoint `step-2606819.pt`（2,606,819 environment steps、159 optimizer updates），而不是错误地使用优化update160之后写出的模型；checkpoint SHA-256为`37e69db0...b892f`。该rollout为160/217成功（73.73%），在updates 150–169中最高。
- FixedClockV5双图冻结评估：stochastic seed160000与deterministic zero-latent均为31/40（77.5%）、0 truncated、all finite；前者map1/map2为100%/55%，后者为90%/65%。部署采用可复现的zero-latent Euler NFE4，不把训练曲线冒充冻结表现。
- 导出独立六输入结构化v2 ONNX `FPOUpdate160Step2606819.onnx`，SHA-256为`faef7069...cf6eb`；Unity资源`Resources/DinoInference/FpoModel.asset`与PPO `DefaultModel`分离，PPO/FPO按钮分别解析对应模型，地图一/二及防御战场均可选择FPO。
- 验证：ONNX checker PASS；Python导出/冻结评估回归`17 passed`；Unity 6000.3.22f1批处理导入PASS；定向EditMode/Sentis模型加载与独立请求路由共`2 passed, 0 failed`。完整证据与限制见`reports/phase8/fpo-training/ai-strategy-update160-step2606819-20260905/README.md`。

### 2026-09-05：秋招项目学习讲义提示词完善

- 阶段/任务：阶段8并列文档任务，已完成；不改变实现阶段、现有验收复选框或训练安排。
- [x] 结合当前主计划全文、压缩前计划相关章节及双仓库只读状态，完善面向Unity战斗与AI岗位的讲义生成提示词。
- [x] 纳入工作/改动清单、教学结构、真实代码与历史证据核查、典型排错、AI辅助开发、面试追问和学习练习；明确固定步不等于网络帧同步、代码Agent不等于游戏内LLM、个人贡献须本人确认。
- 产物：`docs/interview/秋招项目讲解文档生成提示词.md`。本次交付是提示词，尚未执行其中要求的完整讲义生成与源码审计。
- 路径核对：用户所写桌面`PROJECT\_PLAN.md`不存在；实际采用`C:\Users\hp\Desktop\PROJECT_PLAN.md`，307487 bytes/1732行，与本计划压缩记录一致。旧计划仅作历史来源。
- 验证：2026-09-05，PowerShell读取与章节检查通过（6/6必需章节、无Unicode替换字符）；`git diff --no-index --check -- NUL docs/interview/秋招项目讲解文档生成提示词.md`无空白错误，仅有Git行尾转换提示（新文件比较退出码1）。人工检查岗位覆盖、事实边界与产物路径完成。未运行Python/Unity测试，未修改生产代码、场景、依赖、模型或训练进程。


### 2026-09-05：Unity战斗与AI项目秋招学习讲义

- 阶段/任务：阶段8并列文档任务，已完成；仅新增学习材料并追加本记录，不改变实现阶段状态、既有验收复选框或训练安排。
- [x] 完整阅读当前主计划、分段复核桌面历史计划，结合根/Unity Git状态、正式合入历史与dirty边界，使用Unity Workbench只读流程核对实现、测试和报告；有索引的仓库先使用CodeGraph，未创建或重建索引。
- [x] 交付12章自洽讲义：29项主要改动总表、两条真实运行链、10个九段式教学案例、三种Flow与PPO的数据/公式区别、30道面试题、7天学习安排，以及来源归属和待本人确认清单。
- 产物：`docs/interview/Unity战斗与AI项目_秋招学习讲义.md`。运行进度仅采用2026-09-05 15:20（Asia/Shanghai）的一次性磁盘快照，明确历史证据、未完成验收和陈旧看板冲突。
- 验证（2026-09-05）：Python标准库文档静态检查通过，12/12章、10/10案例及90/90教学字段、30/30题、7/7天；139个引用定义、159个本地链接出现位置与22个内部锚点有效，源码入口行号与所引测试名已核对；无Unicode替换字符、未闭合代码围栏或尾随空白。`git diff --no-index --check -- NUL docs/interview/Unity战斗与AI项目_秋招学习讲义.md`无空白错误，仅LF/CRLF提示。
- 边界：未运行项目Python/Unity测试或构建，未修改生产代码、场景、模型、依赖、训练配置或训练进程；旧计划未改。个人贡献、移动端性能、严格确定性、地图1结果签名及阶段8统一比较等缺口均保留。

### 2026-09-05：PPO / FPO / PolicyFlow 日志拼接与比较图

- 阶段/任务：阶段8并列只读分析任务，已完成；不改变正式统一算法比较验收状态或训练安排。
- 决策：按用户指定，FPO原始段截至`step-786964` / update48，再拼长warm-start a2的本段计数；明确这是展示口径，长段实际经过中间探针`step-377087`，省略该377,087步祖先成本，不冒充精确resume。PolicyFlow各恢复段保持绝对累计计数、不二次偏移。
- [x] 固定2026-09-05 19:20:23（Asia/Shanghai）CSV/config快照，PPO288 updates至4,721,698步；FPO48+288 updates展示至5,508,672步；PolicyFlow三段51+80+2 updates至2,180,241步。共757 updates、190,293真实terminal行，保留全部source counters和SHA-256。
- [x] 导出5张PNG/独立矢量PDF与五页合并PDF：总体/双图胜率、回报/长度/截断；吞吐/optimizer耗时/回包等待；分算法KL/clip/loss诊断；真实超参数表；LR/clip/epochs/MC samples/difference clip调度。另存合并CSV、末20万步及共同预算统计、复现脚本和中文口径说明。
- 验证：`.conda/fpo-gridworld/python.exe reports/phase8/training-comparison-20260905/test_comparison.py`先因导出模块缺失RED，实现后7/7 PASS；导出脚本退出码0，逐段终局去重、success语义、逐地图逐update终局计数、计数连续/增量、公共配置和快照hash全通过；5张PNG已逐张目视检查中文、标注和排版。
- 结果口径：各自末20万展示步在线胜率PPO86.61%、FPO25.06%、PolicyFlow77.03%；这些终点预算不同，且PolicyFlow有PPO教师+BC预训练，不能直接解释为冻结模型或从零算法排名。真实公共配置为16env/timeScale1/rollout16384，不采用误导性目录名。
- 产物：`reports/phase8/training-comparison-20260905/README.md`、`training_comparison.pdf`、`01_performance.png`至`05_parameter_schedules.png`、`merged_updates.csv`、`merged_episodes.csv`、`summary_last200k.csv`、`provenance.json`与`validation.json`。
- 边界：仅新增分析脚本、测试与报告；未改动原始日志、训练/游戏代码、配置、模型、依赖或训练进程。地图1严格确定性、统一冻结评估、多seed公平比较等既有门仍未由此次在线曲线完成。

### 2026-09-05：FPO / PolicyFlow 开局等待与奖励敏感性调查

- 阶段/任务：阶段8并列只读调查，已完成；用户请求分析原因，不授权修改奖励、算法或训练配置。
- [x] 使用Unity Bug Investigation证据优先流程，核对当前安装manifest/checkpoint/ONNX、Wait分桶、决策入口、时间观测与time FiLM、奖励/房屋返肉、GAE与FPO/PolicyFlow实现。有索引仓库先CodeGraph；未更改索引或Unity资产。
- [x] CPU冻结前向：PPO教师成功数据的1,000个开局输入（500/图）；当前FPO policy159两图全部输出Wait，PolicyFlow policy131地图二全部Wait而地图一部署。每图32状态×64随机动作中，FPO仍全Wait，PolicyFlow地图二46.875%为非Wait。PolicyFlow的BC初始化两图均不Wait，支持等待在RL微调后形成。
- [x] 只改时间、其他开局字段冻结，代表输入首次非Wait为FPO地图一15秒/地图二10秒，PolicyFlow地图二16秒；这是离线time-only敏感性，不是实际首部署时间或提前部署收益。安装ONNX两图各1输入对照最大误差均小于8e-7、类别一致，重复前向一致，源hash与开局前置条件通过。
- 结论：资源bonus与胜率目标存在真实权衡、超时形成终局增量悬崖；但不能将两算法统一归因于“探索强且看不到时间”。FPO已学到强Wait模式，PolicyFlow地图二小幅连续动作偏移跨过-0.5阈值，确定性部署放大该差异。训练成因仍需真实同seed动作trace/单变量干预，未声称唯一根因。
- 额外审计：成功BC数据57,459动作中27,620无效且BC未按action_valid过滤；bounded教师动作直接作BC终点而采样再tanh存在表示偏差。两者记录为独立改进/消融项，BC初始不Wait反证它们足以单独解释本次等待，未静默修改数据或现有checkpoint合同。
- 验证：`.conda/flow-rl/python.exe reports/phase8/training-comparison-20260905/probe_wait_behavior.py`退出0；GAE/FPO/PolicyFlow算法与模型回归26 passed in 3.68s，XML为`wait-analysis-regression.xml`。未运行Unity实时复现或构建，未影响当前训练。
- 产物：`reports/phase8/training-comparison-20260905/wait-and-reward-analysis.md`、`wait-probe.json`、`wait-probe-initial-actions.csv`、`probe_wait_behavior.py`。既有严格确定性、冻结公平比较门保持原状态。

### 2026-09-05：项目首页 README 与截图

- 阶段/任务：阶段8并列文档任务，已完成；不改变实现阶段、训练安排或既有验收门。
- [x] 新增根目录`README.md`，突出两种玩法、三地图、PPO/FPO/PolicyFlow已训练策略、外部训练到Unity推理链、快速体验与源码入口；Python安装和训练入口折叠展示。保留原游戏README，不修改源码、场景、模型或依赖。
- [x] 逐张查看用户`C:\Users\hp\Documents\New project 2\picture`中的6张截图，选4张原样复制到`docs/assets/readme/`并使用GitHub相对路径引用；未修改原图，无需新增Unity截图。
- 事实核对：使用CodeGraph优先查询及针对性源码检索核实PolicyFlow模型加载与地图三开始分支；结合manifest、接入报告和最新决策，注明已部署`step-2147452`中间模型及旧Player需重建，不把后续停止点`step-3196659`误作部署模型，不宣称正式训练预算或公平比较全部完成。
- 验证（2026-09-05）：Python标准库检查10/10本地链接有效、4/4截图SHA-256与用户原图一致、Markdown围栏/折叠标签配对、UTF-8无替换字符、无机器绝对路径和尾随空白；README共66行。`git diff --no-index --check -- NUL README.md`无空白错误，仅Git换行提示（新文件比较退出码1）。未运行Python训练/测试、Unity PlayMode或构建；原有运行状态不受影响。

### 2026-09-06：GitHub上传计划交付

- 阶段/任务：阶段8并列文档任务，已完成；上传执行未开始，研究阶段和暂停训练状态不变。
- [x] 只读核对双仓库状态、README/ignore规则、ML-Agents固定commit与本地依赖、三种部署ONNX、对应checkpoint、Flow BC和成功数据来源。
- [x] 交付`docs/release/GITHUB_UPLOAD_PLAN.md`：独立发布副本、Git/Release清单、三模型映射、依赖可移植性、五步执行和退出条件、全新clone验收与远端发布顺序。
- 验证（2026-09-06）：Python标准库静态检查通过，五个阶段/退出条件齐全、待执行复选框和六项部署资源存在性核对通过；UTF-8/代码围栏/尾随空白正常。首次检查脚本因Python3.10 f-string反斜杠语法限制未执行，修正检查脚本后通过，不涉及项目代码变更。`git diff --no-index --check -- NUL docs/release/GITHUB_UPLOAD_PLAN.md`无空白错误，仅Git换行提示（新文件比较退出码1）。
- 本次未创建发布副本或远端仓库，未整理/删除源文件，未运行项目测试/训练/Unity构建，未上传、提交或改动任何Git remote。

### 2026-09-06：面向 Unity 战斗与 AI 岗位重写 README

- 阶段/任务：阶段8并列文档任务，已完成；不改变实现阶段、暂停训练状态和既有验收门。
- [x] 根据用户指定的`docs/interview/秋招项目讲解文档生成提示词.md`、现有学习讲义、主计划最新记录与实际源码，将根目录`README.md`扩展为218行；按共享部署规则、战斗目标与生命周期、固定步/NavMesh排错、地图与UI、并行训练数据正确性、ONNX部署六项组织重点工作，并加入岗位能力映射和Mermaid数据流图。
- [x] 保留4张游戏截图与运行指南；注明三种当前部署checkpoint，区分PolicyFlow部署点2147452与后续停止点3196659。明确上游基础与本项目改造，区分固定步和多人帧同步、代码Agent和游戏内LLM，保留移动端/C++、严格确定性、公平比较及测试未全绿等证据边界。
- 事实核对：此前按CodeGraph优先规则查询Unity符号，返回不足后针对性读取目标追踪器、固定步驱动、部署规则、Unit生命周期、攻击Action与UI绑定；核对采集器、模型接入及历史战斗/导航/完成情况报告。未把历史结果描述为本轮重新验证。
- 验证（2026-09-06）：Python标准库静态检查通过，39/39本地链接（34个不同目标）有效、6个导航锚点有效、4/4截图SHA-256与用户原图一致，代码围栏及折叠标签配对、UTF-8字符/机器路径/尾随空白检查通过。首次静态检查发现并修正1处DinoSpawner路径，同时修正把HTTPS误判为Windows路径的检查正则；不涉及生产代码。
- 产物：根目录`README.md`。本次只修改README与本记录，未运行训练、项目测试、Unity PlayMode或构建，未改动源码、场景、模型、依赖和Git remote，未上传仓库。

### 2026-09-06：README 战斗实现通俗化

- 阶段/任务：阶段8并列文档任务，已完成；不改变实现状态和验收门。
- [x] 按用户选中范围，仅重写根目录`README.md`第2节：用发现目标、选择并靠近、攻击、目标死亡后继续行动四步解释具体实现，补充黑板的含义与单位出生/停用时的处理；移除问题—改造表，保留源码和验证入口，保留用户对其他章节的编辑。
- 验证（2026-09-06）：Python标准库静态检查通过，四个流程步骤、4/4本地链接、实现口吻及尾随空白检查正常。仅编辑README与本记录，未修改或运行游戏、训练与测试代码。

### 2026-09-06：README 可变实体观测与网络结构说明

- 阶段/任务：阶段8并列文档任务，已完成；不改变实现阶段或验收状态。
- [x] 按用户选中范围补充README第5节，区分有效实体数量变化与固定270标量接口，说明容量、补零、valid_mask及空集合；用四步简述实体MLP、ISAB/SAB与时间调制、区域交叉注意力和128维融合、独立Actor/Critic。
- 事实核对：动态确认`flow_rl/`无CodeGraph索引后，读取`dino_set_transformer.py`、`state_encoder.py`，检索策略/critic及PolicyFlow配置，确认48维实体特征、128维输出与独立编码器参数。
- 验证（2026-09-06）：Python标准库文档静态检查通过，四个网络步骤、5/5源码链接、关键术语与尾随空白检查正常。仅修改README与本记录，未运行训练或项目测试，未更改生产实现。

### 2026-09-06：README 整体通俗化与 Flow RL 简介

- 阶段/任务：阶段8并列文档任务，已完成；不改变实现或验收状态。
- [x] 按当前README及用户此前删减后的结构整体润色，按功能与具体流程说明部署、战斗、时间推进、地图、训练及游戏内模型运行；用实例解释可变实体观测，保留固定容量/掩码和四步网络结构。
- [x] 新增简短Flow RL介绍，说明奖励学习、初始向量经网络分步变为一次动作、训练随机采样与演示固定输入的区别，以及三种Flow方法、PPO基线和本地flow_rl包的关系。补充阅读训练器实际flow/gaussian模型源码；未新增算法性能结论。
- [x] 保留截图、源码入口、实际部署checkpoint、历史测试结果与运行命令；删除指向用户已删岗位章节的失效导航，替换为Flow RL简介导航，不恢复用户已删正文。
- 验证（2026-09-06）：Python标准库静态检查通过，225行、38/38本地链接、6/6导航锚点有效、4/4截图与用户原图SHA-256一致；代码围栏/折叠标签、UTF-8、无机器绝对路径及尾随空白检查正常。仅编辑README与本记录，未运行项目测试、Unity或训练，未修改生产实现、模型和依赖。

### 2026-09-06：README 训练与正常游玩一致性说明

- 阶段/任务：阶段8并列文档任务，已完成；不改变实现状态与严格确定性验收门。
- [x] 新增README第7节，以五项说明三控制模式共享真实Unity场景与战斗规则、训练/推理共用观测与动作解码、25 Academy-step决策周期、回合时间与胜负来源、实际构建和模型版本核对；说明训练日志与玩家结算界面的收尾差异。
- [x] 区分Python多Player训练与游戏内推理、随机探索与确定性动作、共用规则与逐步轨迹一致；明确历史19/20终局对照是训练程序内部延迟实验，不能视为训练端与游戏端完整一致性验收。
- 事实核对：动态检查Unity索引并先调用CodeGraph，结果未覆盖所需符号后检索当前训练Agent、推理控制器、观测构建器及决策调度器；参考游戏时间审计、历史指定checkpoint过程对照与当前PolicyFlow接入记录。
- 验证（2026-09-06）：Python标准库静态检查通过，新增5/5源码/报告链接有效，模式、时钟、版本与验证边界表述及尾随空白检查正常。仅编辑README与本记录，未运行训练、Unity或项目测试，未修改生产代码和模型。

### 2026-09-06：GitHub 上传请求与候选文件盘点

- 阶段/任务：阶段8并列发布准备任务，进行中；用户已明确要求提交GitHub，原上传计划E中的上传授权现已取得，但目标账号/仓库尚未提供，不改变研究验收状态。
- [x] 只读核对根/Unity远端与工作区：根仓库无remote，Unity origin为原作者llamacademy/dino-attack，不能推定为用户发布目标；已请求用户提供目标仓库或新建仓库的账号/可见性信息。
- [x] 生成`docs/release/UPLOAD_INVENTORY.json`，记录核心Unity目录、Python源码/测试/配置/脚本、README及截图的候选源路径、大小、SHA-256与双仓库HEAD；候选约396 MiB，未发现超过50 MiB的单文件。此清单不是已审核上传白名单，依赖闭包、发布文档和敏感信息检查尚未完成。
- 本机PATH未发现gh，当前无可调用GitHub连接器；待目标确定后继续选择可用认证与推送方式。未读取或输出凭据，未创建远端、发布副本或Release，未暂存/提交/推送，未修改现有remote及用户生产文件。

### 2026-09-06：GitHub 首轮公开源码发布完成

- 阶段/任务：阶段8并列源码发布任务，已完成；研究阶段8仍进行中，完整Release附件和新Unity构建不由此视为验收通过。用户明确授权在本人账号下新建公开仓库。
- 决策：本次“提交GitHub”落实为首轮源码、场景、三ONNX模型、截图与公开安装/训练/状态文档；初始上传计划的新游戏/训练Player、checkpoint/数据附件、v0.1.0 Release留作后续，已在公开首页与状态文档注明。
- [x] 发布副本`release-staging/dino-attack-flow-rl/`独立Git；源根仓库与Unity仓库remote/history不变。候选2,666文件396.16MiB，经GUID/字面引用检查排除58个未引用备份/临时/旧模型文件，保留被场景测试引用的备份。2,419个保留生产Python/Unity文件与源文件字节一致。
- [x] 公开首页保留用户正文与截图，内部报告链接汇总至随包STATUS；新增SETUP/TRAINING/VALIDATION、依赖脚本、artifacts模型索引和source-manifest。Git包含2,615文件，清单涵盖其中2,614个，约377MiB；不含旧Git历史、缓存、凭据、原始训练数据或完整工作日志。
- [x] 发布副本测试对历史本地文件的依赖改为临时生成夹具，保留真实checkpoint元数据与build hash校验；新增错误build hash拒绝测试；旧PPO断言纠正为当前16env/timeScale1/rollout16384/4718592预算，不改变实验超参数；Flow BC配置绝对路径及测试cwd依赖已在副本修正。原生产与测试源文件未覆盖。
- 验证：首次副本非集成测试367 passed、6 failed、1 skipped、3 deselected，定位历史文件依赖与旧断言；生成夹具完善后最终374 passed、1 skipped、3 deselected（12.52s），XML为`docs/release/source-publication-python-final.xml`。使用已有研究依赖环境及副本PYTHONPATH，不宣称全新Python安装或Unity重验。
- 验证：固定commit ML-Agents脚本实际下载及第二次幂等执行通过；三CLI帮助、三ONNX checker/GUID/SHA-256通过；771个文本文件凭据模式扫描无命中；公开文档链接57处有效。首提交全目录空白扫描产生既有CRLF提示，随后按cr-at-eol仅检查新增/修改文档和测试通过，未改写上游资产行尾。
- [x] 系统Git Credential Manager确认账号`xvchengxvcheng`，认证仅在内存中用于GitHub API，没有输出或保存token。创建公开仓库并推送`main`成功；沙箱/主用户目录所有权差异通过命令级精确safe.directory处理，没有修改全局安全配置。
- 远端：`https://github.com/xvchengxvcheng/dino-attack-flow-rl`；commit：`79b13c28efb82dae7b849b6ec976620735073ebb`；默认分支main。匿名API确认public；新clone位于`release-staging/verify-dino-attack-flow-rl/`，执行`.conda/flow-rl/python.exe docs/release/verify_github_clone.py release-staging/verify-dino-attack-flow-rl`，2,614文件hash与57链接PASS、commit一致。
- 其余本地产物：`docs/release/UPLOAD_INVENTORY.json`、`SOURCE_PUBLICATION_PREPARATION.json`、`verify_github_clone.py`及更新后的上传计划。未运行Unity编辑器/PlayMode/新Player构建、正式训练，未创建Release或推送原仓库历史。

### 2026-09-06：最新 Windows 游戏重建

- 阶段/任务：阶段8并列构建任务，已完成（构建、定向测试及启动检查范围）；用户要求重新构建最新游戏，保留既有Player与训练产物。阶段8科研实现总体状态不变。
- 合同：Windows x86_64，Unity6000.3.22f1，MapSelect为首场景，保留Dinos/LayeredBattlefield/DefenseBattlefield及当前PPO/FPO/PolicyFlow模型；编译、定向模型/启动测试、地图三运行回归、目标构建和启动检查，输出新目录。不隐含完成全量Unity测试或科研验收。
- 基线：采用独立发布副本`release-staging/dino-attack-flow-rl/dino-attack`，源commit79b13c28efb82dae7b849b6ec976620735073ebb；当前源项目与清单中2,330个Unity资产/包/设置文件hash一致。原项目dirty修改保留。现有CLI无可连接Pipeline实例，改用CLI批处理；许可已成功取得，正在首次导入。
- 证据目录：`reports/windows-rebuild-20260906/`。使用Unity Build Validation、Unity CLI流程，未更改场景、模型、包或已有构建。
- [x] EditMode 4/4、PlayMode 2/2通过（`editmode.xml`、`playmode.xml`）；覆盖首场景、FPO/PolicyFlow路由、地图三实际PPO/PolicyFlow开战、布防锁定与重试。
- [x] `unity build <发布副本> --target StandaloneWindows64 --output-path <新目录>/DinoAttack.exe --editor-path D:/job/6000.3.22f1/Editor/Unity.exe --log-file <报告目录>/build.log --timeout 1800 --no-tail --format json`：success=true、exit 0，约133秒。新产物`Builds/DinoAttack-Windows-20260906/`，Mono，包含当前三ONNX与MapSelect/三战场。许可握手错误已恢复，日志保留。
- [x] 新EXE启动进程有响应，图形/Mono/物理/输入初始化完成，启动日志无Exception/Error/Failed/Crash；仅停止本次验证进程。隐藏窗口不接受关闭请求，采用Stop-Process结束，未验证正常退出。证据`player-smoke.log`、`player-smoke-result.json`。
- [x] `.conda/flow-rl/python.exe reports/windows-rebuild-20260906/package_build.py`：生成`Builds/DinoAttack-Windows-20260906.zip`，109,715,068字节，232个文件逐项解压读取hash通过；SHA-256 `15b42f05ad0932d6b6c12be512436d35a59cdb9986597a7b944fc9057a81e521`。仅排除DoNotShip调试目录，包内含完整文件清单。
- 最终发布副本仅测试生成Timers JSON有Git差异；导入曾调整SENTIS_ANALYTICS_ENABLED，最终ProjectSettings无差异。原项目与旧构建未覆盖。报告`reports/windows-rebuild-20260906/README.md`。
- 限制：桌面交互工具无法初始化，未完成成品界面目视/三地图三策略全流程或全量测试；FPO运行链路未新增PlayMode验证。未上传Release，未更新冻结训练Player，本次不宣称完整发布验收。

### 2026-09-06：剩余上传任务交付

- 阶段8并列发布任务：上传已完成；完整人工游玩验收未完成，科研阶段状态不变。
- [x] `main`/`v0.1.0`最终commit `cacab8ee5653a6a7704e20ec8a4376bd9ba78515`；公开研究演示Release：https://github.com/xvchengxvcheng/dino-attack-flow-rl/releases/tag/v0.1.0，共五ZIP与两校验文件。发布过程临时标签已纠正，实际附件URL均验证可下载。
- [x] 新clone的2621清单文件/61链接、固定依赖安装和幂等重跑通过；公开下载脚本`-All`安装五包通过，七附件匿名下载hash全部匹配。远端解压checkpoint/Flow BC加载及数值对照通过；下载版游戏启动有响应，错误模式0，仅停止本次检查进程。
- 证据：`docs/release/V010_PUBLICATION_REPORT.md`、`v010-remote-verification.json`、`reports/windows-rebuild-20260906/remote-player-smoke-result.json`。五附件校验值见报告和`release-staging/artifacts/v0.1.0/SHA256SUMS.txt`。
- 保留：桌面工具不可用导致成品三地图三策略人工全流程未验；隐藏窗口正常退出未验；历史Unity失败、严格确定性和统一算法比较未完成。上传计划C仍进行中，A/B/D/E已完成，不以成功上传伪装完整验收。

### 2026-09-06：README 策略局限与 GAE 假设补充

- 阶段8并列文档任务，已完成；按用户观察补充PPO疑似局部最优、尚未稳定学会通过攻击房屋与保留食物争取更高终局奖励，以及FPO/PolicyFlow开局等待和仍需优化的状态。
- 将GAE lambda偏小影响远期终局奖励对开局优势估计的直接贡献明确写为待对照实验验证的假设，不覆盖既有等待行为调查，不宣称已定位唯一原因；区分研究探索阶段与游戏确定性推理。
- 同步根README和公开副本README，更新公开source-manifest；文档差异/空白检查和清单生成检查通过。仅提交README与清单，commit `06e2774`已推送GitHub main；v0.1.0标签、模型、训练参数和构建均未更改，未运行新训练或项目测试。
- 后续按用户要求，将“当前策略的局限与优化方向”整体移至“游戏预览”上方，提升为二级标题；两份README正文保持一致，章节顺序与空白检查通过，清单同步更新并推送GitHub。仅调整文档位置。

### 2026-09-06：英文 README 与双语导航

- 阶段8并列文档任务，已完成；按用户要求新增公开副本`README.en.md`，覆盖Flow RL简介、预览前策略局限/待验证GAE假设、四张截图、七项工程工作、网络与并行训练、过程一致性、模型/验证边界、Release体验与源码安装、来源说明，翻译Mermaid图中文字。
- 中文首页保留为默认，根README与公开README增加英文入口，英文版提供中文返回链接；公开中文运行指南中“附件尚未发布”的陈旧描述改为实际v0.1.0 Release状态。英文版明确支持文档主要为中文，未伪装已完成全库翻译。
- 验证：英文42处本地链接、7项工程章节、代码围栏、策略局限位于预览前、UTF-8及尾随空白检查通过；公开清单涵盖2622文件，凭据模式/无gitlink检查与git diff --cached --check通过。英文README、中文导航及清单已提交并推送GitHub main；未改模型、构建或训练参数，未运行新训练或项目测试。
- 后续用户指出首页仍显示中文：将公开仓库README.md改为英文默认页，原中文完整保存在README.zh-CN.md，README.en.md保留并修正中文返回链接。此决定取代此前“中文默认”的展示安排；本地工作区根中文README保留。英文默认页与英文独立页字节一致、双语链接及空白检查通过，清单更新并推送GitHub。
- 用户随后明确“首页默认中文”：上一条英文默认安排已取代。恢复README.md中文首页，English指向README.en.md，英文返回中文首页；保留README.zh-CN.md。中文两页一致、导航与空白检查通过，清单同步并推送GitHub。

- 2026-09-07：已完成重复中文 README 清理。按用户要求删除发布仓库 README.zh-CN.md（取代此前保留副本的安排），保留 README.md 默认中文首页与 README.en.md 英文版。删除前确认中文两文件字节一致且无其他受跟踪文档引用被删文件；更新 source-manifest.json（2622 项），凭据扫描与暂存区空白检查通过。提交 f7d6d9b 已推送 origin/main；既有 Unity 生成文件修改未纳入提交。

### 2026-09-12：地图四 GitHub 更新本地审核准备

- 阶段8并列发布准备，已完成本地整理，待用户审核。用户要求先整理、查看后再上传，并明确范围为地图四整套角色、第三人称、技能、倍速、动作、小地图和伤害数字。本轮仅制作本地审核增量包；不提交、不推送、不创建Release，不改原工程或既有发布副本。
- 以独立发布副本本地HEAD `f7d6d9bbba8f399ef4f3b77ea14ed93d6fc1f554`为基准，白名单复制地图四所需源码/场景/资源/meta，补充中英文说明和测试摘要。发布副本已有额外FPO PlayMode测试须保留；Unity生成的Timers/设置差异、Python研究维护、旧角色和缓存均不混入。
- [x] 完成审核清单、源码差异、文档/截图与小型验证证据。
- [x] 核对文件hash、资源引用、Git状态保护与排除范围，交给用户审核。
- 产物目录：`release-staging/map4-review-20260912/`。本轮不重新运行Unity或重建Player；既有测试/构建证据明确记录为2026-09-11历史结果，审核/上传不改变科研验收状态。
- 2026-09-12验证证据：先建立`verify_review.py`，缺少清单时按预期失败；生成增量后执行`.conda/flow-rl/python.exe release-staging/map4-review-20260912/verify_review.py`通过。163个增量文件（150新增、13替换），其中152个Unity文件全部与最终历史来源记录hash一致；3张截图逐字节复制。共23,187,731字节，ZIP为11,534,804字节并逐项读取校验163项；113处文档本地链接在基线加增量视图中有效。原工程与发布副本HEAD、index和完整status均保持；根仓库HEAD/index保持。
- 引用检查边界：100个文本GUID中没有新增未解析引用；两项原基线未解析引用为已禁用`Assets/test.unity`及`AI Tree Settings Object`，证据与路径列入`reference-check.json`。地图四为二进制场景，只核对历史hash，未解析内部引用。合并后仍需Unity导入、定向测试（保留额外FPO回归）、构建与人工游玩验证；本轮不宣称该最终合并状态已通过Unity测试。
- 审核入口`release-staging/map4-review-20260912/README.md`，拟公开文件在`payload/`；原始小型XML证据、生成器、校验器和工作区快照只留在审核目录，不进入增量ZIP。GitHub上传待用户查看后另行确认。

### 2026-09-12：基于简历的双岗位面试准备手册

- 阶段/任务：阶段8并列学习文档任务，已完成；用户明确同时准备Unity客户端/游戏AI与强化学习/算法岗位。本次新增学习材料，不改变实现阶段、既有验收复选框或训练安排。
- [x] 完整阅读当前主计划并只读检查工作区；将用户指定简历PDF渲染为图片，完整阅读教育、Dino项目、三项科研经历和专业技能。PDF文字提取存在字体映射问题，事实以可视页面为准；未修改源简历。
- [x] 交付两条岗位知识路线、两版自我介绍、60道问答及追问、8个动手练习、14天安排、3天压缩方案、模拟面试与自评标准；核对官方Unity/C#文档与PPO/GAE/Flow/MARL原始论文。
- [x] 区分个人贡献、上游与AI工具辅助；明确无人机项目缺少论文/源码/实验表，参数与效果须本人补齐。Dino历史延迟实验仅引述本计划，不把减少偏差说成完全确定性，不把不同评估设置拼接为算法提升。
- 产物：`docs/interview/基于简历_双岗位面试准备手册_20260912.md`（25,868字符、12节）。
- 验证（2026-09-12）：PowerShell调用`C:/Users/hp/.cache/codex-runtimes/codex-primary-runtime/dependencies/python/python.exe -`执行stdin标准库静态校验，退出0；Q01-Q60连续无重复，14/14天、8/8练习、12/12节、2/2本地链接存在，UTF-8无替换字符、无尾随空白，待补与证据边界检查通过。
- 边界：未运行游戏/算法测试、Unity、构建或训练；不修改源码、场景、模型、依赖与原有文档正文，仅新增手册并追加本记录。练习清单尚需用户亲自完成，材料不认证个人独立贡献。

### 2026-09-14：强化学习与生成式模型扩展应用咨询

- 阶段/任务：阶段8并列方向咨询，已完成；仅评估候选应用，不将建议视为实施授权，不改变阶段8验收门、阶段9–10状态或暂停训练安排。
- 完整阅读主计划并只读检查根目录、Unity与官方参考仓库状态。结合地图三自由布防、地图四角色与技能、现有进攻策略及结构化训练基础，建议优先考虑智能布防、守卫战斗AI、条件化战术/布局生成；另外讨论课程与难度调节、自动测试和世界模型。
- 最小候选实验：地图三固定库存，新增布防观测/动作与训练入口，先对冻结进攻策略训练，再用保留的进攻策略评估泛化。生成布局须校验库存、边界、重叠与通行；现有270/4D攻方协议及模型不能直接用于人物控制；连续动作序列仍归阶段9。以上均未实施，效果与算力成本未验证。
- 方法来源：[PCGRL](https://arxiv.org/abs/2001.09212)、[Diffusion Policy](https://diffusion-policy.cs.columbia.edu/)、[PAIRED](https://arxiv.org/abs/2012.02096)、[DreamerV3](https://danijar.com/project/dreamerv3/)。这些研究支持方法方向，不构成本项目效果证据。
- 产物为本次对话建议与本记录。验证（2026-09-14）：PowerShell/.NET追加校验通过，原文件前缀字节保持一致、记录仅出现一次、UTF-8无替换字符。未运行Python/Unity测试、构建或训练，未修改游戏/算法源码、资产、配置、模型和依赖。

### 2026-09-16：地图四行为图与强化学习混合NPC可行性

- 阶段/任务：阶段8并列架构咨询，已完成；用户询问AI接管地图四角色或新增AI队友的可行性。本轮交付接入方案，不将可行性询问视为正式训练或场景扩展指令，原阶段与暂停训练状态保持。
- [x] 完整阅读主计划，检查根/Unity工作区，使用Unity Feature Implementation技能与CodeGraph优先定位；CodeGraph未覆盖地图四角色后读取当前源码。只读核对FirstPersonDefender、DefenderAbilities、DinoFixedBehaviorGraphDriver、攻击目标接口、角色构建入口及Python动作适配限制。
- 源码结论：FirstPersonDefender的IsControlling依赖OwnsCamera，移动、瞄准、技能与受伤均受其限制；键鼠与角色逻辑在同一Update，施法依赖相机射线，移动/闪避仍按帧推进。直接复制角色不能得到独立队友。既有DinoFixedBehaviorGraphDriver提供固定步图执行参考；flow_rl的UnityEnvAdapter明确拒绝discrete_size非零，不能宣称已有混合动作训练支持。
- 推荐设计：共享角色动作执行器，分离玩家输入/相机与AI命令；行为图负责跟随、守点、战斗进入退出和死亡，战斗节点调用独立RL策略。首版学习连续走位/瞄准，技能采用规则；学习技能选择时再补齐离散/混合动作的模型、适配、rollout及导出链。训练与部署使用相同图、约束、目标信息与固定步动作语义；每角色只保留一个移动执行者。
- 建议顺序：先建立纯行为图基线及同角色AI接管验证，再增加独立队友与玩家行为多样性，最后评估RL收益。冻结现有进攻模型作为对手，先训练一个防守Agent；新NPC观测/动作与旧270/4D攻方协议分开。对照必须保持相同角色数和数值，不能将新增队友火力收益归为RL收益。具体胜率、训练成本、推理延迟均待实验。
- 方法核对：[Unity Behavior自定义节点](https://docs.unity3d.com/Packages/com.unity.behavior@1.0/manual/create-custom-node.html)、[ML-Agents动作与观测](https://unity-technologies.github.io/ml-agents/Learning-Environment-Design-Agents/)。产品方案建议为玩家加一名跟随/守点的远程队友；尚未选择或实施最终玩法。
- 验证（2026-09-16）：静态源码与接口核查完成；PowerShell/.NET追加记录校验通过，原主计划字节前缀保留、标题唯一。未运行Unity/Python测试、构建或训练；无新增NPC、行为图、模型或场景变更。

### 2026-09-16：Academy step、训练步数与时间推进讲解

- 阶段/任务：阶段8并列学习咨询，已完成；产物为本次对话讲解，不改变实现阶段、既有验收复选框或训练安排。
- [x] 完整阅读主计划，检查根、ML-Agents及Dino工作区状态；动态检查索引并先用CodeGraph定位，再核对当前Academy、Agent、DecisionRequester、RemotePolicy、RpcCommunicator和Python LLAPI源码。确认EnvironmentStep负责ML-Agents事件/计数/观测/动作，默认由FixedUpdate调用，本身不推进完整PlayerLoop或物理时间。
- [x] 核对DinoTrainingDecisionScheduler每25 Academy steps决策、Running首步立即请求、RoundManager仅Running累计fixed tick，以及Bootstrap按timeScale/fixedDeltaTime设置captureFramerate。核对dino_parallel_ppo.py按已接收transition累计environment_steps；区分固定步、决策、通信、样本、rollout更新、minibatch optimizer step及Flow内部积分步。
- 时间结论：固定h=0.02且正常自动推进时，25步约0.5游戏秒；普通实时模式的理想墙钟换算h/timeScale不适用于本项目capture clock。captureDeltaTime=h/timeScale令每帧缩放游戏增量约h，真实训练速度须实测。固定时钟不代表NavMesh/PlayerLoop严格确定性，既有未通过门保持。
- 验证（2026-09-16）：CodeGraph查询与定向源码读取完成；交叉核对Unity 6官方FixedUpdate、fixedDeltaTime、deltaTime、timeScale、captureDeltaTime、maximumDeltaTime和Physics.Simulate文档。本次为静态讲解，未运行Unity/Python测试、构建或训练，仅追加本记录。

### 2026-09-20：AlphaZero 讲解文档公式渲染修复

- 阶段8并列文档维护；修复外部文档 C:\Users\hp\Documents\Codex\2026-09-18\wei\outputs\AlphaZero源码逐步讲解.md 中两处使用 \[/\] 的显示公式，改为 Markdown 数学块 $$ 分隔符，公式内容未改动。
- 验证：静态检查确认两处公式均成对使用 $$，不再存在 \[ 或 \]；未运行训练、Unity 或项目测试。

### 2026-09-22：网络参数正则项文档公式渲染修复

- 阶段8并列文档维护；将 网络参数正则项.md 中 13 个 \[/\] 显示公式块改为 $$ 分隔符，并将行内 \(/\) 改为 $ 分隔符，公式内容保持不变。
- 验证：旧分隔符搜索结果为 0，$$ 分隔符共 26 个（13 对），UTF-8 替换字符为 0；未运行训练、Unity 或项目测试。

### 2026-09-26：FPO / Flow 策略 Action Chunking 算法咨询

- 阶段/任务：阶段8并列算法设计咨询，已完成；对应阶段9的预研。本次解释如何修改算法，不启动阶段9实现或训练，不勾选其尚未验证的任务。现有阶段8退出门和暂停训练安排保持。
- 完整阅读主计划并只读核对工作区；按索引规则用CodeGraph核对官方FPO commit `418c2554f7cd22d52e14c07d951280929d73bf2f`、PolicyFlow commit `7f304b96e93b2804bdcc943710db885adddd06b8`；核对本地FlowActorCritic、CFM loss/ratio、collector/GAE、版本屏障、Dino动作接口与25-Academy-step调度。已有dirty/untracked文件保留。
- 建议（未实施）：分离基础动作维d、预测长度H、执行长度K和Flow NFE；首版H=K=2，再做1/2/4/8消融。噪声、速度场输入/输出及潜动作均扩展为H*d，以一整段为宏动作；Unity每次仍接收d维动作，由Python按Agent/episode维护队列。保留完整生成的latent chunk和同一MC噪声/时间/旧CFM损失，按实际执行m步累计折扣奖励，并使用gamma^m bootstrap。GAE明确lambda按宏决策或原始步定义，不宣称两种定义等价。
- 数学边界：FPO沿用CFM loss-difference代理ratio及signed clipped objective，不视为精确联合概率比；当前CFM对动作维求均值，扩展维度时先保留归一化，再做尺度消融。receding-horizon中未执行尾段没有直接环境监督；完整联合动作更新与前缀边缘策略是不同目标，简单mask CFM误差不等于获得前缀似然。episode终止清空队列；可用最终观测的截断才bootstrap，无观测故障数据丢弃；update边界排空已开始chunk再切policy version。
- 若用户所指是独立PolicyFlow算法：共享chunk执行/回报层，但保留其Gaussian delta与速度差ratio、Brownian正则；将相应动作字段/速度网格扩展到H*d并核对联合log-prob、KL和正则的维度归约。其Flow BC须用同episode连续动作窗口，单步checkpoint不作为chunk checkpoint直接恢复。
- 资料：[FPO原论文](https://arxiv.org/html/2507.21053v2)、[Diffusion Policy作者项目页](https://diffusion-policy.cs.columbia.edu/)、[PolicyFlow原论文](https://arxiv.org/abs/2602.01156)。本次宏动作改法是结合现有实现提出的设计，未声称论文已验证本项目效果。
- 验证（2026-09-26）：PowerShell here-string通过stdin运行`.conda/flow-rl/python.exe -B -`，对H=1/2/4/8、d=4、B=2、MC=3执行现有小型FlowActorCritic纯CPU前向：展平/reshape、噪声shape、动作边界、有限性、固定seed复现与相同新旧模型ratio=1全部PASS。这仅证明现有网络/CFM可处理扩展向量维数，未验证动作队列、macro rollout、Unity或学习效果。
- 产物：本次对话中的公式、执行语义和代码落点，以及本追加记录。未修改算法、游戏、配置、模型、依赖或启动训练；后续实现需要先建立H=1等价、变时长折扣、提前终止/截断、ID重排和版本切换回归。


### 2026-09-26：前沿训练框架改进方案

- 阶段/任务：阶段8并列架构咨询，已完成；用户要求参考 AlphaStar、IMPALA、R2D2、MAPPO 和 DI-engine，为当前 game_project 训练框架整理改进方案。未启动阶段9，也未修改训练代码、Unity 场景、模型、依赖或训练进程。
- [x] 完整阅读本计划的当前状态和全局约束，并只读核对 flow_rl 的并行环境、版本化采集器、PPO updater、Set Transformer、Dino v2 协议、checkpoint 和训练记录实现。
- 方案结论：当前项目已有并行 Unity step、严格 policy_version 隔离、推理微批、结构化 Set Transformer、GAE、PPO 诊断和带 build/protocol hash 的恢复机制；当前缺口主要是短期记忆、混合动作协议、真正可观测的 Actor-Learner 队列、独立离线数据协议、冻结对手评估和多智能体需求。
- 推荐顺序：先做 Phase 0 的 target-KL early stop、冻结评估命名空间、policy lag/队列诊断和失败熔断；再以不改变 v2 协议为前提验证 GRU/LSTM；然后新增 hybrid v3 动作协议；最后按实际需要引入 Actor-Learner/V-trace、BC/离线 RL 和 MAPPO/League。
- 产物：docs/前沿训练框架改进方案.md；文档明确了当前实现盘点、文件级改造点、阶段退出标准、测试矩阵、消融矩阵、风险和不做事项。
- 验证（2026-09-26）：文档 UTF-8 可读，281 行、20,888 字节，包含 Phase 0–5、V-trace、hybrid v3、测试矩阵和参考资料；未运行 Python/Unity 测试、构建或训练。


### 2026-09-26：0/75 ms 逐步确定性剩余分叉代码审计

- 阶段/任务：阶段8并列确定性调查，已完成静态定位；未修改 Unity/Python 源码、场景、配置、模型或训练进程。
- 证据：NavMesh-readiness 修复后的唯一真实分叉仍为 map1、layout seed `1084202712`：0 ms 在 `49.31998 s` 以 `TargetRadiusEntered` 胜利（food 244、active_dinos 10），75 ms 在 `50.01998 s` 以 `SessionTimeout` 失败（food 0、active_dinos 6）；0/75 ms 为 `1652/1654` environment steps，19/20 业务终局一致。
- 源码审计结论：`flow_rl/src/flow_rl/evaluation/dino_parallel_ppo.py` 在 `adapter.step()` 前注入墙钟等待；`UnityEnvAdapter.step()` 随后同步进入 ML-Agents `EnvironmentStep`。`RoundManager.FixedUpdate()` 与 `DinoFixedStepTime` 已按 fixed tick 计时，但 `DinoFixedBehaviorGraphDriver` 只把 Behavior Graph 调用移到 `FixedUpdate`，没有固定步驱动 NavMeshAgent 的内部移动、自动重寻路或 `pathPending/remainingDistance` 更新。导航动作仍在固定图 tick 中读取这些引擎状态并据此切换 Moving/Arrived/Failure；因此同样的 fixed tick 数并不保证同样的导航状态。
- 放大路径：`DinoFixedBehaviorGraphDriver` 在 Agent 暂时不在 NavMesh 上会跳过整个图 tick；`Dino.cs`/`MoveToClosestAttackableOnXzPlaneAction` 会在图分支切换时重新提交或 `ResetPath`；`DelayedPatrolAction` 在到达 tick 调用 `UnityEngine.Random.Range`，到达相位变化会改变后续随机调用序列。`Wall.HandleDeath()` 仍以 `yield return null` 延后一帧再隐藏墙并发送 NavMesh 更新事件；该路径在配置了动态障碍的墙体上还会把更新相位带出 fixed tick（Open Tropical 生成墙体当前重烘焙数组为空，因此暂列次要嫌疑）。Animator/IK 协程、`Invoke(2.5f)` 和 Unit 的普通 `Update()` 仍是帧/PlayerLoop 状态，不能作为严格逐步确定性保证。
- 当前最可信根因排序：1）NavMeshAgent/自动重寻路/局部路径状态没有纳入权威 fixed tick，Python 等待改变渲染帧与导航更新相位；2）导航状态边界导致行为图、攻击和死亡事件相差一个或多个 tick；3）帧协程、墙体更新事件和随机调用顺序继续放大差异。
- 下一步证据要求：只对 seed `1084202712` 录制每个 fixed tick 的 Agent position/nextPosition/velocity/pathPending/hasPath/pathStatus/remainingDistance/isOnNavMesh、图 tick 是否跳过、当前目标、path Set/Reset、攻击/伤害/死亡和 NavMesh generation；找到第一条差异后再做最小修复和红测。
- 验证（2026-09-26）：已按仓库规则完整读取计划、检查根目录与 `dino-attack` 状态；在 `dino-attack` 和 `ml-agents` 均先用 CodeGraph，再针对性读取源码与报告。未运行 Unity/Python 测试、构建或训练；本记录不宣称确定性门已通过。

### 2026-10-31：同步项目至 GitHub

- 阶段/任务：阶段8并列仓库维护，已完成；用户要求将当前 `game_project` 上传至 GitHub。远程仓库为 `https://github.com/xvchengxvcheng/dino-attack-flow-rl.git`，以远程 `main`（`f7d6d9b`）为基线，在临时副本中叠加本地当前源码、训练器、配置、测试和文档，避免覆盖远程已有内容。
- [x] 保留远程已有 Unity 源码、README、发布清单和历史提交；同步本地 `dino-attack/` 可发布源码与项目设置、`flow_rl/`、`docs/`、脚本及根目录说明文档。
- [x] 按仓库忽略规则排除 `Library/`、`Temp/`、`Obj/`、`Logs/`、`UserSettings/`、构建、数据集、训练运行结果、报告目录、缓存、IDE 状态和嵌套独立 Git 仓库；未上传 `external/` 中的嵌套仓库。
- 验证（2026-10-31）：远程 `HEAD` 可读取；临时副本提交前检查共 908 个变更文件，`git diff --cached --check` 通过，未发现超过 GitHub 单文件限制的待提交文件；提交内容已完成 diff 统计与大文件检查；`git diff --cached --check` 对 Unity YAML 空字段、Markdown 硬换行和既有 CRLF 报告格式警告，未作为阻塞项，最终 push 已完成并通过远程 `refs/heads/main` 校验（`35e32d63a3e7333bc130f4bb3f7709d7543632fc`）。
- 产物：远程仓库 `main` 分支；本地原工作区改动保留，上传使用临时副本 `.github-upload-work/`。




### 2026-10-31：修复 GitHub README 图片链接

- 阶段/任务：阶段8并列仓库维护，已完成；用户反馈 GitHub README 图片显示为破损链接。根因是 README 使用已不存在的 `../picture/中文文件名.png` 路径，而公开仓库实际图片位于 `docs/assets/readme/`。
- [x] 将 README 中 8 个图片引用改为仓库内现有的英文文件名路径，保留图片语义和版式。
- 验证（2026-10-31）：旧 `../picture` 引用为 0；README 引用的 7 个唯一图片路径全部通过 `Test-Path` 检查。已推送至远程 `main`，并在 GitHub 页面确认地图四和 JEV 图片正常渲染。


### 2026-10-31：更新地图一至三 README 图片

- 阶段/任务：阶段8并列仓库维护，已完成；用户要求地图一、二、三使用新图片。
- [x] README 已改用 `docs/assets/readme/map1-open-battlefield.png`、`map2-random-battlefield.png` 和 `map3-defense-ai.png`，地图四与 JEV 图片保持不变。
- 验证（2026-10-31）：三个新路径均存在，旧地图一至三图片路径不再被 README 引用；已推送至远程 main（e89fb1e），并在 GitHub 页面确认地图一、二、三新图片正常渲染。

