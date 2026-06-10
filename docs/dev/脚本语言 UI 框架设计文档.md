# 脚本语言 UI 框架设计文档

## 1. 概述

### 1.1 设计目标

设计一套脚本语言的 UI 框架，允许开发者通过声明式语法动态构建用户界面。脚本执行完毕后返回对象树，宿主程序解析该对象树并创建实际的 UI 控件。

### 1.2 核心特性

- **声明式构建**：通过嵌套对象定义 UI 结构
- **闭包捕获**：事件处理和逻辑通过闭包实现，自动捕获脚本变量
- **对象字面量配置**：使用 `"key" = "value"` 语法设置属性
- **模块化导入**：控件类型独立模块导入
- **响应式更新**：通过修改控件属性触发界面刷新

### 1.3 语法约束

- 对象字面量使用等号：`{key = "value"}`
- 函数定义使用 lambda：`() => {}`
- 无函数定义关键字
- 脚本执行后返回对象树供宿主解析

## 2. 模块设计

### 2.1 模块划分

text

```
avalonia              // 核心运行时
├── app              // 应用级别 API
│   ├── showMessage()
│   ├── log()
│   └── find(name)
│
avalonia.controls    // 控件模块
├── window           // 窗口
├── button           // 按钮
├── textbox          // 文本框
├── label            // 标签
├── checkbox         // 复选框
├── combobox         // 下拉框
├── listbox          // 列表框
├── stackpanel       // 堆叠面板
└── grid             // 网格布局
```



### 2.2 导入语法

javascript

```
// 核心模块
import { app } from "avalonia"

// 控件模块
import { window, button, textbox, label, listbox, grid } from "avalonia.controls"
```



## 3. API 设计

### 3.1 通用控件属性

所有控件支持以下通用属性：

| 属性      | 类型          | 说明                      | 示例                   |
| :-------- | :------------ | :------------------------ | :--------------------- |
| `width`   | number/string | 宽度                      | `200`                  |
| `height`  | number/string | 高度                      | `"auto"`               |
| `margin`  | number/object | 外边距                    | `10` 或 `{"top" = 10}` |
| `padding` | number/object | 内边距                    | `15`                   |
| `visible` | boolean       | 是否可见                  | `true`                 |
| `enabled` | boolean       | 是否启用                  | `true`                 |
| `name`    | string        | 控件名称（用于 app.find） | `"myButton"`           |
| `row`     | number        | Grid 行索引               | `0`                    |
| `col`     | number        | Grid 列索引               | `1`                    |
| `rowSpan` | number        | 跨行数                    | `2`                    |
| `colSpan` | number        | 跨列数                    | `3`                    |

### 3.2 Window（窗口）

javascript

```
window({
    "title" = "窗口标题",
    "width" = 800,
    "height" = 600,
    "content" = /* 子控件 */
})
```



| 属性      | 类型   | 说明     |
| :-------- | :----- | :------- |
| `title`   | string | 窗口标题 |
| `width`   | number | 窗口宽度 |
| `height`  | number | 窗口高度 |
| `content` | object | 根控件   |

### 3.3 Label（标签）

javascript

```
label({
    "text" = "显示文本",
    "fontSize" = 16,
    "color" = "#333333"
})
```



| 属性         | 类型   | 说明                                |
| :----------- | :----- | :---------------------------------- |
| `text`       | string | 显示文本                            |
| `fontSize`   | number | 字体大小                            |
| `color`      | string | 文字颜色                            |
| `fontWeight` | string | 字重：`"normal"`/`"bold"`           |
| `align`      | string | 对齐：`"left"`/`"center"`/`"right"` |

### 3.4 Button（按钮）

javascript

```
button({
    "text" = "按钮文字",
    "width" = 120,
    "onClick" = () => {
        // 点击事件处理
    }
})
```



| 属性         | 类型     | 说明             |
| :----------- | :------- | :--------------- |
| `text`       | string   | 按钮文字         |
| `color`      | string   | 文字颜色         |
| `background` | string   | 背景颜色         |
| `onClick`    | function | 点击事件（闭包） |

### 3.5 TextBox（文本框）

javascript

```
textbox({
    "placeholder" = "请输入...",
    "width" = 200,
    "onChange" = () => {
        // 文本变化事件
    }
})
```



| 属性          | 类型     | 说明         |
| :------------ | :------- | :----------- |
| `text`        | string   | 文本内容     |
| `placeholder` | string   | 占位提示文字 |
| `password`    | boolean  | 密码模式     |
| `readonly`    | boolean  | 只读模式     |
| `multiline`   | boolean  | 多行模式     |
| `onChange`    | function | 文本变化事件 |

### 3.6 CheckBox（复选框）

javascript

```
checkbox({
    "text" = "同意协议",
    "checked" = false,
    "onChange" = () => {
        // 状态变化事件
    }
})
```



| 属性       | 类型     | 说明         |
| :--------- | :------- | :----------- |
| `text`     | string   | 显示文字     |
| `checked`  | boolean  | 选中状态     |
| `onChange` | function | 状态变化事件 |

### 3.7 ComboBox（下拉框）

javascript

```
combobox({
    "items" = ["选项1", "选项2", "选项3"],
    "selected" = 0,
    "onSelect" = () => {
        // 选择变化事件
    }
})
```



| 属性           | 类型     | 说明           |
| :------------- | :------- | :------------- |
| `items`        | array    | 选项列表       |
| `selected`     | number   | 选中索引       |
| `selectedItem` | any      | 选中项（只读） |
| `onSelect`     | function | 选择变化事件   |

### 3.8 StackPanel（堆叠面板）

javascript

```
stackpanel({
    "orientation" = "vertical",  // "vertical" | "horizontal"
    "spacing" = 10,
    "padding" = 20,
    "children" = [
        /* 子控件列表 */
    ]
})
```



| 属性          | 类型   | 说明                                  |
| :------------ | :----- | :------------------------------------ |
| `orientation` | string | 排列方向：`"vertical"`/`"horizontal"` |
| `spacing`     | number | 子控件间距                            |
| `children`    | array  | 子控件列表                            |

### 3.9 Grid（网格布局）

javascript

```
grid({
    "rows" = ["auto", "*", 100],
    "cols" = [200, "*"],
    "children" = [
        label({ "text" = "单元格", "row" = 0, "col" = 0 }),
        button({ "text" = "按钮", "row" = 1, "col" = 1 })
    ]
})
```



| 属性       | 类型  | 说明       |
| :--------- | :---- | :--------- |
| `rows`     | array | 行定义数组 |
| `cols`     | array | 列定义数组 |
| `children` | array | 子控件列表 |

**行列定义语法：**

- `"auto"`：自适应内容
- `"*"`：按比例填充剩余空间
- `"2*"`：占两倍比例
- `100`：固定像素值

### 3.10 ListBox（列表框）

javascript

```
listbox({
    "items" = dataArray,
    "template" = (item) => {
        return "显示文本"
    },
    "onSelect" = () => {
        let selected = listBox.selected
        // 处理选中项
    }
})
```



| 属性       | 类型     | 说明                   |
| :--------- | :------- | :--------------------- |
| `items`    | array    | 数据源数组             |
| `selected` | any      | 当前选中项（只读）     |
| `template` | function | 项模板函数，返回字符串 |
| `onSelect` | function | 选中项变化事件         |

## 4. 应用级 API

### 4.1 app 对象

javascript

```
// 显示消息框
app.showMessage("消息内容")

// 输出日志
app.log("日志信息")

// 按名称查找控件
let control = app.find("controlName")
```



## 5. 使用示例

### 5.1 简单计数器

javascript

```
import { app } from "avalonia"
import { window, stackpanel, button, label } from "avalonia.controls"

let count = 0

let countLabel = label({
    "text" = "计数: 0",
    "fontSize" = 24
})

window({
    "title" = "计数器",
    "width" = 300,
    "height" = 200,
    "content" = stackpanel({
        "spacing" = 15,
        "padding" = 30,
        "children" = [
            countLabel,
            button({
                "text" = "增加",
                "onClick" = () => {
                    count = count + 1
                    countLabel.text = "计数: " + count
                }
            }),
            button({
                "text" = "重置",
                "onClick" = () => {
                    count = 0
                    countLabel.text = "计数: 0"
                }
            })
        ]
    })
})
```



### 5.2 待办事项列表

javascript

```
import { app } from "avalonia"
import { window, stackpanel, button, textbox, listbox } from "avalonia.controls"

let todos = [
    { "text" = "学习脚本语言", "done" = false },
    { "text" = "设计 UI 框架", "done" = true }
]

let input = textbox({
    "placeholder" = "输入新待办...",
    "width" = 250
})

let todoList = listbox({
    "items" = todos,
    "template" = (todo) => {
        return (todo.done ? "✓ " : "○ ") + todo.text
    },
    "onSelect" = () => {
        let todo = todoList.selected
        if (todo != null) {
            todo.done = !todo.done
            todoList.items = todos
        }
    }
})

window({
    "title" = "待办事项",
    "width" = 400,
    "height" = 300,
    "content" = stackpanel({
        "spacing" = 10,
        "padding" = 20,
        "children" = [
            stackpanel({
                "orientation" = "horizontal",
                "spacing" = 10,
                "children" = [
                    input,
                    button({
                        "text" = "添加",
                        "onClick" = () => {
                            if (input.text != "") {
                                todos.add({ "text" = input.text, "done" = false })
                                todoList.items = todos
                                input.text = ""
                            }
                        }
                    })
                ]
            }),
            todoList,
            button({
                "text" = "清除已完成",
                "onClick" = () => {
                    let active = todos.where(t => !t.done)
                    todos.clear()
                    for (let t in active) {
                        todos.add(t)
                    }
                    todoList.items = todos
                }
            })
        ]
    })
})
```



### 5.3 用户管理（Grid 布局）

javascript

```
import { app } from "avalonia"
import { window, grid, stackpanel, textbox, button, label, listbox } from "avalonia.controls"

let users = [
    { "name" = "张三", "email" = "zs@test.com", "role" = "管理员" },
    { "name" = "李四", "email" = "ls@test.com", "role" = "编辑" },
    { "name" = "王五", "email" = "ww@test.com", "role" = "用户" }
]

let detailPanel = stackpanel({
    "spacing" = 10,
    "padding" = 15,
    "children" = [
        label({ "text" = "请选择用户", "color" = "#999" })
    ]
})

let userList = listbox({
    "items" = users,
    "template" = (user) => {
        return user.name + " - " + user.role
    },
    "onSelect" = () => {
        let user = userList.selected
        if (user != null) {
            detailPanel.children = [
                label({ "text" = "用户详情", "fontSize" = 18 }),
                label({ "text" = "姓名: " + user.name }),
                label({ "text" = "邮箱: " + user.email }),
                label({ "text" = "角色: " + user.role }),
                button({
                    "text" = "删除",
                    "onClick" = () => {
                        let idx = users.indexOf(user)
                        if (idx >= 0) {
                            users.removeAt(idx)
                            userList.items = users
                            detailPanel.children = [
                                label({ "text" = "请选择用户", "color" = "#999" })
                            ]
                        }
                    }
                })
            ]
        }
    }
})

let searchBox = textbox({
    "placeholder" = "搜索用户...",
    "onChange" = () => {
        let keyword = searchBox.text
        if (keyword == "") {
            userList.items = users
        } else {
            let filtered = users.where(u => u.name.contains(keyword))
            userList.items = filtered
        }
    }
})

window({
    "title" = "用户管理",
    "width" = 600,
    "height" = 400,
    "content" = grid({
        "rows" = ["auto", "*"],
        "cols" = ["*", 250],
        "children" = [
            searchBox.toGrid(0, 0).margin(10),
            userList.toGrid(1, 0).margin(10),
            detailPanel.toGrid(1, 1)
        ]
    })
})
```



## 6. 宿主解析流程

### 6.1 执行流程

text

```
1. 加载脚本源码
2. 注入 app 运行时对象
3. 注入控件工厂函数
4. 执行脚本
5. 获取返回的对象树
6. 递归解析对象树
7. 创建实际 UI 控件
8. 建立数据绑定和事件处理
```



### 6.2 对象树解析器伪代码

javascript

```
function parseControlTree(node) {
    let control = createNativeControl(node.__type)
    
    for (let key in node) {
        if (key == "__type" || key == "children" || key == "content") {
            continue
        }
        
        let value = node[key]
        
        if (typeof value == "function") {
            // 闭包事件处理器
            control.addEventListener(parseEventName(key), value)
        } else {
            // 普通属性
            control.setProperty(key, value)
        }
    }
    
    // 处理子控件
    if (node.children) {
        for (let child of node.children) {
            control.addChild(parseControlTree(child))
        }
    }
    
    // 处理 content
    if (node.content) {
        control.setContent(parseControlTree(node.content))
    }
    
    return control
}
```



### 6.3 属性变化处理

当脚本中的闭包修改控件属性时（如 `label.text = "新文本"`），需要触发 UI 更新：

text

```
属性赋值 → 触发 setter → 通知宿主 → 更新 UI 控件
```



## 7. 设计原则

1. **简洁优先**：API 设计追求最简表达，减少概念负担
2. **闭包自然**：充分利用闭包捕获变量的特性，避免引入额外复杂度
3. **声明式构建**：UI 结构通过对象嵌套自然表达
4. **类型一致**：所有控件使用统一的对象字面量配置模式
5. **渐进增强**：简单场景简单写，复杂场景有支撑