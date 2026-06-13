# Avalonia Controls API

> 模块 `"avalonia.controls"` — 提供 28 种控件构造器。

## 导入

```javascript
import {
    window, button, label, textbox, checkbox, combobox, listbox,
    stackpanel, grid, border, scrollviewer, image,
    tabcontrol, tabitem, expander,
    dialog, datepicker, timepicker,
    slider, progressbar,
    menu, menuitem, separator,
    navmenu, navmenuitem, navmenugroup,
    datatable
} from "avalonia.controls"
```

---

## 容器与布局

### window — 顶层窗口

```javascript
window({
    title = "窗口标题",
    width = 800, height = 600,
    canResize = true,
    systemDecorations = "none",    // "none" | "full"
    extendClientArea = true,
    windowDrag = true,             // 启用标题栏拖拽
    content = ...
})
```

> 脚本必须返回一个 `window()` 作为根控件。

### stackpanel — 线性布局

```javascript
stackpanel({
    orientation = "vertical",      // "vertical"（默认） | "horizontal"
    spacing = 8,                   // 子元素间距
    children = [ ... ]
})
```

### grid — 网格布局

```javascript
grid({
    rows = ["auto", "*", "2*"],    // auto=自适应 *=比例 数字=固定像素
    cols = [80, "*", 60],
    children = [
        label({ row = 0, col = 0, text = "用户名:" }),
        textbox({ row = 0, col = 1, colSpan = 2 }),
        // row / col 从 0 开始计数
    ]
})
```

**行列定义语法：**

| 值 | 含义 |
|------|------|
| `"auto"` | 自适应内容 |
| `"*"` | 1 倍比例 |
| `"2*"` / `"1.5*"` | 多倍比例 |
| `80` (数字) | 固定像素 |

**子元素定位：** `row` `col` `rowSpan` `colSpan`

### border — 边框容器

```javascript
border({
    borderBrush = "#e2e8f0",
    borderThickness = 1,
    cornerRadius = 6,
    background = "#ffffff",
    padding = 12,
    content = ...
})
```

### scrollviewer — 滚动视图

```javascript
scrollviewer({
    content = stackpanel({ ... })  // 内容超出时自动出现滚动条
})
```

### tabcontrol / tabitem — 标签页

```javascript
tabcontrol({
    items = [
        tabitem({ header = "基本", content = label({ text = "基本设置" }) }),
        tabitem({ header = "高级", content = label({ text = "高级设置" }) }),
    ]
})
```

### expander — 可折叠面板

```javascript
expander({
    header = "高级选项",
    isExpanded = true,             // 初始展开
    content = stackpanel({ ... })
})
```

---

## 基础控件

### label — 文本标签

| 属性 | 类型 | 说明 |
|------|------|------|
| `text` | `string` / `ComputedValue` | 文本内容 |
| `fontSize` | `number` | 字号 |
| `color` | `string` | 文本颜色 |
| `fontWeight` | `string` | 字重 ("normal" / "bold") |
| `textAlignment` | `string` | 对齐 ("left" / "center" / "right") |
| `textWrapping` | `bool` | 是否换行 |

```javascript
label({
    text = "Hello World",
    fontSize = 16,
    color = "#1e293b",
    fontWeight = "bold",
    textAlignment = "center"
})
```

### button — 按钮

| 属性 | 类型 | 说明 |
|------|------|------|
| `text` | `string` | 按钮文本 |
| `background` | `string` | 背景色 |
| `color` | `string` | 文本颜色 |
| `fontSize` | `number` | 字号 |
| `onClick` | `() => void` | 点击回调 |

```javascript
button({
    text = "提交",
    class = "primary",
    onClick = () => { app.toast("已提交", "success") }
})
```

### textbox — 文本输入框

| 属性 | 类型 | 说明 |
|------|------|------|
| `text` | `InpcValue` | 绑定文本（建议 twoway） |
| `placeholder` | `string` | 占位文本 |
| `width` | `number` | 宽度 |
| `password` | `bool` | 密码模式 |
| `readOnly` | `bool` | 只读 |
| `textWrapping` | `bool` | 多行换行 |
| `onChange` | `(e) => void` | 值变更回调 |

```javascript
var input = inpc("", "twoway")

textbox({
    text = input,
    placeholder = "请输入...",
    width = 300
})
```

---

## 选择控件

### checkbox — 复选框

```javascript
var checked = inpc(false, "twoway")

checkbox({
    text = "同意用户协议",
    checked = checked,
    onChange = (e) => { print("checked = " + str(e.value)) }
})
```

### combobox — 下拉框

```javascript
combobox({
    items = ["选项 A", "选项 B", "选项 C"],
    selectedIndex = 0,
    placeholder = "请选择",
    onSelect = (e) => { print("选中: " + e.selectedItem) }
})
```

### listbox — 列表框

```javascript
listbox({
    items = ["Item 1", "Item 2", "Item 3"],
    height = 120,
    selectedIndex = 0,
    onSelect = (e) => { print("选中: " + e.selectedItem) }
})
```

### slider — 滑块

```javascript
var value = inpc(50, "twoway")

slider({
    value = value,
    minimum = 0,
    maximum = 100,
    width = 300,
    onChange = (e) => { print("value = " + str(e.value)) }
})
```

---

## 进度与日期

### progressbar — 进度条

```javascript
progressbar({
    value = 60,
    minimum = 0,
    maximum = 100,
    isIndeterminate = false,       // true = 不确定进度（旋转动画）
    width = 300
})
```

### datepicker — 日期选择器

```javascript
var birth = inpc(date("2000-01-01"), "twoway")
var startDate = inpc(now(), "twoway")

datepicker({
    selectedDate = birth,
    width = 240
})

// 联动显示
label({ text = computed(() => "已选: " + birth.get().toString("yyyy-MM-dd")) })
label({ text = computed(() => "星期: " + birth.get().dayOfWeek) })
label({ text = computed(() => "距今 " + str(startDate.get().year - birth.get().year) + " 年") })
```

### timepicker — 时间选择器

```javascript
var meeting = inpc(now(), "twoway")

timepicker({
    selectedTime = meeting,
    width = 240,
    minuteIncrement = 5,           // 分钟步长
    clockIdentifier = "24HourClock" // "12HourClock" | "24HourClock"
})

label({ text = computed(() => "已选: " + meeting.get().toString("HH:mm")) })
```

### 日期/时间内置函数

| 函数 | 说明 |
|------|------|
| `now()` | 当前日期时间 |
| `date("2000-01-01")` | 解析日期字符串 (ISO 8601) |

### DateTimeValue 属性/方法

| 属性/方法 | 返回 | 说明 |
|-----------|------|------|
| `.year` | `int` | 年 (1-9999，本地时间) |
| `.month` | `int` | 月 (1-12，本地时间) |
| `.day` | `int` | 日 (1-31，本地时间) |
| `.hour` | `int` | 小时 (0-23，本地时间) |
| `.minute` | `int` | 分钟 (0-59，本地时间) |
| `.second` | `int` | 秒 (0-59，本地时间) |
| `.millisecond` | `int` | 毫秒 (0-999) |
| `.dayOfWeek` | `int` | 星期几 (0=周日 ~ 6=周六) |
| `.dayOfYear` | `int` | 一年中的第几天 (1-366) |
| `.ticks` | `long` | Tick 计数 |
| `.toString()` | `string` | 默认 `yyyy/MM/dd HH:mm:ss` |
| `.toString(format)` | `string` | 自定义格式，如 `"yyyy-MM-dd"` |

---

## 高级控件

### dialog — 浮层对话框

```javascript
var showDialog = inpc(false)
var inputText = inpc("", "twoway")

dialog({
    visible = showDialog,
    title = "输入内容",
    width = 380,
    maskClosable = true,           // 点击遮罩关闭
    fullScreen = false,
    content = stackpanel({ spacing = 10, children = [
        textbox({ text = inputText, placeholder = "请输入...", width = 320 }),
        stackpanel({ orientation = "horizontal", spacing = 8,
            horizontalAlignment = "right", children = [
            button({ text = "取消", onClick = () => { showDialog.set(false) } }),
            button({ text = "确定", class = "primary", onClick = () => {
                showDialog.set(false)
            } }),
        ]}),
    ]})
})
```

### image — 图片

```javascript
image({
    source = "path/to/image.png",
    width = 200, height = 120,
    stretch = "uniform"            // "uniform" | "fill" | "none" | "uniformToFill"
})
```

### datatable — 数据表格

```javascript
var items = table([
    { name = "张三", email = "1@test.com", role = "管理员", checked = false },
    { name = "李四", email = "2@test.com", role = "编辑",   checked = false },
])

datatable({
    height = 300,
    maxCount = 5,                            // 每页行数（分页）
    selectionMode = "multiple",              // "none" | "single" | "multiple"
    selectionBinding = "checked",            // 绑定的选择字段
    isReadOnly = false,
    columns = [
        { checkbox = true, width = "40" },
        { header = "姓名", binding = "name", width = "*", sortable = true },
        { header = "邮箱", binding = "email", width = "1.5*" },
        { header = "角色", binding = "role", width = "100" },
        { header = "操作", binding = "role", width = "80",
          template = (val, row, idx) => button({
              text = "查看", fontSize = 11,
              onClick = () => { app.toast("行" + str(idx) + ": " + row.name) }
          })
        },
    ],
    items = items,
    headerStyle = { bg = "#e2e8f0", fg = "#1e293b", fontSize = 13 },
    selectedStyle = { bg = "#dbeafe", fg = "#1e40af" },
})
```

**列定义：**

| 列属性 | 类型 | 说明 |
|--------|------|------|
| `checkbox` | `bool` | 复选框列 |
| `header` | `string` | 列标题 |
| `binding` | `string` | 绑定字段名 |
| `width` | `string` | `"*"` `"1.5*"` `"100"` `"auto"` |
| `sortable` | `bool` | 是否可排序 |
| `template` | `(value, row, index) → Control` | 自定义渲染模板 |

---

## 菜单系统

### menu / menuitem / separator — 菜单栏

```javascript
menu({ items = [
    menuitem({ header = "文件", items = [
        menuitem({ header = "新建", icon = "📄", onClick = () => { ... } }),
        menuitem({ header = "保存", icon = "💾", onClick = () => { ... } }),
        separator({}),
        menuitem({ header = "退出", onClick = () => { app.close() } }),
    ]}),
    menuitem({ header = "帮助", items = [
        menuitem({ header = "关于", onClick = () => { app.toast("v1.0", "info") } }),
    ]}),
]})
```

### 右键菜单 (contextMenu)

```javascript
border({
    contextMenu = [
        menuitem({ header = "复制", icon = "📋", onClick = () => { ... } }),
        menuitem({ header = "粘贴", icon = "📄", onClick = () => { ... } }),
        separator({}),
        menuitem({ header = "删除", icon = "🗑", onClick = () => { ... } }),
    ],
    content = ...
})
```

### navmenu / navmenuitem / navmenugroup — 导航菜单

```javascript
navmenu({ items = [
    navmenugroup({
        header = "基础控件",
        fontSize = 16,
        isExpanded = true,         // 初始展开
        items = [
            navmenuitem({ text = "Label 标签", onClick = () => { ... } }),
            navmenuitem({ text = "Button 按钮", onClick = () => { ... } }),
        ]
    }),
    navmenugroup({
        header = "选择控件",
        isExpanded = computed(() => currentPage.get() == "select"),  // 响应式
        items = [ ... ]
    }),
]})
```

---

## 通用属性

所有控件均支持以下通用属性：

| 属性 | 类型 | 说明 |
|------|------|------|
| `width` / `height` | `number` | 宽高 |
| `minWidth` / `minHeight` | `number` | 最小宽高 |
| `maxWidth` / `maxHeight` | `number` | 最大宽高 |
| `margin` | `number` / `string` / `number[]` / `object` | 外边距 |
| `padding` | `number` / `string` / `number[]` / `object` | 内边距 |
| `visible` | `bool` | 可见性 |
| `enabled` | `bool` | 启用/禁用 |
| `name` | `string` | 控件标识（`app.find()`） |
| `tooltip` | `string` | 提示文本 |
| `class` | `string` | CSS 类名 |
| `horizontalAlignment` | `string` | 水平对齐 |
| `verticalAlignment` | `string` | 垂直对齐 |
| `background` | `string` | 背景色 |
| `foreground` | `string` | 前景色（文本颜色） |

### 属性值格式速查

**尺寸/间距：**
```javascript
margin = 12                      // 四边 12
margin = "2,4,6,8"              // 上右下左（CSS 顺序）
margin = [4, 8]                  // 垂直 水平
padding = { top = 2, left = 4 }  // 对象格式
```

**颜色：**
```javascript
color = "red"                    // 命名颜色
color = "#6366f1"               // Hex
background = "transparent"       // 透明
```

**对齐：**
```javascript
horizontalAlignment = "center"   // left | center | right | stretch
verticalAlignment = "center"     // top | center | bottom | stretch
```

---

## 通用事件

| 事件 | 说明 | 回调参数 |
|------|------|----------|
| `onClick` | 点击事件 | `()` |
| `onChange` | 值变更事件 | `(e)` — `e.property` `e.value` |
| `onSelect` | 选择变更事件 | `(e)` — `e.selectedIndex` `e.selectedItem` |
| `onKeyDown` | 按键事件 | `(e)` — `e.key` |
| `onFocus` | 获得焦点 | `(e)` — `e.source` |
| `onBlur` | 失去焦点 | `(e)` — `e.source` |

---

## 属性缩写

| 缩写 | 全名 |
|------|------|
| `bg` | `background` |
| `size` | `fontSize` |
| `halign` | `horizontalAlignment` |
| `valign` | `verticalAlignment` |
| `radius` | `cornerRadius` |

## 控件属性速查

| 控件 | 特有属性 |
|------|----------|
| `window` | `title` `canResize` `systemDecorations` `extendClientArea` `windowDrag` |
| `button` | `text` `onClick` |
| `label` | `text` `fontWeight` `textAlignment` `textWrapping` |
| `textbox` | `text` `placeholder` `password` `readOnly` `textWrapping` |
| `checkbox` | `text` `checked` |
| `combobox` | `items` `selectedIndex` `placeholder` |
| `listbox` | `items` `selectedIndex` |
| `slider` | `value` `minimum` `maximum` |
| `progressbar` | `value` `minimum` `maximum` `isIndeterminate` |
| `datepicker` | `selectedDate` `minYear` `maxYear` |
| `timepicker` | `selectedTime` `minuteIncrement` `clockIdentifier` |
| `dialog` | `title` `visible` `width` `height` `maskClosable` `fullScreen` |
| `image` | `source` `stretch` |
| `border` | `borderBrush` `borderThickness` `cornerRadius` |
| `stackpanel` | `orientation` `spacing` |
| `grid` | `rows` `cols` (子元素: `row` `col` `rowSpan` `colSpan`) |
| `tabcontrol` | `items` (tabitem) |
| `tabitem` | `header` |
| `expander` | `header` `isExpanded` |
| `scrollviewer` | — (容器) |
| `menu` / `menuitem` | `header` `icon` `items` |
| `separator` | — |
| `navmenu` | `items` (navmenuitem / navmenugroup) |
| `navmenuitem` | `text` `icon` |
| `navmenugroup` | `header` `items` `isExpanded` `fontSize` |
| `datatable` | `items` `columns` `maxCount` `selectionMode` `selectionBinding` `isReadOnly` `headerStyle` `selectedStyle` |
