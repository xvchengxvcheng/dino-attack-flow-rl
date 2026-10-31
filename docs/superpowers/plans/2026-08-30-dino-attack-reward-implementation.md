# Dino Attack 正式训练奖励实施计划

**日期：** 2026-08-30  
**阶段：** `TASK-8.11` 正式奖励门 / `TASK-8.11-MAP2` 前置依赖  
**批准规格：** `docs/superpowers/specs/2026-08-30-dino-attack-reward-design.md`

## 目标与边界

把已批准奖励接入正式 `DinoTrainingAgent`：胜利 `10 + 0.01 × final_meat`、失败 `-1`、守卫 `+0.02`、墙 `+0.05`、非法部署 `-0.02`、Wait `0`；移除合法部署成本和逐决策时间惩罚，并把正式 Dino 配置统一为 `gamma=0.995`。

本任务不改变 Human 玩法、房屋返肉、战斗数值、50 秒终止、观测/动作 shape、场景、Packages、Player build 或地图布局。只运行规格列出的聚焦验证，不恢复全量测试或长 smoke。

## Task 1：先冻结可测试的奖励规则

**Files:**
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Training/Core/DinoTrainingRewardRules.cs`
- Create: `dino-attack/Assets/Tests/Editor/DinoTrainingRewardRulesTests.cs`

- [x] 先写 RED：胜利肉余额 `0/100/更大值`、失败有肉仍为 `-1`、interrupted 为 `0`、守卫/墙去重、非法/Wait/合法部署语义。
- [x] 实现无 Unity 场景依赖的规则/episode ledger；终局和实体事件都只能结算一次，总奖励可作为后续玩家/AI结果收集的共享事实来源。
- [x] 只运行 `DinoTrainingRewardRulesTests` 并记录 RED/GREEN。

## Task 2：接入正式 Agent 与 gamma

**Files:**
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingAgent.cs`
- Modify: `flow_rl/configs/phase8_dino_ppo_smoke.yaml`
- Modify: `flow_rl/configs/phase8_dino_fpo_smoke.yaml`
- Modify: 其他现有正式 Dino PPO/FPO/ReinFlow/PolicyFlow 配置（仅在实际存在且仍使用旧 gamma 时）
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`

- [x] Agent 在 `OnEpisodeBegin` 重置 ledger；Wait 不访问部署位置且奖励为零；非法部署只扣一次 `0.02`；合法部署不再产生成本惩罚；守卫/墙按实例稳定身份去重。
- [x] 进入 `Ending` 时先冻结 `PlayerWon` 与胜利时最终肉，再结算一次并结束 episode；interrupted 只调用 `EpisodeInterrupted()`，不发终局奖励。
- [x] 把现有正式 Dino 算法配置的 `gamma` 统一为 `0.995`，不修改历史 3DBall 配置。
- [x] 运行一个聚焦 Agent PlayMode 测试和配置解析/值检查；不重复 Player build 或四环境 smoke。

## Task 3：证据、审查与计划更新

**Files:**
- Create: `reports/phase8/structured-parallel-training/reward-validation.md`
- Modify: `docs/PROJECT_PLAN.md`
- Modify: `docs/superpowers/plans/2026-08-30-dino-attack-layered-battlefield.md`
- Modify: this plan file

- [x] 记录精确命令、退出码、测试数量与修改后的奖励/gamma；明确未运行的长验证。
- [x] 独立审查奖励量级、去重、终止顺序和范围；通过后才勾选“分层战场”Task 1 的奖励前置门。
- [x] 保持 Unity stopped/clean、Console 无新错误；不提交、不暂存、不清理或还原既有修改。

## 完成定义

- 聚焦规则和 Agent 验证通过；自然胜/负、interrupted、重复事件、非法/Wait/合法部署均符合批准规格。
- 四种正式 Dino 算法配置为 `gamma=0.995`；历史配置不变。
- 共享奖励规则可供后续结果对比复用，且 `TASK-8.11-MAP2` 不再依赖旧奖励语义。
