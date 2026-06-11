using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Layout;

namespace AvaloniaScriptLoader.Controls;

/// <summary>
/// 现代化垂直导航菜单 — 继承 ScrollViewer，内部 StackPanel
/// </summary>
public class NavMenu : ScrollViewer
{
    private readonly StackPanel _panel;

    public NavMenu()
    {
        _panel = new StackPanel();
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled;
        Content = _panel;
    }

    internal void AddItem(Control item)
    {
        _panel.Children.Add(item);
    }
}

/// <summary>
/// 导航菜单项 — 选中/悬停均有视觉反馈
///
/// 支持属性: text, icon, active, fontSize（computed 绑定均可用）
/// </summary>
public class NavMenuItem : ContentControl
{
    // === 内部视觉元素 ===
    private readonly Border _selectionBar;
    private readonly Border _bgBorder;
    private readonly TextBlock _iconBlock;
    private readonly TextBlock _textBlock;

    // === 字段 ===
    private string _text = "";
    private string _icon = "";
    private bool _active;

    /// <summary>菜单文本（通过反射由 PropertyBinder 设置）</summary>
    public string Text
    {
        get => _text;
        set { _text = value ?? ""; _textBlock.Text = value ?? ""; }
    }

    /// <summary>图标字符（通过反射由 PropertyBinder 设置）</summary>
    public string Icon
    {
        get => _icon;
        set
        {
            _icon = value ?? "";
            _iconBlock.Text = value ?? "";
            _iconBlock.IsVisible = !string.IsNullOrEmpty(value);
        }
    }

    /// <summary>是否激活/选中（通过反射由 PropertyBinder 设置，可用 computed 绑定）</summary>
    public bool Active
    {
        get => _active;
        set
        {
            _active = value;
            _selectionBar.Background = value ? Brush.Parse("#6366f1") : Brushes.Transparent;
            _bgBorder.Background = value ? Brush.Parse("#1e293b") : Brushes.Transparent;
            _textBlock.Foreground = value ? Brush.Parse("#ffffff") : Brush.Parse("#94a3b8");
        }
    }

    public NavMenuItem()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        MinHeight = 28;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        FontSize = 12; // 默认文字大小，可被脚本 fontSize 属性覆盖（子 TextBlock 自动继承）

        // 左侧选中指示条（3px 宽，右侧圆角）
        _selectionBar = new Border
        {
            Width = 3,
            Background = Brushes.Transparent,
            CornerRadius = new CornerRadius(0, 2, 2, 0),
            VerticalAlignment = VerticalAlignment.Stretch,
        };

        // 图标（比文字略大，不继承 FontSize）
        _iconBlock = new TextBlock
        {
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 8, 0),
            IsVisible = false,
        };

        // 文字（从父级 NavMenuItem 继承 FontSize）
        _textBlock = new TextBlock
        {
            VerticalAlignment = VerticalAlignment.Center,
            Foreground = Brush.Parse("#94a3b8"),
        };

        // 内容行：图标 + 文字
        var row = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _iconBlock, _textBlock },
            Margin = new Thickness(14, 8),
        };

        // 背景容器（圆角）
        _bgBorder = new Border
        {
            CornerRadius = new CornerRadius(4),
            Margin = new Thickness(4, 1, 4, 1),
            Child = row,
        };

        // 最外层：选中条 + 背景容器
        var outer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _selectionBar, _bgBorder },
        };

        Content = outer;

        // Hover 效果（非激活态才有）
        PointerEntered += (_, _) =>
        {
            if (!_active) _bgBorder.Background = Brush.Parse("#1e293b");
        };
        PointerExited += (_, _) =>
        {
            if (!_active) _bgBorder.Background = Brushes.Transparent;
        };
    }
}

/// <summary>
/// 导航菜单分组标题 — 可折叠/展开，自动隐藏其子项
///
/// 支持属性: header (string | 控件描述符), isExpanded (bool), fontSize
///
/// header 为 string 时显示文字；为控件描述符时使用自定义控件（保留箭头）
/// </summary>
public class NavMenuGroup : ContentControl
{
    // === 内部视觉元素 ===
    private readonly TextBlock _arrowBlock;
    private readonly TextBlock _headerBlock;
    private readonly StackPanel _headerRow;

    // === 字段 ===
    private bool _isExpanded = false;
    private List<NavMenuItem>? _children;

    /// <summary>分组标题文本（通过反射由 PropertyBinder 设置）</summary>
    public string? Header
    {
        get => _headerBlock.Text;
        set => _headerBlock.Text = value ?? "";
    }

    /// <summary>是否展开（通过反射由 PropertyBinder 设置）</summary>
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            _arrowBlock.Text = value ? "▼" : "▶";
            ApplyChildVisibility();
        }
    }

    /// <summary>注册子项（由 ControlBuilder 在构建 items 时调用），同步当前展开状态</summary>
    internal void AddChild(NavMenuItem item)
    {
        _children ??= new List<NavMenuItem>();
        _children.Add(item);
        item.IsVisible = _isExpanded;
    }

    /// <summary>
    /// 用自定义控件替换标题文字（保留箭头），由 ControlBuilder 在 header 为 ObjectValue 时调用
    /// </summary>
    internal void SetHeaderControl(Control headerControl)
    {
        headerControl.VerticalAlignment = VerticalAlignment.Center;
        var index = _headerRow.Children.IndexOf(_headerBlock);
        if (index >= 0)
        {
            _headerRow.Children.RemoveAt(index);
            _headerRow.Children.Insert(index, headerControl);
        }
    }

    private void ApplyChildVisibility()
    {
        if (_children == null) return;
        foreach (var child in _children)
            child.IsVisible = _isExpanded;
    }

    public NavMenuGroup()
    {
        Cursor = new Cursor(StandardCursorType.Hand);
        MinHeight = 28;
        HorizontalContentAlignment = HorizontalAlignment.Stretch;
        FontSize = 11;

        // 展开/折叠箭头（不继承 FontSize，保持小号）
        _arrowBlock = new TextBlock
        {
            Text = "▶",
            FontSize = 8,
            Foreground = Brush.Parse("#64748b"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 6, 0),
        };

        // 分组标题（从父级 NavMenuGroup 继承 FontSize；可被 SetHeaderControl 替换）
        _headerBlock = new TextBlock
        {
            Foreground = Brush.Parse("#64748b"),
            VerticalAlignment = VerticalAlignment.Center,
        };

        _headerRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Children = { _arrowBlock, _headerBlock },
            Margin = new Thickness(14, 12, 14, 4),
        };

        Content = _headerRow;

        // 点击切换展开/折叠
        Tapped += (_, _) =>
        {
            IsExpanded = !IsExpanded;
        };
    }
}
