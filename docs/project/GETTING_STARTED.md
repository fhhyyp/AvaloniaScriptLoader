# 如何使用

## 环境要求

| 依赖 | 版本 |
|------|------|
| .NET SDK | 10.0+ |
| 操作系统 | Windows / macOS / Linux |

## 快速开始

```bash
# 1. 克隆仓库
git clone https://github.com/fengjiayi/AvaloniaScriptLoader.git
cd AvaloniaScriptLoader

# 2. 运行（默认加载控件画廊）
dotnet run
```

应用启动后自动加载 `Samples/gallery-route/main.script`（控件画廊）。

### 指定脚本运行

```bash
# Linux / macOS
SCRIPT_PATH=./Samples/demo/main.script dotnet run

# Windows PowerShell
$env:SCRIPT_PATH="./Samples/Counter.script"; dotnet run

# Windows CMD
set SCRIPT_PATH=./Samples/Counter.script && dotnet run
```

## 编写第一个脚本

创建 `hello.script`：

```javascript
// hello.script
import { window, label } from "avalonia.controls"

return window({
    "title" = "Hello World",
    "width" = 400,
    "height" = 300,
    "content" = label({
        "text" = "Hello AvaloniaScriptLoader!",
        "fontSize" = 20,
        "horizontalAlignment" = "center",
        "verticalAlignment" = "center"
    })
})
```

```bash
SCRIPT_PATH=./hello.script dotnet run
```

## 脚本基本结构

```javascript
// 1. 导入依赖
import { inpc, computed, style } from "avalonia"
import { window, stackpanel, button, label } from "avalonia.controls"
import { _loaded } from "styles.script"  // 跨文件导入

// 2. 定义状态（可选）
var count = inpc(0)
var msg = computed(() => "计数: " + count.get())

// 3. 定义样式（可选）
style("primary", { "background" = "#6366f1", "color" = "#fff" })

// 4. 构建界面并返回根控件
return window({
    "title" = "应用标题",
    "width" = 800, "height" = 600,
    "content" = ...
})
```

## 模块系统

### 内置模块

| 模块 | 导入路径 | 说明 |
|------|----------|------|
| 系统 API | `"avalonia"` | `app` `inpc` `computed` `table` `vif` `vfor` `component` `style` `fetch` `fetchAsync` |
| 控件库 | `"avalonia.controls"` | 28 种控件构造器 |

### 自定义模块

```javascript
// mymodule.script
var greet = (name) => "Hello, " + name
return { greet }

// main.script
import { greet } from "mymodule.script"
print(greet("World"))
```

### 动态导入

```javascript
var mod = import("demos/label.script")  // 运行时加载
var content = mod.render()
```

## 运行模式

| 模式 | 命令 | 说明 |
|------|------|------|
| 开发运行 | `dotnet run` | 默认加载画廊 |
| 指定脚本 | `SCRIPT_PATH=<path> dotnet run` | 加载指定脚本 |
| Release 构建 | `dotnet build -c Release` | 优化编译 |
| 独立发布 | `dotnet publish -c Release -r win-x64 --self-contained` | 生成 .exe |

## 示例项目

| 示例 | 路径 | 说明 |
|------|------|------|
| 控件画廊 | [Samples/gallery-route/](../AvaloniaScriptLoader/Samples/gallery-route/) | 26 个独立控件 demo + 分类导航 |
| 模块化示例 | [Samples/demo/](../AvaloniaScriptLoader/Samples/demo/) | 用户管理（多文件模块） |
| Dialog 测试 | [Samples/demo-dialog/](../AvaloniaScriptLoader/Samples/demo-dialog/) | 对话框组件完整测试 |
| 控件测试 | [Samples/demo-controls/](../AvaloniaScriptLoader/Samples/demo-controls/) | DatePicker/Slider/ProgressBar 测试 |

## 常见问题

### Q: 脚本文件放哪里？

放在任意位置，通过 `SCRIPT_PATH` 环境变量或修改 `Program.cs` 中的默认路径指定。

### Q: 如何调试脚本？

使用 `print()` 函数输出到控制台，或使用 `app.log()` 输出到应用日志。

### Q: 支持热加载吗？

当前版本需重启应用来加载新脚本。修改 `.script` 文件后重新运行 `dotnet run` 即可看到效果。

### Q: 如何引用外部 DLL？

通过 SereinScript 的 `import_clr()` 机制加载 .NET 程序集（详见 SereinScript 文档）。
