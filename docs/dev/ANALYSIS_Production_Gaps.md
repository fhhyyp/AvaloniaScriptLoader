# 生产级软件差距分析

## 分析方法

从 8 个维度逐一审计当前代码库（16 个 .cs 文件），标注具体风险点。

---

## 1. 错误处理与容错

### 当前状态

```csharp
// 典型的当前模式（PropertyBinder.cs）：
public void SetControlProperty(Control control, string propertyName, Value value)
{
    try { ApplyProperty(control, propertyName, value); }
    catch (Exception ex) { Debug.WriteLine(...); }  // 吞掉异常
}
```

### 差距

| # | 问题 | 位置 | 风险等级 | 生产要求 |
|---|------|------|----------|----------|
| 1.1 | **异常被静默吞掉** | PropertyBinder.SetControlProperty, ControlBuilder event handlers, ComputedValue.Recompute, InpcValue.NotifyAll | 🔴 高 | 异常应记录到结构化日志 + 可选的回调通知用户 |
| 1.2 | **无全局异常边界** | ScriptEngineAdapter.ExecuteAsync | 🔴 高 | 需 try-catch 包裹整个脚本执行+构建流程，返回结构化错误对象 |
| 1.3 | **ComputedValue 求值失败返回 Value.Null** | ComputedValue.Recompute | 🟡 中 | 应保持上次有效值而非静默变为 null（stale-while-revalidate） |
| 1.4 | **vfor 模板失败跳过该项** | ControlBuilder.BuildVfor | 🟡 中 | 应渲染错误占位符而非跳过（用户看不到缺失项） |
| 1.5 | **事件处理器异常无声** | ControlBuilder.RegisterEvents | 🟡 中 | 应提供 `onError` 回调或全局错误事件 |
| 1.6 | **无脚本编译错误友好展示** | ScriptEngineAdapter | 🟡 中 | 当前抛裸 Exception，应解析位置信息并格式化 |

---

## 2. 线程安全

### 当前状态

脚本在后台线程执行，UI 操作通过 `Dispatcher.UIThread.Post` 调度。InpcValue 无任何线程同步。

### 差距

| # | 问题 | 位置 | 风险等级 | 生产要求 |
|---|------|------|----------|----------|
| 2.1 | **InpcValue._subscribers 无锁保护** | InpcValue.cs | 🔴 高 | 订阅者列表在 UI 线程添加、可能在任意线程遍历通知 → 需 `lock` 或 `ConcurrentBag` |
| 2.2 | **InpcValue._dependents 无锁保护** | InpcValue.cs | 🔴 高 | ComputedValue 的依赖追踪跨线程操作 → 需锁或不可变快照 |
| 2.3 | **ComputedValue._subscribers / _dependents 无锁** | ComputedValue.cs | 🔴 高 | 同上 |
| 2.4 | **ReactiveTracker 线程静态无嵌套保护** | ReactiveTracker.cs | 🟡 中 | 线程静态本身正确，但若 ComputedValue 求值期间触发异步操作可能丢上下文 |
| 2.5 | **ControlWrapper.Descriptor 跨线程读写** | ControlWrapper.cs | 🟡 中 | 描述符在 UI 线程构建、后台线程 setter 可能访问 → 非线程安全 |
| 2.6 | **ControlBuilder.Build 无重入保护** | ControlBuilder.cs | 🟢 低 | 若两个脚本同时 Build 可能状态混乱 |

---

## 3. 内存与资源管理

### 差距

| # | 问题 | 位置 | 风险等级 | 生产要求 |
|---|------|------|----------|----------|
| 3.1 | **事件处理器泄漏** | ControlBuilder.RegisterEvents | 🔴 高 | `button.Click += async (s, e) => {...}` 注册的匿名 Lambda 永不移除 → 控件无法被 GC |
| 3.2 | **InpcValue.OnChange 订阅无取消** | PropertyBinder.BindObservableValue | 🔴 高 | 绑定的回调永不移除 → InpcValue 持有 PropertyBinder 回调引用 → 阻止 GC |
| 3.3 | **ComputedValue.OnChange 同上** | PropertyBinder.BindObservableValue | 🔴 高 | 同上 |
| 3.4 | **vif/vfor 占位 Panel 的子控件未释放** | ControlBuilder.BuildVif/BuildVfor | 🟡 中 | 条件切换时旧子控件从 Panel 移除但事件未注销 |
| 3.5 | **ScriptEngineAdapter 的 _controlRegistry 无限增长** | ScriptEngineAdapter.cs | 🟡 中 | 控件注册后永不清理，即使控件已销毁 |
| 3.6 | **无 IDisposable 链路** | 多个文件 | 🟡 中 | ControlWrapper/InpcValue/ComputedValue 不实现 IDisposable → 事件订阅无法批量清理 |

---

## 4. 输入验证

### 差距

| # | 问题 | 位置 | 风险等级 | 生产要求 |
|---|------|------|----------|----------|
| 4.1 | **控件属性值无类型校验** | PropertyBinder | 🟡 中 | 传入 `"text" = NumberValue(42)` 会隐式 `AsString()` 转 "42" → 应警告或拒绝 |
| 4.2 | **颜色值无校验** | PropertyBinder.ParseHexColor | 🟢 低 | 无效颜色静默返回 Black |
| 4.3 | **GridLength 无校验** | PropertyBinder.ToGridLength | 🟢 低 | 无效值静默返回 Auto |
| 4.4 | **控件尺寸无边界检查** | PropertyBinder.ToDouble | 🟢 低 | 负数 width/height 无警告 |
| 4.5 | **inpc 初始值无类型提示** | InpcFactory | 🟢 低 | `inpc("hello")` 后 `set(42)` 接受，类型不一致无警告 |
| 4.6 | **vif 条件无类型校验** | StructureFactory.CreateVifFunction | 🟡 中 | 运行时才发现 condition 不是 bool 类型 |

---

## 5. 可观测性

### 当前状态

仅 `System.Diagnostics.Debug.WriteLine` 输出，Release 构建中不生效。

### 差距

| # | 问题 | 风险等级 | 生产要求 |
|---|------|----------|----------|
| 5.1 | **无结构化日志** | 🔴 高 | 需要 ILogger 抽象 + 分级（Info/Warn/Error）+ 上下文信息 |
| 5.2 | **无性能追踪** | 🟡 中 | 脚本编译/执行/构建耗时无计量 |
| 5.3 | **无脚本执行遥测** | 🟡 中 | 执行次数、错误率、平均耗时无统计 |
| 5.4 | **无调试工具** | 🟡 中 | 无法查看当前 InpcValue 依赖图、控件树状态 |
| 5.5 | **错误信息无上下文** | 🟡 中 | 当前错误消息不含脚本名、行号、变量值 |

---

## 6. API 完整性与一致性

### 差距

| # | 问题 | 风险等级 | 生产要求 |
|---|------|----------|----------|
| 6.1 | **控件覆盖不全** | 🟡 中 | 缺少 Image, ScrollViewer, Border, TabControl, DataGrid, Menu 等常用控件 |
| 6.2 | **属性映射不全** | 🟡 中 | Avalonia 控件的许多属性未映射（ToolTip, Opacity, ZIndex, ClipToBounds 等） |
| 6.3 | **ComboBox 只支持字符串列表** | 🟡 中 | 不支持对象列表 + DisplayMemberPath |
| 6.4 | **Grid 不支持 Row/Column 的 Min/Max 约束** | 🟢 低 | 仅支持固定值、Auto、Star |
| 6.5 | **无对话框 API** | 🟡 中 | OpenFileDialog, SaveFileDialog, MessageBox 未封装 |
| 6.6 | **无键盘/焦点管理** | 🟡 中 | 无法通过脚本设置焦点、处理快捷键 |
| 6.7 | **无拖拽支持** | 🟢 低 | 无拖拽事件 |
| 6.8 | **无剪贴板 API** | 🟢 低 | 无法读写剪贴板 |

---

## 7. 性能

### 差距

| # | 问题 | 位置 | 风险等级 | 生产要求 |
|---|------|------|----------|----------|
| 7.1 | **vfor 全量重建** | ControlBuilder.BuildVfor | 🟡 中 | 数组元素变更时重建所有子控件 → 应增量更新（key 追踪） |
| 7.2 | **ComputedValue 每次求值新建 VM** | ComputedValue._compute | 🟡 中 | `callable.CallAsync` 每次 `new VM()` → 应复用或缓存 |
| 7.3 | **PropertyBinder 每次 new 实例** | ControlWrapper.SetProperty | 🟢 低 | `new PropertyBinder()` 每次创建 → 应复用单例 |
| 7.4 | **值转换的装箱/拆箱** | PropertyBinder.ConvertValue | 🟢 低 | 反射 + 类型转换路径可优化 |
| 7.5 | **对象树全量遍历** | ControlBuilder.Build | 🟢 低 | 无懒加载，大型 UI 一次性构建 |

---

## 8. 安全性

### 差距

| # | 问题 | 风险等级 | 生产要求 |
|---|------|----------|----------|
| 8.1 | **脚本无沙箱限制** | 🟡 中 | 脚本可访问文件系统（通过 SystemModule）→ 需可配置禁用 |
| 8.2 | **无脚本执行超时** | 🟡 中 | 死循环或耗时操作无超时中断 |
| 8.3 | **无脚本大小/复杂度限制** | 🟢 低 | AST 深度、字节码大小无上限 |
| 8.4 | **无 XSS/注入防护** | 🟢 低 | 脚本生成 UI，恶意脚本可创建遮挡层钓鱼 |

---

## 9. 测试

### 差距

| # | 问题 | 风险等级 | 生产要求 |
|---|------|----------|----------|
| 9.1 | **零单元测试** | 🔴 高 | 无任何 test 项目 |
| 9.2 | **零集成测试** | 🔴 高 | 无法自动化验证端到端流程 |
| 9.3 | **无可测试性设计** | 🟡 中 | 大量 static 方法和 new 实例化，难以 mock |

---

## 10. 部署与运维

### 差距

| # | 问题 | 风险等级 | 生产要求 |
|---|------|----------|----------|
| 10.1 | **无配置系统** | 🟡 中 | 脚本路径、日志级别、超时等硬编码 |
| 10.2 | **无版本兼容策略** | 🟡 中 | 脚本语言演进无版本标记 |
| 10.3 | **无热重载** | 🟡 中 | 开发时需重启应用 |
| 10.4 | **无脚本预编译缓存持久化** | 🟢 低 | 每次启动重新编译 |

---

## 差距汇总

| 维度 | 🔴 致命 | 🟡 重要 | 🟢 改善 | 合计 |
|------|---------|---------|---------|------|
| 错误处理 | 2 | 4 | 0 | 6 |
| 线程安全 | 3 | 2 | 1 | 6 |
| 内存管理 | 3 | 3 | 0 | 6 |
| 输入验证 | 0 | 3 | 3 | 6 |
| 可观测性 | 1 | 4 | 0 | 5 |
| API 完整性 | 0 | 6 | 2 | 8 |
| 性能 | 0 | 2 | 3 | 5 |
| 安全性 | 0 | 2 | 2 | 4 |
| 测试 | 2 | 1 | 0 | 3 |
| 部署运维 | 0 | 3 | 1 | 4 |
| **合计** | **11** | **30** | **12** | **53** |

---

## 建议修复路线

### Phase 1：可靠性基础（🔴 致命项）

| 优先级 | 修复项 | 方案 |
|--------|--------|------|
| **P0** | 线程安全 | InpcValue/ComputedValue 订阅列表加 `lock` 或切换 `ConcurrentDictionary` |
| **P0** | 事件泄漏 | 实现 `IDisposable` 链路：ControlWrapper → PropertyBinder → InpcValue.OnChange 注销 |
| **P0** | 内存泄漏 | vif/vfor 子控件移除时注销事件处理器 |
| **P0** | 全局异常边界 | ScriptEngineAdapter 统一 try-catch，返回 `Result<T, Error>` |
| **P0** | 结构化日志 | 引入 `Microsoft.Extensions.Logging`，替换 Debug.WriteLine |
| **P0** | 单元测试 | 创建 xUnit 项目，覆盖 InpcValue/ComputedValue/PropertyBinder |

### Phase 2：成熟度提升（🟡 重要项）

- 输入验证：属性类型校验 + 边界检查
- 性能：vfor 增量更新（key 追踪）、VM 复用
- 可观测性：执行耗时统计、依赖图可视化端点
- API 补全：缺失控件 + 对话框 + 键盘焦点
- 安全：脚本超时 + 沙箱可配置

### Phase 3：生产就绪（🟢 改善项）

- 配置系统（appsettings.json / 环境变量）
- 预编译缓存持久化
- 版本兼容标记
- 热重载
