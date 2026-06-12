using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaScriptLoader.Model;
using ScriptLang.Runtime;
using System;
using System.Collections.Generic;
using System.Linq;

namespace AvaloniaScriptLoader.Controls;

/// <summary>
/// 自定义数据表格控件，支持分页、排序、单选/多选、自定义模板等功能
/// 继承自 Grid，分为数据区域（ScrollViewer）和分页导航区域两部分
/// </summary>
public class DataTable : Grid
{
    // ════════════════════════════════════════
    // 外部依赖注入（由脚本加载器设置）
    // ════════════════════════════════════════

    /// <summary>UI 构建器，用于将脚本中的 ObjectValue 转换为 Avalonia 控件</summary>
    internal static Builder.ControlBuilder? Builder { get; set; }

    /// <summary>脚本引擎实例，用于执行模板回调</summary>
    internal static ScriptLang.ScriptEngine? ScriptEngine { get; set; }

    // ════════════════════════════════════════
    // UI 核心控件
    // ════════════════════════════════════════

    /// <summary>滚动容器，包裹内部数据网格</summary>
    private readonly ScrollViewer _scroller = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,  // 水平滚动条自动显示
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto     // 垂直滚动条自动显示
    };

    /// <summary>内部网格，用于渲染表头和数据行</summary>
    private readonly Grid _innerGrid = new();

    /// <summary>分页导航面板（包含页码按钮、跳转输入框等）</summary>
    private readonly StackPanel _pagerPanel;

    /// <summary>页码跳转输入框（用户输入页码后按回车或点击 Go 跳转）</summary>
    private TextBox _pageInput;

    /// <summary>分页信息标签（显示"共 X 条 | 1/10"等文本）</summary>
    private TextBlock _pageLabel;

    // ════════════════════════════════════════
    // 列配置
    // ════════════════════════════════════════

    /// <summary>总列数（包含复选框列）</summary>
    private int _colCount;

    /// <summary>每列绑定的属性名数组，索引对应列索引</summary>
    private string[] _bindings = [];

    /// <summary>是否为只读模式（只读时使用 TextBlock 展示数据，否则使用 TextBox 可编辑）</summary>
    private bool _isReadOnly;

    /// <summary>当前排序列的绑定属性名，null 表示未排序</summary>
    private string? _sortCol;

    /// <summary>排序方向：true=升序，false=降序</summary>
    private bool _sortAsc = true;

    /// <summary>全量数据（未分页前的所有行）</summary>
    private ArrayValue? _allItems;

    /// <summary>每页最大行数，0 表示不启用分页</summary>
    private int _maxCount;

    /// <summary>当前页码（从 0 开始）</summary>
    private int _currentPage;

    // ════════════════════════════════════════
    // 选择模式配置
    // ════════════════════════════════════════

    /// <summary>当前选择模式："none"=不可选，"single"=单选，"multiple"=多选</summary>
    private string _selMode = "none";

    /// <summary>保存复选框列的原始宽度（GridLength 结构体），用于恢复显示</summary>
    private GridLength _savedCbWidth = new GridLength(40);

    /// <summary>已选中的全局索引集合（仅在不使用属性绑定时生效）</summary>
    private HashSet<int> _selected = new();

    /// <summary>选择状态绑定的属性名，不为 null 时通过对象属性记录选中状态</summary>
    private string? _selBinding;

    /// <summary>选择索引偏移量（用于计算全局索引时的额外偏移）</summary>
    private int _selOffset;

    /// <summary>是否显示复选框列</summary>
    private bool _hasCheckbox;

    /// <summary>数据列起始索引（0 或 1，取决于是否有复选框列）</summary>
    private int _dataColOffset;

    /// <summary>单元格模板回调函数数组（索引对应列索引），用于自定义渲染</summary>
    private ICallable?[] _templates = [];

    /// <summary>绑定到的 TableValue 对象（支持数据变更通知）</summary>
    private TableValue? _tableValue;

    // ════════════════════════════════════════
    // 样式配置
    // ════════════════════════════════════════

    private string _hdrBg = "#f1f5f9";       // 表头背景色
    private string _hdrFg = "#475569";       // 表头文字色
    private string _rowEvenBg = "#ffffff";   // 偶数行背景色
    private string _rowOddBg = "#f8fafc";    // 奇数行背景色
    private string _lineColor = "#e2e8f0";   // 网格线颜色
    private string _selBg = "#dbeafe";       // 选中行背景色
    private string _selFg = "#1e40af";       // 选中行文字色
    private int _cellFontSize = 12;          // 单元格字体大小
    private int _hdrFontSize = 12;           // 表头字体大小


    private bool _isLoad = false;

    // ════════════════════════════════════════
    // 构造函数：初始化布局结构
    // ════════════════════════════════════════

    /// <summary>
    /// 构造函数，初始化两行布局：
    /// - Row 0：ScrollViewer（数据区域，占据剩余空间 Star）
    /// - Row 1：分页导航栏（自适应高度 Auto）
    /// </summary>
    public DataTable()
    {
        // 设置滚动容器的内容为内部数据网格
        _scroller.Content = _innerGrid;

        // 主布局：两行，数据区占满剩余空间，分页区自适应高度
        RowDefinitions.Add(new RowDefinition(GridLength.Star));   // 数据区域
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));   // 分页区域

        // 创建分页信息标签（显示"共 X 条"等信息）
        _pageLabel = new TextBlock
        {
            FontSize = 11,
            Foreground = Brush.Parse("#64748b"),                  // 灰色文字
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 0)
        };

        // 创建页码跳转输入框
        _pageInput = new TextBox
        {
            Width = 40,                                           // 固定宽度
            FontSize = 11,
            Margin = new Thickness(4, 0)
        };
        // 监听回车键事件，触发页码跳转
        _pageInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                GoToPage();
        };

        // 创建分页导航面板（水平排列的 StackPanel）
        _pagerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,                                          // 控件间距
            HorizontalAlignment = HorizontalAlignment.Right,      // 右对齐
            Margin = new Thickness(0, 2),
        };

        // 将分页面板包裹在 Border 中，添加到主布局的第二行
        var pagerRow = new Border
        {
            Child = _pagerPanel,
            Padding = new Thickness(4, 0)
        };
        Grid.SetRow(pagerRow, 1);                                 // 放置在第二行
        Children.Add(pagerRow);

        // 将滚动容器添加到主布局的第一行
        Children.Add(_scroller);
    }

    // ════════════════════════════════════════
    // 布局重写：手动排列两个区域
    // ════════════════════════════════════════

    /// <summary>
    /// 测量阶段：计算滚动区域和分页区域的期望尺寸
    /// </summary>
    /// <param name="s">可用空间</param>
    /// <returns>总期望尺寸（宽度取滚动区域宽度，高度 = 滚动区域高度 + 分页区域高度）</returns>
    protected override Size MeasureOverride(Size s)
    {
        // 测量滚动区域（占据主要空间）
        _scroller.Measure(s);

        // 测量分页面板（高度不限，取其自然高度）
        _pagerPanel.Measure(new Size(s.Width, double.PositiveInfinity));

        // 计算分页区域高度（如果没有子控件则高度为 0）
        var pH = _pagerPanel.Children.Count > 0 ? _pagerPanel.DesiredSize.Height : 0;

        // 返回总期望尺寸
        return new Size(_scroller.DesiredSize.Width, _scroller.DesiredSize.Height + pH);
    }

    /// <summary>
    /// 排列阶段：手动设置两个区域的位置和大小
    /// </summary>
    /// <param name="s">最终可用空间</param>
    /// <returns>最终尺寸</returns>
    protected override Size ArrangeOverride(Size s)
    {
        var pagerH = _pagerPanel.DesiredSize.Height;

        // 排列滚动区域：占据上方所有空间（减去分页高度）
        _scroller.Arrange(new Rect(0, 0, s.Width, Math.Max(0, s.Height - pagerH)));

        // 排列分页面板：固定在右下角
        _pagerPanel.Arrange(new Rect(
            new Point(s.Width - _pagerPanel.DesiredSize.Width, s.Height - pagerH),
            _pagerPanel.DesiredSize));

        return s;
    }

    // ════════════════════════════════════════
    // 列配置方法
    // ════════════════════════════════════════

    /// <summary>
    /// 设置表格列定义
    /// </summary>
    /// <param name="cols">列配置数组，每个元素是一个 ObjectValue，
    /// 可包含属性：header(标题), binding(绑定字段), width(宽度), sortable(是否可排序), 
    /// checkbox(是否为复选框列), template(自定义模板回调)</param>
    internal void SetColumns(ArrayValue cols)
    {
        // 重置排序状态
        _sortCol = null;
        _sortAsc = true;

        // 重置列计数和标志
        _colCount = 0;
        _hasCheckbox = false;
        _dataColOffset = 0;

        // 清空现有列定义和子控件
        _innerGrid.ColumnDefinitions.Clear();
        _innerGrid.Children.Clear();
        _innerGrid.RowDefinitions.Clear();

        // 添加表头行
        _innerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var colStart = 0;  // 数据列起始索引

        // 检查第一列是否为复选框列
        if (cols.Elements.Count > 0 && cols.Elements[0] is ObjectValue firstCol)
        {
            if (firstCol.Properties.TryGetValue("checkbox", out var cbVal) && cbVal.AsBool())
            {
                _hasCheckbox = true;
                _dataColOffset = 1;  // 数据列从第 1 列开始（第 0 列是复选框）

                // 解析复选框列宽度（默认 40 像素）
                var wStr = firstCol.Properties.TryGetValue("width", out var wv) ? wv.AsString() : "40";
                var checkboxColWidth = ParseGL(wStr);
                _innerGrid.ColumnDefinitions.Add(new ColumnDefinition(checkboxColWidth));
                _savedCbWidth = checkboxColWidth;  // 保存原始宽度用于恢复

                // 创建表头复选框（用于全选/取消全选）
                var hdrCb = new CheckBox
                {
                    IsEnabled = _selMode != "none",     // 不可选模式下禁用
                    Margin = new Thickness(4, 0)
                };
                hdrCb.IsCheckedChanged += (_, _) =>
                {
                    if (_selMode != "none")
                        ToggleAll(hdrCb.IsChecked == true);
                };

                // 将复选框包裹在带样式的 Border 中
                var cbCell = new Border
                {
                    Background = Brush.Parse(_hdrBg),
                    BorderBrush = Brush.Parse(_lineColor),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(4, 4),
                    Child = hdrCb
                };
                Grid.SetRow(cbCell, 0);
                Grid.SetColumn(cbCell, 0);
                _innerGrid.Children.Add(cbCell);

                colStart = 1;  // 数据列从索引 1 开始
            }
        }

        // 设置总列数
        _colCount = cols.Elements.Count;

        // 初始化绑定和模板数组
        _bindings = new string[_colCount];
        _templates = new ICallable?[_colCount];

        // 遍历每一列，创建列定义和表头
        for (int c = colStart; c < _colCount; c++)
        {
            if (cols.Elements[c] is not ObjectValue co) continue;

            // 解析列宽度（支持 "*", "2*", "100" 等格式）
            var wStr2 = co.Properties.TryGetValue("width", out var wv2) ? wv2.AsString() : "*";

            // 解析绑定字段名（用于从数据对象中取值）
            _bindings[c] = co.Properties.TryGetValue("binding", out var bv) ? bv.AsString() : $"col{c}";

            // 解析自定义模板回调
            _templates[c] = co.Properties.TryGetValue("template", out var tv) && tv is ICallable tc ? tc : null;

            // 添加列定义
            _innerGrid.ColumnDefinitions.Add(new ColumnDefinition(ParseGL(wStr2)));

            // 解析表头文本
            var headerText = co.Properties.TryGetValue("header", out var hv) ? hv.AsString() : "";

            // 捕获当前列索引（用于闭包）
            var capC = c;

            // 解析是否可排序
            var sortable = co.Properties.TryGetValue("sortable", out var sv) && sv.AsBool();

            // 创建排序箭头指示器
            var arrow = new TextBlock
            {
                FontSize = 10,
                Foreground = Brush.Parse("#94a3b8"),              // 浅灰色箭头
                VerticalAlignment = VerticalAlignment.Center,
                Text = sortable ? " ↕" : ""                       // 可排序列显示双箭头
            };

            // 创建表头单元格（Border 包裹标题和箭头）
            var hdr = new Border
            {
                Background = Brush.Parse(_hdrBg),
                BorderBrush = Brush.Parse(_lineColor),
                BorderThickness = new Thickness(0, 0, 1, 1),     // 仅右侧和底部有边框
                Padding = new Thickness(6, 4),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = headerText,
                            FontSize = _hdrFontSize,
                            FontWeight = FontWeight.Bold,
                            Foreground = Brush.Parse(_hdrFg),
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        arrow
                    }
                }
            };

            // 如果可排序，添加点击排序功能
            if (sortable)
            {
                hdr.Cursor = new Cursor(StandardCursorType.Hand);  // 鼠标悬停时显示手型
                hdr.PointerPressed += (_, _) =>
                {
                    // 点击已排序的列：切换升降序；第三次点击取消排序
                    if (_sortCol == _bindings[capC])
                    {
                        if (_sortAsc)
                            _sortAsc = false;      // 升序 → 降序
                        else
                            _sortCol = null;       // 降序 → 取消排序
                    }
                    else
                    {
                        // 点击新列：设为升序
                        _sortCol = _bindings[capC];
                        _sortAsc = true;
                    }
                    UpdateSortArrows();   // 更新所有列的箭头指示器
                    RebuildRows();        // 重新渲染数据行
                };
            }

            Grid.SetRow(hdr, 0);
            Grid.SetColumn(hdr, c);
            _innerGrid.Children.Add(hdr);
        }
    }

    // ════════════════════════════════════════
    // 数据设置方法
    // ════════════════════════════════════════

    private Action<Value> _callback;
    /// <summary>
    /// 绑定 TableValue 对象（支持数据变更通知）
    /// 当外部修改 TableValue 时，表格会自动刷新
    /// </summary>
    internal void SetTableValue(TableValue tv)
    {
        var oldTable = _tableValue;
        var isEq = oldTable == tv;
        _tableValue = tv;
        _allItems = tv.Get();          // 获取初始数据
        _currentPage = 0;
        ClearAllSelections();
        RebuildRows(GetPageItems());

        if (isEq)
        {
            // 注册数据变更回调
            _callback = av =>
            {
                if (av is ArrayValue av2)
                {
                    // 使用 UI 线程调度确保线程安全
                    Dispatcher.UIThread.Post(() =>
                    {
                        // 数据变化后，当前页可能超出新数据范围，需要修正
                        _currentPage = Math.Min(
                            _currentPage,
                            Math.Max(0, TotalPages - 1));

                        // 数据变更后全局索引可能失效，清空选择更安全
                        ClearAllSelections();
                        RebuildRows(GetPageItems());
                    });
                }
            };
            tv.OnChange(_callback);
        }
        else
        {
            tv.RemoveOnChanged(_callback);
        }
       
    }

    /// <summary>
    /// 设置只读模式
    /// </summary>
    internal void SetReadOnly(bool ro)
    {
        _isReadOnly = ro;
        RebuildRows(GetPageItems());   // 重新渲染以应用只读/编辑模式
    }

    // ════════════════════════════════════════
    // 分页相关属性和方法
    // ════════════════════════════════════════

    /// <summary>
    /// 设置每页最大行数
    /// </summary>
    internal void SetMaxCount(int n)
    {
        _maxCount = n;
        _currentPage = 0;              // 重置到第一页
        ClearAllSelections();          // 清空选择
        RebuildRows(GetPageItems());
        BuildPager();                  // 重建分页控件
    }

    /// <summary>
    /// 设置当前页码
    /// </summary>
    internal void SetCurrentPage(int n)
    {
        if (_allItems == null) return;

        var tp = TotalPages;
        // 限制页码在有效范围内 [0, tp-1]
        _currentPage = Math.Clamp(n, 0, Math.Max(0, tp - 1));
        RebuildRows(GetPageItems());
        BuildPager();
    }

    /// <summary>
    /// 计算总页数
    /// </summary>
    private int TotalPages
    {
        get
        {
            if (_allItems == null) return 1;
            if (_maxCount <= 0) return 1;  // 未启用分页时视为 1 页
            return Math.Max(1, (int)Math.Ceiling((double)_allItems.Elements.Count / _maxCount));
        }
    }

    /// <summary>
    /// 获取当前页的数据切片
    /// </summary>
    /// <returns>当前页的 ArrayValue，如果未启用分页则返回全部数据</returns>
    private ArrayValue GetPageItems()
    {
        if (_allItems == null || _maxCount <= 0)
            return _allItems ?? new ArrayValue(new List<Value>());

        var start = _currentPage * _maxCount;
        var list = new List<Value>();

        // 从全量数据中截取当前页范围
        for (int i = start; i < start + _maxCount && i < _allItems.Elements.Count; i++)
            list.Add(_allItems.Elements[i]);

        return new ArrayValue(list);
    }

    /// <summary>
    /// 计算全局索引（在全量数据中的位置）
    /// </summary>
    /// <param name="pageIndex">当前页内的行索引</param>
    /// <returns>全局索引 = 当前页起始索引 + 偏移量 + 页内索引</returns>
    private int GetGlobalIndex(int pageIndex)
    {
        return (_maxCount > 0 ? _currentPage * _maxCount : 0) + _selOffset + pageIndex;
    }

    /// <summary>
    /// 构建分页导航面板
    /// ★ 每次调用都会创建新的按钮和控件，避免重复添加到同一个父容器导致异常
    /// </summary>
    private void BuildPager()
    {
        // 清空现有分页控件
        _pagerPanel.Children.Clear();

        // 未启用分页或无数据时，不显示分页控件
        if (_maxCount <= 0 || _allItems == null) return;

        var tp = TotalPages;    // 总页数
        var cp = _currentPage;  // 当前页码

        // 辅助方法：添加分页按钮
        void AddBtn(string text, int target, bool enabled)
        {
            var btn = new Button
            {
                Content = text,
                FontSize = 11,
                Padding = new Thickness(6, 2),
                Margin = new Thickness(1),
                IsEnabled = enabled  // 禁用时按钮不可点击
            };
            btn.Click += (_, _) =>
            {
                _currentPage = target;
                RebuildRows(GetPageItems());
                // BuildPager 会被 RebuildRows 内部调用，此处无需再调
            };
            _pagerPanel.Children.Add(btn);
        }

        // 首页和上一页按钮
        AddBtn("<<", 0, cp > 0);           // "<<" 跳转到首页
        AddBtn("<", cp - 1, cp > 0);       // "<"  跳转到上一页

        // 计算显示的页码范围（当前页前后各 2 页，最多显示 5 页）
        var start = Math.Max(0, cp - 2);
        var end = Math.Min(tp, cp + 3);

        // 如果起始页大于 0，显示省略号
        if (start > 0)
            _pagerPanel.Children.Add(new TextBlock
            {
                Text = "...",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2)
            });

        // 创建页码按钮
        for (int i = start; i < end; i++)
        {
            // 当前页按钮高亮显示（蓝色背景白色文字）
            var bg = i == cp ? Brush.Parse("#6366f1") : Brushes.Transparent;
            var fg = i == cp ? Brushes.White : Brush.Parse("#475569");

            var pb = new Button
            {
                Content = (i + 1).ToString(),   // 显示数字从 1 开始
                FontSize = 11,
                Padding = new Thickness(6, 2),
                Margin = new Thickness(1),
                Background = bg,
                Foreground = fg
            };

            var pi = i;  // 捕获当前页码用于闭包
            pb.Click += (_, _) =>
            {
                _currentPage = pi;
                RebuildRows(GetPageItems());
            };
            _pagerPanel.Children.Add(pb);
        }

        // 如果结束页小于总页数，显示省略号
        if (end < tp)
            _pagerPanel.Children.Add(new TextBlock
            {
                Text = "...",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2)
            });

        // 下一页和末页按钮
        AddBtn(">", cp + 1, cp < tp - 1);    // ">"  跳转到下一页
        AddBtn(">>", tp - 1, cp < tp - 1);   // ">>" 跳转到末页

        // ★ 创建全新的页码输入框（避免控件重复添加到父容器）
        _pageInput = new TextBox
        {
            Width = 40,
            FontSize = 11,
            Margin = new Thickness(4, 0)
        };
        _pageInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
                GoToPage();
        };
        _pagerPanel.Children.Add(_pageInput);

        // 创建 Go 按钮
        var goBtn = new Button
        {
            Content = "Go",
            FontSize = 11,
            Padding = new Thickness(4, 2)
        };
        goBtn.Click += (_, _) => GoToPage();
        _pagerPanel.Children.Add(goBtn);

        // ★ 创建全新的分页信息标签
        _pageLabel = new TextBlock
        {
            FontSize = 11,
            Foreground = Brush.Parse("#64748b"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 0)
        };
        _pageLabel.Text = $"  {_allItems.Elements.Count} 条 | {cp + 1}/{tp}";
        _pagerPanel.Children.Add(_pageLabel);
    }

    /// <summary>
    /// 跳转到用户输入的页码
    /// </summary>
    private void GoToPage()
    {
        // 解析输入框中的页码（1-based），验证有效性后跳转
        if (int.TryParse(_pageInput.Text, out var p) && p >= 1 && p <= TotalPages)
        {
            _currentPage = p - 1;          // 转换为 0-based
            RebuildRows(GetPageItems());   // 内部会调用 BuildPager
        }
    }

    // ════════════════════════════════════════
    // 选择模式常量和配置方法
    // ════════════════════════════════════════

    /// <summary>
    /// 选择模式常量定义
    /// </summary>
    private static class SelectMode
    {
        public const string None = "none";         // 不可选择
        public const string Single = "single";     // 仅单选
        public const string Multiple = "multiple"; // 可多选
    }

    /// <summary>
    /// 设置选择模式
    /// </summary>
    /// <param name="m">选择模式字符串：none/single/multiple</param>
    internal void SetSelectionMode(string m)
    {
        var prev = _selMode;  // 保存之前的模式
        _selMode = m;

        // 切换到不可选模式，或从多选切换到单选时，清空选择
        if (m == SelectMode.None || (m == SelectMode.Single && prev == SelectMode.Multiple))
            ClearAllSelections();

        // 如果存在复选框列，根据模式显示/隐藏复选框列
        if (_hasCheckbox && _innerGrid.ColumnDefinitions.Count > 0)
        {
            var d = _innerGrid.ColumnDefinitions[0];

            if (m == "none")
            {
                // 隐藏复选框列：保存当前宽度，将列宽度设为 0
                _savedCbWidth = d.Width;          // ★ 保存 GridLength 结构体（而非仅数值）
                d.MaxWidth = 0;
                d.MinWidth = 0;
                d.Width = new GridLength(0);
            }
            else if (prev == "none")
            {
                // 恢复复选框列：使用保存的宽度
                d.MaxWidth = double.PositiveInfinity;
                d.MinWidth = 0;
                d.Width = _savedCbWidth;           // ★ 恢复原始 GridLength
            }
        }

        // 更新复选框列的可见性
        foreach (var ch in _innerGrid.Children)
        {
            if (Grid.GetColumn(ch) == 0 && ch is Border b && b.Child is CheckBox cb)
            {
                if (Grid.GetRow(ch) == 0)
                    // 表头复选框：仅多选模式显示
                    cb.IsVisible = m == "multiple";
                else if (Grid.GetRow(ch) > 0)
                    // 数据行复选框：非 none 模式显示
                    cb.IsVisible = m != "none";
            }
        }

        UpdateHeaderCheckbox();  // 更新表头复选框状态
    }

    /// <summary>
    /// 设置选择状态绑定的属性名
    /// 当设置后，选中状态通过数据对象的该属性记录，而非内部 HashSet
    /// </summary>
    internal void SetSelectionBinding(string? k)
    {
        _selBinding = k;
        RebuildRows(GetPageItems());
    }

    /// <summary>
    /// 设置选择索引偏移量
    /// </summary>
    internal void SetSelectionOffset(int o)
    {
        _selOffset = o;
    }

    // ════════════════════════════════════════
    // 样式设置方法
    // ════════════════════════════════════════

    /// <summary>
    /// 设置表格样式
    /// </summary>
    /// <param name="n">样式类别：headerStyle/rowStyle/cellStyle/selectedStyle</param>
    /// <param name="v">样式配置 ObjectValue</param>
    internal void SetStyle(string n, Value v)
    {
        if (v is not ObjectValue obj) return;

        switch (n)
        {
            case "headerStyle":
                // 表头样式：背景色、文字色、字体大小
                if (obj.Properties.TryGetValue("bg", out var hb)) _hdrBg = hb.AsString();
                if (obj.Properties.TryGetValue("fg", out var hf)) _hdrFg = hf.AsString();
                if (obj.Properties.TryGetValue("fontSize", out var fs)) _hdrFontSize = (int)fs.As<double>();
                break;

            case "rowStyle":
                // 行样式：偶数行背景、奇数行背景、网格线颜色
                if (obj.Properties.TryGetValue("evenBg", out var eb)) _rowEvenBg = eb.AsString();
                if (obj.Properties.TryGetValue("oddBg", out var ob)) _rowOddBg = ob.AsString();
                if (obj.Properties.TryGetValue("lineColor", out var lc)) _lineColor = lc.AsString();
                break;

            case "cellStyle":
                // 单元格样式：字体大小
                if (obj.Properties.TryGetValue("fontSize", out var cs)) _cellFontSize = (int)cs.As<double>();
                break;

            case "selectedStyle":
                // 选中行样式：背景色、文字色
                if (obj.Properties.TryGetValue("bg", out var sb)) _selBg = sb.AsString();
                if (obj.Properties.TryGetValue("fg", out var sf)) _selFg = sf.AsString();
                break;
        }
    }

    // ════════════════════════════════════════
    // 选择状态管理
    // ════════════════════════════════════════

    /// <summary>
    /// 判断指定行是否被选中
    /// </summary>
    /// <param name="gIdx">全局索引</param>
    /// <param name="obj">数据对象（用于属性绑定判断）</param>
    /// <returns>是否选中</returns>
    private bool IsSelected(int gIdx, ObjectValue? obj)
    {
        // 如果设置了选择绑定属性，从对象属性读取
        if (_selBinding != null && obj != null)
            return obj.Properties.TryGetValue(_selBinding, out var bv) && bv.AsBool();

        // 否则从内部 HashSet 查询
        return _selected.Contains(gIdx);
    }

    /// <summary>
    /// 设置指定行的选中状态
    /// </summary>
    /// <param name="gIdx">全局索引</param>
    /// <param name="obj">数据对象（用于属性绑定）</param>
    /// <param name="sel">是否选中</param>
    private void SetSelected(int gIdx, ObjectValue? obj, bool sel)
    {
        if (_selBinding != null && obj != null)
        {
            // 使用属性绑定模式：直接修改对象属性
            obj.Properties[_selBinding] = BoolValue.Create(sel);
        }
        else
        {
            // 使用内部 HashSet 模式
            if (sel)
                _selected.Add(gIdx);
            else
                _selected.Remove(gIdx);
        }
    }

    /// <summary>
    /// 判断当前页是否全选
    /// </summary>
    private bool IsAllSelected()
    {
        var rows = GetRows(GetPageItems());
        if (rows.Count == 0) return false;  // 空列表视为未全选

        for (int i = 0; i < rows.Count; i++)
            if (!IsSelected(GetGlobalIndex(i), rows[i]))
                return false;               // 存在未选中的行

        return true;                        // 所有行都被选中
    }

    /// <summary>
    /// 切换当前页的全选状态
    /// </summary>
    /// <param name="sel">true=全选，false=取消全选</param>
    private void ToggleAll(bool sel)
    {
        var rows = GetRows(GetPageItems());

        for (int i = 0; i < rows.Count; i++)
            SetSelected(GetGlobalIndex(i), rows[i], sel);

        RebuildRows(GetPageItems());   // 重新渲染以更新 UI
        UpdateHeaderCheckbox();       // 更新表头复选框状态
    }

    /// <summary>
    /// 切换指定行的选中状态
    /// </summary>
    /// <param name="pi">当前页内的行索引</param>
    /// <param name="obj">数据对象</param>
    private void ToggleRow(int pi, ObjectValue obj)
    {
        int g = GetGlobalIndex(pi);  // 计算全局索引

        if (_selMode == "single")
        {
            // ★ 单选模式：先清除所有其他选择
            if (_selBinding != null && _allItems != null)
            {
                // 遍历全量数据，将绑定属性设为 false
                foreach (var item in _allItems.Elements)
                {
                    if (item is ObjectValue o)
                        o.Properties[_selBinding] = BoolValue.False;
                }
            }
            _selected.Clear();           // 清空内部 HashSet
            SetSelected(g, obj, true);   // 设置当前行为选中
        }
        else if (_selMode == "multiple")
        {
            // 多选模式：切换当前行的选中状态
            SetSelected(g, obj, !IsSelected(g, obj));
        }
        else
        {
            // none 模式：不处理
            return;
        }

        RebuildRows(GetPageItems());   // 重新渲染
        UpdateHeaderCheckbox();        // 更新表头复选框
    }

    /// <summary>
    /// 更新表头复选框的状态（全选/未全选/部分选中）
    /// </summary>
    private void UpdateHeaderCheckbox()
    {
        foreach (var ch in _innerGrid.Children)
        {
            if (Grid.GetColumn(ch) == 0 && Grid.GetRow(ch) == 0 &&
                ch is Border b && b.Child is CheckBox cb)
            {
                cb.IsEnabled = _selMode != "none";    // 不可选模式下禁用
                cb.IsChecked = IsAllSelected();        // 全选时勾选
                return;
            }
        }
    }

    /// <summary>
    /// 清空所有选中状态（包括内部 HashSet 和属性绑定）
    /// </summary>
    private void ClearAllSelections()
    {
        _selected.Clear();  // 清空内部集合

        // 如果使用了属性绑定，遍历全量数据清除绑定属性
        if (_allItems != null && _selBinding != null)
        {
            var rows = GetRows(_allItems);
            foreach (var r in rows)
                r.Properties[_selBinding] = BoolValue.False;
        }
    }

    // ════════════════════════════════════════
    // 内部工具方法
    // ════════════════════════════════════════

    /// <summary>
    /// 解析列宽度字符串为 GridLength
    /// </summary>
    /// <param name="w">宽度字符串："*"=星号填充, "2*"=比例填充, "100"=固定像素</param>
    /// <returns>GridLength 对象</returns>
    private static GridLength ParseGL(string w)
    {
        if (w == "*") return GridLength.Star;                        // 1* 的简写
        if (w.EndsWith("*") && double.TryParse(w[..^1], out var sw)) // "2*" 格式
            return new GridLength(sw, GridUnitType.Star);
        if (double.TryParse(w, out var pw))                          // "100" 格式
            return new GridLength(pw);
        return GridLength.Star;                                       // 默认
    }

    /// <summary>
    /// 更新所有列标题的排序箭头指示器
    /// ▲ = 升序, ▼ = 降序, ↕ = 未排序
    /// </summary>
    private void UpdateSortArrows()
    {
        for (int c = _dataColOffset; c < _colCount && c < _innerGrid.Children.Count; c++)
        {
            if (_innerGrid.Children[c] is Border h &&
                h.Child is StackPanel sp &&
                sp.Children.Count > 1 &&
                sp.Children[1] is TextBlock a)
            {
                // 根据当前排序列设置箭头
                a.Text = _bindings[c] == _sortCol
                    ? (_sortAsc ? " ▲" : " ▼")   // 当前排序列显示方向箭头
                    : " ↕";                       // 其他列显示双箭头
            }
        }
    }

    /// <summary>
    /// 获取数据行列表（支持排序）
    /// </summary>
    /// <param name="av">数据源，null 时使用当前页数据</param>
    /// <returns>排序后的 ObjectValue 列表</returns>
    private List<ObjectValue> GetRows(ArrayValue? av = null)
    {
        av ??= GetPageItems();  // 默认使用当前页数据
        if (av is null) return [];

        // 过滤出 ObjectValue 类型的元素
        var list = av.Elements.OfType<ObjectValue>().ToList();

        // 如果设置了排序列，执行排序
        if (_sortCol == null) return list;

        var ci = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;

        list.Sort((a, b) =>
        {
            // 获取两个对象的排序列值
            var va = a.Properties.TryGetValue(_sortCol, out var a1) ? a1 : Value.Null;
            var vb = b.Properties.TryGetValue(_sortCol, out var b1) ? b1 : Value.Null;

            // 数值比较
            if (va.IsNumber && vb.IsNumber)
            {
                var c = va.As<double>().CompareTo(vb.As<double>());
                return _sortAsc ? c : -c;
            }

            // 字符串比较（使用当前区域设置）
            var c2 = ci.Compare(va.AsString(), vb.AsString(),
                System.Globalization.CompareOptions.None);
            return _sortAsc ? c2 : -c2;
        });

        return list;
    }

    /// <summary>
    /// 核心渲染方法：根据数据重建所有数据行
    /// 清除现有数据行（保留表头行 row 0），然后逐行渲染
    /// </summary>
    /// <param name="av">要渲染的数据，null 时使用当前页数据</param>
    private void RebuildRows(ArrayValue? av = null)
    {
        av ??= GetPageItems();

        // 移除所有数据行（row >= 1），保留表头行
        for (int i = _innerGrid.Children.Count - 1; i >= 0; i--)
            if (Grid.GetRow(_innerGrid.Children[i]) >= 1)
                _innerGrid.Children.RemoveAt(i);

        // 移除所有数据行定义（保留表头行定义）
        while (_innerGrid.RowDefinitions.Count > 1)
            _innerGrid.RowDefinitions.RemoveAt(1);

        if (av == null) return;

        var rows = GetRows(av);     // 获取排序后的数据行
        var eng = ScriptEngine;     // 脚本引擎引用
        var bld = Builder;          // UI 构建器引用

        // 逐行渲染
        for (int r = 0; r < rows.Count; r++)
        {
            var obj = rows[r];                  // 当前行数据对象
            var dr = r + 1;                     // 数据行索引（row 0 是表头）
            var gIdx = GetGlobalIndex(r);       // 全局索引
            _innerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

            var isSel = IsSelected(gIdx, obj);  // 是否选中

            // 根据奇偶行和选中状态确定背景色
            var bg = isSel
                ? Brush.Parse(_selBg)           // 选中行背景
                : Brush.Parse(r % 2 == 0 ? _rowEvenBg : _rowOddBg);  // 奇偶行背景

            var capR = r;  // 捕获行索引用于事件闭包

            // 渲染复选框列
            if (_hasCheckbox)
            {
                var cb = new CheckBox
                {
                    IsChecked = isSel,          // 绑定选中状态
                    Margin = new Thickness(4, 0)
                };
                cb.IsCheckedChanged += (_, _) => ToggleRow(capR, obj);  // 点击切换选中

                var cbCell = new Border
                {
                    Background = bg,
                    BorderBrush = Brush.Parse(_lineColor),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(4, 3),
                    Child = cb
                };
                Grid.SetRow(cbCell, dr);
                Grid.SetColumn(cbCell, 0);
                _innerGrid.Children.Add(cbCell);
            }

            // 渲染数据列
            for (int c = _dataColOffset; c < _colCount; c++)
            {
                var key = _bindings[c];                                 // 绑定的属性名
                var raw = obj.Properties.TryGetValue(key, out var v) ? v : Value.Null;  // 获取原始值
                var text = raw is StringValue s ? s.Value : raw.AsString();  // 转为显示文本
                var fg = isSel ? Brush.Parse(_selFg) : Brushes.Black;   // 选中时使用特殊颜色

                // ★ 如果该列有自定义模板，尝试使用模板渲染
                if (_templates[c] != null && eng != null && bld != null)
                {
                    try
                    {
                        // 调用模板回调：参数为 (文本值, 数据对象, 行索引)
                        var tr = _templates[c]!.CallAsync(eng,
                            [StringValue.Create(text), obj, NumberValueFactory.Create(capR)])
                            .GetAwaiter().GetResult();

                        // 如果模板返回 ObjectValue，使用 Builder 构建为控件
                        if (tr is ObjectValue td)
                        {
                            var tc = bld.Build(td);
                            var tcell = new Border
                            {
                                Background = bg,
                                BorderBrush = Brush.Parse(_lineColor),
                                BorderThickness = new Thickness(0, 0, 1, 1),
                                Padding = new Thickness(2),
                                Child = tc
                            };

                            // 可选择模式下，点击单元格触发选择
                            if (_selMode != SelectMode.None)
                                tcell.PointerPressed += (_, _) => ToggleRow(capR, obj);

                            Grid.SetRow(tcell, dr);
                            Grid.SetColumn(tcell, c);
                            _innerGrid.Children.Add(tcell);
                            continue;  // 跳过默认渲染
                        }
                    }
                    catch
                    {
                        // 模板执行失败时回退到默认渲染
                    }
                }

                // 默认渲染：TextBlock（只读）或 TextBox（可编辑）
                var cell = new Border
                {
                    Background = bg,
                    BorderBrush = Brush.Parse(_lineColor),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    Padding = new Thickness(4, 3)
                };

                // 可选择模式下，点击单元格触发选择
                if (_selMode != "none")
                    cell.PointerPressed += (_, _) => ToggleRow(capR, obj);

                if (_isReadOnly)
                {
                    // 只读模式：使用 TextBlock
                    cell.Child = new TextBlock
                    {
                        Text = text,
                        FontSize = _cellFontSize,
                        Foreground = fg,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
                else
                {
                    // 可编辑模式：使用 TextBox
                    var tb = new TextBox
                    {
                        Text = text,
                        FontSize = _cellFontSize,
                        Foreground = fg,
                        BorderThickness = new Thickness(0),  // 无边框
                        Padding = new Thickness(2, 0),
                        VerticalContentAlignment = VerticalAlignment.Center
                    };

                    // 捕获变量用于事件闭包
                    var capKey = key;
                    var capObj = obj;
                    var capGIdx = gIdx;
                    var tv = _tableValue;
                    cell.Child = tb;

                    // 文本变化时同步更新数据对象和 TableValue
                    void tb_TextChanged(object? sender, TextChangedEventArgs e)
                    {
                        var old = capObj.Properties[capKey] as StringValue;
                        if(old?.Value == tb.Text)
                        {
                            return;
                        }
                        var nv = StringValue.Create(tb.Text ?? "");
                        capObj.Properties[capKey] = nv;         // 更新对象属性
                        tv?.SetCell(capGIdx, capKey, nv);       // 更新 TableValue
                    }
                    tb.TextChanged += tb_TextChanged;

                }

                Grid.SetRow(cell, dr);
                Grid.SetColumn(cell, c);
                _innerGrid.Children.Add(cell);
            }
        }

        // 渲染完成后构建分页控件并更新表头复选框
        BuildPager();
        UpdateHeaderCheckbox();
    }
}