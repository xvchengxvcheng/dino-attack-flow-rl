# Dino Attack 场景美术升级设计

**日期：** 2026-08-29

**状态：** 用户已批准设计方向与资源范围，等待书面规格复核

**任务：** `TASK-8.10-ART`
**目标场景：** `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`

## 1. 背景

当前五区四边形地图的玩法、部署、速度、NavMesh、攻击和 AI 坐标契约已经冻结并通过验证，但美术仍具有明显灰盒特征：外围 Rim 主要由等高长方体构成，五区长期显示为纯色四边形，公共地面缺少纹理与宏观变化，环境道具密度和轮廓变化不足。

本任务在不改变玩法语义的前提下，将场景升级为“暖色、风格化 Low-poly 热带峡谷”。运行时以自然地貌区分五区；精确四边形边界只在部署交互需要时显示。

## 2. 目标与非目标

### 2.1 目标

- 用模块化岩壁取代可见的方块 Rim，形成低、中、高三档轮廓和自然断口。
- 为公共地面、村庄和五区建立统一的轻量 URP 地表材质体系。
- 保留五区各自的视觉身份，但消除持续可见的调试色块感。
- 为 Z3 实现平静透明、缓慢流动的轻量 URP 浅水材质。
- 只选择性导入已批准的 CC0 资源，并记录来源、许可证、哈希和用途。
- 保持确定性场景构建和可复现冻结。

### 2.2 非目标

- 不改变五区 A/B/C/D 顶点、`(u,v)` 映射、部署合法性或区域面积。
- 不改变 6 维 action、504 维 observation、reward、episode 或 Human/AI 契约。
- 不改变石路/草地/泥地/坡地/浅水速度 `1.10/1.00/0.85/0.80/0.70`。
- 不增加攻击遮挡、掩体、LOS、隐藏防御数值或地貌伤害。
- 不引入真实地形位移、可碰撞装饰高差、顶点波浪或交互涟漪。
- 不修改 Packages、ProjectSettings、官方算法仓库、checkpoint 或冻结 Python 环境。
- 不导入 `.blend`、GLB、OBJ、MTL 或未使用的整包资源。

## 3. 核心架构：玩法层与美术层分离

### 3.1 玩法层

现有四边形 Trigger、NavMesh、部署服务、地表速度解析、公共空间和攻击规则保持原样。所有回归测试继续把这些对象视为唯一玩法事实来源。

### 3.2 美术层

新增或替换的岩壁、碎石、植物、地表覆盖和水面均属于视觉层。默认不带 Collider，不参与 NavMesh 烘焙，不进入攻击判定，也不成为 AI 坐标来源。需要保留的地图外边界继续使用现有简单不可见碰撞体。

### 3.3 构建方式

场景继续由 `OpenTropicalBattlefieldSceneBuilder` 或其拆分后的专用美术构建单元确定性生成，不手改场景 YAML。相同资源、seed 和配置必须生成相同层级、数量、Transform、材质引用与场景字节。

## 4. 批准的资源范围

### 4.1 Poly Haven 1K 地表

只从以下下载目录提取实际使用的 1K 贴图，不把 `.blend` 文件复制进 Unity：

- `C:\Users\hp\Documents\New project 2\dirt_1k.blend`
- `C:\Users\hp\Documents\New project 2\sand_03_1k.blend`
- `C:\Users\hp\Documents\New project 2\brown_mud_03_1k.blend`
- `C:\Users\hp\Documents\New project 2\forrest_ground_01_1k.blend`
- `C:\Users\hp\Documents\New project 2\marble_rock_03_1k.blend`

每套资源优先选择 Diffuse、Normal GL 和 Roughness/Specular。Displacement 不导入；Normal 和 Roughness如需转换为 8-bit PNG，由可复现的项目侧导入步骤产生，并保留原文件 SHA-256。所有 Diffuse 都要通过材质参数完成低饱和、低对比和暖色统一，避免写实纹理与 Low-poly 模型割裂。

### 4.2 Kenney Modular Cave Kit

来源目录：`C:\Users\hp\Documents\New project 2\kenney_modular-cave-kit_1.0`。

只选择性导入用于 Rim 的 FBX、单张 colormap 和许可证。优先候选为 `template-wall`、`template-wall-half`、`template-wall-corner`、`template-wall-top`、`template-wall-detail-a`、`template-corner`、`gate-rock` 和少量 floor/detail 模块。最终清单以代表性预制验证为准，不复制完整 40 模型资源包。

### 4.3 既有项目资源

- Mini Forest：`tree`、`tree-high`、`plant`、`patch-grass`、`rocks-high/low/ramp`、`stones`。
- Tower Defense Kit：只使用适合环境装饰的 dirt/rocks/detail 模型。
- 原 Dino Attack：棕榈、村庄建筑、围栏和现有战斗模型。
- Kenney Nature Kit 不存在，也不是本任务依赖。

### 4.4 资源治理

资源放入 `Assets/ThirdParty/` 的隔离目录，保留原始文件名、来源 URL、许可证副本、源 SHA-256、项目内目标路径和 active/inactive 状态。相同模型不以 FBX/OBJ/GLB 多格式重复导入。

## 5. 地表材质体系

### 5.1 公共地面

公共地面以 `dirt` 为主体，通过世界坐标平铺、宏观色差和轻微法线打破单色。宏观变化尺度约 20–40 米，细节尺度约 1–3 米；不使用位移。

### 5.2 五区映射

| 区域 | 主材质与视觉语言 |
|---|---|
| Z1 石路/废墟 | `sand_03` 底层、断裂石板、碎柱和砖块；保留最快地表语义 |
| Z2 草地 | 公共 dirt 与低对比 `forrest_ground_01` 混合，辅以草簇、植物、小石块 |
| Z3 浅水 | `sand_03` 水底、卵石与轻量浅水 Shader |
| Z4 泥地 | `brown_mud_03`、低亮湿斑、树根、棕榈和枯枝 |
| Z5 坡地/岩台 | 暖色化的 `marble_rock_03` 作为弱细节，配合碎石与低矮岩阶 |

视觉表面的自然斑块可以略微越过玩法四边形，但不得造成可部署范围误导。部署阶段仍用现有边界 Presenter 显示精确合法区域；Running 阶段隐藏持续纯色填充，只保留自然地貌身份。

### 5.3 村庄房屋

现有 `Graybox Building + Warm Roof` 不再作为最终可见房屋，但它的玩法占地、Collider、防御槽、NavMesh 关系和其他交互组件继续作为唯一玩法事实来源。构建器只隐藏灰盒 Renderer，并在其下添加独立视觉子节点；视觉子节点不得反向改变灰盒 Transform 或玩法组件。

主房屋从项目既有 `Assets/LlamAcademy/Dinos/Vendor/polyperfect/Low Poly Tribal Pack/T- Prefabs_T/Tribal_T/` 选择：

- `Hut_Tribal_Tier1.prefab`：小型住宅和村缘填充，数量最多。
- `Hut_Tribal_Tier2.prefab`：普通住宅，构成村落主体。
- `Hut_Tribal_Tier3.prefab`：工坊或大型住宅，用于广场和主要通路附近。
- `Hut_Tribal_Tier4.prefab`：中央大厅或地标，只放少量，避免遮挡镜头和战斗单位。

视觉实例采用确定性的模型、旋转、比例和锚点配置。Prefab 自带的 Collider、NavMeshObstacle、脚本或其他玩法组件必须禁用；若无法安全剥离，则使用对应 FBX 创建纯 Renderer 视觉 Prefab。房屋沿用原包的暖色茅草和木材，不另做高饱和屋顶色块。Mini Forest 的 `building-structure`/`building-roof` 只用于小棚、门岗等附属建筑，不替代四级主房屋。

## 6. Rim 设计

- 保留现有地图外边界碰撞体，但使其不承担主要视觉表现。
- 用 6–10 种 Cave Kit 模块构成外圈岩壁，避免连续相同模块和等高轮廓。
- 轮廓分低、中、高三档；靠五区方向较低，远景与角落形成较高峰值。
- 使用主岩壁、底部碎石、顶部岩帽三层构图，转角和断口使用专用模块。
- Rim 材质以 Cave Kit colormap 为基础，统一为暖赭石、暗褐阴影和浅橙受光面。
- 所有可见 Rim 模块默认 0 Collider；若现有边界碰撞需要调整外观位置，玩法 Collider 仍保持独立且必须通过原有可达性验证。
- 以确定性的有限旋转、比例档位和组合模板增加变化，不使用运行时随机散布。

## 7. 轻量 URP 浅水 Shader

Z3 水面使用项目侧透明 URP Shader，设计为平静透明浅水并具有缓慢流动：

- 青绿色基础色，目标 alpha `0.70–0.85`。
- 一张小型可平铺 Noise/Normal 贴图采样两次，采用不同尺度、方向和速度。
- 使用轻微 Fresnel 高光和低强度法线，不做高频闪烁。
- 根据四边形 Mesh 的 `[0,1]` UV 生成薄岸边亮带/泡沫，不依赖 Scene Depth。
- Transparent、ZWrite Off，不投射阴影；水面高度保持固定。
- 不使用折射、实时反射、GrabPass、Scene Color、Depth Texture、Render Texture、额外摄像机、顶点位移、交互涟漪或水下系统。
- 现有 Z3 Trigger、NavMesh 和 `0.70` 速度倍率完全不变。

## 8. 性能与可读性边界

- 地表纹理保持 1K；法线和粗糙度按平台压缩，关闭不需要的 alpha 功能。
- 材质数量保持小而共享，使用 SRP Batcher/GPU Instancing 可用路径。
- 植物和碎石使用少量模型的确定性变体，不通过大量独立材质制造变化。
- Home 视图必须能清楚辨认村庄、五区、公共空间和 Rim，不让装饰遮挡主要单位或部署反馈。
- 五区颜色来自材质、植被和道具组合，不依赖高饱和纯色块。

## 9. 实施顺序

1. 冻结当前四边形版本基线和资源 provenance。
2. 选择性导入 Cave Kit 与 Poly Haven 贴图，建立 manifest。
3. 实现公共地面和五区材质，不改变几何与玩法组件。
4. 用四级 Tribal Hut 视觉替换灰盒房屋 Renderer，保留原玩法占地和组件。
5. 实现并接入轻量浅水 Shader。
6. 用模块化岩壁替换可见 Rim，并补充碎石与植物。
7. 调整五区地标密度和 Running/部署显示状态。
8. 完成视觉、性能、玩法、Player 和确定性重新冻结。

## 10. 验证与退出标准

- Unity 6000.3.22f1 编译无错误，所有材质在 URP 中无粉色/丢失 Shader。
- ProjectSettings、Packages、6D/504、reward、episode 和算法代码均无变化。
- 五区 A/B/C/D、面积、间隙、`(u,v)` 映射和公共区域拒绝结果与当前冻结版本一致。
- 三种恐龙 × 五地貌的 15/15 NavMesh 路径、速度倍率和退出恢复全部通过。
- 攻击继续不受岩壁美术、植物、碎石和其他视觉道具阻挡。
- 房屋灰盒的玩法占地、防御槽和 NavMesh 结果不变；Tribal Hut 视觉层没有启用的 Collider、NavMeshObstacle 或玩法脚本。
- Tier 1–3 在村落中均有可见实例，Tier 4 只作少量地标；Home 与运行镜头下不持续遮挡主要战斗区域。
- Setup/Running/Ending 的边界显示与清理正确；Running 不持续显示五块纯色卡片。
- 16:9、16:10、21:9、1280×720 的 Home 与运行截图通过目视检查。
- 记录同质量设置、同分辨率、同采样窗口的 before/after CPU/GPU 或帧时间证据。
- 完整 EditMode、PlayMode、20 回合重载、Windows Player build 与图形 smoke 通过，fresh Console error 为 0。
- 连续两次 canonical builder 为 byte-stable，生成新的 scene/build/material/resource manifest；此前 `A3CB0CAD...D00FF` 冻结保留为历史版本。
- 只有全部硬门通过后，`TASK-8.10-ART` 才能完成并恢复 `TASK-8.11`。

## 11. 失败恢复

- 新资源只进入隔离目录；可通过禁用/移除美术层恢复玩法层，不回退用户其他修改。
- 如果 Cave Kit 与现有模型风格不协调，保留已验证的公共地面和水面，只恢复旧 Rim 视觉，不改变玩法碰撞。
- 如果透明水面产生排序或性能问题，降级为 Opaque/Alpha Clip 的双色流动材质，保留 UV 岸边亮带。
- 如果视觉修改改变 NavMesh、部署或攻击结果，任务立即保持进行中，先恢复玩法层一致性再继续美术调整。
