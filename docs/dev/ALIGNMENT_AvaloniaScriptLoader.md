# ALIGNMENT — Avalonia 脚本加载器需求对齐

## 1. 原始需求

**任务目标：** 通过自定义脚本动态生成 Avalonia 页面

**项目环境：**
- SDK: .NET 10
- Avalonia: 12.0+（最新版本）

**工作步骤：**
1. 分析 ScriptLang 脚本语言的闭包模型、值模型
2. 分析 ExcelScriptLoader 如何封装 Excel API 提供宏调度服务
3. 结合上述分析重新设计「脚本语言 UI 框架设计文档」
4. 整理开发计划（模糊处需确认）
5. 按计划实施

---

## 2. 项目理解

### 2.1 ScriptLang 脚本语言核心模型

#### 值模型（Value System）

统一类型系统，所有运行时值继承自抽象类 `Value`：

| 类型 | 说明 | 关键特性 |
|------|------|----------|
| `NullValue` | null 单例 | `Value.Null` |
| `NumberValue<T>` | 不可变数值 | int/long/float/double/decimal，小整数缓存 [-128,127] |
| `MutableNumber` | 可变数值 | 用于 var 局部变量，原地修改零分配 |
| `StringValue` | 字符串 | 空字符串单例 |
| `BoolValue` | 布尔 | True/False 单例 |
| `ObjectValue` | 对象/字典 | `Dictionary<string, Value>` |
| `ArrayValue` | 数组 | `List<Value>`，支持 add/pop/removeAt 等 |
| `ClrObjectValue` | CLR 对象包装 | 包装任意 C# 对象，反射访问成员 |
| `ClrMethodValue` | CLR 方法包装 | 包装 MethodInfo + 实例，支持异步调用 |
| `FunctionValue` | 原生函数 | 通过 Delegate + Flags 模式支持同步/异步/有参/无参组合 |
| `CompiledFunctionValue` | 编译后的脚本函数 | 携带 ByteCodeChunk + VariableTable + LightweightClosure |
| `RangeIterator` | 惰性范围迭代器 | 用于 for-in-range 循环，零分配 |

**关键设计：**
- **不可变默认 + 可变优化**：let 产生不可变值，var 使用 MutableNumber 原地修改
- **小整数缓存**：-128~127 的 int 值复用单例
- **MutableNumber 冻结**：传参/返回时自动 ToImmutable()，防止跨函数副作用
- **类型判断**：通过 `IsXxx` 虚属性判断，避免 `is` 模式匹配的版本兼容问题

#### 闭包模型（Closure Model）

**三层结构：**

```
VariableCell (堆分配，引用语义)
    ↓ 被持有
VariableInfo { Cell, IsMutable, IsCaptured }
    ↓ 被持有
LightweightClosure { VariableInfo[] CapturedCells }
    ↓ 被嵌入
CompiledFunctionValue { Parameters, Chunk, VariableTable, Closure }
```

**闭包生命周期：**

1. **编译时 - CaptureAnalysis（静态分析）：**
   - 遍历 AST，收集 Lambda 中的自由变量（不在参数列表中、不在内层绑定中的外部引用）
   - 对于嵌套 Block/Program，逐条处理 LetExpr/VarExpr 的绑定追加
   - 返回 `HashSet<string>` 候选捕获变量名

2. **编译时 - Compiler（槽位分配）：**
   - 被捕获的变量在 VariableTable 中分配 Capture 区域槽位
   - 生成 `CreateClosure` 指令，携带 `(chunkIndex, parameters, captureMappings)`
   - captureMappings: `List<(name, outerCaptureSlot)>`

3. **运行时 - VM.CreateClosure：**
   - 为每个捕获变量创建新的 `VariableCell`（拷贝当前值）
   - 创建 `VariableInfo(cell, isMutable=true, isCaptured=true)`
   - **回写**到外部帧的 Captures 数组（确保嵌套闭包共享同一个 Cell）
   - 创建 `LightweightClosure(capturedCells)` 并嵌入 CompiledFunctionValue

4. **运行时 - 变量访问：**
   - 帧内槽位分为四个区域：Local → Capture → Global → Builtin
   - 写入 Capture 区域时同步更新 `Captures[index].Cell.Value`
   - VM 通过 `VariableTable.GetRegion(slot)` 判断区域

**闭包示例：**
```javascript
let makeCounter = () => {
    let count = 0           // 被闭包捕获 → Capture 区
    return () => {
        count = count + 1   // 通过 Capture Cell 共享修改
        return count
    }
}
```

#### 模块系统

- `import { member } from "module"` 语法
- `ImportResolver` 负责解析：内置模块缓存 → 文件系统（优先 .ssc 编译产物）
- 宿主可通过 `ImportResolver.RegisterBuiltinModule(name, exports)` 注册虚拟模块
- 模块导出 `ObjectValue`，成员为键值对

#### 原型扩展系统

- `PrototypeManager` 管理 `IPrototype` 列表
- 脚本访问 `target.method()` 时，VM.GetMember 先查 ObjectValue 属性，再查 PrototypeManager
- Excel 用 `[PrototypeExtension(PushThis=true)]` + `[PrototypeFunction]` / `[PrototypeProperty]` 特性

### 2.2 ExcelScriptLoader 集成模式

ExcelScriptLoader 展示了如何将 ScriptLang 与宿主应用程序集成：

```
┌──────────────────────────────────────────────────────┐
│                 ExcelScriptLoader                     │
│                                                       │
│  ┌─────────────────┐    ┌──────────────────────────┐ │
│  │ ExcelApplication │───▶│  ScriptEngineAdapter     │ │
│  │ (COM 对象)       │    │                          │ │
│  └─────────────────┘    │  - Initialize(app)        │ │
│                          │  - Execute(script, name)  │ │
│  ┌─────────────────┐    │  - RefreshExcelContext()  │ │
│  │ ExcelModule      │◀───│  - RegisterExcelModule() │ │
│  │ [PrototypeExt]   │    └──────────┬───────────────┘ │
│  └─────────────────┘               │                  │
│                          ┌──────────▼───────────────┐ │
│  ┌─────────────────┐    │  ScriptLang 引擎          │ │
│  │ ExcelFactory     │───▶│                          │ │
│  │ - WorkbookToObj  │    │  - ImportResolver         │ │
│  │ - WorksheetToObj │    │    .RegisterBuiltinModule │ │
│  │ - CellToObject   │    │  - PrototypeManager       │ │
│  │ - RangeToObject  │    │  - GlobalScope            │ │
│  │ - TableToObject  │    └──────────────────────────┘ │
│  └─────────────────┘                                  │
└──────────────────────────────────────────────────────┘
```

**关键集成模式：**

1. **ScriptEngineAdapter 作为中枢：**
   - 管理 ScriptEngine 生命周期
   - 注入宿主对象到 Scope（通过 `DefineClrObject`）
   - 注册内置模块（通过 `RegisterBuiltinModule`）
   - 每次执行前刷新上下文

2. **两种 API 暴露方式：**
   - **Prototype 模式**（ExcelModule）：通过 `[PrototypeFunction]` 特性，脚本直接调用 `excel.sheet()`
   - **ObjectValue + FunctionValue 模式**（ExcelFactory）：将 COM 对象包装为 `ObjectValue`，方法作为 `FunctionValue` 属性

3. **值转换桥梁：**
   - `WrapCell`/`UnwrapV`：COM ↔ ScriptLang Value 互转
   - CLR 对象通过 `ClrObjectValue` 包装传递
   - 脚本可直接访问 CLR 对象属性和方法（通过反射）

4. **执行流程：**
   ```
   加载脚本 → Lexer → Parser → Compiler → VM.Execute → 返回 Value
   ```

### 2.3 AvaloniaScriptLoader 当前状态

- 项目目录存在但仅有文档，尚无代码
- 设计文档 `脚本语言 UI 框架设计文档.md` 已存在，需要根据实际分析重设计

---

## 3. 任务边界

### 范围内

1. 设计并实现 Avalonia UI 脚本模块（`AvaloniaModule`）
2. 实现控件工厂函数（Window、Button、Label、TextBox、StackPanel、Grid 等）
3. 实现对象树 → Avalonia 控件树的解析器
4. 实现闭包事件处理（OnClick、OnChange 等）
5. 实现属性绑定与响应式更新（脚本修改属性 → UI 刷新）
6. 实现应用级 API（ShowMessage、Log、Find）
7. 编写示例脚本和集成测试

### 范围外（当前阶段）

1. 数据绑定/ MVVM 模式
2. 样式/主题系统
3. 动画系统
4. 自定义控件扩展
5. 可视化设计器
6. 热重载

---

## 4. 风险与假设

### 风险

| 风险 | 影响 | 缓解措施 |
|------|------|----------|
| Avalonia 12.x API 变更 | 高 | 使用 Avalonia 官方文档，锁定具体版本 |
| UI 线程安全问题 | 高 | 所有 UI 操作通过 Dispatcher 调度到 UI 线程 |
| 闭包中修改 UI 属性的同步 | 中 | 利用 VariableCell 引用语义，实现属性变更通知 |
| 脚本执行阻塞 UI | 中 | 脚本在后台线程执行，UI 操作通过 Dispatcher 回发 |
| 对象树递归深度过大 | 低 | 设定合理深度限制 |

### 假设

1. **Avalonia 12.x API 与 11.x 基本兼容**（待验证）
2. **脚本在非 UI 线程执行，UI 操作通过 Dispatcher.UIThread.Post 调度**
3. **控件工厂函数返回 `ObjectValue`，内部携带 `__type` 元信息标识控件类型**
4. **属性修改通过 `ObjectValue.Set()` 触发 → 通知宿主更新 UI**
5. **项目结构采用与 ExcelScriptLoader 类似的 Adapter 模式**

---

## 5. 待确认问题

以下问题需要在进入架构设计阶段前确认：

### Q1: 脚本执行线程模型
脚本是在后台线程执行（类似 Excel 模式），还是在 UI 线程执行？
- **建议**：后台线程执行 + UI 操作通过 Dispatcher 调度（避免阻塞 UI）
- 影响：ObjectValue 属性变更通知是否需要线程安全

### Q2: 控件树构建时机
对象树是脚本执行完毕后一次性构建，还是脚本执行过程中逐步构建？
- **选项 A**：脚本返回完整对象树 → 宿主一次性解析 → 构建控件
- **选项 B**：脚本调用工厂函数时即时创建控件
- **建议**：选项 A（与设计文档一致，脚本是可预测的值返回）

### Q3: 属性变更通知机制
脚本中 `label.text = "新值"` 如何触发 UI 更新？
- **选项 A**：ObjectValue.Set 发出变更事件 → 宿主监听 → 更新 UI
- **选项 B**：控件属性通过 CLR 对象包装 → 设置属性直接反映到 Avalonia 控件
- **选项 C**：脚本执行完毕后统一同步（不支持运行时动态更新）
- **建议**：选项 A（最灵活，但需要为 ObjectValue 添加变更通知能力）

### Q4: Avalonia 版本
需要确认使用的 Avalonia 具体版本号（12.x 的具体版本），因为 12.0 之后 API 有较大变化。

### Q5: 项目命名
项目名为 `AvaloniaScriptLoader`，是否也需要一个模块命名空间（类似 `avalonia`）供脚本使用？
- **建议**：脚本中使用 `import { window, button, ... } from "avalonia.controls"`

### Q6: 是否需要支持数据绑定
设计文档中未提及数据绑定（`{Binding Path=xxx}`），是否需要？
- **建议**：第一阶段不支持，通过闭包手动更新属性即可

---

## 6. 关键发现总结

1. **ScriptLang 的值模型足够灵活**：`ObjectValue` 天然适合表示 UI 对象树，`FunctionValue` 适合表示事件处理器
2. **闭包模型成熟**：`LightweightClosure` 的 Cell 共享机制天然支持事件处理器捕获脚本变量
3. **Excel 集成模式可复用**：`ScriptEngineAdapter` → `RegisterBuiltinModule` → `ObjectValue + FunctionValue` 的架构可直接映射到 Avalonia
4. **设计文档需重写**：当前设计文档写于分析之前，语法细节需要与实际 ScriptLang 语法对齐（如 `let` vs `var`、对象字面量使用 `=` 而非 `:` 等）
5. **最大挑战**：属性变更通知 — 当前 `ObjectValue` 没有变更事件，需要扩展
