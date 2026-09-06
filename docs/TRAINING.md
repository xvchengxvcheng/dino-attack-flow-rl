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

当前Git仓库只包含运行游戏所需ONNX，不包含原始训练Player、PyTorch checkpoint、成功数据集和完整实验日志。缺少这些产物时，不能直接重放原训练命令或恢复原进度；相关下载附件尚未发布。

## 恢复与导出

完整续训需要包含模型、优化器、归一化、随机状态和版本信息的checkpoint，并写入新的输出目录。warm-start只继承权重，与精确续训分开记录；两者都不恢复Unity物理世界的瞬间状态。

导出实现见 `flow_rl/src/flow_rl/export/dino_policy_onnx.py`。在确定模型来源、观测合同和动作方式后导出，再核对PyTorch/ONNX计算结果及Unity实际加载。模型清单与SHA-256见[模型索引](../artifacts.json)。
