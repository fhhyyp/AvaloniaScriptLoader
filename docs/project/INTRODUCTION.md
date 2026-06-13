# 项目介绍

AvaloniaScriptLoader 是一个基于 [SereinScript](https://github.com/fengjiayi/SereinScript) 脚本语言的 Avalonia UI 动态生成框架。开发者无需编译即可用 JavaScript 风格的脚本编写跨平台桌面界面，支持响应式数据绑定、组件化开发、模块化导入。

## 核心理念

> **Script → ObjectValue → Avalonia Control Tree → Cross-Platform Desktop App**

用脚本描述 UI，运行时动态构建原生控件，兼顾开发效率与运行性能。

## 核心特性

| 特性 | 说明 |
|------|------|
| 🧩 **声明式 UI** | 嵌套对象定义界面结构，直观简洁 |
| 🔄 **响应式数据** | `inpc()` / `computed()` / `twoway` 自动追踪依赖，UI 即时更新 |
| 🧱 **组件化** | `component()` 定义可复用组件，支持 props 传参 |
| 📦 **模块化** | `import { ... } from "module.script"` 跨文件导入，支持动态 `import()` |
| 🔀 **条件/列表渲染** | `vif()` / `vfor()` 声明式控制 UI 结构 |
| 🪟 **浮层对话框** | Element-Plus 风格的 `dialog()` 组件，遮罩 + 居中卡片 |
| 🎨 **样式系统** | `style()` 注册全局样式类，支持伪类 `:pointerover` / `:pressed` / `:focus` / `:disabled` |
| 📝 **CSS 简写** | `margin`/`padding` 支持 `"2,4,6,8"` / `[2,4]` / `12` 多种格式 |
| 📊 **DataTable** | 内置分页、排序、单选/多选、模板列、响应式数据源 |
| 🧭 **NavMenu** | 侧边导航菜单，支持分组折叠/展开 |
| 🌐 **HTTP 客户端** | 内置 `fetch()` / `fetchAsync()` 同步/异步请求 |
| 💬 **Toast 通知** | `app.toast(msg, type)` 非阻塞消息提示 |
| 🖥️ **跨平台** | 基于 Avalonia 12.x，支持 Windows / macOS / Linux |
| ⚡ **热加载** | 修改脚本即见效果，无需重新编译 |

## 技术栈

```
.NET 10  +  Avalonia 12.0.4  +  SereinScript 脚本引擎
```

## 一个例子

```javascript
import { inpc, computed } from "avalonia"
import { window, stackpanel, button, label } from "avalonia.controls"

var count = inpc(0)
var msg = computed(() => "计数: " + count.get())

return window({
    "title" = "计数器",
    "width" = 300, "height" = 200,
    "content" = stackpanel({
        "spacing" = 15, "padding" = 30,
        "children" = [
            label({"text" = msg, "fontSize" = 24}),
            button({"text" = "+1", "onClick" = () => {
                count.set(count.get() + 1)
            }}),
            button({"text" = "重置", "onClick" = () => {
                count.set(0)
            }})
        ]
    })
})
```

## 控件画廊

项目内置分类控件画廊（26 个独立 demo），启动即可浏览所有控件效果：

```
Samples/gallery-route/demos/
├── 基础控件     label  button  textbox
├── 选择控件     checkbox  combobox  listbox  slider
├── 进度与日期   progressbar  datepicker  timepicker
├── 容器与布局   border  scrollviewer  stackpanel  grid  tabcontrol  expander
├── 高级         dialog  image  contextmenu  datatable
└── 框架特性     reactive  tooltip  style  toast  pseudoclass  dynimport  fetch
```

## 相关链接

- [SereinScript 脚本引擎](https://github.com/fengjiayi/SereinScript)
- [Avalonia UI](https://avaloniaui.net/)
