# 剩余基础功能差距分析 — 基于 ComputedBinding.script

## 当前能力基线（ComputedBinding.script 已覆盖）

```
✅ inpc(x)         — 响应式状态
✅ inpc(x,"twoway")— 双向绑定
✅ computed(()=>)  — 计算属性 + 自动依赖追踪 + 嵌套 computed
✅ 事件参数 e      — onChange(e) { e.value, e.checked, e.selected, e.index }
✅ 9 种基础控件    — Window/Button/Label/TextBox/CheckBox/ListBox/StackPanel/Grid
✅ 属性绑定        — text/fontSize/color/width/... + setXxx() 方法
```

---

## 剩余差距：按影响面分级

### 🔴 致命缺失（没有它无法构建非平凡应用）

| # | 能力 | 当前脚本表现 | 现代框架对照 |
|---|------|------------|------------|
| **1** | **条件渲染** | 所有控件在树中静态定义，无法根据状态动态挂载/卸载 | Vue: `v-if`, React: `{cond && <Comp/>}` |
| **2** | **列表模板渲染** | 数组变更需整体 `items.set(arr)` 替换，无法为每个元素定义独立控件模板 | Vue: `v-for`, React: `array.map()` |
| **3** | **组件/代码复用** | ComputedBinding.script 123 行中大量重复模式（nameRow 改为组件只需 8 行调用） | Vue: `defineComponent`, React: function component |

### 🟡 严重缺失（大幅影响开发效率）

| # | 能力 | 当前脚本表现 | 现代框架对照 |
|---|------|------------|------------|
| **4** | **动态样式绑定** | 颜色硬编码为 `"#555"`，无法根据状态切换（如 `agreeTerms` 为 false 时按钮变红） | Vue: `:class`, `:style` |
| **5** | **控件属性读取** | 只能通过事件参数 `e.value` 读取 TextBox 文本，无法读取 CheckBox 状态、ComboBox 选中项等 | Vue: `v-model` 自动同步 |
| **6** | **watch 监听器** | 无法在值变更时执行副作用（如保存到 localStorage、发送请求） | Vue: `watch()`, React: `useEffect` |

### 🟢 改善缺失（锦上添花）

| # | 能力 | 当前脚本表现 | 现代框架对照 |
|---|------|------------|------------|
| **7** | **生命周期钩子** | 无 onMounted/onUnmounted，无法做初始化/清理 | Vue: `onMounted`, React: `useEffect([], [])` |
| **8** | **异步操作** | 无法在事件处理器中做 HTTP 请求、定时器等 | JS: `fetch()`, `setTimeout()` |
| **9** | **表单验证** | 无内置验证，需手动写 if/else | Vue: vee-validate, React: react-hook-form |

---

## 具体场景对照（以 ComputedBinding.script 为例）

### 场景 1：条件渲染 — 缺失 `v-if`

```javascript
// ❌ 当前：无法实现"未同意条款时显示警告"
// 只能定义静态控件树，无法动态增删

// ✅ 理想：
var showWarning = computed(() => not agreeTerms.get())
vif(showWarning, label({
    "text" = "请先同意服务条款！",
    "color" = "red"
}))
```

### 场景 2：列表模板 — 缺失 `v-for`

```javascript
// ❌ 当前：数组 items 整体替换，无法为每项自定义控件
// 只能 listbox 显示纯文本

// ✅ 理想：为 TodoList 的每一项渲染自定义控件
vfor(items, (item, index) => stackpanel({
    "children" = [
        checkbox({"checked" = item.done}),
        label({"text" = item.text}),
        button({"text" = "删除", "onClick" = () => items.get().removeAt(index)})
    ]
}))
```

### 场景 3：组件 — 缺失复用

```javascript
// ❌ 当前：nameRow 已经手动提取为变量，但仍是全局定义
// email行、age行等重复 StackPanel 模式无法抽取

// ✅ 理想：
component("FormField", ["label", "value", "placeholder"], (props) => {
    return stackpanel({
        "children" = [
            label({"text" = props.label}),
            textbox({"text" = props.value, "placeholder" = props.placeholder})
        ]
    })
})
// 使用：FormField({label="姓:", value=firstName, placeholder="输入姓"})
// 使用：FormField({label="名:", value=lastName, placeholder="输入名"})
```

### 场景 4：动态样式 — 缺失 `:style`

```javascript
// ❌ 当前：所有 label 颜色硬编码
label({"text" = agreementLabel, "fontSize" = 14, "color" = "#1976D2"})

// ✅ 理想：颜色根据 agreeTerms 状态动态变化
var labelColor = computed(() => agreeTerms.get() ? "#1976D2" : "red")
label({"text" = agreementLabel, "color" = labelColor})
```

### 场景 5：控件读取 — 缺失通用 getter

```javascript
// ❌ 当前：只能通过事件参数读取 TextBox 文本
"onChange" = (e) => { firstName.set(e.value) }

// ❌ 缺失：无法在任意时刻读取 CheckBox 状态
// agreeTerms.get() 是 InpcValue，但 CheckBox 手动点击时不会回写（非 twoway）
// twoway 模式虽可回写，但无法读取 ComboBox.selectedItem 等复杂属性
```

---

## 建议实现顺序

按**投入产出比**排序，优先实现影响面最大、实现成本最低的：

| 优先级 | 功能 | 预估复杂度 | 解锁场景 | 实现思路 |
|--------|------|-----------|---------|---------|
| **P0-1** | 条件渲染 `v-if` | 🟢 低 | 警告提示、登录/登出切换、空状态 | `vif(inpcBool, element)` → Placeholder 控件 → subscription 动态挂载/卸载 |
| **P0-2** | 列表模板 `v-for` | 🟡 中 | TodoList 每一项自定义、动态表单 | `vfor(inpcArray, (item,index)=>element)` → 监听数组变更 → 增量重建子控件 |
| **P0-3** | 组件系统 | 🟡 中 | 消除重复代码、FormField/ButtonGroup 复用 | `component(name, props, renderFn)` → ComponentRegistry → 脚本调用生成控件 |
| **P1-1** | 动态样式绑定 | 🟢 低 | 错误态红色、成功态绿色、禁用态灰色 | `inpc` 直接绑定 color/background → PropertyBinder 自动订阅 |
| **P1-2** | watch 监听器 | 🟢 低 | 值变更写 localStorage、日志、副作用 | `watch(inpc, (old,new)=>...)` → 调用 OnChange + 传递旧值 |
| **P1-3** | 通用控件 getter | 🟡 中 | 读取 CheckBox/ComboBox 当前值 | 控件工厂自动注入 `getText()`/`getChecked()` 等 getter 方法 |
| **P2-1** | 生命周期钩子 | 🟢 低 | 初始化数据、清理定时器 | `onMounted(callback)` / `onUnmounted(callback)` → ControlBuilder 调用 |
| **P2-2** | fetch/timer 异步 API | 🟢 低 | 网络请求、轮询 | 注入 `fetch()` / `setTimeout()` 到 avalonia 模块 |

---

## 与上次分析报告的进度对比

| 上次 P0 项 | 状态 |
|-----------|------|
| computed 计算属性 | ✅ 已完成 |
| 双向绑定 | ✅ 已完成 |
| v-if 条件渲染 | ❌ 待实现 |
| v-for 列表渲染 | ❌ 待实现 |
| 组件系统 | ❌ 待实现 |

---

## 决策建议

以下 3 项需要确认：

| # | 决策 | 建议 |
|---|------|------|
| **D1** | P0 剩余 3 项（v-if/v-for/组件）的实施顺序？ | 建议: v-if → v-for → 组件（按复杂度递增） |
| **D2** | 是否并行实现 P1（动态样式/watch/getter）？ | 建议: 先完成 P0，P1 项目可在间隙穿插 |
| **D3** | 组件系统的定位：轻量模板复用 还是 完整组件模型（含生命周期/props 验证/slots）？ | 建议: 先轻量（props→element 函数），后续迭代增强 |
