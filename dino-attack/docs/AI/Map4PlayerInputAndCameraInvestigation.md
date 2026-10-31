# 地图4玩家操控与队友交互输入问题检查

检查日期：2026-09-28  
检查场景：`Assets/LlamAcademy/Dinos/Scenes/FirstPersonDefenseBattlefield.unity`

## 结论

当前有两个独立问题：

1. 玩家点击自然语言输入框后，UI Toolkit 会让文本框获得键盘焦点。代码把这个焦点解释为“暂停战斗输入”，因此会清零 WASD 移动，并且暂时不处理视角和技能输入。
2. 常驻 JEV 模式会主动关闭鼠标锁定。此模式下，角色视角只在“世界区域按住鼠标右键拖动”时更新，普通移动鼠标不会转向；从队友面板按下右键后，整次拖动也会被屏蔽。

当前工程没有实现 Tab 开关面板。因此按 Tab 目前不能直接修复这两个问题，反而可能触发 UI Toolkit 自己的焦点移动。

## 证据链

### 移动被队友交互中断

`Assets/LlamAcademy/Dinos/Companion/UI/CompanionDialoguePanel.cs` 的 `Update` 每帧执行：

```csharp
Player.SetDialogueInputBlocked(IsInputFocused());
```

`IsInputFocused()` 会检查自然语言 `TextField` 是否仍然获得 UI 焦点。输入框获得焦点后，`FirstPersonDefender.Update` 立即进入阻塞分支：

```csharp
LastMoveDirection = Vector3.zero;
SimulateMovement(Vector2.zero, false, false, Time.deltaTime);
return;
```

因此 WASD、视角和战斗输入都会停止。当前只有发送命令、点击快捷命令、或输入框处理 Enter 时才会 `Blur()`；Esc 按键按现有设计不会关闭面板，也不会释放文本框焦点。

### 角色无法正常转向

`CompanionDialoguePanel.Update` 会持续调用 `ConfigurePersistentInteraction(true)`。该模式会：

- 强制 `LookCaptured = false`；
- 解锁鼠标并显示鼠标指针；
- 进入 `UpdatePersistentInteraction()`。

在 `FirstPersonDefender.UpdatePersistentInteraction()` 中，视角更新条件是：

```csharp
mouse.rightButton.isPressed && !InteractionPointerOverUi
```

所以当前控制方式实际是“按住右键拖动视角”。`ThirdPersonFollowCamera.ApplyLook()` 本身的 Yaw/Pitch 计算正常，问题出在上游输入模式和右键门槛。常驻模式下 `IsLookCaptured` 一直为 false，准星也被隐藏，容易让玩家误以为视角系统没有工作。

运行态检查地图4战斗演示后，得到的状态是：

- `PersistentInteractionEnabled = true`
- `IsLookCaptured = false`
- `BlockCombatInput = false`（未聚焦输入框时）
- 鼠标处于解锁状态

这与上述代码路径一致。

## Tab 是否适合

适合把 Tab 设计成“交互面板显示/隐藏”和“玩家操控模式”的明确切换键，但需要同时修改输入状态，单纯隐藏 UI 不够。

建议行为：

### 面板隐藏时

- 隐藏 JEV 面板；
- `ConfigurePersistentInteraction(false)`；
- 清除 `DialogueInputBlocked`；
- 如果玩家正在战斗，调用 `SetLookCaptured(true)`，锁定鼠标；
- 恢复普通鼠标转向、WASD 移动、攻击和技能输入。

### 面板显示时

- 面板常驻显示，但不阻塞 WASD 移动；
- 只有自然语言输入框获得焦点时，才暂停移动和战斗输入；
- Enter 发送命令并释放文本框焦点；
- Tab 在输入框中应先释放焦点，再切换面板，不能留在 UI Toolkit 的默认焦点导航中；
- 从面板按下的右键不应改变视角，离开面板后再按右键才开始拖动。

## 推荐实现顺序

1. 增加一个明确的 `JevInteractionMode`：`Gameplay`、`PanelVisible`、`TextEditing`，不要只用多个布尔值隐式推断状态。
2. 在 `FirstPersonDefender` 统一处理 Tab：切换面板和鼠标模式，并在切换时清除 `TextField` 焦点、攻击按键抑制状态和移动残留状态。
3. 把“文本框焦点”与“是否允许移动”分开：默认允许移动；只有用户明确进入文本编辑状态时才暂停移动。
4. 面板隐藏时恢复锁鼠标和自由视角；面板显示时继续使用右键拖动视角，并在 HUD 中显示“右键拖动视角”提示和准星状态。
5. 增加回归测试：Tab 开关、输入框 Enter/Tab 退出、面板内右键不转向、世界区域右键拖动转向、WASD 不因面板常驻而停止。

## 涉及文件

- `Assets/LlamAcademy/Dinos/Companion/UI/CompanionDialoguePanel.cs`
- `Assets/LlamAcademy/Dinos/DefenseBattle/FirstPersonDefender.cs`
- `Assets/LlamAcademy/Dinos/DefenseBattle/ThirdPersonFollowCamera.cs`
- `Assets/LlamAcademy/Dinos/DefenseBattle/FirstPersonDefenseHud.cs`

## 已实施

已按推荐方案完成修改：

- Tab 可以显示/隐藏 JEV 面板；切换时会清除文本框焦点和残留按键状态。
- 隐藏面板时退出常驻交互模式，恢复锁定鼠标和普通自由视角。
- 显示面板时保留右键拖动视角；文本框获得焦点时才暂停战斗输入。
- 输入框支持 Enter 发送并恢复操控，Esc 释放文本焦点，Tab 直接切换面板。
- HUD 准星会在常驻交互模式下正常显示，文本编辑时隐藏。

新增回归验证覆盖：面板 Tab 切换、文本焦点释放、鼠标视角状态恢复，以及原有地图4布防/撤回流程。
