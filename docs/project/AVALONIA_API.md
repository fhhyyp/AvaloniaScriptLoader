# Avalonia API

> 模块 `"avalonia"` — 系统级 API，提供响应式数据、结构化指令、应用交互、HTTP 请求。

## 导入

```javascript
import { app, inpc, computed, table, vif, vfor, component, style, fetch, fetchAsync } from "avalonia"
```

---

## app — 应用级 API

`app` 是一个全局 ObjectValue，提供应用交互能力。

### 对话框

| 方法 | 签名 | 说明 |
|------|------|------|
| `app.showMessage(msg)` | `(string) → void` | 弹出信息对话框 |
| `app.showConfirm(msg)` | `(string) → bool` | 弹出确认对话框，返回 true/false |
| `app.showDialog(title, content)` | `(string, string) → void` | 弹出自定义对话框 |

```javascript
app.showMessage("操作完成")
var ok = app.showConfirm("确定要删除吗？")
if (ok) { /* 执行删除 */ }
```

### 文件操作

| 方法 | 签名 | 说明 |
|------|------|------|
| `app.openFile(title?, filter?)` | `(string?, string?) → string?` | 打开文件选择器，返回文件路径 |
| `app.saveFile(title?, filter?)` | `(string?, string?) → string?` | 打开保存文件对话框 |

```javascript
var path = app.openFile("选择图片", "*.png;*.jpg;*.jpeg")
if (path != null) { image.source = path }
```

### 控件查找

| 方法 | 签名 | 说明 |
|------|------|------|
| `app.find(name)` | `(string) → Control?` | 按 `name` 属性查找控件 |
| `app.focus(name)` | `(string) → void` | 聚焦指定 `name` 的控件 |

```javascript
app.focus("myInput")           // 聚焦输入框
var btn = app.find("submitBtn") // 查找按钮
```

### 窗口与通知

| 方法 | 签名 | 说明 |
|------|------|------|
| `app.close(name?)` | `(string?) → void` | 关闭指定窗口（默认当前窗口） |
| `app.toast(msg, type)` | `(string, "info"\|"success"\|"warning"\|"error") → void` | 弹出 Toast 通知，3 秒自动消失 |
| `app.log(msg)` | `(any) → void` | 输出日志到控制台 |

```javascript
app.toast("保存成功!", "success")
app.toast("连接失败", "error")
app.log("debug info")
```

---

## inpc — 响应式值

`inpc(value, mode?)` 创建一个响应式值，值变化时自动通知绑定它的 UI 控件。

```javascript
var x = inpc(0)                 // 普通响应式值
var y = inpc("hello", "twoway") // 双向绑定模式
var z = inpc([1, 2, 3])        // 数组值
```

### 读写操作

```javascript
var count = inpc(0)
count.get()                      // 读取值（自动注册依赖追踪）
count.set(5)                     // 写入值（自动通知 UI 更新）
count.value                      // 同 get()
```

### 数组操作

当 `inpc` 的值是数组时，可使用以下方法（UI 自动更新）：

```javascript
var list = inpc(["A", "B", "C"])
list.push("D")                   // 追加元素
list.pop()                       // 弹出末尾元素
list.removeAt(0)                 // 删除指定索引元素
var len = len(list.get())        // 获取长度
```

### 双向绑定 (`twoway`)

```javascript
// 用于 TextBox / CheckBox / Slider / DatePicker / TimePicker 等
var text = inpc("", "twoway")
var checked = inpc(false, "twoway")
var sliderValue = inpc(50, "twoway")

textbox({ text = text })         // 用户输入自动同步到 text
checkbox({ checked = checked })  // 勾选状态自动同步
slider({ value = sliderValue })  // 滑块值自动同步
```

---

## computed — 计算属性

`computed(fn)` 创建一个计算属性，自动追踪依赖的 InpcValue，依赖变化时自动重新计算。

```javascript
var firstName = inpc("张")
var lastName = inpc("三")
var fullName = computed(() => lastName.get() + firstName.get())  // "张三"

var a = inpc(30, "twoway")
var b = inpc(50, "twoway")
var sum = computed(() => a.get() + b.get())           // 自动更新
var avg = computed(() => sum.get() / 2)               // 链式计算
var status = computed(() => if sum.get() > 80 then "优秀" else "及格")
```

### 依赖追踪原理

```
computed(fn) 求值时:
  1. 将自身推入 ReactiveTracker 栈顶
  2. 执行 fn()
  3. fn() 中调用的每个 inpc.get() 都注册当前 computed 为依赖
  4. 从栈中弹出

当任一依赖的 inpc.set() 被调用:
  1. 通知所有订阅的 UI 控件
  2. 标记所有依赖的 computed 为 dirty
  3. 下次读取 computed 时重新求值
```

### 使用场景

```javascript
// 条件样式
button({
    text = "提交",
    background = computed(() => if isValid.get() then "#6366f1" else "#94a3b8")
})

// 文本拼接
label({ text = computed(() => "共 " + count.get() + " 条记录") })

// 列表过滤
var selected = computed(() => {
    var items = data.get().where(x => x.checked)
    return table(items, data)
})
```

---

## table — 响应式表格数据

`table(source, sourceTable?)` 创建响应式表格数据源，用于 DataTable 控件。

```javascript
var data = table([
    { name = "张三", email = "1@test.com", role = "管理员", checked = false },
    { name = "李四", email = "2@test.com", role = "编辑",   checked = false },
])
```

### 行操作

```javascript
data.addRow({ name = "新用户", email = "new@test.com", role = "读者" })
data.removeRow(0)
data.get()                         // 获取所有行
data.get()[0].name                 // 访问行字段
data.set(index, row)               // 替换行
```

### 派生 table

```javascript
// 基于已有 table 创建视图（过滤/投影），传入源 table 以保持同步
var selected = computed(() => {
    var items = data.get().where(x => x.checked)
    return table(items, data)      // data 作为源 table
})
```

---

## vif / vfor — 结构化指令

### vif — 条件渲染

```javascript
var visible = inpc(true)
vif(visible, label({"text" = "可见内容"}))

// 切换显示/隐藏
visible.set(false)  // 内容隐藏（从视觉树中移除）
```

### vfor — 列表渲染

```javascript
var items = inpc(["Apple", "Banana", "Cherry"])

vfor(items, (item, index) =>
    label({"text" = str(index) + ". " + item})
)

// 数组变化时自动更新
items.push("Durian")
items.removeAt(1)
```

---

## component — 组件定义

```javascript
var Greeting = component((props) =>
    stackpanel({"children" = [
        label({"text" = "Hello, " + props.name}),
        label({"text" = props.message}),
    ]})
)

// 使用组件
Greeting({name = "World", message = "Welcome!"})
```

---

## style — 样式注册

`style(name, props)` 注册全局样式类，通过 `class` 属性应用。

### 基础样式

```javascript
style("primary", {
    "background" = "#6366f1",
    "color" = "#ffffff",
    "fontSize" = 13,
    "padding" = "8,16"
})

button({"text" = "提交", "class" = "primary"})
```

### 伪类样式

```javascript
style("primary:pointerover", { "background" = "#818cf8" })    // 悬停
style("primary:pressed",     { "background" = "#4f46e5" })    // 按下
style("primary:focus",       { "borderBrush" = "#f59e0b", "borderThickness" = 2 })  // 获焦
style("primary:disabled",    { "opacity" = 0.4 })             // 禁用
```

### 样式叠加

```javascript
// 多个 class 可叠加
button({"text" = "按钮", "class" = "primary large"})
```

---

## fetch / fetchAsync — HTTP 请求

### fetch — 同步请求

```javascript
var res = fetch("https://api.example.com/users")
if (res.ok) {
    var data = res.json()
    print(data)
} else {
    print("请求失败: " + res.text())
}
```

**⚠️ 同步请求会阻塞 UI，仅建议用于简单场景。**

### POST / PUT / DELETE

```javascript
// POST
var res = fetch("https://api.example.com/users", {
    "method" = "POST",
    "headers" = { "Content-Type" = "application/json" },
    "body" = "{\"name\":\"test\"}"
})

// DELETE
var res = fetch("https://api.example.com/users/1", {
    "method" = "DELETE"
})

print("status: " + str(res.status))
print(if res.ok then "成功" else "失败: " + res.text())
```

### fetchAsync — 异步请求

```javascript
// 不阻塞 UI，结果通过回调处理
fetchAsync("https://api.example.com/data", (res) => {
    if (res.ok) {
        result.set(res.json())
    } else {
        app.toast("请求失败: " + res.status, "error")
    }
})
```

### Response 对象

| 属性/方法 | 类型 | 说明 |
|-----------|------|------|
| `res.ok` | `bool` | 请求是否成功 (status 200-299) |
| `res.status` | `number` | HTTP 状态码 |
| `res.text()` | `string` | 响应文本 |
| `res.json()` | `any` | 解析 JSON 响应 |

## 内置函数

| 函数 | 说明 |
|------|------|
| `now()` | 当前日期时间 (DateTimeValue) |
| `date(str)` | 解析日期字符串 (ISO 8601) |
| `timespan(n, unit)` | 创建时间间隔 |
| `str(val)` | 转为字符串 |
| `int(val)` | 转为整数 |
| `len(arr)` | 获取数组长度 |
| `range(start, end)` | 创建范围迭代器 |
| `print(...args)` | 输出到控制台 |
| `typeof(val)` | 获取值类型名称 |
