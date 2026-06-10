# 窗口管理、模态对话框、跨组件传值 — 需求分析与方案

## 1. 新窗口的打开与关闭

### 当前行为

```csharp
// MainWindow.axaml.cs 的 Loaded 事件：
var scriptWindow = controlBuilder.BuildWindow(rootDescriptor);
scriptWindow.Show();
Close();  // ← 关闭 Loader 窗口
```

**问题：**
- Loader 窗口启动 → 执行脚本 → 新建脚本 Window → 关闭 Loader → 只留一个窗口
- 如果脚本想打开第二个窗口（如编辑用户弹窗），没有 API 支持
- 脚本 Window 关闭时没有生命周期钩子通知脚本

### 缺失能力

| 需求 | 当前状态 |
|------|----------|
| `app.openWindow(descriptor)` 打开新窗口 | ❌ |
| `window.close()` 关闭自身 | ❌（窗口由宿主管理，脚本无法操作） |
| 窗口关闭回调 `onClose` | ❌ 事件未实现 |
| 多窗口管理 | ❌ |

### 建议方案

```javascript
// 脚本中打开新窗口
let editWin = app.openWindow(window({
    "title" = "编辑用户",
    "width" = 400, "height" = 300,
    "content" = editForm
}))

// 窗口关闭事件
window({
    "onClose" = () => { app.log("窗口已关闭") }
})

// 关闭当前窗口
app.closeWindow()  // 或 this.close()
```

**实现要点：**
- `app.openWindow(descriptor)` → ControlBuilder.BuildWindow → new Window → Show()
- `onClose` → Window.Closed 事件 → 传入空参数调用脚本 Lambda
- `app.closeWindow()` → 获取当前 Window → Close()

---

## 2. 模态窗口 / 对话框支持

### 当前行为

```csharp
// AvaloniaModule.cs — ShowCustomDialog
var dialog = new Window { Content = builtContent };
dialog.ShowDialog(MainWindow);  // 模态！
```

已有 `app.showDialog(content, title)`，底层使用 `Window.ShowDialog()` 实现模态。

### 存在的问题

| 问题 | 说明 |
|------|------|
| **对话框内按钮无法关闭对话框** | 对话框内"确定"/"关闭"按钮的 onClick 无权限调用 `dialog.Close()` |
| **无法返回值** | 打开对话框后无法获取用户选择结果（确定/取消、表单数据） |
| **showMessage/showConfirm 用裸 Window** | 样式不统一，没有应用脚本级样式 |
| **无遮罩层** | 原生 `ShowDialog` 有系统级模态，但无自定义遮罩效果 |

### 建议方案

```javascript
// 打开模态对话框，await 返回结果
let result = app.showModal(editForm, "编辑用户")
if (result == "ok") {
    app.log("用户点击了确定")
}
```

**实现要点：**
- `showDialog` 返回 `Task<Value>`，对话框关闭时 resolve
- 对话框内通过 `app.closeModal(value)` 关闭并传值
- 对话框 Window 自动查找并注入 `closeModal` 函数到脚本作用域

```javascript
// 对话框内部脚本
button({"text" = "确定", "onClick" = () => {
    app.closeModal("ok")    // 关闭对话框，返回 "ok"
}})
button({"text" = "取消", "onClick" = () => {
    app.closeModal("cancel")
}})
```

**等待机制：** `ShowDialog()` 本身是同步阻塞的（模态窗口），所以 `showModal` 可以在 UI 线程同步等待：

```csharp
// C# 侧
public Value ShowModal(ObjectValue content, string title)
{
    var tcs = new TaskCompletionSource<Value>();
    var dialog = new Window { ... };
    // 注册 closeModal 到对话框作用域
    dialog.Closed += (_, _) => tcs.TrySetResult(_modalResult);
    dialog.ShowDialog(MainWindow);
    return tcs.Task.GetAwaiter().GetResult(); // 同步等待
}
```

---

## 3. 跨组件 / 窗口传值

### 当前状态

| 机制 | 状态 | 说明 |
|------|------|------|
| 模块导入导出 | ✅ | `import { x } from "module.script"` → ObjectValue 引用共享 |
| InpcValue 共享引用 | ✅ | 导入的 inpc 是同一个 C# 对象，`.set()` 跨模块生效 |
| 父→子传值 | ⚠️ | 可通过模块导入实现，但需显式 import |
| 子→父通知 | ⚠️ | 可通过共享 inpc 实现，子模块调用 `.set()` → 父模块 computed 响应 |
| 窗口间传值 | ❌ | 无机制，新窗口是独立 Window 实例 |
| 事件总线 | ❌ | 无全局事件系统 |

### 当前最接近的实现

```javascript
// data.script — 共享状态
var selectedUser = inpc(null)
return { "selectedUser" = selectedUser, ... }

// main.script — 读取
import { selectedUser } from "data.script"
var detailText = computed(() => {
    let u = selectedUser.get()
    if (u == null) { return "未选择" }
    return u.name
})

// 点击编辑按钮 → 更新共享状态
button({"onClick" = () => {
    selectedUser.set(user)  // 跨组件通知
}})
```

**这已经实现了类似 Vue 的 provide/inject 或 pinia store 的效果！**

### 建议补充

| 新增 | 脚本 API | 说明 |
|------|----------|------|
| 事件总线 | `app.on("eventName", callback)` / `app.emit("eventName", data)` | 解耦的跨组件通信 |
| 窗口传参 | `app.openWindow(desc, {"userId" = 123})` → `app.windowParams` | 打开窗口时传参 |
| 环境变量 | `app.env` — 全局可读写的 ObjectValue | 类似 Vue 的 provide/inject，全局共享状态 |

---

## 建议实施优先级

| 优先级 | 功能 | 理由 |
|--------|------|------|
| **P0** | `showModal` 返回值 | 当前 showDialog 按钮无法关闭对话框，形同虚设 |
| **P0** | `onClose` 窗口关闭事件 | 窗口生命周期基本需求 |
| **P1** | `app.openWindow` 多窗口 | 编辑表单等场景 |
| **P1** | `app.closeWindow` / `app.closeModal` | 程序化关闭 |
| **P2** | 事件总线 `app.on/emit` | 解耦通信 |
| **P2** | 窗口传参 `windowParams` | 新窗口初始化数据 |

---

## 待确认

| # | 问题 |
|---|------|
| Q1 | `showModal` 的返回值模型：确定/取消 二值，还是任意 Value？ |
| Q2 | 是否需要对话框遮罩层（半透明背景覆盖主窗口）？ |
| Q3 | 事件总线是否需要命名空间/作用域隔离？还是全局单例？ |
| Q4 | 实施范围：P0 两项 + P1 两项，还是先只做 P0？ |
