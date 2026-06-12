# AvaloniaScriptLoader — 下一阶段工作计划

**日期**: 2026-06-12  
**状态**: 待审批

---

## 当前进度总览

### 已完成

| 类别 | 项目 |
|------|------|
| **控件** | Button, Label, TextBox, CheckBox, ComboBox, ListBox, Slider, ProgressBar, DatePicker, TimePicker, Border, ScrollViewer, StackPanel, Grid, TabControl, TabItem, Expander, Image, Dialog, DataGrid |
| **导航** | NavMenu, NavMenuItem, NavMenuGroup (分组折叠/图标/active) |
| **菜单** | Menu, MenuItem, Separator (菜单栏 + ContextMenu) |
| **框架** | inpc/computed/vif/vfor, 动态 import(), Toast, 伪类样式, 无边框窗口, windowDrag, app.close(), window.close() |
| **引擎** | 帧栈深度隔离, if-then 栈平衡, InPlaceOp 补 LoadSlot, TryWrapper.ok 修复, content observable 绑定 |
| **画廊** | gallery-route (25 个独立 demo + 动态 import 路由) |

---

## 下一阶段计划

### Phase 1: 生产必备 (P0)

| # | 特性 | 说明 | 工作量 |
|---|------|------|--------|
| 1 | **HTTP Client 增强** | `fetch()` 支持 POST/PUT/DELETE、headers、JSON body、超时、错误处理 | ~2h |
| 2 | **表单验证** | `required`/`pattern`/`minLength`/自定义校验 + 错误提示绑定 | ~4h |
| 3 | **全局错误边界** | 脚本异常时的 UI 兜底展示，替代静默崩溃 | ~2h |

### Phase 2: 控件扩展 (P1)

| # | 特性 | 说明 | 工作量 |
|---|------|------|--------|
| 4 | **TreeView** | 层级树控件，Avalonia 原生内置 | ~3h |
| 5 | **DataGrid 增强** | 列定义/排序/行选/编辑模式 | ~4h |
| 6 | **SplitView** | IDE 式可拖拽分栏面板 | ~3h |
| 7 | **ToggleSwitch** | 开关控件（Avalonia 内置） | ~1h |
| 8 | **Calendar/CalendarDatePicker** | 日历视图 | ~2h |

### Phase 3: 主题 & 体验 (P2)

| # | 特性 | 说明 | 工作量 |
|---|------|------|--------|
| 9 | **暗色主题** | `app.setTheme("dark")` 全局切换 | ~3h |
| 10 | **虚拟滚动** | `VirtualizingStackPanel` 暴露到脚本 | ~2h |
| 11 | **拖拽排序** | ListBox/DataGrid 拖拽重排 | ~3h |
| 12 | **键盘快捷键声明式** | `shortcut = "Ctrl+S"` 属性 | ~2h |

### Phase 4: 应用模板 & 生态 (P3)

| # | 特性 | 说明 | 工作量 |
|---|------|------|--------|
| 13 | **Todo App 示例** | 完整 CRUD + 路由 + 表单验证 | ~4h |
| 14 | **Settings App 示例** | 多 Tab 设置面板 + 持久化 | ~3h |
| 15 | **Dashboard 示例** | 数据仪表盘 + Chart 绑定 | ~4h |
| 16 | **多窗口管理** | `app.createWindow(...)` 子窗口 | ~3h |
| 17 | **LocalStorage** | 键值持久化 API (`app.storage.set/get`) | ~2h |

---

## 建议优先启动

**Phase 1 — 全部三项**，因为它们是构建生产系统的底线：

```
HTTP Client → 表单验证 → 全局错误边界
```

之后按需从 Phase 2-4 选取。建议从 **TreeView** + **ToggleSwitch** 入手（Avalonia 原生控件，实现成本低，视觉效果好）。

---

## 完整能力矩阵

```
                   基础控件  高级控件  导航    样式    数据    通信    路由    主题
AvaloniaScriptLoader   ✅       ✅      ✅      ✅     ⚠️      ⚠️     ✅(动态)  ❌
WPF                   ✅       ✅      ❌      ✅     ✅      ✅      ❌      ❌
Electron              ✅       ✅      ✅      ✅     ✅      ✅      ✅      ✅
Flutter               ✅       ✅      ✅      ✅     ✅      ✅      ✅      ✅
```

**关键差距**：HTTP 通信、数据验证、主题切换、持久化存储。补上这些后即可对标轻量级桌面框架。

---

## 预估总投入

| 阶段 | 工时 | 里程碑 |
|------|------|--------|
| Phase 1 | 8h | 可构建 CRUD 应用 |
| Phase 2 | 13h | 控件完备度 90% |
| Phase 3 | 10h | 可发布 1.0-alpha |
| Phase 4 | 16h | 有完整示例 + 文档 |
