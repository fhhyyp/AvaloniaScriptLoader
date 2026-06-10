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
        if (control is Window window) return window;

        var wrapperWindow = new Window
        {
            Title = "Avalonia Script",
            Width = 800,
            Height = 600,
            Content = control,
        };
        return wrapperWindow;
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
        ControlMeta.Types.Window     => new Window(),
        ControlMeta.Types.Button     => new Button(),
        ControlMeta.Types.Label      => new TextBlock(),
        ControlMeta.Types.TextBox    => new TextBox(),
        ControlMeta.Types.CheckBox   => new CheckBox(),
        ControlMeta.Types.ComboBox   => new ComboBox(),
        ControlMeta.Types.ListBox    => new ListBox(),
        ControlMeta.Types.StackPanel => new StackPanel(),
        ControlMeta.Types.Grid       => new Grid(),
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
        var props = descriptor.Properties;

        // onClick → Button.Click / Control.Tapped
        // 事件参数: { type: "click", name: "controlName" }
        if (props.TryGetValue("onClick", out var onClick)
            && onClick is ICallable clickFunc)
        {
            var clickArgs = Evt("click",
                ("name", StringValue.Create(control.Name ?? "")));

            switch (control)
            {
                case Button button:
                    button.Click += async (s, e) =>
                    {
                        try { await clickFunc.CallAsync(_adapter.Engine!, [clickArgs]); }
                        catch (Exception ex) { LogEventError("onClick", ex); }
                    };
                    break;
                default:
                    control.Tapped += async (s, e) =>
                    {
                        try { await clickFunc.CallAsync(_adapter.Engine!, [clickArgs]); }
                        catch (Exception ex) { LogEventError("onClick", ex); }
                    };
                    break;
            }
        }

        // onChange → TextBox.TextChanged / CheckBox.IsCheckedChanged
        // TextBox 参数: { type: "change", value: "当前文本" }
        // CheckBox 参数: { type: "change", checked: true/false }
        if (props.TryGetValue("onChange", out var onChange)
            && onChange is ICallable changeFunc)
        {
            switch (control)
            {
                case TextBox textBox:
                    textBox.TextChanged += async (s, e) =>
                    {
                        try
                        {
                            var args = Evt("change",
                                ("value", StringValue.Create(textBox.Text ?? "")));
                            await changeFunc.CallAsync(_adapter.Engine!, [args]);
                        }
                        catch (Exception ex) { LogEventError("onChange", ex); }
                    };
                    break;
                case CheckBox checkBox:
                    checkBox.IsCheckedChanged += async (s, e) =>
                    {
                        try
                        {
                            var args = Evt("change",
                                ("checked", BoolValue.Create(checkBox.IsChecked ?? false)));
                            await changeFunc.CallAsync(_adapter.Engine!, [args]);
                        }
                        catch (Exception ex) { LogEventError("onChange", ex); }
                    };
                    break;
                default:
                    break;
            }
        }

        // onSelect → ComboBox.SelectionChanged / ListBox.SelectionChanged
        // 事件参数: { type: "select", selected: 选中项, index: 索引 }
        if (props.TryGetValue("onSelect", out var onSelect)
            && onSelect is ICallable selectFunc)
        {
            switch (control)
            {
                case ComboBox comboBox:
                    comboBox.SelectionChanged += async (s, e) =>
                    {
                        try
                        {
                            var selected = comboBox.SelectedItem is string si
                                ? StringValue.Create(si) : Value.Null;
                            var index = NumberValueFactory.Create(comboBox.SelectedIndex);
                            var args = Evt("select",
                                ("selected", selected),
                                ("index", index));
                            await selectFunc.CallAsync(_adapter.Engine!, [args]);
                        }
                        catch (Exception ex) { LogEventError("onSelect", ex); }
                    };
                    break;
                case ListBox listBox:
                    listBox.SelectionChanged += async (s, e) =>
                    {
                        try
                        {
                            var selected = listBox.SelectedItem is string si
                                ? StringValue.Create(si) : Value.Null;
                            var index = listBox.SelectedIndex;
                            var args = Evt("select",
                                ("selected", selected),
                                ("index", NumberValueFactory.Create(index)));
                            await selectFunc.CallAsync(_adapter.Engine!, [args]);
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
    /// 根据数组的每个元素调用模板函数生成子控件，数组变更时全量重建。
    /// </summary>
    private Control BuildVfor(ObjectValue descriptor)
    {
        var arrayWrapper = descriptor.Properties["__array"];
        var templateValue = descriptor.Properties["__template"];
        var template = templateValue as ICallable;

        // 获取数组 InpcValue
        var inpc = InpcFactory.ExtractInpc(arrayWrapper);
        if (inpc == null || template == null)
            return new Panel(); // 无效配置，返回空占位

        var placeholder = new StackPanel(); // 用 StackPanel 容纳列表项

        // 重建所有子控件
        void RebuildChildren()
        {
            var arr = inpc.Get();
            if (arr is not ArrayValue av) return;

            Dispatcher.UIThread.Post(() =>
            {
                placeholder.Children.Clear();
                var engine = _adapter.Engine!;

                for (int i = 0; i < av.Elements.Count; i++)
                {
                    var item = av.Elements[i];
                    var index = NumberValueFactory.Create(i);

                    try
                    {
                        // 调用模板函数 (item, index) => element
                        var task = template.CallAsync(engine, [item, index]);
                        var result = task.GetAwaiter().GetResult();

                        if (result is ObjectValue childDesc)
                        {
                            var child = BuildInternal(childDesc);
                            placeholder.Children.Add(child);
                        }
                    }
                    catch (Exception ex)
                    {
                        System.Diagnostics.Debug.WriteLine(
                            $"[vfor] 模板调用失败 index={i}: {ex.Message}");
                    }
                }
            });
        }

        // 初始构建
        RebuildChildren();

        // 订阅数组变更 → 全量重建
        inpc.OnChange(_ => RebuildChildren());

        return placeholder;
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
