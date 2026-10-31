# 星夜地图主题

覆盖 Dinos、LayeredBattlefield、DefenseBattlefield、FirstPersonDefenseBattlefield、JevVillageDemo 五个场景。

- 天空、各地图天空材质和夜景后处理资源位于 `Assets/LlamAcademy/Dinos/Environment/StarNight/`。
- 使用三色环境补光、冷色主光、远景线性雾，以及最多四盏无阴影村庄暖光。分层地图主光略亮，方便识别坡道和平台。
- 月光使用偏银蓝色的 1.25 强度主光（分层地图 1.35），阴影强度 0.42，夜景曝光 +0.70；提亮地面和阴影，保持深蓝天空。
- 星空使用静态程序化天空着色器，无动态粒子、星形图标或运行时纹理生成。
- 战斗 UI 使用低饱和蓝灰面板和细描边，保留生命、伤害、警告和部署反馈的功能色。
- 未修改共享日景材质、默认后处理资源、地形几何、碰撞器、导航或单位规则。原有训练场景不应用星夜灯光；共享 RuntimeStyles 的颜色调整也会作用于使用该样式的训练 UI。

## 调整与重新应用

在 Edit Mode 打开上述场景，执行 `Tools > Dino Attack > Star Night > Apply To Active Map`。工具会直接保存当前场景。参数集中在 `Assets/Editor/NightMapVisualTheme.cs`。

开阔战场的视觉修复器会保留已应用的星夜天空和后处理，不再自动覆盖成暖色日景。完整重建场景后，应再次执行星夜主题菜单。

## 本次验证

- 五个场景修改前后的冻结玩法配置签名一致；场景无缺失脚本。
- 天空着色器和 C# 编译无错误。
- 11 项 EditMode 检查通过：夜景可读性、UI 元素契约、视觉修复不改变玩法、修复幂等性、地图三场景检查、地图四场景检查及分层布局绑定。
- 使用 Play Mode 检查地图 UI、星空和人物视角。未执行完整战斗通关或目标平台打包。
