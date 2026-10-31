# 阶段 2：Unity 外部训练基础设施实施计划

> **面向智能体工作者：** 必需子技能：使用 `superpowers:executing-plans` 逐项实施本计划。各步骤使用复选框（`- [ ]`）语法跟踪进度。

**目标：** 构建项目阶段 2 所需的共享、可测试的 Unity ML-Agents 外部训练基础设施，并通过 100,000 个 3DBall 环境步进行验证。

**架构：** 独立的 `flow_rl` 包通过 ML-Agents Low-Level API 驱动 Unity。Unity 适配器保留传感器元组和决策顺序，而 rollout、GAE、归一化、跟踪和评估均保持算法无关，并且无需启动 Unity 即可测试。

**技术栈：** Python 3.10.12、NumPy 1.23.5、PyTorch 2.2.1+cu121、ML-Agents Env API 1.1.0、pytest、TensorBoard、CSV、YAML。

**规格：** `docs/PROJECT_PLAN.md`

## 全局约束

- 不得修改 `ml-agents/`、其中的 Unity 资源或设置、3DBall 构建、PPO 基线产物或三个算法参考仓库。
- 保持 `.conda/mlagents-r23` 冻结；创建并使用 `.conda/flow-rl`。
- 使用原始 ML-Agents LLAPI，将每个观察流保留为元组，并按照当前 `DecisionSteps.agent_id` 的顺序提交动作。
- 将自然终止事件映射为 `terminated=True`；将 `TerminalSteps.interrupted=True` 映射为 `truncated=True`，并在 GAE 中对其进行 bootstrap。
- 对生产代码行为采用测试先行的红灯/绿灯循环。
- 不创建 Git 提交，因为父仓库没有 `HEAD`，并且完整的 `game_project/` 目录当前处于未跟踪状态。

---

### 任务 1：项目包、锁定环境与诊断

**文件：**
- 创建：`flow_rl/pyproject.toml`
- 创建：`flow_rl/environment.yml`
- 创建：`flow_rl/requirements.lock`
- 创建：`flow_rl/src/flow_rl/__init__.py`
- 创建：`flow_rl/src/flow_rl/cli/diagnose.py`
- 创建：`flow_rl/tests/test_diagnostics.py`

**接口：**
- 产出：`flow_rl.cli.diagnose.collect_diagnostics(build_path: Path) -> dict[str, object]`
- 产出：控制台脚本 `flow-rl-diagnose`

- [x] 编写一个预期失败的测试：传入已知的 3DBall 路径，并断言返回的映射包含 Python、NumPy、Torch、CUDA 可用性/版本、ML-Agents，以及解析后构建是否存在等精确键名。
- [x] 运行 `pytest flow_rl/tests/test_diagnostics.py -q`，确认由于 `flow_rl.cli.diagnose` 不存在而失败。
- [x] 添加 `src` 包骨架、精确锁定的直接依赖、pytest 配置和诊断实现。
- [x] 运行诊断测试和完整单元测试套件。
- [x] 通过克隆冻结且兼容的基线环境来创建 `.conda/flow-rl`，仅在克隆环境中安装新增的包和测试依赖，并将实际解析出的精确软件包版本记录到 `requirements.lock`，且不改动 `.conda/mlagents-r23`。
- [x] 运行 `.conda/flow-rl/python.exe -m flow_rl.cli.diagnose --build Builds/3DBall/3DBall.exe`，并将输出保存到 `reports/phase2/diagnostics.json`。

### 任务 2：EnvStep 与 UnityEnvAdapter

**文件：**
- 创建：`flow_rl/src/flow_rl/envs/__init__.py`
- 创建：`flow_rl/src/flow_rl/envs/types.py`
- 创建：`flow_rl/src/flow_rl/envs/unity.py`
- 创建：`flow_rl/tests/envs/fakes.py`
- 创建：`flow_rl/tests/envs/test_types.py`
- 创建：`flow_rl/tests/envs/test_unity_adapter.py`

**接口：**
- 产出：

```python
@dataclass(frozen=True)
class EnvStep:
    agent_ids: np.ndarray
    observations: tuple[np.ndarray, ...]
    rewards: np.ndarray
    terminated: np.ndarray
    truncated: np.ndarray

class UnityEnvAdapter:
    def reset(self) -> EnvStep: ...
    def step(self, actions: np.ndarray) -> EnvStep: ...
    def close(self) -> None: ...
    @property
    def pending_agent_ids(self) -> np.ndarray: ...
    @property
    def continuous_action_size(self) -> int: ...
```

- [x] 为 `EnvStep` 编写预期失败的测试，覆盖批次维度相等、Agent ID 为整数、观察/奖励为 float32、标志为布尔值、数据有限，以及 `terminated`/`truncated` 互斥。
- [x] 运行针对性测试，并确认出现预期的模块缺失错误。
- [x] 实现不可变且经过验证的 `EnvStep`，复制公开数组，使调用方无法修改适配器状态。
- [x] 运行针对性测试直至通过。
- [x] 使用完整模拟 LLAPI 形状的 fake 编写预期失败的适配器测试，覆盖：仅有一个 behavior 时自动选择、显式选择 behavior、拒绝多个 behavior、保留元组、先 decision 后 terminal 的行顺序、自然终止/interrupted 映射，以及 Agent 重排/消失/重现时替换待决策 Agent ID。
- [x] 针对错误 shape、错误 dtype、NaN/Inf、超出范围的值，以及正确的 `ActionTuple` 行顺序，编写预期失败的动作测试。
- [x] 编写预期失败的生命周期测试，证明策略或环境步进抛出异常时上下文管理器仍会清理资源，并且重复调用适配器的 `close()` 不会造成问题。
- [x] 使用可注入的环境工厂，实现最小化的原始 LLAPI 适配器。绝不拼接观察流；只针对返回的事件批次，逐传感器合并 decision/terminal 行。
- [x] 运行适配器测试，然后运行全部单元测试。

### 任务 3：按 Agent 组织的 rollout、GAE 与观察归一化

**文件：**
- 创建：`flow_rl/src/flow_rl/data/__init__.py`
- 创建：`flow_rl/src/flow_rl/data/rollout.py`
- 创建：`flow_rl/src/flow_rl/data/gae.py`
- 创建：`flow_rl/src/flow_rl/data/normalization.py`
- 创建：`flow_rl/tests/data/test_rollout.py`
- 创建：`flow_rl/tests/data/test_gae.py`
- 创建：`flow_rl/tests/data/test_normalization.py`

**接口：**
- 产出：`AgentRolloutBuffer.append(transition: Transition) -> None`
- 产出：`AgentRolloutBuffer.trajectories() -> Mapping[int, tuple[Transition, ...]]`
- 产出：`compute_gae(rewards, values, next_values, terminated, truncated, gamma, gae_lambda) -> tuple[np.ndarray, np.ndarray]`
- 产出：`ObservationNormalizer.update/normalize/state_dict/load_state_dict`

- [x] 编写预期失败的 rollout 测试：交错输入顺序依次为 `[20, 10]`、`[10]`、`[20]`，并验证在不假设批次索引固定的情况下，每个 Agent 的轨迹仍保持时间顺序。
- [x] 实现最小化的按 Agent ID 组织的缓冲区，复制数组并验证有限性和 shape；运行针对性测试直至通过。
- [x] 编写手工计算的预期失败 GAE 测试，证明自然终止会阻止 bootstrap，truncation 会使用 `next_value` 但会截断递归轨迹，而普通步骤会进行 bootstrap 并继续递归。
- [x] 实现反向扫描 GAE：使用 `~terminated` 控制 TD bootstrap，使用 `~(terminated | truncated)` 控制轨迹继续；运行针对性测试直至通过。
- [x] 为两个独立观察流、零方差稳定性、有限输出和精确状态往返编写预期失败的归一化器测试。
- [x] 实现数值稳定的并行运行均值/方差以及元组观察流归一化；运行针对性测试和完整单元测试。

### 任务 4：Episode 统计、checkpoint、TensorBoard/CSV 与评估

**文件：**
- 创建：`flow_rl/src/flow_rl/tracking/__init__.py`
- 创建：`flow_rl/src/flow_rl/tracking/episodes.py`
- 创建：`flow_rl/src/flow_rl/tracking/run.py`
- 创建：`flow_rl/src/flow_rl/tracking/checkpoint.py`
- 创建：`flow_rl/src/flow_rl/evaluation/__init__.py`
- 创建：`flow_rl/src/flow_rl/evaluation/runner.py`
- 创建：`flow_rl/tests/tracking/test_episodes.py`
- 创建：`flow_rl/tests/tracking/test_run.py`
- 创建：`flow_rl/tests/tracking/test_checkpoint.py`
- 创建：`flow_rl/tests/evaluation/test_runner.py`

**接口：**
- 产出：`EpisodeTracker.record(step: EnvStep) -> tuple[EpisodeSummary, ...]`
- 产出：`RunLogger.log_episode(summary, environment_steps)`、配置快照、CSV 和 TensorBoard 事件。
- 产出：包含模型/优化器/归一化器/配置元数据的 `save_checkpoint/load_checkpoint`。
- 产出：`evaluate_policy(..., training_worker_id: int, evaluation_worker_id: int)`，并验证两个 worker ID 不相等。

- [x] 针对 Agent 重排、消失、终止和 interrupted 编写预期失败的 episode 测试；断言输出的 return 和 length 精确值能够准确重建每个 episode。
- [x] 实现按 Agent ID 组织的 episode 统计，并运行针对性测试。
- [x] 编写预期失败的运行日志器测试：解析生成的 CSV、重新加载 YAML 配置，并确认关闭后已创建 TensorBoard 事件。
- [x] 实现运行目录创建、配置快照、CSV 追加/刷新和 TensorBoard 标量日志；运行针对性测试。
- [x] 编写预期失败的原子 checkpoint 往返测试，其中包含归一化器统计信息和任意算法元数据。
- [x] 使用 `torch.save/load`，通过临时文件加 `os.replace` 实现 checkpoint 写入；运行针对性测试。
- [x] 为拒绝相同 worker ID、确定性动作顺序、episode 数量限制和异常时清理资源编写预期失败的评估器测试。
- [x] 实现算法无关的评估器，并运行全部单元测试。

### 任务 5：Unity 冒烟运行器与 100k 步阶段验证

**文件：**
- 创建：`flow_rl/src/flow_rl/cli/random_policy.py`
- 创建：`flow_rl/tests/integration/test_unity_smoke.py`
- 创建：`flow_rl/configs/phase2_random_3dball.yaml`
- 创建：`reports/phase2/README.md`
- 修改：`docs/PROJECT_PLAN.md`

**接口：**
- 产出：控制台脚本 `flow-rl-random-policy`
- 产出：机器可读的 `reports/phase2/random-3dball-100k/summary.json`、`episodes.csv`、TensorBoard 事件、配置快照和 Unity Player 日志。

- [x] 编写一个选择性启用的集成测试：导入 `mlagents_envs`，使用专用 worker ID 启动 `Builds/3DBall/3DBall.exe`，执行 reset，提交一个 shape 正确的随机动作批次，推进一步，并始终关闭环境。
- [x] 启用相应环境变量运行集成测试，并确认在运行器/fixture 完成之前测试会失败。
- [x] 使用 `with UnityEnvAdapter(...)` 实现带固定随机种子的随机策略 CLI，对每个事件批次执行有限性检查，按 Agent ID 统计 episode，并通过 `try/finally` 清理日志器。
- [x] 运行冒烟测试直至通过，并确认退出后不存在匹配的 Unity 进程。
- [x] 先运行排除集成测试的完整单元测试套件，然后运行集成冒烟测试。
- [x] 使用隔离的 worker ID 运行随机策略至少 100,000 个 Agent 决策步；验证退出码、摘要步数、统计值有限性、episode 重建、输出文件，以及不存在泄漏的 Unity 进程/端口。
- [x] 重新阅读阶段 2 的每个复选框和退出标准。更新 `docs/PROJECT_PLAN.md` 中的状态、复选框、准确命令、测试数量、环境步数、输出、阻塞项以及所有新决策。没有证据的标准不得标记为完成。

### 审查后加固

- [x] 与官方 `AgentProcessor` 语义保持一致：排除每次 terminal 后的第一个 Decision 基线，包括同一批次内 terminal/新 decision 重叠以及延迟重现的情况。
- [x] 在写入任何配置、CSV、TensorBoard、Unity 日志或摘要产物之前，拒绝使用非空的运行目录。
- [x] 拒绝非 `float32` 的 LLAPI 观察/奖励，而不是静默转换类型。
- [x] 记录 Agent 最大缺席步数；达到目标预算后继续排空，直到再次观察到每个初始 Agent ID；通过一次真实的 100k 运行验证 3DBall 阈值。
- [x] 完成审查修复后，重新运行单元测试、机器诊断、Unity 冒烟测试、依赖检查、耐久性验证、CSV/JSON 验证、Unity 日志检查、进程检查和端口检查。
