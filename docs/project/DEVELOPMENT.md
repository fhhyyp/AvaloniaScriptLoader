# 如何二次开发

## 项目结构概览

添加新功能时需要修改的文件路径及作用：

```
添加新控件     → Model/ControlMeta.cs → Factory/ControlFactory.cs
                → Builder/ControlBuilder.cs → Builder/PropertyBinder.cs
                → Modules/ControlsModule.cs

添加新事件     → Builder/ControlBuilder.cs (RegisterEvents)
                → Factory/ControlFactory.cs (通用属性列表)

添加新模块     → Modules/ 下新建类
                → ScriptEngineAdapter.cs (RegisterBuiltinModules)

添加自定义控件 → Controls/ 下新建类
                → Builder/ControlBuilder.cs (CreateNativeControl)

添加响应式类型 → Model/ 下新建类
                → Factory/InpcFactory.cs → 对应 Prototype/
```

---

## 添加新控件

以添加一个 `Calendar` 控件为例：

### Step 1 — 添加类型常量

`Model/ControlMeta.cs`:
```csharp
public const string TypeCalendar = "calendar";
```

### Step 2 — 添加工厂方法

`Factory/ControlFactory.cs`:
```csharp
public static FunctionValue CalendarFunc => new("calendar", args => {
    var descriptor = new ObjectValue();
    descriptor[ControlMeta.__type] = StringValue.Create(ControlMeta.TypeCalendar);
    // 处理参数...
    return descriptor;
});
```

### Step 3 — 注册原生控件映射

`Builder/ControlBuilder.cs` 的 `CreateNativeControl()` 方法中:
```csharp
case ControlMeta.TypeCalendar:
    return new Calendar();
```

### Step 4 — 添加属性映射（如需特殊属性）

`Builder/PropertyBinder.cs` 的 `ApplyControlSpecific()` 方法中:
```csharp
case Calendar cal:
    ApplyCalendarProperty(cal, name, value);
    break;

// ...

private static void ApplyCalendarProperty(Calendar cal, string name, Value value)
{
    switch (name)
    {
        case "displayDate":
            if (value is DateTimeValue dtv)
                cal.DisplayDate = dtv.Value.ToLocalTime();  // 注意使用本地时间
            break;
        // ...
    }
}
```

### Step 5 — 注册模块导出

`Modules/ControlsModule.cs`:
```csharp
module.AddExport("calendar", ControlFactory.CalendarFunc);
```

---

## 添加新事件

### Step 1 — 注册 Avalonia 事件

`Builder/ControlBuilder.cs` 的 `RegisterEvents()` 方法中:
```csharp
case Calendar cal:
    cal.DisplayDateChanged += async (s, e) => {
        try {
            var dt = e.NewDate?.LocalDateTime ?? DateTime.Now;  // 使用 LocalDateTime
            var args = Evt("change", ("displayDate", new DateTimeValue(dt)));
            await changeFunc.CallAsync(engine, [args]);
        } catch (Exception ex) { LogEventError("onChange", ex); }
    };
    break;
```

### Step 2 — 加入通用属性列表

`Factory/ControlFactory.cs` — 如事件为所有控件通用，加入通用事件列表。

---

## 添加新模块

### Step 1 — 创建模块类

`Modules/` 下新建类，继承模块基类:

```csharp
public class MyModule : Module
{
    public MyModule()
    {
        AddExport("myFunction", new FunctionValue("myFunction", args => {
            // 实现...
            return StringValue.Create("done");
        }));

        AddExport("myValue", StringValue.Create("constant"));
    }
}
```

### Step 2 — 注册模块

`ScriptEngineAdapter.cs` 的 `RegisterBuiltinModules()` 方法中:
```csharp
engine.RegisterModule("mymodule", new MyModule());
```

脚本中使用:
```javascript
import { myFunction, myValue } from "mymodule"
```

---

## 添加自定义 Avalonia 控件

### Step 1 — 创建控件类

`Controls/` 下新建 .cs 文件:

```csharp
using Avalonia;
using Avalonia.Controls.Primitives;

namespace AvaloniaScriptLoader.Controls;

public class MyControl : TemplatedControl
{
    public static readonly StyledProperty<string> MyPropertyProperty =
        AvaloniaProperty.Register<MyControl, string>(nameof(MyProperty));

    public string MyProperty
    {
        get => GetValue(MyPropertyProperty);
        set => SetValue(MyPropertyProperty, value);
    }
}
```

### Step 2 — Builder 映射

`Builder/ControlBuilder.cs` — 在 `CreateNativeControl()` 中添加映射:
```csharp
case ControlMeta.TypeMyControl:
    return new MyControl();
```

---

## 添加新属性值类型

### InpcValue / ComputedValue 等响应式类型

已在 `Model/` 下实现。若添加新响应式类型：

1. 实现 `INotifyPropertyChanged` 接口
2. 在 `Factory/InpcFactory.cs` 中添加工厂方法
3. 在 `Prototype/` 下添加脚本侧原型扩展
4. 在 `Modules/AvaloniaModule.cs` 中注册导出

### Value 类型（脚本侧值类型）

在 SereinScript 引擎的 `ScriptLang/Runtime/Value.cs` 中添加新的 Value 子类。

---

## DateTime 处理规范

`DateTimeValue` 内部以 **UTC** 存储。跨边界使用时的关键规则：

### 控件属性 → 控件（写）
```csharp
// ✅ 正确 — 使用本地时间
dp.SelectedDate = new DateTimeOffset(dtv.Value.ToLocalTime());
tp.SelectedTime = dtv.Value.ToLocalTime().TimeOfDay;

// ❌ 错误 — 直接使用 UTC
dp.SelectedDate = new DateTimeOffset(dtv.Value);
tp.SelectedTime = dtv.Value.TimeOfDay;
```

### 控件 → DateTimeValue（读）
```csharp
// ✅ 正确 — 使用 LocalDateTime（Kind=Local）
var dt = e.NewDate?.LocalDateTime ?? DateTime.Now;
new DateTimeValue(dt);

// ❌ 错误 — 使用 DateTime（Kind=Unspecified）
var dt = e.NewDate?.DateTime ?? DateTime.Now;
new DateTimeValue(dt);
```

### 脚本侧属性（DateTimePrototype）
```csharp
// ✅ Local() 方法必须返回 ToLocalTime()
private static DateTime Local(DateTimeValue dt) => dt.Value.ToLocalTime();
```

---

## 构建与调试

### 开发构建

```bash
# Debug 构建
dotnet build

# 运行（带脚本指定）
SCRIPT_PATH=./Samples/gallery-route/main.script dotnet run
```

### Release 发布

```bash
# Release 构建
dotnet build -c Release

# 发布独立可执行文件
dotnet publish -c Release -r win-x64 --self-contained

# macOS
dotnet publish -c Release -r osx-x64 --self-contained

# Linux
dotnet publish -c Release -r linux-x64 --self-contained
```

### 调试技巧

- **脚本调试**：使用 `print()` 输出变量值到控制台
- **UI 调试**：使用 `app.log()` / `app.toast()` 在运行时查看状态
- **断点调试**：在 C# 层（Builder/Factory/Model）打断点，脚本引擎在 VM 中执行无法直接断点
- **控件查找**：给控件设置 `name` 属性，运行时通过 `app.find(name)` 获取引用

---

## 代码规范

### C# 代码
- 4 空格缩进
- 中文注释文档
- 遵循项目既有命名风格
- 公共 API 使用 XML 文档注释

### 脚本代码
- 2 空格缩进
- 字符串使用 `"` 双引号
- 属性赋值使用 `=` 而非 `:`
- 对象字面量使用 `{ key = value }` 格式

### 功能交付
- 新增功能同步更新 `Samples/gallery-route/demos/` 下的示例
- 同步更新 `docs/project/` 下对应文档

---

## 依赖版本

| 包/项目 | 版本 |
|---------|------|
| .NET | 10.0 |
| Avalonia | 12.0.4 |
| Avalonia.Desktop | 12.0.4 |
| Avalonia.Themes.Fluent | 12.0.4 |
| Avalonia.Fonts.Inter | 12.0.4 |
| ScriptLang | 本地项目引用 |
| ScriptLang.Generator | 本地项目引用（Analyzer） |
