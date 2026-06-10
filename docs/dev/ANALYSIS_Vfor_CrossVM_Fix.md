# vfor Lambda 跨 VM 调用的根因分析与修复方案

## 问题复现

main.script 中调用 `vfor(displayUsers, userTemplate)`，模板 Lambda 内部使用 `border`/`label`/`button`/`app`，执行时报：
```
无法调用类型为 NullValue 的值
```

## 根因追踪

### 时序

```
1. main.script 编译 → Compiler.Register 20 个全局名 → Count=20
2. main VM 初始化 → GetValues() → _values.Length(0)!=Count(20) → InitializeValues() → _values=new Value[20](全null)
3. main VM Import "avalonia" → SetValue 写 6 个值
4. main VM Import "avalonia.controls" → SetValue 写 14 个值
   _values 现在有 20 个正确值 ✅

5. styles.script 编译（main VM 的 ImportModule 触发）
   → Compiler.Register("style") → 已注册,Count 不变
   → Compiler.Register("_loaded") → 新! Count=21 ← ⚠️

6. styles.script 执行 → 新 VM → GetValues()
   → _values.Length(20) != Count(21) → InitializeValues()
   → _values = new Value[21](全null) ← 🔴 之前 20 个值全部丢失!
   → Styles 注册成功（style() 是副作用函数）
   → 返回 {_loaded: true}

7. data.script 编译 → Compiler.Register("inpc","computed") → 已注册
   → Compiler.Register 若干局部变量名 → 可能不增加全局 Count

8. vfor 模板 Lambda 执行 → CallAsync → 新 VM → GetValues()
   → _values 被步骤 6 清空 → border/label/button/app 全为 null
   → 模板失败
```

### 根因

**`ImportModule` 触发子模块编译时，Compiler 注册了新全局名 → `Count` 增大 → `_values.Length != Count` → 子模块 VM 的 `GetValues()` 触发 `InitializeValues()` → 全部已有槽位值清零。**

## 方案对比

### 方案 A：恢复 PreserveGlobalSlotValues（已完成，之前因"治标不治本"移除）

在 main.script 执行完毕后、Build 开始前，将 `_values` 扩容到匹配 `Count` 并保留已有值。

```csharp
// 反射写入 _values 私有字段
var valuesField = typeof(GlobalSlotRegistry).GetField("_values", ...);
var newValues = new Value[Count];
Array.Copy(currentValues, newValues, currentLength);
valuesField.SetValue(null, newValues);
```

**问题：** 步骤 6 中 styles.script 的 VM 已经 wipe 了一次值。即使事后修复 _values.Length，被清空的 20 个值已永久丢失。PreserveGlobalSlotValues 只能防止 FUTURE wipe，无法恢复已丢失的值。

**结论：❌ 不适用**

### 方案 B：在子模块编译后立即保存/恢复全局槽位值

修改 `ImportResolver.LoadSourceModuleAsync` 或在 VM.ImportModule 中添加钩子，每次模块加载完成后保存当前 _values 快照。

**问题：** 需要修改 ScriptLang 核心代码（ImportResolver / VM），侵入性强。

**结论：❌ 不适用（无法修改 ScriptLang）**

### 方案 C：禁止子模块注册新全局名

子模块（styles.script/data.script）的编译不应影响全局 Count。可通过以下方式实现：
- 预注册所有可能的全局名（在 module registration 时）
- 子模块编译前"冻结" Count，编译后回滚

**问题：** 无法干预 Compiler 行为。

**结论：❌ 不适用**

### 方案 D：模块编译前手动 SetValue 保存，编译后恢复

在 `ExecuteAsync` 中，每次 import 模块前手动保存 _values 内容，模块加载后判断 Count 是否变化，若变化则扩容并恢复值。

实现：Hook into ScriptEngine 的 import 流程。可以在 `RegisterBuiltinModules` 后，监听后续的模块编译。

**问题：** `CreateTask(scriptPath)` 内部流程不可控。模块 import 在 VM 执行时自动触发。

**结论：❌ 实现复杂且脆弱**

### 方案 E：不创建新 VM — 在当前 VM 中调用模板（推荐 ✅）

核心思路：**既然 `CallAsync` 必然创建新 VM，那就不使用 `CallAsync`。** 

在 `CreateVforFunction` 中，由于 vfor 是在脚本执行期间被调用的，当前正处于 VM 执行上下文中。此时可以不通过 `CallAsync` 创建新 VM，而是直接在当前帧栈上执行模板 Lambda 的字节码。

但 ScriptLang 的 VM 不暴露"在当前 VM 中执行另一个 CompiledFunctionValue"的 API。

**替代实现：** 利用 ScriptLang 的 `for` 循环 + 函数调用（函数体在当前 VM 中执行，不创建新 VM）。

当前 demo 已使用此方案（`buildUserCards()` + `for` 循环）。

**结论：✅ 已实施，但失去了 vfor 的声明式语法和自动响应式更新**

### 方案 F：修复 GlobalSlotRegistry 的 InitializeValues — 保留旧值（推荐 ✅）

**修改 ScriptLang 的 `GlobalSlotRegistry.GetValues()`，使其扩容时保留已有值而非清零。**

```csharp
public static Value[] GetValues()
{
    if (_values.Length != Count)
    {
        var old = _values;
        _values = new Value[Count];
        Array.Copy(old, _values, Math.Min(old.Length, Count));  // 保留旧值
        for (int i = old.Length; i < Count; i++)
            _values[i] = Value.Null;
    }
    return _values;
}
```

**优点：** 
- 一行改动，从根源修复
- 所有调用 GetValues() 的地方自动受益
- 不需要反射、不需要额外钩子
- vfor / ComputedValue / 事件处理器中任何需要新 VM 的场景都受益

**缺点：**
- 需要修改 ScriptLang 源码

**结论：✅ 推荐**

### 方案 G：vfor 模板用 for 循环替代（当前方案）

已实施。`buildUserCards()` 使用 `for` 循环直接构建数组，在当前 VM 中执行，全局变量可用。

**缺点：**
- 失去声明式 vfor 语法
- 失去 vfor 的自动响应式更新
- 数据变更后需手动重建列表

**结论：✅ 当前可用，但非长期方案**

## 建议

**短期（立即）：** 保持方案 G（for 循环），demo 可正常运行。

**长期（推荐）：** 实施方案 F，修改 ScriptLang 的 `GlobalSlotRegistry.GetValues()` 保留旧值。这是最小改动、最大收益的根源修复。修改后：
- vfor Lambda 模板可在新 VM 中正常执行
- ComputedValue 跨 VM 求值更稳定
- 事件处理器 Lambda 中访问全局变量更可靠

是否确认实施方案 F？
