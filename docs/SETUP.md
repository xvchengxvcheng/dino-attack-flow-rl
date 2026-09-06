# 安装与运行

## Unity 游戏

1. 安装 Unity 6000.3.22f1（如需构建 Windows 游戏，同时安装对应构建模块）。
2. 在仓库根目录运行 `powershell -ExecutionPolicy Bypass -File scripts/setup-dependencies.ps1`。需要 Git 和网络连接；脚本获取固定版本 ML-Agents 的 Unity Package，已存在目录不会被覆盖或重置。
3. 用 Unity 打开 `dino-attack/`，等待包恢复与资源导入。
4. 打开 `Assets/LlamAcademy/Dinos/Scenes/MapSelect.unity` 并进入 Play Mode。
5. 地图一、二可手动部署或在结算后选择 AI 重赛；地图三先布置防御，再选择 PPO、FPO 或 PolicyFlow 开始。

三个部署 ONNX 随源码提供，游戏内 AI 无需 Python。外部依赖、编辑器导入和编译仍需要完成。本次是源码发布，不提供已重新构建的游戏 EXE；旧版 Player 不代表当前源码。

## Python

在仓库根目录执行：

```powershell
cd flow_rl
conda env create -f environment.yml
conda activate flow-rl
python -m pip install -r requirements.lock
cd ..
python -m flow_rl.cli.train_dino_parallel_policyflow --help
python -m pytest -q flow_rl/tests -m "not integration"
```

环境为 Python 3.10、PyTorch 2.2.1（原实验 CUDA 12.1）。配置帮助和多数单元测试不启动 Unity。完整训练需要另外准备 Unity 训练程序；参见[训练说明](TRAINING.md)。

## 目录与依赖

- `dino-attack/Packages/manifest.json` 保留 `file:../../ml-agents/com.unity.ml-agents` 引用。
- ML-Agents 固定 commit：`a2777719560e4676be99e2ea128c5eb1fbeb3dbb`。
- 官方 FPO、ReinFlow、PolicyFlow 仓库不随包复制；普通游戏运行和本地算法单元测试不依赖它们。
- `flow_rl/scripts/` 中显式命名 official 的复现工具需要另行获取对应官方仓库与隔离依赖；版本来源见[状态说明](STATUS.md)。
