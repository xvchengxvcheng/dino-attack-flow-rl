# 阶段 4–5：Flow 基础模块与 FPO 设计

## 目标

在现有 `flow_rl` Unity LLAPI 训练框架中新增可复用的单步 Rectified Flow Policy 基础模块，并以本地只读参考仓库 `fpo/` 的 `main` commit `418c2554f7cd22d52e14c07d951280929d73bf2f` 为算法事实来源实现 FPO。实现继续复用阶段 2–3 已验证的 Unity 适配、Agent ID 轨迹、GAE、observation normalizer、critic、checkpoint、日志和独立 evaluator。

本设计只覆盖阶段 4 和阶段 5，不引入 action chunk，也不修改 `ml-agents/`、`fpo/` 或 Unity 场景与构建。

## 来源优先级与有意差异

FPO 数学与更新规则以本地 `fpo/gridworld/models/fpo.py` 和 `fpo/gridworld/models/diffusion_policy.py` 为准；论文和仓库中的 Playground/PHC 实现只用于交叉核对。具体采用：

- CondOT 路径 `x_t = (1-t) * eps + t * action`，目标速度 `action - eps`。
- 同一批 rollout 保存的 `eps`、`t` 和旧 CFM loss 在多个更新 epoch 中复用。
- advantage 默认按本地实现归一化为有符号值，不执行 advantage shift；`positive_advantage=False` 是固定的首轮正式实验配置。
- 每个样本先裁剪 CFM loss difference 到 `[-3, 3]`，跨 Monte Carlo 样本求均值后再次裁剪到 `[-3, 3]`，再计算 `ratio = exp(difference)`。
- actor 使用 PPO clipped surrogate；critic 使用 MSE；actor 和 critic gradient norm 分别裁剪。

以下差异由现有项目约束决定，必须写入运行配置和报告：

- 官方 GridWorld sampler 默认 10 步；本项目阶段 4 要求默认 NFE=4，并允许 1、2、4、8。
- 官方 GridWorld 网络为小型 ReLU MLP；本项目使用与 Gaussian PPO 同量级的 2×128 SiLU 主干，并明确记录参数量。
- 官方 GridWorld 直接输出动作；Unity 连续动作要求 `[-1, 1]`，因此 Flow 在无界 latent action 上训练，执行前使用 `tanh`。rollout 同时保存 latent action 和实际 bounded action。
- 官方训练样本数 `num_fpo_samples=50` 作为默认值保留，但允许通过配置降低以执行 smoke test。

## 架构

### Flow 模型与采样

`flow_rl.models.flow` 提供四个边界清晰的组件：

1. `FlowTimeEmbedding`：将形状 `(batch, 1)` 的 float32 flow time 编码为固定维度向量。
2. `FlowStateEncoder`：复用现有多 observation stream flatten 约定，将状态编码为条件向量。
3. `ConditionalVelocityMLP`：以状态编码、时间编码和当前 latent action 为输入，输出同 action size 的速度。
4. `EulerFlowSampler`：从由显式 `torch.Generator` 产生的标准高斯噪声出发，按 0→1 等距 Euler 步积分。返回最终 latent action、`tanh` bounded action、NFE 和可选路径。

采样 API 不隐式读取全局随机状态。NFE 只能为正整数；正式配置验证进一步限制为 1、2、4、8。所有输入、速度、latent action 和 bounded action 都必须是有限 float32 tensor。

### FPO 策略与数据流

`FlowActorCritic` 组合 velocity network 和现有 `ValueCritic`。一次训练动作决策产生：

- 发送给 Unity 的 bounded action；
- 作为 CFM 数据目标的 latent action；
- 每个动作的 `N_mc` 组独立 `eps` 和 `t`；
- 用 rollout 时 actor 计算并 detach 的旧 CFM loss；
- critic value。

共享 `OnPolicyCollectorCore` 只负责 LLAPI 事件语义、Agent ID pending/completed 状态、自然终止/截断和轨迹顺序。`PPOCollector` 与 `FPOCollector` 分别把算法辅助数据封装成 `PPOBatch` 和 `FPOBatch`。这样 FPO 不复制环境状态机或 GAE，同时不会把 CFM ratio 伪装成 Gaussian log-prob。

`FPOUpdater` 对每个 minibatch 重新计算相同 `eps/t/latent_action` 下的新 CFM loss，严格按本地 clone 的两级 `[-3,3]` 裁剪生成 ratio，再使用有符号归一化 advantage 和 PPO clip。它独立记录 flow loss、log-ratio、ratio、clip fraction、proxy KL、critic loss、actor/critic gradient norm，并在任何非有限值出现时立即失败且不写坏 checkpoint。

### 训练、评估与 checkpoint

FPO trainer 复用现有 Unity adapter、normalizer、episode tracker、run directory、CSV/TensorBoard logger 和 checkpoint 原子写入。Flow checkpoint 的配置或 metadata 必须包含：

- observation shapes、action size、state/time/velocity 网络配置；
- solver=`euler`、direction=`0_to_1`、NFE 和 action transform=`tanh`；
- `num_fpo_samples`、ratio difference clamp、PPO clip 和 `positive_advantage=false`；
- normalizer、model、optimizer、environment steps、seed 和本地 FPO commit。

恢复训练时严格核对这些结构性字段。评估使用独立 worker 和显式固定 RNG；Flow 保持随机采样语义，但相同 checkpoint、seed、episode 序号和 NFE 必须生成可重复动作序列。NFE 消融对同一 checkpoint、同一 observation batch 和同一初始噪声分别运行 1、2、4、8 步，避免把 RNG 差异误算为 solver 差异。

### 官方最小复现

官方 GridWorld 复现与 Unity 移植隔离：新建 `.conda/fpo-gridworld` 环境，依据 `fpo/gridworld/requirements.txt` 安装依赖，不改变冻结环境 `.conda/mlagents-r23` 或项目环境 `.conda/flow-rl`。复现脚本从只读 `fpo/` 导入官方类，在临时输出目录中禁用在线 W&B、关闭 render，并执行一个有界 rollout/update smoke run；记录 Python、依赖、commit、命令、seed、耗时和有限 loss/ratio 结果。不会直接运行 `gridworld/main.py` 中硬编码的 2 亿步正式任务。

## 文件边界

预计新增：

- `flow_rl/src/flow_rl/models/flow.py`
- `flow_rl/src/flow_rl/models/fpo_policy.py`
- `flow_rl/src/flow_rl/algorithms/fpo.py`
- `flow_rl/src/flow_rl/training/on_policy.py`
- `flow_rl/src/flow_rl/training/fpo_collector.py`
- `flow_rl/src/flow_rl/training/fpo_config.py`
- `flow_rl/src/flow_rl/training/fpo_trainer.py`
- `flow_rl/src/flow_rl/evaluation/fpo.py`
- `flow_rl/src/flow_rl/cli/train_fpo.py`
- `flow_rl/src/flow_rl/cli/evaluate_fpo.py`
- `flow_rl/src/flow_rl/cli/benchmark_flow.py`
- `flow_rl/configs/phase5_fpo_3dball*.yaml`
- 与上述模块一一对应的单元及集成测试
- 阶段 4/5 的版本化验证和官方复现报告

预计修改：

- `flow_rl/src/flow_rl/training/collector.py`：在保持公开行为不变的前提下委托共享 on-policy 核心。
- `flow_rl/src/flow_rl/training/trainer.py`：只抽取确实被 PPO/FPO 共用的运行设施，不合并算法更新。
- `flow_rl/src/flow_rl/models/__init__.py`、相关包导出与 `flow_rl/pyproject.toml` CLI 入口。
- `docs/PROJECT_PLAN.md`：阶段状态、复选框、验证证据、产物和决策记录。

如果实现中证明无需修改 `trainer.py` 即可复用设施，则保持该文件不动，避免无关重构。

## 错误处理与可观测性

- 模型、batch 和 updater 在边界验证 shape、dtype、device、动作范围和有限性。
- collector 延续阶段 2/3 的重复 Agent ID、无 pending terminal 和 interrupted bootstrap 检查。
- 配置拒绝未知键、非法 NFE、空 run directory 复用、训练/评估 worker 冲突和不兼容 checkpoint。
- benchmark 先 warm up，再报告 batch size、device、NFE、重复次数、均值/P50/P95 latency；CUDA 时额外报告 peak allocated memory，CPU 时明确写为不可用。
- 训练指标始终包含 environment steps、NFE 和 `num_fpo_samples`，以便把环境成本、solver 成本和 Monte Carlo 成本分开解释。

## 测试与验收

实现遵循测试先行，并按以下顺序加强验证：

1. Flow 单元测试：time/state/velocity shape、Euler 手算轨迹、动作边界、NFE 调用次数、固定 seed、finite gradient、CPU/CUDA 条件分支。
2. FPO 数值测试：CFM 公式、同噪声/时间复用、两级 difference clamp、ratio、正/零/负 advantage 下的 clipped surrogate、有限 loss 和 gradient。
3. Collector 回归：PPO 现有 Agent ID/terminated/truncated 测试全部保持通过；新增 FPO auxiliary data 对齐测试。
4. checkpoint 与配置：完整往返、结构字段不匹配拒绝、恢复后随机序列和 solver 配置一致。
5. 官方 smoke：隔离环境执行本地 clone 的有界 rollout/update。
6. Unity smoke：3DBall 短程训练、checkpoint、独立评估和恢复。
7. 正式实验：seed 0/1/2 各 500k environment decisions；每个最终 checkpoint 独立评估 100 episodes；至少 2/3 seed 平均回报达到 90，且所有核心数值有限。
8. NFE 消融：同一最终 checkpoint 在 NFE 1/2/4/8 下报告回报、NFE、batch latency、推理延迟和峰值显存，并与相同 environment-step 预算的 Gaussian PPO 对比。

阶段 4 只有在 Flow 数学测试和可重复 benchmark 都通过后才完成。阶段 5 只有在官方 smoke、三 seed Unity 训练、独立评估、NFE 消融及 PPO 公平对比均留下可审计证据后才完成。
