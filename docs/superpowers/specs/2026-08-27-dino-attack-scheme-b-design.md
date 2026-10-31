# Dino Attack 方案 B：持续作战时序决策设计规格

**版本：** 2026-08-27
**状态：** 已获用户继续指示，等待实施计划评审
**范围：** `dino-attack` 的训练专用场景、ML-Agents Unity 适配层、外部 Python LLAPI 训练器；不修改原始游戏场景和既有训练产物。
**算法事实来源：** 根目录 `fpo/`、`ReinFlow/`、`PolicyFlow/` 的固定本地 clone；算法移植目标为 `flow_rl/`。

## 1. 目标与边界

训练目标定义为最大化单回合胜率。一个 episode 对应一个游戏回合：Agent 在战斗持续期间反复观察环境，可以等待，也可以放置新的恐龙。恐龙的移动、攻击、敌人行为和建筑受损由现有游戏逻辑执行；RL Agent 只负责部署决策。

本规格不改变原始 `Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`、原始 ScriptableObject、ProjectSettings、Packages、现有构建或既有 3DBall/FPO/ReinFlow/PolicyFlow 结果。实现使用独立训练场景和隔离配置；若 Unity 包接入需要修改目标项目的 `Packages/manifest.json`，必须在实施计划中显式列出并先做备份/差异检查。

## 2. 环境模型

这是一个持续时间的有限时域 MDP，而不是一次性组合优化：

- 状态随战斗推进而变化；
- 击杀敌人或摧毁建筑会改变食物余额；
- 食物余额又影响后续可部署动作；
- Agent 可以选择等待，让已有恐龙继续战斗；
- 胜负由整个回合的时序结果决定。

第一版采用一个 Unity 环境一个 planner Agent。恐龙不是独立 Agent，继续使用现有 Behavior Graph/NavMesh/战斗脚本。并行环境通过多个 Unity worker 进程实现，轨迹始终按 Agent ID 组织。

## 3. 场景与难度

训练专用场景复制运行所需的环境对象和配置，但与原始场景分离。第一版将敌人难度固定为当前场景配置 `Hard`，同时冻结当前 AI 风格、初始布局和资源倍率，避免难度随机化掩盖环境接口问题。后续可作为独立消融加入 Normal/Hard/Insane 和布局随机化。

ML-Agents 源码/包来源为本地目录：

`C:\Users\hp\Documents\New project 2\game_project\ml-agents`

该目录是参考/包来源，不代表 `dino-attack` 当前已经安装 ML-Agents。接入时优先使用隔离分支或可回滚的训练配置，不侵入 `mlagents-learn` 内部。

## 4. 决策节奏与 episode 生命周期

Agent 不在每个渲染帧决策，而在固定仿真时间间隔收到一次 Decision Request。间隔必须是可配置字段 `decision_interval_seconds`，首轮实验在 0.5–1.0 秒范围内做吞吐和学习稳定性比较。

配置同时包含：

- `max_episode_seconds`：单回合最长仿真时间；
- `max_decision_steps`：防止环境卡死的安全上限；它不是“最多能放置多少只恐龙”；
- 可选放置冷却 `placement_cooldown_seconds`。

胜利（恐龙到达目标）和失败（所有恐龙死亡）是自然 `terminated`。达到时间或安全上限是 `truncated`，两者在 LLAPI 和 GAE 中保持区分。

## 5. Observation

Observation 使用固定维度、零填充的实体槽位，避免随实体数量变化：

- 回合时间、回合进度、当前食物余额、近期食物收入；
- 当前存活恐龙数；每个恐龙槽位包含 active、类型、位置、生命值、目标进度；
- 敌人槽位包含 active、类型、位置、生命值和威胁/距离摘要；
- 建筑/墙体槽位包含 active、类型、位置、生命值、升级或存活状态；
- 蛋的位置、关键距离摘要和当前胜负相关标志；
- 上次部署时间、放置冷却、最近击杀/摧毁事件计数；
- 合法放置区域的边界/占用摘要。

槽位排序和缺省填充值必须固定，并写入 normalizer/checkpoint 元数据。训练前用单元测试验证 shape、dtype、有限值和固定 seed 复现。

## 6. Action

动作固定为混合动作：

```text
action_mode: Wait | Place
dinosaur_type: Pachycephalosaurus | Velociraptor | TRex
x, z: 归一化连续场地坐标
```

`Wait` 忽略类型和坐标；`Place` 尝试放置对应类型。朝向继续由现有逻辑自动指向目标蛋。

离散部分可使用 action mask：食物不足、冷却未结束、不可用类型等动作被屏蔽。连续坐标不能枚举完整 mask，必须经过合法性检测；越界、重叠、危险层或无效位置采用明确的拒绝/投影策略，并记录非法动作率。

## 7. 资源与奖励

游戏资源和学习奖励分离记录：

- 放置成功：食物余额减少该恐龙成本；
- 敌人或建筑死亡：沿用现有 `floor(cost / 2)` 食物返还规则；
- 击杀/摧毁事件同时产生训练奖励；
- 胜利/失败终端奖励必须明显大于单个事件奖励。

建议的可配置形式：

```text
reward = win_reward * win
       - loss_penalty * loss
       + kill_reward * defender_kills
       + destroy_reward * buildings_destroyed
       - placement_penalty * placed_cost_normalized
       - invalid_action_penalty * invalid_actions
       - time_penalty * decision_count
```

最终指标以 round win rate 为主，辅以平均食物、击杀数、建筑摧毁数、回合长度、非法动作率和资源利用率。不能用食物返还数直接替代胜率目标。

## 8. Python/Unity 接口

Unity 侧负责观测、动作执行、合法性、资源、事件奖励、reset、terminal/truncated。Python `flow_rl` 侧复用既有 UnityEnvAdapter、Agent-ID rollout、GAE、critic、normalizer、checkpoint、日志和 evaluator。

适配层必须：

- 保持 `DecisionSteps` 的 Agent 顺序；
- 不按数组下标假定 Agent 永久存在；
- 正确区分 `TerminalSteps.interrupted`；
- 对动作 shape、dtype、范围和 mask 做断言；
- 保存事件奖励与胜负指标的独立统计。

## 9. 算法接入顺序

先建立合法随机、贪心、网格/CEM 和 Gaussian PPO 基线，再接入三种 Flow 算法：

1. PPO：验证环境接口和胜负奖励是否可学；
2. FPO：优先用于连续坐标与混合部署动作；
3. ReinFlow：使用 PPO/CEM/启发式成功轨迹进行 Flow BC 后再在线微调；
4. PolicyFlow：在确认存在多峰成功布局后接入，保留官方 velocity-ratio 与 Brownian regularizer 语义。

三种算法共享环境、rollout、GAE、critic、日志和评估协议；概率模型、ratio 和正则项保持各自官方实现边界。

## 10. 验证与验收

实施必须先测试后编码，并至少覆盖：

- observation/action shape、dtype、有限值和固定 seed；
- Agent ID 重排、terminal/truncated、空 Decision batch；
- Wait/Place 语义、动作边界、非法位置和 action mask；
- 资源扣除、击杀返还、建筑摧毁返还和奖励分解；
- 单回合胜负、超时截断和 reset 后状态清零；
- 训练/评估使用隔离 worker，checkpoint 可恢复；
- PPO/FPO/ReinFlow/PolicyFlow 在同一环境步预算和评估 seed 下比较；
- 报告 round win rate、environment steps、wall-clock、推理 NFE/延迟和资源指标。

## 11. 暂不确定但可配置的事项

- 固定时间间隔的最终值；
- 单回合最大仿真秒数；
- 放置冷却时间；
- 事件奖励的具体权重；
- 是否在第二阶段加入难度/布局随机化；
- 是否把多个连续回合组成更长 episode。

这些参数不应在 Unity 脚本中散落硬编码，应进入训练配置并在 checkpoint 和报告中记录。

