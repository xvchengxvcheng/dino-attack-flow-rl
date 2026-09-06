# Dino Attack · Flow RL v0.1.0

首个研究演示版本：三地图、玩家进攻/自由布防，以及本地运行的 PPO、FPO、PolicyFlow AI。游戏源码基于 `79b13c2` 于2026-09-06使用 Unity 6000.3.22f1 构建；本次后续提交补充下载文档、附件索引和FPO测试，不改变游戏生产逻辑。

## 下载

| 附件 | 用途 |
|---|---|
| `dino-attack-windows-x64-v0.1.0.zip` | 最新Windows x64游戏，约105 MiB。解压运行 `Builds/DinoAttack-Windows-20260906/DinoAttack.exe`，保留整个目录；无需Unity/Python。 |
| `dino-training-fixedclock-v5-windows-x64.zip` | 原实验使用的冻结训练Player，约103 MiB，独立于新游戏。 |
| `dino-deployed-checkpoints-v0.1.0.zip` | PPO final4721698、FPO step2606819、PolicyFlow step2147452、Flow BC best，原始状态与来源未改写。 |
| `dino-success-demonstrations-v1.zip` | 两图各500个成功episode、57,459样本及合法动作覆盖报告。 |
| `dino-experiment-records-v0.1.0.zip` | CSV、配置、冻结评估、历史曲线和失败摘要；不是所有周期权重或全部工作日志。 |
| `SHA256SUMS.txt` / `artifacts.json` | 校验和及机器可读附件索引。 |

源码仓库的 `scripts/download-artifacts.ps1 -All` 可下载、验证并安装全部附件；不带 `-All` 只安装游戏。数据与权重恢复到原始相对路径。开始新实验前必须使用新的 `run_directory`。

## 验证与边界

- 全新Python 3.10环境按锁定依赖安装成功，`pip check`通过；非集成测试374通过、1跳过、3未选入。

- Windows x64 Mono构建成功，成品进程及图形/脚本/输入初始化检查通过。
- EditMode 4/4；地图三真实PPO、PolicyFlow、FPO开始/结算/同布局重试合计PlayMode 3/3通过。
- 三种模型PyTorch/ONNX batch6对照通过，最大绝对误差不超过2.7e-6，动作有限且在[-1,1]。
- 冻结训练Player：六组观测、4维动作，325个决策、4个自然终局、0截断；覆盖两张训练地图。
- 五个ZIP均有原始路径/大小/SHA-256清单，解压逐文件校验通过，下载脚本重复安装通过。

这是 prerelease 研究演示。桌面交互工具不可用，**成品三地图三策略全流程人工游玩未验证**；历史全量Unity测试仍有已记录失败。训练与新游戏的逐步动力学等价性、多seed公平比较、Dino ReinFlow与action chunk未完成。FPO后期退化、Flow策略开局等待、成功轨迹包含非法动作等限制保留，不能把训练曲线当作冻结排名。完整验证更新见仓库 `docs/VALIDATION.md`。
