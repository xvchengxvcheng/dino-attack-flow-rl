# Dino Attack 持续决策训练环境实施计划

**目标：** 在不改变原始玩家场景行为的前提下，为 `dino-attack` 增加可由 ML-Agents LLAPI 驱动的持续部署训练环境，并配置项目固定的 Unity MCP。

**源场景：** `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`（只读保留）

**训练场景：** `dino-attack/Assets/LlamAcademy/Dinos/Scenes/DinoAttackTraining.unity`（新增）

## 任务 1：持续部署 API

- 在 `DinoSpawner` 中抽取 `TrySpawnDino`，统一资源扣除、实例化、死亡订阅和生成事件。
- 玩家鼠标路径继续只在 `GameState.Setup` 调用该 API。
- 训练路径显式传入 `allowDuringRunning=true`，仅允许 `Setup/Running`，不放宽其他状态。
- 验证资源不足、非法状态和成功生成的返回结果。

## 任务 2：连续动作和部署区域

- 新增纯逻辑 `DinoTrainingActionCodec`：6 维连续动作映射为 Wait/Place、三类恐龙 argmax 和归一化坐标。
- 新增训练区域组件，把 `[-1,1]²` 映射到 BoxCollider XZ 边界，向地面射线投影并做 NavMesh/碰撞合法性检查。
- 为 codec 编写 EditMode 单元测试：shape、阈值、argmax、坐标边界和非有限值拒绝。

## 任务 3：持续决策 Agent

- 一个训练场景一个 planner Agent；恐龙和敌人继续使用现有 Behavior Graph/NavMesh。
- Observation 使用固定槽位：全局资源/时间、恐龙、守军、墙体及合法性摘要。
- 每隔可配置仿真秒数 RequestDecision；Agent 可 Wait 或 Place。
- 第一次成功部署后自动从 Setup 进入 Running；Running 中继续允许部署。
- 守军死亡、墙体摧毁产生 shaping reward；胜负终端奖励占主导。
- 自然胜负调用 `EndEpisode`；时间/决策安全上限调用 `EpisodeInterrupted`。
- episode 后重载训练场景，避免污染原始回合循环并确保完整 reset。

## 任务 4：训练场景复杂化

- 通过 Unity Editor 脚本复制原始场景，禁止手改 `.unity` YAML。
- 禁用训练场景中的玩家输入/UI 可视化，但保留游戏战斗逻辑。
- 添加训练 Agent、BehaviorParameters、训练区域、决策调度和场景重载器。
- 添加可配置的运行时场景变体：开放场、双通道、瓶颈障碍；使用 NavMeshObstacle carving 增加路径和部署差异。
- 第一阶段固定 Hard 难度；场景变体 seed 和索引写入统计。

## 任务 5：Unity MCP

- 在目标项目安装 `com.unity.pipeline`。
- 在 Codex MCP 配置中注册 `unity mcp --project-path <dino-attack>`。
- 使用精确 Editor `D:\job\6000.3.22f1\Editor\Unity.exe` 打开/批处理项目。
- 验证 Pipeline server、MCP 工具目录和项目固定路径。

## 任务 6：验证与退出标准

- Unity EditMode tests 通过。
- 批处理导入/编译无 C# error。
- 训练场景存在，原始场景内容哈希未变化。
- 进入 PlayMode 后 Agent 能在 Running 阶段成功生成恐龙。
- Wait、非法位置、资源不足、自然终止和 interrupted 分别有可观察证据。
- Python LLAPI smoke 能读取 behavior spec、提交 `(N,6)` float32 动作并完成至少一个 episode。
- 更新 `docs/PROJECT_PLAN.md`：决策、改动路径、实际命令、结果和阻塞项。

