namespace AvaloniaScriptLoader.Model;

/// <summary>
/// 控件属性名常量
/// </summary>
public static class PropertyNames
{
    // === 通用属性 ===
    public const string Width = "width";
    public const string Height = "height";
    public const string Margin = "margin";
    public const string Padding = "padding";
    public const string Visible = "visible";
    public const string Enabled = "enabled";
    public const string Name = "name";

    // === 布局属性（Grid） ===
    public const string Row = "row";
    public const string Col = "col";
    public const string RowSpan = "rowSpan";
    public const string ColSpan = "colSpan";

    // === 内容属性 ===
    public const string Text = "text";
    public const string Content = "content";
    public const string Children = "children";

    // === 外观属性 ===
    public const string FontSize = "fontSize";
    public const string FontWeight = "fontWeight";
    public const string Color = "color";
    public const string Background = "background";

    // === Label 特有 ===
    public const string Align = "align";

    // === TextBox 特有 ===
    public const string Placeholder = "placeholder";
    public const string Password = "password";
    public const string Readonly = "readonly";
    public const string Multiline = "multiline";

    // === CheckBox 特有 ===
    public const string Checked = "checked";

    // === ComboBox / ListBox 特有 ===
    public const string Items = "items";
    public const string Selected = "selected";
    public const string SelectedItem = "selectedItem";
    public const string Template = "template";

    // === StackPanel 特有 ===
    public const string Orientation = "orientation";
    public const string Spacing = "spacing";

    // === Grid 特有 ===
    public const string Rows = "rows";
    public const string Cols = "cols";

    // === Window 特有 ===
    public const string Title = "title";

    // === 事件属性 ===
    public const string OnClick = "onClick";
    public const string OnChange = "onChange";
    public const string OnSelect = "onSelect";

    /// <summary>
    /// 判断属性名是否为事件处理器（onXxx 模式）
    /// </summary>
    public static bool IsEventProperty(string name) => name switch
    {
        "onClick" or "onChange" or "onSelect" or "onLoad" or "onClose" => true,
        _ => name.StartsWith("on") && name.Length > 2 && char.IsUpper(name[2]),
    };

    /// <summary>
    /// 判断属性名是否为 setter 方法
    /// </summary>
    public static bool IsSetterMethod(string name) =>
        (name.StartsWith("set") && name.Length > 3 && char.IsUpper(name[3]))
        || name == "set";

    /// <summary>
    /// 从 setter 方法名提取属性名：setText → text, set → ""
    /// </summary>
    public static string SetterToPropertyName(string setterName)
    {
        if (setterName == "set") return "";
        if (setterName.StartsWith("set") && setterName.Length > 3)
            return char.ToLowerInvariant(setterName[3]) + setterName[4..];
        return setterName;
    }
}
