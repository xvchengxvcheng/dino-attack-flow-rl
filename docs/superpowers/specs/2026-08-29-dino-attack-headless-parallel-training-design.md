# Dino Attack 同场景无渲染并行训练设计

**日期：** 2026-08-29

**状态：** 并行运行架构继续生效；本文原 504/6 接口条款已于 2026-08-30 被结构化协议规格取代

**对应阶段：** 阶段 8 / `TASK-8.11` 前置设计

**正式场景：** `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`

## 1. 背景

阶段 8 已冻结玩家和 AI 必须使用同一个 `Dinos.unity`、同一地图及同一玩法对象，只替换决策控制器。历史 `DinoAttackTraining.unity` 只保留为早期接入和诊断证据，不进入正式训练或评估结果。

现有训练接入仍以单 Unity 进程、历史训练场景和独立 episode watchdog 为主；正式训练还需要解决无渲染运行、多环境并行、加速后的时间语义、异步采样的数据一致性、场景重载以及 terminal/truncated 协议。本设计定义这些运行边界。本文最初冻结的 504 维观测和 6 维动作已由 [`2026-08-30-dino-attack-structured-set-observation-action-design.md`](2026-08-30-dino-attack-structured-set-observation-action-design.md) 取代；奖励来源、部署规则和游戏战斗逻辑仍不因并行化而改变。

本文关于 50 秒自然失败的定义取代旧设计中把一般“时间中断”归为 truncated 的宽泛表述：正式游戏内的 50 秒规则是可观测、可重复的环境结局，因此属于 natural terminal；只有训练基础设施中断才属于 truncated。

## 2. 目标与非目标

### 2.1 目标

- Human 与 AI 正式运行统一加载 `Dinos.unity`。
- AI 训练通过显式运行模式关闭表现层，但完整保留玩法与观测依赖。
- 一进程一战场，默认四个 Unity Player 并行。
- Unity 环境异步采样，Python 在冻结策略版本下同步更新。
- 以模拟时间定义决策周期和 50 秒单局上限，不受墙钟速度倍率改变。
- 完整场景重载复位 episode，并保证终止数据先于重载送达 Python。
- 确定性派生环境和 episode seed，记录完整实验 provenance。
- 用五项必要验证覆盖接口、终止、重载、并行、一致性与基本恢复。

### 2.2 非目标

- 不维护第二份正式训练场景，也不把历史 `DinoAttackTraining.unity` 更新为正式环境。
- 不在单个 Unity 进程内复制多个 Arena；当前单例和场景级状态仍按一场景一局管理。
- 并行化本身不改变正式 observation/action 协议、恐龙类型、地貌速度、资源返还或胜负规则；正式接口由 2026-08-30 结构化协议规格定义。
- 不引入 action chunk；第一版仍是单次 `Wait/Place`。
- 不实现策略持续变化的完全异步学习；采样期间策略保持冻结。
- 不以渲染帧率或墙钟秒数定义游戏决策和自然失败。
- 不为了吞吐量先实现原地 reset；第一版以完整场景重载保证正确性。

## 3. 总体架构

```text
Python Trainer
├─ Policy version N（采样期间冻结）
├─ Async Vector Collector
│  ├─ Unity Player 0 → Dinos.unity → Agent 0
│  ├─ Unity Player 1 → Dinos.unity → Agent 1
│  ├─ Unity Player 2 → Dinos.unity → Agent 2
│  └─ Unity Player 3 → Dinos.unity → Agent 3
├─ Rollout Buffer（env/agent/episode/policy version 隔离）
└─ 达到约 4096 decisions → 排空在途动作 → 同步更新 → Policy version N+1
```

每个 `UnityEnvAdapter` 只拥有一个 Unity 进程和一个 `Dinos.unity` 战场。四个进程使用不同 worker ID、环境 seed、日志目录和生命周期状态。采集器允许任一环境独立推进或重载；更新器只消费同一策略版本产生的完整 transition。

## 4. 运行模式与同场景边界

### 4.1 显式训练入口

训练 Player 使用启动参数：

```text
--dino-training
```

- 有该参数：启用 ML-Agent 与训练控制器，场景初始化后自动进入 `Running`。
- 无该参数：保持正常 Human 模式、Logo/Start、玩家输入和可视表现。
- 不使用 batch mode、`Application.isBatchMode` 或 `no_graphics` 单独推断训练模式；自动化测试和普通无头启动不能被误识别为正式训练。
- Python 同时使用 ML-Agents 的 `no_graphics=True`，使 Player 不创建设备渲染输出。

### 4.2 训练模式关闭与保留内容

训练模式关闭：

- Camera 与 Camera 控制；
- UIDocument、UI Toolkit 和玩家部署输入；
- Renderer、Light、Volume、ParticleSystem；
- AudioListener 与音频播放；
- 仅服务于表现的动画、特效或可视反馈组件，前提是它们不写入玩法状态。

训练模式保留：

- Collider、Trigger、NavMesh、NavMeshAgent 与运行时导航更新；
- RoundManager、DinoSpawner、EnemyAIController、房屋、墙、守卫和恐龙逻辑；
- Behavior Graph、攻击、血量、死亡、资源返还和中央目标；
- 五个部署四边形、区域坐标转换、地貌触发器和速度倍率；
- ML-Agent、观测构建、奖励、决策调度和 episode 生命周期。

关闭对象必须按组件类型和明确引用执行，不能停用承载玩法组件的整个层级根节点。训练模式不得改变 Transform、Collider、NavMesh 或观测读取对象。

## 5. 时间模型与决策协议

### 5.1 三种步长

| 名称 | 默认值 | 含义 |
|---|---:|---|
| Unity 物理步长 | `Time.fixedDeltaTime = 0.02s` | 移动、攻击、导航和 Academy 自动步进 |
| AI 决策周期 | `25 Academy steps = 0.5s` 模拟时间 | 收集观测并执行一次 `Wait/Place` |
| 同步更新采样量 | 约 `4096 Agent decisions` | 一个冻结策略版本的 rollout 预算 |

ML-Agents 的 Academy 自动步进发生在 `FixedUpdate`。正式调度器按 Academy step 计数，每 25 步调用一次 `RequestDecision()`；不依赖帧率，不使用累计浮点秒数决定边界。

`Wait/Place` 是一次性战略指令，因此两次决策之间不得重复执行旧动作。训练模式进入 `Running` 后立即请求首次决策，随后在第 25、50、75……个 Academy step 请求。自然结局发生时立即发送终止数据，不等待下一个周期。

### 5.2 游戏速度

- 默认 `timeScale = 20`，可由实验配置调整。
- `timeScale` 只压缩墙钟耗时，不改变 `fixedDeltaTime`、每局模拟时长或决策次数。
- 50 秒上限的一局最多约包含开局决策加 100 个周期决策，即约 101 次决策。
- 在理想吞吐下，`timeScale=20` 时 50 秒模拟时间约需 2.5 秒墙钟时间，0.5 秒决策周期约对应 0.025 秒墙钟间隔；实际值由 CPU 和场景重载决定。
- 四种正式算法必须使用相同 timeScale、物理步长和决策周期。

## 6. Episode 生命周期与终止语义

### 6.1 开始与重置

训练模式加载完成并完成场景玩法初始化后自动调用既有开始入口进入 `Running`，不等待 Start，也不保留独立 Setup 部署窗口。Agent 从首次决策起即可在 Running 中选择 `Wait/Place`。

每个 episode 结束后通过统一 restart service 完整重载 `Dinos.unity`。重载恢复资源、房屋、墙、守卫、恐龙、中央目标、奖励去重状态、随机布局、Agent 和计时器。第一版不实现原地 reset。

### 6.2 Natural terminal

以下均为自然终止：

- 恐龙达成中央目标，`RoundManager.PlayerWon=true`；
- 恐龙全灭，`RoundManager.PlayerWon=false`；
- `Running` 累计达到 50 秒模拟时间，`RoundManager.PlayerWon=false`。

50 秒失败时场上可能仍有存活恐龙，因此训练 Agent 必须读取 `RoundManager.PlayerWon`，不得通过 `AliveDinos > 0` 推断胜负。

### 6.3 Truncated / interrupted

只有以下基础设施事件属于 truncated/interrupted：

- Unity 进程崩溃或被终止；
- 通信断开；
- 场景重载或启动握手超时；
- 训练器主动中止环境。

历史训练 Agent 的 180 秒和 360 决策步 watchdog 不进入正式协议，避免与 50 秒自然失败竞争。自然 terminal 必须先提交奖励和 TerminalSteps，再延迟触发场景重载；不得用重载代替 `EndEpisode()`。

若进程异常且没有最终观测，当前环境的未完成 episode 轨迹整体丢弃，不伪造 terminal、最终观测或 bootstrap 值。

## 7. 异步采样与同步更新

### 7.1 采样阶段

每轮采样开始时冻结策略版本 `N`。四个 Unity 环境独立推进：哪个环境先产生 DecisionSteps 或 TerminalSteps，采集器就先处理哪个，不等待统一 tick。某个环境进行完整场景重载时，其他环境继续采样。

每条 transition 至少携带：

- `environment_id`；
- ML-Agents `agent_id`；
- `episode_id`；
- `policy_version`；
- observation、实际提交动作、reward、next observation；
- terminal/truncated 及终止原因；
- 行为策略计算更新所需的既有 snapshot/log-prob 数据。

Agent ID 只在对应 environment 内解释。场景重载产生新 Agent ID 后，旧 ID 的 terminal/truncated 状态必须退休，不能与新 episode 拼接。

### 7.2 更新屏障

默认 rollout 目标为约 4096 个 Agent decisions。达到目标后：

1. 停止向所有环境派发新动作；
2. 接收并落盘已经派发动作的返回结果；
3. 允许最终条数小幅超过 4096；
4. 验证整批数据只包含策略版本 `N`；
5. 对未终止轨迹使用 critic bootstrap，对 natural terminal 不 bootstrap；
6. 执行一次同步参数更新；
7. 广播策略版本 `N+1`，恢复异步采样。

不得把旧策略生成但尚未返回的 pending action 记入下一策略版本。PPO、FPO、ReinFlow 和 PolicyFlow 共用该采集屏障；算法专用概率模型和更新逻辑保持独立。

rollout 边界不会结束 Unity episode。跨越更新边界的 episode 在缓冲区中切成两个策略版本不同的片段：版本 `N` 的尾片段使用边界 next observation 做 bootstrap，版本 `N+1` 从同一环境的下一次动作继续；GAE 不跨策略版本拼接。

## 8. 并行进程、种子与恢复

### 8.1 并行模型

- 默认 `num_envs=4`，保留配置入口。
- 一个 Unity Player 只运行一个 `Dinos.unity` 战场。
- 每个进程使用唯一 worker ID 和独立日志目录。
- worker ID 只用于端口隔离，不参与随机数派生。
- 四种正式算法使用相同并行数和进程配置。

### 8.2 确定性种子

```text
environment_seed = base_seed + environment_index
episode_seed = deterministic_hash(environment_seed, episode_index)
```

Unity 中墙位、地面守卫位置、AI 部署采样及其他正式随机项统一由 episode seed 驱动。日志保存算法 seed、base seed、environment seed、episode seed、worker ID、episode index 和策略版本。

### 8.3 恢复策略

- 单个 Unity 环境异常时，其他环境继续运行。
- 当前异常 episode 记录为基础设施 truncated；若没有最终观测则丢弃其未完成轨迹。
- 使用相同 worker ID 重启进程，但保留新的 process generation 标识，避免把重启前后的消息混合。
- 默认允许同一环境自动重启两次；第三次连续失败时中止本次训练，不静默减少并行数。
- 退出和异常路径必须关闭子进程并释放端口。

## 9. 配置与 provenance

第一版默认值：

| 配置 | 默认值 |
|---|---:|
| scene | `Dinos.unity` |
| training flag | `--dino-training` |
| no graphics | `true` |
| time scale | `20` |
| fixed delta time | `0.02s` |
| decision period | `25 Academy steps` |
| episode limit | `40s` simulation time |
| parallel environments | `4` |
| rollout target | `4096 Agent decisions` |
| automatic process retries | `2` |

实验记录必须包含：Unity 版本、Player/build hash、`Dinos.unity` hash、Python commit/config hash、算法名和 seed、上述全部运行配置、worker/process generation、各终止原因计数、wall-clock/environment-step 吞吐及残留进程检查结果。

默认值允许按实际稳定性调整，但一次正式算法比较内不得变化。任何调整必须进入版本化配置和计划决策记录。

## 10. 必要验证

只保留下列五组阻塞验证。

### 10.1 编译与接口

- Unity 编译无新增错误，Python 聚焦测试通过。
- observation/action 按 2026-08-30 结构化协议规格验证五流固定 shape、mask、4D 动作、有限性及分箱边界；原 504/6 检查不再作为验收标准。
- 每 25 个 Academy step 请求一次决策，中间不重复执行旧动作。

### 10.2 终止语义

分别验证一次胜利、恐龙全灭、50 秒失败和基础设施 interrupted。前三者必须进入 natural terminal，异常必须进入 truncated；终止数据必须先于场景重载送达 Python。

### 10.3 场景重载

连续运行 20 个 episode，确认资源、房屋、墙、守卫、恐龙、Agent ID、episode seed 和计时正确复位，不产生重复订阅或残留单位。

### 10.4 四环境异步采样

运行一个完整的约 4096-decision rollout，确认四进程可并行采样、重载不阻塞其他环境、数据不跨 environment/episode/policy version，且更新屏障正确排空在途动作。

### 10.5 同场景一致性与基本恢复

- 用同一 seed 和同一动作序列分别运行普通模式与无渲染训练模式，比较胜负、奖励、episode 长度和关键玩法状态；浮点 observation 使用明确容差。
- 一致性测试由测试夹具在两种模式完成相同初始化后调用同一开始入口，避免把 Human 的手动 Start 等待时间计入玩法差异。
- 主动终止一个 Unity 进程一次，确认当前异常 episode 正确处理、进程能够重启，其他环境继续运行且退出后无残留进程或端口。

不要求 100 局耐久、20k 决策多更新、十种 seed、完整 timeScale/并行数性能矩阵或详细 CPU/GPU 基准。

## 11. 影响范围与迁移

预计实施影响以下边界，最终文件集以实施计划和源码调查为准：

- Unity 训练模式 bootstrap 与表现层开关；
- `DinoTrainingAgent` 的结局读取、episode 结束及历史 watchdog；
- `DinoTrainingDecisionScheduler` 的 Academy-step 调度；
- `DinoTrainingSceneReloader` 与统一 restart service；
- 正式训练 Player builder，使其加载 `Dinos.unity` 并传递训练模式；
- `flow_rl` 的多进程异步采集器、策略版本屏障、恢复和 provenance；
- 对应 Unity PlayMode、Python 单元/集成测试与阶段报告。

历史 `DinoAttackTraining.unity`、旧训练构建和既有报告保持原样，仅在新文档中标记为历史诊断产物。不得删除它们或把其结果混入正式对比。

## 12. 验收标准

- 正式 Human 与 AI Player 都加载同一份 `Dinos.unity`。
- AI 模式无渲染、自动 Running，并使用 2026-08-30 结构化协议规格定义的五流 observation 与 4D action。
- 0.5 秒模拟时间决策周期、50 秒自然失败和 timeScale 语义正确。
- 四个进程能够异步采样，同一策略版本约 4096 decisions 后同步更新。
- natural terminal、truncated、pending action、Agent ID 和场景重载顺序无混淆。
- 相同 seed/动作的普通与无渲染关键结果一致。
- 五组必要验证全部通过并生成可追溯报告。

本文档获用户审阅通过后，下一步使用 `superpowers:writing-plans` 编写实施计划；在实施计划获批前不修改 Unity 或 Python 生产代码。
