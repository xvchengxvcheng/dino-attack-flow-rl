# Dino Attack · Flow RL

**简体中文** | [English](release-staging/dino-attack-flow-rl/README.en.md)

**搭建可以亲自游玩的战场，让 AI 学习何时、何地部署哪种恐龙，再把训练好的模型放回游戏。**

这是一个基于 Unity 6 的反向塔防项目。玩家既可以部署恐龙突破防线，也可以布置守军并亲自上阵，挑战 AI 的进攻。项目基于原版 Dino Attack，通过独立的 Python / PyTorch 训练系统，将 **PPO、FPO、PolicyFlow** 三种已训练策略接入游戏的 AI Strategy 菜单。

**直接体验：** [下载 Windows 游戏及训练附件（v0.1.0）](https://github.com/xvchengxvcheng/dino-attack-flow-rl/releases/tag/v0.1.0)。解压后运行 `Builds/DinoAttack-Windows-20260906/DinoAttack.exe`，保留同目录依赖。三种 AI 已内置，无需安装 Unity 或 Python。

[Flow RL 简介](#flow-rl) · [四张地图](#maps) · [地图四：星光守卫 + JEV 伴侣](#map4) · [重点工程](#engineering) · [模型状态](#status) · [运行指南](#quickstart) · [源码导航](#resources)

---

<a id="flow-rl"></a>

## Flow RL 是什么？

强化学习让 AI 通过反复对局和观察奖励来学习决策。在本项目中，AI 读取战场和资源情况，决定部署哪种恐龙、部署在哪里；训练器根据战斗结果调整模型。

**Flow RL** 指使用流模型生成动作的强化学习方法：先给出一个初始向量，由网络结合当前战场分几小步调整，最后转换成一次部署指令。这里的"几步"发生在模型内部，最终仍只提交一次动作。

本项目以 PPO 为基线，探索 FPO、ReinFlow、PolicyFlow 三种 Flow 方法。`flow_rl/` 是这些算法及训练、评估、导出工具所在的 Python 包。PPO、FPO、PolicyFlow 已接入游戏；ReinFlow 完成了 3DBall 实现，尚未接入 Dino。

### 当前策略的局限

- **PPO**：能取得胜利，但尚未稳定学会通过攻击房屋获取额外食物以争取更高终局奖励，有陷入局部最优的迹象。
- **FPO / PolicyFlow**：模型仍在调参阶段，部署时机和开局等待有待改善。这里的"探索阶段"指模型研究尚未收敛，并非游戏内随机探索动作。

---

<a id="maps"></a>

## 四张地图

| 地图 | 玩法 | 用途 |
|---|---|---|
| 地图一：开阔战场 | 玩家或 AI 持续部署恐龙进攻 | 训练与评估 |
| 地图二：分层随机战场 | 随机种子生成布局，可让 AI 重赛 | 训练与评估 |
| 地图三：防御战场 | 玩家布防 + 选择 AI 策略进攻 | 体验 AI 对战 |
| 地图四：星光守卫 | 布防后亲自操控角色加入战斗，含 JEV 伴侣 | 可玩性扩展 |

| 地图一：开阔战场 | 地图二：分层随机战场 |
|:---:|:---:|
| ![地图一开阔战场](../picture/地图一开阔战场.png) | ![地图二随机战场](../picture/地图二随机战场.png) |

| 地图三：玩家布防，AI 进攻 | 目前可用 AI 策略 |
|:---:|:---:|
| ![地图三玩家布防AI进攻](../picture/地图三_玩家布防AI进攻.png) | ![目前可用算法](../picture/目前可用算法.png) |

**单局规则**：存活恐龙抵达目标即进攻方获胜；战斗阶段最多 50 秒。部署需要足够资源、合法位置，且场上恐龙未达上限。摧毁房屋可获得额外肉量继续进攻。

---

<a id="map4"></a>

## 地图四：星光守卫 + JEV 伴侣

地图四在地图三的防守战场基础上，增加了可操控的二次元角色、第三人称跟随镜头、远程法杖与技能，以及一名有状态的 AI 伴侣"溯光"（JEV）。

### 操控角色与战斗

| 布防界面（含小地图） | 实战第三人称视角 |
|:---:|:---:|
| ![地图四星光守卫布防](../picture/地图四_星光守卫.png) | ![地图四实战场景](../picture/地图四_星光守卫_实战场景.png) |

布防完成、选择 AI 策略后，玩家直接操控角色参与战斗：

| 操作 | 按键 |
|---|---|
| 移动 / 奔跑 / 跳跃 | WASD / Shift / 空格 |
| 视角 / 镜头距离 | 鼠标右键拖动 / 滚轮 |
| 远程法杖攻击 | 鼠标左键 |
| 星陨冲击（范围伤害）| Q |
| 星光护盾（吸收 60 伤害 / 4 秒）| E |
| 疾风闪避 | R |
| 游戏速度 0.5 / 1 / 2 倍 | F1 / F2 / F3 |

法杖命中显示金色伤害数字，Q 范围技能为每个受伤目标显示紫色数字，击杀时保留最后一次反馈。地图四默认 0.5 倍速，右上角小地图实时显示玩家、友军、敌人与村庄目标。

### JEV 伴侣系统（溯光）

| JEV 交互面板（实战中） | 关系实验室 |
|:---:|:---:|
| ![JEV交互地图](../picture/地图四_jev交互地图.png) | ![关系设置](../picture/关系设置.png) |

"溯光"是一名有独立状态的 AI 伴侣，在战斗中跟随玩家并响应自然语言指令。系统由两层数据驱动：

- **关系变量**：好感、熟悉度、信任、尊重、压力五项数值，影响溯光的命令接受度、反馈语气和自主权。
- **荣格心理维度**：外倾、感觉、直觉、思维、情感等七项倾向，用于行为图权重，决定溯光主动行为的风格偏向。

游戏中可在"溯光·JEV 交互"面板实时发送自然语言指令（如"守住这里""跟随我""自主防卫"），也可通过快捷命令直接触发。关系数据保存在独立档案，不影响战斗存档。关系实验室（CompanionLab 场景）可离线调整参数并预设关系模板。

JEV 伴侣系统带有可选的本地 LLM 网关（`CompanionJevClient`，默认关闭）；网关离线时自动回退到本地意图解析，游戏流程不受影响。

> 地图四已完成本地开发验证（EditMode 20/20、PlayMode 10/10）及 Windows 构建，待审核后合并发布。v0.1.0 下载包为此前三张地图版本。

---

<a id="engineering"></a>

## 重点工程

### 1. 玩家、AI、伴侣使用同一套部署规则

无论玩家点击、AI 发出指令，还是伴侣自主行动，都经过同一流程：**提出请求 → 检查规则 → 扣除资源并生成**。检查包括当前阶段、肉量、位置合法性、导航有效性和对象重叠。`DinoDeploymentRules` 负责规则判断，`DinoSpawner.TryDeploy` 是唯一部署入口，确保训练和正常游玩遵守相同的玩法约束。

源码：[部署规则](dino-attack/Assets/LlamAcademy/Dinos/Deployment/DinoDeploymentRules.cs) · [DinoSpawner](dino-attack/Assets/LlamAcademy/Dinos/Player/DinoSpawner.cs) · [回合管理](dino-attack/Assets/LlamAcademy/Dinos/RoundManagement/RoundManager.cs)

### 2. 恐龙战斗行为：目标追踪、寻路与攻击

每只恐龙持有自己的目标列表。**Behavior Graph** 决定移动还是攻击，NavMesh 负责寻路，C# 攻击节点执行伤害。目标进入检测范围时触发回调，`DinoTargetTracker` 只记录存活目标并监听其死亡；目标死亡后恐龙自动切换下一个目标，无目标时继续向村庄推进。死亡事件只处理一次，避免重复结算。

源码：[目标追踪器](dino-attack/Assets/LlamAcademy/Dinos/Unit/DinoTargetTracker.cs) · [Unit 生命周期](dino-attack/Assets/LlamAcademy/Dinos/Unit/Unit.cs) · [攻击节点](dino-attack/Assets/LlamAcademy/Dinos/Behavior/AttackClosestObjectAction.cs)

### 3. 固定时间步长与导航就绪

训练时 Unity 需要等待 Python 返回动作，等待时长会影响战斗推进。项目用固定时间步长驱动关键战斗逻辑和行为图，并统一 `timeScale` 与 capture clock 配置。开局前等待导航组件就绪并检查必要路径，移动时区分"可到达""只能走到一部分"和"无路径"三种状态。

导航就绪修复后，0 / 75 ms 等待对照实验 20 局中 19 局结局一致，仍有 1 局不同，尚在完善。

### 4. 统一区域坐标：显示、部署与 AI 动作共用

战场有五个四边形部署区域，区域显示、范围检测、部署位置校验以及 AI 动作到地面坐标的转换，全部使用同一份坐标数据。玩家看到的区域边界与实际规则判定严格一致。

源码：[四边形几何](dino-attack/Assets/LlamAcademy/Dinos/Map/Core/QuadrilateralXZ.cs) · [RuntimeUI](dino-attack/Assets/LlamAcademy/Dinos/UI/RuntimeUI.cs)

### 5. 战场观测与并行训练

训练器通过 **ML-Agents Low-Level API** 与 Unity 交换战场信息、动作和奖励。观测分为全局、区域、城墙、守军、房屋、恐龙六组，共 **270 个数值**，预留固定容量（最多 10 只恐龙、6 座墙等），用 `valid_mask` 标记有效位置。网络输出 **4 个数值**，分别表示区域、区域内坐标和等待/兵种选择。

网络先用小型 MLP 将每个对象编码为 48 维特征，再通过集合注意力（ISAB / SAB）建模对象间关系，最后经五区域交叉注意力汇总为 128 维局势表示，送入独立的策略网络（Actor）和价值网络（Critic）。

并行训练同时运行 **16 个 Unity 游戏**，`timeScale = 1`，每轮目标收集 16,384 条交互后更新模型；训练器追踪环境编号、Agent 身份和模型版本，防止多环境数据串混。

源码：[Unity 适配](flow_rl/src/flow_rl/envs/unity.py) · [并行环境](flow_rl/src/flow_rl/envs/parallel_unity.py) · [版本化采集器](flow_rl/src/flow_rl/training/versioned_collector.py) · [Set Transformer](flow_rl/src/flow_rl/models/dino_set_transformer.py)

### 6. 模型导出与游戏内推理

选定模型后，将 PyTorch 权重导出为 ONNX，由 Unity Sentis 在本地计算动作。导出时记录来源版本和文件校验值，并核对相同观测在 PyTorch、ONNX 和 Unity 三端的输出是否一致。

PolicyFlow 采用"先行为克隆、再强化学习"的两阶段训练：从 PPO 在两张地图上各采集 500 局成功对局，共 57,459 条样本做预训练，再通过 RL 微调。

```
flowchart LR
    Unity[战斗 · NavMesh · 奖励] -->|战场观测| Python[采集 → 训练 → 评估]
    Python -->|训练动作| Unity
    Python -->|导出 ONNX| 游戏内 AI[AI Strategy · 本地推理]
```

游戏内推理不需要 Python 环境。

---

<a id="status"></a>

## 模型状态

截至 2026-09-06，已部署以下三种 ONNX 模型：

| 策略 | 当前版本 | 游戏内动作生成方式 |
|---|---|---|
| PPO | `step-4721698` | 策略输出均值，经 tanh 限制范围 |
| FPO | `step-2606819`（策略版本 159）| 从零向量出发，Euler 积分，Flow 网络调用 4 次 |
| PolicyFlow | `step-2147452`（策略版本 131）| 初始向量与扰动均为零，midpoint 积分两步，Flow 网络调用 4 次 |

ReinFlow 完成了 3DBall 实现，尚未接入 Dino 游戏。

**后续工作**：多随机种子统一比较、相同输入下训练与游戏端轨迹完整核对、ReinFlow Dino 接入，以及探索连续多动作预测（action chunk）。

### 验证记录摘要

| 日期 | 范围 | 结果 |
|---|---|---|
| 2026-09-01 | 选定回归集 33 项 | 33/33 通过 |
| 2026-09-03 | 导航就绪修复后 0 / 75 ms 等待对照，20 局 | 19/20 结局一致 |
| 2026-09-05 | PolicyFlow 导出数值对照 + Unity 实际运行；地图三 PPO / PolicyFlow 回归 | 2/2 通过 |
| 2026-09-05 | Python 测试 | 372 通过 / 1 失败 / 3 未选入；Unity PlayMode 历史 46/59 |
| 2026-09-11 | 地图四（EditMode + PlayMode + 动作专项） | 20/20 + 10/10 + 3/3 通过 |

---

<a id="quickstart"></a>

## 运行指南

### Unity 游戏（不需要 Python）

1. 使用 **Unity 6000.3.22f1** 打开 `dino-attack/`，依赖布局如下：

   ```
   game_project/
   ├── dino-attack/     # Unity 工程
   ├── ml-agents/       # release_23，含 com.unity.ml-agents
   └── flow_rl/         # Python 训练与导出
   ```

2. 打开 `Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity`，进入 Play Mode 选择地图。
3. 地图一、二点击 **Start** 后部署恐龙；地图三、四完成布防后选择 **PPO / FPO / PolicyFlow** 开始。

### Python 训练（可选）

<details>
<summary>展开查看训练入口</summary>

已用环境：Python 3.10、PyTorch 2.2.1 + CUDA 12.1、ML-Agents Python 1.1.0。

```powershell
cd flow_rl
conda env create -f environment.yml
conda activate flow-rl
python -m pip install -r requirements.lock
```

训练入口：
- PPO：`python -m flow_rl.cli.train_dino_parallel_ppo --config <yaml>`
- FPO：`python -m flow_rl.cli.train_dino_parallel_fpo --config <yaml>`
- PolicyFlow：`python -m flow_rl.cli.train_dino_parallel_policyflow --config <yaml>`

正式训练前需要构建 Unity Player、填写程序路径并指定输出目录。现有配置含本地路径，使用前按实际目录调整。

</details>

---

<a id="resources"></a>

## 源码导航

| 路径 | 内容 |
|---|---|
| [dino-attack/Assets/LlamAcademy/Dinos/](dino-attack/Assets/LlamAcademy/Dinos/) | 部署、战斗、地图、回合、训练桥接、推理控制器、地图四角色与 JEV 伴侣 |
| [flow_rl/src/flow_rl/](flow_rl/src/flow_rl/) | 游戏连接、算法、数据采集、训练、评估、导出 |
| [flow_rl/configs/](flow_rl/configs/) | 各阶段训练配置（PPO / FPO / PolicyFlow，3DBall 与 Dino） |
| [dino-attack/Assets/Resources/DinoInference/](dino-attack/Assets/Resources/DinoInference/) | 三种 ONNX 模型及 Unity 模型配置 |
| [docs/PROJECT_PLAN.md](docs/PROJECT_PLAN.md) | 开发阶段、关键决策、验证证据和未完成项 |
| [docs/interview/Unity战斗与AI项目_秋招学习讲义.md](docs/interview/Unity战斗与AI项目_秋招学习讲义.md) | 战斗 / Unity / AI 模块讲解与面试追问 |
| [reports/phase8/](reports/phase8/) | 各子任务验证报告 |

## 基础项目与算法来源

游戏基础来自 [LlamAcademy / Dino Attack](https://github.com/llamacademy/dino-attack)，沿用基础玩法、单位系统、行为图与导航。环境桥接使用 [Unity ML-Agents](https://github.com/Unity-Technologies/ml-agents)。Flow 算法参考自：[FPO](https://github.com/akanazawa/fpo)、[ReinFlow](https://github.com/ReinFlow/ReinFlow)、[PolicyFlow](https://github.com/PolicyFlow2026/PolicyFlow)。本仓库的贡献是 Unity 环境适配、工程实现、实验排错、游戏部署，以及地图四的角色战斗与 JEV 伴侣系统。
