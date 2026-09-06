# v0.1.0 发布检查

日期：2026-09-06。

## 本次增量验证

- 在新建Python 3.10虚拟环境中按公开`requirements.lock`安装完成，PyTorch 2.2.1+cu121；`pip check`通过，非集成测试 **374 passed、1 skipped、3 deselected**（14.15秒）。未复用旧环境site-packages。C盘首次安装因空间不足失败，清理本任务失败环境后改在D盘安装；此过程没有更改锁定版本或原研究环境。

- Unity 6000.3.22f1 Windows x64 Mono构建成功；首场景MapSelect，保留三战场和三种已部署ONNX。游戏程序启动后，Direct3D 11、Mono、PhysX及输入初始化完成；日志无运行异常。隐藏启动进程检查后被停止，未验证正常关闭流程。
- EditMode 4/4通过；地图三PPO、PolicyFlow原有PlayMode 2/2，新补FPO真实模型开始、认输、原布局重试1/1通过。仅新增发布副本测试，不改变游戏生产逻辑。
- 从解压后的附件加载三份checkpoint，以6组样本对照ONNX：最大绝对误差PPO `1.19e-7`、FPO `2.62e-6`、PolicyFlow `6.26e-7`；动作有限且位于[-1,1]。Flow BC best的严格checkpoint合同检查通过。
- 原实验FixedClockV5训练Player经LLAPI连接：六流shape `(5,) / (5,8) / (6,5) / (11,7) / (8,6) / (10,7)`，连续动作4维。实际325个决策、4个自然终局、0截断，日志覆盖两张训练地图。
- 五个ZIP逐项解压读取校验成功；PowerShell下载脚本在独立空目录安装及重复安装均通过，遇到已有不同文件时不会覆盖。包内清单保留原路径、大小、SHA-256；机器可读索引见[artifacts.json](../artifacts.json)。
- 损坏缓存ZIP拒绝测试通过；四份`release_*_example.yaml`在只安装Release附件的独立目录解析通过，Flow BC同时核对数据与训练build的来源hash。

**未完成的验证：** 桌面交互工具无法初始化，成品三地图三策略的全流程人工游玩检查未完成。因此本次标为研究演示 prerelease，不宣称完整发布验收通过。历史全量Unity失败及科研限制仍见[状态说明](STATUS.md)。

## 首轮源码发布历史检查

- 在独立发布副本根目录运行非集成测试，使用发布副本的`flow_rl/src`：**374 passed、1 skipped、3 deselected**，12.52秒。跳过项需要未随Git发布的历史PolicyFlow checkpoint；3项集成测试未选择。
- 本次使用原项目现有Python 3.10研究环境的依赖，未声称在全新Python环境完成依赖安装验证。
- 公开副本的生产Python源码、保留的Unity资产及.meta与源文件一致。测试的历史文件依赖改为临时生成数据，保留真实配置/元数据校验；另增加构建hash不匹配的拒绝测试。纠正旧PPO配置断言，实际实验超参数未变。
- 三种部署ONNX与Unity配置保留；具体SHA-256见`artifacts.json`。模型结构检查与配置GUID对应关系在发布前静态验证。
- ML-Agents依赖脚本已实际下载固定commit并通过第二次幂等执行；三种训练CLI帮助命令通过。
- 首页、公开说明的本地链接与截图在发布前检查；内部工作计划/面试文档及大实验日志未上传。
- 首轮仅源码发布时尚未重跑Unity；后续构建与定向验证已由上节覆盖，仍不能称全量Unity测试通过。

复查命令（在已安装依赖的仓库根目录）：

```powershell
python -m pytest -q flow_rl/tests -m "not integration" -p no:cacheprovider
python -m flow_rl.cli.train_dino_parallel_ppo --help
python -m flow_rl.cli.train_dino_parallel_fpo --help
python -m flow_rl.cli.train_dino_parallel_policyflow --help
```
