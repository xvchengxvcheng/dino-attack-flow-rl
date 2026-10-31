# 阶段 7 PolicyFlow 详细实施计划

> 执行技能：`superpowers:executing-plans`、`superpowers:test-driven-development`、`superpowers:systematic-debugging`、`superpowers:verification-before-completion`。本计划已由 02:00/08:00 自动任务授权，写完直接执行，不等待再次批准。

**目标：** 以固定本地 PolicyFlow clone 为唯一算法事实来源，取得官方 MultiGoal 多峰证据，并在共用 Unity 3DBall 基础设施上完成单步 PolicyFlow 三种子训练、评估和 NFE 消融。

## Task 0：冻结事实与实施前证据

- [x] 核对 `PolicyFlow` commit、branch、dirty 状态和 `.codegraph/`。
- [x] 用 CodeGraph 读取 `ContinuousNormalizingFlow`、`PolicyFlow.update`、`MultiGoalRunner` 调用链。
- [x] 把 additive delta-velocity ratio、Brownian 公式、snapshot refresh、midpoint NFE 口径写入规格。
- [x] 记录实施前全套 pytest、`pip check` 和官方依赖清单。

## Task 1：隔离官方 MultiGoal 复现

- [x] 创建 `.conda/policyflow-official`，只安装本地官方包和 MultiGoal 所需依赖；保存 `pip freeze`。
- [x] 为固定 seed、迭代数、输出目录和 checkpoint 提供不修改官方源码的 wrapper。
- [x] 先运行小规模 CUDA smoke，验证 rollout/update/checkpoint 数值有限。
- [x] 执行初始化对照与正式预算；用严格角度/幅值或轨迹成功判据评估多峰，而不是把每个非零动作强制归到最近方向。
- [x] 写 `reports/phase7/official-policyflow/{environment.json,run.json,mode-coverage.json,validation.md}`；正式门槛必须显著优于初始化对照并至少覆盖 5/6 严格模式。

## Task 2：PolicyFlow model（TDD）

- [x] RED：新增 `tests/models/test_policyflow_policy.py`，覆盖 midpoint 手算、实际 NFE、动作 shape/bounds、variance、snapshot 和 seed。
- [x] GREEN：实现 `models/policyflow_policy.py`。
- [x] 验证：`.conda/flow-rl/python.exe -m pytest flow_rl/tests/models/test_policyflow_policy.py -q`。

## Task 3：PolicyFlow 数学与 updater（TDD）

- [x] RED：新增 `tests/algorithms/test_policyflow.py`，手算 delta velocity、log-prob ratio、正负 advantage clip、Gaussian KL、Brownian loss、finite gradient、snapshot refresh。
- [x] GREEN：实现 `algorithms/policyflow.py`，严格复现固定 clone公式。
- [x] 验证两份同初值模型/批次/generator 产生完全相同指标和参数。

## Task 4：collector（TDD）

- [x] RED：新增 `tests/training/test_policyflow_collector.py`，覆盖 ID ownership/reorder、terminal-only、truncated bootstrap、空 Decision 和 frozen normalizer。
- [x] GREEN：实现 `training/policyflow_collector.py`，复用 `OnPolicyCollectorCore`。
- [x] 与 PPO/FPO/ReinFlow collector 回归一起运行。

## Task 5：配置、trainer、checkpoint/resume（TDD）

- [x] RED：补充 update 后 pending action、显式 restart-boundary/in-flight 元数据、固定 source commit、solver 一致性和 BC normalizer provenance 回归测试。
- [x] GREEN：修复 trainer/checkpoint/config，保证每条 transition 保存行为快照速度网格，并明确可恢复边界语义。
- [x] 添加 smoke、三 seed 和 NFE ablation 配置；不修改冻结基线环境或 Unity 资产。

## Task 6：evaluator 与 smoke（TDD）

- [x] RED/GREEN：实现 `evaluation/policyflow.py`、`cli/evaluate_policyflow.py` 和固定 seed 测试。
- [x] 运行真实 Unity 24k smoke；确认至少一次 update、12 Agent ID 排空、所有指标有限、日志无 Error/Exception/NaN/Infinity。
- [x] 从中间 checkpoint 在新目录恢复，验证 CUDA RNG 与 snapshot 正确；检查点明确为 Unity 重启边界并记录未完成决策。

## Task 7：NFE 消融与三种子正式训练

- [x] 在相同初始化、seed、算法配置、环境预算和 evaluation seed 下运行 actual velocity NFE `2/4/8` 消融，分别评估 100 episodes。
- [x] 选择达到回报/稳定性门槛且成本合理的 NFE，先写决策记录再更改正式配置。
- [x] 运行 seed 0/1/2 各 500k decisions，checkpoint 每 100k；失败只从最新有效 checkpoint 新目录恢复。
- [x] 每 seed 独立固定 seed 评估 100 episodes；三 seed 均值必须至少 90。

## Task 8：最强验证与交付

- [x] 修正后设置 `FLOW_RL_UNITY_BUILD` 运行全套 pytest；运行 `pip check`、`compileall`、checkpoint/CSV/JSON解析、Unity 日志、进程/端口和 `git diff --check`。
- [x] 重写 `reports/phase7/validation.md`、`comparison.json`，只使用修正后的严格官方证据和受控 NFE 数据。
- [x] 更新 `docs/PROJECT_PLAN.md` 阶段 7 复选框、状态、退出标准、证据、产物、阻塞项和决策记录；不触碰阶段 8–10。

## 退出与停止规则

只有 Task 1 的正式多峰证据和 Task 2–8 全部通过，阶段 7 才标记“已完成”。未运行的测试不得写成通过，训练曲线不得替代独立评估。若本次自动窗口结束时仍有长训练，保留 checkpoint/日志并把精确续跑命令写入计划和验证报告。
