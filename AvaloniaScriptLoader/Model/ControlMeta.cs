namespace AvaloniaScriptLoader.Model;

/// <summary>
/// 控件描述符元数据常量
/// </summary>
public static class ControlMeta
{
    /// <summary>控件类型键（"__type"）</summary>
    public const string TypeKey = "__type";

    /// <summary>控件唯一 ID 键（"__id"）</summary>
    public const string IdKey = "__id";

    /// <summary>实际 Avalonia Control 引用键（"__control"，Build 后注入）</summary>
    public const string ControlKey = "__control";

    /// <summary>ControlWrapper 引用键（"__wrapper"，Build 后注入）</summary>
    public const string WrapperKey = "__wrapper";

    /// <summary>控件类型名常量</summary>
    public static class Types
    {
        public const string Window = "window";
        public const string Button = "button";
        public const string Label = "label";
        public const string TextBox = "textbox";
        public const string CheckBox = "checkbox";
        public const string ComboBox = "combobox";
        public const string ListBox = "listbox";
        public const string StackPanel = "stackpanel";
        public const string Grid = "grid";
        // 新增控件
        public const string Image = "image";
        public const string ScrollViewer = "scrollviewer";
        public const string Border = "border";
        public const string TabControl = "tabcontrol";
        public const string TabItem = "tabitem";
        public const string DataGrid = "datagrid";
        public const string Dialog = "dialog";
        public const string DatePicker = "datepicker";
        public const string TimePicker = "timepicker";
        public const string Slider = "slider";
        public const string ProgressBar = "progressbar";
        public const string Expander = "expander";
        public const string MenuItem = "menuitem";
        public const string Separator = "separator";
        public const string NavMenu = "navmenu";
        public const string NavMenuItem = "navmenuitem";
        public const string NavMenuGroup = "navmenugroup";
    }

    /// <summary>控件属性名前缀（用于识别 setter 方法）</summary>
    public const string SetterPrefix = "set";

    /// <summary>通用 setter 方法名</summary>
    public const string GenericSetter = "set";
}
