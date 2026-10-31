# Codex 项目协作规则

## 项目目标与事实来源

- 本项目的目标是在 Unity ML-Agents 环境上实现 FPO、ReinFlow 和 PolicyFlow。
- 每次开始工作前，必须完整阅读 `docs/PROJECT_PLAN.md`。该文件是项目范围、阶段状态、验收标准和关键决策的唯一事实来源。
- 对话记录只能作为补充背景；当对话与 `docs/PROJECT_PLAN.md` 冲突时，以计划文件为准，并向用户指出冲突。

## 每次任务的工作流程

1. 阅读 `docs/PROJECT_PLAN.md` 的“当前状态”“全局约束”“对应阶段”和“决策记录”。
2. 用只读命令检查工作区状态；已有修改均视为用户工作，禁止擅自覆盖、还原或删除。
3. 明确本次任务属于哪个阶段、要完成哪些复选框以及退出标准；如果请求超出当前阶段，先说明依赖和影响。
4. 修改前先建立或补充与当前任务成比例的测试；实现后运行最强可用验证。
5. 完成任务后立即更新 `docs/PROJECT_PLAN.md`：复选框、阶段状态、验证证据、产物路径、阻塞项和决策记录。

## CodeGraph 使用规则

- 以下本地仓库都已建立 `.codegraph/`：`ml-agents/`、`fpo/`、`ReinFlow/`、`PolicyFlow/`。理解或定位其中代码时，必须先进入对应仓库运行：

  ```powershell
  Set-Location <repository>
  codegraph explore "<问题、文件名或符号名>"
  ```

- CodeGraph 返回的当前源码和调用路径不足时，再使用 `rg` 或读取具体文件。
- 每次操作前动态检查目标仓库是否存在 `.codegraph/`，不要假设索引只存在于 `ml-agents/`。
- 对 `flow_rl` 等当前没有 `.codegraph/` 的目录，直接使用 `rg`、`rg --files` 和针对性文件读取。
- 不得自行创建、删除或重建 CodeGraph 索引；索引由用户决定。

## 工作区安全

- `ml-agents` 是基于官方 `release_23` 的上游仓库，目前包含用户修改和未跟踪文件。禁止使用 `git reset --hard`、`git checkout --`、`git clean` 或任何等价清理命令。
- `fpo/`、`ReinFlow/`、`PolicyFlow/` 是独立 Git 仓库和官方参考源码，默认只读。除非当前计划阶段和用户明确授权要求修改，否则只从中复现、核对和移植算法，不直接开发；其未跟踪 `.codegraph/` 必须保留。
- 未经用户明确要求，不修改 Unity 示例场景、ProjectSettings、Packages、现有构建和 PPO 基线产物。
- 优先在根目录的独立 `flow_rl/` Python 包中实现外部训练器，通过 ML-Agents Python Low-Level API 连接 Unity；不要为了方便直接侵入 `mlagents-learn`。
- 基线环境 `.conda/mlagents-r23` 视为冻结环境。新的研究依赖安装到独立项目环境；存在依赖冲突时，为官方算法复现实验创建隔离环境。

## 官方算法参考仓库

- FPO：`fpo/`，官方远端 `https://github.com/akanazawa/fpo.git`。优先从 `gridworld/models/fpo.py`、`gridworld/main.py` 和 `phc/scripts/train.py` 追踪核心实现与训练入口。
- ReinFlow：`ReinFlow/`，官方远端 `https://github.com/ReinFlow/ReinFlow.git`。优先从 `agent/pretrain/`、`agent/finetune/reinflow/` 及其 Hydra 配置追踪预训练和微调流程。
- PolicyFlow：`PolicyFlow/`，官方远端 `https://github.com/PolicyFlow2026/PolicyFlow.git`。优先从 `policyflow/policyflow_torch/agents/policyflow/`、`policyflow/policyflow_torch/modules/flow/` 和 `scripts/multigoal/` 追踪算法与最小复现入口。
- 正式移植前先用 CodeGraph 核对当前 commit 的真实符号、调用路径和配置；不得仅根据论文描述或记忆重写。
- 官方仓库的复现实验与 `flow_rl` 中的 Unity 移植实验分开记录，依赖环境、命令和结果不得混用。

## 实现与测试约束

- Python 代码采用小而清晰的模块边界、类型标注和可测试接口；算法专用逻辑不得泄漏到 Unity 环境适配层。
- Unity 适配层必须保持 `DecisionSteps` 中的 Agent 顺序，并正确区分 `TerminalSteps.interrupted` 与自然终止。
- 三种 Flow 算法共享环境、rollout、GAE、critic、日志和评估协议，但算法特有的概率模型、ratio 和正则项保持独立。
- 先用单步动作完成 3DBall 正确性验证，再进入 Crawler/Walker 和 action chunk；不得提前把多步动作引入基础对照实验。
- 新功能和 bug 修复遵循测试先行。测试至少覆盖 shape、dtype、Agent ID 映射、终止处理、数值有限性、动作边界和固定随机种子复现。
<!-- - 公平比较同时报告 environment steps、wall-clock time、NFE、推理延迟、显存、回报和随机种子方差。 -->

## 计划维护规则

- 阶段状态只使用：`未开始`、`进行中`、`阻塞`、`已完成`。
- 任一时刻原则上只有一个实现阶段处于 `进行中`；不依赖共享状态的调查任务可以并列，但必须在计划中说明。
- 勾选任务时，在同一阶段的“验证证据”中记录日期、命令、结果和关键产物路径。
- 计划发生范围、接口、依赖、指标或阶段顺序变化时，先更新“决策记录”，再修改对应任务。
- 不删除历史决策；被替代的内容标记为“已取代”，并链接到新决策。
- 遇到无法安全推断的算法细节时，优先核对论文和官方仓库，然后把结论及来源写入计划。
