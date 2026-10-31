# v0.1.0 上传完成记录

日期：2026-09-06。**源码与附件上传：已完成；完整人工游玩验收：未完成。**

- 仓库：https://github.com/xvchengxvcheng/dino-attack-flow-rl
- Release：https://github.com/xvchengxvcheng/dino-attack-flow-rl/releases/tag/v0.1.0（公开，prerelease研究演示）
- `main` 与 `v0.1.0`：`cacab8ee5653a6a7704e20ec8a4376bd9ba78515`。
- 游戏生产源码对应构建来源`79b13c2`；后续提交增加文档、下载脚本、示例配置、FPO测试和忽略规则，未改生产玩法。

## 实际验证

- 新建Python 3.10环境：`D:/job/codex-validation/dino-flow-rl-v010/env`，按`requirements.lock`安装，PyTorch2.2.1+cu121；pip check通过，CUDA可用，三CLI帮助通过。
- 非集成测试：374 passed、1 skipped、3 deselected，14.15秒。XML：`reports/windows-rebuild-20260906/release-python-fresh.xml`。
- Unity定向：此前EditMode4/4、PPO/PolicyFlow PlayMode2/2；本次新增FPO真实地图三开始/认输/重试1/1通过。
- 冻结训练Player：六流/4D，325决策、4自然终局、0截断；日志覆盖两张训练地图。未启动新训练。
- 三ONNX与解压checkpoint batch6数值对照通过，最大误差2.63e-6，动作有限/有界；Flow BC best严格加载通过。
- 五包本地逐文件hash验证通过；下载脚本空目录安装、幂等重跑、损坏ZIP拒绝通过；四发布示例配置在仅解压附件的目录解析通过。
- GitHub新clone`release-staging/verify-v010-source`：2,621个清单文件、61处文档本地链接通过，连同清单共2,622个Git文件；固定ML-Agents依赖下载及幂等重跑通过。
- 在新clone运行公开`download-artifacts.ps1 -All`：五包全部匿名下载、解压、逐文件校验通过。随后匿名下载索引/校验和，七附件与本地SHA-256全部一致；公开Release页面HTTP200，正式标签指向上述commit。
- 使用独立Python环境重新加载远端解压模型通过；远端下载游戏PID54452有响应，图形/脚本/输入正常，启动日志错误模式0。隐藏窗口无法正常关闭，检查后仅停止本次进程；未验证正常退出。

关键命令：`docs/release/prepare_v010.py`、`validate_v010.py`、`prepare_example_configs.py`、`finalize_source_manifest.py`、`verify_github_clone.py`、`github_v010.py`；执行记录与XML在`reports/windows-rebuild-20260906/`，远端记录`docs/release/v010-remote-verification.json`。

## 附件

| 文件 | 字节 | SHA-256 |
|---|---:|---|
| `dino-attack-windows-x64-v0.1.0.zip` | 109,731,521 | `5821e5b83b44946a1ee7edea25c7ab2fe5d4d586673e51d5e81b190d5e4df1c4` |
| `dino-training-fixedclock-v5-windows-x64.zip` | 108,219,349 | `6edc48583169c6d8b778abf9d0ab861591723a606ae8be46fe1afe39d9722759` |
| `dino-deployed-checkpoints-v0.1.0.zip` | 21,254,278 | `fdfa4c4dbbc3bc6c72367d5499acf92bf71f883defd5965750031c2923a69692` |
| `dino-success-demonstrations-v1.zip` | 4,421,969 | `bd5c1a86967959c055cee3fd248805328fb762132e9eefe32c97b5c28df02e6c` |
| `dino-experiment-records-v0.1.0.zip` | 16,586,467 | `567ebdc24976d08e9d1935436afc71f70397ccce577591e439cf43399e9c6ec0` |

另有`artifacts.json`与`SHA256SUMS.txt`，共七项Release资产。可选研究checkpoint包未纳入首版；PolicyFlow后续step3196659未替换部署step2147452。

## 保留的限制与过程异常

- 桌面交互工具初始化失败，成品三地图三策略的完整人工游玩未验证。发布为研究演示，不将完整Unity/发布验收记为通过。科研阶段8、严格确定性、多seed、Dino ReinFlow和action chunk状态不变。
- C盘首次安装空间不足，仅清理本任务失败环境/重复下载，迁至D盘成功。原研究环境未修改。
- 新clone依赖首次GitHub TLS中断，失败副本保存在`release-staging/failed-v010-dependency`，重新下载成功。公开操作也遇到一次TLS中断，重试同一Release成功。
- GitHub曾为草稿生成临时untagged标签，已将同一Release关联到正确`v0.1.0`，七附件URL均经实际下载复验。
- 匿名GitHub API返回403，未将其解释为私有仓库；公开页面与直接附件下载实际通过。
- 原Unity项目/旧构建/checkpoint未覆盖。发布副本仅保留Unity运行自动生成的Timers与ProjectSettings差异，均未提交。
