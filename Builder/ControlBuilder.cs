using Avalonia.Controls;
using Avalonia.Threading;
using ScriptLang.Runtime;
using AvaloniaScriptLoader.Model;
using AvaloniaScriptLoader.Wrapper;

namespace AvaloniaScriptLoader.Builder;

/// <summary>
/// 控件构建器 — 将 ObjectValue 描述符树递归转换为 Avalonia 控件树
/// 必须在 UI 线程调用
/// </summary>
public class ControlBuilder
{
    private readonly ScriptEngineAdapter _adapter;
    private readonly PropertyBinder _binder = new();

    public ControlBuilder(ScriptEngineAdapter adapter)
    {
        _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
    }

    /// <summary>
    /// 递归构建控件树（必须在 UI 线程调用）
    /// </summary>
    /// <param name="descriptor">根控件描述符</param>
    /// <returns>根 Avalonia 控件</returns>
    public Control Build(ObjectValue descriptor)
    {
        if (!Dispatcher.UIThread.CheckAccess())
            throw new InvalidOperationException("ControlBuilder.Build 必须在 UI 线程调用");

        return BuildInternal(descriptor);
    }

    /// <summary>
    /// 从描述符构建控件树，返回 Window（用于 demo 场景）
    /// </summary>
    public Window? BuildWindow(ObjectValue descriptor)
    {
        var control = Build(descriptor);
        if (control is Window window)
            return window;

        // 如果脚本返回的不是 Window，包装到新 Window 中
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
    // 递归构建
    // ========================================================================

    private Control BuildInternal(ObjectValue descriptor)
    {
        var type = descriptor.Properties[ControlMeta.TypeKey].AsString();
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
}
