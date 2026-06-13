using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ScriptLang.Runtime;
using AvaloniaScriptLoader.Controls;
using AvaloniaScriptLoader.Factory;
using AvaloniaScriptLoader.Model;

namespace AvaloniaScriptLoader.Builder;

/// <summary>
/// 属性绑定器 — 将描述符中的属性值映射到 Avalonia 控件属性
/// 支持 InpcValue/ComputedValue 自动订阅 + 双向绑定
/// </summary>
public class PropertyBinder
{
    /// <summary>
    /// 应用初始属性到控件（Build 阶段调用）
    /// </summary>
    public void ApplyInitialProperties(Control control, ObjectValue descriptor)
    {
        var props = descriptor.Properties;

        foreach (var kv in props)
        {
            var name = kv.Key;
            var value = kv.Value;

            // 跳过元数据、事件、setter 方法、子控件容器
            if (name.StartsWith("__")) continue;
            if (PropertyNames.IsEventProperty(name)) continue;
            if (PropertyNames.IsSetterMethod(name)) continue;
            if (name is "children" or "content" or "contextMenu") continue;
            if (name is "header" && value is ObjectValue) continue; // ObjectValue header → BuildInternal 处理

            // 检测可观察包装（inpc / computed）
            if (InpcFactory.IsObservableWrapper(value))
            {
                BindObservableValue(control, name, value);
            }
            else
            {
                SetControlProperty(control, name, value);
            }
        }
    }

    /// <summary>
    /// 对可观察包装对象（InpcValue / ComputedValue）建立自动绑定：
    /// 1. 读取当前值 → 设置控件初始属性
    /// 2. 订阅变更 → Dispatcher 调度更新 UI
    /// 3. 若为双向绑定 (inpc_twoway) → 自动注册控件事件回写
    /// </summary>
    private void BindObservableValue(Control control, string propertyName, Value wrapper)
    {
        var inpc = InpcFactory.ExtractInpc(wrapper);
        var computed = InpcFactory.ExtractComputed(wrapper);
        var table = InpcFactory.ExtractTable(wrapper);

        // 获取初始值 + 订阅变更的统一处理
        Action<Value> updateAction = newValue =>
        {
            void Apply()
            {
                SetControlProperty(control, propertyName, newValue);
            }
            if (Dispatcher.UIThread.CheckAccess()) Apply();
            else Dispatcher.UIThread.Post(Apply);
        };

        if (inpc != null)
        {
            // InpcValue: 初始值 + 订阅
            SetControlProperty(control, propertyName, inpc.Get());
            inpc.OnChange(updateAction);

            // 双向绑定：自动注册控件→model 回写事件
            if (InpcFactory.IsTwoWay(wrapper))
            {
                RegisterTwoWayWriteback(control, propertyName, inpc);
            }
        }
        else if (computed != null)
        {
            // ComputedValue: 初始值 + 订阅（只读）
            SetControlProperty(control, propertyName, computed.Get());
            computed.OnChange(updateAction);
        }
        else if (table != null)
        {
            // TableValue: 初始值通过 ObjectValue 包装传递给控件（DataTable.SetTableValue 内部自行订阅类型化事件）
            // 不订阅 OnChange —— Notify 回调传的是 ArrayValue，ApplyDataTableProperty 的 "items" 分支
            // 只识别含 __table 的 ObjectValue，对 ArrayValue 无效，属空操作回调。
            SetControlProperty(control, propertyName, table.Table);
        }
    }

    /// <summary>
    /// 双向绑定回写：注册控件事件 → 将 view 值写回 InpcValue
    ///
    /// 属性→事件映射:
    ///   "text"    → TextBox.TextChanged  / CheckBox.IsCheckedChanged 不适用
    ///   "checked" → CheckBox.IsCheckedChanged
    ///   "selected"→ ComboBox/ListBox.SelectionChanged
    /// </summary>
    private static void RegisterTwoWayWriteback(Control control, string propertyName, InpcValue inpc)
    {
        switch (propertyName)
        {
            case "text":
                if (control is TextBox tb)
                {
                    tb.TextChanged += (s, e) =>
                    {
                        inpc.Set(StringValue.Create(tb.Text ?? ""));
                    };
                }
                break;

            case "checked":
                if (control is CheckBox cb)
                {
                    cb.IsCheckedChanged += (s, e) =>
                    {
                        inpc.Set(BoolValue.Create(cb.IsChecked ?? false));
                    };
                }
                break;

            case "value":
                if (control is Slider slider)
                {
                    slider.ValueChanged += (s, e) =>
                    {
                        inpc.Set(NumberValueFactory.Create(slider.Value));
                    };
                }
                break;

            case "selectedDate":
                if (control is DatePicker dp)
                {
                    var updating = false;
                    dp.SelectedDateChanged += (s, e) =>
                    {
                        if (updating) return;
                        if (e.NewDate.HasValue)
                        {
                            updating = true;
                            try
                            {
                                inpc.Set(new DateTimeValue(e.NewDate.Value.LocalDateTime));
                            }
                            finally
                            {
                                updating = false;
                            }
                        }
                    };
                }
                break;

            case "selectedTime":
                if (control is TimePicker tp)
                {
                    var updating = false;
                    tp.SelectedTimeChanged += (s, e) =>
                    {
                        if (updating) return;
                        if (e.NewTime.HasValue)
                        {
                            updating = true;
                            try
                            {
                                inpc.Set(new DateTimeValue(System.DateTime.Today.Add(e.NewTime.Value)));
                            }
                            finally
                            {
                                updating = false;
                            }
                        }
                    };
                }
                break;

            case "selected":
                if (control is ComboBox combo)
                {
                    combo.SelectionChanged += (s, e) =>
                    {
                        inpc.Set(NumberValueFactory.Create(combo.SelectedIndex));
                    };
                }
                else if (control is ListBox list)
                {
                    list.SelectionChanged += (s, e) =>
                    {
                        inpc.Set(NumberValueFactory.Create(list.SelectedIndex));
                    };
                }
                break;
        }
    }

    // 保留旧方法名兼容（ControlBuilder 引用）
    private void BindInpcValue(Control control, string propertyName, Value wrapper)
        => BindObservableValue(control, propertyName, wrapper);

    /// <summary>
    /// 设置单个控件属性
    /// </summary>
    public void SetControlProperty(Control control, string propertyName, Value value)
    {
        try
        {
            ApplyProperty(control, propertyName, value);
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[PropertyBinder] 设置属性 '{propertyName}' 失败: {ex.Message}");
        }
    }

    // ========================================================================
    // 属性映射核心
    // ========================================================================

    /// <summary>属性缩写映射（仅复合词或过长单词，同时保留原名）</summary>
    private static readonly Dictionary<string, string> _shorthands = new()
    {
        ["bg"]     = "background",
        ["size"]   = "fontSize",
        ["halign"] = "horizontalAlignment",
        ["valign"] = "verticalAlignment",
        ["radius"] = "cornerRadius",
    };

    /// <summary>归一化属性名（缩写 → 全名）</summary>
    private static string NormalizeProp(string name)
        => _shorthands.TryGetValue(name, out var full) ? full : name;

    private void ApplyProperty(Control control, string name, Value value)
    {
        name = NormalizeProp(name);
        switch (name)
        {
            // === 通用布局属性 ===
            case "width":
                control.Width = ToDouble(value);
                break;
            case "height":
                control.Height = ToDouble(value);
                break;
            case "minWidth":
                control.MinWidth = ToDouble(value);
                break;
            case "minHeight":
                control.MinHeight = ToDouble(value);
                break;
            case "maxWidth":
                control.MaxWidth = ToDouble(value);
                break;
            case "maxHeight":
                control.MaxHeight = ToDouble(value);
                break;
            case "tooltip":
                ToolTip.SetTip(control, value.AsString());
                break;
            case "horizontalAlignment":
                control.HorizontalAlignment = ToHorizontalAlign(value);
                break;
            case "verticalAlignment":
                control.VerticalAlignment = ToVerticalAlign(value);
                break;
            case "margin":
                control.Margin = ToThickness(value);
                break;
            case "padding":
                if (control is Decorator decorator)
                    decorator.Padding = ToThickness(value);
                break;
            case "visible":
                control.IsVisible = value.AsBool();
                break;
            case "enabled":
                control.IsEnabled = value.AsBool();
                break;

            // === windowDrag: 标记控件为窗口拖拽句柄（自定义标题栏） ===
            case "windowDrag":
                if (value.AsBool())
                {
                    control.AddHandler(InputElement.PointerPressedEvent, (s, e) =>
                    {
                        var e2 = e as PointerPressedEventArgs;
                        if (e2?.GetCurrentPoint(control).Properties.IsLeftButtonPressed != true)
                            return;

                        // 如果点击的是子控件（Button/MenuItem 等），跳过拖拽
                        var src = e2.Source as Control;
                        while (src != null && src != control)
                        {
                            if (src is Button or MenuItem)
                                return; // 点到了交互控件，不拖拽
                            src = src.Parent as Control;
                        }

                        var w = TopLevel.GetTopLevel(control) as Window;
                        w?.BeginMoveDrag(e2);
                    });
                }
                break;

            // === CheckBox 特有（顶层处理，避免路由丢失） ===
            case "checked":
                if (control is CheckBox cb)
                    cb.IsChecked = value.AsBool();
                else if (control is ToggleSwitch ts)
                    ts.IsChecked = value.AsBool();
                break;

            // === 通用标识 ===
            case "name":
                control.Name = value.AsString();
                break;

            // === 控件特化属性 ===
            default:
                ApplyControlSpecific(control, name, value);
                break;
        }
    }

    private void ApplyControlSpecific(Control control, string name, Value value)
    {
        name = NormalizeProp(name);
        // 使用 if/else 链而非 switch 模式匹配
        if (control is Window window)
            ApplyWindowProperty(window, name, value);
        else if (control is TextBox textBox)
            ApplyTextBoxProperty(textBox, name, value);
        else if (control is Button button)
            ApplyButtonProperty(button, name, value);
        else if (control is CheckBox checkBox)
            ApplyCheckBoxProperty(checkBox, name, value);
        else if (control is ComboBox comboBox)
            ApplyComboBoxProperty(comboBox, name, value);
        else if (control is ListBox listBox)
            ApplyListBoxProperty(listBox, name, value);
        else if (control is MenuItem menuItem)
            ApplyMenuItemProperty(menuItem, name, value);
        else if (control is StackPanel stackPanel)
            ApplyStackPanelProperty(stackPanel, name, value);
        else if (control is DataTable table)
            ApplyDataTableProperty(table, name, value);
        else if (control is Grid grid)
            ApplyGridProperty(grid, name, value);
        else if (control is Image image)
            ApplyImageProperty(image, name, value);
        else if (control is ScrollViewer sv)
            ApplyScrollViewerProperty(sv, name, value);
        else if (control is Border border)
            ApplyBorderProperty(border, name, value);
        else if (control is TabControl tabControl)
            ApplyTabControlProperty(tabControl, name, value);
        else if (control is TabItem tabItem)
            ApplyTabItemProperty(tabItem, name, value);
        else if (control.GetType().Name == "DataGrid")
            ApplyDataGridByReflection(control, name, value);
        else if (control is DatePicker dp)
            ApplyDatePickerProperty(dp, name, value);
        else if (control is TimePicker tp)
            ApplyTimePickerProperty(tp, name, value);
        else if (control is Slider slider)
            ApplySliderProperty(slider, name, value);
        else if (control is ProgressBar pb)
            ApplyProgressBarProperty(pb, name, value);
        else if (control is Expander expander)
            ApplyExpanderProperty(expander, name, value);
        else if (control is TextBlock textBlock)
            ApplyLabelProperty(textBlock, name, value);
        else
            ApplyByReflection(control, name, value);
    }

    // ========================================================================
    // DatePicker
    // ========================================================================
    private static void ApplyDatePickerProperty(DatePicker dp, string name, Value value)
    {
        switch (name)
        {
            case "selectedDate":
                if (value is DateTimeValue dtv)
                    dp.SelectedDate = new DateTimeOffset(dtv.Value.ToLocalTime());
                else if (value is StringValue sv && DateTime.TryParse(sv.Value, out var dt))
                    dp.SelectedDate = new DateTimeOffset(dt);
                else if (DateTime.TryParse(value.AsString(), out var dt2))
                    dp.SelectedDate = new DateTimeOffset(dt2);
                break;
            case "minYear":
                if (value.IsNumber_Int) dp.MinYear = new DateTimeOffset(value.As<int>(), 1, 1, 0, 0, 0, System.TimeSpan.Zero);
                break;
            case "maxYear":
                if (value.IsNumber_Int) dp.MaxYear = new DateTimeOffset(value.As<int>(), 12, 31, 0, 0, 0, System.TimeSpan.Zero);
                break;
        }
    }

    // ========================================================================
    // TimePicker
    // ========================================================================
    private static void ApplyTimePickerProperty(TimePicker tp, string name, Value value)
    {
        switch (name)
        {
            case "selectedTime":
                if (value is DateTimeValue dtv)
                    tp.SelectedTime = dtv.Value.TimeOfDay;
                else if (value is StringValue sv && System.TimeSpan.TryParse(sv.Value, out var ts))
                    tp.SelectedTime = ts;
                else if (System.TimeSpan.TryParse(value.AsString(), out var ts2))
                    tp.SelectedTime = ts2;
                break;
            case "minuteIncrement":
                if (value.IsNumber_Int) tp.MinuteIncrement = value.As<int>();
                break;
            case "clockIdentifier":
                tp.ClockIdentifier = value.AsString();
                break;
        }
    }

    // ========================================================================
    // Slider
    // ========================================================================
    private static void ApplySliderProperty(Slider s, string name, Value value)
    {
        switch (name)
        {
            case "value": s.Value = ToDouble(value); break;
            case "minimum": s.Minimum = ToDouble(value); break;
            case "maximum": s.Maximum = ToDouble(value); break;
            case "tickFrequency": s.TickFrequency = ToDouble(value); break;
        }
    }

    // ========================================================================
    // Expander
    // ========================================================================
    private static void ApplyExpanderProperty(Expander expander, string name, Value value)
    {
        switch (name)
        {
            case "header":
                if (value is not ObjectValue)
                    expander.Header = value.AsString();
                // ObjectValue header → handled by BuildInternal
                break;
            case "isExpanded":
                expander.IsExpanded = value.AsBool();
                break;
            case "expandDirection":
                expander.ExpandDirection = value.AsString()?.ToLowerInvariant() switch
                {
                    "up" => Avalonia.Controls.ExpandDirection.Up,
                    "left" => Avalonia.Controls.ExpandDirection.Left,
                    "right" => Avalonia.Controls.ExpandDirection.Right,
                    _ => Avalonia.Controls.ExpandDirection.Down,
                };
                break;
        }
    }

    // ========================================================================
    // ProgressBar
    // ========================================================================
    private static void ApplyProgressBarProperty(ProgressBar pb, string name, Value value)
    {
        switch (name)
        {
            case "value": pb.Value = ToDouble(value); break;
            case "minimum": pb.Minimum = ToDouble(value); break;
            case "maximum": pb.Maximum = ToDouble(value); break;
            case "isIndeterminate": pb.IsIndeterminate = value.AsBool(); break;
        }
    }

    // ========================================================================
    // Image
    // ========================================================================
    private static void ApplyImageProperty(Image img, string name, Value value)
    {
        switch (name)
        {
            case "source":
                var src = value.AsString();
                if (!string.IsNullOrEmpty(src))
                {
                    try { img.Source = new Avalonia.Media.Imaging.Bitmap(src); }
                    catch (Exception ex) {
#if DEBUG
                        System.Diagnostics.Debug.WriteLine(ex);
#endif
                    }
                }
                break;
            case "stretch":
                img.Stretch = value.AsString()?.ToLowerInvariant() switch
                {
                    "fill" => Avalonia.Media.Stretch.Fill,
                    "uniformtofill" or "uniform_to_fill" => Avalonia.Media.Stretch.UniformToFill,
                    "none" => Avalonia.Media.Stretch.None,
                    _ => Avalonia.Media.Stretch.Uniform,
                };
                break;
        }
    }

    // ========================================================================
    // ScrollViewer
    // ========================================================================
    private static void ApplyScrollViewerProperty(ScrollViewer sv, string name, Value value)
    {
        switch (name)
        {
            case "horizontalScrollBarVisibility":
                sv.HorizontalScrollBarVisibility = ParseScrollBarVisibility(value);
                break;
            case "verticalScrollBarVisibility":
                sv.VerticalScrollBarVisibility = ParseScrollBarVisibility(value);
                break;
        }
    }

    private static Avalonia.Controls.Primitives.ScrollBarVisibility ParseScrollBarVisibility(Value v)
    {
        return v.AsString()?.ToLowerInvariant() switch
        {
            "disabled" or "hidden" => Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            "auto" => Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            "visible" => Avalonia.Controls.Primitives.ScrollBarVisibility.Visible,
            _ => Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
        };
    }

    // ========================================================================
    // Border
    // ========================================================================
    private static void ApplyBorderProperty(Border border, string name, Value value)
    {
        switch (name)
        {
            case "background":
                border.Background = ToBrush(value);
                break;
            case "borderBrush":
                border.BorderBrush = ToBrush(value);
                break;
            case "borderThickness":
                border.BorderThickness = ToThickness(value);
                break;
            case "cornerRadius":
                border.CornerRadius = new Avalonia.CornerRadius(ToDouble(value));
                break;
        }
    }

    // ========================================================================
    // TabControl
    // ========================================================================
    private static void ApplyTabControlProperty(TabControl tc, string name, Value value)
    {
        switch (name)
        {
            case "selectedIndex":
                if (value.IsNumber_Int) tc.SelectedIndex = value.As<int>();
                break;
        }
    }

    // ========================================================================
    // TabItem
    // ========================================================================
    private static void ApplyTabItemProperty(TabItem ti, string name, Value value)
    {
        switch (name)
        {
            case "header":
                ti.Header = value.AsString();
                break;
        }
    }

    // ========================================================================
    // ========================================================================
    // DataTable
    // ========================================================================
    private static void ApplyDataTableProperty(DataTable table, string name, Value value)
    {
        switch (name)
        {
            case "columns":  if (value is ArrayValue cols) table.SetColumns(cols); break;
            case "items":
                if (value is ClrObjectValue clr && clr.Value is TableValue tv)
                    table.SetTableValue(tv);
                break;
            case "isReadOnly":      table.SetReadOnly(value.AsBool()); break;
            case "selectionMode":   table.SetSelectionMode(value.AsString()); break;
            case "selectionOffset": table.SetSelectionOffset((int)ToDouble(value)); break;
            case "selectionBinding": table.SetSelectionBinding(value.AsString()); break;
            case "maxCount": table.SetMaxCount((int)ToDouble(value)); break;
            case "currentPage": table.SetCurrentPage((int)ToDouble(value)); break;
            case "headerStyle": case "rowStyle": case "cellStyle": case "selectedStyle":
                table.SetStyle(name, value); break;
        }
    }

    // DataGrid（反射实现，无 NuGet 包依赖）
    // ========================================================================
    private static void ApplyDataGridByReflection(Control control, string name, Value value)
    {
        var type = control.GetType();
        switch (name)
        {
            case "items":
                if (value is ArrayValue av)
                {
                    var itemsProp = type.GetProperty("ItemsSource");
                    if (itemsProp != null)
                    {
                        var items = av.Elements
                            .Select(v => (object)(v is ObjectValue o ? o.Properties : v.ToString()!))
                            .ToList();
                        itemsProp.SetValue(control, items);
                    }
                }
                break;
            case "columns":
                if (value is ArrayValue cols)
                {
                    var columnsProp = type.GetProperty("Columns");
                    if (columnsProp?.GetValue(control) is System.Collections.IList columnList)
                    {
                        var dgtcType = Type.GetType("Avalonia.Controls.DataGridTextColumn, Avalonia.Controls.DataGrid");
                        foreach (var colVal in cols.Elements)
                        {
                            if (colVal is ObjectValue colObj && dgtcType != null)
                            {
                                var header = colObj.Properties.TryGetValue("header", out var h)
                                    ? h.AsString() : "";
                                var binding = colObj.Properties.TryGetValue("binding", out var b)
                                    ? b.AsString() : header;
                                var col = Activator.CreateInstance(dgtcType)!;
                                dgtcType.GetProperty("Header")?.SetValue(col, header);
                                var bType = Type.GetType("Avalonia.Data.Binding, Avalonia.Base");
                                if (bType != null)
                                {
                                    var bInst = Activator.CreateInstance(bType, binding);
                                    dgtcType.GetProperty("Binding")?.SetValue(col, bInst);
                                }
                                columnList.Add(col);
                            }
                        }
                    }
                }
                break;
        }
    }

    // ========================================================================
    // Window
    // ========================================================================
    private static void ApplyWindowProperty(Window window, string name, Value value)
    {
        switch (name)
        {
            case "title":
                window.Title = value.AsString();
                break;
            case "width":
                window.Width = ToDouble(value);
                break;
            case "height":
                window.Height = ToDouble(value);
                break;
            case "systemDecorations":
                var sdProp = typeof(Window).GetProperty("SystemDecorations");
                if (sdProp != null)
                {
                    var sdNone = Enum.Parse(sdProp.PropertyType, "None");
                    var sdBorderOnly = Enum.Parse(sdProp.PropertyType, "BorderOnly");
                    var sdFull = Enum.Parse(sdProp.PropertyType, "Full");
                    var sdVal = value.AsString() switch
                    {
                        "none" => sdNone,
                        "borderOnly" => sdBorderOnly,
                        _ => sdFull,
                    };
                    sdProp.SetValue(window, sdVal);
                }
                break;
            case "canResize":
                window.CanResize = value.AsBool();
                break;
            case "extendClientArea":
                if (value.AsBool())
                {
                    // ExtendClientAreaToDecorationsHint
                    var hintProp = typeof(Window).GetProperty("ExtendClientAreaToDecorationsHint");
                    hintProp?.SetValue(window, true);
                }
                break;
        }
    }

    // ========================================================================
    // Button
    // ========================================================================
    private static void ApplyButtonProperty(Button button, string name, Value value)
    {
        switch (name)
        {
            case "text":
                button.Content = value.AsString();
                break;
            case "background":
                button.Background = ToBrush(value);
                break;
            case "color":
                button.Foreground = ToBrush(value);
                break;
        }
    }

    // ========================================================================
    // Label (TextBlock)
    // ========================================================================
    private static void ApplyLabelProperty(TextBlock label, string name, Value value)
    {
        switch (name)
        {
            case "text":
                label.Text = value.AsString();
                break;
            case "fontSize":
                label.FontSize = ToDouble(value);
                break;
            case "color":
                label.Foreground = ToBrush(value);
                break;
            case "fontWeight":
                label.FontWeight = ToFontWeight(value);
                break;
            case "align":
                label.TextAlignment = ToTextAlignment(value);
                break;
        }
    }

    // ========================================================================
    // TextBox
    // ========================================================================
    private static void ApplyTextBoxProperty(TextBox textBox, string name, Value value)
    {
        switch (name)
        {
            case "text":
                textBox.Text = value.AsString();
                break;
            case "placeholder":
                textBox.PlaceholderText = value.AsString();
                break;
            case "password":
                if (value.AsBool()) textBox.PasswordChar = '*';
                break;
            case "readonly":
                textBox.IsReadOnly = value.AsBool();
                break;
            case "multiline":
                textBox.AcceptsReturn = value.AsBool();
                break;
        }
    }

    // ========================================================================
    // CheckBox
    // ========================================================================
    private static void ApplyCheckBoxProperty(CheckBox checkBox, string name, Value value)
    {
        switch (name)
        {
            case "text":
                checkBox.Content = value.AsString();
                break;
            case "checked":
                checkBox.IsChecked = value.AsBool();
                break;
        }
    }

    // ========================================================================
    // ComboBox
    // ========================================================================
    private static void ApplyComboBoxProperty(ComboBox comboBox, string name, Value value)
    {
        switch (name)
        {
            case "items":
                if (value is ArrayValue arr)
                {
                    var items = arr.Elements
                        .Select(v => (object)(v.AsString()))
                        .ToList();
                    comboBox.ItemsSource = items;
                }
                break;
            case "selected":
                if (value.IsNumber_Int)
                    comboBox.SelectedIndex = value.As<int>();
                break;
        }
    }

    // ========================================================================
    // ListBox
    // ========================================================================
    private static void ApplyListBoxProperty(ListBox listBox, string name, Value value)
    {
        switch (name)
        {
            case "items":
                if (value is ArrayValue arr)
                {
                    // 暂存原始数据，供 template 使用
                    listBox.Tag = arr;
                    // 默认直接显示字符串
                    var items = arr.Elements
                        .Select(v => (object)(v is ObjectValue obj
                            ? obj.Properties.GetValueOrDefault("text", v)?.AsString() ?? v.ToString()
                            : v.ToString()))
                        .ToList();
                    listBox.ItemsSource = items;
                }
                break;
        }
    }

    // ========================================================================
    // MenuItem
    // ========================================================================
    private static void ApplyMenuItemProperty(MenuItem menuItem, string name, Value value)
    {
        switch (name)
        {
            case "header":
                menuItem.Header = value.AsString();
                break;
            case "icon":
                menuItem.Icon = new TextBlock { Text = value.AsString(), FontSize = 14 };
                break;
            case "isChecked":
                menuItem.IsChecked = value.AsBool();
                break;
            case "foreground":
                menuItem.Foreground = ToBrush(value);
                break;
            case "background":
                menuItem.Background = ToBrush(value);
                break;
        }
    }

    // ========================================================================
    // StackPanel
    // ========================================================================
    private static void ApplyStackPanelProperty(StackPanel panel, string name, Value value)
    {
        switch (name)
        {
            case "orientation":
                panel.Orientation = value.AsString() == "horizontal"
                    ? Orientation.Horizontal
                    : Orientation.Vertical;
                break;
            case "spacing":
                panel.Spacing = ToDouble(value);
                break;
        }
    }

    // ========================================================================
    // Grid
    // ========================================================================
    private static void ApplyGridProperty(Grid grid, string name, Value value)
    {
        switch (name)
        {
            case "rows":
                if (value is ArrayValue rows)
                {
                    foreach (var row in rows.Elements)
                        grid.RowDefinitions.Add(new RowDefinition(ToGridLength(row)));
                }
                break;
            case "cols":
                if (value is ArrayValue cols)
                {
                    foreach (var col in cols.Elements)
                        grid.ColumnDefinitions.Add(new ColumnDefinition(ToGridLength(col)));
                }
                break;
        }
    }

    // ========================================================================
    // 反射兜底
    // ========================================================================
    private static void ApplyByReflection(Control control, string name, Value value)
    {
        var type = control.GetType();
        var prop = type.GetProperty(name,
            System.Reflection.BindingFlags.Public |
            System.Reflection.BindingFlags.Instance |
            System.Reflection.BindingFlags.IgnoreCase);

        if (prop != null && prop.CanWrite)
        {
            var converted = ConvertValue(value, prop.PropertyType);
            prop.SetValue(control, converted);
        }
    }

    // ========================================================================
    // 类型转换工具
    // ========================================================================

    public static double ToDouble(Value value)
    {
        if (value.IsNumber_Double) return value.As<double>();
        if (value.IsNumber_Int) return value.As<int>();
        if (value.IsNumber_Long) return value.As<long>();
        if (value.IsNumber_Float) return value.As<float>();
        if (value.IsNumber_Decimal) return (double)value.As<decimal>();
        if (value.IsString && double.TryParse(value.AsString(), out var d))
            return d;
        return 0;
    }

    public static Thickness ToThickness(Value value)
    {
        // 单值 → 四边相同
        if (value.IsNumber_Double) return new Thickness(value.As<double>());
        if (value.IsNumber_Int) return new Thickness(value.As<int>());

        // 字符串简写 (CSS 顺序: top right bottom left)
        // "2" → 四边 2; "2,4" → 上下2 左右4; "2,4,6,8" → 上2 右4 下6 左8
        if (value.IsString)
        {
            var s = value.AsString();
            if (string.IsNullOrWhiteSpace(s)) return new Thickness(0);
            var parts = s.Replace(',', ' ').Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var v = parts.Select(p => double.TryParse(p, out var d) ? d : 0).ToArray();
            return v.Length switch
            {
                1 => new Thickness(v[0]),
                2 => new Thickness(v[1], v[0], v[1], v[0]),       // CSS: v/h → left,top,right,bottom
                4 => new Thickness(v[3], v[0], v[1], v[2]),       // CSS: t,r,b,l → left,top,right,bottom
                _ => new Thickness(0)
            };
        }

        // 数组简写: [2] / [2,4] / [2,4,6,8]（CSS 顺序）
        if (value.IsArray && value is ArrayValue arr)
        {
            var v = arr.Elements.Select(e => e.IsNumber ? ToDouble(e) : 0).ToArray();
            return v.Length switch
            {
                1 => new Thickness(v[0]),
                2 => new Thickness(v[1], v[0], v[1], v[0]),
                4 => new Thickness(v[3], v[0], v[1], v[2]),
                _ => new Thickness(0)
            };
        }

        // 对象格式: { top=10, left=5, right=5, bottom=10 }
        if (value is ObjectValue obj)
        {
            var p = obj.Properties;
            return new Thickness(
                p.TryGetValue("left", out var l) ? ToDouble(l) : 0,
                p.TryGetValue("top", out var t) ? ToDouble(t) : 0,
                p.TryGetValue("right", out var r) ? ToDouble(r) : 0,
                p.TryGetValue("bottom", out var b) ? ToDouble(b) : 0
            );
        }

        return new Thickness(0);
    }

    public static Avalonia.Layout.HorizontalAlignment ToHorizontalAlign(Value value)
    {
        return value.AsString()?.ToLowerInvariant() switch
        {
            "center" => Avalonia.Layout.HorizontalAlignment.Center,
            "right" => Avalonia.Layout.HorizontalAlignment.Right,
            "stretch" => Avalonia.Layout.HorizontalAlignment.Stretch,
            _ => Avalonia.Layout.HorizontalAlignment.Left,
        };
    }

    public static Avalonia.Layout.VerticalAlignment ToVerticalAlign(Value value)
    {
        return value.AsString()?.ToLowerInvariant() switch
        {
            "center" => Avalonia.Layout.VerticalAlignment.Center,
            "bottom" => Avalonia.Layout.VerticalAlignment.Bottom,
            "stretch" => Avalonia.Layout.VerticalAlignment.Stretch,
            _ => Avalonia.Layout.VerticalAlignment.Top,
        };
    }

    public static Avalonia.Media.FontWeight ToFontWeight(Value value)
    {
        var s = value.AsString()?.ToLowerInvariant();
        return s switch
        {
            "bold" => Avalonia.Media.FontWeight.Bold,
            "normal" => Avalonia.Media.FontWeight.Normal,
            "light" => Avalonia.Media.FontWeight.Light,
            "medium" => Avalonia.Media.FontWeight.Medium,
            "heavy" => Avalonia.Media.FontWeight.Heavy,
            _ => Avalonia.Media.FontWeight.Normal,
        };
    }

    public static TextAlignment ToTextAlignment(Value value)
    {
        var s = value.AsString()?.ToLowerInvariant();
        return s switch
        {
            "center" => TextAlignment.Center,
            "right" => TextAlignment.Right,
            "left" => TextAlignment.Left,
            _ => TextAlignment.Left,
        };
    }

    public static GridLength ToGridLength(Value value)
    {
        if (value.IsNumber_Int) return new GridLength(value.As<int>());
        if (value.IsNumber_Double) return new GridLength(value.As<double>());

        var s = value.AsString()?.ToLowerInvariant() ?? "";
        if (s == "auto") return GridLength.Auto;
        if (s == "*") return new GridLength(1, GridUnitType.Star);
        if (s.EndsWith("*") && double.TryParse(s[..^1], out var star))
            return new GridLength(star, GridUnitType.Star);
        if (double.TryParse(s, out var px))
            return new GridLength(px);

        return GridLength.Auto;
    }

    public static Avalonia.Media.IBrush? ToBrush(Value value)
    {
        var s = value.AsString()?.ToLowerInvariant() ?? "";
        if (string.IsNullOrEmpty(s)) return null;

        // 颜色名 → 颜色值
        var color = s switch
        {
            "red" => Avalonia.Media.Colors.Red,
            "green" => Avalonia.Media.Colors.Green,
            "blue" => Avalonia.Media.Colors.Blue,
            "yellow" => Avalonia.Media.Colors.Yellow,
            "white" => Avalonia.Media.Colors.White,
            "black" => Avalonia.Media.Colors.Black,
            "gray" or "grey" => Avalonia.Media.Colors.Gray,
            "orange" => Avalonia.Media.Colors.Orange,
            _ => ParseHexColor(s),
        };

        return new Avalonia.Media.SolidColorBrush(color);
    }

    private static Avalonia.Media.Color ParseHexColor(string hex)
    {
        try
        {
            if (hex.StartsWith("#"))
            {
                hex = hex[1..];

                // 3 字符简写 → 6 字符: #ABC → #AABBCC
                if (hex.Length == 3)
                    hex = $"{hex[0]}{hex[0]}{hex[1]}{hex[1]}{hex[2]}{hex[2]}";

                if (hex.Length == 6)
                    return Avalonia.Media.Color.Parse("#" + hex);
                if (hex.Length == 8)
                    return Avalonia.Media.Color.Parse("#" + hex);
            }
            return Avalonia.Media.Color.Parse(hex);
        }
        catch
        {
            return Avalonia.Media.Colors.Black;
        }
    }

    private static object? ConvertValue(Value value, Type targetType)
    {
        if (targetType == typeof(string)) return value.AsString();
        if (targetType == typeof(double)) return ToDouble(value);
        if (targetType == typeof(int)) return value.As<int>();
        if (targetType == typeof(bool)) return value.AsBool();
        if (targetType == typeof(Thickness)) return ToThickness(value);
        if (value is ClrObjectValue clr) return clr.Value;
        return null;
    }
}
