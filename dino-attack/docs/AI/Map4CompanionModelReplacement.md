# 地图4队友模型替换记录

## 当前结论

截图中队友显示为洋红色不是行为图或动画逻辑造成的。原有 `CompanionSceneBuilder` 在生成预制体时把材质复制到了内存，`PrefabUtility.SaveAsPrefabAsset` 不会保存这些临时材质，重新载入 `Companion.prefab` 后三个 `SkinnedMeshRenderer` 的材质槽为空，因此 Unity 使用错误着色器颜色显示。

已将生成流程改为：

1. 以 FBX 材质名查找或创建 `Assets/ThirdParty/VRoid/SendagayaShino/<材质名>.mat`。
2. 使用项目当前 URP 的 Unlit 着色器。
3. 自动绑定 `Textures/<材质名>.png`，启用双面绘制和透明裁剪。
4. 将持久化材质写回队友预制体，再保存资源。

当前队友仍使用工程内的 `Shino.fbx`，其来源说明为 CC0，可作为可运行的默认角色；待取得具有游戏使用权限的外部模型后，只需要替换模型资源并重新执行 `Dino Attack/Companion/Build Companion Map 4 + Lab`。

## APlayBox 链接核验

用户提供的页面：<https://www.aplaybox.com/details/model/fyxu1HgyD14n>

页面标题为“【原神】荧”。页面说明允许完善物理、修正权重和表情、改色及适度服装调整，但同时禁止商业用途、二次配布，并明确限制为视频制作之外的用途。当前 Dino-Attack 是 Unity 游戏工程，因此没有把该模型下载或纳入工程，避免违反页面的使用规则。页面内容应以作者最新规则和下载包内的 README 为准。

用户提供的下载包为 `【女主角_荧】_by_原神_39fd8673fd145a6c65858227788cb173.zip`。包内只有一个 PMX 模型、贴图和 README，没有 VMD、FBX 或 VRM 动作文件。README 允许修正物理、权重、表情和改色，但禁止二次配布、拆取部件、商业用途，并要求遵守 miHoYo 的最终权利声明；结合详情页的“视频制作之外的用途禁止”规则，当前不能把它转换后接入 Dino-Attack 游戏。压缩包没有复制到仓库，也没有在 Unity 中导入。

## 可接入模型的最低条件

- 作者明确允许在 Unity/游戏中使用，且允许项目所需的转换、材质修复和动画绑定。
- FBX（优先）或已验证的 VRM；若是 PMX，需要用户提供允许转换的授权和转换后的 FBX/VRM。
- 至少有 Humanoid 骨架，或同时提供可映射到 Humanoid 的动作数据。
- 包含待机、移动和攻击动作，或允许使用项目内 `DefenderAnimationBuilder` 绑定这些动作。
- 随模型保留作者、原作者、动作来源和授权文本，不把模型文件重新分发到公共仓库或安装包之外。

收到满足上述条件的新 APlayBox 详情页或用户已下载的合法文件后，导入目录建议为 `Assets/ThirdParty/APlayBox/<ModelName>/`，并继续复用当前队友的行为图、Jev 指令、好感度属性和独立实验室场景。
