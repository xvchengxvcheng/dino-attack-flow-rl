# Dino Attack Structured Parallel Training Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 将正式 `Dinos.unity` 迁移到五流 207 标量 observation、4D 区域动作和四进程异步采样，并为 PPO/FPO 提供共享规格的 Set Transformer 编码器及 Deep Sets 架构基线。

**Architecture:** Unity 以一个主场景训练 bootstrap、一个结构化 observation frame 和五个固定 shape 自定义 Sensor 作为环境事实来源；4D action 经统一分箱后调用现有四边形区域与共享部署服务。Python 保留五流 Tensor，使用版本化协议 manifest 校验环境；Deep Sets 和 Set Transformer 都实现同一 `StateEncoder` 接口，PPO/FPO 分别实例化独立 actor/critic encoder。四个 `UnityEnvAdapter` 由异步进程管理器驱动，采样阶段冻结策略版本，在约 4096 decisions 的 drain barrier 后同步更新。

**Tech Stack:** Unity 6000.3.22f1、ML-Agents 4.0.0、C#、NUnit/EditMode/PlayMode、Python 3.10、ML-Agents Python 1.1.0、PyTorch 2.2.1、NumPy、pytest、YAML/JSON。

**Spec:** `docs/superpowers/specs/2026-08-30-dino-attack-structured-set-observation-action-design.md`；并行运行边界见 `docs/superpowers/specs/2026-08-29-dino-attack-headless-parallel-training-design.md`。

## Global Constraints

- 正式 observation 版本固定为 `dino_attack_structured_set_v1`，流顺序与 shape 固定为 Global `(3,)`、Walls `(6,5)`、Guards `(8,7)`、Houses `(8,6)`、Dinos `(10,7)`，合计 207 个标量。
- 正式 action 固定为 4 个 `[-1,1]` 连续值：Z1–Z5 等宽分箱、`u`、`v`、`Wait/Velociraptor/Pachycephalosaurus/T-Rex` 等宽分箱；`Wait` 忽略前三维。
- `MaxDinos=10` 是生成前硬限制；第 11 次部署必须失败，不能先生成后截断 observation。
- 当前 episode 上限为 50 秒模拟时间，胜利、恐龙全灭和 50 秒失败均为 natural terminal；进程、通信、启动或重载异常才是 truncated。
- 决策周期固定为 25 Academy steps，训练默认 `timeScale=20`、`num_envs=4`、rollout target 约 4096 decisions、每个进程最多自动重启 2 次。
- Deep Sets 只作 encoder ablation；正式 PPO/FPO 比较使用相同 Set Transformer 规格，actor 与 critic 使用独立参数。
- FPO 的 flow time `τ` 使用独立 32 维 embedding，不得复用环境 `remaining_time` FiLM。
- 不引入 action chunk，不修改攻击、NavMesh、地貌速度、恐龙互相避障、奖励来源或 Human 模式流程。
- 不修改 `fpo/`、`ReinFlow/`、`PolicyFlow/`、Packages、其他 ProjectSettings、历史 `DinoAttackTraining.unity` 或冻结基线环境。
- Unity 场景只通过连接中的 Unity Editor/Workbench 和确定性 installer 修改，不手改 `.unity` YAML。
- 按用户既有要求不创建逐任务提交、提交切片、自动暂存或自动 commit；每个任务只检查本任务路径的 diff，保留用户既有 dirty 内容。
- 实施门禁：用户已于 2026-08-30 确认 `TASK-8.10-ART/GAMEPLAY` 最终视觉/玩法复核结束；修改 `Dinos.unity` 前仍须记录新的实施前 scene hash。

## File Responsibility Map

### Unity

- `Training/Core/DinoTrainingObservationLayout.cs`：唯一 observation 版本、五流 shape、字段索引和上限。
- `Training/Core/DinoTrainingActionCodec.cs`：4D 有限值校验、等宽分箱和解码结果。
- `Training/Core/DinoStructuredObservationFrame.cs`：五个固定数组及严格有限性/长度校验，不引用场景对象。
- `Training/Runtime/DinoStructuredObservationBuilder.cs`：从正式场景对象构造 frame，处理稳定 Wall/House 行、Guard/Dino padding 和上限错误。
- `Training/Runtime/DinoStructuredObservationSensorComponent.cs`：创建五个具名 rank-1/rank-2 `ISensor`，保持 ML-Agents 流分离。
- `Training/Runtime/DinoTrainingAgent.cs`：奖励、4D action 执行、自然终止和 reload；不再构造旧 504 向量。
- `Training/Runtime/DinoTrainingBootstrap.cs`：只认显式 `--dino-training`，配置 seed、表现层开关、自动 Running 和 Agent 生命周期。
- `Training/Runtime/DinoTrainingDecisionScheduler.cs`：首次决策及每 25 Academy step 决策。
- `Training/Runtime/DinoTrainingRunContext.cs`：environment/episode seed、episode index 和 process generation。
- `Training/Editor/DinoFormalTrainingSceneInstaller.cs`：确定性接线 `Dinos.unity` 的训练根、Agent、Sensors 和 4D BehaviorParameters。
- `Training/Editor/DinoFormalTrainingPlayerBuilder.cs`：只构建正式 `Dinos.unity` Player，不覆盖历史训练构建。

### Python

- `flow_rl/envs/dino_protocol.py`：协议 manifest、shape/field/action/geometry 校验及 checkpoint metadata。
- `flow_rl/configs/dino_attack_structured_set_v1.yaml`：从冻结 Unity 几何复制的唯一 Python 协议配置。
- `flow_rl/models/state_encoder.py`：共同 `StateEncoder` 接口及 flat fallback，保护现有 3DBall。
- `flow_rl/models/dino_deep_sets.py`：Deep Sets baseline。
- `flow_rl/models/dino_set_transformer.py`：ISAB、FiLM、Region Query、分离 Cross-Attention 和 256 维 state。
- `flow_rl/envs/parallel_unity.py`：四个进程的异步 step、重启、generation、关闭和端口清理。
- `flow_rl/training/versioned_collector.py`：按 env/agent/episode/policy version 隔离并在 barrier 排空 pending action。
- 现有 Gaussian/FPO actor、critic、trainer 与 checkpoint 模块：接受 encoder factory 和协议 metadata，同时保持旧 flat 配置兼容。

---

### Task 1: Freeze the 4D Action and Five-Stream Core Contract

**Files:**
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Core/DinoTrainingObservationLayout.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Core/DinoTrainingActionCodec.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Training/Core/DinoStructuredObservationFrame.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Deployment/DeploymentResult.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Player/DinoSpawner.cs`
- Modify: `dino-attack/Assets/Tests/Editor/DinoTrainingActionCodecTests.cs`
- Create: `dino-attack/Assets/Tests/Editor/DinoStructuredObservationFrameTests.cs`

**Interfaces:**
- Produces: `DinoTrainingObservationLayout.ProtocolVersion`, five `(rows, fields)` constants, `TotalScalarCount=207`, `MaxDinos=10`.
- Produces: `DecodedDinoTrainingAction(bool isWait, int zoneIndex, int dinoIndex, Vector2 relativeUv)`.
- Produces: `DinoStructuredObservationFrame` with `float[] Global/Walls/Guards/Houses/Dinos` and `Validate()`.

- [x] **Step 1: Replace the old contract assertions with failing v1 assertions**

```csharp
[Test]
public void StructuredProtocolHasFiveStableStreams()
{
    Assert.That(DinoTrainingObservationLayout.ProtocolVersion,
        Is.EqualTo("dino_attack_structured_set_v1"));
    Assert.That(DinoTrainingObservationLayout.GlobalShape, Is.EqualTo(new[] { 3 }));
    Assert.That(DinoTrainingObservationLayout.WallShape, Is.EqualTo(new[] { 6, 5 }));
    Assert.That(DinoTrainingObservationLayout.GuardShape, Is.EqualTo(new[] { 8, 7 }));
    Assert.That(DinoTrainingObservationLayout.HouseShape, Is.EqualTo(new[] { 8, 6 }));
    Assert.That(DinoTrainingObservationLayout.DinoShape, Is.EqualTo(new[] { 10, 7 }));
    Assert.That(DinoTrainingObservationLayout.TotalScalarCount, Is.EqualTo(207));
}

[TestCase(-1f, 0)]
[TestCase(-0.6f, 1)]
[TestCase(-0.2f, 2)]
[TestCase(0.2f, 3)]
[TestCase(0.6f, 4)]
[TestCase(1f, 4)]
public void RegionBinsUseFrozenUpperBoundaryRule(float value, int expected)
{
    Assert.That(DinoTrainingActionCodec.TryDecode(
        new[] { value, 0f, 0f, 1f }, out var decoded), Is.True);
    Assert.That(decoded.ZoneIndex, Is.EqualTo(expected));
}
```

- [x] **Step 2: Run the focused Core tests and confirm RED**

Run the connected Unity Editor EditMode filter `DinoAttack.Training.Core.Tests`.

Expected: failures reference missing `ProtocolVersion`, `GlobalShape`, `TotalScalarCount` and the old `ActionSize=6` decoder.

- [x] **Step 3: Implement the exact layout and decoder**

```csharp
public const string ProtocolVersion = "dino_attack_structured_set_v1";
public const int MaxWalls = 6, WallFields = 5;
public const int MaxGuards = 8, GuardFields = 7;
public const int MaxHouses = 8, HouseFields = 6;
public const int MaxDinos = 10, DinoFields = 7;
public const int GlobalFields = 3;
public const int TotalScalarCount = 3 + 6 * 5 + 8 * 7 + 8 * 6 + 10 * 7;
public const int ActionSize = 4;

private static int DecodeBin(float value, int count)
{
    float t = Mathf.Clamp01((value + 1f) * 0.5f);
    return Mathf.Min(count - 1, Mathf.FloorToInt(t * count));
}
```

Decode `a3` as `0=Wait, 1=Velociraptor, 2=Pachycephalosaurus, 3=T-Rex`; for Wait set `dinoIndex=-1` and `relativeUv=Vector2.zero`. For placement, map `a1/a2` to `[0,1]` and set `dinoIndex=choice-1`.

- [x] **Step 4: Add the pre-spawn cap to the shared deployment entry**

Add `DeploymentFailureReason.UnitCapReached`. Before `DeploymentService.Evaluate` and before `SpawnAuthorizedDino`, reject when `RoundManager.Instance.AliveDinos >= DinoTrainingObservationLayout.MaxDinos`; emit the normal `OnDeploymentEvaluated` event and do not subtract food.

```csharp
if (RoundManager.Instance != null &&
    RoundManager.Instance.AliveDinos >= DinoTrainingObservationLayout.MaxDinos)
{
    result = DeploymentResult.Rejected(DeploymentFailureReason.UnitCapReached);
    OnDeploymentEvaluated?.Invoke(result);
    return false;
}
```

- [x] **Step 5: Implement and test strict observation-frame storage**

`DinoStructuredObservationFrame` allocates exactly `3/30/56/48/70` floats. `Validate()` rejects wrong lengths and any NaN/Inf rather than sanitizing them silently.

```csharp
Assert.That(frame.Streams.Select(stream => stream.Length),
    Is.EqualTo(new[] { 3, 30, 56, 48, 70 }));
frame.Dinos[4] = float.NaN;
Assert.Throws<InvalidOperationException>(() => frame.Validate());
```

- [x] **Step 6: Run only the two focused EditMode fixtures**

Run filters `DinoTrainingActionCodecTests` and `DinoStructuredObservationFrameTests`.

Expected: all cases pass; no scene or Player build runs in this task.

### Task 2: Build Stable Scene Observations and Five ML-Agents Sensors

**Files:**
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoStructuredObservationBuilder.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoStructuredObservationSensorComponent.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoStructuredObservationSensor.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingAgent.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Enemy/Defense/SessionDefenseLayoutController.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/RoundManagement/RoundManager.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`

**Interfaces:**
- Produces: `DinoStructuredObservationBuilder.BuildFrame() : DinoStructuredObservationFrame`.
- Produces: sensor names `00_Global`, `01_Walls`, `02_Guards`, `03_Houses`, `04_Dinos` with shapes from Task 1.
- Consumes: six stable Wall slots, eight stable houses, at most eight live guards and at most ten live dinos.

- [x] **Step 1: Add a reflection-based failing real-scene observation test**

Load `Dinos`, locate the training observation builder by type name, call `BuildFrame`, and assert:

```csharp
private static float[] ReadStream(object frame, string propertyName) =>
    (float[])frame.GetType().GetProperty(propertyName).GetValue(frame);

private static IEnumerable<float[]> ReadRows(object frame, string propertyName, int width) =>
    ReadStream(frame, propertyName).Chunk(width);

Assert.That(ReadStream(frame, "Global"), Has.Length.EqualTo(3));
Assert.That(ReadStream(frame, "Walls"), Has.Length.EqualTo(30));
Assert.That(ReadRows(frame, "Walls", 5).All(row => row[0] == 1f), Is.True);
Assert.That(ReadStream(frame, "Guards").Chunk(7).Count(row => row[0] == 1f), Is.EqualTo(8));
Assert.That(ReadRows(frame, "Houses", 6).All(row => row[0] == 1f), Is.True);
Assert.That(ReadStream(frame, "Dinos").Chunk(7).All(row => row[0] == 0f), Is.True);
```

Expected RED: the builder/sensor types do not exist.

- [x] **Step 2: Expose stable read-only gameplay collections**

Add `AllWallSlots` to `SessionDefenseLayoutController`, and `SessionHouses`, `ActiveDinoUnits`, `RunningElapsedSeconds` and `SessionDurationSeconds=50f` to `RoundManager`. Return `IReadOnlyList<T>`; do not expose mutable lists.

```csharp
public IReadOnlyList<WallSpawnSlot> AllWallSlots => Slots;
public IReadOnlyList<VillageHouse> SessionHouses => Houses;
public IReadOnlyList<Dino> ActiveDinoUnits => ActiveDinos;
public float RunningElapsedSeconds => RunningElapsedSecondsValue;
```

Rename the existing private elapsed field to `RunningElapsedSecondsValue` to avoid a field/property collision.

- [x] **Step 3: Implement `BuildFrame()` with exact row semantics**

- Global: remaining time, clipped nonnegative food ratio, previous action validity.
- Walls: sort all six slots by `StableId`; mask always 1; use the corresponding active wall if present, else active/health 0; position comes from the slot.
- Guards: live wall guards first only for deterministic collection, then live ground guards; encode Archer/Mage only, `is_wall_guard` from the active wall-guard set; pad zeros to eight.
- Houses: sort eight houses by `StableHouseId`; mask always 1 even when destroyed.
- Dinos: use `RoundManager.ActiveDinoUnits`, fail if count exceeds ten, sort only for reproducible serialization by stable instance ID; do not add distance, zone or terrain fields.
- Normalize x/z with frozen map bounds `x=[-55.5,48.5]`, `z=[-53,29]`; normalize House max HP by 325 and food by the configured frozen food scale.

- [x] **Step 4: Implement five custom sensors**

`DinoStructuredObservationSensorComponent.CreateSensors()` returns five sensors. Rank-2 specs use `new ObservationSpec(new InplaceArray<int>(rows, fields), new InplaceArray<DimensionProperty>(DimensionProperty.None, DimensionProperty.None))`; sensor names enforce stable sorting.

```csharp
public int Write(ObservationWriter writer)
{
    float[] values = Source.GetFrameForAcademyStep(Academy.Instance.StepCount).GetStream(Stream);
    if (Rows == 1)
    {
        for (int field = 0; field < Fields; field++) writer[field] = values[field];
    }
    else
    {
        for (int row = 0; row < Rows; row++)
            for (int field = 0; field < Fields; field++)
                writer[row, field] = values[row * Fields + field];
    }
    return values.Length;
}
```

Cache one frame by `Academy.Instance.StepCount`, so all five sensors read one snapshot even if `Write()` is called more than once. `Update()` and `Reset()` do not mutate gameplay state; a changed Academy step replaces the cached frame.

- [x] **Step 5: Remove the old 504-vector path from the formal Agent**

`DinoTrainingAgent.CollectObservations(VectorSensor sensor)` becomes empty; all observation data comes from the five custom sensors. Delete obsolete distance/cost/`can_buy_*` helpers and the `LastObservedFood` delta. Keep reward fields and `LastActionWasValid`, exposed read-only to the builder.

- [x] **Step 6: Run the focused real-scene PlayMode test**

Expected: five streams have correct counts, all numbers are finite, Wall/House masks remain valid, empty Dino rows are zero, and no Cannoneer column exists.

### Task 3: Execute 4D Region Actions and Correct Natural Terminals

**Files:**
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingAgent.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingSceneReloader.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingDecisionScheduler.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingRunContext.cs`
- Modify: `dino-attack/Assets/LlamAcademy/Dinos/RoundManagement/GameSessionRestartService.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`

**Interfaces:**
- Consumes: five ordered `DinoDeploymentZone` references and decoded `(zoneIndex, uv, dinoIndex)`.
- Produces: `DinoTrainingRunContext.EnvironmentId/EpisodeIndex/EpisodeSeed/ProcessGeneration`; Python collector alone owns `policy_version`.
- Produces: exact Academy-step schedule: immediate Running decision, then steps `25,50,75...`.

- [x] **Step 1: Add failing PlayMode cases for action execution and terminal outcome**

Test the four corners and center of one real zone through the Agent action path, a Wait action that performs no position lookup, the tenth/eleventh dino boundary, and a 50-second failure with dinos still alive.

```csharp
Assert.That(roundManager.PlayerWon, Is.False);
Assert.That(reloader.IsReloadPending, Is.True);
```

Expected RED: current Agent calls rectangular `DinoTrainingArea`, uses old 6D fields, contains 180-second/360-decision watchdogs, and infers win from `AliveDinos > 0`.

- [x] **Step 2: Resolve deployment through the selected quadrilateral**

Add serialized `DinoDeploymentZone[] Zones` in strict `Z1..Z5` order. For a non-Wait action call `Zones[zoneIndex].TryResolveRelative(relativeUv, out candidate)` and then `DinoSpawner.TryDeploy`. Reject missing/misordered/invalid geometry before spawning.

- [x] **Step 3: Remove gameplay watchdog competition and read the real outcome**

Delete `MaxEpisodeSeconds`, `MaxDecisionSteps` and their `FixedUpdate` interruption path from the formal Agent. In `OnGameStateChange`, when entering `Ending`, call:

```csharp
bool won = RoundManager.Instance != null && RoundManager.Instance.PlayerWon;
ConcludeEpisode(won, interrupted: false);
```

Only an explicit infrastructure callback may call `EpisodeInterrupted()`.

- [x] **Step 4: Implement deterministic episode seeds and explicit reload seed**

Use a platform-stable integer hash over `(baseSeed, environmentIndex, episodeIndex)`. Add `GameSessionRestartService.TryScheduleReloadWithSeed(int nextSeed, float delay)`; the training reloader increments the episode index, stores the next seed, waits at least 0.15 realtime seconds after `EndEpisode`, then reloads.

- [x] **Step 5: Replace floating-time decisions with Academy step counts**

On the first `Running` FixedUpdate request one decision and save `Academy.Instance.StepCount`. Request again only when the integer difference reaches 25. Never call `RequestAction()` between decisions, so the strategic action is not repeated.

- [x] **Step 6: Run the focused action/lifecycle PlayMode cases**

Expected: Wait is legal; real zone mapping matches `TryResolveRelative`; the 11th deployment fails without food change; 50-second failure is natural; decision requests occur at exact Academy-step intervals.

### Task 4: Add Explicit Main-Scene Training Mode and Formal Player Build

**Files:**
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingLaunchOptions.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Training/Runtime/DinoTrainingBootstrap.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Training/Editor/DinoFormalTrainingSceneInstaller.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Training/Editor/DinoFormalTrainingPlayerBuilder.cs`
- Create: `dino-attack/Assets/LlamAcademy/Dinos/Training/Core/DinoFormalTrainingBuildLayout.cs`
- Modify: `dino-attack/Assets/Tests/Editor/DinoTrainingActionCodecTests.cs`
- Modify: `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`
- Modify through Unity Editor: `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`

**Interfaces:**
- Produces: exact flag `--dino-training`, integer args `--dino-base-seed`, `--dino-environment-index`, `--dino-process-generation`.
- Produces: formal build scene list containing only `Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`.

- [x] **Step 1: Add failing parser, build-layout and Human-mode tests**

```csharp
Assert.That(DinoTrainingLaunchOptions.TryParse(
    new[] { "game.exe", "--dino-training", "--dino-base-seed", "7", "--dino-environment-index", "2" },
    out var options), Is.True);
Assert.That(options.BaseSeed, Is.EqualTo(7));
Assert.That(DinoFormalTrainingBuildLayout.CreateSceneList(),
    Is.EqualTo(new[] { "Assets/LlamAcademy/Dinos/Scenes/Dinos.unity" }));
```

Also verify arguments without the exact flag leave Logo/Start, UIDocument, Camera and Human input enabled.

- [x] **Step 2: Implement exact launch parsing**

Reject duplicate, missing or non-integer values. Do not infer training from `Application.isBatchMode`, `no_graphics`, disabled rendering or test execution.

- [x] **Step 3: Implement component-level presentation shutdown**

In training mode disable Camera/CameraControl, UIDocument, player deployment input, Renderer, Light, Volume, ParticleSystem and audio components. Preserve GameObjects and all Collider/Trigger/NavMesh/Agent/RoundManager/DinoSpawner/EnemyAI/health/death/resource/zone/terrain components. Enable the formal Agent and sensor component, apply the episode seed before defense controllers initialize, then invoke the same `RoundManager.StartRound()` entry after Setup is ready.

- [x] **Step 4: Implement the deterministic scene installer**

The installer creates one `/Training/FormalTrainingEnvironment` root, wires the existing five zones in `DeploymentZoneId` order, three DinoSO assets in frozen action order, controllers, map bounds and restart service, and configures `BehaviorParameters` with continuous action size 4 and built-in vector observation size 0. A second installer run must be a no-op.

- [x] **Step 5: Implement the formal Player builder without touching the legacy builder**

Build only `Dinos.unity` to `Builds/DinoAttack/DinoAttack.exe`. Keep `DinoTrainingBuildLayout` and `Builds/DinoAttackTrainingOnly` as historical artifacts.

- [x] **Step 6: Run the installer in the connected Unity Editor and save the scene**

Record the pre-install scene hash, run installer twice, verify the second run is clean, and save through Unity. The visual-review gate was satisfied on 2026-08-30.

- [x] **Step 7: Run only parser/layout tests, one Human-mode PlayMode case and one training-mode PlayMode case**

Expected: Human UI remains; exact training flag hides presentation components without disabling gameplay roots; training enters Running automatically; BehaviorParameters reports five sensors and action size 4.

### Task 5: Define the Python Protocol Manifest and Strict LLAPI Validation

**Files:**
- Create: `flow_rl/src/flow_rl/envs/dino_protocol.py`
- Modify: `flow_rl/src/flow_rl/envs/unity.py`
- Create: `flow_rl/configs/dino_attack_structured_set_v1.yaml`
- Create: `flow_rl/tests/envs/test_dino_protocol.py`
- Modify: `flow_rl/tests/envs/test_unity_adapter.py`

**Interfaces:**
- Produces: `DinoProtocol.from_yaml(path)`, `.validate_shapes(shapes)`, `.validate_action_size(size)`, `.checkpoint_metadata()`.
- Produces: `UnityEnvAdapter.observation_shapes` after reset.

- [x] **Step 1: Write failing manifest and adapter tests**

```python
EXPECTED_SHAPES = ((3,), (6, 5), (8, 7), (8, 6), (10, 7))

def test_protocol_rejects_legacy_interface(protocol):
    with pytest.raises(ValueError, match="observation shapes"):
        protocol.validate_shapes(((504,),))
    with pytest.raises(ValueError, match="action size"):
        protocol.validate_action_size(6)
```

Add a fake LLAPI behavior with the five observation specs and action size 4; verify reset exposes the same order and retains rank-2 arrays.

- [x] **Step 2: Implement the frozen YAML**

Store field names, maxima, map bounds `x=[-55.5,48.5]`, `z=[-53,29]`, food/house scales, action orders and all five A/B/C/D vertices copied from `reports/phase8/quadrilateral-terrain-zones/geometry.json`. Store its SHA-256 in the run metadata, not as a hard-coded constant in source.

- [x] **Step 3: Implement strict protocol parsing and metadata**

Require exact keys and exact stream order. `checkpoint_metadata()` returns only JSON-serializable tuples/lists/scalars and includes protocol version, shapes, fields, maxima, normalized vertices, bin orders and manifest SHA-256.

- [x] **Step 4: Add optional protocol validation to `UnityEnvAdapter`**

The generic adapter remains usable for 3DBall when `protocol=None`. For Dino runs, validate shapes and continuous size immediately after behavior selection; fail before policy construction on 504/6 or reordered streams.

- [x] **Step 5: Run focused Python environment tests**

Run:

```powershell
.conda/flow-rl/python.exe -m pytest flow_rl/tests/envs/test_dino_protocol.py flow_rl/tests/envs/test_unity_adapter.py -q
```

Expected: all pass; no Unity process starts.

### Task 6: Implement Deep Sets and the Common StateEncoder Boundary

**Files:**
- Create: `flow_rl/src/flow_rl/models/state_encoder.py`
- Create: `flow_rl/src/flow_rl/models/dino_deep_sets.py`
- Create: `flow_rl/tests/models/dino_fixtures.py`
- Create: `flow_rl/tests/models/test_dino_deep_sets.py`
- Modify: `flow_rl/src/flow_rl/models/__init__.py`

**Interfaces:**
- Produces: `StateEncoder.output_size`, `StateEncoder.observation_shapes`, `forward(tuple[Tensor,...]) -> Tensor`.
- Produces: `DeepSetsDinoEncoder(output_size=256)`.

- [x] **Step 1: Write failing shape, permutation and empty-set tests**

In `dino_fixtures.py`, define `make_structured_batch(batch_size: int) -> tuple[torch.Tensor, ...]` with exact five shapes and `permute_valid_rows(observations, stream_index, permutation)` that permutes complete entity rows without changing padding. Import those helpers in both encoder test files.

```python
encoder = DeepSetsDinoEncoder()
batch = make_structured_batch(batch_size=3)
state = encoder(batch)
assert state.shape == (3, 256)
assert torch.isfinite(state).all()

permuted = permute_valid_rows(batch, stream_index=4, permutation=(1, 0, 2, 3, 4, 5, 6, 7, 8, 9))
torch.testing.assert_close(encoder(batch), encoder(permuted), atol=1e-6, rtol=1e-6)
```

Set all Guard/Dino masks to zero and assert finite output.

- [x] **Step 2: Implement shared validation utilities**

Validate exactly five float32 finite tensors with batch-aligned shapes. Return masks from column 0 without normalizing them. Keep the existing flat encoder as `FlatMLPStateEncoder` for unchanged 3DBall configs.

- [x] **Step 3: Implement the approved Deep Sets capacities**

Use entity MLPs `5/7/6/7 -> 64 -> 64`, Global `3 -> 32 -> 64`, masked mean and max per category, concatenated `576 -> 256 -> 256`, SiLU. Empty max/mean returns zeros, never `-inf` or division by zero.

- [x] **Step 4: Run the focused Deep Sets tests**

Run:

```powershell
.conda/flow-rl/python.exe -m pytest flow_rl/tests/models/test_dino_deep_sets.py -q
```

### Task 7: Implement the Final Set Transformer Encoder

**Files:**
- Create: `flow_rl/src/flow_rl/models/dino_set_transformer.py`
- Create: `flow_rl/tests/models/test_dino_set_transformer.py`
- Modify: `flow_rl/src/flow_rl/models/state_encoder.py`

**Interfaces:**
- Produces: `SetTransformerDinoEncoder(protocol, d_model=64, heads=4, inducing_points=8, layers=2, dropout=0.05, output_size=256)`.
- Consumes: normalized Region vertices from `DinoProtocol`, registered as non-trainable buffers.

- [x] **Step 1: Write failing forward, permutation, empty-set and geometry-buffer tests**

Assert output `(batch,256)`, finite empty Guard/Dino behavior, row-permutation invariance, five query states and `region_vertices.requires_grad=False`. Change one vertex in a copied protocol and assert encoder state/checkpoint metadata differs.

- [x] **Step 2: Implement typed tokens, ISAB and masked attention**

Project Wall/Guard/House/Dino through separate `input_fields -> 64 -> 64` SiLU MLPs. Add learned Wall/Guard/House type embeddings before concatenating the defense branch; do not add slot positional embeddings. Apply two ISAB layers to Defense and two to Dinosaur with `d_model=64`, 4 heads, 8 learned inducing points and configured dropout `0.05`. Use `nn.MultiheadAttention(batch_first=True)` with key-padding masks. Before a fully masked branch reaches attention, replace it with one learned empty token and an unmasked key-padding entry.

- [x] **Step 3: Implement separate time FiLM branches**

Feed only `global[:, 0:1]` to Time MLP. Create independent defense and dinosaur `gamma/beta`; zero-initialize the final FiLM projections so the initial transform is identity.

- [x] **Step 4: Implement Region Queries and separate cross-attention**

Use five learned region embeddings plus `8 -> 32 -> 64` geometry MLP. Compute defense and dinosaur cross-attention separately, fuse `192 -> 128 -> 64`, concatenate five region states plus Global64, then `384 -> 256 -> 256`.

- [x] **Step 5: Run the focused Set Transformer tests**

Run:

```powershell
.conda/flow-rl/python.exe -m pytest flow_rl/tests/models/test_dino_set_transformer.py -q
```

### Task 8: Wire Independent Encoders into PPO/FPO and Version Checkpoints

**Files:**
- Modify: `flow_rl/src/flow_rl/models/gaussian.py`
- Modify: `flow_rl/src/flow_rl/models/critic.py`
- Modify: `flow_rl/src/flow_rl/models/flow.py`
- Modify: `flow_rl/src/flow_rl/models/policy.py`
- Modify: `flow_rl/src/flow_rl/models/fpo_policy.py`
- Modify: `flow_rl/src/flow_rl/training/config.py`
- Modify: `flow_rl/src/flow_rl/training/fpo_config.py`
- Modify: `flow_rl/src/flow_rl/training/trainer.py`
- Modify: `flow_rl/src/flow_rl/training/fpo_trainer.py`
- Modify: `flow_rl/src/flow_rl/tracking/checkpoint.py`
- Create: `flow_rl/tests/models/test_dino_actor_critic.py`
- Modify: `flow_rl/tests/tracking/test_checkpoint.py`
- Modify: `flow_rl/tests/tracking/test_flow_checkpoint.py`

**Interfaces:**
- Consumes: a callable `encoder_factory() -> StateEncoder`; actor and critic call it separately.
- PPO produces 4D tanh-Gaussian actions from state256.
- FPO velocity consumes `[state256, latent_action4, flow_time32]` and produces 4D velocity.

- [x] **Step 1: Write failing independence and forward tests**

```python
factory = lambda: build_state_encoder("set_transformer", protocol)
ppo = GaussianActorCritic(
    observation_shapes=protocol.observation_shapes,
    action_size=4,
    hidden_sizes=(256, 256),
    encoder_factory=factory,
)
assert ppo.actor.encoder is not ppo.critic.encoder
assert ppo.actor.sample(obs, deterministic=False).actions.shape == (2, 4)

fpo = FlowActorCritic(
    observation_shapes=protocol.observation_shapes,
    action_size=4,
    state_size=256,
    time_embedding_size=32,
    velocity_hidden_sizes=(256, 256),
    critic_hidden_sizes=(256, 256),
    encoder_factory=factory,
)
assert fpo.actor.state_encoder is not fpo.critic.encoder
velocity = fpo.actor(obs, torch.zeros(2, 4), torch.full((2, 1), 0.5))
assert velocity.shape == (2, 4)
```

Register a hook proving Set Transformer FiLM receives environment time from Global while `FlowTimeEmbedding` separately receives the supplied `τ`.

- [x] **Step 2: Generalize existing models without breaking flat 3DBall**

Add optional encoder instances/factories. If absent, construct the current flat MLP behavior byte-for-byte in structure and key naming where possible; existing 3DBall tests must remain valid. With an external structured encoder, the Gaussian actor applies a separate `256 -> 256 -> 256` SiLU policy head before its 4D mean/log-std output, and the critic applies an independent `256 -> 256 -> 256 -> 1` value head. FPO velocity head input is 292 and hidden sizes are `256,256`.

- [x] **Step 3: Add backward-compatible config fields**

Add `encoder_type`, `protocol_path`, Set Transformer hyperparameters and `normalize_observations`. Existing YAML files omit them and receive defaults `encoder_type=flat`, `protocol_path=None`, `normalize_observations=True`. Dino configs require `encoder_type=set_transformer` for formal comparisons.

- [x] **Step 4: Preserve mask semantics during preprocessing**

For structured Dino runs, bypass the generic running-stat normalizer because Unity fields are already normalized and masks/one-hot values must remain exact. Save an identity normalizer state containing the protocol hash; keep the existing `ObservationNormalizer` for flat 3DBall.

- [x] **Step 5: Extend checkpoint validation**

Require structured checkpoints to contain protocol metadata, manifest hash, action bin order, region vertices and encoder config. Loading 504/6, changed stream order, changed vertices or mismatched encoder type raises `ValueError` before loading model weights.

- [x] **Step 6: Run model, checkpoint and existing Gaussian/FPO regressions**

Run:

```powershell
.conda/flow-rl/python.exe -m pytest flow_rl/tests/models/test_dino_actor_critic.py flow_rl/tests/models/test_gaussian.py flow_rl/tests/models/test_flow.py flow_rl/tests/models/test_fpo_policy.py flow_rl/tests/tracking/test_checkpoint.py flow_rl/tests/tracking/test_flow_checkpoint.py -q
```

### Task 9: Add Four-Process Async Sampling and a Policy-Version Barrier

**Files:**
- Create: `flow_rl/src/flow_rl/envs/parallel_unity.py`
- Create: `flow_rl/src/flow_rl/training/versioned_collector.py`
- Create: `flow_rl/tests/envs/test_parallel_unity.py`
- Create: `flow_rl/tests/training/test_versioned_collector.py`
- Modify: `flow_rl/src/flow_rl/training/trainer.py`
- Modify: `flow_rl/src/flow_rl/training/fpo_trainer.py`

**Interfaces:**
- Produces: `ParallelEnvEvent(environment_id, process_generation, step, error)`.
- Produces: `AsyncUnityVectorEnv.submit_actions()`, `poll_ready()`, `drain()`, `restart_environment()`, `close()`.
- Produces: `VersionedCollector.begin(version)`, `record(event, action_info)`, `seal_and_bootstrap(values)`, `build_batch()`.

- [x] **Step 1: Write deterministic fake-environment tests**

Use four fake adapters with different completion order and one injected failure. Assert events arrive in completion order, Agent ID `7` in env0 is distinct from Agent ID `7` in env1, generation increments on restart, third consecutive failure aborts, and `close()` closes every adapter.

- [x] **Step 2: Implement one-thread-per-blocking-adapter stepping**

Use a bounded `ThreadPoolExecutor(max_workers=num_envs)` because each LLAPI adapter owns a separate Unity process. Never call the same adapter concurrently. Keep worker ID, environment seed, log directory and generation in `EnvironmentHandle`.

- [x] **Step 3: Implement the frozen policy-version barrier**

After accepted transitions reach the target, stop submitting new actions, drain all already submitted actions, reject mixed `policy_version`, bootstrap nonterminal fragments from their returned next observation, do not bootstrap natural terminals, then advance the version only after the optimizer update.

- [x] **Step 4: Implement failure semantics**

If a process fails without a final observation, discard its incomplete episode fragment; emit infrastructure-truncated diagnostics without synthesizing a terminal observation. Restart with the same worker ID, increment generation and derive the next episode seed. Other environments continue.

- [x] **Step 5: Run the two focused async tests**

Run:

```powershell
.conda/flow-rl/python.exe -m pytest flow_rl/tests/envs/test_parallel_unity.py flow_rl/tests/training/test_versioned_collector.py -q
```

### Task 10: Add Dino PPO/FPO Configurations and Focused End-to-End Validation

**Files:**
- Create: `flow_rl/configs/phase8_dino_ppo_smoke.yaml`
- Create: `flow_rl/configs/phase8_dino_fpo_smoke.yaml`
- Modify: `flow_rl/src/flow_rl/cli/train_ppo.py`
- Modify: `flow_rl/src/flow_rl/cli/train_fpo.py`
- Create: `reports/phase8/structured-parallel-training/validation.md`
- Create: `reports/phase8/structured-parallel-training/protocol-manifest.json`
- Create: `flow_rl/scripts/dino_v2_single_update_smoke.py`
- Create: `flow_rl/tests/integration/test_dino_v2_single_update_smoke_script.py`
- Modify: `docs/PROJECT_PLAN.md`
- Modify: this plan file

**Interfaces:**
- PPO/FPO smoke configs both use `Dinos.unity`, action4, Set Transformer, `num_envs=4`, `rollout_size=4096`, `timeScale=20`, 25-step decisions and the same protocol file.

- [x] **Step 1: Build the formal Player after one final Unity compile**

Use `DinoFormalTrainingPlayerBuilder` through the connected Unity Editor. Record Unity version, `Dinos.unity` hash, Player hash, protocol export hash and compile errors. Do not run a second unrelated Player build.

- [ ] **Step 2: Run one protocol/terminal/reload validation sequence**

Launch with `--dino-training`. Verify one victory, one all-dinos-dead loss and one 50-second loss appear as natural TerminalSteps before reload; inject one infrastructure interruption as truncated. Continue to 20 total scene reloads and check resource/entity/seed/Agent reset plus zero duplicate subscriptions.

- [x] **Step 3: Run one four-environment PPO rollout**

Run the PPO smoke until one complete approximately 4096-decision versioned rollout and one optimizer update finish. Assert all four environment IDs contributed, pending actions drained, no mixed version exists, every stream is finite with exact shape, and shutdown leaves no Player process or worker port.

- [x] **Step 4: Run one finite FPO forward/update smoke**

Use the same build/protocol/parallel settings with a reduced single-update smoke. Assert 4D finite actions, finite velocity/loss/value/gradient metrics and distinct environment-time/flow-time inputs. Do not run a long formal training seed in `TASK-8.11`.

- [ ] **Step 5: Run the minimum scoped regression set**

Run the focused Unity fixtures from Tasks 1–4 and the Python files from Tasks 5–9. Do not run the full Unity suite, 100-episode evaluation, Player performance matrix, four-resolution UI matrix or formal PPO/FPO training.

- [x] **Step 6: Freeze evidence and update the main plan**

Record exact commands, test counts, terminal/truncated counts, 20-reload result, four-env rollout counts, build/scene/protocol hashes, process cleanup and known limitations in `validation.md`. Mark `TASK-8.11` complete only if all required evidence exists; otherwise leave it `进行中` with the exact blocker. Do not stage or commit files automatically.

**2026-08-30 v2 Task 5 retained current-map integration:** Steps 1, 3, 4 and 6 are complete. Scene SHA-256 is `ECFD823C14F5EC9BAE061D56B50D8A809EED3AACBABC12D33A6F7ACE4EC63812`, protocol SHA-256 is `D322C4DE79659D3CF90A32018715C3224E0F5B23D18628D33DC46622C8836CD7`, and the final clean outer build `build_d56f4ab06098` contains managed DLL SHA-256 `01BB59BED8577D3A2176D27EBA02A93748FC6F9D1C0F4B2246D6A6F16BF35880`. The retained single-session 20-reload gate passed `1/1` after a TDD lifecycle-race correction. Four real environments then produced one PPO update from `4,099` drained decisions (`93` natural terminals) and one finite FPO update from `4,098` decisions (`101` natural terminals); all workers contributed and cleanup left no Player process or worker port. This v2 gate intentionally does not satisfy broader Step 2's separately specified victory/all-dinos-dead/50-second/injected-interruption matrix or Step 5's complete minimum regression set, so those remain pending and `TASK-8.11` stays `进行中`. See `reports/phase8/structured-parallel-training/v2-validation.md`.

**2026-08-30 independent-review reproducibility fix:** The four-environment gate now has a retained parameterized entry point, `flow_rl/scripts/dino_v2_single_update_smoke.py`, rather than relying on an unretained inline harness. Its focused tests pass `2/2`; fresh review runs completed PPO with `4,099` transitions/`96` natural terminals and FPO with `4,099`/`92`, each with all four workers, exactly one finite update and raw JSON/exit artifacts. Two smallest existing tests prove `interrupted -> truncated` adapter mapping and truncated-terminal bootstrap semantics. This is synthetic mapping evidence, not an injected interruption in the real Player, so Step 2 remains unchecked; Step 5 also remains unchecked.

## Execution Order and Review Gates

1. The 8.10 visual-review gate was satisfied on 2026-08-30; Tasks 1–3 may proceed without saving `Dinos.unity`, and Task 4 may install the scene changes after recording a fresh pre-install scene hash.
2. Task 5 begins after Unity stream/action constants are frozen; its manifest must match the exported scene geometry.
3. Tasks 6–8 may run against synthetic structured batches while the formal Player is not yet built.
4. Task 9 begins only after the adapter and model interfaces are stable.
5. Task 10 is the only integrated Player validation and the only place that runs 20 reloads plus a four-process 4096-decision rollout.
6. No task performs an automatic Git commit; the user reviews the accumulated diff at checkpoints they choose.
