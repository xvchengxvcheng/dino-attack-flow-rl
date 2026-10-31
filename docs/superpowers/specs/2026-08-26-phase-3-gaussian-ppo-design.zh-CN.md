# 阶段 3 Gaussian PPO 设计

## 目的

阶段 3 向 `flow_rl` 添加一个独立的 Gaussian PPO 基线。它必须通过 ML-Agents Low-Level API 训练现有的 `Builds/3DBall/3DBall.exe`，且不依赖 `mlagents-learn` 内部实现。其成果将建立供后续 Flow 算法复用的共享模型、rollout、优化、评估、checkpoint、日志和数据集接口。

只有在两个固定随机种子都完成至少 500,000 个 Agent 决策步的训练、两次运行均保持数值稳定、独立确定性评估的平均回报至少达到 90、收集了带版本的 `(observation, action)` 数据集，并记录了与官方 PPO 基线的对比证据后，本阶段才算完成。

## 范围

### 包含

- 使用 tanh 压缩的对角 Gaussian 连续动作 actor。
- 与 actor 使用相同编码器容量、但相互独立的标量 critic。
- 稳定的动作采样、确定性动作选择，以及动作空间边界处的 log-probability 计算。
- PPO 策略裁剪、裁剪 value loss、基础 Gaussian 熵正则化、advantage 归一化、近似 KL、clip fraction 和梯度裁剪。
- 使用线性 schedule 调整学习率、策略裁剪范围和熵系数的多 epoch 随机 minibatch 更新。
- 基于 `UnityEnvAdapter`、对 Agent ID 安全的 on-policy 数据收集，包括自然终止、interruption bootstrap、仅 terminal 批次、Agent 重排，以及同一 Agent ID 的 terminal/新 decision 重叠。
- 可原子写入、可恢复的 checkpoint，其中包含模型、优化器、归一化器、schedule 进度、配置、源码/构建身份和环境步元数据。
- 在与训练不同的 worker ID 上进行隔离的确定性评估。
- 为后续 ReinFlow 行为克隆收集带版本的原始观察和实际执行的有界动作。
- 先进行短程 smoke 训练，再分别用 seed 0 和 seed 1 运行 500,000 个决策步，并生成与现有官方 PPO 运行的对比报告。

### 不包含

- 修改 `mlagents-learn`、Unity 场景、prefab、package、ProjectSettings、现有 3DBall 构建或官方 PPO 产物。
- ONNX 导出；在后续阶段需要部署之前，它仍为可选项。
- 循环策略、离散或混合动作、视觉编码器、action chunk、Flow 模型，或修改官方 FPO/ReinFlow/PolicyFlow 代码。
- 超出固定双种子正确性协议的超参数搜索。

## 已验证上下文

- 当前 3DBall behavior 为 `3DBall?team=0`，包含一个形状为 `(batch, 8)` 的向量观察流和二维连续动作。
- 阶段 2 的单元测试基线为 `42 passed, 2 deselected`。
- 官方 PPO 基线使用两层、每层 128 个单元的隐藏层，batch size 为 64，buffer size 为 12,000，学习率为 `3e-4`，策略裁剪为 `0.2`，熵系数为 `1e-3`，`gamma=0.99`、`lambda=0.99`，训练 3 个 epoch。
- CodeGraph 确认，官方 ML-Agents 每次更新只对 advantage 归一化一次，每个 epoch 都会打乱完整 buffer，使用裁剪后的策略损失和 value loss，并对学习率、epsilon 和 beta 进行线性 schedule。
- 官方产物记录了 500,499 步，最终训练回报约为 100，wall-clock time 为 374.98 秒。

## 架构

实现被划分为职责集中的模型、算法、数据收集、编排和产物模块。PPO 专用数据绝不进入 `UnityEnvAdapter`；适配器仍只负责 LLAPI 顺序、事件映射、动作验证和生命周期。

### 模型

`flow_rl.models.gaussian` 负责 `TanhGaussianActor`。它展平每个已归一化的观察流，按照 tuple 顺序拼接这些展平后的观察，然后将结果输入一个两层、每层 128 单元、使用 SiLU 激活的 MLP。线性 mean head 与不依赖状态、可训练的 `log_std` 共同定义一个对角 Normal 分布。构造分布时将 `log_std` 裁剪到 `[-20, 2]`。

随机动作使用重参数化采样 `z = mean + std * epsilon` 和 `action = tanh(z)`。确定性动作使用 `tanh(mean)`。Log probability 等于各维 Normal log probability 之和减去 tanh Jacobian，并使用以下稳定恒等式计算：

```text
log(1 - tanh(z)^2) = 2 * (log(2) - z - softplus(-2z)).
```

重新评估已保存的有界动作时，先将其裁剪到 `[-1 + 1e-6, 1 - 1e-6]`，再执行 inverse tanh。熵正则化使用未压缩对角 Normal 的解析熵；这与官方基线在连续动作上的实用熵约定一致，同时保证策略 likelihood 对 tanh 修正完整无误。

`flow_rl.models.critic` 负责 `ValueCritic`，它使用一个独立的两层、每层 128 单元、采用 SiLU 激活的 MLP，以及一个标量线性输出。Actor 与 critic 不共享参数，这与官方基线中的 `shared_critic: false` 一致。

`flow_rl.models.policy` 负责 `GaussianActorCritic` 和不可变的 `PolicyOutput`。`PolicyOutput` 包含有界 `float32` 动作、标量 value、求和后的 log probability 和 tanh 前样本。这是通用动作选择边界；未来算法可以定义不同的辅助数据，而无需修改环境适配器。

### PPO 算法

`flow_rl.algorithms.ppo` 负责：

- `PPOBatch`：经过验证的 tensor batch，包含归一化观察、有界动作、旧 log probability、旧 value、advantage 和 return。
- `PPOMetrics`：包含 policy loss、value loss、entropy、total loss、近似 KL、clip fraction、actor 梯度范数、critic 梯度范数、学习率、裁剪范围和熵系数。
- 用于裁剪 policy loss 和裁剪 value loss 的纯函数。
- `PPOUpdater`：在完整 rollout 上仅归一化一次 advantage；每个 epoch 使用带种子的 Torch generator 打乱索引；遍历每个完整 minibatch 以及最后一个不完整 minibatch；拒绝非有限 tensor；裁剪 actor 与 critic 梯度范数；并使用一个同时包含两个网络参数的 Adam 优化器。

对于 ratio `r = exp(new_log_prob - old_log_prob)`，policy loss 是 `min(r*A, clamp(r, 1-epsilon, 1+epsilon)*A)` 的负均值。Value loss 是未裁剪平方误差与旧 value 被裁剪到 `old_value ± epsilon` 后产生的误差二者最大值均值的一半。Total loss 为：

```text
policy_loss + value_coefficient * value_loss
              - entropy_coefficient * entropy.
```

阶段 3 固定使用 `value_coefficient=0.5`，最大梯度范数为 `0.5`。在 500,000 个决策步内，学习率从 `3e-4` 线性衰减到 `1e-10`，策略裁剪范围从 `0.2` 线性衰减到 `0.1`，熵系数从 `1e-3` 线性衰减到 `1e-5`。所有 schedule 均由环境步进度推导，并在恢复训练时精确恢复。

### 对 Agent ID 安全的数据收集

`flow_rl.training.collector` 负责管理待处理的策略决策，并将 LLAPI 事件转换为 transition。待处理记录以稳定 Agent ID 为键，保存提交动作时实际使用的归一化观察、动作、旧 log probability 和旧 value。

对于每个返回的 `EnvStep`，数据收集按 terminal 行优先于 decision 行的顺序处理：

1. terminal 行关闭该 Agent 的旧待处理动作。自然终止使用 `next_value=0`；interruption 则在 terminal observation 上评估 critic，并使用该 value 进行 bootstrap。
2. 非 terminal 的 decision 行使用该行 reward 和新 observation 的 critic value，关闭已有的待处理动作。
3. 没有待处理动作的 decision 行是 reset 基线。它的 reward 不计入 transition，这与官方 `AgentProcessor` 行为一致。
4. 关闭旧 transition 后，策略按照当前 DecisionSteps 顺序选择动作，并在调用 `adapter.step()` 前保存新的待处理记录。

已完成的 transition 按 Agent ID 分组，并分别保持时间顺序。在每次更新边界，使用现有 `compute_gae` 独立计算每条 Agent 轨迹的 GAE。Rollout 截断使用已保存的 `next_value` 进行 bootstrap，但不会将 GAE trace 传播到下一个优化器 buffer。待处理动作在优化器更新之间保持有效，因此不会丢失或重复任何环境 decision。

Collector 会断言 shape、dtype、数值有限性、动作边界、待处理 Agent 身份和总样本数，并分别记录自然终止和 interruption。

### 观察归一化

现有 `ObservationNormalizer` 继续作为每个观察流运行统计量的唯一所有者。训练在动作选择前使用原始 DecisionSteps observation 更新统计量。Actor 实际使用的归一化 observation 会存入 PPO transition，防止后续归一化器更新改变旧策略输入。

评估和数据集收集会加载 checkpoint 中的归一化器并保持冻结。行为克隆数据集保留原始观察，使阶段 6 可以使用随附的 checkpoint 状态复现归一化过程。

### 训练编排

`flow_rl.training.trainer` 负责带类型的 `PPOTrainingConfig` 和训练循环。阶段 3 的正式配置为：

```yaml
total_environment_steps: 500000
rollout_size: 12000
batch_size: 64
epochs: 3
gamma: 0.99
gae_lambda: 0.99
learning_rate: 0.0003
final_learning_rate: 1.0e-10
clip_range: 0.2
final_clip_range: 0.1
entropy_coefficient: 0.001
final_entropy_coefficient: 1.0e-5
value_coefficient: 0.5
max_gradient_norm: 0.5
hidden_sizes: [128, 128]
checkpoint_interval: 100000
time_scale: 20
device: cuda
```

CLI 在启动 Unity 前拒绝非空运行目录。它会为 Python、NumPy、Torch CPU 和 Torch CUDA 设置随机种子。每次运行都会写入配置 YAML、episode CSV、TensorBoard 事件、更新指标 CSV、Unity Player 日志、周期性 checkpoint、最终 checkpoint 和机器可读 summary。

短程 smoke 运行至少使用 24,000 个 decision，使其至少执行一次完整 PPO 更新，然后验证参数变化、指标有限性、原子 checkpoint 重载和确定性评估连线。

正式训练按顺序执行，以避免 GPU 和端口争用：

- seed 0：训练 worker 80，评估 worker 90；
- seed 1：训练 worker 81，评估 worker 91。

每次正式运行使用 500,000 个 decision step。评估采用确定性动作，并在隔离 worker 上精确收集 100 个已完成 episode。当平均回报至少为 90，且所有报告的 loss、ratio、gradient、KL、runtime 和 throughput 均为有限值时，该 seed 才算通过。

### Checkpoint 与恢复

复用现有原子 checkpoint helper。模型 payload 包含完整 actor-critic 状态；优化器 payload 包含 Adam 状态。PPO checkpoint 必须包含归一化器状态。配置为完整解析后的训练配置。元数据至少包含：

- schema 版本；
- environment step 和 Unity step；
- 优化器更新次数；
- seed 和 worker ID；
- observation shape 和 action size；
- 源码身份和 Unity build SHA-256；
- 累计 wall-clock；
- Torch RNG、CUDA RNG 和 NumPy generator 状态。

恢复前会验证架构、observation/action shape、构建 hash、seed 和定义 schedule 的配置，验证通过后才恢复。Checkpoint 不匹配时会直接拒绝，而不是部分加载。

### 评估

`flow_rl.cli.evaluate_ppo` 在配置的设备上加载 checkpoint，冻结归一化器，将模型切换到 evaluation mode，并使用确定性动作调用共享 evaluator。评估使用与训练 worker 不同的 worker ID，并写入每 episode CSV 以及 `evaluation.json`；后者包含平均值、标准差、最小值、最大值、episode 数量、decision step、wall-clock、throughput 和策略参数量。

评估绝不更新模型、优化器、归一化器或 checkpoint 文件。

### ReinFlow 数据集

`flow_rl.data.demonstrations` 和 `flow_rl.cli.collect_ppo_dataset` 使用固定的 seed-0 最终 checkpoint 收集 100,000 个 decision 样本。策略采用随机采样并使用 checkpoint 中固定的数据集 seed，同时保存实际提交给 Unity 的精确有界动作。数据集是一个压缩 NPZ，包含：

- `agent_ids`，类型为 `int64`；
- 每个原始观察流分别对应一个 `float32` 数组 `observations_<index>`；
- 有界 `float32` 类型的 `actions`；
- 审计所需的 episode 边界标志。

相邻的 JSON 元数据文件记录 checkpoint 路径及 SHA-256、build 路径及 SHA-256、behavior name、训练 seed、数据集 seed、样本数、observation shape、action size、normalizer count、收集 wall-clock 和源码身份。两个文件都通过临时文件写入并原子替换。已存在的数据集目标会被拒绝。

## 错误处理与安全

- observation、action、log probability、value、advantage、return、loss、ratio、梯度范数或 metric 中出现非有限值时，立即中止运行并报告精确错误。
- 在所有失败路径上，通过上下文管理器或 `finally` 块关闭 Unity、logger、TensorBoard 和 dataset handle。
- 运行或数据集绝不覆盖已有的非空目标。
- 训练和评估 worker ID 必须不同，正式运行也使用各自独立的 ID。
- 在 smoke、训练、评估和数据集收集后检查端口和 `3DBall` 进程。
- 现有 Unity 资产、基线结果、参考仓库和冻结的 `.conda/mlagents-r23` 环境保持不变。

## 测试策略

所有生产行为均采用测试先行方式实现：在编写代码前，必须亲眼确认对应测试失败。

### 模型测试

- 对随机和确定性输出进行精确 shape 与 dtype 检查。
- 动作保持在 `[-1, 1]` 内；对于位于或距离两个边界不超过 `1e-6` 的动作，log probability 保持有限。
- 使用手工推导的标量 Normal/Jacobian 示例验证 log probability。
- 相同初始化 seed 和采样 seed 可复现动作与 log probability。
- Actor 与 critic 梯度均有限，且二者参数相互独立。

### PPO 测试

- 使用手工推导的正、负 advantage 情形验证裁剪 surrogate 会选择正确分支。
- 使用手工推导的 value target 验证裁剪 value loss。
- 常量 advantage 归一化后得到有限的零值。
- 带种子的 minibatch 更新会改变参数、报告有限指标、遵守梯度上限，并在强制 ratio 下产生非零 clip fraction。
- 拒绝无效 shape、dtype、NaN 和配置值。

### Collector 测试

- 合成的 Agent ID 重排仍保持 observation/action/log-prob/value 的归属关系。
- 自然终止禁用 bootstrap。
- Interruption 使用 terminal observation value 进行 bootstrap，并切断 GAE trace。
- 同批 terminal/新 decision 和延迟 reset 基线不会重复 reward 或 transition。
- Rollout 截断保留待处理动作，并生成精确预期数量的优化器样本。

### 编排与产物测试

- 一个小型 fake 环境完成数据收集、一次更新、日志写入和干净关闭，且不 mock 模型行为。
- Checkpoint 往返会精确恢复模型输出、优化器状态、归一化器、进度和 RNG 状态；配置不匹配时拒绝恢复。
- 数据集往返验证精确数据行、dtype、边界、元数据 hash 和拒绝覆盖行为。
- 评估保持归一化器冻结、具有确定性、遵守 episode 数量限制、隔离 worker ID，并在异常时关闭资源。

### 真实验证

1. 运行完整的非集成测试套件。
2. 运行现有 Unity 集成测试。
3. 运行 24,000 步 PPO smoke 训练并检查 checkpoint/评估。
4. 分别训练 seed 0 和 seed 1，各运行 500,000 个 decision。
5. 对每个最终 checkpoint 确定性评估 100 个 episode。
6. 收集包含 100,000 个样本的 seed-0 数据集。
7. 解析全部 CSV/JSON/NPZ 产物，并验证数量、dtype、有限值、动作边界、episode-index 连续性和 checkpoint hash。
8. 将外部 PPO 结果与不可变的官方基线产物进行比较。
9. 扫描 Unity 日志，并确认没有残留匹配进程或端口。

## 计划文件

### 新增生产文件

- `flow_rl/src/flow_rl/models/__init__.py`
- `flow_rl/src/flow_rl/models/gaussian.py`
- `flow_rl/src/flow_rl/models/critic.py`
- `flow_rl/src/flow_rl/models/policy.py`
- `flow_rl/src/flow_rl/algorithms/__init__.py`
- `flow_rl/src/flow_rl/algorithms/ppo.py`
- `flow_rl/src/flow_rl/training/__init__.py`
- `flow_rl/src/flow_rl/training/collector.py`
- `flow_rl/src/flow_rl/training/config.py`
- `flow_rl/src/flow_rl/training/trainer.py`
- `flow_rl/src/flow_rl/data/demonstrations.py`
- `flow_rl/src/flow_rl/cli/train_ppo.py`
- `flow_rl/src/flow_rl/cli/evaluate_ppo.py`
- `flow_rl/src/flow_rl/cli/collect_ppo_dataset.py`

### 新增测试

- `flow_rl/tests/models/test_gaussian.py`
- `flow_rl/tests/models/test_critic.py`
- `flow_rl/tests/models/test_policy.py`
- `flow_rl/tests/algorithms/test_ppo.py`
- `flow_rl/tests/training/test_collector.py`
- `flow_rl/tests/training/test_config.py`
- `flow_rl/tests/training/test_trainer.py`
- `flow_rl/tests/data/test_demonstrations.py`
- `flow_rl/tests/integration/test_ppo_training_smoke.py`

### 配置和证据

- `flow_rl/configs/phase3_ppo_3dball.yaml`
- `reports/phase3/README.md`
- `reports/phase3/ppo-smoke/`
- `reports/phase3/ppo-seed-0/`
- `reports/phase3/ppo-seed-1/`
- `reports/phase3/evaluation-seed-0/`
- `reports/phase3/evaluation-seed-1/`
- `reports/phase3/reinflow-dataset-seed-0/`
- `reports/phase3/comparison.json`
- `docs/PROJECT_PLAN.md`

## 验收标准

- 所有现有和新增非集成测试均通过。
- 真实 Unity smoke 和 PPO 集成测试均通过，且没有泄漏进程或端口。
- Seed 0 和 seed 1 均完成至少 500,000 个 decision step，优化指标有限，并生成可恢复的最终 checkpoint。
- 每个 seed 在隔离环境中的确定性 100-episode 平均回报至少为 90。
- Seed-0 数据集精确包含 100,000 个有效、有界的 `(observation, action)` 样本，并带有完整来源元数据。
- 对比报告记录外部 PPO 和官方 PPO 的环境步、wall-clock time、return、throughput、参数量和协议差异。
- `docs/PROJECT_PLAN.md` 只将有证据支持的复选框标记为完成，记录命令和产物路径，并在阶段 3 的所有退出标准通过前保持阶段 4 未开始。
