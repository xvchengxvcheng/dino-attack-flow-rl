# 源码发布检查

日期：2026-09-06。

- 在独立发布副本根目录运行非集成测试，使用发布副本的`flow_rl/src`：**374 passed、1 skipped、3 deselected**，12.52秒。跳过项需要未随Git发布的历史PolicyFlow checkpoint；3项集成测试未选择。
- 本次使用原项目现有Python 3.10研究环境的依赖，未声称在全新Python环境完成依赖安装验证。
- 公开副本的生产Python源码、保留的Unity资产及.meta与源文件一致。测试的历史文件依赖改为临时生成数据，保留真实配置/元数据校验；另增加构建hash不匹配的拒绝测试。纠正旧PPO配置断言，实际实验超参数未变。
- 三种部署ONNX与Unity配置保留；具体SHA-256见`artifacts.json`。模型结构检查与配置GUID对应关系在发布前静态验证。
- ML-Agents依赖脚本已实际下载固定commit并通过第二次幂等执行；三种训练CLI帮助命令通过。
- 首页、公开说明的本地链接与截图在发布前检查；内部工作计划/面试文档及大实验日志未上传。
- Unity编辑器、PlayMode、Windows构建和实际交互流程未在此次源码发布中重新运行；不能据此称当前源码全量Unity测试通过。此前的验证与已知限制见[状态说明](STATUS.md)。

复查命令（在已安装依赖的仓库根目录）：

```powershell
python -m pytest -q flow_rl/tests -m "not integration" -p no:cacheprovider
python -m flow_rl.cli.train_dino_parallel_ppo --help
python -m flow_rl.cli.train_dino_parallel_fpo --help
python -m flow_rl.cli.train_dino_parallel_policyflow --help
```
