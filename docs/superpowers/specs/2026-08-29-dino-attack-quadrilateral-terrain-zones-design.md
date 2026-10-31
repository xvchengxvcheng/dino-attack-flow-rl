# Dino Attack 五个扇形四边形地貌区设计

**日期：** 2026-08-29  
**状态：** 用户已批准空间结构与书面规格，进入实施  
**场景：** `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`  
**上游规格：** `docs/superpowers/specs/2026-08-28-dino-attack-open-tropical-battlefield-design.md`

## 1. 背景

当前场景用一个覆盖全图的 Grass 盒体作为基础地貌，再叠加四个较小的矩形地貌体。五个部署区同样由轴对齐 `BoxCollider` 表示。这使地貌看起来像“全图草地加四块覆盖”，也无法直接支持 AI 的“先选区域，再输出区域内相对坐标”决策方式。

用户批准把 Z1–Z5 改为五个互不覆盖的扇形四边形。村庄继续作为大型核心区；五区沿村庄约 240° 外缘分布，每区用较窄的村庄侧边连接村庄，用较宽的外缘朝向地图边界。五区之间和五区外保留中性公共战场，不要求覆盖整张地图。

## 2. 目标

- Z1–Z5 均为独立、凸、可自由穿行的四边形区域。
- 每区固定保存四个顶点，并提供稳定的 `(u,v) -> 世界坐标` 转换。
- 五区的地貌视觉、地貌速度 Trigger、部署合法范围和 UI 边界使用同一组四顶点，消除形状漂移。
- 五区不相互重叠，不覆盖村庄内部，也不覆盖全部地图；区域之间保留中性公共机动空间。
- 保持 Z1–Z5 ID、显示名、地貌类型和倍率：
  - Z1 Ruins Forecourt：StoneRoad，`1.10x`
  - Z2 Open Meadow：Grass，`1.00x`
  - Z3 River Terrace：ShallowWater，`0.70x`
  - Z4 Palm Grove：Mud，`0.85x`
  - Z5 Rocky Shelf：Slope，`0.80x`
- 本轮只建立区域几何和坐标转换契约，不修改当前 6 维 AI 动作、504 维 observation、奖励或 episode 协议。

## 3. 非目标

- 不让五区完全覆盖整张地图。
- 不在区域之间增加墙、固定路线、锁区或隐藏 waypoint。
- 不把区域边界解释为攻击遮挡或防御加成。
- 不在本轮把 AI 动作升级为区域 logits + 相对坐标。
- 不修改 ProjectSettings、Packages、历史训练场景或算法实现。

## 4. 空间结构

村庄保持四个聚落区和中央广场，仍位于地图上半部核心。五区从村庄的西侧，经西南、南、东南，到东侧依次展开：

1. Z1 从村庄西侧向地图西缘展开。
2. Z2 从村庄西南侧向地图西南部展开。
3. Z3 从村庄南侧向地图南部展开。
4. Z4 从村庄东南侧向地图东南部展开。
5. Z5 从村庄东侧向地图东缘展开。

每区的村庄侧边必须足够宽，不能退化为入口或窄路；外缘可以更宽，以形成扇形梯形。相邻区之间保留可见的中性地面缝隙，玩家和恐龙可以跨越这些公共空间移动，但不能在那里部署。

实现初始值以批准的俯视图为几何事实来源。允许在不改变顺序、村庄接触侧、凸性和公共空隙的前提下做小范围坐标微调，以避开建筑、确保 NavMesh 和改善镜头构图；最终四顶点必须写入场景证据。

## 5. 四顶点契约

每个区域按固定顺序保存四个本地 XZ 顶点：

- `A`：村庄侧边起点
- `B`：村庄侧边终点
- `C`：与 B 对应的外缘终点
- `D`：与 A 对应的外缘起点

`A -> B -> C -> D` 必须沿同一方向绕区域边界排列。区域必须满足：

- 四点数值有限；
- 投影到 XZ 后为严格凸四边形；
- 无自交、无连续重复点、面积大于最小阈值；
- 村庄侧边 `AB` 和外缘 `DC` 长度均大于最小部署宽度；
- 五个区域两两不重叠；
- 顶点和代表性内部采样点均能投影到有效地面和运行时 NavMesh。

顶点采用区域 Transform 的本地坐标存储，通过 `TransformPoint` 转为世界坐标。这样整个区域可以整体移动，但非均匀缩放和会破坏 XZ 平面的旋转不进入正式场景契约。

## 6. 相对坐标转换

区域内相对坐标为 `uv = (u,v)`，其中：

- `u=0` 位于 A/D 一侧，`u=1` 位于 B/C 一侧；
- `v=0` 位于村庄侧边 AB，`v=1` 位于外缘 DC。

转换公式：

```text
inner = lerp(A, B, u)
outer = lerp(D, C, u)
world = lerp(inner, outer, v)
```

区域 API 接收严格的 `[0,1]` 坐标；非有限值或越界值返回失败，不静默截断。未来 TASK-8.11 若升级 AI 动作，由动作适配层负责把策略的 `[-1,1]` 输出映射并约束到 `[0,1]`，再调用区域 API。

双线性映射保证四个角和四条边精确对应，但不保证世界面积均匀采样。当前目标是可解释、确定性的部署坐标，不在本轮引入面积均匀化逆变换。

## 7. 运行时组件设计

### 7.1 四边形数学

在 `DinoAttack.Map.Core` 中新增可测试的四边形值类型或静态数学模块，职责仅包括：

- 验证四点有限、凸、无自交和面积有效；
- 返回世界顶点；
- 严格执行 `(u,v) -> 世界 XZ` 转换；
- 用一致的边包含规则判断世界点是否位于四边形内；
- 提供有符号面积和边，供场景验证与 Mesh 构建使用。

### 7.2 部署区

`DinoDeploymentZone` 不再用 `BoxCollider.bounds` 作为真实合法范围。它保存四顶点契约，并公开：

```csharp
bool IsGeometryValid { get; }
bool Contains(Vector3 worldPoint)
bool TryResolveRelative(Vector2 uv01, out Vector3 worldPoint)
IReadOnlyList<Vector3> WorldVertices { get; }
```

`DinoDeploymentService.TryGetZone` 和玩家放置继续调用 `Contains`，因此 Human 与未来 AI 使用同一四边形合法性。

### 7.3 地貌 Trigger 与视觉

每个区域由同一组四顶点生成：

- 一张贴地四边形视觉 Mesh；
- 一张用于部署显界的四边形边界 Mesh；
- 一个有有限高度的 8 顶点凸棱柱 MeshCollider Trigger；
- 一个 `TerrainSurfaceVolume`，继续驱动现有速度控制器。

Trigger 只负责地貌进入/退出，不加入 NavMesh 几何，也不阻挡移动或攻击。UI 边界默认隐藏，选择恐龙时按现有生命周期显示。

### 7.4 中性公共地面

整张地图保留独立的中性物理地面和视觉底色，但它不挂 `TerrainSurfaceVolume`，也不属于任何部署区。恐龙离开五种地貌 Trigger 后恢复 Prefab 基础速度；中性地面不显示成第六种地貌，也不允许部署。

## 8. 场景构建与幂等

`OpenTropicalBattlefieldSceneBuilder` 改用五组四顶点构建区域，不再创建五个轴对齐矩形区域和“Grass 覆盖全图”的地貌体。canonical preflight 必须比较：

- 五区 ID、显示名、地貌类型和四顶点；
- 四边形视觉 Mesh、边界 Mesh 和 Trigger Mesh 的稳定签名；
- 中性地面的物理层和材质；
- 部署服务、presenter、RuntimeUI 和地貌组件绑定；
- NavMesh、镜头和防御对象仍满足原契约。

canonical 场景再次运行 builder 必须在任何 Mesh 重建、NavMesh 烘焙、dirty 或 save 前返回，保持 byte-stable。真实四顶点偏差必须走可验证的修复路径。

## 9. 验证

### 9.1 EditMode

- 四角、边中点、中心和非对称四边形的双线性映射精确。
- 非有限、越界、凹、自交、零面积和重复顶点失败。
- `Contains` 覆盖内部、边界、顶点和外部点。
- Transform 平移和允许的水平旋转后映射一致。
- 五区配置必须 exact-five、ID 唯一、几何有效且两两不重叠。
- 视觉 Mesh、边界 Mesh 和 Trigger 棱柱均由相同四点生成。
- builder 能修复真实顶点偏差，canonical 双运行 byte-stable。

### 9.2 PlayMode

- 玩家只能在五个四边形内放置，公共空隙和村庄内均返回区域外错误。
- 每区多个 `(u,v)` 样本能解析、落地并通过 NavMesh 采样。
- 三种恐龙 × 五区继续保持 15/15 可达。
- 三种恐龙在五种 Trigger 中继续得到 1.10/1.00/0.70/0.85/0.80 倍率，并在公共地面恢复基础速度。
- 五区边界在选择、hover、非法和 Ending 生命周期中正确显示与清理。
- 静态物继续不阻挡攻击事件。

### 9.3 场景与视觉

- 五个扇形四边形在 Home 和四种批准比例中清晰可辨。
- 五区不覆盖村庄内部，不覆盖全部地图，并保留可穿行的公共缝隙。
- 地貌视觉边界、部署边界和实际 Trigger 无可见漂移。
- 最终记录五区世界顶点、面积、占全图比例、最小间距、NavMesh 样本和截图。

## 10. 计划和冻结影响

本变更会修改正式 `Dinos.unity`、地貌/部署核心组件、场景 builder、测试和冻结 hash。因此当前 TASK-8.10 的 scene/build manifest 在实施后只能作为历史冻结证据，不能继续代表新地图。

书面规格和实施计划获批后，应暂停 TASK-8.11，新增一个地图几何修订任务；完成实现、玩家回归、20 回合稳定性、Windows Player smoke 和新 manifest 后，再恢复 TASK-8.11。当前 AI 动作仍为 6 维，旧 observation/action 协议不会在本轮代码中改变。

## 11. 交付物

- 更新后的 `Dinos.unity` 和场景 builder。
- 四边形数学、部署区和 Mesh/Trigger 构建代码。
- EditMode/PlayMode/场景回归测试。
- 五区四顶点与相对坐标说明图。
- 更新后的场景截图、测试报告、长运行证据、Windows Player smoke 和冻结 manifest。
