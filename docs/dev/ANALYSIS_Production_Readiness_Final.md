# 生产可用 UI 脚本框架 — 最终差距分析

## 已完成能力矩阵

| 类别 | 已实现 | 数量 |
|------|--------|------|
| 控件 | window, button, label, textbox, checkbox, combobox, listbox, stackpanel, grid, image, scrollviewer, border, tabcontrol, tabitem, datagrid, dialog | 16 |
| 响应式 | inpc, computed, twoway, push/pop/removeAt | 完整 |
| 结构化 | vif, vfor, component | 3 |
| 样式 | style(), class, 5 缩写属性, margin/padding CSS 简写 | 完整 |
| 布局 | Grid rows/cols/*/auto, alignment, min/max w/h | 完整 |
| 事件 | onClick, onChange, onSelect, onKeyDown (+ event params) | 4 |
| 对话框 | 浮层 overlay, 遮罩, 居中卡片, fullscreen, closable, maskClosable, 嵌套 | 完整 |
| 模块化 | import/export 跨文件, data/styles 分离 | 完整 |
| 数据层 | fetch() HTTP + JSON, showMessage/Confirm | 完整 |
| 生产加固 | 线程安全, IDisposable, 异常边界, 超时, 日志 | 完整 |

## 待实现清单

### 🔴 致命（阻塞关键场景）

| # | 能力 | 影响 | 复杂度 |
|---|------|------|--------|
| 1 | **vfor 响应式更新** | 数据变更后列表不自动刷新，search/filter 场景不可用 | 高 |
| 2 | **表单校验** | 无 required/email/pattern 等校验原语，生产表单需要大量手动代码 | 中 |
| 3 | **onBlur / onFocus 事件** | 失焦校验、焦点样式切换无法实现 | 低 |
| 4 | **更好的错误诊断** | 脚本错误含行号但无代码片段和上下文，调试困难 | 中 |

### 🟡 重要（大幅提升体验）

| # | 能力 | 说明 | 复杂度 |
|---|------|------|--------|
| 5 | **DatePicker / Calendar** | 生产表单必需品 | 低 |
| 6 | **Slider / ProgressBar** | 设置面板、进度反馈 | 低 |
| 7 | **ListBox / ComboBox 对象绑定** | 当前仅支持字符串列表，无法显示对象属性 | 中 |
| 8 | **ToolTip** | 悬停提示，UX 标准特性 | 低 |
| 9 | **Keyboard 导航 (TabIndex)** | 无鼠标操作 | 中 |
| 10 | **app 生命周期钩子** | onStart / onExit / onWindowClose | 低 |
| 11 | **watch() 监听器** | `watch(inpc, (old, val) => {})` 带新旧值对比 | 低 |

### 🟢 改善（锦上添花）

| # | 能力 | 说明 |
|---|------|------|
| 12 | 动画/过渡 | dialog 打开关闭动画、列表过渡 |
| 13 | 多窗口管理 | app.openWindow / .closeWindow / onClose / 窗口传参 |
| 14 | 事件总线 | app.on / app.emit 解耦通信 |
| 15 | 国际化 i18n | $t('key') 模板函数 |
| 16 | 暗色主题 | 全局 theme 切换 |
| 17 | 热重载 | 脚本文件变更 → 自动重建 UI |
| 18 | 拖拽 | Drag & Drop 事件 |
| 19 | Menu / ContextMenu | 右键菜单、菜单栏 |
| 20 | 单元测试框架 | InpcValue / ComputedValue 测试覆盖 |

## 建议实施顺序

**Phase 1（立即可做）:** onBlur/onFocus + ToolTip + DatePicker + Slider + ProgressBar（5 个低复杂度项）

**Phase 2（需要设计）:** vfor 响应式 + 表单校验 + ListBox 对象绑定

**Phase 3（可选）：** app 生命周期 + watch + 事件总线 + 错误诊断优化

---

### 待确认

| # | 问题 |
|---|------|
| Q1 | vfor 响应式是最复杂的遗留问题。可选的实现方向：(A) 在脚本层提供 refresh API 手动触发重建 (B) 改造 vfor 为真正的响应式组件（类似 dialog 模式，订阅 InpcValue 变更自动重建） |
| Q2 | 表单校验：(A) HTML5 风格声明式 `{"required"=true, "pattern"="email"}` (B) 函数式 `validate(callback)` (C) 两者都支持 |
| Q3 | 是否优先做 Phase 1（5 个低复杂度项），再讨论 Phase 2 的设计？ |
