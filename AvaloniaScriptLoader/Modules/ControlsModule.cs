using ScriptLang.Runtime;
using AvaloniaScriptLoader.Factory;

namespace AvaloniaScriptLoader.Modules;

/// <summary>
/// "avalonia.controls" 内置模块 — 提供所有控件工厂函数
/// 脚本用法: import { window, button, label, ... } from "avalonia.controls"
/// </summary>
public static class ControlsModule
{
    /// <summary>
    /// 创建模块导出对象，包含 9 个控件工厂函数
    /// </summary>
    public static ObjectValue CreateExports()
    {
        var properties = new Dictionary<string, Value>
        {
            ["window"]       = ControlFactory.CreateWindowFactory(),
            ["button"]       = ControlFactory.CreateButtonFactory(),
            ["label"]        = ControlFactory.CreateLabelFactory(),
            ["textbox"]      = ControlFactory.CreateTextBoxFactory(),
            ["checkbox"]     = ControlFactory.CreateCheckBoxFactory(),
            ["combobox"]     = ControlFactory.CreateComboBoxFactory(),
            ["listbox"]      = ControlFactory.CreateListBoxFactory(),
            ["stackpanel"]   = ControlFactory.CreateStackPanelFactory(),
            ["grid"]         = ControlFactory.CreateGridFactory(),
            // 新增控件
            ["image"]        = ControlFactory.CreateImageFactory(),
            ["scrollviewer"] = ControlFactory.CreateScrollViewerFactory(),
            ["border"]       = ControlFactory.CreateBorderFactory(),
            ["tabcontrol"]   = ControlFactory.CreateTabControlFactory(),
            ["tabitem"]      = ControlFactory.CreateTabItemFactory(),
            ["datagrid"]     = ControlFactory.CreateDataGridFactory(),
            ["dialog"]       = ControlFactory.CreateDialogFactory(),
            ["datepicker"]   = ControlFactory.CreateDatePickerFactory(),
            ["timepicker"]   = ControlFactory.CreateTimePickerFactory(),
            ["slider"]       = ControlFactory.CreateSliderFactory(),
            ["progressbar"]  = ControlFactory.CreateProgressBarFactory(),
            ["expander"]     = ControlFactory.CreateExpanderFactory(),
            ["menuitem"]     = ControlFactory.CreateMenuItemFactory(),
            ["separator"]    = ControlFactory.CreateSeparatorFactory(),
            ["navmenu"]      = ControlFactory.CreateNavMenuFactory(),
            ["navmenuitem"]  = ControlFactory.CreateNavMenuItemFactory(),
            ["navmenugroup"] = ControlFactory.CreateNavMenuGroupFactory(),
        };

        return new ObjectValue(properties);
    }
}
