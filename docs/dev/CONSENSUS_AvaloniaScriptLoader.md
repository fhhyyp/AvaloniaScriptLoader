# CONSENSUS — Avalonia 脚本加载器需求共识

## 1. 需求描述

通过 ScriptLang 自定义脚本语言动态生成 Avalonia 页面。脚本采用声明式语法描述 UI 结构，执行后返回对象树，宿主程序解析对象树并创建实际的 Avalonia 控件。

## 2. 验收标准

| # | 标准 | 验证方式 |
|---|------|----------|
| AC1 | 脚本可通过 `import { xxx } from "avalonia.controls"` 导入控件工厂 | 集成测试 |
| AC2 | 脚本可通过 `import { app } from "avalonia"` 导入系统 API | 集成测试 |
| AC3 | 脚本执行后返回完整对象树，宿主一次性解析并构建 Avalonia 控件 | 单元测试 |
| AC4 | 控件事件（OnClick 等）通过闭包实现，捕获脚本变量 | 集成测试 |
| AC5 | 脚本可通过控件方法修改属性，宿主自动更新实际 UI 控件 | 集成测试 |
| AC6 | 支持 Window、Button、Label、TextBox、CheckBox、ComboBox、ListBox、StackPanel、Grid | 单元测试 |
| AC7 | 项目使用 .NET 10 + Avalonia 12.0.4 | 编译验证 |

## 3. 技术方案

### 3.1 线程模型
- 脚本在**后台线程**执行
- UI 操作通过 `Avalonia.Threading.Dispatcher.UIThread.Post/InvokeAsync` 调度

### 3.2 控件树构建
- 脚本执行完毕返回完整对象树（`ObjectValue` 嵌套结构）
- 宿主一次性递归解析 → 在主线程构建 Avalonia 控件树
- 控件树解析为纯函数：`ObjectValue → Control`

### 3.3 属性变更机制
- **不使用属性赋值**（脚本引擎不支持赋值监听）
- 控件对象提供 `set(name, value)` / `setName(value)` 方法
- 方法内部通过 Dispatcher 调度到 UI 线程更新实际控件
- 示例：`label.setText("新的文本")` 或 `label.set("text", "新的文本")`

### 3.4 命名空间
- `"avalonia"` — 系统级 API：`app.showMessage()`, `app.log()`, `app.find(name)`
- `"avalonia.controls"` — 控件工厂：`window()`, `button()`, `label()`, `textbox()` 等

### 3.5 事件处理
- 事件处理器为脚本 Lambda（编译为 `CompiledFunctionValue`）
- 宿主将 Lambda 注册为 Avalonia 控件事件
- Lambda 通过 `LightweightClosure` 自动捕获外部变量

## 4. 技术约束

| 约束 | 说明 |
|------|------|
| C1 | .NET 10 SDK |
| C2 | Avalonia 12.0.4 |
| C3 | 属性修改通过方法调用，非赋值 |
| C4 | 脚本在后台线程执行 |
| C5 | 暂不支持数据绑定 |
| C6 | 控件对象树一次性构建，不支持增量更新结构（仅属性值可更新） |

## 5. 集成方案

```
┌──────────────────────────────────────────────────────┐
│ AvaloniaScriptLoader (新项目)                         │
│                                                       │
│  ┌──────────────────┐   ┌──────────────────────────┐ │
│  │ Avalonia App      │   │ ScriptEngineAdapter      │ │
│  │ (UI 线程)         │◀──│ (引擎生命周期管理)        │ │
│  └──────┬───────────┘   └──────────┬───────────────┘ │
│         │                          │                  │
│  ┌──────▼───────────┐   ┌──────────▼───────────────┐ │
│  │ ControlBuilder    │   │ AvaloniaModule           │ │
│  │ (对象树→控件树)   │   │ (注册 avalonia 模块)     │ │
│  └──────────────────┘   └──────────┬───────────────┘ │
│                                     │                  │
│  ┌──────────────────┐   ┌──────────▼───────────────┐ │
│  │ ControlFactory    │   │ ControlsModule           │ │
│  │ (控件包装工厂)     │   │ (注册 avalonia.controls) │ │
│  └──────────────────┘   └──────────────────────────┘ │
│                                                       │
│  依赖: ScriptLang.dll                                 │
└──────────────────────────────────────────────────────┘
```

### 脚本执行流程
```
1. 加载脚本源码
2. ScriptEngineAdapter 注入 "avalonia" 和 "avalonia.controls" 内置模块
3. Lexer → Parser → Compiler → VM.Execute（后台线程）
4. 脚本调用控件工厂 → 构建 ObjectValue 树
5. 脚本返回根 ObjectValue（通常是 Window）
6. 宿主在 UI 线程解析 ObjectValue 树 → 创建 Avalonia 控件
7. 注册事件处理器（Lambda → CompiledFunctionValue）
8. 显示窗口
```

### 控件对象结构
```javascript
// 脚本中调用 button({...}) 返回的 ObjectValue 内部结构：
{
    "__type": "button",        // 控件类型标识
    "__id": "ctrl_0",          // 唯一 ID（用于 app.find）
    "text": StringValue,       // 属性值
    "width": NumberValue,
    "onClick": FunctionValue,  // 事件处理器
    // ... 方法
    "setText": FunctionValue,  // 属性设置方法
    "setWidth": FunctionValue,
    // ...
}
```

## 6. 与设计文档的关键差异

| 设计文档 | 共识方案 | 原因 |
|----------|----------|------|
| `control.text = "新值"` 赋值更新 | `control.setText("新值")` 方法更新 | 脚本引擎不支持赋值监听 |
| 未指定线程模型 | 后台线程 + Dispatcher | 避免脚本阻塞 UI |
| 未指定 Avalonia 版本 | 12.0.4 | 用户指定 |
| 对象字面量使用 `=` | 保持 ScriptLang 语法：`{"key" = value}` | 语言固有语法 |
| 未详细指定 setter 方法 | 每个属性提供独立 setXxx 方法 + 通用 set(name, value) | 性能与灵活性平衡 |
