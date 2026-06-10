using ScriptLang.Runtime;
using AvaloniaScriptLoader.Model;

namespace AvaloniaScriptLoader.Factory;

/// <summary>
/// 控件描述符工厂 — 为每种控件类型创建对应的 ObjectValue 描述符
///
/// 每个工厂返回 FunctionValue，脚本中调用后返回 ObjectValue 描述符：
/// {
///     "__type": "button",
///     "__id": "abc123...",
///     "text": "点击我",
///     "onClick": CompiledFunctionValue,
///     "setText": DeferredFunctionValue,   // Build 后激活
///     "setWidth": DeferredFunctionValue,
///     "set": DeferredFunctionValue,       // 通用 setter
///     ...
/// }
/// </summary>
public static class ControlFactory
{
    // ============================================================================
    // 公共工厂方法（由 ControlsModule 调用）
    // ============================================================================

    public static FunctionValue CreateWindowFactory() =>
        new("window", args => BuildDescriptor(ControlMeta.Types.Window, args,
            ["title", "width", "height", "content"]));

    public static FunctionValue CreateButtonFactory() =>
        new("button", args => BuildDescriptor(ControlMeta.Types.Button, args,
            ["text", "width", "height", "color", "background", "onClick"]));

    public static FunctionValue CreateLabelFactory() =>
        new("label", args => BuildDescriptor(ControlMeta.Types.Label, args,
            ["text", "fontSize", "color", "fontWeight", "align"]));

    public static FunctionValue CreateTextBoxFactory() =>
        new("textbox", args => BuildDescriptor(ControlMeta.Types.TextBox, args,
            ["text", "placeholder", "width", "height", "password", "readonly", "multiline", "onChange"]));

    public static FunctionValue CreateCheckBoxFactory() =>
        new("checkbox", args => BuildDescriptor(ControlMeta.Types.CheckBox, args,
            ["text", "checked", "onChange"]));

    public static FunctionValue CreateComboBoxFactory() =>
        new("combobox", args => BuildDescriptor(ControlMeta.Types.ComboBox, args,
            ["items", "selected", "selectedItem", "onSelect"]));

    public static FunctionValue CreateListBoxFactory() =>
        new("listbox", args => BuildDescriptor(ControlMeta.Types.ListBox, args,
            ["items", "selected", "template", "onSelect"]));

    public static FunctionValue CreateStackPanelFactory() =>
        new("stackpanel", args => BuildDescriptor(ControlMeta.Types.StackPanel, args,
            ["orientation", "spacing", "padding", "children"]));

    public static FunctionValue CreateGridFactory() =>
        new("grid", args => BuildDescriptor(ControlMeta.Types.Grid, args,
            ["rows", "cols", "children"]));

    // === 新增控件 ===

    public static FunctionValue CreateImageFactory() =>
        new("image", args => BuildDescriptor(ControlMeta.Types.Image, args,
            ["source", "stretch"]));

    public static FunctionValue CreateScrollViewerFactory() =>
        new("scrollviewer", args => BuildDescriptor(ControlMeta.Types.ScrollViewer, args,
            ["content", "horizontalScrollBarVisibility", "verticalScrollBarVisibility"]));

    public static FunctionValue CreateBorderFactory() =>
        new("border", args => BuildDescriptor(ControlMeta.Types.Border, args,
            ["content", "background", "borderBrush", "borderThickness", "cornerRadius"]));

    public static FunctionValue CreateTabControlFactory() =>
        new("tabcontrol", args => BuildDescriptor(ControlMeta.Types.TabControl, args,
            ["items", "selectedIndex"]));

    public static FunctionValue CreateTabItemFactory() =>
        new("tabitem", args => BuildDescriptor(ControlMeta.Types.TabItem, args,
            ["header", "content"]));

    // ============================================================================
    // 核心构建逻辑
    // ============================================================================

    /// <summary>
    /// 为指定控件类型构建描述符 ObjectValue
    /// </summary>
    /// <param name="typeName">控件类型名（"button", "label" ...）</param>
    /// <param name="args">脚本调用时传入的参数列表（第一个应为 opts ObjectValue）</param>
    /// <param name="supportedProperties">该控件支持的属性名列表</param>
    private static ObjectValue BuildDescriptor(string typeName, List<Value> args, string[] supportedProperties)
    {
        var descriptor = new Dictionary<string, Value>
        {
            [ControlMeta.TypeKey] = StringValue.Create(typeName),
            [ControlMeta.IdKey]   = StringValue.Create(Guid.NewGuid().ToString("N")),
        };

        // 解析 opts 参数
        var opts = args.FirstOrDefault() as ObjectValue;
        var optProps = opts?.Properties ?? [];

        // 复制用户定义的属性
        foreach (var propName in supportedProperties)
        {
            if (optProps.TryGetValue(propName, out var value))
            {
                descriptor[propName] = value;
            }
        }

        // 复制通用属性（所有控件都支持）
        foreach (var commonProp in new[] { "width", "height", "margin", "padding", "visible", "enabled", "name", "row", "col", "rowSpan", "colSpan" })
        {
            if (optProps.TryGetValue(commonProp, out var value))
            {
                descriptor[commonProp] = value;
            }
        }

        // 先创建 ObjectValue，再注入 setter（使 setter 能捕获此 ObjectValue 引用）
        var objValue = new ObjectValue(descriptor);

        // 注入延迟 Setter 方法（捕获 objValue 引用）
        InjectSetters(objValue, supportedProperties);

        // 注入通用 set(name, value) 方法
        objValue.Properties[ControlMeta.GenericSetter] = CreateDeferredSetter(objValue, "");

        return objValue;
    }

    // ============================================================================
    // Setter 方法注入
    // ============================================================================

    /// <summary>
    /// 为描述符注入所有 setXxx 方法（延迟模式，Build 时激活）
    /// </summary>
    private static void InjectSetters(ObjectValue descriptorObj, string[] supportedProperties)
    {
        // 为每个支持的可设置属性创建 setXxx 方法
        var settableProps = new HashSet<string>
        {
            "text", "width", "height", "visible", "enabled",
            "checked", "selected", "selectedItem", "items",
            "fontSize", "color", "background", "placeholder",
            "title", "content", "children",
        };

        foreach (var propName in supportedProperties)
        {
            if (settableProps.Contains(propName))
            {
                var setterName = "set" + char.ToUpperInvariant(propName[0]) + propName[1..];
                descriptorObj.Properties[setterName] = CreateDeferredSetter(descriptorObj, propName);
            }
        }

        // 通用属性 setter
        foreach (var commonProp in new[] { "width", "height", "visible", "enabled" })
        {
            if (!supportedProperties.Contains(commonProp))
            {
                var setterName = "set" + char.ToUpperInvariant(commonProp[0]) + commonProp[1..];
                if (!descriptorObj.Properties.ContainsKey(setterName))
                    descriptorObj.Properties[setterName] = CreateDeferredSetter(descriptorObj, commonProp);
            }
        }
    }

    /// <summary>
    /// 创建延迟 Setter 函数（捕获 descriptor 引用）
    ///
    /// Phase 1（脚本执行时）：仅更新描述符中的属性值
    /// Phase 2（Build 激活后）：由 ControlWrapper.Activate() 替换为真实实现
    /// </summary>
    /// <param name="descriptor">控件描述符 ObjectValue（闭包捕获）</param>
    /// <param name="propertyName">属性名（空字符串表示通用 set）</param>
    internal static FunctionValue CreateDeferredSetter(ObjectValue descriptor, string propertyName)
    {
        var setterName = string.IsNullOrEmpty(propertyName)
            ? ControlMeta.GenericSetter
            : "set" + char.ToUpperInvariant(propertyName[0]) + propertyName[1..];

        // 捕获 descriptor 引用
        var capturedDescriptor = descriptor;
        var capturedPropName = propertyName;

        return new FunctionValue(setterName, args =>
        {
            var value = args.FirstOrDefault() ?? Value.Null;

            // 更新描述符中的属性值
            if (!string.IsNullOrEmpty(capturedPropName))
            {
                capturedDescriptor.Properties[capturedPropName] = value;
            }

            // 如果已激活（有 __wrapper），则通过 wrapper 更新 UI
            if (capturedDescriptor.Properties.TryGetValue(ControlMeta.WrapperKey, out var wrapperValue)
                && wrapperValue is ClrObjectValue clr
                && clr.Value is Wrapper.ControlWrapper wrapper)
            {
                wrapper.SetProperty(capturedPropName, value);
            }
            // 否则，变更将在 Activate 时由 pending 队列处理
            // （注：脚本执行期间对 setter 的调用仅在事件处理器中发生，
            //   事件处理器在 Build 后才注册，此时 wrapper 已激活）
        });
    }
}
