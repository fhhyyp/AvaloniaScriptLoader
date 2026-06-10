# AvaloniaScriptLoader

通过自定义脚本语言动态生成 Avalonia UI 的框架。声明式语法描述界面结构，脚本执行后返回对象树，框架自动构建原生 Avalonia 控件。

## 项目介绍

AvaloniaScriptLoader 是一个基于 [SereinScript](https://github.com/) 脚本语言的 Avalonia UI 动态生成框架。开发者无需编译即可用 JavaScript 风格的脚本编写跨平台桌面界面，支持响应式数据绑定、组件化开发、模块化导入。

### 核心特性

- **声明式 UI** — 嵌套对象定义界面结构，直观简洁
- **响应式数据** — `inpc()` / `computed()` / `twoway` 自动追踪依赖，UI 即时更新
- **组件化** — `component()` 定义可复用组件，支持 props 传参
- **模块化** — `import { ... } from "module.script"` 跨文件导入
- **条件/列表渲染** — `vif()` / `vfor()` 声明式控制 UI 结构
- **浮层对话框** — Element-Plus 风格的 `dialog()` 组件，遮罩 + 居中卡片
- **样式系统** — `style()` 注册全局样式类，`class` 属性复用
- **CSS 简写** — `margin`/`padding` 支持 `"2,4,6,8"` / `[2,4]` 格式
- **跨平台** — 基于 Avalonia 12.x，支持 Windows / macOS / Linux

### 一个例子

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

## 项目架构

```
AvaloniaScriptLoader/
├── AvaloniaScriptLoader.sln
├── README.md
├── docs/                            # 用户文档
├── AvaloniaScriptLoader/            # 主项目
│   ├── AvaloniaScriptLoader.csproj  # .NET 10 + Avalonia 12.0.4
│   ├── Program.cs                   # 应用入口
│   ├── App.axaml / .cs              # Avalonia 生命周期
│   ├── MainWindow.axaml / .cs       # 脚本加载与窗口管理
│   ├── ScriptEngineAdapter.cs       # 引擎适配器（生命周期、模块注册）
│   ├── Model/                       # 数据模型
│   │   ├── InpcValue.cs             # 响应式值（INotifyPropertyChanged）
│   │   ├── ComputedValue.cs         # 计算属性（Vue computed）
│   │   ├── ReactiveTracker.cs       # 依赖自动追踪
│   │   ├── ControlMeta.cs           # 控件类型元数据
│   │   ├── PropertyNames.cs         # 属性名常量与工具
│   │   └── Log.cs                   # 日志抽象
│   ├── Factory/                     # 工厂层
│   │   ├── ControlFactory.cs        # 19 种控件 ObjectValue 工厂
│   │   ├── InpcFactory.cs           # inpc()/computed() 工厂
│   │   ├── StructureFactory.cs      # vif/vfor/component 结构化指令
│   │   └── StyleFactory.cs          # style() 样式注册
│   ├── Builder/                     # 构建层
│   │   ├── ControlBuilder.cs        # ObjectValue → Avalonia Control（含 dialog/vif/vfor）
│   │   └── PropertyBinder.cs        # 属性映射、类型转换、缩写展开
│   ├── Wrapper/
│   │   └── ControlWrapper.cs        # 控件包装器（setter 两阶段激活）
│   ├── Modules/                     # 内置模块
│   │   ├── AvaloniaModule.cs        # "avalonia" 系统模块
│   │   ├── ControlsModule.cs        # "avalonia.controls" 控件模块
│   │   └── HttpModule.cs            # fetch() HTTP 请求
│   └── Samples/                     # 示例脚本
│       ├── HelloWorld.script
│       ├── Counter.script
│       ├── ComputedBinding.script
│       ├── InpcBinding.script
│       ├── TodoList.script
│       ├── ProductionDemo.script
│       ├── demo/                    # 模块化示例
│       ├── demo-dialog/            # Dialog 组件测试
│       └── demo-controls/           # 新控件测试
└── (依赖)
    └── ScriptLang.dll               # SereinScript 脚本引擎
```

### 架构分层

```
脚本层 (.script)
    ↓ import 内置模块
工厂层 (Factory)
    ↓ ObjectValue 描述符
构建层 (Builder)
    ↓ Avalonia Control 树
Avalonia UI
```

## 如何使用

### 环境要求

- .NET 10 SDK
- Windows / macOS / Linux

### 快速开始

```bash
git clone <repo-url>
cd AvaloniaScriptLoader
dotnet run
```

应用启动后自动加载 `Samples/demo-controls/main.script`。可通过环境变量指定脚本：

```bash
SCRIPT_PATH=./Samples/Counter.script dotnet run
```

### 编写脚本

```javascript
// 1. 导入模块
import { inpc } from "avalonia"
import { window, label } from "avalonia.controls"

// 2. 构建界面并返回
return window({
    "title" = "Hello",
    "content" = label({"text" = "Hello World!"})
})
```

详细示例见 [Samples/](AvaloniaScriptLoader/Samples/) 目录。

## Avalonia API

### 模块 "avalonia"

```javascript
import { app, inpc, computed, vif, vfor, component, style, fetch } from "avalonia"
```

| 导出 | 类型 | 说明 |
|------|------|------|
| `app` | ObjectValue | 应用级 API（showMessage / showConfirm / showDialog / openFile / saveFile / log / find / focus） |
| `inpc(value, mode?)` | FunctionValue | 响应式值，`"twoway"` 启用双向绑定 |
| `computed(fn)` | FunctionValue | 计算属性，自动追踪依赖 |
| `vif(cond, el)` | FunctionValue | 条件渲染 |
| `vfor(arr, fn)` | FunctionValue | 列表渲染 |
| `component(fn)` | FunctionValue | 组件定义 |
| `style(name, props)` | FunctionValue | 注册全局样式类 |
| `fetch(url, opts?)` | FunctionValue | HTTP 请求 |

### 模块 "avalonia.controls"

19 种可用控件：

| 控件 | 说明 |
|------|------|
| `window` | 窗口 |
| `button` | 按钮 |
| `label` | 文本标签 |
| `textbox` | 文本输入框 |
| `checkbox` | 复选框 |
| `combobox` | 下拉框 |
| `listbox` | 列表框 |
| `stackpanel` | 线性布局 |
| `grid` | 网格布局 |
| `border` | 边框容器 |
| `scrollviewer` | 滚动视图 |
| `tabcontrol` / `tabitem` | 标签页 |
| `image` | 图片 |
| `dialog` | 浮层对话框 |
| `datepicker` | 日期选择 |
| `slider` | 滑块 |
| `progressbar` | 进度条 |
| `datagrid` | 数据表格（反射，需安装 Avalonia.Controls.DataGrid） |

### 通用属性

所有控件支持：`width` `height` `minWidth` `minHeight` `maxWidth` `maxHeight` `margin` `padding` `visible` `enabled` `name` `horizontalAlignment` `verticalAlignment` `tooltip` `class`

### 通用事件

所有控件支持：`onClick` `onChange` `onSelect` `onKeyDown` `onFocus` `onBlur`

### 属性缩写

| 缩写 | 全名 |
|------|------|
| `bg` | `background` |
| `size` | `fontSize` |
| `halign` | `horizontalAlignment` |
| `valign` | `verticalAlignment` |
| `radius` | `cornerRadius` |

### 响应式 API

```javascript
var x = inpc(0)              // 普通响应式
var x = inpc("", "twoway")   // 双向绑定
var y = computed(() => x.get() + 1)  // 计算属性

x.get()                      // 读取
x.set(5)                     // 写入 + 通知
x.push(item)                 // 数组追加（自动通知）
x.pop()                      // 数组弹出（自动通知）
x.removeAt(0)                // 数组删除（自动通知）
```

## 如何二次开发

### 添加新控件

1. `Model/ControlMeta.cs` — 添加类型常量
2. `Factory/ControlFactory.cs` — 添加工厂方法
3. `Builder/ControlBuilder.cs` — `CreateNativeControl()` + 如需特殊处理
4. `Builder/PropertyBinder.cs` — 属性映射
5. `Modules/ControlsModule.cs` — 注册导出

### 添加新事件

1. `Builder/ControlBuilder.cs` — `RegisterEvents()` 中注册 Avalonia 事件
2. `Factory/ControlFactory.cs` — 加入通用属性列表（所有控件支持）

### 添加新模块

1. `Modules/` 下新建模块类
2. `ScriptEngineAdapter.RegisterBuiltinModules()` 中注册

### 项目依赖

- `.NET 10`
- `Avalonia 12.0.4` (Desktop + Themes.Fluent + Fonts.Inter)
- `ScriptLang.dll` (SereinScript 脚本引擎)

## 文档目录

| 文档 | 说明 |
|------|------|
| [README.md](README.md) | 项目门户（本文件） |
| [Samples/](AvaloniaScriptLoader/Samples/) | 示例脚本集 |
| [Samples/demo/](AvaloniaScriptLoader/Samples/demo/) | 模块化示例 |
| [Samples/demo-dialog/](AvaloniaScriptLoader/Samples/demo-dialog/) | Dialog 组件测试 |
| [Samples/demo-controls/](AvaloniaScriptLoader/Samples/demo-controls/) | 新控件测试 |
| [Samples/gallery/](AvaloniaScriptLoader/Samples/gallery/) | 📦 **控件画廊** — 19 种控件 + 示例代码 |

---

## 社群

**QQ群 955830545** 提供技术交流与支持，欢迎加入。

> 个人是社畜，可能不会及时回复，请谅解。

## 许可证

本项目基于 **MIT 许可证** 开源。
