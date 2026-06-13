# AvaloniaScriptLoader

通过自定义脚本语言动态生成 Avalonia UI 的框架。声明式语法描述界面，运行时构建原生控件。

```javascript
import { inpc, computed } from "avalonia"
import { window, stackpanel, button, label } from "avalonia.controls"

var count = inpc(0)
return window({
    title = "计数器", width = 300, height = 200,
    content = stackpanel({ spacing = 15, padding = 30, children = [
        label({ text = computed(() => "计数: " + count.get()), fontSize = 24 }),
        button({ text = "+1", onClick = () => { count.set(count.get() + 1) } }),
    ]})
})
```

---

## 📖 文档目录

| 文档 | 说明 |
|------|------|
| [项目介绍](docs/project/INTRODUCTION.md) | 核心理念、特性列表、技术栈、示例概览 |
| [项目架构](docs/project/ARCHITECTURE.md) | 目录结构、分层架构、设计模式、依赖关系 |
| [如何使用](docs/project/GETTING_STARTED.md) | 环境配置、快速开始、脚本结构、模块系统、常见问题 |
| [Avalonia API](docs/project/AVALONIA_API.md) | `app` 应用交互、`inpc`/`computed`/`table` 响应式数据、`vif`/`vfor`/`component` 结构化指令、`style` 样式系统、`fetch` HTTP 请求 |
| [Controls API](docs/project/CONTROLS_API.md) | 28 种控件完整参考：容器布局、基础控件、选择控件、日期时间、DataTable、菜单系统、通用属性/事件/缩写 |
| [如何二次开发](docs/project/DEVELOPMENT.md) | 添加控件/事件/模块/自定义控件的完整步骤、DateTime 处理规范、代码规范、构建发布 |

### 示例项目

| 示例 | 路径 | 说明 |
|------|------|------|
| 控件画廊 | [Samples/gallery-route/](AvaloniaScriptLoader/Samples/gallery-route/) | 📦 26 个独立控件 demo，分类导航 |
| 模块化示例 | [Samples/demo/](AvaloniaScriptLoader/Samples/demo/) | 多文件用户管理 |
| Dialog 测试 | [Samples/demo-dialog/](AvaloniaScriptLoader/Samples/demo-dialog/) | 对话框组件测试 |
| 控件测试 | [Samples/demo-controls/](AvaloniaScriptLoader/Samples/demo-controls/) | 新控件集成测试 |

### 

---

## 社群

**QQ群 955830545** 提供技术交流与支持，欢迎加入。

> 因为个人是社畜，所以可能不会及时回复，请谅解。

## 📄 许可证

本项目基于 **MIT 许可证** 开源。
