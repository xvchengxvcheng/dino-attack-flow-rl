# 公开源码状态

日期：2026-09-06。交付源码、场景、三种已部署ONNX与截图；[v0.1.0研究演示](https://github.com/xvchengxvcheng/dino-attack-flow-rl/releases/tag/v0.1.0)另提供新Windows游戏、冻结训练Player、部署checkpoint/Flow BC、成功数据和精选实验记录。下载与校验见[安装说明](SETUP.md)。

## 已有能力

- 三张地图、持续部署、资源循环、玩家进攻和自由布防。
- PPO、FPO、PolicyFlow 已接入当前 Unity 源码的 AI Strategy；ReinFlow 尚未接入 Dino。
- 结构化可变实体观测、Set Transformer、并行训练、版本化采集、评估与模型导出。
- 共享玩法接口、固定步战斗与导航就绪处理；尚未证明训练端和游戏端逐步轨迹完全一致。

## 当前部署模型

| 模型 | checkpoint环境步数 | 说明 |
|---|---:|---|
| PPO | 4,721,698 | 已完成run的final模型。 |
| FPO | 2,606,819 | policy version159，中间候选；长期run后段发生退化。 |
| PolicyFlow | 2,147,452 | policy version131；后续训练停止点3,196,659未替换当前部署模型。 |

模型校验值见[artifacts.json](../artifacts.json)。ONNX用于游戏推理，不能直接代替训练checkpoint恢复优化器状态。

<a id="evidence"></a>

## 历史证据与范围

以下摘要来自原项目报告，完整工作日志和面试资料未纳入公开源码。它们不是本次重新运行Unity后取得的结果。

| 日期 | 原报告主题 | 结果与限制 |
|---|---|---|
| 2026-09-01 | 战斗目标、死亡和场景入口回归 | 选定回归集33/33通过，其他验收另行记录。 |
| 2026-09-03 | 导航就绪及Python动作返回等待对照 | 0/75ms对照19/20局结局相同，仍有分叉；不能当作训练与正常游玩的全轨迹一致证明。 |
| 2026-09-04 | 观测时间审计 | 剩余时间来自Running固定步计数，不按Python等待的现实耗时扣减。 |
| 2026-09-05 | PolicyFlow导出与AI Strategy接入 | PyTorch/ONNX batch1、3数值对照通过，Unity有实际模型前向；地图三开始流程PPO/PolicyFlow定向测试2/2通过。 |
| 2026-09-05 | 原项目完成情况审计 | Python372通过、1失败、3未选入；所引Unity历史PlayMode为46/59，不能称全量通过。 |

训练/推理过程历史对照针对当时的`step-1312449`与具体Player：共享观测、动作和25-step接口，但程序集、动作采样和回合收尾存在差异。当前默认模型已更新，旧版本调查仅说明需要核对的维度，不能当作当前模型表现结论。

训练曲线统计曾按不同恢复段和warm-start口径整理；PolicyFlow使用PPO教师与BC，预训练成本需要单列。FPO后期退化、部分Flow策略开局等待较久均为已记录现象，不宣称三算法完成公平排名。

## 本次发布验证

验证结果见[发布检查](VALIDATION.md)。发布副本保留生产源码及实验参数；将部分单元测试对本地历史产物的依赖改为临时生成夹具，并纠正已过期的20环境测试断言为当前16环境配置。BC配置中的机器绝对路径改成相对路径。原工作区未被覆盖。

## 待完成

严格确定性、多随机种子公平比较、Dino ReinFlow、action chunk、移动端性能仍未完成。新Windows构建和启动检查已通过，成品三地图三策略的完整人工游玩检查未完成（桌面交互工具无法初始化）；不能称完整发布验收或全量Unity测试通过。本项目未实现多人网络帧同步或游戏内LLM。

## 来源

游戏基础：[LlamAcademy Dino Attack](https://github.com/llamacademy/dino-attack)。保留[dino-attack原README](../dino-attack/README.MD)与其原始来源文件。

官方算法参考版本：FPO `418c2554`、ReinFlow `e722e151`、PolicyFlow `7f304b96`。对应仓库链接位于[首页](../README.md)。发布来源根仓库HEAD `209725f4087dd4e04e5f15ca48efd4a3de69733b`、Unity HEAD `7a0c2001ca349b146d7a74becaa31a133f7e1613`；发布同时包含已检查的未提交源码，不能仅以这些HEAD重建发布内容。
