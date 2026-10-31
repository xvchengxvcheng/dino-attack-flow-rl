# Dino Attack Gaussian PPO Baseline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在冻结的双地图 `dino_attack_structured_set_v2` 环境上完成可恢复的四环境异步 Gaussian PPO 训练、评估与 checkpoint 导出，并为游戏内 AI Strategy 提供首个真实训练模型。

**Architecture:** 保留现有 `PPOUpdater`、Set Transformer、`AsyncUnityVectorEnv`、`VersionedCollector` 和六流协议，不修改 Unity 场景。把已经通过一次更新验证的 mixed-map smoke 循环抽成正式 PPO trainer：每个 policy version 精确收集一个 rollout，排空旧版本动作后同步更新，再开始下一版本；checkpoint 只在完整 update 边界保存。先执行 seed 0 学习性检查，只有回报/胜率相对未训练初始化出现可重复上升后，才冻结正式三种子预算。

**Tech Stack:** Python 3.10、PyTorch 2.2、ML-Agents Low-Level API、Unity headless Player、pytest、ONNX exporter、Unity AI Inference 2.6.1。

**Spec:** `docs/superpowers/plans/2026-08-28-phase-8-dino-attack.md` 的 `TASK-8.12`；`docs/superpowers/specs/2026-08-29-dino-attack-headless-parallel-training-design.md`；`docs/superpowers/specs/2026-08-30-dino-attack-structured-set-observation-action-design.md`。

**2026-08-31 最新正式规格覆盖：** 用户在 8 环境参数更新诊断通过后要求正式训练，随后依次把规格改为 12 环境/`524,288` transitions，最终改为 16 环境/每 seed `1,048,576` transitions。Task 4 固定 seed 0/1/2、独立 worker base/run directory；每次 PPO update 的 rollout target 仍为 `8,192`，优化 minibatch `256`、`4` epochs。旧四/八/十二环境实现、配置与 run 仅保留为历史证据。该授权覆盖此前学习门触发的停止执行决定，但不预先判定模型 eligible，也不自动启用 AI Strategy。

**2026-09-01 同步采样覆盖：** 异步 seed 0/1 已各完成 `1,050,435` transitions 与 128 updates。用户取消原 seed 2，并要求用相同随机种子 `1` 在全新目录新增严格同步采样运行：16 环境每轮统一提交动作，必须等本轮 16 个结果全部返回后才提交下一轮；预算、rollout `8,192`、minibatch `256`、4 epochs、build 与协议不变。异步和同步运行分组报告，不合并为同协议三种子统计。

同步首启在地图预分类阶段发现旧停滞计数按单个返回事件累计，失败目录保留；16环境延迟分类回归复现后改为按完整同步轮计数，相关回归 `39 passed`。正式同步运行改用唯一 `formal-16env-seed1-sync-1048576-20260901-a2` 目录。

随后用户取消 `-a2`：源码复核确认同步屏障仍在16环境循环内逐次 `_decide()`，每次独立调用 `policy.act()`，并非一次 batch size 16 GPU 推理。已精确停止1个训练器和16个Player、残留0，并暂停每小时自动续训。现有同步实现与运行目录仅作诊断证据；Task 4 不得据此声称完成同步采样或批量推理，恢复前需另行设计/TDD真正的跨环境 observation batch、单次 `policy.act()` 与输出拆分。

用户随后授权修改：新增严格 `decide_batch` 边界与生产 `DinoBatchedPolicyDecider`，同一同步屏障轮的六流 observation 按 Agent 维拼接并只执行一次 `policy.act()`，再按环境/Agent 数量拆分 actions、values、log-probs，保持 environment/process-generation/policy-version provenance。同步模式缺少 batch callback 直接拒绝启动；空 Agent batch 不调用 policy。聚焦训练/配置/resume 回归 `42 passed`，全包 `303 passed, 2 skipped, 1 failed`（唯一失败为无关的 v1 fixture 与 v2 loader 不匹配）。正式训练和每小时自动化仍暂停；检测到15个归属不可读的既有 Unity 进程，因此本轮没有启动真实16环境 smoke 或新正式目录。

异步路径随后升级为动态 GPU 微批：每次先获取 ready 环境，首个返回后用最长 `2 ms` 的有界聚合窗口补收，最多把16个环境合并为一次 `policy.act()`；输出按原请求的环境、Agent 数量及 generation/version provenance 拆分，各 Unity Player 无需等待最慢环境即可继续推进。rollout 达标后停止重提交并排空在途动作，更新仍严格发生在完整 policy-version 边界。旧配置缺省 `inference_batch_size=1`、`inference_batch_wait_seconds=0`，不改变历史结果。

真实16环境 smoke 使用 `phase8_dino_ppo_microbatch_16env_smoke.yaml` 完成 `4,101` transitions、16/16 worker、1次有限更新；500次策略前向的平均环境微批 `8.202`、最大 `16`，吞吐 `78.8845 steps/s`，`319/321` 参数张量变化，Player与端口清理均为0。产物位于 `reports/phase8/ppo-baseline/microbatch16-smoke-seed1-4096-20260901/`。正式配置 `phase8_dino_ppo_formal_16env_seed1_microbatch.yaml` 已创建但未启动，继续遵守用户取消正式训练后的暂停状态。

后续24环境速度对照已完成：两种模式统一 seed 1、target/rollout `24,576`、minibatch `512`、5 epochs、推理批上限24。同步为 `157.457 steps/s`、`156.015s`、平均批 `23.504`；异步微批为 `165.041 steps/s`、`148.901s`、平均批 `12.044`，即吞吐提高 `4.816%`、wall-clock减少 `7.114s/4.560%`。两轮均24/24 worker、245个优化minibatch、有限更新与清理通过。报告见 `reports/phase8/ppo-baseline/benchmark24-sync-vs-async-microbatch-20260901/`；单次配对不构成统计方差或eligible checkpoint证据。

用户随后明确采用建议参数恢复正式训练。新正式组为24环境异步动态微批、seed 0/1、每seed目标`1,048,576`、rollout`8,192`、minibatch`512`、5 epochs、batch上限24、2ms聚合窗、checkpoint间隔`131,072`；seed2继续取消。对应配置为`phase8_dino_ppo_formal_24env_seed{0,1}_async_microbatch.yaml`，使用唯一目录严格串行。该组与历史16环境结果分组报告，且两种子不能冒充原计划的三种子正式统计。

启动前用户改为“暂时不启动”：配置继续保留，但两个正式run目录未创建、没有训练进程。应用内接口确认原每小时自动任务已不存在，不能自动触发；后续必须由用户再次明确授权，若仍需每小时监控则显式新建自动任务。

**2026-09-01 seed 0 评估/观察模型更新：** 用户再次明确禁止启动 PPO seed 2。异步 seed 0 final checkpoint 使用冻结 build、evaluation seed `101`、每图20局确定性评估，平均回报 `-0.4125`、`2/40` 胜（`5%`）、无截断；map1 为 `0.175` / `2/20`，map2 为 `-1.0` / `0/20`。模型以 `ppo-formal-seed0-1050435-observation-only` 导出为 ONNX 并接入 AI Strategy，旧40,988-step观察模型不覆盖。Python导出回归 `4 passed`，Unity默认配置 EditMode与同地图/seed接管PlayMode均完成预期 RED→GREEN `1/1`。该模型不满足 eligible 门；FPO 在 seed 范围明确前不启动。

## Global Constraints

- 严格保留全部既有修改、场景、Build、结果目录和 checkpoint；新运行使用唯一目录，非空目录必须 fail closed。
- 正式协议固定为 `dino_attack_structured_set_v2` 六输入、4D 单步动作、Set Transformer、`gamma=0.995`，不增加 map one-hot。
- 正式历史 seed 0/1 使用16环境异步采样；新增 seed 1 对照使用16环境严格同步采样；两者均同步更新且一次 update 中不得混入不同 policy version。
- 双地图由 Unity 的确定性 selector 选择，训练器必须传递 `--dino-training`、base seed、environment index 和 process generation。
- 只运行必要验证：配置/版本边界单元测试、一次很短的双地图 resume smoke、seed 0 学习性检查；不重复已通过的场景与美术验收。
- 不要求 Git 提交、暂存或提交切片。

---

### Task 1: 可恢复的并行 PPO 训练核心

**Files:**
- Create: `flow_rl/src/flow_rl/training/dino_parallel_ppo.py`
- Create: `flow_rl/tests/training/test_dino_parallel_ppo.py`
- Reuse: `flow_rl/scripts/dino_v2_single_update_smoke.py`

**Interfaces:**
- Consumes: `AsyncUnityVectorEnv`、`VersionedCollector`、`update_versioned_ppo`、`PPOUpdater`、`DinoProtocol`。
- Produces: `DinoParallelPPOTrainer.train() -> DinoParallelTrainingSummary`；每个 update 的 `policy_version`、transition 数、episode/map 分解和有限数值指标。

- [x] **Step 1: Write failing version-boundary tests**

  构造两个小 rollout，断言 update 结束前排空所有旧动作、下一 rollout 使用递增后的 policy version，并且四个 environment 都贡献 transition。

- [x] **Step 2: Run the focused test and confirm RED**

  Run: `.conda/flow-rl/python.exe -m pytest flow_rl/tests/training/test_dino_parallel_ppo.py -q`

- [x] **Step 3: Implement the minimal repeated-update loop**

  将 smoke 已验证的 adapter factory、地图分类、动作生成和 bootstrap 逻辑复用到正式 trainer；每轮执行 `begin(version) -> collect/drain -> seal_and_bootstrap -> update -> version += 1`，并在异常时关闭全部 Player。

- [x] **Step 4: Run the focused test and confirm GREEN**

  Run: `.conda/flow-rl/python.exe -m pytest flow_rl/tests/training/test_dino_parallel_ppo.py -q`

### Task 2: 配置、checkpoint/resume 与 CLI

**Files:**
- Create: `flow_rl/src/flow_rl/training/dino_parallel_ppo_config.py`
- Create: `flow_rl/src/flow_rl/cli/train_dino_parallel_ppo.py`
- Create: `flow_rl/tests/training/test_dino_parallel_ppo_config.py`
- Create: `flow_rl/tests/integration/test_dino_parallel_ppo_resume.py`
- Create: `flow_rl/configs/phase8_dino_ppo_learning_seed0.yaml`

**Interfaces:**
- Consumes: Task 1 trainer、现有 structured checkpoint schema、冻结 dual-map Player。
- Produces: `DinoParallelPPOConfig.from_yaml(Path)`；`python -m flow_rl.cli.train_dino_parallel_ppo --config <path>`；完整 update 边界的 `step-*.pt` 与 `final.pt`。

- [x] **Step 1: Write failing config and resume tests**

  覆盖四环境 worker/seed 唯一性、非空 run directory 拒绝、v2/Set Transformer/gamma 契约、checkpoint policy version/optimizer/RNG/normalizer 恢复，以及 resume 目标必须大于 checkpoint steps。

- [x] **Step 2: Run focused tests and confirm RED**

  Run: `.conda/flow-rl/python.exe -m pytest flow_rl/tests/training/test_dino_parallel_ppo_config.py flow_rl/tests/integration/test_dino_parallel_ppo_resume.py -q`

- [x] **Step 3: Implement config, checkpoint, resume and CLI**

  默认四环境、rollout `4096`、batch `256`、`epochs=4`、`gamma=0.995`、Set Transformer `d_model=64/heads=4/layers=2/output=256`、CUDA、`time_scale=20`。CLI 仅创建新的 run directory；resume 写入新的目录，不覆盖源 run。

- [x] **Step 4: Run focused tests and confirm GREEN**

  Run: `.conda/flow-rl/python.exe -m pytest flow_rl/tests/training/test_dino_parallel_ppo_config.py flow_rl/tests/integration/test_dino_parallel_ppo_resume.py -q`

### Task 3: 学习性检查、独立评估与模型导出

**Files:**
- Create: `flow_rl/src/flow_rl/evaluation/dino_parallel_ppo.py`
- Create: `flow_rl/src/flow_rl/cli/evaluate_dino_parallel_ppo.py`
- Create: `flow_rl/tests/evaluation/test_dino_parallel_ppo_evaluation.py`
- Create: `reports/phase8/ppo-baseline/learning-seed0/`
- Modify: `docs/PROJECT_PLAN.md`

**Interfaces:**
- Consumes: Task 2 `final.pt`、固定 evaluation seeds、现有 `export_trained_checkpoint(...)`。
- Produces: `evaluation.json`（全局及 map1/map2 胜率、回报、肉、episode 时长、环境步、wall-clock）；ONNX、manifest 与 provenance；游戏内默认模型候选。

- [x] **Step 1: Write failing deterministic-evaluation tests**

  断言 deterministic action、normalizer frozen、两图结果分组、自然终止/截断分离、相同 checkpoint/build/protocol provenance。

- [x] **Step 2: Run focused tests and confirm RED**

  Run: `.conda/flow-rl/python.exe -m pytest flow_rl/tests/evaluation/test_dino_parallel_ppo_evaluation.py -q`

- [x] **Step 3: Implement evaluator and run seed 0 learning check**

  先记录未训练初始化评估，再训练 `40,960` transitions（10 updates），在 `20,480` 与 `40,960` 的完整 update 边界保存；分别评估初始化、中点和 final。若趋势方向正确但证据不足，只从 checkpoint 续跑到 `65,536`；若回报/胜率未高于初始化或出现非有限值，则停止，不启动三种子正式训练。

  结果：完整异步边界实际为 `20,493/40,988`；三点固定评估均为 `0/40` 胜，平均回报依次为 `-2.2385/-2.7050/-2.4340`。最终未高于初始化，按规则停止，不续训和不启动三种子。

- [ ] **Step 4: Export the first eligible checkpoint**

  使用生产 `export_trained_checkpoint(...)` 生成 `trained_checkpoint` manifest；随后执行两地图同 seed、25-step 推理动作 hash 重赛门，成功后再把模型复制/配置为游戏内默认 AI Strategy。

  当前阻塞：seed 0 学习门未通过，没有 eligible checkpoint；本步骤保持未完成。用户后续明确批准把 final checkpoint 单独导出为 `ppo-seed0-40988-observation-only` 供 Unity 观察当前策略，不能据此称为 eligible checkpoint 或完成 `TASK-8.12`。

- [x] **Step 5: Record evidence**

  在 `docs/PROJECT_PLAN.md` 记录命令、环境步、updates、两图 episode 分布、胜率/回报趋势、checkpoint/ONNX/build SHA-256、运行时间和是否允许进入正式三种子。

  学习门证据：`reports/phase8/ppo-baseline/learning-seed0/README.md`。观察用途例外导出及 Unity 验证见 `reports/phase8/ppo-baseline/observation-policy/README.md`；该 ONNX 不满足本步骤的 eligible checkpoint 条件。

### Task 4: 冻结预算并运行历史16环境组与正式24环境异步微批组

**Files:**
- Create: `flow_rl/configs/phase8_dino_ppo_seed0.yaml`
- Create: `flow_rl/configs/phase8_dino_ppo_seed1.yaml`
- Create: `flow_rl/configs/phase8_dino_ppo_seed2.yaml`
- Create: `reports/phase8/ppo-baseline/formal-seed-{0,1,2}/`
- Modify: `docs/PROJECT_PLAN.md`

**Interfaces:**
- Consumes: Task 3 学习性证据与冻结预算。
- Produces: 三种子 checkpoint、统一 evaluation seed 集、成本报告和 Task 8.12 退出证据。

- [x] **Step 1: Freeze the smallest justified formal budget**

  根据 seed 0 学习曲线首次稳定提升所需步数，将正式预算冻结为不小于该点且三 seed 完全一致的 update 整数倍；在决策记录中写明换算依据。

  当前结果：原学习曲线不存在稳定提升点，无法按旧规则推导预算；用户最终直接冻结为 16 环境、每 seed `1,048,576` transitions。配置/训练核心已按 TDD 扩展为仅允许 4/8/12/16，16 环境聚焦 `5 passed`、相关回归 `42 passed`。

- [ ] **Step 2: Run the non-overwriting asynchronous and dynamic-microbatch directories**

  异步 seed 0/1 已完成；同步 seed 1 使用独立 worker base `680`、run directory 和 checkpoint。失败 run 原样保留并报告，不覆盖或挑 seed。原 seed 2 按用户最新决定不运行。

  当前状态：严格同步与异步动态微批均已实现，24环境公平速度对照也已通过。正式参数已固定在`phase8_dino_ppo_formal_24env_seed{0,1}_async_microbatch.yaml`，但用户要求暂时不启动；两个run目录保持不存在，原heartbeat已不存在。未来获明确授权后seed0/1严格串行，seed2仍取消；旧16环境、`-a1/-a2`与benchmark目录全部保留并分组报告。

- [ ] **Step 3: Evaluate and summarize**

  使用完全相同的 build、协议和 evaluation seed 集，报告每 seed 原始值、mean/std、胜率区间、环境步、wall-clock、吞吐、参数量、推理延迟和显存。

- [ ] **Step 4: Complete Task 8.12 and unblock later algorithms**

  只有三 seed 和统一评估均完成后勾选 Task 8.12；至少一个兼容 checkpoint 通过 Task 6 推理门后可先完成地图2 AI Strategy，但不得提前宣称 Task 8.12 整体完成。
