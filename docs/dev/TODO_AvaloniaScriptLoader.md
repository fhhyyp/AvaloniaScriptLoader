# TODO — Avalonia 脚本加载器待办事项

## 未完成事项

### 1. 属性映射完善
- [ ] `password` 属性：需确认 Avalonia 12.x 中 TextBox 密码模式的实际 API（`PasswordChar` vs `NewPassword`）
- [ ] `multiline` 属性：`AcceptsReturn` 可能需配合 `TextWrapping` 实现完整多行
- [ ] `align` 属性：`TextAlignment` 的导入可能需在 Avalonia 12.x 中调整命名空间

### 2. ListBox 模板支持
- [ ] 当前 `template` 属性仅存储，未在 ControlBuilder 中实现
- [ ] 需要实现 `Func<ObjectValue, string>` 模板回调 → ItemTemplate

### 3. 应用级 API
- [ ] `app.showMessage()`：需要使用 Avalonia 12.x 的 MessageBox API 或自定义对话框
- [ ] `app.log()`：当前输出到 Debug，可考虑输出到 UI 日志面板
- [ ] `app.find(name)`：当前依赖 ControlWrapper 注册表，需验证跨模块查找

### 4. 事件处理增强
- [ ] 事件参数传递：将 Avalonia 事件参数（如 RoutedEventArgs）传递给脚本 Lambda
- [ ] 更多事件支持：onLoad、onClose、onKeyDown 等

### 5. 控件扩展
- [ ] Image 控件
- [ ] Slider 控件
- [ ] ProgressBar 控件
- [ ] TabControl 控件
- [ ] ScrollViewer / Border 容器

## 测试待办
- [ ] 在 Avalonia 12.0.4 桌面环境运行 Counter.script 验证
- [ ] 在 Avalonia 12.0.4 桌面环境运行 HelloWorld.script 验证
- [ ] 验证 Grid 布局（rows/cols + row/col/rowSpan/colSpan 附加属性）
- [ ] 验证 ComboBox/ListBox 的 items 绑定和事件

## 缺失配置
- [ ] 配置示例脚本的默认加载路径（环境变量 vs 命令行参数 vs 内嵌资源）

## 后续优化建议
1. **预编译脚本**：支持 .ssc 字节码直接执行，跳过编译阶段
2. **热重载**：脚本文件变更 → 自动重新执行 → UI 刷新
3. **错误展示**：美观的脚本错误对话框（含行号和代码片段）
4. **脚本编辑器集成**：提供 Avalonia 控件 + ScriptLang 语法高亮
5. **响应式属性绑定**：通过 INotifyPropertyChanged 实现脚本变量 ↔ 控件属性的自动同步
