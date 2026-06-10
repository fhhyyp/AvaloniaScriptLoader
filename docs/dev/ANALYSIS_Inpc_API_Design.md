# inpc() API 设计重新剖析（基于 ScriptLang 约束）

## 约束重申

ScriptLang 的 `ObjectValue` 属性赋值（`obj.prop = value`）只更新内部字典，**不触发任何 C# 回调**。因此 InpcValue 的通知必须通过**函数调用**触发：

```javascript
// ✅ 可行：函数调用 → 触发 C# FunctionValue → 调用 InpcValue.Set()
count.set(5)

// ❌ 不可行：成员赋值 → 更新字典 → 无 C# 回调 → 无通知
count.value = 5
```

这是不可改变的底层约束。

## 当前 API 评估

| API | 评分 | 说明 |
|-----|------|------|
| `var x = inpc(0)` | ✅ 好 | 简洁清晰 |
| `x.get()` | ✅ 必须 | 唯一能触发 C# 读的语法 |
| `x.set(v)` | ✅ 必须 | 唯一能触发 C# 写+通知的语法 |
| `x.set(x.get() + 1)` | ⚠️ 啰嗦但必须 | 无替代方案 |
| `label({"text" = x})` | ✅ 好 | 自动绑定，无需 get/set |
| `inpc(x, "twoway")` | ⚠️ | magic string |

**结论：当前 API 在 ScriptLang 约束下已是最优形态。** 需要改进的不是 API 本身，而是**常见使用模式**。

## 可改进的方面

### 1. twoway magic string

```javascript
// 当前：magic string
var email = inpc("", "twoway")

// 建议：语义化函数
var email = model("")    // model() = inpc + twoway
```

`model()` 比 `inpc(x, "twoway")` 更直观，与表单绑定语义一致。

### 2. 数组操作的冗长模式

```javascript
// 当前：每次修改都要两步
let arr = todos.get()
arr.add("新项")
todos.set(arr)

// 建议：给 InpcValue 添加数组代理方法
todos.push("新项")    // 内部：get → add → set
todos.pop()           // 内部：get → pop → set
todos.removeAt(0)     // 内部：get → removeAt → set
```

在 InpcValue 的 ObjectValue 包装上添加 `push/pop/removeAt` 等代理方法，内部自动完成 get→mutate→set→notify。

### 3. 批量更新

```javascript
// 当前：多次 set 触发多次通知
firstName.set("张")
lastName.set("三")

// 建议：批量更新
batch(() => {
    firstName.set("张")
    lastName.set("三")
})  // 结束时统一通知一次
```

### 4. 表单绑定简化

```javascript
// 当前：手动 twoway + onChange
var name = model("")
textbox({
    "text" = name,
    "onChange" = (e) => { name.set(e.value) }  // twoway 已自动完成，此行多余
})

// twoway 模式下 onChange 多余，但初学者容易混淆
```

## 建议改进路线

| 优先级 | 改进 | 工作量 | 效果 |
|--------|------|--------|------|
| P0 | `model()` 替代 `inpc(x, "twoway")` | 🟢 1 行代码 | 消除 magic string |
| P0 | InpcValue 数组代理方法 (`push/pop/removeAt`) | 🟡 中 | 数组操作从 2 步变 1 步 |
| P1 | `batch()` 批量更新 | 🟡 中 | 减少冗余通知 |
| P2 | 文档/示例突出 `model()` 推荐用法 | 🟢 低 | 开发者教育 |

## 最终评价

**inpc API 在 ScriptLang 约束下已达到生产可用水平。** 

- `.get()/.set()` 不是设计缺陷，是底层约束的必然结果
- 与 Vue/Svelte 的可比性差异来自 ScriptLang 的语法限制，非 API 设计问题
- 重点应放在减少使用模式的冗长度（数组操作）、提升语义清晰度（model 命名）
