# Dino Attack 紧凑结构化编码器与训练实施计划

**目标：** 按用户批准的容量方案缩小结构化 PPO 网络，保留 Defense/Dinosaur 分流与三路区域拼接，并使用战斗修复后的 Player 启动新的正式训练。

**范围：** 只修改 `flow_rl` 的 Set Transformer、Deep Sets、正式 PPO 配置/契约、相关测试和本计划；Unity 源码仅用于重新构建包含既有战斗修复的 Player，不改变场景或玩法。

## Task 1：Set Transformer 拓扑与容量

- [x] 先修改模型测试，要求默认 `d_model=48`、Defense 一层 ISAB（8 inducing points）、Dinosaur 一层 SAB、dropout 0、三路拼接 `144→128→48`、最终融合 `288→256→128`。
- [x] 运行聚焦测试并确认旧拓扑按预期 RED。
- [x] 最小修改 `dino_set_transformer.py`，加入独立 Dinosaur SAB 路径并扩充 checkpoint 拓扑元数据。
- [x] 运行聚焦测试并确认 GREEN。

## Task 2：Deep Sets 动态 Regions

- [x] 先修改模型测试，要求 Regions `8→32→16`、无固定编号 embedding、融合输入 656、输出 128，并验证 Regions 行置换只按动态坐标影响结果。
- [x] 运行聚焦测试并确认旧实现按预期 RED。
- [x] 最小修改 `dino_deep_sets.py`，删除固定编号 embedding 并调整融合维度/metadata。
- [x] 运行聚焦测试并确认 GREEN。

## Task 3：正式 PPO 配置与 checkpoint fail-closed

- [x] 先修改配置/检查点测试，要求 `hidden_sizes=[128,128]`、`d_model=48`、`heads=4`、`inducing_points=8`、`layers=1`、`dropout=0.0`、`output=128`，并拒绝旧 ISAB/ISAB 拓扑。
- [x] 运行聚焦测试并确认旧契约按预期 RED。
- [x] 最小修改配置验证与新 seed 0/1 YAML；使用唯一 post-fix run 目录。
- [x] 运行聚焦测试并确认 GREEN，审计完整 PPO 参数量接近 0.51M。

## Task 4：验证、post-fix Player 与训练

- [x] 运行全部模型、PPO 配置、checkpoint 与训练相关 pytest；运行 `compileall`、外层 `git diff --check` 与本次 Unity 目标文件 scoped `diff --check`。
- [ ] 生成新的双地图 post-fix 训练 Player，记录路径与 SHA-256，并做最小启动/单更新 smoke。
- [ ] smoke 数值、24/24 worker、进程和端口清理全部通过后，使用唯一目录启动 seed 0 正式训练；完成后再串行启动 seed 1，不运行 seed 2。
- [x] 在 `docs/PROJECT_PLAN.md` 记录 RED/GREEN、参数量、构建、训练状态与产物路径。

**当前阻塞：** Python 改造与离线验证已完成；Unity `6000.3.22f1` 的 Licensing Client 当前没有可用的 `com.unity.editor.headless` entitlement，聚焦 Unity test 与 post-fix Player build 无法形成判定，因此 smoke/正式训练尚未启动。用户在 Unity Hub 登录并激活许可证后，从 Task 4 第二项继续。
