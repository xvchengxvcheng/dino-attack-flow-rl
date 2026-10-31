# Dino Attack 房屋目标、体积寻路与山体阻挡设计

**日期：** 2026-08-29

**状态：** 用户已最终确认并授权实施；ART 与 GAMEPLAY 的最终复核和冻结合并进行

**任务：** `TASK-8.10-GAMEPLAY`

**目标场景：** `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`

## 1. 背景

当前每只恐龙生成后由既有 `Dino Graph` 自主行动：`AttackRadius` 收集附近的 `IDamageable`，`SortAttackablesAction` 按 `AttackConfigSO.EnemyPriority` 和 NavMesh 距离选择目标，随后复用既有移动、攻击和中央目标回退。该流程只能通过 `UnitType.Building` 粗略识别建筑，无法区分居民房屋与防御墙；可见 Tribal Hut 又是纯视觉层，因此房屋目前不可攻击，也不产生资源收益。

地图已有迅猛龙、肿头龙、霸王龙和守卫四种独立 NavMeshSurface，但部分烘焙体积与真实 Prefab 不一致：肿头龙运行半径为 `1.17`、对应烘焙半径为 `0.5`；霸王龙运行高度为 `5`、对应烘焙高度为 `2.5`。三种恐龙的局部避障当前均关闭。外围 9 段主要 Rim 又是无 Collider 的纯视觉对象，不参与 Layer 6/7 的物理 Collider NavMesh 收集，因而不能阻止恐龙穿山。

本任务在不改变部署动作、训练观测维度、地貌速度或攻击视线规则的前提下，使房屋成为有明确资源收益的局部目标，使三类恐龙遵循用户批准的不同目标优先级，并用真实身体体积、独立 NavMesh、局部避让和简化山体轮廓改进通行表现。

## 2. 目标与非目标

### 2.1 目标

- 房屋按 Tier 具有不同耐久，摧毁任意房屋恰好返还一次 `100` 肉。
- 迅猛龙、肿头龙和霸王龙采用独立、严格的局部目标类别优先级。
- 同类别目标只按实际 NavMesh 路径长度选择，不把地貌速度、通道余量或转弯惩罚纳入目标评分。
- 三种恐龙的静态可达性与实际身体半径、高度一致，并启用与体积相符的局部避让。
- 9 段主要 Rim 使用独立、可控的隐藏物理/NavMesh 阻挡轮廓；小石块、植物和普通装饰继续保持纯视觉。
- 地图提供 6 个固定墙位；每局开始或重新开始时无放回随机选择 3 个墙位，并在每个选中位置生成一面基础满血墙和一个不巡逻的固定弓箭手。
- 取消多回合维修循环；单局结束后弹出重新开始 UI，完整重载场景并复位全部玩法状态。
- 保持五区到中央目标的三种恐龙 `15/15` 可达性和现有中央目标胜负语义。

### 2.2 非目标

- 不把恐龙个体行为替换为 ML-Agents Agent、Utility AI 或新的训练策略。
- 不改变 6 维部署动作、504 维观测、terminal/truncated 区分或 Agent ID 语义；玩家与 AI 的局间重置统一改为完整场景重载。
- 不把单栋房屋状态加入部署 Agent 观测；房屋收益仅通过现有食物数量及食物差值间接反映。
- 不把地貌类型或地貌速度用于寻路评分；既有地貌速度系统继续只影响实际移动速度。
- 不增加山体、房屋或墙体对攻击的 LOS、射线或掩体阻挡。
- 不让小石块、植物、废墟装饰或美术表面参与 NavMesh。
- 不直接给复杂山体模型添加 MeshCollider。
- 不要求摧毁全部房屋才能获胜；中央目标仍是唯一直接胜负条件。
- 不保留回合计数、回合资源追加、敌方维修阶段或墙体升级阶段。
- 不修改 Packages、算法仓库、checkpoint、冻结 Python 环境或与本任务无关的 ProjectSettings；只允许为真实导航体积窄修改 `ProjectSettings/NavMeshAreas.asset`。

## 3. 核心架构

### 3.1 目标类别与元数据

新增 `DinoTargetCategory`，第一版只包含：

- `House`
- `Defender`
- `Wall`

新增轻量目标元数据组件或接口，使目标明确暴露类别。现有 `UnitType`、伤害和护甲系统保持兼容；目标排序不得再用 `UnitType.Building` 推断房屋或墙。未带新元数据的历史对象使用明确的兼容映射，无法映射的对象不进入新排序。

### 3.2 每种恐龙的严格优先级

每种恐龙使用独立的 `DinoTargetPriorityProfile`，优先级固定为：

| 恐龙 | 第一目标 | 第二目标 | 第三目标 |
|---|---|---|---|
| 迅猛龙 | 房屋 | 守卫 | 防御墙 |
| 肿头龙 | 守卫 | 房屋 | 防御墙 |
| 霸王龙 | 防御墙 | 守卫 | 房屋 |

优先级是严格的：只要感知范围内存在可达的更高类别目标，就不因低类别目标更近而降级。同一类别的所有候选都不可达时，才允许尝试下一类别。所有局部候选都不存在或不可达时，恐龙恢复既有中央目标行为。

### 3.3 目标选择数据流

```text
AttackRadius 收集附近 IDamageable
  → 清除 null、死亡、禁用和缺少有效类别的目标
  → 按当前恐龙的严格类别优先级分组
  → 为最高可用类别的每个候选只计算一次实际 NavMesh 路径长度
  → 排除不可达候选
  → 选择路径最短者，容差内按 Stable ID 决定
  → 写回现有 Dino Graph 的 ClosestAttackable
  → 复用既有移动和攻击节点
```

新选择器取代 `PriorityDistanceOnNavMeshComparer` 在 `List.Sort` 比较过程中反复调用 `NavMesh.CalculatePath` 的方式。每轮选择对当前类别的每个候选最多计算一次路径，不增加跨帧缓存。

## 4. 房屋玩法

### 4.1 玩法层与美术层

可见 Tribal Hut 继续作为无 Collider、无 Rigidbody、无 NavMeshObstacle、无 MonoBehaviour 的美术子层。现有隐藏 `Graybox Building` 占地升级为房屋玩法根，承载生命、目标元数据、受伤/死亡事件和原有简化 Collider。美术替换不会改变房屋玩法身份。

### 4.2 耐久与奖励

| Tier | 最大生命 |
|---|---:|
| 1 | 100 |
| 2 | 150 |
| 3 | 225 |
| 4 | 325 |

任意 Tier 房屋死亡后都执行一次且仅一次：

```text
房屋生命归零
  → 标记奖励已结算
  → DinoSpawner.ResourcesToSpend += 100
  → 从所有恐龙候选目标中退出
  → 禁用受伤与目标元数据，但保持独立玩法占地启用
  → 仅隐藏完整房屋视觉，显示 3–5 根暖色木梁组成的非碰撞残骸
  → 通知附近恐龙重新选择目标
```

延迟攻击、重复死亡事件、场景卸载和同一帧多次命中都不能重复发放肉。第一版不增加额外 ML shaped reward，`100` 肉已经通过现有资源与部署能力形成实际收益，并自动反映在现有食物和食物差值观测中。

房屋死亡后保留原简化 Collider/NavMesh 占地，视为不可穿越的坍塌残骸，不触发运行时 NavMesh 重烘焙；本任务不会让摧毁房屋同时成为开路机制。死亡视觉不使用独立废墟模型，只保留 `3–5` 根采用既有暖色木材材质的简化木梁；木梁应横倒、交错并分布到隐藏阻挡轮廓的主要边缘，使玩家能明确辨认该处仍不可通行，且部署预览继续拒绝该范围。木梁只含 Renderer/MeshFilter，不含 Collider、Rigidbody、NavMeshObstacle、`IDamageable` 或玩法 MonoBehaviour。完整房屋视觉、木梁残骸、可攻击组件和独立占地必须是不同子层，死亡时不得停用整个房屋玩法根。重新开始或 AI 新 episode 的完整场景重载恢复全部房屋、完整视觉和未结算状态。

## 5. 体积感知寻路

### 5.1 有效导航体积

有效体积取实际碰撞体与 NavMeshAgent 配置中较大的安全尺寸：

| 恐龙 | 有效半径 | 有效高度 | 局部避让角色 |
|---|---:|---:|---|
| 迅猛龙 | 0.50 | 2.0 | 主动为大型恐龙让行 |
| 肿头龙 | 1.17 | 2.0 | 中等优先级 |
| 霸王龙 | 2.00 | 5.0 | 最高通行优先级 |

对应 NavMesh agent type 和三种 gameplay Prefab 必须使用一致的 agent type、半径与高度。静态障碍通过各自 NavMeshSurface 的烘焙腐蚀自然形成不同通行宽度；不得为了维持旧路径而静默缩小真实体积。

### 5.2 同类别路径选择

- 同类别只比较完整 `NavMeshPath` 的实际长度，即连续 path corners 之间欧氏距离之和；不使用平方段长或直线距离。
- invalid、partial、空角点、NaN 或 Infinity 路径均视为不可达；当前类别全部不可达时才降级到下一类别。
- 路径长度差在 `0.01m` 内时，以显式 Stable ID 打破平局，保证选择可复现。
- 不增加通道余量采样、急转弯惩罚、跨帧路径缓存、卡住检测或目标冷却；地貌类型、地貌速度和视觉材质也不进入评分。

### 5.3 局部避让

- 启用三种恐龙的局部避让；霸王龙通行优先级最高，肿头龙居中，迅猛龙主动让行。
- 体积差异由三种独立 NavMesh agent type 的半径、高度和静态烘焙负责，不在 Task 2 重复建立路径评分模型。
- 目标死亡或失效后由既有 Behavior Graph 下一次选择重新计算，不新增缓存失效、卡住恢复或传送机制。
- 所有局部目标都不可达时回退中央目标。

## 6. 山体阻挡

### 6.1 分层结构

9 段主要 Rim 的视觉模型继续保持纯视觉，另建确定性的隐藏阻挡层：

```text
Canyon Rim
├─ Visuals
│  └─ 原有山体模型（无玩法组件）
└─ Navigation Barriers
   ├─ Rim North West Barrier
   ├─ Rim North Gate Barrier
   ├─ Rim North East Barrier
   ├─ Rim West Rocks Barrier
   ├─ Rim West Corner Barrier
   ├─ Rim East Rocks Barrier
   ├─ Rim East Corner Barrier
   ├─ Rim South West Barrier
   └─ Rim South East Barrier
```

每段 Barrier 由少量隐藏 BoxCollider、CapsuleCollider 或简单凸轮廓组合，固定放在现有 NavMeshSurface 收集的 `Grass`（Layer 6）玩法 Collider 层，不使用 `Enemies`（Layer 7），也不新增项目 Layer。每个 Barrier 同时使用 `NavMeshModifier` 将自身几何覆盖为内建 `Not Walkable` 区域，防止 BoxCollider 顶面被烘焙成可行走山顶。Barrier 不包含 Renderer、`IDamageable`、目标元数据或攻击 LOS 组件。轮廓只覆盖山体实际接地区域，不跨越视觉缺口、山谷或门洞。Cave Gate 如保留门洞，只阻挡两侧岩柱和必要的顶部净空；不同恐龙能否通过由各自半径和高度决定。

### 6.2 规则边界

- 山体阻挡恐龙与守卫移动，并从四种 NavMesh 中切除覆盖范围。
- 四种 NavMesh 在 Barrier 接地点和顶面采样都必须失败；不得出现可走山顶或与主地面连接的孤立导航面。
- 山体没有生命、不进入攻击候选，也不成为部署坐标目标。
- 攻击动作继续直接对 `IDamageable` 应用效果，不新增山体 LOS/射线检查。
- 小石块、植物、普通废墟和区域装饰继续无 Collider，不参与 NavMesh。
- Rim 只限制外围和实际山体覆盖，不在战场内部制造固定三路线。
- 五个部署区到中央目标的三种恐龙组合必须保持 `15/15` 可达；若失败，优先修正隐藏山体轮廓和通道宽度，不缩小恐龙体积。
- 部署服务必须拒绝位于山体 Barrier 内部的位置，即使该位置落在某个四边形部署区边界内。

## 7. 六选三墙位与固定弓箭手

### 7.1 墙位选择

地图只保存 6 个固定 `WallSpawnSlot`，不再保存两套整组防御预设。每局初始化规则为：

1. 使用本局 start seed 对 6 个墙位做确定性洗牌。
2. 无放回选择前 3 个，保证三个位置互不重复。
3. 每个选中位置生成一面基础 Tier、满生命墙；其余 3 个位置保持空置。
4. 每面墙生成后触发必要的 NavMesh 更新，再开放 Setup 输入。

玩家点击重新开始时派生新的 start seed，因此布局重新随机；AI 使用可记录的环境 seed 与 episode 序号派生 start seed，确保同 seed 运行可复现。场景报告必须记录 6 个候选墙位和当局选中的 3 个索引。

### 7.2 固定岗位规则

每面当局生成的墙都有一个 `WallGuardPost`：

- 岗位固定生成弓箭手，不使用随机守卫类型。
- 岗位不占用三个普通地面 Spawn Slot。
- 弓箭手固定在墙上平台，不巡逻、不离开平台、不响应其他墙体求援，只攻击射程内恐龙。
- 岗位初始化完成后必须满足“3 面墙、每面墙恰好 1 个固定弓箭手”，不得重复生成或重复订阅事件。

### 7.3 单局生命周期

- 墙生命归零时，岗位让弓箭手按正常守卫死亡流程随墙死亡，再执行墙失效和 NavMesh 更新。
- 固定弓箭手死亡计入现有守卫死亡事件；墙死亡仍计入现有墙死亡事件，两者都是实际发生的独立战斗结果。
- 本局内死亡的墙和弓箭手不维修、不升级、不再次生成；其位置保持空置直到重新开始。
- 原 `EnemyRepairs`、`HandleRepairs`、`HandleUpgrades` 和防御 preset 切换不再参与正式 `Dinos.unity` 游戏流程。
- 重新开始通过完整场景重载销毁旧墙与旧事件订阅，并按新的 start seed 重新执行六选三初始化。

## 8. 单局状态机与重新开始 UI

### 8.1 状态机

正式游戏从多回合循环改为单局流程：

```text
Load / Restart
  → Reset all gameplay state
  → Select 3 of 6 wall slots and spawn walls/archers
  → Setup
  → Running
  → Ending
  → Ended (wait for restart in Human mode)
```

- 删除自动 `Round++`、按回合追加资源、`Scoring → EnemyRepairs → Setup` 和下一回合循环。
- 本局胜负仍由既有中央目标与全灭条件决定；`Ending` 保留必要的死亡/结算表现，随后进入 `Ended`。
- Human 模式在 `Ended` 停留；AI 模式必须先向 ML-Agents 提交 `EndEpisode()` 或 `EpisodeInterrupted()`，再调度同一个场景重载入口。重载不得早于终止步进入 LLAPI；沿用项目现有的单次调度保护与至少 `0.15` 秒 realtime 延迟，并以集成 smoke 证明 TerminalSteps 未丢失。
- 完整重载必须恢复玩家与敌方资源、房屋、恐龙、普通守卫、墙、固定弓箭手、中央目标、一次性奖励标记、NavMesh、UI 和随机布局状态。

### 8.2 结算面板

复用现有 UI Toolkit `RuntimeUI`，在 `Ended` 显示居中的模态结算面板：

- 背景半透明变暗，锁定部署、选择和 Start 输入。
- 标题显示“胜利”或“失败”。
- 摘要显示本局剩余肉，以及摧毁的房屋、守卫和墙体数量。
- 只提供一个主要操作：“重新开始”。
- 点击后立即禁用按钮并进入 reload-pending 状态，防止重复点击；加载失败时恢复按钮并显示明确错误。
- AI 模式不需要点击或依赖可视 UI，但必须调用与按钮相同的 reload service。

## 9. Human/AI 与训练协议兼容

- 玩家和 AI 继续调用同一个 `DinoSpawner` 部署入口，个体恐龙生成后共享同一 Behavior Graph 目标规则。
- 6 维动作仍为 Wait/Place、三种恐龙选择和二维部署坐标；不新增“指定攻击目标”动作。
- 504 维观测布局不变。房屋的单体生命和位置不新增 observation slot；房屋死亡的 `100` 肉通过现有 food 与 food delta 体现。
- 墙上弓箭手作为普通 `Defender` 自动进入现有守卫数量和守卫槽，不新增观察类别；现有最大 24 守卫容量不变。
- 中央目标、胜负和超时语义不变；自然胜负仍为 terminal，时间/外部中断仍为 truncated。AI 顺序固定为“结算奖励 → `EndEpisode()`/`EpisodeInterrupted()` → 单次调度延迟重载”，玩家按钮只调用同一个重载服务；任何重复结算事件不得二次终止或二次加载。
- UI 不再显示 Wave/Round、Scoring 或 Enemy Repairs；阶段只显示 Setup、Battle 和最终结算。

## 10. 错误处理与确定性

- 缺少优先级 Profile、房屋目标元数据、6 个合法 WallSpawnSlot、WallGuardPost 绑定、固定弓箭手 Prefab、restart service 或任一必需 NavMeshSurface 时，builder/validator 必须明确失败，不静默回退为旧行为。
- 房屋肉奖励使用对象级一次性结算状态，并在完整场景重载后复位。
- 目标列表每次使用前清除 null、死亡和禁用对象，避免延迟攻击引用废墟或已销毁守卫。
- 路径选择器必须对无效、partial、NaN/Infinity 和空角点路径 fail closed。
- 山体视觉资源可替换，但阻挡轮廓名称、Transform 和几何由确定性 builder 管理；不得依赖源 FBX 自带 Collider。
- 相同场景、配置和 seed 下，目标优先级、同成本 tie-break、固定守卫数量与 builder 结果必须稳定。Unity 局部避让不承诺逐帧或浮点位级轨迹完全一致；确定性验收以目标选择、布局、终局语义和容差内路径结果一致为准。
- 六选三结果必须无重复、恰好 3 个；同 start seed 得到相同索引，不同 restart seed 允许得到不同组合。
- reload-pending 期间重复点击或重复 episode 结束事件只能调度一次场景重载。

## 11. 精简验证范围

遵循用户已批准的精简验收方向，只保留本功能直接相关的验证：

1. 三种恐龙严格目标优先级和不可达降级。
2. 四个房屋 Tier 的 `100/150/225/325` HP、一次性 `100` 肉、死亡后仅显示 3–5 根暖色木梁且能辨认隐藏占地，以及完整重载复位。
3. 三种有效体积对宽/窄通道的不同静态可达结果和局部避让配置。
4. 同类别实际路径长度、稳定平局、不可达降级和中央目标回退。
5. 9 段 Rim 均有独立隐藏 Barrier；接地点与顶面均不在对应 NavMesh；视觉缺口按体积区分通行。
6. 小石块、植物和普通装饰仍不阻挡移动；山体不进入攻击候选，也不阻挡攻击结算。
7. 三种恐龙 × 五个部署区到中央目标保持 `15/15`。
8. 6 个墙位中每局无放回选择 3 个；同 seed 可复现；每面墙恰好一个弓箭手且随墙死亡，本局不维修、不升级、不重生。
9. Human 结算 UI 只在 Ended 弹出，重复点击只重载一次；重载后资源、单位、房屋、墙、奖励、UI 和随机布局全部复位。
10. AI episode 先提交 terminal/truncated，再由同一 reload service 延迟且仅重载一次；LLAPI 必须收到对应 TerminalSteps。
11. 一次真实 `Dinos.unity` Play Mode smoke，确认房屋、肉、目标切换、六选三、墙/弓箭手、结算 UI 和重载生命周期。
12. 编译通过，并确认部署动作仍为 6 维、观测仍为 504 维。

不重复环境美术的完整测试套件、20 episode 长稳、四分辨率视觉矩阵或与本功能无关的 Unity 测试。最终是否需要 Player smoke 和 byte-stable 冻结由后续实施计划结合 `TASK-8.10-ART` 的最终状态决定，但进入 8.11 前必须有一个包含本任务结果的最终环境 manifest。

## 12. 实施顺序与任务边界

本设计不并入仍在进行的 `TASK-8.10-ART` 美术实现，以避免把“视觉对象无玩法组件”的旧边界静默改写。推荐顺序为：

1. 完成当前 ART 场景视觉复核，固定最终 Rim 与房屋 Transform。
2. 执行独立 `TASK-8.10-GAMEPLAY`：目标类别、房屋玩法、六选三墙位、固定弓箭手、单局状态机、重新开始 UI、体积寻路和山体 Barrier。
3. 生成包含 ART 与 GAMEPLAY 的最终场景/构建 manifest。
4. 恢复 `TASK-8.11` 环境协议和轨迹正确性验证。

本规格批准后只进入实施计划编写，不自动修改 Unity 代码、Prefab、场景、ProjectSettings 或测试。
