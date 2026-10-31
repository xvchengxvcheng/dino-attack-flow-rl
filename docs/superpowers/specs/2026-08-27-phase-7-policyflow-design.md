# 阶段 7 PolicyFlow 设计规格

**版本：** 2026-08-27 08:00 自动续跑版  
**权威来源：** 本地只读 `PolicyFlow/`，`main` @ `7f304b96e93b2804bdcc943710db885adddd06b8`  
**范围：** 官方 MultiGoal 复现、单步 PolicyFlow 数学移植、共用 Unity 3DBall 训练/评估、NFE 消融；不进入阶段 8–10。

## 1. 官方语义

rollout 从标准高斯采样 `x0`，用 rectified-flow ODE 得到先验动作 `x1`，再采样扰动 `delta_action ~ Normal(0, sigma)` 并执行 `x1 + delta_action`。rollout 必须保存 `x0`、`x1`、扰动、旧标准差和旧扰动 log-prob。

更新时在离散时间网格（端点和 midpoint）随机取 `t`，构造

`x_t = (1-t) x0 + t x1`。

当前和冻结快照速度场的差

`delta_v = v_theta(x_t,t,s) - v_last(x_t,t,s)`

是新扰动分布 `Normal(delta_v, sigma_theta)` 的均值。importance ratio 为新扰动 log-prob 与 rollout 旧 log-prob 之差的指数；policy loss 使用 PPO clipped surrogate。近似 KL 采用当前与旧对角高斯的解析 KL。每次完整更新完成后，才把当前 actor 复制到 frozen snapshot。

rectified-flow Brownian implicit entropy regularizer 完全按固定 clone：

`MSE((1-t) v_theta, x_t - t v_last)`。

第一版保持官方 learnable per-action variance、ratio clip、value clip 和 gradient clip。动作送入 Unity 前执行 `tanh`，这是 Unity 有界动作接口所需的明确偏差；ratio 仍定义在官方 additive perturbation 空间，不改写为完整 CNF likelihood。

## 2. 采样与 NFE 口径

官方 MultiGoal 使用 10 个 midpoint ODE solver steps，每步调用速度场两次，即实际 velocity NFE=20。Unity 实现提供 midpoint sampler，配置字段同时记录 `solver_steps` 和 `velocity_nfe=2*solver_steps`。NFE 消融报告实际 velocity NFE `2/4/8`（对应 solver steps `1/2/4`）；不把 solver steps 误报为 NFE。阶段 4 的 Euler NFE 口径保留，不静默覆盖。

## 3. 模块边界

- `models/policyflow_policy.py`：当前 actor、冻结 snapshot、midpoint sampling、learnable variance、critic。
- `algorithms/policyflow.py`：flow variation、Brownian loss、Gaussian KL、clipped surrogate、minibatch updater。
- `training/policyflow_collector.py`：复用 `OnPolicyCollectorCore`，保持 Agent ID/terminal/truncated 语义，保存 PolicyFlow 专用辅助量。
- `training/policyflow_config.py` 与 `policyflow_trainer.py`：严格配置、checkpoint/resume、快照刷新、有限性日志。
- `evaluation/policyflow.py`：固定 seed，保留初始流噪声；评估默认不添加 delta-action 扰动，以测量 flow prior 的控制质量。

normalizer、GAE、critic、run logger、liveness、Unity adapter 和 evaluator 协议均复用已验证基础设施。官方仓库保持只读。

## 4. 测试与验收

数学单测必须手算验证 `delta_v`、ratio、clip 正负 advantage、解析 KL、Brownian loss、variance bounds、snapshot refresh、固定 generator 重现、shape/dtype/finite/gradient。collector 回归覆盖 Agent ID 重排、terminal-only、truncated bootstrap 和空 Decision batch。checkpoint 覆盖 schema、source commit、current/snapshot/variance、optimizer、normalizer及 CPU/CUDA RNG 恢复。

官方 MultiGoal 使用隔离环境运行固定 seed smoke/训练并在原点采样首动作；按 6 个目标方向分配动作模式，报告覆盖目标数、每模式计数和熵，不能只报告平均 reward。若完整 40,000 iterations 在本次窗口不具成本可行性，必须保存可恢复 checkpoint、实际迭代数和“未满足正式复现”状态，不能冒充通过。

Unity 正式验收：至少三种子达到 3DBall 100-episode 平均回报 90；训练 CSV 中 ratio、KL、Brownian regularizer、velocity norm 和 gradient norm全程有限；完成实际 velocity NFE 消融，并报告环境步、wall-clock、吞吐、延迟和参数量。

## 5. 失败恢复

每个运行目录一次性写入；按间隔保存可恢复 checkpoint。恢复精确还原模型、snapshot、variance、optimizers、normalizer、调度进度与 Python/NumPy/Torch RNG，但重新启动 Unity，不声称恢复物理状态。瞬态端口或 player 失败只从最新有效 checkpoint 在新目录恢复；数学或有限性失败先按 systematic debugging 隔离根因，不跳过断言。
