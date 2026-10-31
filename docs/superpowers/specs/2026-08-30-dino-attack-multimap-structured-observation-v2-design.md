# Dino Attack 双地图共享模型与结构化观测 v2 设计

**日期：** 2026-08-30  
**对应阶段：** 阶段 8 / `TASK-8.11` 协议升级  
**状态：** 用户已批准  
**事实来源：** `docs/PROJECT_PLAN.md`

## 1. 背景

`dino_attack_structured_set_v1` 已实现五个观测流，并把 Z1–Z5 四边形顶点作为 Python 协议和 Set Transformer checkpoint 中的固定常量。该结构只能描述当前地图。项目后续会制作第二张独立 Unity Scene，并要求两张地图使用同一个端到端训练模型，因此区域几何必须来自当前 episode 的场景观测，而不能继续固定在 Python 配置或模型 buffer 中。

当前项目已经有唯一玩法目的地：`RoundManager.DinoTarget`。战斗开始时，`RoundManager.StartRound()` 把它写给已存在恐龙；战斗中生成的恐龙由 `Dino.Start()` 读取它；`Dino.SetDestination()` 将位置写入行为树的 `EggLocation`，无可攻击目标时 `MoveToTargetLocationAction` 继续向该位置导航；恐龙进入对应 `EggRadius` 后判定玩家胜利。因此不得创建重复的 `FinalDestination`。

## 2. 已批准目标

1. 两张独立 Unity Scene 共用同一个模型和同一个 observation/action 版本。
2. 两张地图都固定为五个四边形部署区，动作第一维始终选择 `Z1–Z5`。
3. 两张地图使用相同世界坐标原点、相同尺寸和相同固定归一化边界。
4. 将当前场景的五区 A/B/C/D 顶点作为第六个动态观测流发送。
5. 将现有 `RoundManager.DinoTarget` 的归一化 x/z 加入 Global；不创建第二个目标点。
6. 第二张地图完成后，每个 episode 以 50%/50% 随机选择两张地图；当前只有第一张地图时只运行 `Dinos.unity`。

## 3. 非目标

- 不把地图宽度、深度或 map ID 输入网络。
- 暂不把地貌速度倍率或防御倍率输入网络。
- 不改变 4D 动作、50 秒自然失败、单步动作、奖励、资源、战斗、NavMesh 或视觉规则。
- 不在本协议升级中创建第二张地图、复制村落或实现无法验证的双场景选择器。
- 不保留 v1 checkpoint 兼容。v1 与 v2 shape 不同，载入必须快速失败。

## 4. 协议 v2

协议名为 `dino_attack_structured_set_v2`，流顺序固定如下：

| 索引 | 流 | Shape | 字段 | 标量数 |
|---:|---|---:|---|---:|
| 0 | Global | `(5,)` | `remaining_time, food_normalized, last_action_valid, dino_target_x_norm, dino_target_z_norm` | 5 |
| 1 | Regions | `(5,8)` | `Ax, Az, Bx, Bz, Cx, Cz, Dx, Dz` | 40 |
| 2 | Walls | `(6,5)` | v1 字段不变 | 30 |
| 3 | Guards | `(11,7)` | v1 字段不变 | 77 |
| 4 | Houses | `(8,6)` | v1 字段不变 | 48 |
| 5 | Dinos | `(10,7)` | v1 字段不变 | 70 |
| | **合计** | 六个独立流 | | **270** |

Regions 没有 `valid_mask`。两张地图都必须恰好提供五个有效区域，并按 `DeploymentZoneId` 的 `Z1..Z5` 顺序写入。任一区域缺失、重复、几何无效或顶点非有限时，训练场景验证失败，不输出伪造的零行。

Guards 的正式上限提高为 11：最多 8 名地面守卫，加最多 3 名墙上弓箭手。现有地图仍可只生成 5 名地面守卫；未使用行保持 v1 的全零 padding，`valid_mask=0`。守卫字段、类型语义和 `is_wall_guard` 不变，不为第二张地图增加新的兵种列。

## 5. 坐标规则

两张地图共用固定边界：

```text
X = [-55.5, 48.5]
Z = [-53.0, 29.0]
```

目标、实体位置和区域顶点都使用同一个线性映射转换到 `[-1,1]`。边界是 Unity/Python 协议常量和 checkpoint metadata，但宽度、深度不作为运行时观测。第二张地图必须沿用同一世界坐标原点和范围。

Global 的最后两个值直接读取 `RoundManagerSource.DinoTarget.position`。`DinoTarget` 为空时 builder 必须抛出明确的场景契约错误；禁止用 `(0,0)` 代替。

## 6. Unity 数据来源

- `DinoStructuredObservationBuilder` 持有按 ID 排序的五个 `DinoDeploymentZone` 引用。
- Regions 每个 Academy step 与其他五流使用同一个 cached frame，确保六个 Sensor 读取同一快照。
- 每个区域通过 `WorldVertices` 提供 A/B/C/D；不从 Editor builder 常量、报告 JSON 或 Python YAML反向读取。
- `DinoFormalTrainingSceneInstaller` 把同一组五区引用同时接到 `DinoTrainingAgent` 和 observation builder。
- 每张正式地图的 `RoundManager` 自行绑定本地图现有的 `DinoTarget`。

## 7. 网络结构

### 7.1 Deep Sets 基线

墙、守卫、房屋和恐龙仍按 valid mask 做 masked mean/max。Global MLP 输入从 3 改为 5。Regions 的每行通过共享 `8 -> 32 -> 64` Geometry MLP，并叠加对应 `Z1–Z5` learned identity embedding；五个 token 按固定区域顺序拼接后参与最终融合。融合输入为 `4×128 + 5×64 + 64 = 896`，Fusion MLP 为 `896 -> 256 -> 256`。区域顺序与动作区域箱绑定，因此不对 Regions 做行置换不变性测试。

### 7.2 Set Transformer 正式编码器

删除来自 `DinoProtocol.region_vertices` 的固定 `region_vertices` buffer。每次 forward 从 `Regions (5,8)` 读取几何，经过现有 Geometry MLP，并与五个 learned region embeddings 相加形成 Region Query。Defense/Dinosaur ISAB、分离式 Cross-Attention、FiLM、256 维输出和 PPO/FPO 独立 actor/critic encoder 规则保持不变。

Global MLP 输入改为 5；时间 FiLM 仍只读取 Global 第 0 项。`DinoTarget x/z` 只进入完整 Global 编码，不冒充环境时间或 FPO flow-time `tau`。

## 8. Python 协议与 checkpoint

新配置 `flow_rl/configs/dino_attack_structured_set_v2.yaml` 记录六流字段、固定归一化边界、实体上限和既有 4D action。它不保存某张地图的区域顶点。

checkpoint metadata 至少记录：

- `protocol_version=dino_attack_structured_set_v2`
- 六个 observation shape 与字段顺序
- 固定归一化边界
- 4D action 版本、分箱顺序和边界规则
- encoder 类型与结构参数

地图几何随 observation 输入，不再参与 checkpoint 兼容性判定。v1 checkpoint 因 stream 数和 shape 不一致而被拒绝。

## 9. 第二张地图接入边界

本次只使协议和模型具备跨地图能力，不创建不存在的场景。第二张地图完成后，单独执行接入任务：

1. 验证它使用相同坐标范围、唯一 `DinoTarget` 和恰好五个有效四边形区。
2. 把两个正式场景加入训练 Player。
3. 在 episode 边界用确定性 seed 进行 50%/50% 选择。
4. 分别记录 Map 1、Map 2 和混合评估，不只报告合并均值。

只有第一张地图时生成的 v2 checkpoint 可以继续加载并在双地图上训练，但不能标记为具有跨地图能力。

## 10. 最小必要验证

1. Unity v2 layout 为六流、270 标量，Guards 上限为 11；Global 目标坐标来自现有 `DinoTarget`。
2. 当前场景五区按 Z1–Z5 输出，四顶点与 `DinoDeploymentZone.WorldVertices` 归一化结果一致。
3. `DinoTarget` 缺失、区域缺失/重复/无效时快速失败。
4. Python 接受 v2 六流并拒绝 v1/错序/错 shape。
5. Deep Sets 和 Set Transformer 在两组不同合成区域几何下输出有限，且改变 Regions 会改变编码结果。
6. Set Transformer 不再包含固定 `region_vertices` buffer；checkpoint 不再绑定当前地图顶点。
7. PPO/FPO 各完成一次有限 forward/update smoke；不重复全量测试或正式长训练。
8. 当前地图正式 Player 的最终 Task 10 集成只在视觉任务交回 stopped/clean Editor 后执行一次。

## 11. 取代关系

本规格取代 `2026-08-30-dino-attack-structured-set-observation-action-design.md` 中以下 v1 条款：五流 207 标量、Global `(3,)`、区域顶点作为模型常量/buffer、配置/checkpoint 固定当前地图顶点。其 4D 动作、实体字段、mask/padding、MaxDinos=10、Set Transformer 主体、公平比较和单步动作规则继续生效。

`2026-08-30-dino-attack-structured-parallel-training.md` 的 Tasks 1–9 作为 v1 实施历史保留；其 Task 10 在 v2 升级完成前暂停，不得用旧 Player 冻结最终证据。
