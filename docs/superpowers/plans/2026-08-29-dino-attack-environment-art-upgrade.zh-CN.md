# Dino Attack 环境美术升级实施计划

> **面向代理工作者：** 必需子技能：使用 `superpowers:subagent-driven-development`（推荐）或 `superpowers:executing-plans`，逐项实施本计划。步骤使用复选框（`- [ ]`）语法跟踪。

**目标：** 在不改变玩法语义的前提下，将已冻结的四边形 Dino Attack 地图升级为暖色、风格化的低多边形热带峡谷，加入纹理化地貌、Tribal Hut 村落视觉、轻量浅水和模块化岩石 Rim。

**架构：** 保留现有四边形区域、物理地面、Trigger、部署服务、NavMesh、速度体积、灰盒建筑 Collider 和攻击规则作为玩法层。扩展确定性的 `OpenTropicalBattlefieldSceneBuilder`，增加隔离的视觉层；其 Renderer 使用项目本地 URP Shader/材质，导入模型不含任何启用的 Collider、NavMeshObstacle、Rigidbody 或玩法组件。扩展 validator 和测试，使 canonical 提前返回仅在玩法层与视觉层都匹配时成立。

**技术栈：** Unity 6000.3.22f1、Universal Render Pipeline、C# Editor 工具、ShaderLab/HLSL、NUnit EditMode/PlayMode 测试、Unity NavMesh、确定性场景 builder、SHA-256 manifest。

**规格：** `docs/superpowers/specs/2026-08-29-dino-attack-environment-art-upgrade-design.md`

## 全局约束

- 不得修改五个区域的 A/B/C/D 顶点、双线性 `(u,v)` 映射、部署合法性、`1.10/1.00/0.85/0.80/0.70` 速度倍率、NavMesh 语义、攻击、6D action、504D observation、reward 或 episode 生命周期。
- 不得修改 `Packages/`、`ProjectSettings/`、官方算法仓库、checkpoint 或冻结的 Python 环境。
- 不得导入 `.blend`、GLB、OBJ、MTL、位移贴图或完整源资源包。
- 每个装饰房屋、岩石、植物、遗迹和可见 Rim 实例都必须具有零个启用的 Collider、NavMeshObstacle、Rigidbody 和玩法 MonoBehaviour 组件。
- 保留灰盒建筑 Collider 和玩法位置；Tribal Hut 视觉就位后禁用其 Renderer。
- 浅水 Shader 不得使用 Scene Depth、Scene Color、GrabPass、折射、反射、RenderTexture、额外摄像机、顶点位移或交互式涟漪。
- 源码/文档编辑使用 `apply_patch`，场景和资产变更使用已连接的 Unity Editor/builder；绝不手工编辑 `Dinos.unity` YAML 或 `.meta` 文件。
- 外层与嵌套仓库都视为包含用户 dirty 工作。仅在范围审查后暂存明确的任务文件；绝不 reset、clean、stash 或批量 add。

## 文件结构

- 创建 `dino-attack/Assets/ThirdParty/PolyHaven/EnvironmentGround/`，只包含选定的 Diffuse/Normal/Roughness-or-Specular 贴图、许可证和来源清单。
- 创建 `dino-attack/Assets/ThirdParty/Kenney/ModularCaveKit/`，只包含选定的 FBX 模块、colormap、许可证和来源清单。
- 创建 `dino-attack/Assets/ThirdParty/environment-art-import-manifest.json`，记录每个导入文件的准确源路径、SHA-256、许可证、目标路径、角色和启用状态。
- 创建 `dino-attack/Assets/LlamAcademy/Dinos/Stage8/Shaders/DinoAttackStylizedGround.shader`，实现基于世界 XZ 平铺、包含 diffuse、弱 normal、roughness 和宏观 tint 的地面着色；无位移。
- 创建 `dino-attack/Assets/LlamAcademy/Dinos/Stage8/Shaders/DinoAttackShallowWater.shader`，实现透明双向流动浅水着色和 UV 边缘泡沫。
- 创建 `dino-attack/Assets/LlamAcademy/Dinos/Editor/EnvironmentArtImportSettings.cs`，为批准的贴图和 Cave Kit FBX 文件提供确定性 importer 设置。
- 修改 `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`，实现材质创建、房屋视觉、水体绑定、Rim 模块、视觉净化和 canonical 检查。
- 修改 `dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`，增加美术资源、纯视觉、材质/Shader、房屋 Tier、Rim 和玩法不变性报告字段。
- 修改 `dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`，增加资产治理、Shader/材质、房屋、Rim、修复和字节稳定性测试。
- 修改 `dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`，增加运行时边界可见性、攻击、速度、NavMesh、视觉组件和清理回归测试。
- 通过 builder 修改 `dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`。
- 创建 `reports/phase8/environment-art-upgrade/`，保存基线、导入 manifest 副本、截图、性能、测试、长运行、Player smoke、验证和替代冻结 manifest。
- 仅用已验证的状态和证据修改 `docs/PROJECT_PLAN.md` 与本计划。

---

### 任务 1：冻结美术升级基线和准确资源清单

**文件：**
- 创建：`reports/phase8/environment-art-upgrade/baseline.md`
- 修改：`docs/PROJECT_PLAN.md`

**接口：**
- 输入：已批准规格、当前 `A3CB0CAD...D00FF` 四边形冻结、规格中列出的源目录。
- 输出：保留的场景 hash、源文件 hash、活动的 `TASK-8.10-ART`、暂停的 `TASK-8.11`、dirty worktree 清单，以及 Console/性能基线。

- [x] **步骤 1：记录已批准的执行状态**

将书面规格标记为已审阅；仅在本计划获得用户批准后将详细计划标记为已批准；保持 `TASK-8.10-ART` 活动，并保持 8.11 为 `未开始（等待重新冻结）`。

- [x] **步骤 2：捕获仓库和 Unity 基线**

运行：

```powershell
git status --short
git -C dino-attack status --short
Get-FileHash 'dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity' -Algorithm SHA256
Get-FileHash 'reports/phase8/quadrilateral-terrain-zones/manifest.json' -Algorithm SHA256
```

在 `baseline.md` 中记录 Unity 版本、活动场景、Edit/Play 状态、活动构建目标、质量等级、分辨率、renderer、Console cursor、新鲜错误，以及截图/性能采样的准确条件。

- [x] **步骤 3：只对批准的源候选文件计算 hash**

清点五个 Poly Haven 目录中的 Diffuse、Normal GL、Roughness/Specular 贴图；清点 `template-wall`、`template-wall-half`、`template-wall-corner`、`template-wall-top`、`template-wall-detail-a`、`template-corner`、`gate-rock`、Cave Kit colormap/license，以及 Tribal Hut Tier 1–4 源 Prefab。排除所有 `.blend`、位移贴图、GLB、OBJ 和 MTL 文件。

- [x] **步骤 4：建立可比较的性能样本**

在当前 Home 视图中捕获预热后的 300 帧，并记录 CPU 帧时间中位数/p95、可用时的 GPU 帧时间、batches、SetPass 调用、三角形数量和分辨率。GPU timing 不可用时记录为 unavailable，不得记录为零。

- [x] **步骤 5：审查检查点**

确认只有报告和计划状态发生变化；活动场景仍为 clean，hash 仍为 `A3CB0CAD78D8B540C12CFB06CCAEC5CDCF9FEDDF27819C288AB8E037098D00FF`。

- [x] **步骤 6：提交隔离的基线产物**

```powershell
git add -- reports/phase8/environment-art-upgrade/baseline.md
git diff --cached --check
git commit -m "docs: record environment art baseline"
```

当 `docs/PROJECT_PLAN.md` 包含无关的既有工作树修改时，不得暂存该文件。

### 任务 2：导入并治理最小美术资源集

**文件：**
- 创建：`dino-attack/Assets/ThirdParty/PolyHaven/EnvironmentGround/{Dirt,Sand03,BrownMud03,ForestGround01,MarbleRock03}/`
- 创建：`dino-attack/Assets/ThirdParty/Kenney/ModularCaveKit/{Models,Textures}/`
- 创建：`dino-attack/Assets/ThirdParty/environment-art-import-manifest.json`
- 创建：`dino-attack/Assets/LlamAcademy/Dinos/Editor/EnvironmentArtImportSettings.cs`
- 创建：`dino-attack/Assets/LlamAcademy/Dinos/EnvironmentArtTests/Editor/`

**接口：**
- 输入：任务 1 的源资源清单和 hash。
- 输出：批准的项目相对 texture/FBX 路径，以及所有后续任务使用的确定性 importer 设置。

- [x] **步骤 1：编写失败的资产治理测试**

添加 `EnvironmentArtAssets_AreSelectiveLicensedAndImportedDeterministically`，断言：

```csharp
Assert.That(Directory.GetFiles(root, "*.blend", SearchOption.AllDirectories), Is.Empty);
Assert.That(Directory.GetFiles(root, "*.obj", SearchOption.AllDirectories), Is.Empty);
Assert.That(Directory.GetFiles(root, "*.glb", SearchOption.AllDirectories), Is.Empty);
Assert.That(Directory.GetFiles(root, "*disp*", SearchOption.AllDirectories), Is.Empty);
Assert.That(manifest.Entries.All(entry => File.Exists(entry.Destination) && entry.Sha256.Length == 64), Is.True);
```

定义测试专用的私有 `[Serializable]` DTO：`EnvironmentArtManifest` 和 `EnvironmentArtManifestEntry`，字段包括 `Entries`、`Source`、`SourceSha256`、`Destination`、`License`、`Role` 和 `Active`，然后使用 `JsonUtility.FromJson<EnvironmentArtManifest>` 解析。还要断言每张 Diffuse 均启用 sRGB 且最大尺寸为 1024，每张 Normal 均为 `TextureImporterType.NormalMap` 且关闭 sRGB，每张 Roughness/Specular 均为线性，并且每个 Cave Kit 模型都禁用 readable 且不导入动画。

- [x] **步骤 2：运行聚焦 EditMode 测试并验证 RED**

在已连接的 Editor 中运行过滤器 `OpenTropicalBattlefieldSceneTests.EnvironmentArtAssets_AreSelectiveLicensedAndImportedDeterministically`。

预期：FAIL，因为受治理的导入目录和 manifest 尚不存在。

- [x] **步骤 3：添加确定性导入规则**

实现仅作用于两个新根目录的 `AssetPostprocessor`：

```csharp
private const string PolyHavenRoot = "Assets/ThirdParty/PolyHaven/EnvironmentGround/";
private const string CaveRoot = "Assets/ThirdParty/Kenney/ModularCaveKit/";

void OnPreprocessTexture()
{
    var importer = (TextureImporter)assetImporter;
    importer.maxTextureSize = 1024;
    importer.mipmapEnabled = true;
    importer.textureCompression = TextureImporterCompression.CompressedHQ;
    importer.sRGBTexture = assetPath.Contains("_diff_");
    if (assetPath.Contains("_nor_gl_")) importer.textureType = TextureImporterType.NormalMap;
}
```

对 FBX 文件设置 `importAnimation=false`、`isReadable=false`、`importCameras=false`、`importLights=false`，并在导入完整批准子集前，用一个代表性模块验证并保持固定 scale factor。

- [x] **步骤 4：只复制批准的源文件和许可证**

复制 Diffuse/Normal/Roughness-or-Specular 贴图和八个 Cave Kit FBX 候选文件。不得复制位移贴图或 `.blend`。使用源资源包中的 Cave Kit colormap 和许可证；在 manifest 中加入 Poly Haven CC0 来源/许可证元数据。通过 Unity 重新导入，使 `.meta` 文件由 Editor 生成。

- [x] **步骤 5：编写准确 manifest**

每个 JSON 条目必须包含：

```json
{"source":"absolute source file","sourceSha256":"64 hex chars","destination":"Assets/...","license":"CC0-1.0","role":"diffuse|normal|roughness|specular|rim-model|colormap|license","active":true}
```

拒绝重复目标路径、重复模型格式、缺失许可证、未列出的文件和源 hash 漂移。

- [x] **步骤 6：运行一次聚焦资产治理测试**

预期：专用治理测试运行一次并通过。此仅导入切片不得运行无关的完整 EditMode 或 PlayMode 套件。

- [x] **步骤 7：处理已完成的独立审查发现**

修订后的导入记录 Poly Haven 源 URL 和许可证副本，根据准确 allowlist 对源文件和目标文件字节分别计算 hash，使用自包含的任务 2 测试 assembly，将 Cave Kit colormap 以 sRGB 导入，排除 Kenney Nature Kit，并且不修改任何 package/settings/scene 路径。

- [x] **步骤 8：提交受治理的导入**

```powershell
git -C dino-attack add -- Assets/ThirdParty/PolyHaven/EnvironmentGround Assets/ThirdParty/Kenney/ModularCaveKit Assets/ThirdParty/environment-art-import-manifest.json Assets/LlamAcademy/Dinos/Editor/EnvironmentArtImportSettings.cs Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs
git -C dino-attack diff --cached --check
git -C dino-attack commit -m "assets: import governed environment art resources"
```

### 任务 3：添加风格化地面 Shader 和材质契约

**文件：**
- 创建：`dino-attack/Assets/LlamAcademy/Dinos/Stage8/Shaders/DinoAttackStylizedGround.shader`
- 修改：`dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- 修改：`dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- 修改：`dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`

**接口：**
- 输入：任务 2 的贴图路径。
- 输出：Shader `Dino Attack/Environment/Stylized Ground` 和六个 canonical 材质：中性泥土、Z1 沙地、Z2 森林地面、Z3 沙质河床、Z4 棕色泥地、Z5 暖色岩石。

- [ ] **步骤 1：实现最小 URP 地面 Shader**

使用世界 XZ 坐标和一次宏观调制，不修改顶点：

```hlsl
float2 detailUV = positionWS.xz / _DetailScale;
float2 macroUV = positionWS.xz / _MacroScale;
half3 albedo = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, detailUV).rgb * _BaseColor.rgb;
half macro = 0.9h + 0.1h * sin((macroUV.x + macroUV.y) * 6.28318h);
half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_NormalMap, sampler_NormalMap, detailUV), _NormalStrength);
half smoothness = 1.0h - SAMPLE_TEXTURE2D(_RoughnessMap, sampler_RoughnessMap, detailUV).r;
```

使用 URP forward lighting、兼容 SRP Batcher 的 `UnityPerMaterial`、不透明渲染和 GPU instancing；不得使用 tessellation/parallax/displacement。

- [ ] **步骤 2：用显式资产绑定替换纯色材质设置**

添加 `EnsureGroundMaterial(string name, string diffusePath, string normalPath, string roughnessPath, Color tint, float detailScale, float macroScale, float normalStrength)`，绑定批准的映射，不改变任何区域几何或组件。

- [ ] **步骤 3：扩展 canonical 验证**

在 `ArtPassIsCanonical` 和报告 signature 中加入 Shader 名称、贴图 GUID、tint、scale、normal strength、renderer-material identity，以及不存在禁止的 Shader 属性。

- [ ] **步骤 4：运行聚焦的修改后验证**

预期：材质导入后无粉色渲染；五区准确顶点、mesh signature、Trigger 和中性地面的玩法 Collider 保持不变。

- [ ] **步骤 5：审查检查点**

捕获一张近景和一张 Home 截图。确认区域具有自然区分，同时不存在持续显示的高饱和调试填充。

- [ ] **步骤 6：提交地面材质切片**

仅暂存新地面 Shader、六个命名材质资产及其生成的 `.meta` 文件、builder、validator、测试和 `Dinos.unity`；检查 `git -C dino-attack diff --cached --name-only`，然后使用 `git -C dino-attack commit -m "feat: add stylized battlefield ground materials"` 提交。

### 任务 4：用纯视觉 Tribal Hut 替换可见灰盒房屋

**文件：**
- 修改：`dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- 修改：`dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- 修改：`dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`
- 通过 builder 修改：`dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`

**接口：**
- 输入：八个现有 `Graybox Building` 锚点和项目本地 `Hut_Tribal_Tier1–4.prefab` 资产。
- 输出：具有确定性 Tier 分布 `1,2,2,3,1,2,3,4` 的 `Art Pass/Village Details/Houses`；灰盒 Renderer 不可见，玩法 Collider 不变。

- [ ] **步骤 1：添加纯视觉 Prefab helper**

实现：

```csharp
private static GameObject EnsureVisualPrefab(Transform parent, string name, string assetPath,
    Vector3 position, float yaw, Vector3 scale, bool preserveSourceMaterials)
```

使用 `PrefabUtility.InstantiatePrefab` 实例化，应用固定 transform，销毁实例上的 Collider/Rigidbody/NavMeshObstacle 组件，从此静态视觉 clone 中移除非渲染 MonoBehaviour，将所有后代设为 layer 0，并保留 Tribal Hut 源材质。

- [ ] **步骤 2：构建批准的村落层级**

只移除 builder 所有的 `Warm Roof 1–8`；在匹配的灰盒锚点创建八个分 Tier 的视觉对象；禁用但不销毁灰盒 Renderer；将 Tier 4 放在现有 Home camera 所选、最不妨碍视线的中央地标位置。

- [ ] **步骤 3：扩展 canonical 和修复检查**

要求准确的 Tier 源 GUID、名称、transform、数量、无禁止组件、禁用的灰盒 Renderer、不变的灰盒 Collider signature，并且不存在残留的 Warm Roof cube。

- [ ] **步骤 4：运行修改后的房屋、NavMesh、防御槽和 builder 幂等检查**

预期：房屋测试通过；每个 defense preset 路径保持完整；builder 能修复被重新启用的灰盒 Renderer 或新增的视觉 Collider，并在下一次运行成为 no-op。

- [ ] **步骤 5：审查检查点**

检查 Home 和最大缩放视图中的屋顶穿插与战斗遮挡。只调整确定性的视觉 offset/scale，不调整玩法锚点。

- [ ] **步骤 6：提交村落视觉切片**

```powershell
git -C dino-attack add -- Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs Assets/LlamAcademy/Dinos/Scenes/Dinos.unity
git -C dino-attack diff --cached --check
git -C dino-attack commit -m "feat: replace graybox houses with tribal visuals"
```

### 任务 5：实现轻量 URP 浅水表面

**文件：**
- 创建：`dino-attack/Assets/LlamAcademy/Dinos/Stage8/Shaders/DinoAttackShallowWater.shader`
- 修改：`dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- 修改：`dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- 修改：`dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`
- 修改：`dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`

**接口：**
- 输入：保持不变的 Z3 四边形 Mesh 及其 `[0,1]` UV。
- 输出：Shader `Dino Attack/Environment/Shallow Water`、材质 `ShallowWaterTeal`，以及与 Z3 Trigger/NavMesh/速度体积相互独立的透明视觉 Renderer。

- [ ] **步骤 1：实现受约束的水体 Shader**

使用同一张 normal 贴图的两次采样和 UV 边缘泡沫：

```hlsl
float2 uvA = input.uv * _TilingA + _Time.y * _PanA.xy;
float2 uvB = input.uv * _TilingB + _Time.y * _PanB.xy;
half3 normalTS = normalize(UnpackNormal(SAMPLE_TEXTURE2D(_FlowNormal, sampler_FlowNormal, uvA)) +
                           UnpackNormal(SAMPLE_TEXTURE2D(_FlowNormal, sampler_FlowNormal, uvB)));
half3 n = TransformTangentToWorld(normalTS, input.tangentToWorld);
half edge = 1.0h - smoothstep(0.0h, _FoamWidth,
    min(min(input.uv.x, 1.0h - input.uv.x), min(input.uv.y, 1.0h - input.uv.y)));
half fresnel = pow(1.0h - saturate(dot(normalize(input.viewDirWS), n)), _FresnelPower);
```

使用透明 alpha 混合、`ZWrite Off`；不使用 shadow caster pass、scene/depth texture 或顶点位移。

- [ ] **步骤 2：绑定 Shader，且不改变玩法对象**

仅把材质赋给 `Terrain Surfaces/ShallowWater/Visible Graybox`（如果测试和 canonical 路径同步更新，builder 可将其重命名为 `Water Surface`）。保持现有 Trigger Mesh、`TerrainSurfaceVolume`、Collider flag、layer 和 `0.70` 速度不变。

- [ ] **步骤 3：运行修改后的 EditMode 和运行时水体检查**

断言 Z3 顶点准确不变、Trigger signature 不变、15/15 路径通过、实测速率为 `0.70`、退出后恢复中性速度、每帧不创建材质实例、不增加额外摄像机，也不使用 RenderTexture。

- [ ] **步骤 4：审查检查点**

捕获 Home 与近距离缩放静帧，并进行一次短时运行观察。确认运动缓慢且清晰、河床可见、透明排序稳定、岸边带狭窄。

- [ ] **步骤 5：提交水体切片**

仅暂存 `DinoAttackShallowWater.shader`、`ShallowWaterTeal.mat`、它们生成的 `.meta` 文件、builder、validator、两个测试文件和 `Dinos.unity`；检查已暂存文件清单，然后使用 `git -C dino-attack commit -m "feat: add lightweight shallow water"` 提交。

### 任务 6：用确定性的 Cave Kit 模块替换可见的立方体 Rim

**文件：**
- 修改：`dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- 修改：`dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- 修改：`dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`
- 通过 builder 修改：`dino-attack/Assets/LlamAcademy/Dinos/Scenes/Dinos.unity`

**接口：**
- 输入：受治理的 Cave Kit 模块和保持不变的外围边界 Collider。
- 输出：确定性的低/中/高三层 `Art Pass/Canyon Rim` 模块组合；视觉 Collider 为 0，并隐藏旧的外围 Renderer。

- [ ] **步骤 1：定义固定模块记录**

添加 `RimVisualSpec` 表，包含准确的名称、资产路径、position、yaw、scale 和 `Low|Mid|High` 高度带。只使用固定记录；不得调用 `UnityEngine.Random`，也不得根据导入顺序推导 transform。

- [ ] **步骤 2：构建三层 Rim**

隐藏旧 `Low Perimeter Cliff` Renderer，但保留其 Collider。用选定的 Cave Kit wall/corner/top/gate 模块替换 builder 所有的 Rim 立方体；使用批准的模型添加有限的底部碎石和岩石顶盖；净化每个视觉实例。

- [ ] **步骤 3：扩展 canonical 检查和修复行为**

要求准确的源 GUID/transform 序列、高度带覆盖、启用的 Renderer、安全的 Shader/材质、零禁止组件、隐藏的旧 Renderer，以及不变的边界 Collider。添加修复测试：引入一个 Collider 和 transform 漂移，证明一次构建可修复两者，下一次构建保持字节稳定。

- [ ] **步骤 4：运行修改后的 Rim、摄像机、NavMesh 和场景检查**

预期：全部通过；Home 视图中村落与五区仍然可见；没有 Rim Mesh 成为 NavMesh source 或攻击 blocker。

- [ ] **步骤 5：审查检查点**

检查四种目标宽高比是否存在不自然重复、模块穿插和峰顶过高。修正构图时仅修改固定视觉表。

- [ ] **步骤 6：提交模块化 Rim 切片**

```powershell
git -C dino-attack add -- Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs Assets/LlamAcademy/Dinos/Scenes/Dinos.unity
git -C dino-attack diff --cached --check
git -C dino-attack commit -m "feat: build modular tropical canyon rim"
```

### 任务 7：完成区域装饰和运行时呈现行为

**文件：**
- 修改：`dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldSceneBuilder.cs`
- 修改：`dino-attack/Assets/LlamAcademy/Dinos/Editor/OpenTropicalBattlefieldValidator.cs`
- 修改：`dino-attack/Assets/Tests/Editor/OpenTropicalBattlefieldSceneTests.cs`
- 修改：`dino-attack/Assets/Tests/PlayMode/OpenTropicalBattlefieldPlayModeTests.cs`

**接口：**
- 输入：任务 3–6 的材质和视觉 helper。
- 输出：清晰、非阻挡的 Z1 遗迹、Z2 植被、Z3 河岸、Z4 泥地/棕榈和 Z5 岩石台地；仅部署时显示准确轮廓，并在 Running 时自然呈现。

- [ ] **步骤 1：替换后备立方体并调整确定性装饰**

使用固定的 `DecorationSpec` 记录和 `EnsureVisualPrefab`；仅删除 builder 所有、已被取代的装饰子对象。保留 Z1 遗迹、Z2 低矮植被、Z3 石块/河床、Z4 棕榈/树根和 Z5 岩石台阶，同时避免产生虚假的部署边界暗示。

- [ ] **步骤 2：强制仅在部署时显示精确覆盖层**

保持 `DinoDeploymentZonePresenter` 为唯一的精确轮廓权威。不得添加持续显示区域填充的开关；验证纹理化地面保持可见，同时边界 Renderer 遵循现有选择/悬停生命周期。

- [ ] **步骤 3：运行聚焦的修改后运行时检查**

运行放置、相对 UV、速度、中性恢复、UI 生命周期、15/15 路径和真实攻击事件套件。预期：所有玩法结果均与四边形冻结版本一致。

- [ ] **步骤 4：审查检查点**

捕获 Home/近景截图，确认无需纯色卡片即可识别五个区域，同时村落和单位仍为视觉焦点。

- [ ] **步骤 5：提交区域装饰切片**

按准确路径暂存 builder、validator、两个已修改的测试文件和 `Dinos.unity`；确认未暂存 package/settings/算法文件，然后使用 `git -C dino-attack commit -m "feat: finish five-zone environment dressing"` 提交。

### 任务 8：验证性能、玩法不变性、长运行和 Player 构建

**文件：**
- 创建：`reports/phase8/environment-art-upgrade/tests.json`
- 创建：`reports/phase8/environment-art-upgrade/performance.json`
- 创建：`reports/phase8/environment-art-upgrade/art-audit.json`
- 创建：`reports/phase8/environment-art-upgrade/longevity-20-episodes.json`
- 创建：`reports/phase8/environment-art-upgrade/player-smoke.log`
- 创建：`reports/phase8/environment-art-upgrade/validation.md`
- 创建：`reports/phase8/environment-art-upgrade/home-{16x9-1920x1080,16x10-1920x1200,21x9-2560x1080,16x9-1280x720}.png`

**接口：**
- 输入：canonical 美术升级场景和任务 1 的基线条件。
- 输出：准确测试总数、视觉审计、可比较性能数据、长运行证据和图形化 Player 证据。

- [ ] **步骤 1：按最强顺序运行测试**

依次运行编译、聚焦美术 EditMode、聚焦美术 PlayMode、完整 EditMode、完整 PlayMode、validator、3 种恐龙 × 5 种地貌导航/速度、UI 生命周期、攻击回归和新鲜 Console 扫描。记录准确的命令/工具调用、总数、时长、Unity 版本和 cursor。

- [ ] **步骤 2：以机械方式审计视觉层**

把每个视觉根节点、源 GUID、材质/Shader、Renderer 数量、三角形数量、layer 和禁止组件数量写入 `art-audit.json`。预期：所有禁止组件数量均为零，所有源 GUID 均已获批准。

- [ ] **步骤 3：捕获可比较的性能证据**

使用任务 1 的质量设置、分辨率、Home 摄像机、预热和 300 帧样本。报告前后 CPU/GPU 中位数与 p95、batches、SetPass、三角形数量、百分比变化，以及各指标是否具有有效可比性。性能门槛为：CPU 和 GPU 帧时间 p95 不得比基线上升超过 20%，SetPass 不得上升超过 25%，batches 不得上升超过 35%。GPU timing 不可用时，披露此限制并要求 CPU、SetPass 和 batch 门槛通过；不得对不匹配的样本宣称通过。

- [ ] **步骤 4：捕获四种宽高比并以全分辨率检查**

验证完整岛屿取景、村落可读性、五种地貌身份、Rim 轮廓、水体透明度、UI 安全区域，以及不存在粉色/缺失 Shader。

- [ ] **步骤 5：运行 20 个真实场景重载 episode**

执行十胜十负，并让每个区域使用四次。记录状态转换、返还、对象/订阅/材质数量、速度恢复、NavMesh 有效性、内存趋势和每个 episode 的 Console error。

- [ ] **步骤 6：构建并 smoke Windows Player**

构建同一个 `Dinos.unity`，启动新创建的 Player，验证图形化启动和真实回合活动，仅关闭本次运行的进程/窗口，并扫描其日志中的 crash、exception、missing reference、assertion、NaN、NavMesh、waypoint、armor、error 和 failed 标记。

- [ ] **步骤 7：审查检查点**

在 `validation.md` 中写入直接产物链接，并区分本次引入的失败与继承的 warning。任一硬门槛缺失时，任务保持活动状态。

- [ ] **步骤 8：提交验证证据**

```powershell
git add -- reports/phase8/environment-art-upgrade
git diff --cached --check
git commit -m "test: validate environment art upgrade"
```

### 任务 9：证明 canonical 稳定性并创建替代冻结

**文件：**
- 创建：`reports/phase8/environment-art-upgrade/manifest.json`
- 修改：`reports/phase8/quadrilateral-terrain-zones/manifest.json`，仅标记为历史版本/已取代，不删除证据。
- 修改：`docs/PROJECT_PLAN.md`
- 修改：`docs/superpowers/plans/2026-08-29-dino-attack-environment-art-upgrade.md`

**接口：**
- 输入：任务 1–8 的全部产物。
- 输出：字节稳定的替代冻结、已完成的 `TASK-8.10-ART`，以及恢复进行的 `TASK-8.11`。

- [ ] **步骤 1：执行最终验证顺序**

完成所有修复后，运行编译、聚焦与完整 EditMode/PlayMode 套件、validator、导航/速度/攻击/UI 矩阵、截图、性能、长运行和 Player smoke。最终一次完整 EditMode 测试后记录 H0，并且不得再运行可能保存场景的测试。

- [ ] **步骤 2：证明 canonical 字节稳定性**

调用 `OpenTropicalBattlefieldSceneBuilder.Build(true)` 两次并记录 H1/H2。预期：`H0 == H1 == H2`；场景保持 clean；所有 NavMeshData 引用/signature 保持不变；第二次构建不执行任何资产/场景写入。

- [ ] **步骤 3：创建替代 manifest**

对场景、Windows build、Packages lock、builder/validator/测试、两个 Shader、活动材质、每个活动导入资源、Tribal Hut Prefab、玩法防御资产和证据文件计算 hash。包含五个不变的顶点数组、速度表、6D/504 契约、被取代的 `A3CB0CAD...D00FF` hash 和替代 hash。

- [ ] **步骤 4：更新事实来源状态**

仅当每个硬门槛均通过时，才将 `TASK-8.10-ART` 标记为完成，并将 `TASK-8.11` 恢复为 `进行中`。追加准确的总数、hash、产物路径、warning、限制，以及玩法和 AI 坐标契约保持不变的决策。

- [ ] **步骤 5：运行最终文档与范围 diff 检查**

```powershell
git diff --check -- docs/PROJECT_PLAN.md docs/superpowers/plans/2026-08-29-dino-attack-environment-art-upgrade.md reports/phase8/environment-art-upgrade
$forbidden = @(('T'+'BD'), ('T'+'ODO'), ('implement '+'later'), ('fill in '+'details')); Select-String -Path docs/superpowers/plans/2026-08-29-dino-attack-environment-art-upgrade.md -Pattern $forbidden
git status --short
git -C dino-attack status --short
```

预期：无空白/占位符失败；状态中包含保留的用户工作和明确列出的美术升级交付物，除此之外没有其他新增内容。

- [ ] **步骤 6：最终审查检查点**

将每项设计要求映射到通过的测试或产物。若任何视觉、玩法、性能、Player 或字节稳定性硬门槛缺失，不得宣称完成、暂存宽泛路径或恢复 8.11。

- [ ] **步骤 7：提交替代冻结产物**
