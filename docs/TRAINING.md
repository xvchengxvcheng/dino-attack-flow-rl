# 训练与模型说明

`flow_rl/src/flow_rl/` 提供环境连接、PPO / FPO / ReinFlow / PolicyFlow、采集、评估和导出实现。游戏中的已部署策略为 PPO、FPO、PolicyFlow；ReinFlow 的 Dino 接入尚未完成。

## 训练入口

```powershell
python -m flow_rl.cli.train_dino_parallel_ppo --help
python -m flow_rl.cli.train_dino_parallel_fpo --help
python -m flow_rl.cli.train_dino_parallel_policyflow --help
```

在仓库根目录执行，使用 `--config` 传入 YAML。`flow_rl/configs/` 保留研究配置以便理解参数和运行配置测试；其中的构建、历史数据和 checkpoint 路径并不表示文件已包含在仓库中。

## 启动前准备

1. 准备与场景及六组观测协议匹配的 Unity 训练 Player，填写配置中的 `build_path`。
2. 使用 `dino_attack_structured_set_v2.yaml`，确认六组观测共270个数、动作4维。
3. 将 `run_directory` 指向新的空目录；根据实际机器选择设备及并行资源。近期研究配置为16环境、timeScale 1、rollout 16384，历史文件名可能保留旧值。
4. 先进行小规模连通性和自然终局检查，再运行正式训练。源码导入或CLI帮助通过，不代表训练Player已经验证。

## PolicyFlow 的数据依赖

过程是 PPO 教师成功对局 → Flow BC 预训练 → PolicyFlow 微调。数据采集入口为 `flow_rl.cli.collect_dino_ppo_demonstrations`，预训练入口为 `flow_rl.cli.train_flow_bc`。应使用自己的数据及相应来源校验值，不能把原实验hash用于新数据。

Git 仓库内提供 ONNX；原始训练 Player、三种部署 checkpoint、Flow BC best、成功数据和精选实验记录通过 [v0.1.0 Release](https://github.com/xvchengxvcheng/dino-attack-flow-rl/releases/tag/v0.1.0) 提供。在仓库根目录执行：

```powershell
powershell -ExecutionPolicy Bypass -File scripts/download-artifacts.ps1 -All
```

附件解压到原始相对路径 `reports/phase8/...`，现有配置的 `../../reports/...` 引用可解析。checkpoint 内部来源保持原始字节；其中机器绝对路径只是历史记录，公开运行路径以旁置清单及 YAML 为准。安装实验记录后，旧 `run_directory` 已存在，**开始新实验前必须复制配置并将输出改为新的空目录**，不要覆盖历史记录。

`flow_rl/configs/release_ppo_example.yaml`、`release_fpo_example.yaml`、`release_flow_bc_example.yaml`、`release_policyflow_example.yaml` 提供已解析验证的相对路径示例，输出分别指向新的 `runs/v010-*-example`。它们只将所引用历史配置的输出目录改为新目录，不改实验参数，也不自动启动训练。FPO示例保留原始从零配置，其长期稳定性限制仍然存在；它不是当前中间checkpoint的精确复训配方。

- 游戏 Player 是 2026-09-06 新构建；训练 Player 为实验实际使用的 FixedClockV5，两者分别有完整文件清单，不能视为动力学完全等价。
- 数据为两图各500个成功回合，共57,459样本；成功回合中仍包含被拒绝的动作。`action-mode-report-legal-v2.json` 记录合法部署覆盖，不能把所有样本都解释为合法操作。
- PPO 为 final；FPO 为 update160 使用的 policy159；PolicyFlow 为 step2147452。后续 step3196659 并非当前部署模型，首版未附可选研究checkpoint包。
- 原始 CSV、配置、冻结评估和历史曲线快照随实验记录包提供。PolicyFlow 恢复段使用累计步数；FPO 经 warm-start，图中的拼接口径省略部分祖先成本，不能作为从零公平排名。

## 恢复与导出

完整续训需要包含模型、优化器、归一化、随机状态和版本信息的checkpoint，并写入新的输出目录。warm-start只继承权重，与精确续训分开记录；两者都不恢复Unity物理世界的瞬间状态。

导出实现见 `flow_rl/src/flow_rl/export/dino_policy_onnx.py`。在确定模型来源、观测合同和动作方式后导出，再核对PyTorch/ONNX计算结果及Unity实际加载。模型清单与SHA-256见[模型索引](../artifacts.json)。

各模型的动作方式、历史冻结结果及限制见[模型说明](MODELS.md)。
