# Unity UI 事件与 ExecuteEvents 学习笔记

本文总结 uGUI 按钮从识别点击，到调用业务方法的过程。源码依据是本次对话读取的项目本地 Unity 包；UI Toolkit 使用不同 API，不能直接套用本笔记中的类型。

## 1. 完整调用链

```text
输入模块获取指针位置、按下和松开状态
    ↓
UI 射线检测确定指针当前命中的对象
    ↓
按下时记录点击处理对象
    ↓
松开时检查对象是否相同、点击资格是否仍有效
    ↓
ExecuteEvents.Execute<IPointerClickHandler>(...)
    ↓
Button.OnPointerClick(eventData)
    ↓
Button.Press()
    ↓
m_OnClick.Invoke()
    ↓
用户通过 AddListener 登记的方法
```

两层通知要分清：

- UI 事件系统通知按钮组件：“发生点击了。”
- 按钮的 UnityEvent 通知业务代码：“执行点击后的功能。”

## 2. 如何登记按钮事件

```csharp
using UnityEngine;
using UnityEngine.UI;

public class TestUI : MonoBehaviour
{
    [SerializeField] private Button button;

    private void OnEnable()
    {
        button.onClick.AddListener(OnClick);
    }

    private void OnDisable()
    {
        button.onClick.RemoveListener(OnClick);
    }

    private void OnClick()
    {
        Debug.Log("点击按钮");
    }
}
```

前提：在 Inspector 中将实际 Button 组件绑定到 button 字段。

| 代码 | 含义 |
| --- | --- |
| `Button button` | 声明一个可保存按钮组件引用的变量，不会创建按钮 |
| `[SerializeField]` | 让 Unity 保存这个字段，并在 Inspector 中显示 |
| `AddListener(OnClick)` | 登记回调，将来触发时执行 |
| `OnClick()` | 立即调用方法 |
| `RemoveListener(OnClick)` | 移除通过代码登记的对应监听 |

`OnClick` 是普通方法，可以改名；`OnEnable`、`OnDisable` 是 Unity 生命周期方法。

组件停用不代表其他对象保存的回调自动消失，所以这里配对登记、取消。RemoveListener 不负责删除 Inspector 配置的持久化监听。

## 3. 输入模块如何判定点击

### 3.1 找到指针下面的 UI

输入模块调用：

```csharp
eventSystem.RaycastAll(eventData, m_RaycastResultCache);
```

普通 Canvas UI 通常由 GraphicRaycaster 参与检测。检测结果与指针位置、元素的射线设置、遮挡和排序等有关，不只是矩形包含判断。

当前命中对象来自：

```csharp
var currentOverGo = eventData.pointerCurrentRaycast.gameObject;
```

### 3.2 按下时记住对象

以下摘录保留关键逻辑：

```csharp
eventData.pressPosition = eventData.position;
eventData.eligibleForClick = true;

var pointerClickHandler =
    ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentOverGo);

eventData.pointerClick = pointerClickHandler;
```

GetEventHandler 会沿当前对象及父对象查找点击处理器。因此，命中按钮的子文字对象，也可能找到父对象上的 Button。

此时只是记录，不会立即触发按钮 onClick。

### 3.3 松开时比较

```csharp
var pointerClickHandler =
    ExecuteEvents.GetEventHandler<IPointerClickHandler>(currentOverGo);

var isClick =
    eventData.pointerClick != null &&
    eventData.pointerClick == pointerClickHandler &&
    eventData.eligibleForClick;
```

三个条件必须同时满足：

1. 按下时找到过点击处理对象。
2. 松开时找到同一个点击处理对象。
3. 点击资格没有被取消。

比较的是处理对象，不要求按下和松开的像素位置完全相同。

| 操作 | 在按钮可交互且点击资格有效的前提下 |
| --- | --- |
| A 上按下，A 上松开 | 点击成立 |
| A 上按下，B 上松开 | A、B 都不成立 |
| 空白处按下，A 上松开 | 不成立 |
| A 上按下，空白处松开 | 不成立 |
| A 上按下，移出再移回 A 松开 | 可能成立，离开本身不一定取消资格 |
| 只按住，没有松开 | 尚未完成这条鼠标点击路径 |

拖拽可能取消点击资格，但不是所有鼠标移动都必然取消。当前输入模块会检查拖拽处理对象、拖拽阈值等；进入拖拽且按下处理对象与拖拽处理对象不同时，会把 eligibleForClick 设为 false。

## 4. ExecuteEvents 是什么

它是 Unity 提供的静态工具类：

```csharp
public static class ExecuteEvents
{
    // UI 事件分发工具
}
```

无需创建实例，可以直接写 `ExecuteEvents.Execute(...)`。它不是场景组件，也不是按钮。

原始调用：

```csharp
ExecuteEvents.Execute(
    eventData.pointerClick,
    eventData,
    ExecuteEvents.pointerClickHandler);
```

| 参数 | 作用 |
| --- | --- |
| `eventData.pointerClick` | 目标 GameObject：通知谁 |
| `eventData` | 事件数据：携带指针位置、按键等信息 |
| `ExecuteEvents.pointerClickHandler` | 分发委托：对找到的组件执行什么操作 |

**第三个参数不是接口。**它是属性返回的委托值。

## 5. 三个容易混淆的名字

| 名称 | 类型 | 作用 |
| --- | --- | --- |
| `IPointerClickHandler` | 接口 | 约定组件提供 OnPointerClick 方法 |
| `pointerClickHandler` | 静态属性 | 返回点击分发委托 |
| `OnPointerClick` | 方法 | 接收点击通知 |

接口核心约定：

```csharp
public interface IPointerClickHandler : IEventSystemHandler
{
    void OnPointerClick(PointerEventData eventData);
}
```

Button 实现该接口，所以能够作为点击处理组件被找到。

## 6. 委托是什么，为什么能指定方法

委托（delegate）是可以保存并调用符合特定参数、返回值要求的方法的类型。

Unity 定义：

```csharp
public delegate void EventFunction<T1>(
    T1 handler,
    BaseEventData eventData);
```

`EventFunction<IPointerClickHandler>` 表示这种委托接收：

1. 一个 IPointerClickHandler 对象；
2. 一份 BaseEventData 数据；
3. 不返回结果（void）。

泛型参数 T1 是类型占位符，此处被 IPointerClickHandler 替换。

Unity 保存点击回调的真实代码：

```csharp
private static readonly EventFunction<IPointerClickHandler>
    s_PointerClickHandler = Execute;

private static void Execute(
    IPointerClickHandler handler,
    BaseEventData eventData)
{
    handler.OnPointerClick(
        ValidateEventData<PointerEventData>(eventData));
}

public static EventFunction<IPointerClickHandler> pointerClickHandler
{
    get { return s_PointerClickHandler; }
}
```

`= Execute` 没有括号，表示把方法交给委托，而不是立即执行。

**之所以调用 OnPointerClick，是因为回调方法里明确写了 handler.OnPointerClick(...)，不是 Unity 根据名字猜测。**接口保证这个方法存在；回调代码负责真正调用。

ValidateEventData 用于检查事件数据是否属于所需类型，并返回对应类型的数据。

## 7. 为什么有两个 Execute

这是方法重载：名字相同，但参数不同。

公开的分发入口：

```csharp
public static bool Execute<T>(
    GameObject target,
    BaseEventData eventData,
    EventFunction<T> functor)
    where T : IEventSystemHandler
```

内部的点击回调：

```csharp
private static void Execute(
    IPointerClickHandler handler,
    BaseEventData eventData)
```

第一个负责找组件并循环，第二个负责调用 OnPointerClick。

公开入口的核心语句是：

```csharp
GetEventList<T>(target, internalHandlers);

// 在循环中，对每个找到的组件调用：
functor(arg, eventData);
```

functor 只是委托参数的名字，可理解为 callback。

第三个参数的类型允许编译器推断 T，因此原始调用等价于显式指定：

```csharp
ExecuteEvents.Execute<IPointerClickHandler>(
    eventData.pointerClick,
    eventData,
    ExecuteEvents.pointerClickHandler);
```

注意：GetEventHandler 可以向父级寻找目标；这里的 Execute 分发给指定目标对象上的有效处理组件，不会自动向所有父对象广播。

## 8. Button 怎样触发 UnityEvent

Button 接到通知：

```csharp
public virtual void OnPointerClick(PointerEventData eventData)
{
    if (eventData.button != PointerEventData.InputButton.Left)
        return;

    Press();
}
```

Press 的实际核心代码：

```csharp
private void Press()
{
    if (!IsActive() || !IsInteractable())
        return;

    UISystemProfilerApi.AddMarker("Button.onClick", this);
    m_OnClick.Invoke();
}
```

AddMarker 是性能分析标记；Invoke 才是触发事件的调用。

事件的定义与公开属性：

```csharp
public class ButtonClickedEvent : UnityEvent {}

[SerializeField]
private ButtonClickedEvent m_OnClick = new ButtonClickedEvent();

public ButtonClickedEvent onClick
{
    get { return m_OnClick; }
    set { m_OnClick = value; }
}
```

因此，button.onClick 的准确类型是 Button.ButtonClickedEvent，它继承无参数 UnityEvent。

按钮也支持提交事件 OnSubmit，例如配置好的键盘或手柄提交操作；本笔记主要展开鼠标点击路径。

## 9. 其他 uGUI 控件

| 控件 | 常用事件 | 回调参数 |
| --- | --- | --- |
| Button | onClick | 无参数 |
| Toggle | onValueChanged | bool |
| Slider | onValueChanged | float |
| Dropdown | onValueChanged | int，选项索引 |
| InputField | onValueChanged、onEndEdit | string |

回调参数必须与事件类型匹配。值变化通知也可能由代码修改控件值引起，不一定来自用户操作。

UI Toolkit 中同名 Button、Slider 等控件不是这里的 uGUI 类型，先看命名空间再判断 API。

## 10. 自测

1. `AddListener(OnClick)` 与 `OnClick()` 有什么区别？
2. `pointerClickHandler` 为什么不是接口？
3. 哪一行明确决定调用 OnPointerClick？
4. 在 A 上按下、B 上松开，为什么两者都不应触发按钮点击？
5. ExecuteEvents 的分发与 UnityEvent.Invoke 分别通知哪一层？

参考答案：

1. 前者登记方法，后者立即执行。
2. 它是返回 `EventFunction<IPointerClickHandler>` 委托的属性；接口是 IPointerClickHandler。
3. 内部点击回调中的 `handler.OnPointerClick(...)`。
4. 松开时的点击处理对象与按下时记录的对象不相同。
5. ExecuteEvents 通知符合接口的 UI 组件；按钮的 UnityEvent.Invoke 调用登记的业务回调。

## 11. 本地源码索引

以下路径相对于项目根目录，行号依据本次对话读取版本。Library/PackageCache 属于生成的包缓存，更新包后目录和行号可能改变；检索时优先使用类名与方法名。

- `Library/PackageCache/com.unity.ugui@20629863fcab/Runtime/UGUI/EventSystem/ExecuteEvents.cs`
  - 第 7–9 行：静态类、EventFunction 委托。
  - 第 53–58 行：点击委托及调用 OnPointerClick 的回调。
  - 第 169–172 行：pointerClickHandler 属性。
  - 第 248–282 行：公开 Execute<T> 的查找与调用循环。
- `Library/PackageCache/com.unity.ugui@20629863fcab/Runtime/UGUI/UI/Core/Button.cs`
  - 第 19–24 行：事件类型与字段。
  - 第 58–71 行：onClick 属性、Press 与 Invoke。
  - 第 109–115 行：OnPointerClick。
  - 第 148–158 行：OnSubmit。
- `Library/PackageCache/com.unity.inputsystem@7a4e1a2a8194/InputSystem/Runtime/Plugins/UI/InputSystemUIInputModule.cs`
  - 第 413 行：RaycastAll。
  - 第 633–745 行：ProcessPointerButton 的按下、松开与点击分发。
  - 第 748–783 行：ProcessPointerButtonDrag 的拖拽与点击资格处理。

本笔记为源码阅读总结，没有修改 Unity 源码、项目脚本或场景，也没有运行点击测试。
