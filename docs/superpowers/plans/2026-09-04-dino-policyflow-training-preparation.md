# Dino Attack PolicyFlow 训练准备计划

日期：2026-09-04  
所属：`docs/PROJECT_PLAN.md` / `TASK-8.14`  
状态：进行中（全部准备门已通过；正式 seed0 待首 update 验收）

## 目标

在不复用3DBall模型、不把FPO checkpoint伪装成Flow BC、也不绕过Dino v2结构化协议的前提下，为FixedClockV5 Dino Attack建立可审计的PolicyFlow数据、预训练、并行在线训练和冻结评估链。

## 已确认事实

- 官方事实来源为只读`PolicyFlow/` commit `7f304b96e93b2804bdcc943710db885adddd06b8`；`.codegraph/`仅为用户索引，仓库源码未修改。
- `flow_rl`已有PolicyFlow midpoint flow prior、additive Gaussian delta、clipped surrogate、Gaussian KL、Brownian regularization、snapshot refresh、checkpoint/resume和3DBall评估实现。
- 当前PolicyFlow训练器是单Unity进程、扁平MLP、普通running-stat normalizer；没有Dino启动参数、v2协议、Set Transformer、16-env异步采样或Dino逐地图日志。
- 当前PPO demonstration collector同样是单进程、普通normalizer，并逐transition收集全部回合；它没有只保留成功episode，也没有Dino episode/map/protocol provenance。
- 当前Flow BC仅构造扁平`ConditionalVelocityMLP`，不能生成Set Transformer actor初始化checkpoint。
- 当前没有Dino demonstration dataset或Dino Flow BC checkpoint。FPO `step-377087.pt`是`dino_parallel_fpo`模型，不是`flow_bc`，不能作为PolicyFlow actor初始化。

## 实施顺序

### Task 1：冻结训练源与模式判据

- [x] 取得通过统一冻结评估的FixedClockV5 PPO checkpoint；不得使用旧生命周期、旧Player或仅凭训练曲线选择的checkpoint。
- [x] 固定完整Unity构建指纹、v2 protocol hash、Set Transformer metadata、policy seed和布局seed。
- [x] 定义Dino成功策略模式报告：按`map × zone × dino choice`统计部署尝试，并用下一观测`Global.last_action_valid`区分合法/非法部署；报告成功episode覆盖和4×4坐标簇，Wait单列。
- [x] 成功数据中确认存在重复多峰合法部署：map1/map2分别覆盖13/14个合法`zone × choice`模式和50/77个合法坐标bin；多个主模式有数百至数千次合法样本，稀疏T-Rex模式单独披露，不把尝试或非法部署计入覆盖收益。

### Task 2：Dino成功轨迹数据集（TDD）

进行中：已完成纯数据层、原子扩展格式、VersionedBatch桥接及动作合法性记录，并新增严格加载冻结PPO教师的16-env随机策略采集入口；入口复用现有异步环境、v2校验、Set Transformer、Identity normalizer和冻结policy version，单rollout结束后按完整自然成功episode筛选并fail closed检查地图配额、16/16贡献及Unity关键日志。真实500×2采集已启动，结果完成前下列总门暂不勾选。

- [x] 新增Dino并行dataset collector，复用`AsyncUnityVectorEnv`、`_training_launch_args`、v2 protocol校验和16-env异步微批。
- [x] 以完整episode为缓存单位，只把自然成功episode的全部transition写入训练集；失败与truncated episode只计审计统计，不混入成功BC数据。
- [x] 保存environment/process/agent/episode/map/policy-version/terminal reason，保持六流shape和动作顺序。
- [x] 元数据记录checkpoint/build/protocol/encoder/normalizer SHA-256、布局与policy seed、成功率、每图样本量和模式分布。
- [x] 覆盖Agent ID重排、空Decision、自然终止、truncated丢弃、动作边界、固定seed复现和原子写入测试。

### Task 3：结构化Flow BC（TDD）

- [x] 为Flow BC增加`encoder_type=set_transformer`、Dino protocol与48/4/8/1/0/128合同。
- [x] 使用`IdentityObservationNormalizer`，禁止对valid mask、类型位和区域几何做running normalization。
- [x] actor为Set Transformer state encoder + 32维time embedding + `[128,128]` velocity head，动作维度4，NFE4。
- [x] checkpoint写入protocol/encoder/build/dataset provenance，并能被PolicyFlow严格加载；3DBall旧配置继续兼容。
- [x] 训练/验证按episode分组切分，禁止同一episode泄漏到两侧。

### Task 4：Dino并行PolicyFlow（TDD）

- [x] `PolicyFlowActorCritic`的actor、snapshot和critic均支持独立Set Transformer encoder；Flow BC只初始化actor并立即刷新snapshot。
- [x] 新增versioned PolicyFlow batch桥接，保存每个transition自己的`x0/x1/delta/std/logp/old_velocity_grid`，跨异步返回不得读取新policy snapshot。
- [x] 新增Dino batched PolicyFlow decider和16-env trainer，复用现有并行生命周期、地图分类、bootstrap、restart和逐update reporter。
- [x] 使用identity normalizer；checkpoint保存actor/critic optimizer、log-std、snapshot、RNG、policy version、完整build/protocol/encoder/BC provenance。
- [x] 新增Dino冻结 evaluator，分别报告flow-prior deterministic（delta=0但固定x0种子）和stochastic策略；记录NFE、延迟、显存和两图胜率；部署模式覆盖沿用统一v2动作报告接口，正式checkpoint完成后生成。

### Task 5：Smoke与正式训练门

- [x] Python shape/finite/version/snapshot/checkpoint全套回归通过。
- [x] 16-env单rollout smoke：至少16,384 transitions、16/16环境贡献、两图自然终止、0基础设施truncated、一次有限更新、Unity关键异常0。
- [x] 检查初始/更新后delta std、analytic/proxy KL、clip fraction、ratio范围、Brownian loss和非法动作率。
- [x] smoke通过且正式预算与PPO/FPO统一后，才能生成正式seed0 YAML并启动；run目录必须全新。

## 初始参数草案

| 参数 | 初始值 | 依据 |
|---|---:|---|
| Unity build | FixedClockV5 | 当前结构化训练合同 |
| environments | 16 | 与当前FPO动力学负载一致；正式公平合同仍待冻结 |
| time scale | 1 | 降低残余负载敏感风险 |
| rollout | 16,384 | 每环境约1,024 transitions |
| batch | 512 | 与PPO/FPO共同批大小 |
| epochs | 2 | 控制每次on-policy漂移 |
| actor LR | `1e-4` | 比阶段7的`2e-4`更保守 |
| critic LR | `1e-4` | 首个smoke先保持对称，之后依据value loss调整 |
| gamma / GAE lambda | `0.995 / 0.95` | Dino正式合同 |
| ratio clip | `0.2` | 官方/阶段7起点 |
| Gaussian entropy | `0.001` | 阶段7已验证值 |
| Brownian coefficient | `0.001` | 现行关键决策 |
| value clip | `0.2` | 阶段7已验证值 |
| max grad norm | `1.0` | 阶段7低Brownian配置 |
| solver steps / velocity NFE | `2 / 4` | midpoint每步两次velocity调用 |
| horizon | `1` | 阶段8禁止action chunk |
| inference batch wait | `0` | FixedClock训练合同 |
| smoke budget | 16,384 | 一个完整update |
| formal budget | 4,718,592 | PPO与完成后的FPO配置目标预算一致 |

## 2026-09-05 实施与 smoke 证据

- Flow BC `best.pt` 在FixedClockV5双图各20局冻结评估中，deterministic与固定seed stochastic均为map1 `20/20`、map2 `16/20`、总体`36/40=90%`，0 truncated、日志异常0。
- 已实现结构化三编码器、versioned行为快照、16-env batched decider、并行训练/checkpoint和双口径Dino evaluator。定向回归37项通过；全套排除一项与本任务无关的历史PPO“文件名20-env但内容16-env”断言后为`371 passed, 2 skipped, 1 deselected`。
- `std=1` smoke数值有限但随机冻结胜率仅32.5%；`std=0.1/LR=1e-4`发生KL 63.74与ratio `0–594.96`，两者均保留为失败审计，不进入正式配置。
- 合格smoke为`std=0.3`、actor LR `5e-6`：16,399 transitions、16/16贡献、map1/map2各145自然终止、0 truncated、KL 0.00353、clip 0.0316、ratio `0.557–1.695`、Brownian 0.00248、显存峰值0.184 GiB、Unity异常0。训练rollout胜率map1 91%、map2 50%；冻结deterministic为87.5%，stochastic为75%，均0 truncated。
- 正式seed0的首次`a1`启动因外部PID占用worker 4005端口而在训练前fail closed，失败审计保留。`a2`改用worker 4500–4515，Python PID `58564`；首update为16,393 transitions、16/16贡献、双图自然终止144/143、0 truncated、KL 0.00355、clip 3.20%、ratio `0.551–1.563`，checkpoint `step-16393.pt` SHA-256 `fc079b2f...ec53af`。旧接续心跳已暂停，防止重复启动。

## 退出条件

“准备完成”必须同时满足：合格PPO源存在、成功轨迹数据与模式报告存在、结构化Flow BC通过冻结评估、Dino并行PolicyFlow smoke通过、正式预算明确。缺任一项时不得用阶段7 CLI直接启动Dino训练，也不得生成看似可执行但必然不兼容的正式YAML。
