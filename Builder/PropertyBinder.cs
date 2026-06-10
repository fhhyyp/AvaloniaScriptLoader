using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ScriptLang.Runtime;
using AvaloniaScriptLoader.Factory;
using AvaloniaScriptLoader.Model;

namespace AvaloniaScriptLoader.Builder;

/// <summary>
/// 属性绑定器 — 将描述符中的属性值映射到 Avalonia 控件属性
/// 支持 InpcValue 自动订阅（脚本变量变更 → UI 自动更新）
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
            if (name is "children" or "content") continue;

            // 检测 InpcValue 包装 → 自动订阅
            if (InpcFactory.IsInpcWrapper(value))
            {
                BindInpcValue(control, name, value);
            }
            else
            {
                SetControlProperty(control, name, value);
            }
        }
    }

    /// <summary>
    /// 对 InpcValue 包装对象建立自动绑定：
    /// 1. 读取当前值 → 设置控件初始属性
    /// 2. 订阅 InpcValue 变更 → Dispatcher 调度更新 UI
    /// </summary>
    private void BindInpcValue(Control control, string propertyName, Value inpcWrapper)
    {
        var inpc = InpcFactory.ExtractInpc(inpcWrapper);
        if (inpc == null) return;

        // 1. 设置初始值
        SetControlProperty(control, propertyName, inpc.Get());

        // 2. 订阅变更
        inpc.OnChange(newValue =>
        {
            // 在 UI 线程更新控件
            void Update()
            {
                SetControlProperty(control, propertyName, newValue);
            }

            if (Dispatcher.UIThread.CheckAccess())
                Update();
            else
                Dispatcher.UIThread.Post(Update);
        });

        System.Diagnostics.Debug.WriteLine(
            $"[InpcBinding] 已绑定 '{propertyName}' → {inpc.SubscriberCount} 订阅者");
    }

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

    private void ApplyProperty(Control control, string name, Value value)
    {
        switch (name)
        {
            // === 通用布局属性 ===
            case "width":
                control.Width = ToDouble(value);
                break;
            case "height":
                control.Height = ToDouble(value);
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
        // 使用 if/else 链而非 switch 模式匹配，避免 Avalonia 12.x 类层次冲突
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
        else if (control is StackPanel stackPanel)
            ApplyStackPanelProperty(stackPanel, name, value);
        else if (control is Grid grid)
            ApplyGridProperty(grid, name, value);
        else if (control is TextBlock textBlock)
            ApplyLabelProperty(textBlock, name, value);
        else
            ApplyByReflection(control, name, value);
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
        if (value.IsNumber_Double) return new Thickness(value.As<double>());
        if (value.IsNumber_Int) return new Thickness(value.As<int>());

        // 支持 { top=10, left=5, right=5, bottom=10 } 对象格式
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
