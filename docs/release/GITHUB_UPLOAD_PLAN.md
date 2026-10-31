# GitHub 上传计划

日期：2026-09-06。计划编写：已完成；源码发布：已完成；可执行程序及数据附件发布：已完成。完整人工游玩验收受桌面工具不可用影响，未完成。

### 当前执行口径（2026-09-06，覆盖下方初始计划状态）

用户已要求提交GitHub，并指定在本人账号下新建公开仓库`dino-attack-flow-rl`。已确认账号为`xvchengxvcheng`，采用下方独立发布副本方案。此次先完成源码、场景、三种部署ONNX、截图与公开运行文档的提交；新游戏构建、训练Player/checkpoint/数据附件和`v0.1.0` Release仍保留为后续任务，不把源码上传等同完整Release验收。

- [x] 候选清单、依赖引用检查、独立复制、公开文档和模型校验；保留2,419个生产/Unity文件与原文件字节一致。
- [x] 发布副本Python测试374 passed、1 skipped、3 deselected；依赖脚本实际下载固定ML-Agents commit且幂等复跑通过，三CLI帮助、三ONNX结构/GUID/hash检查通过。
- [x] 创建公开仓库`https://github.com/xvchengxvcheng/dino-attack-flow-rl`，本地提交`79b13c28efb82dae7b849b6ec976620735073ebb`。
- [x] 推送`main`并从远端重新克隆；commit为`79b13c28efb82dae7b849b6ec976620735073ebb`，2,614个清单文件SHA-256与57处公开文档本地链接全部通过；连同清单文件共2,615个Git文件。匿名GitHub API确认公开可访问、默认分支main。
- [x] 2026-09-06按用户要求重新导入并构建最新Windows x64游戏：EditMode 4/4、PlayMode 2/2通过，成品启动初始化检查通过；完整ZIP为`Builds/DinoAttack-Windows-20260906.zip`，232个文件解压读取hash通过。详见`reports/windows-rebuild-20260906/README.md`。成品界面全流程未验证。
- [ ] 独立全新Python环境、三地图三策略成品全流程、训练Player/checkpoint/数据附件、GitHub Release及下载复验。新游戏包目前仅在本地，覆盖上文“新游戏构建仍为后续任务”的历史描述。

### 剩余计划执行更新（2026-09-06，覆盖历史未开始项）

- [x] 用户已授权完成剩余上传，采用`v0.1.0`研究演示prerelease；不继续训练，不将阶段8科研任务标为完成。
- [x] 独立Python 3.10环境按锁定依赖安装成功，pip check、374 passed/1 skipped/3 deselected、三CLI帮助与CUDA识别通过。C盘空间不足的失败安装保留日志，仅清理本任务残留并转至D盘独立环境。
- [x] FPO地图三真实开始/认输/重试PlayMode 1/1；与先前PPO/PolicyFlow合计3/3。三checkpoint/ONNX batch6对照、Flow BC严格加载、数据边界检查通过。
- [x] 冻结训练Player真实LLAPI：325决策、4自然终局、0截断，两图日志、六流/4D合同通过。原Player未重建或替换。
- [x] 五个ZIP及SHA256SUMS/artifacts索引齐备，原路径及逐文件hash验证通过；下载脚本空目录安装、幂等与损坏缓存拒绝通过；四份相对路径示例配置在解压目录解析通过。
- [x] 源码凭据模式扫描通过，未纳入Unity生成Timers/设置差异。远端新clone2,621清单文件、61处链接校验通过。ML-Agents依赖首次TLS中断后保留失败副本，重新下载及幂等重跑通过。
- [x] Release已公开：https://github.com/xvchengxvcheng/dino-attack-flow-rl/releases/tag/v0.1.0；最终commit/tag为`cacab8ee5653a6a7704e20ec8a4376bd9ba78515`。新clone中五包匿名下载/解压/逐文件校验通过，索引和SHA256SUMS亦匿名下载一致，共七附件；下载版游戏启动与解压checkpoint加载通过。完整记录`docs/release/V010_PUBLICATION_REPORT.md`。
- [ ] 成品三地图三策略完整人工游玩检查：桌面交互工具无法初始化，未冒充通过；公开VALIDATION、STATUS与Release说明已披露。该缺口不会因上传成功自动勾选。
- 可选研究checkpoint附件不纳入本版；公开包仅含三部署checkpoint和Flow BC best。完整实验记录指选定运行的CSV/配置/摘要和必要证据，不包含全部历史周期权重。

发布副本：`release-staging/dino-attack-flow-rl/`；候选源文件清单：`docs/release/UPLOAD_INVENTORY.json`；筛选记录：`docs/release/SOURCE_PUBLICATION_PREPARATION.json`。下方A–E保留完整Release原计划，不因首轮源码提交全部勾选。

## 1. 首版范围与组织

发布“Dino Attack · Flow RL”研究演示：三张地图、玩家进攻与布防、PPO/FPO/PolicyFlow三种已接入AI、Python训练/评估/导出代码，以及对应模型和实验材料。首版不要求继续训练、补做ReinFlow Dino或Action Chunk，不宣称阶段8公平比较及原阶段10全部完成。

采用**一个源码仓库 + GitHub Release附件**。建议仓库名`dino-attack-flow-rl`、首版标签`v0.1.0`；账号/组织和仓库名在实际上传前确定。以下目标路径均为规划，并非已创建的产物。

在项目内新建`release-staging/dino-attack-flow-rl/`作为独立发布副本；附件放在`release-staging/artifacts/v0.1.0/`。现有根仓库和Unity独立仓库保持原样，不更改其remote，不清理dirty文件、不删除嵌套`.git`、不改写旧历史。新副本不复制任何原仓库`.git`或工作树。

```text
dino-attack-flow-rl/
├── README.md
├── .gitignore / .gitattributes
├── flow_rl/                  # 源码、测试、依赖和配置
├── dino-attack/              # Assets、Packages、ProjectSettings
├── docs/
│   ├── assets/readme/       # 现有4张截图
│   ├── SETUP.md             # Unity/Python/依赖安装
│   ├── TRAINING.md          # 训练、评估、导出、恢复
│   └── STATUS.md            # 已交付能力和已知限制
├── scripts/                 # 依赖获取、附件下载与校验
├── results/                 # 精简结果、曲线和来源记录
└── artifacts.json           # 附件版本、相对路径、SHA-256
```

## 2. 上传清单

| 位置 | 上传内容 | 整理要求 |
|---|---|---|
| Git：Python | `flow_rl/src/`、`tests/`、必要`scripts/`、`pyproject.toml`、`environment.yml`、`requirements.lock` | 保留已有四算法代码；测试或CLI引用的辅助脚本不能遗漏 |
| Git：配置 | v2协议、最终采用的三算法/Flow BC配置、匹配的smoke/评估配置 | 公开示例用相对路径；原始实验配置另留来源快照，不静默改超参数；测试引用的历史配置继续保留 |
| Git：Unity | 正式检出的必要`Assets/`、全部配套`.meta`、`Packages/`、`ProjectSettings/`、原有来源说明 | 按场景/Prefab/脚本引用闭包筛选；不能只复制`LlamAcademy`文件夹或依据目录名删除依赖 |
| Git：部署模型 | 下表三组ONNX、manifest、模型配置及其`.meta` | 保持GUID；只保留实际部署组，旧模型排除前检查资产和测试引用 |
| Git：文档 | 首页、4张截图、安装/训练/状态说明、上游来源和原有许可文件 | 不复制全部工作日志/面试材料；公开副本README链接改指随包文档，不能保留断链 |
| Git：结果 | 小型冻结评估JSON、三策略模型卡、精简训练曲线、失败实验摘要 | 标明build、checkpoint、seed、动作模式和统计口径；不把在线曲线当冻结排名 |
| Release：大产物 | 游戏Player、训练Player、checkpoint、成功数据、完整实验记录 | 独立ZIP、校验和、下载/放置说明；不提交整个`reports/`或`Builds/` |

**当前部署模型集合**（位于`dino-attack/Assets/Resources/DinoInference/`）：

| 策略 | ONNX | 配置 | 对应checkpoint |
|---|---|---|---|
| PPO | `PPONavMesh16EnvSeed0TimeScale5Final4721698.onnx` | `DefaultModel.asset` | final，4,721,698 steps |
| FPO | `FPOUpdate160Step2606819.onnx` | `FpoModel.asset` | step-2606819，policy version159 |
| PolicyFlow | `PolicyFlowStep2147452.onnx` | `PolicyFlowModel.asset` | step-2147452，policy version131 |

三份ONNX合计约5.24 MiB，可随源码提交。执行时重新核对配置引用、GUID与SHA-256；若模型已被其他任务更新，先更新本清单，不能依据旧文件名覆盖新模型。

**排除项**：`.conda/`、`Library/`、`Temp/`、`Obj/`、`Logs/`、`UserSettings/`、`.worktrees/`、`.codegraph/`、`.codex/`、`.superpowers/`、机器专用IDE配置、Python缓存、自动生成解决方案、PID/临时日志、场景历史备份、全部周期checkpoint。`Assets/ML-Agents`、`RayFire`、其他示例或备份目录须先检查实际引用，不能盲目全删。排除仅作用于发布副本，不操作源目录。

## 3. Release附件

| 建议附件名 | 内容 | 说明 |
|---|---|---|
| `dino-attack-windows-x64-v0.1.0.zip` | 从发布副本新建的完整Player | 包含EXE、`_Data`和依赖；必须验证三策略；旧exe不能直接更名发布 |
| `dino-training-fixedclock-v5-windows-x64.zip` | 当前实验实际使用的完整FixedClockV5 Player | 保留原构建，按全目录文件清单/hash标识；与新游戏Player分开，不声称动力学等价 |
| `dino-deployed-checkpoints-v0.1.0.zip` | 上表3个checkpoint + Flow BC `best.pt` | 共约22.4 MiB原始权重；包含配置/normalizer/provenance，不只提取裸tensor |
| `dino-success-demonstrations-v1.zip` | `dataset/`中NPZ/metadata、`provenance-v2.json`、合法模式报告 | 两图各500个成功episode，57,459样本；保留原数据字节与来源hash，不把成功episode等同每步动作合法 |
| `dino-experiment-records-v0.1.0.zip` | 与模型相关的CSV、完整摘要、配置和必要日志 | 按PPO/FPO/PolicyFlow/BC分目录，恢复段、warm-start与中断边界明确 |
| `dino-research-checkpoints-v0.1.0.zip`（可选） | FPO final、PolicyFlow step-3196659等少量研究权重 | 与部署模型区分；不称为更优、final或已经过游戏验证，除非有对应证据 |

所有附件配`SHA256SUMS.txt`与机器可读索引。checkpoint内部已有路径/provenance保持原始可验证字节；公开的旁置manifest使用相对路径、源hash和路径映射。若确需制作改写过的可移植checkpoint，作为新衍生产物另算hash并重验加载，不能沿用原hash。

## 4. 执行顺序与退出条件

### A. 冻结并导出发布副本 — 已完成

- [x] 生成文件白名单、源路径、大小、SHA-256及双仓库commit/dirty说明；包含必要未跟踪源码、模型与meta。不能仅用`git archive HEAD`，否则会漏掉当前AI接入与Python实现。
- [x] 白名单复制到新的空发布目录；复制后核对hash，发现执行期间源文件变化则单独重采，不覆盖用户源文件。
- [x] 使用发布专用`.gitignore`，取消对整个`dino-attack/`的忽略；按文件盘点结果给必要大二进制配置Git LFS，普通小文件走Git。
- [x] 检查将上传的所有文件和新建Git历史没有环境缓存、临时凭据、机器配置或多余Git仓库。

**退出条件**：得到完整、可审查的文件清单、总大小和独立源码副本，源项目未改动。

### B. 补齐可移植依赖与说明 — 已完成

- [x] 新增幂等`setup-dependencies.ps1`，获取ML-Agents官方固定commit `a2777719560e4676be99e2ea128c5eb1fbeb3dbb`到同级`ml-agents/`；保留`file:../../ml-agents/com.unity.ml-agents`合同。已存在不匹配的目标目录时报告差异，不reset或覆盖。
- [x] 执行时再次核对Unity package目录相对该commit的差异，包括未跟踪文件；必要改动以明确补丁提供。本次仅确认该目录无已跟踪diff，未判定所有未跟踪文件都无关。
- [x] 外部FPO/ReinFlow/PolicyFlow官方仓库仅记录URL和固定commit；正式Unity/Python运行若无依赖则不复制。被测试或官方复现入口实际引用的依赖要提供可选获取步骤。
- [x] 创建可移植配置与`download-artifacts.ps1`，下载后校验hash并安装至文档规定路径；发布前把占位URL替换为真实附件URL。
- [x] 写明安装、Player路径、新run目录、PPO成功轨迹→Flow BC→PolicyFlow依赖顺序；修正公开README的旧路径与文档链接。

**退出条件**：新目录无需访问本机旧workspace即可安装依赖、解析配置、加载对应模型。

### C. 验证公开副本 — 进行中

- [x] 在新的独立Python环境运行项目根目录非集成测试与CLI帮助/配置预检；不修改冻结基线环境，不启动正式训练。
- [x] 处理影响公开配置的20env旧断言失配及cwd依赖；任何测试调整要反映真实合同，不改实验参数或删除失败测试来取绿。
- [x] Unity重新导入与编译，运行模型路由/加载、地图三开始/重试等定向EditMode/PlayMode；按Unity技能执行。
- [ ] 构建新的Windows游戏Player，在三地图分别检查PPO/FPO/PolicyFlow的选择、开始、决策、结算与返回/重试流程，保存日志。 **新构建已完成；完整人工交互检查因桌面工具不可用未完成。**
- [x] 训练Player验证LLAPI握手、v2六流/4D动作及代表性自然终局；ONNX加载/边界/有限性与PyTorch对照通过。只跑必要smoke，不续训。
- [x] 核对README图片、全部本地链接、附件解压目录及来源hash；用新clone重复快速体验与模型加载。

**退出条件**：公开版核心玩法、三策略和安装流程通过。历史OpenTropical失败须列出名称/影响/基线并区分新增失败；已知科研确定性、算法稳定性和多seed不足记录在`STATUS.md`，不伪装通过，也不把它们扩展为本次新研究任务。若失败影响首页承诺的玩法或模型加载，则先修复对应发布问题。

### D. 打包与本地提交 — 已完成

- [x] 将新游戏Player和冻结训练Player分别打包，生成完整目录hash，不仅哈希启动器EXE。
- [x] 准备必需checkpoint/数据包和可选实验记录，逐包检查解压、加载、路径与校验和。
- [x] 汇总发布说明：功能、三种部署模型、环境要求、已知限制、测试结果与附件索引。
- [x] 在发布副本初始化独立Git，按核对后的文件列表暂存与提交；检查无嵌套gitlink遗漏、无意外大文件或凭据；不改原仓库remote/history。

**退出条件**：源码提交、候选标签、附件、校验和及发布说明齐备，形成最终上传预览。

### E. 上传与远端复验 — 已完成

- [x] 填定GitHub账号/组织、仓库名和公开可见性；当前任务只授权制定计划，执行发布需用户明确要求上传。
- [x] 将公开源码推到指定的新仓库；若目标已有内容，先检查并选择兼容整合方式，不force push。
- [x] 创建`v0.1.0` Release并上传对应附件；填入实际下载链接和标签commit。
- [x] 从GitHub新clone并下载附件，验证图片展示、依赖获取、附件hash和代表性游戏/模型加载流程。
- [x] 回填仓库URL、Release URL、commit、附件校验和、验证日期与已知限制至主计划。

**退出条件**：远端源码和附件均可访问、可按README运行，且最终上传内容与本地验证版本一致。

## 5. 本次计划依据

- 外层HEAD：`209725f4087dd4e04e5f15ca48efd4a3de69733b`；Unity HEAD：`7a0c2001ca349b146d7a74becaa31a133f7e1613`，两者均有dirty/untracked内容，HEAD不代表完整当前产物。
- 根`.gitignore`忽略Unity及官方参考仓库；Unity package当前引用同级ML-Agents目录。README和四张截图已存在。
- 上表三种部署ONNX及三个checkpoint、Flow BC best、PolicyFlow后续step-3196659均已按磁盘路径确认存在。PolicyFlow实际已停止训练，首版使用当前已接入模型，无需恢复训练来完成上传计划。
- 本次仅编写计划并维护主计划；A–E的文件整理、依赖改动、测试、构建、打包、Git提交与远端发布均未执行。
