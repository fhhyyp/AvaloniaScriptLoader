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

        // === 标准控件 ===
        var control = CreateNativeControl(type);

        // 1. 应用初始属性
        _binder.ApplyInitialProperties(control, descriptor);

        // 2. 注册事件处理器
        RegisterEvents(control, descriptor);

        // 3. 创建并激活 ControlWrapper
        var wrapper = new ControlWrapper(control, descriptor);
        wrapper.Builder = this;
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
                else if (childDesc is ArrayValue nestedArr)
                {
                    // vfor 返回的嵌套数组 — 递归展开
                    foreach (var nestedDesc in nestedArr.Elements)
                    {
                        if (nestedDesc is ObjectValue nestedObj)
                        {
                            var nestedControl = BuildInternal(nestedObj);
                            AddChild(control, nestedControl, nestedObj);
                        }
                    }
                }
            }
        }

        // 7. 递归处理 items（TabControl / ListBox 等集合型控件）
        if (descriptor.Properties.TryGetValue("items", out var itemsValue)
            && itemsValue is ArrayValue items)
        {
            foreach (var itemDesc in items.Elements)
            {
                if (itemDesc is ObjectValue itemObj)
                {
                    var itemControl = BuildInternal(itemObj);
                    AddChild(control, itemControl, itemObj);
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
        ControlMeta.Types.DataGrid    => CreateDataGridReflection(),
        _ => throw new ArgumentException($"未知控件类型: '{type}'"),
    };

    /// <summary>
    /// 反射创建 DataGrid（避免硬依赖 Avalonia.Controls.DataGrid NuGet 包）
    /// </summary>
    private static Control CreateDataGridReflection()
    {
        var dgType = Type.GetType("Avalonia.Controls.DataGrid, Avalonia.Controls.DataGrid");
        if (dgType != null)
            return (Control)Activator.CreateInstance(dgType)!;
        // 回退：如果未安装 DataGrid 包，返回 TextBlock 提示
        return new TextBlock { Text = "[DataGrid: 请安装 Avalonia.Controls.DataGrid 包]" };
    }

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
            case TabItem tabItem:
                tabItem.Content = child;
                break;
            case ContentControl cc:
                cc.Content = child;
                break;
            case Decorator decorator:
                decorator.Child = child;
                break;
            default:
                // 反射回退：Child → Content
                var childProp = parent.GetType().GetProperty("Child");
                if (childProp != null) { childProp.SetValue(parent, child); return; }
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

        // onKeyDown → Control.KeyDown（事件参数: key, modifiers）
        if (props.TryGetValue("onKeyDown", out var onKeyDown) && onKeyDown is ICallable keyFunc)
        {
            control.KeyDown += async (s, e) =>
            {
                try
                {
                    var kargs = Evt("keydown",
                        ("key", StringValue.Create(e.Key.ToString())),
                        ("modifiers", StringValue.Create(e.KeyModifiers.ToString())));
                    await keyFunc.CallAsync(engine, [kargs]);
                }
                catch (Exception ex) { LogEventError("onKeyDown", ex); }
            };
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

        // 支持 InpcValue 和 ComputedValue 作为数组源
        var inpc = InpcFactory.ExtractInpc(arrayWrapper);
        var computedArr = InpcFactory.ExtractComputed(arrayWrapper);

        Func<Value> getArray = inpc != null ? () => inpc.Get()
            : computedArr != null ? () => computedArr.Get()
            : null;

        Action<Action<Value>> subscribe = inpc != null ? cb => inpc.OnChange(cb)
            : computedArr != null ? cb => computedArr.OnChange(cb)
            : null;

        if (getArray == null || subscribe == null || template == null)
            return new Panel();

        var engine = _adapter.Engine!;
        EnsureGlobalSlotsForTemplate(engine);

        var placeholder = new VirtualizingStackPanel();
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
            var arr = getArray();
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
            var arr = getArray();
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
        subscribe(_ => IncrementalUpdate());

        return placeholder;
    }

    /// <summary>
    /// 确保 vfor 模板 Lambda 中的 import 变量在 GlobalSlotRegistry 有正确的值。
    /// 模板运行在新 VM 中，需要全局槽位值可用。
    /// 注意：只 SetValue，不 Register — 槽位索引在编译时已固定，重新注册会改变索引。
    /// </summary>
    /// <summary>
    /// 确保 vfor 模板 Lambda 中的控件工厂在 GlobalSlotRegistry 有值。
    /// 仅设置已存在槽位的值，不注册新槽位（Register 会扩容数组，清空所有已有值）。
    /// </summary>
    private static void EnsureGlobalSlotsForTemplate(ScriptLang.ScriptEngine engine)
    {
        var controlsExports = Modules.ControlsModule.CreateExports();

        foreach (var kv in controlsExports.Properties)
        {
            try
            {
                int slot = ScriptLang.Runtime.ByteCode.GlobalSlotRegistry.GetSlot(kv.Key);
                ScriptLang.Runtime.ByteCode.GlobalSlotRegistry.SetValue(slot, kv.Value);
            }
            catch (KeyNotFoundException)
            {
                // 槽位不存在说明模板 Lambda 不使用此变量（通过闭包捕获），跳过
            }
        }
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
