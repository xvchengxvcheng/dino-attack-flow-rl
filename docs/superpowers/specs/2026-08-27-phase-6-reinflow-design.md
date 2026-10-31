# 阶段 6：ReinFlow 设计

## 目标与来源

在阶段 2–5 已验证的 Unity LLAPI 基础设施上，使用阶段 3 固定数据集训练单步 Rectified Flow behavior-cloning 策略，再按本地只读 `ReinFlow/` 的 `release` commit `e722e151bed767f3ffef47527cf697f2358af55d` 移植带可学习逐 Flow-step 噪声的 PPO 在线微调。阶段退出要求是：Flow BC 的 3DBall 独立评估平均回报至少 80、目标至少 90；ReinFlow 微调不低于同一个 BC checkpoint；预训练与在线环境成本分开报告。

固定官方事实来源为：

- Hydra 入口 `ReinFlow/script/run.py`，通过 `_target_` 构造 `TrainReFlowAgent` 或 `TrainPPOFlowAgent`。
- BC 路径 `agent/pretrain/train_reflow_agent.py` 与 `model/flow/reflow.py`：`x_t=t*x_1+(1-t)*x_0`，目标速度 `x_1-x_0`，MSE flow matching。
- 微调路径 `agent/finetune/reinflow/train_ppo_flow_agent.py`、`model/flow/ft_ppo/ppoflow.py`、`model/flow/mlp_flow.py` 与 buffer：保存完整 Flow chain，以各 Euler 转移的 Normal likelihood 计算 chain log-prob，训练可学习噪声 head，使用 PPO clipped ratio、entropy-rate regularization、critic warmup 和独立 actor/critic optimizer。
- 已核对的官方 Gym 配置：`cfg/gym/pretrain/hopper-medium-v2/pre_reflow_mlp.yaml` 与 `cfg/gym/finetune/hopper-v2/ft_ppo_reflow_mlp.yaml`。正式 Hydra 命令与完整 Gym/MuJoCo/D4RL 依赖仅作为复现记录；Unity 移植不导入官方 trainer。

## 方案比较

### 采用：ReinFlow 专用 bounded Markov-chain policy

新增 ReinFlow policy、batch、collector 和 updater，但继续复用 `OnPolicyCollectorCore`、Agent-ID 事件语义、GAE、`ValueCritic`、RunLogger、checkpoint 原子写入和 Unity evaluator。BC 与在线微调共享 velocity network 和固定 observation normalizer。此方案保持官方概率语义，同时不把算法逻辑泄漏到 Unity adapter。

### 不采用：复用 FPO latent/tanh ratio

FPO ratio 来自相同噪声/时间下的 CFM loss difference；ReinFlow ratio 来自完整随机 Euler chain 的 transition log-prob。二者不可互换。FPO 在无界 latent 上训练并在边界 `tanh`，官方 ReinFlow 在归一化 bounded action 空间对中间均值和最终动作裁剪，因此必须保留独立实现。

### 不采用：直接嵌入官方 Hydra trainer

官方 trainer 假设 Gym/MuJoCo/D4RL、vector env、历史观测和 action chunk，且依赖版本与冻结项目环境不同。直接嵌入会破坏项目的 LLAPI 架构边界。官方 clone 仅用于源码核对和有界 synthetic smoke。

## 数据与 Flow BC

数据源固定为 `datasets/phase3/ppo-seed-0-dataset-seed-10000-100k/`：100,000 条 raw observation `(8,)` 和 bounded action `(2,)`，来源 PPO checkpoint、Unity build SHA-256、dataset seed 与 normalizer 已记录。BC 不重新收集、不选择最好数据源。

`FlowBCTrainingConfig` 使用固定 seed 打乱 90/10 train/validation split。训练时从 metadata 恢复 observation normalizer，输入保持 raw-on-disk、normalize-on-batch；action 直接作为 bounded `x_1`，`x_0~N(0,I)`，`t~Uniform[0,1)`，损失为 velocity MSE。验证使用固定 generator，确保同 checkpoint/seed 得到相同 loss。checkpoint 保存 actor、optimizer、normalizer、split seed、dataset/build/checkpoint hash、network/solver 配置、epoch、train/validation loss 和 RNG。

BC evaluator 从高斯初值使用 0→1 Euler 积分，在每一步把 mean 裁剪到 `[-1,1]`，不注入在线探索噪声；最终动作直接发送 Unity。相同 checkpoint、evaluation seed 和 NFE 必须复现动作序列。

## ReinFlow policy 与概率

`ReinFlowActorCritic` 组合：

- 阶段 4 的 `ConditionalVelocityMLP`，从 BC checkpoint 加载；
- `LearnableFlowNoise`，复用 actor 的 state embedding，并使用 detach 的 time embedding，输出每个动作维度的 log-variance；先经 `tanh`，再映射到 `[log(min_std^2), log(max_std^2)]`；
- 现有 `ValueCritic`。

第一版严格保持单步动作 `horizon_steps=1`，这是项目阶段顺序对官方 action-chunk 配置的有意差异。NFE 默认 4。

训练采样从 `x_0~N(0,I)` 开始。每一步计算 `mean=clamp(x_k+v_k/K,-1,1)` 和 `std_k(s,t)`，从 `Normal(mean,std)` 采样并限制在 `mean±3*std`；最后一步再裁剪到 `[-1,1]`。collector 保存完整 `(B,K+1,A)` chain、rollout 时旧 normalized log-prob、value 与实际动作。评估仍随机采样初值，但不注入 transition noise，匹配官方 `eval_mode`。

chain log-prob 包含初始标准 Normal 和 K 个 transition likelihood；按官方默认先除以参与的 log-prob step 数，再除以动作维度。entropy 使用同一 Markov chain 的 joint entropy，做相同归一化得到 entropy rate。更新前把 old/new normalized log-prob 裁剪到配置区间（官方 Gym 为 `[-1,1]`），计算 `ratio=exp(new-old)`、approximate KL 和 PPO clipped surrogate。advantage 做有符号 batch normalization。

critic 每个 minibatch 更新；actor 仅在 `environment_steps >= critic_warmup_environment_steps` 后更新。actor optimizer 覆盖 velocity actor 与 noise head；critic optimizer 独立。总 actor loss 为 clipped policy loss 加 `entropy_coefficient * (-entropy_rate.mean())`。所有非有限 loss、ratio、log-prob、noise std、KL 或 gradient 在 optimizer step 前失败。

## Normalizer、checkpoint 与恢复

官方微调使用预训练数据 normalization 文件，因此 Unity ReinFlow 在线阶段冻结 BC checkpoint 中的 observation normalizer，不随在线 rollout 更新。这样 BC 与微调策略输入分布一致，并把在线收益归因于策略更新，而不是 normalizer 漂移。

ReinFlow checkpoint schema 包含 BC source hash、官方 commit、Unity build hash、actor/noise/critic、双 optimizer、normalizer、NFE、noise bounds、log-prob normalization/clamp、entropy coefficient、warmup progress、环境步、RNG 和 solver 配置。恢复与阶段 5 相同：恢复 Python/Torch/CUDA/generator 状态，但重新启动 Unity，不声称恢复物理状态。

## 实验协议

1. 官方 synthetic smoke：在隔离环境中导入固定 clone 的 `FlowMLP`、`NoisyFlowMLP`/`PPOFlow`，验证 noise bounds、完整 chain、首 minibatch ratio=1、entropy/log-prob/gradient 有限。若 Windows 无法安装完整 Gym/MuJoCo 栈，只运行不依赖环境的官方模型级最小复现，并明确记录该限制。
2. BC：固定 100k 数据集，记录 epoch/batch/wall-clock 与 train/validation loss；对最终 checkpoint 独立评估 100 episodes，平均回报至少 80。
3. 在线 smoke：24k decisions，覆盖 critic warmup 边界、actor/noise/critic 参数变化、checkpoint 与恢复。
4. 最小消融：同一 BC checkpoint 对比低噪声/无 entropy 与官方 Hopper 风格噪声/entropy 配置，使用相同短预算和 evaluation seed；据证据选择正式配置。
5. 正式微调：500k decisions，独立 100-episode 评估；结果不得低于 BC checkpoint。报告 BC 数据与 GPU 训练成本、在线 environment steps 和 wall-clock，不能合并。

## 测试与错误处理

- BC：dataset source/hash、split 复现、shape/dtype/bounds、手算 CondOT loss、train/validation loss、checkpoint round-trip。
- policy：noise bounds、每步 chain shape、固定 generator、eval 无 transition noise、动作边界、初始/transition log-prob 手算、entropy normalization。
- updater：ratio=1 初始不变量、正/零/负 advantage、clip/approx KL、warmup 仅 critic 更新、warmup 后 actor/noise 更新、finite gradient。
- collector：Agent ID 重排、terminal、truncated、terminal+decision overlap、空 decision batch和 chain 所有权。
- integration：真实 3DBall smoke、独立评估、resume、新/旧 worker 端口释放、Unity 日志扫描。

不修改 Unity、`ml-agents/`、三个官方算法仓库、`.conda/mlagents-r23`、既有 build 或 PPO/FPO 产物；不进入阶段 8–10。

