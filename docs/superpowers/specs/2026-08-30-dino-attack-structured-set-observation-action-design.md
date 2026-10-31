# Dino Attack 结构化集合观测、4D 动作与 Set Transformer 设计

**日期：** 2026-08-30  
**对应阶段：** 阶段 8 / `TASK-8.11` 前置设计  
**状态：** 用户已于 2026-08-30 复核通过；尚未实施  
**事实来源：** `docs/PROJECT_PLAN.md`

## 1. 背景与问题

当前生产代码仍使用单个 504 维扁平 observation 和 6 维连续 action。该接口能完成早期 LLAPI 接入，但不适合作为 Dino Attack 的最终训练协议：

- 城墙、守卫、房屋和恐龙的数量不同且会随一局进程变化，扁平槽位容易把类别语义、实体顺序与 padding 混在一起。
- 地图已冻结 Z1–Z5 五个扇形四边形部署区，旧 action 仍直接输出世界 x/z，没有显式表达“选区后再选区内相对坐标”。
- PPO、FPO 后续需要公平比较；若两者使用不同容量或不同类别处理方式的编码器，算法结论会被网络架构混淆。
- 当前游戏从进入 `Running` 起最多持续 50 秒。环境剩余时间是状态；FPO 的流时间 `τ` 是生成过程变量，两者不能共用同一个含义。

本设计把传输协议固定为五个独立 observation 流与 4D 连续 action，并分别定义 Deep Sets 架构基线及 PPO/FPO 正式比较使用的 Set Transformer 编码器。

## 2. 目标与非目标

### 2.1 目标

1. 用固定 shape、mask 和类别独立流表达可变数量实体。
2. 让策略先选择 Z1–Z5，再输出该四边形内的相对坐标，最后选择等待或三种恐龙。
3. 以 Deep Sets 建立轻量集合编码基线，以 Set Transformer 表达同类实体交互和区域条件信息。
4. PPO 与 FPO 使用相同的正式状态编码器规格，保证算法比较公平。
5. 将 observation/action 版本、字段顺序、上限、区域顶点和动作分箱写入 checkpoint metadata，禁止旧新模型静默混用。

### 2.2 非目标

- 不在本任务引入 action chunk；每次 Decision 仍只产生一个单步动作。
- 不改变 50 秒自然失败、胜负、奖励、食物返还、完整场景重载或四进程训练架构。
- 不让恐龙之间启用局部避障，也不改变现有战斗、NavMesh、地貌速度或视觉规则。
- 不在本设计阶段修改 Unity、Python 生产代码、场景、Prefab、Packages 或 ProjectSettings。
- 不把 Deep Sets 与 Set Transformer 的差异解释为 PPO 与 FPO 的算法差异。

## 3. 协议版本与取代关系

新协议记为 `dino_attack_structured_set_v1`：

- observation：五个固定 shape 流，合计 207 个动态标量；逻辑上保持分类，不在模型入口重新拼成一个 207 维槽位向量。
- action：4 维连续向量。
- `MaxDinos = 10`。

本规格取代：

- `docs/PROJECT_PLAN.md` 中 2026-08-27 决策定义的 6D 动作协议；
- 当前生产代码的 504 维 observation 协议；
- `2026-08-29-dino-attack-headless-parallel-training-design.md` 中写死 504/6 的接口、验证与验收条款。

并行训练规格的其余内容继续生效，包括：同一 `Dinos.unity`、显式 `--dino-training`、默认四个无渲染 Player、`timeScale=20`、每 25 Academy step 决策、50 秒自然失败、完整场景重载、确定性 episode seed、异步采样及约 4096 decisions 的同步更新屏障。

当前生产代码在实施前仍是 504/6；本文档批准不等于接口已经迁移。

## 4. 4D 连续动作协议

策略输出 `a = [a0, a1, a2, a3]`，每一维均限制在 `[-1, 1]`。

### 4.1 通用等宽分箱

对包含 `K` 个选项的维度：

```text
t = clamp((a + 1) / 2, 0, 1)
index = min(K - 1, floor(t * K))
```

因此 `a = 1` 明确落入最后一箱；内部边界按 `floor` 归入较高编号箱。Unity 与 Python 必须共用同一算法和固定顺序。

### 4.2 四个动作维度

| 维度 | 含义 | 解码规则 |
|---|---|---|
| `a0` | 区域选择 | 5 个等宽箱，按 `Z1, Z2, Z3, Z4, Z5` 映射到索引 `0..4` |
| `a1` | 区域内相对坐标 `u` | `u = clamp((a1 + 1) / 2, 0, 1)` |
| `a2` | 区域内相对坐标 `v` | `v = clamp((a2 + 1) / 2, 0, 1)` |
| `a3` | 部署选择 | 4 个等宽箱：`Wait, Velociraptor, Pachycephalosaurus, T-Rex` |

当 `a3` 解码为 `Wait` 时，`a0–a2` 完全忽略，不做位置解析或非法位置检查。

### 4.3 四边形坐标转换

每个区域沿用现有有序四顶点 `A/B/C/D` 和双线性映射：

```text
P(u, v) = (1-v) * ((1-u)A + uB)
        + v     * ((1-u)D + uC)
```

顶点顺序必须与现有部署区域 API 保持一致。`u/v` 只确定候选世界位置；部署服务仍负责水面、玩法占用、资源、NavMesh 或其他正式合法性检查。

### 4.4 非法动作

以下任一情况使本次部署无效：

- 当前存活/已部署恐龙数量已达到 10；
- 食物不足以购买所选恐龙；
- 区域配置或四顶点缺失；
- 相对位置转换后未通过正式部署合法性检查；
- 部署服务未能生成实体。

无效动作不得生成第 11 只恐龙，也不得先生成后静默截断 observation。它沿用既有非法动作反馈/惩罚，并令下一次 observation 的 `last_action_valid = 0`。`Wait` 是合法动作，令其为 `1`。

## 5. 五个结构化 observation 流

ML-Agents 传输 shape 必须固定。所谓“可变维度观测”在本项目中指实体有效数量可变，不是运行时改变 Tensor shape；变化通过 `valid_mask`、生命周期字段和零 padding 表达。

### 5.1 总表

| 流 | Shape | 每项字段 | 标量数 |
|---|---:|---|---:|
| Global | `(3,)` | `remaining_time, food_normalized, last_action_valid` | 3 |
| Walls | `(6, 5)` | `valid_mask, active, x_norm, z_norm, health_ratio` | 30 |
| Guards | `(8, 7)` | `valid_mask, type_archer, type_mage, x_norm, z_norm, health_ratio, is_wall_guard` | 56 |
| Houses | `(8, 6)` | `valid_mask, alive, x_norm, z_norm, health_ratio, max_health_normalized` | 48 |
| Dinos | `(10, 7)` | `valid_mask, type_velociraptor, type_pachy, type_trex, x_norm, z_norm, health_ratio` | 70 |
| **合计** | 五个独立流 | 不在环境侧扁平混类 | **207** |

### 5.2 Global

- `remaining_time = clamp((40 - running_elapsed_time) / 40, 0, 1)`。
- `food_normalized` 使用冻结配置中的固定归一化尺度并裁剪到 `[0,1]`。恐龙价格固定，因此不再发送三个 `can_buy_*` 字段；策略可由食物与固定价格推断购买能力。若未来价格会随机或动态变化，必须升级 observation 版本。
- `last_action_valid` 为上一决策动作结果的 `0/1`；episode 首次决策使用冻结的初始化值 `1`。

### 5.3 Walls

六个场景 Wall Slot 均具有稳定身份，因此六行始终 `valid_mask = 1`。本局未抽中或已经死亡的墙保留该行，使用 `active = 0`、`health_ratio = 0`；其固定 slot 坐标继续发送，便于策略区分“该位置没有墙”和“未知 padding”。

### 5.4 Guards

正式上限为 8：3 名墙上弓箭手、3 名地面弓箭手和 2 名法师。只编码当前存活守卫；其余行 `valid_mask = 0` 且其他字段全为 0。墙上弓箭手使用 `is_wall_guard = 1`，其余为 0。

当前正式场景不使用 Cannoneer，因此本版本不加入 Cannoneer 类型。以后若正式启用新兵种，必须升级字段和 observation 版本，不能复用空 padding 语义。

### 5.5 Houses

八栋房屋具有稳定身份，八行始终 `valid_mask = 1`。房屋死亡后完整模型消失，但隐藏占地和 3–5 根暖色木梁仍存在，因此死亡房屋保留观测：`alive = 0`、`health_ratio = 0`，位置与归一化最大耐久继续发送。

### 5.6 Dinos

最多编码 10 只当前存活恐龙，其余行 padding。每只恐龙只发送批准的种类、当前位置和血量；不发送到村落中心距离、所在区域 one-hot 或地貌 one-hot。网络可从全地图归一化坐标学习空间关系，区域几何另由 Region Query 提供。

### 5.7 归一化、排序与 padding

- `x_norm/z_norm` 按冻结地图边界线性映射到 `[-1,1]`，并将边界写入环境配置与 checkpoint metadata。
- `health_ratio = clamp(current_health / max_health, 0, 1)`。
- `max_health_normalized` 使用冻结的 House HP 尺度。
- 所有 padding 行的全部字段为 0，包括 mask。
- Guard 与 Dino 的行顺序不承载语义；收集器可以使用稳定顺序保证复现，但编码器不得添加 slot positional embedding，测试必须验证有效行置换不改变集合表示。
- Wall 与 House 可以保留稳定 slot 顺序用于调试和生命周期，但正式模型仍依赖类型、状态与坐标，而不是数组下标位置编码。

## 6. Deep Sets 架构基线

Deep Sets 用来验证结构化集合接口和衡量集合编码器收益，不作为 PPO/FPO 正式算法比较中不同算法的差异点。

### 6.1 分类编码

```text
Wall MLP:   5 -> 64 -> 64
Guard MLP:  7 -> 64 -> 64
House MLP:  6 -> 64 -> 64
Dino MLP:   7 -> 64 -> 64
Global MLP: 3 -> 32 -> 64
```

每类实体只对 `valid_mask = 1` 的 token 做聚合。每个集合同时计算 masked mean 与 masked max，拼成 128 维类别表示。空集合必须返回有限的零表示或明确的 learned empty 表示；不得由除以零或全 `-inf` max 产生 NaN。

### 6.2 融合

```text
4 categories * 128 + Global 64 = 576
Fusion MLP: 576 -> 256 -> 256
```

输出 256 维 state embedding。激活函数使用 SiLU；归一化和初始化在实施计划中统一，但不得改变上述容量而不更新配置版本。

## 7. 正式 Set Transformer 编码器

PPO 与 FPO 的正式算法比较使用相同的 Set Transformer 编码器规格。

### 7.1 Token 编码

四类输入分别使用两层 MLP 投影到 `d_model = 64`。Wall、Guard、House token 加入各自 learned entity-type embedding；Dino token 使用独立 Dino MLP。模型不添加实体 slot positional embedding，空间位置来自 `x_norm/z_norm`。

### 7.2 两条集合分支

```text
Wall ─ Wall MLP ─┐
Guard ─ Guard MLP ├─ Defense ISAB x2 ─ DefenseTokens
House ─ House MLP ┘

Dino ─ Dino MLP ─── Dinosaur ISAB x2 ─ DinosaurTokens
```

统一超参数：

- `d_model = 64`
- attention heads = 4
- inducing points = 8
- ISAB layers = 2
- activation = SiLU
- dropout = `0–0.05`，正式值写入实验配置

当某分支全部被 mask（例如没有存活恐龙）时，插入 learned empty token 或走等价的显式空分支，确保 attention 输出有限且可训练。

### 7.3 时间 FiLM

环境 `remaining_time` 单独经过 Time MLP，分别为 Defense 与 Dinosaur token 生成不同的 `gamma/beta`：

```text
token' = (1 + gamma) * token + beta
```

`gamma` 采用近零初始化、`beta` 采用零初始化，使网络初始接近恒等映射。FiLM 只使用环境剩余时间；完整 Global 三字段仍由 Global MLP 编成 64 维全局表示。

FPO 的流时间 `τ` 不是环境时间，不进入该 FiLM。它在 FPO velocity 网络中拥有独立的 flow-time embedding。

### 7.4 五个 Region Query

每个 Z1–Z5 区域拥有一个 learned region embedding，并结合其归一化四顶点：

```text
Geometry input = [Ax, Az, Bx, Bz, Cx, Cz, Dx, Dz]
Geometry MLP: 8 -> 32 -> 64
RegionQuery = learned_region_embedding + geometry_embedding
```

五区四顶点是冻结模型常量/buffer，并写入 checkpoint metadata；它们不在 Unity 每个 Decision 重复发送。如果地图几何改变，必须升级环境/observation 版本或拒绝载入不匹配 checkpoint。

### 7.5 分离式 Region Cross-Attention

每个区域分别读取防御与恐龙集合，不先把两类 token 混为同一池：

```text
R_defense  = CrossAttention(RegionQuery, DefenseTokens)
R_dinosaur = CrossAttention(RegionQuery, DinosaurTokens)
RegionState = MLP(concat(RegionQuery, R_defense, R_dinosaur))
            = MLP(192 -> 128 -> 64)
```

五个 64 维 RegionState 按冻结的 `Z1..Z5` 顺序拼接为 320 维。Global MLP 输出 64 维，最终融合为：

```text
concat(5 * RegionState64, Global64) = 384
Fusion MLP: 384 -> 256 -> 256
```

输出统一 256 维 state embedding。

## 8. PPO 与 FPO 接入

### 8.1 公平性规则

- 正式 PPO 与 FPO 必须使用相同的 Set Transformer 状态编码器规格、输入字段、mask 语义和 256 维输出。
- Actor 与 Critic 各自拥有独立编码器参数；不得共享后再只对其中一个算法改变共享方式。
- Deep Sets 与 Set Transformer 的比较单独记为 encoder ablation，不能把 `PPO + Deep Sets` 对 `FPO + Set Transformer` 报告为算法优劣。
- 所有算法继续使用单步 4D action，不引入 action chunk。

### 8.2 PPO

```text
Set Transformer Actor Encoder -> state 256
Actor MLP: 256 -> 256 -> 256 -> 4
Distribution: tanh-squashed Gaussian

Independent Set Transformer Critic Encoder -> state 256
Critic MLP -> scalar value
```

### 8.3 FPO

```text
Set Transformer Actor Encoder -> state 256
latent/current action: 4
flow-time embedding τ: 32
concat = 292
Velocity MLP: 292 -> 256 -> 256 -> 4

Independent Set Transformer Critic Encoder -> state 256
Critic MLP -> scalar value
```

环境 `remaining_time` 已包含在 observation 与 encoder 中；FPO `τ` 只描述 flow integration 进度。二者必须使用不同字段、模块名和 checkpoint key。

## 9. LLAPI、配置与 checkpoint 元数据

Unity 必须按固定顺序注册五个 observation specs，Python adapter 按流名或冻结索引读取，保持 `DecisionSteps` 中的 Agent 顺序。不得为了兼容现有 MLP 在 adapter 中直接把五类实体混成无类别的 207 维向量。

每个训练配置和 checkpoint 至少记录：

- `observation_version = dino_attack_structured_set_v1`
- 五个 stream 的名称、shape 和字段顺序
- `MaxWalls=6, MaxGuards=8, MaxHouses=8, MaxDinos=10`
- 全地图 x/z 归一化边界及 House/Food 归一化尺度
- Z1–Z5 的 A/B/C/D 归一化顶点与顶点顺序
- action version、四个维度含义、区域/恐龙分箱顺序与边界算法
- encoder 类型及完整结构超参数
- 50 秒 episode 上限、decision interval、Unity scene/build/config hash

载入时发现版本、shape、字段顺序、顶点或动作分箱不一致，应快速失败并给出明确错误，不允许继续训练。

## 10. 边界情况与失败处理

- **无恐龙/无守卫：** 对应集合走明确空表示，forward 必须有限。
- **实体恰好达到上限：** 仍完整编码；只有下一次超上限部署被拒绝。
- **实体超过上限：** 视为环境契约错误并记录诊断；不得按距离排序后静默丢弃。
- **死亡目标：** 按现有 gameplay 生命周期退出活动集合；Wall/House 因稳定场景身份按本规格保留非活动行。
- **价格固定：** 只用 `food_normalized` 推断购买能力；价格配置变化必须触发版本更新。
- **Region 几何缺失或非预期：** 动作拒绝且环境输出诊断；训练启动时应更早通过配置校验拦截。
- **动作出现 NaN/Inf：** 拒绝动作、标记无效并记录；不得把非有限值传给四边形映射或生成服务。
- **50 秒结束：** 继续作为 natural terminal；通信、进程或重载异常才是 truncated。

## 11. 最小必要验证

本阶段按用户要求避免无关的大型验收，只保留接口与训练正确性直接相关的检查：

1. 五个 observation 流的 shape、dtype、字段顺序、mask 与 padding。
2. Deep Sets 及无 slot positional embedding 的 Set Transformer 对有效实体行置换保持集合不变性。
3. Guard/Dino 空集合时 forward 无 NaN/Inf。
4. `MaxDinos=10`：第 10 只可被完整观测，第 11 次部署被拒绝且不会静默截断。
5. Z1–Z5 分箱、Wait/三恐龙分箱在端点和边界附近一致。
6. `(u,v)` 的四角、中心及边界点按四边形公式映射正确。
7. PPO 与 FPO 单次 forward 的 shape 正确且输出有限；FPO 环境时间与流时间使用独立输入。
8. 新 checkpoint metadata 能拒绝旧 504/6 或错误区域顶点的模型。

并行运行的 20 局重载、四环境 4096-decision rollout、终止语义与故障恢复继续由并行训练规格负责，不在本规格重复扩张。

## 12. 预期实施影响面

实施计划预计需要修改以下边界，具体文件以 CodeGraph 和实施前审计为准：

- Unity：observation layout、training agent 的五流收集、4D action codec、BehaviorParameters observation/action specs、协议版本与配置校验。
- Python adapter：多 observation stream 保留、batch/Agent ID 映射、mask 传递、旧接口快速失败。
- `flow_rl`：Deep Sets、Set Transformer、Region Query/Cross-Attention、PPO/FPO actor-critic 接口、独立 critic encoder。
- 配置与 checkpoint：上限、归一化、区域顶点、动作分箱、encoder 参数和 provenance。
- 文档：并行训练规格中旧 504/6 条款的交叉引用、阶段 8.11 实施计划和验证证据。

不修改 `fpo/`、`ReinFlow/`、`PolicyFlow/` 官方参考仓库；算法专用逻辑不得泄漏到 Unity adapter。

## 13. 验收标准

本文档复核通过后，才能使用 `superpowers:writing-plans` 编写实施计划。后续实现完成必须满足：

- Unity 与 Python 对五流 207 标量、mask、上限和字段顺序完全一致。
- 4D 动作能稳定表达 `Z1–Z5 × (u,v) × (Wait/三种恐龙)`，Wait 正确忽略位置。
- 第 11 只恐龙无法生成，且不存在 observation 静默截断。
- Deep Sets baseline 与 Set Transformer final encoder 均能处理空集合和任意有效实体顺序。
- PPO/FPO 正式比较共享同规格 Set Transformer；Deep Sets 结果单列为架构消融。
- checkpoint/配置可追溯并拒绝旧 504/6 或几何不匹配模型。
- 50 秒规则、同场景并行训练架构、natural terminal/truncated 语义和单步动作边界无回归。
