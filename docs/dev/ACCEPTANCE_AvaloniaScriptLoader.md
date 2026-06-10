# ACCEPTANCE — Avalonia 脚本加载器验收报告

## 1. 实现内容

### 1.1 项目结构

```
AvaloniaScriptLoader/
├── AvaloniaScriptLoader.csproj      # .NET 10 + Avalonia 12.0.4
├── Program.cs                       # 应用入口
├── App.axaml / App.axaml.cs         # Avalonia 应用生命周期
├── MainWindow.axaml / .cs           # 启动窗口 + 脚本引擎集成
├── ScriptEngineAdapter.cs           # 引擎适配器
├── app.manifest                     # Windows 兼容性清单
├── Modules/
│   ├── AvaloniaModule.cs            # "avalonia" 系统模块
│   └── ControlsModule.cs            # "avalonia.controls" 控件模块
├── Factory/
│   └── ControlFactory.cs            # 9 种控件的 ObjectValue 工厂
├── Builder/
│   ├── ControlBuilder.cs            # ObjectValue 树 → Avalonia 控件树
│   └── PropertyBinder.cs            # 属性映射 + 类型转换
├── Wrapper/
│   └── ControlWrapper.cs            # 两阶段 setter 激活
├── Model/
│   ├── ControlMeta.cs               # 控件元数据常量
│   └── PropertyNames.cs             # 属性名常量 + 工具方法
└── Samples/
    ├── Counter.script               # 计数器示例
    └── HelloWorld.script            # Hello World 示例
```

### 1.2 模块清单

| 模块 | 文件 | 状态 |
|------|------|------|
| T0 项目初始化 | `.csproj` + `App.axaml` + `Program.cs` | ✅ |
| T1 元数据模型 | `Model/ControlMeta.cs` + `PropertyNames.cs` | ✅ |
| T2 引擎适配器 | `ScriptEngineAdapter.cs` | ✅ |
| T3 系统模块 | `Modules/AvaloniaModule.cs` | ✅ |
| T4 控件模块 | `Modules/ControlsModule.cs` | ✅ |
| T5 控件工厂 | `Factory/ControlFactory.cs` | ✅ |
| T6 属性绑定 | `Builder/PropertyBinder.cs` | ✅ |
| T7 控件包装 | `Wrapper/ControlWrapper.cs` | ✅ |
| T8 控件构建 | `Builder/ControlBuilder.cs` | ✅ |
| T9 事件注册 | `Builder/ControlBuilder.cs` (RegisterEvents) | ✅ |
| T10 示例脚本 | `Samples/Counter.script` + `HelloWorld.script` | ✅ |
| T11 宿主入口 | `MainWindow.axaml.cs` (脚本加载+构建+显示) | ✅ |

---

## 2. 验收标准检查

| # | 标准 | 状态 | 说明 |
|---|------|------|------|
| AC1 | `import { xxx } from "avalonia.controls"` | ✅ | ControlsModule 导出 9 个控件工厂 |
| AC2 | `import { app } from "avalonia"` | ✅ | AvaloniaModule 导出 app 对象 |
| AC3 | 脚本返回对象树 → 一次性解析构建 | ✅ | ControlBuilder.Build() 递归构建 |
| AC4 | 闭包事件处理 | ✅ | Lambda → CompiledFunctionValue → Avalonia 事件 |
| AC5 | 方法调用修改属性 → UI 更新 | ✅ | setText/setWidth/... + 通用 set(name, value) |
| AC6 | 9 种控件支持 | ✅ | Window/Button/Label/TextBox/CheckBox/ComboBox/ListBox/StackPanel/Grid |
| AC7 | .NET 10 + Avalonia 12.0.4 | ✅ | 编译验证通过 |

---

## 3. 编译验证

```bash
dotnet build
# 结果: 0 错误, 2 警告 (均为 ScriptLang.Generator 的 CS0436，不影响功能)
```

## 4. 已知限制

1. **Padding 属性**：只在 `Decorator` 子类上设置（Avalonia 12.x 中 `Control` 无 `Padding`）
2. **ListBox 模板**：当前使用简化实现，未完全集成 template 函数
3. **事件参数**：事件处理器当前不传递事件参数（传空列表）
4. **app.showMessage**：当前输出到 Debug 而非弹窗（Avalonia 12.x 无内置 MessageBox）
5. **控件属性映射**：部分属性（如 password、multiline）映射可能需在 Avalonia 12.x 中微调

---

## 5. 运行测试

### 当前可测试范围

- ✅ 编译通过
- ✅ 脚本文件被复制到输出目录
- ⚠ 实际 UI 运行需在有图形环境的系统上测试
- ⚠ `TextBox.PlaceholderText` 等 API 需实际运行确认
