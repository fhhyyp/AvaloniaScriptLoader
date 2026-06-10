using Avalonia.Controls;
using Avalonia.Threading;
using ScriptLang.Runtime;
using AvaloniaScriptLoader.Factory;
using AvaloniaScriptLoader.Model;
using AvaloniaScriptLoader.Wrapper;

namespace AvaloniaScriptLoader.Builder;

/// <summary>
/// 控件构建器 — 将 ObjectValue 描述符树递归转换为 Avalonia 控件树
/// 必须在 UI 线程调用。支持 vif 条件渲染。
/// </summary>
public class ControlBuilder
{
    private readonly ScriptEngineAdapter _adapter;
    private readonly PropertyBinder _binder = new();

    public ControlBuilder(ScriptEngineAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    public Control Build(ObjectValue descriptor)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("ControlBuilder.Build 必须在 UI 线程调用");

        return BuildInternal(descriptor);
    }

    public Window? BuildWindow(ObjectValue descriptor)
    {
        var control = Build(descriptor);
        Window? result;
        if (control is Window window)
            result = window;
        else
            result = new Window
            {
                Title = "Avalonia Script",
                Width = 800, Height = 600,
                Content = control,
            };

        // 将主窗口注册到 AvaloniaModule（用于对话框 parent）
        Modules.AvaloniaModule.MainWindow = result;
        return result;
    }

    // ========================================================================
    // 递归构建（入口：处理结构化指令）
    // ========================================================================

    private Control BuildInternal(ObjectValue descriptor)
    {
        var type = descriptor.Properties[ControlMeta.TypeKey].AsString();

        // === v-if 条件渲染 ===
        if (type == "vif")
            return BuildVif(descriptor);

        // === v-for 列表渲染 ===
        if (type == "vfor")
            return BuildVfor(descriptor);

        // === 标准控件 ===
        var control = CreateNativeControl(type);

        // 1. 应用初始属性
        _binder.ApplyInitialProperties(control, descriptor);

        // 2. 注册事件处理器
        RegisterEvents(control, descriptor);

        // 3. 创建并激活 ControlWrapper
        var wrapper = new ControlWrapper(control, descriptor);
        wrapper.Activate();

        // 4. 注册到控件注册表（用于 app.find）
        if (descriptor.Properties.TryGetValue("name", out var nameValue))
        {
            var name = nameValue.AsString();
            if (!string.IsNullOrEmpty(name))
                _adapter.RegisterControl(name, wrapper);
        }

        // 5. 递归处理 content（单一子控件）
        if (descriptor.Properties.TryGetValue("content", out var contentValue)
            && contentValue is ObjectValue contentObj)
        {
            var childControl = BuildInternal(contentObj);
            SetContent(control, childControl);
        }

        // 6. 递归处理 children（子控件列表）
        if (descriptor.Properties.TryGetValue("children", out var childrenValue)
            && childrenValue is ArrayValue children)
        {
            foreach (var childDesc in children.Elements)
            {
                if (childDesc is ObjectValue childObj)
                {
                    var childControl = BuildInternal(childObj);
                    AddChild(control, childControl, childObj);
                }
            }
        }

        return control;
    }

    // ========================================================================
    // 控件创建
    // ========================================================================

    private static Control CreateNativeControl(string type) => type switch
    {
        ControlMeta.Types.Window       => new Window(),
        ControlMeta.Types.Button       => new Button(),
        ControlMeta.Types.Label        => new TextBlock(),
        ControlMeta.Types.TextBox      => new TextBox(),
        ControlMeta.Types.CheckBox     => new CheckBox(),
        ControlMeta.Types.ComboBox     => new ComboBox(),
        ControlMeta.Types.ListBox      => new ListBox(),
        ControlMeta.Types.StackPanel   => new StackPanel(),
        ControlMeta.Types.Grid         => new Grid(),
        ControlMeta.Types.Image        => new Image(),
        ControlMeta.Types.ScrollViewer => new ScrollViewer(),
        ControlMeta.Types.Border       => new Border(),
        ControlMeta.Types.TabControl   => new TabControl(),
        ControlMeta.Types.TabItem      => new TabItem(),
        _ => throw new ArgumentException($"未知控件类型: '{type}'"),
    };

    // ========================================================================
    // 子控件添加
    // ========================================================================

    private static void SetContent(Control parent, Control child)
    {
        switch (parent)
        {
            case Window window:
                window.Content = child;
                break;
            case ContentControl cc:
                cc.Content = child;
                break;
            default:
                // 尝试反射设置 Content 属性
                var contentProp = parent.GetType().GetProperty("Content");
                contentProp?.SetValue(parent, child);
                break;
        }
    }

    private static void AddChild(Control parent, Control child, ObjectValue childDescriptor)
    {
        // 设置 Grid 附加属性
        if (parent is Grid)
        {
            if (childDescriptor.Properties.TryGetValue("row", out var row))
                Grid.SetRow(child, (int)PropertyBinder.ToDouble(row));
            if (childDescriptor.Properties.TryGetValue("col", out var col))
                Grid.SetColumn(child, (int)PropertyBinder.ToDouble(col));
            if (childDescriptor.Properties.TryGetValue("rowSpan", out var rowSpan))
                Grid.SetRowSpan(child, (int)PropertyBinder.ToDouble(rowSpan));
            if (childDescriptor.Properties.TryGetValue("colSpan", out var colSpan))
                Grid.SetColumnSpan(child, (int)PropertyBinder.ToDouble(colSpan));
        }

        switch (parent)
        {
            case TabControl tab:
                tab.Items.Add(child);
                break;
            case Panel panel:
                panel.Children.Add(child);
                break;
            case Decorator decorator:
                decorator.Child = child;
                break;
            default:
                // 尝试反射设置 Content 或 Children
                var childrenProp = parent.GetType().GetProperty("Children");
                if (childrenProp?.GetValue(parent) is Avalonia.Controls.Controls children)
                {
                    children.Add(child);
                }
                else
                {
                    SetContent(parent, child);
                }
                break;
        }
    }

    // ========================================================================
    // 事件注册（模拟 Vue $event 参数传递）
    // ========================================================================

    private void RegisterEvents(Control control, ObjectValue descriptor)
    {
        // 在 Build 时捕获引擎引用（窗口关闭后 _adapter.Engine 可能被置 null）
        var engine = _adapter.Engine!;
        var props = descriptor.Properties;

        // onClick
        if (props.TryGetValue("onClick", out var onClick) && onClick is ICallable clickFunc)
        {
            var clickArgs = Evt("click", ("name", StringValue.Create(control.Name ?? "")));
            switch (control)
            {
                case Button button:
                    button.Click += async (s, e) =>
                    {
                        try { await clickFunc.CallAsync(engine, [clickArgs]); }
                        catch (Exception ex) { LogEventError("onClick", ex); }
                    };
                    break;
                default:
                    control.Tapped += async (s, e) =>
                    {
                        try { await clickFunc.CallAsync(engine, [clickArgs]); }
                        catch (Exception ex) { LogEventError("onClick", ex); }
                    };
                    break;
            }
        }

        // onChange
        if (props.TryGetValue("onChange", out var onChange) && onChange is ICallable changeFunc)
        {
            switch (control)
            {
                case TextBox textBox:
                    textBox.TextChanged += async (s, e) =>
                    {
                        try
                        {
                            var args = Evt("change", ("value", StringValue.Create(textBox.Text ?? "")));
                            await changeFunc.CallAsync(engine, [args]);
                        }
                        catch (Exception ex) { LogEventError("onChange", ex); }
                    };
                    break;
                case CheckBox checkBox:
                    checkBox.IsCheckedChanged += async (s, e) =>
                    {
                        try
                        {
                            var args = Evt("change", ("checked", BoolValue.Create(checkBox.IsChecked ?? false)));
                            await changeFunc.CallAsync(engine, [args]);
                        }
                        catch (Exception ex) { LogEventError("onChange", ex); }
                    };
                    break;
            }
        }

        // onSelect
        if (props.TryGetValue("onSelect", out var onSelect) && onSelect is ICallable selectFunc)
        {
            switch (control)
            {
                case ComboBox comboBox:
                    comboBox.SelectionChanged += async (s, e) =>
                    {
                        try
                        {
                            var selected = comboBox.SelectedItem is string si ? StringValue.Create(si) : Value.Null;
                            var args = Evt("select", ("selected", selected), ("index", NumberValueFactory.Create(comboBox.SelectedIndex)));
                            await selectFunc.CallAsync(engine, [args]);
                        }
                        catch (Exception ex) { LogEventError("onSelect", ex); }
                    };
                    break;
                case ListBox listBox:
                    listBox.SelectionChanged += async (s, e) =>
                    {
                        try
                        {
                            var selected = listBox.SelectedItem is string si ? StringValue.Create(si) : Value.Null;
                            var args = Evt("select", ("selected", selected), ("index", NumberValueFactory.Create(listBox.SelectedIndex)));
                            await selectFunc.CallAsync(engine, [args]);
                        }
                        catch (Exception ex) { LogEventError("onSelect", ex); }
                    };
                    break;
            }
        }
    }

    /// <summary>
    /// 创建事件参数 ObjectValue（模拟 Vue $event）
    /// 用法: Evt("change", ("value", StringValue), ("checked", BoolValue))
    /// </summary>
    private static ObjectValue Evt(string type, params (string key, Value value)[] props)
    {
        var dict = new Dictionary<string, Value> { ["type"] = StringValue.Create(type) };
        foreach (var (key, value) in props)
            dict[key] = value;
        return new ObjectValue(dict);
    }

    private static void LogEventError(string eventName, Exception ex)
    {
        System.Diagnostics.Debug.WriteLine(
            $"[Script Event] 事件 '{eventName}' 处理器异常: {ex.Message}");
    }

    // ========================================================================
    // v-for 列表渲染
    // ========================================================================

    /// <summary>
    /// 构建 vfor 列表渲染。
    /// </summary>
    private Control BuildVfor(ObjectValue descriptor)
    {
        var arrayWrapper = descriptor.Properties["__array"];
        var templateValue = descriptor.Properties["__template"];
        var template = templateValue as ICallable;

        var inpc = InpcFactory.ExtractInpc(arrayWrapper);
        if (inpc == null || template == null)
            return new Panel();

        var engine = _adapter.Engine!;
        EnsureGlobalSlotsForTemplate(engine);

        var placeholder = new StackPanel();
        // 增量更新追踪: index → (control, itemHash)
        var rendered = new Dictionary<int, (Control control, int itemHash)>();

        Control RenderItem(int i, Value item)
        {
            var idx = NumberValueFactory.Create(i);
            try
            {
                var task = template.CallAsync(engine, [item, idx]);
                var result = task.GetAwaiter().GetResult();
                return result is ObjectValue childDesc ? BuildInternal(childDesc)
                    : new TextBlock { Text = "?" };
            }
            catch (Exception ex)
            {
                Log.Warn($"[vfor] template failed index={i}: {ex.Message}");
                return new TextBlock { Text = "⚠" };
            }
        }

        void FullRebuild()
        {
            var arr = inpc.Get();
            if (arr is not ArrayValue av) return;
            Dispatcher.UIThread.Post(() =>
            {
                placeholder.Children.Clear();
                rendered.Clear();
                for (int i = 0; i < av.Elements.Count; i++)
                {
                    var c = RenderItem(i, av.Elements[i]);
                    placeholder.Children.Add(c);
                    rendered[i] = (c, av.Elements[i].GetHashCode());
                }
            });
        }

        void IncrementalUpdate()
        {
            var arr = inpc.Get();
            if (arr is not ArrayValue av) return;
            Dispatcher.UIThread.Post(() =>
            {
                int n = av.Elements.Count;
                for (int i = 0; i < n; i++)
                {
                    var item = av.Elements[i];
                    var hash = item.GetHashCode();
                    if (rendered.TryGetValue(i, out var ex) && ex.itemHash == hash)
                        continue; // 复用
                    var c = RenderItem(i, item);
                    if (i < placeholder.Children.Count)
                    {
                        placeholder.Children[i] = c;
                    }
                    else
                    {
                        placeholder.Children.Add(c);
                    }
                    rendered[i] = (c, hash);
                }
                while (placeholder.Children.Count > n)
                    placeholder.Children.RemoveAt(placeholder.Children.Count - 1);
                foreach (var k in rendered.Keys.Where(k => k >= n).ToArray())
                    rendered.Remove(k);
            });
        }

        FullRebuild();
        inpc.OnChange(_ => IncrementalUpdate());

        return placeholder;
    }

    /// <summary>
    /// 确保 vfor 模板 Lambda 中的 import 变量在 GlobalSlotRegistry 有正确的值。
    /// 模板运行在新 VM 中，需要全局槽位值可用。
    /// 注意：只 SetValue，不 Register — 槽位索引在编译时已固定，重新注册会改变索引。
    /// </summary>
    private static void EnsureGlobalSlotsForTemplate(ScriptLang.ScriptEngine engine)
    {
        var controlsExports = Modules.ControlsModule.CreateExports();
        var avaloniaExports = Modules.AvaloniaModule.CreateExports(null!);

        // 更新 ImportResolver 的模块缓存（确保后续 import 能找到）
        engine.ImportResolver.RegisterBuiltinModule("avalonia", avaloniaExports);
        engine.ImportResolver.RegisterBuiltinModule("avalonia.controls", controlsExports);

        // 仅设置值（不重新注册，保持编译时分配的槽位索引不变）
        void SetIfExists(string name, Value value)
        {
            try
            {
                int slot = ScriptLang.Runtime.ByteCode.GlobalSlotRegistry.GetSlot(name);
                ScriptLang.Runtime.ByteCode.GlobalSlotRegistry.SetValue(slot, value);
            }
            catch (KeyNotFoundException)
            {
                // 编译时未分配此全局变量的槽位，忽略
            }
        }

        foreach (var kv in controlsExports.Properties)
            SetIfExists(kv.Key, kv.Value);
        foreach (var kv in avaloniaExports.Properties)
            SetIfExists(kv.Key, kv.Value);
    }

    // ========================================================================
    // v-if 条件渲染
    // ========================================================================

    /// <summary>
    /// 构建 vif 条件渲染占位符。
    /// 创建一个空的 Panel 作为占位容器，根据 condition 的值动态挂载/卸载子控件。
    /// </summary>
    private Control BuildVif(ObjectValue descriptor)
    {
        var conditionWrapper = descriptor.Properties["__condition"];
        var elementDesc = descriptor.Properties["__element"] as ObjectValue;

        // 创建占位容器（空 Panel，不占用可见空间）
        var placeholder = new Panel();

        // 获取可观察对象
        var inpc = InpcFactory.ExtractInpc(conditionWrapper);
        var computed = InpcFactory.ExtractComputed(conditionWrapper);

        // 获取当前条件值
        bool GetCondition() =>
            inpc?.Get().AsBool()
            ?? computed?.Get().AsBool()
            ?? false;

        // 更新子控件（挂载/卸载）
        void UpdateChild()
        {
            Dispatcher.UIThread.Post(() =>
            {
                placeholder.Children.Clear();
                if (GetCondition() && elementDesc != null)
                {
                    var child = BuildInternal(elementDesc);
                    placeholder.Children.Add(child);
                }
            });
        }

        // 初始状态
        if (GetCondition() && elementDesc != null)
        {
            placeholder.Children.Add(BuildInternal(elementDesc));
        }

        // 订阅变更
        if (inpc != null)
            inpc.OnChange(_ => UpdateChild());
        else if (computed != null)
            computed.OnChange(_ => UpdateChild());

        return placeholder;
    }
}
