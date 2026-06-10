# 窗口管理、模态对话框、跨组件传值 — 需求分析 v2

## 1. 新窗口的打开与关闭

### 建议 API

```javascript
// 打开新窗口（独立 OS 窗口）
let editWin = app.openWindow(window({
    "title" = "编辑用户",
    "width" = 400, "height" = 300,
    "content" = editForm,
    "onClose" = () => { app.log("窗口已关闭") }
}))

// 程序化关闭
editWin.close()
```

### 实现方案

| 能力 | C# 实现 | 脚本 API |
|------|---------|----------|
| 打开窗口 | `ControlBuilder.BuildWindow(desc)` → `new Window()` → `Show()` | `app.openWindow(desc)` → 返回 WindowHandle |
| 关闭窗口 | `Window.Close()` | `handle.close()` |
| 关闭回调 | `Window.Closed += Lambda` | `"onClose" = () => {...}` |

`WindowHandle` 是一个 ObjectValue：
```
{
    close: FunctionValue → 调用 Window.Close()
    window: ObjectValue → 原始窗口描述符
}
```

---

## 2. 模态对话框（参照 Element-Plus `<el-dialog>`）

### 设计理念

**不依赖系统 Window**。对话框是一个 **浮层组件**（overlay），渲染在主窗口内容之上。类似 Element-Plus 的 `<el-dialog>`：

```
┌──────────────────────────────────────┐
│ 主窗口内容                            │
│ ┌──────────────────────────────────┐ │
│ │ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │ │ ← 半透明遮罩（覆盖全窗口）
│ │ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │ │
│ │ ░░░░  ┌─────────────────┐  ░░░░ │ │
│ │ ░░░░  │ 对话框标题  ✕    │  ░░░░ │ │ ← 居中卡片
│ │ ░░░░  │                  │  ░░░░ │ │
│ │ ░░░░  │  自定义内容区域    │  ░░░░ │ │
│ │ ░░░░  │                  │  ░░░░ │ │
│ │ ░░░░  │     [确定] [取消]  │  ░░░░ │ │
│ │ ░░░░  └─────────────────┘  ░░░░ │ │
│ │ ░░░░░░░░░░░░░░░░░░░░░░░░░░░░░░ │ │
│ └──────────────────────────────────┘ │
└──────────────────────────────────────┘
```

### 脚本 API

```javascript
// ═══ 声明式对话框 ═══
dialog({
    "visible" = showDialog,    // inpc(bool) 控制显隐
    "title" = "编辑用户",       // 标题
    "width" = 500,             // 卡片宽度（默认 500）
    "closable" = true,         // 是否显示关闭按钮
    "maskClosable" = true,     // 点击遮罩是否关闭
    "content" = stackpanel({   // 对话框内容
        "children" = [
            label({"text" = "这是对话框内容"}),
            button({"text" = "确定", "onClick" = () => {
                showDialog.set(false)
            }})
        ]
    }),
    "onClose" = () => {        // 关闭回调
        app.log("对话框已关闭")
    }
})

// ═══ 打开 ═══
showDialog.set(true)

// ═══ 关闭 ═══
showDialog.set(false)
```

### 实现方案

**C# 新增 `dialog` 控件类型**，在 `ControlBuilder` 中构建：

```
dialog 描述符:
{
    __type: "dialog",
    visible: InpcValue(bool),
    title: StringValue,
    width: NumberValue,
    closable: BoolValue,
    maskClosable: BoolValue,
    content: ObjectValue (子控件描述符),
    onClose: ICallable (回调 Lambda),
}
```

**BuildDialog 构建流程：**

1. 创建 `Grid` 作为 overlay 容器（`IsVisible` 绑定 `visible`）
2. Grid 第一层：半透明 `Border`（遮罩，`background="#80000000"`）
   - 若 `maskClosable=true`，点击遮罩 → `visible.set(false)`
3. Grid 第二层：居中 `Border`（卡片）
   - 顶部：标题栏（title + 关闭按钮）
   - 中部：`BuildInternal(content)` 构建内容
   - `width` 控制卡片最大宽度
4. 此 Grid 作为 dialog 的返回控件 → 插入到主窗口的 **最顶层**（通过 Adorner 或根 Grid 叠加层）

**关键问题：如何让 dialog overlay 覆盖整个窗口？**

方案：在 `BuildWindow` 时，创建一个顶层 `Grid` 包装原始内容 + dialog 区：

```csharp
// BuildWindow 改造
var rootGrid = new Grid();
rootGrid.Children.Add(originalContent);       // 第 0 层：主内容
rootGrid.Children.Add(dialogOverlayLayer);    // 第 1 层：dialog 浮层
window.Content = rootGrid;
```

每个 `dialog()` 调用返回的控件放到 `dialogOverlayLayer` 中，通过 `visible` 控制显隐。

### Element-Plus 核心特性对照

| Element-Plus `<el-dialog>` | 本方案 | 实现 |
|---------------------------|--------|------|
| `v-model` / `visible` | `"visible" = inpc(bool)` | InpcValue 双向绑定 |
| `title` | `"title" = "..."` | StringValue |
| `width` | `"width" = 500` | NumberValue |
| `modal` (遮罩) | 始终有 | 半透明 Border |
| `close-on-click-modal` | `"maskClosable" = true` | 遮罩点击事件 |
| `show-close` | `"closable" = true` | 关闭按钮 |
| `before-close` | `"onClose" = () => {...}` | Lambda 回调 |
| `destroy-on-close` | 控件保留在树中 | 不销毁，仅隐藏 |
| 自定义 content | `"content" = stackpanel({...})` | 任意控件树 |
| 自定义 footer | 在 content 中手动构建 | 按钮写在 content 内 |
| ESC 关闭 | `"escClosable" = true` | onKeyDown 监听 |
| 打开/关闭动画 | 暂不支持 | 后续扩展 |
| 嵌套 dialog | 支持（多层 overlay） | 每层独立 visible |

### 与系统 Window 的对比

| 特性 | 系统 Window `showDialog` | 组件 Dialog `dialog()` |
|------|--------------------------|------------------------|
| 无边框 | ❌ 有标题栏 | ✅ 完全自定义 |
| 遮罩层 | ❌ 系统默认 | ✅ 自定义半透明 |
| 样式定制 | ❌ 受限于 OS | ✅ 完全脚本控制 |
| 返回值 | ⚠️ 需要 hack | ✅ inpc 驱动，天然双向 |
| 动画 | ❌ | 后续可扩展 |
| 实现复杂度 | 🟢 低 | 🟡 中 |

---

## 3. 跨组件 / 窗口传值

### 已有机制（无需额外开发）

```javascript
// data.script — 共享状态（类似 Pinia store）
var selectedUser = inpc(null)
return { "selectedUser" = selectedUser }

// main.script — 任意模块读取
import { selectedUser } from "data.script"
var detailText = computed(() => {
    let u = selectedUser.get()
    return u == null ? "未选择" : u.name
})
```

InpcValue 通过 `import/export` 共享的 ObjectValue 引用是同一个 C# 对象 → `.set()` 跨模块自动生效 → `computed()` 链式响应。

### 建议新增

| 新增 | 脚本 API | 说明 |
|------|----------|------|
| 事件总线 | `app.on("event", cb)` / `app.emit("event", data)` | 发布订阅，解耦通信 |
| 窗口传参 | `app.openWindow(desc, params)` → 窗口内 `app.windowParams` | 新窗口初始化数据 |

### 事件总线实现

```javascript
// 模块 A: 发送事件
app.emit("userSelected", user)

// 模块 B: 接收事件
app.on("userSelected", (user) => {
    app.log("收到: " + user.name)
})
```

**C# 实现：** `ConcurrentDictionary<string, List<ICallable>>`，`emit` 时逐个调用 Lambda。

---

## 实施计划

| 优先级 | 任务 | 预估工作量 | 依赖 |
|--------|------|-----------|------|
| **P0** | `dialog()` 浮层组件（含遮罩、居中卡片、visible 控制） | 中 | 无 |
| **P0** | 主窗口改造（Grid 叠加层容纳 dialog） | 低 | 无 |
| **P1** | `app.openWindow` / `editWin.close()` / `onClose` | 中 | 无 |
| **P1** | 事件总线 `app.on` / `app.emit` | 低 | 无 |
| **P2** | 窗口传参 `app.windowParams` | 低 | P1 |
| **P2** | dialog ESC 关闭 / 动画 | 低 | P0 |

### Q3 解答：销毁 vs 隐藏

| 维度 | 销毁 (destroy-on-close=true) | 隐藏 (destroy-on-close=false，默认) |
|------|---------------------------|----------------------------------|
| **控件树** | 从视觉树移除 + Dispose | 保留在视觉树，`IsVisible=false` |
| **内存** | 释放 | 保留（占用少量内存） |
| **事件处理器** | 注销 | 保持注册 |
| **InpcValue 状态** | 随控件树销毁，表单输入丢失 | 完整保留（文本、选中项、滚动位置） |
| **重新打开** | 慢：重新 Build → PropertyBinder → 注册事件 | 快：仅 `IsVisible=true` |
| **适用场景** | 简单确认框、一次性弹窗 | 编辑表单、复杂配置面板 |
| **Element-Plus 默认** | — | ✅ 默认隐藏 |

**推荐：默认隐藏。** 对标 Element-Plus 行为。提供 `"destroyOnClose" = true` 选项按需开启销毁。

---

### 更新后的待确认

| # | 问题 | 确认结果 |
|---|------|----------|
| Q1 | 遮罩颜色：固定还是可配置？ | ✅ **可配置**，`"maskBackground" = "#80000000"` |
| Q2 | 是否需要 `fullscreen` 模式？ | ✅ **需要**，`"fullscreen" = true` |
| Q3 | 销毁 vs 隐藏？ | ✅ **默认隐藏**（保留状态），`"destroyOnClose" = true` 开启销毁 |
| Q4 | 实施范围？ | ✅ **仅 P0**：dialog 浮层组件 + 主窗口改造 |
