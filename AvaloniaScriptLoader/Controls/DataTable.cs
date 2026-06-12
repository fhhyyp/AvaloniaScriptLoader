using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using AvaloniaScriptLoader.Model;
using ScriptLang.Runtime;

namespace AvaloniaScriptLoader.Controls;

public class DataTable : Grid
{
    private static class SelectMode
    {
        public static string None = "none";
        public static string Single = "single";
        public static string Multiple = "multiple";
    }
    internal static Builder.ControlBuilder? Builder { get; set; }
    internal static ScriptLang.ScriptEngine? ScriptEngine { get; set; }

    private readonly ScrollViewer _scroller = new()
    {
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto
    };

    private readonly Grid _innerGrid = new();
    private readonly StackPanel _pagerPanel;
    private readonly TextBox _pageInput;
    private readonly TextBlock _pageLabel;
    private int _colCount, _maxCount, _currentPage;
    private string[] _bindings = [];
    private bool _isReadOnly;
    private string? _sortCol;
    private bool _sortAsc = true;
    //private ArrayValue? _allItems;
    private TableValue? _tableValue;
    private string _selMode = SelectMode.None;
    private double _savedCbWidth = 40;
    private HashSet<int> _selected = new();
    private string? _selBinding;
    private int _selOffset, _dataColOffset;
    private bool _hasCheckbox;
    private ICallable?[] _templates = [];
    private bool _updatingAllHeaderCheckbox = false;
    private bool _updatingHeaderCheckbox = false;
    private bool _suppressCellEvent = false;
    // Cached row controls: global data index → rendered controls on current page
    private readonly Dictionary<int, List<Control>> _rowControls = new();

    private string _hdrBg = "#f1f5f9";
    private string _hdrFg = "#475569";
    private string _rowEvenBg = "#ffffff";
    private string _rowOddBg = "#f8fafc";
    private string _lineColor = "#e2e8f0";
    private string _selBg = "#dbeafe";
    private string _selFg = "#1e40af";
    private int _cellFontSize = 12;
    private int _hdrFontSize = 12;

    /// <summary> 索引对照表 </summary>
    private Dictionary<int, int> _indexComparison = [];

    public DataTable()
    {
        _scroller.Content = _innerGrid;

        _pageLabel = new TextBlock
        {
            FontSize = 11,
            Foreground = Brush.Parse("#64748b"),
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 4, 8, 0)
        };

        _pageInput = new TextBox
        {
            Width = 40,
            FontSize = 11,
            Margin = new Thickness(4, 0)
        };

        _pageInput.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter)
            {
                GoToPage();
            }
        };

        _pagerPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 2,
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 2)
        };

        var pr = new Border
        {
            Child = _pagerPanel,
            Padding = new Thickness(4, 0)
        };

        RowDefinitions.Add(new RowDefinition(GridLength.Star));
        RowDefinitions.Add(new RowDefinition(GridLength.Auto));
        Children.Add(_scroller);
        Children.Add(pr);
    }

    protected override Size ArrangeOverride(Size s)
    {
        var ph = _pagerPanel.Children.Count > 0 ? _pagerPanel.DesiredSize.Height : 0;
        _scroller.Arrange(new Rect(new Size(s.Width, s.Height - ph)));
        _pagerPanel.Arrange(new Rect(new Point(s.Width - _pagerPanel.DesiredSize.Width, s.Height - ph), _pagerPanel.DesiredSize));
        return s;
    }

    protected override Size MeasureOverride(Size s)
    {
        _scroller.Measure(s);
        _pagerPanel.Measure(new Size(s.Width, double.PositiveInfinity));
        var ph = _pagerPanel.Children.Count > 0 ? _pagerPanel.DesiredSize.Height : 0;
        return new Size(_scroller.DesiredSize.Width, _scroller.DesiredSize.Height + ph);
    }

    // ═════════ Properties ═════════

    internal void SetColumns(ArrayValue cols)
    {
        BuildColumns(cols);
    }

    /*internal void SetItems(ArrayValue av)
    {
        _allItems = av;
        FullRebuild();
    }*/

    internal void SetTableValue(TableValue tv)
    {
        _tableValue = tv;
        //_allItems = tv.Get();

        tv.RowAdded += OnRowAdded;
        tv.RowRemoved += OnRowRemoved;
        tv.RowReplaced += OnRowReplaced;
        tv.RowMoved += OnRowMoved;
        tv.CellChanged += OnCellChanged;
        tv.CollectionReset += OnReset;

        FullRebuild();
    }

    internal void SetReadOnly(bool ro)
    {
        _isReadOnly = ro;
        FullRebuild();
    }

    internal void SetMaxCount(int n)
    {
        _maxCount = n;
        _currentPage = 0;
        FullRebuild();
    }

    internal void SetCurrentPage(int n)
    {
        if (_tableValue?.Get() == null)
        {
            return;
        }

        _currentPage = Math.Clamp(n, 0, Math.Max(0, TotalPages - 1));
        FullRebuild();
    }

    private int TotalPages => _maxCount <= 0 ? 1 : Math.Max(1, (int)Math.Ceiling((double)_tableValue!.Count / _maxCount));

    internal void SetSelectionMode(string m)
    {
        var prev = _selMode;
        _selMode = m;

        if (m == SelectMode.None|| (m == SelectMode.Single && prev == SelectMode.Multiple))
        {
            ClearAllSelections();
        }

        if (_hasCheckbox && _innerGrid.ColumnDefinitions.Count > 0)
        {
            var d = _innerGrid.ColumnDefinitions[0];

            if (m == SelectMode.None)
            {
                if (d.Width.Value > 0)
                {
                    _savedCbWidth = d.Width.Value;
                }

                d.MaxWidth = 0;
                d.MinWidth = 0;
                d.Width = new GridLength(0);
            }
            else if (prev == SelectMode.None)
            {
                d.MaxWidth = double.PositiveInfinity;
                d.MinWidth = 0;
                d.Width = new GridLength(_savedCbWidth, GridUnitType.Pixel);
            }
        }

        foreach (var ch in _innerGrid.Children)
        {
            if (Grid.GetColumn(ch) == 0 && ch is Border b && b.Child is CheckBox cb)
            {
                if (Grid.GetRow(ch) == 0)
                {
                    cb.IsVisible = m == SelectMode.Multiple;
                }
                else if (Grid.GetRow(ch) > 0)
                {
                    cb.IsVisible = m != SelectMode.None;
                }
            }
        }

        UpdateHeaderCheckbox();
    }

    internal void SetSelectionBinding(string? k)
    {
        _selBinding = k;
        _bindings[0] = k;
        FullRebuild();
    }

    internal void SetSelectionOffset(int o)
    {
        _selOffset = o;
    }

    internal void SetStyle(string n, Value v)
    {
        if (v is not ObjectValue obj)
        {
            return;
        }

        switch (n)
        {
            case "headerStyle":
                if (obj.Properties.TryGetValue("bg", out var hb))
                {
                    _hdrBg = hb.AsString();
                }

                if (obj.Properties.TryGetValue("fg", out var hf))
                {
                    _hdrFg = hf.AsString();
                }

                if (obj.Properties.TryGetValue("fontSize", out var fs))
                {
                    _hdrFontSize = (int)fs.As<double>();
                }
                break;

            case "rowStyle":
                if (obj.Properties.TryGetValue("evenBg", out var eb))
                {
                    _rowEvenBg = eb.AsString();
                }

                if (obj.Properties.TryGetValue("oddBg", out var ob))
                {
                    _rowOddBg = ob.AsString();
                }

                if (obj.Properties.TryGetValue("lineColor", out var lc))
                {
                    _lineColor = lc.AsString();
                }
                break;

            case "cellStyle":
                if (obj.Properties.TryGetValue("fontSize", out var cs))
                {
                    _cellFontSize = (int)cs.As<double>();
                }
                break;

            case "selectedStyle":
                if (obj.Properties.TryGetValue("bg", out var sb))
                {
                    _selBg = sb.AsString();
                }

                if (obj.Properties.TryGetValue("fg", out var sf))
                {
                    _selFg = sf.AsString();
                }
                break;
        }
    }

    // ═════════ Incremental ═════════

    private bool OnPage(int g)
    {
        return _maxCount <= 0 || (g >= _currentPage * _maxCount && g < (_currentPage + 1) * _maxCount);
    }

    private void OnRowAdded(object? _, TableRowEventArgs e)
    {
        //_allItems = _tableValue!.Get();

        foreach (var kv in _rowControls.OrderByDescending(x => x.Key).ToList())
        {
            if (kv.Key >= e.RowIndex)
            {
                _rowControls.Remove(kv.Key);
                _rowControls[kv.Key + 1] = kv.Value;
            }
        }

        if (OnPage(e.RowIndex))
        {
            FullRebuild();
        }
        else
        {
            BuildPager();
        }
    }

    private void OnRowRemoved(object? _, TableRowEventArgs e)
    {
        //_allItems = _tableValue!.Get();

        // 修正当前页码：确保当前页不会超过总页数
        var tp = TotalPages;
        if (_currentPage >= tp)
        {
            _currentPage = Math.Max(0, tp - 1);
        }

        if (OnPage(e.RowIndex))
        {
            FullRebuild();
        }
        else
        {
            foreach (var kv in _rowControls.OrderBy(x => x.Key).ToList())
            {
                if (kv.Key > e.RowIndex)
                {
                    _rowControls.Remove(kv.Key);
                    _rowControls[kv.Key - 1] = kv.Value;
                }
            }

            _rowControls.Remove(e.RowIndex);
            FullRebuild();
            BuildPager();
        }
    }

    private void OnRowReplaced(object? _, TableRowEventArgs e)
    {
        //_allItems = _tableValue!.Get();

        if (OnPage(e.RowIndex))
        {
            FullRebuild();
        }
    }

    private void OnRowMoved(object? _, TableRowEventArgs e)
    {
        //_allItems = _tableValue!.Get();
        FullRebuild();
    }

    private void OnCellChanged(object? _, TableRowEventArgs e)
    {
        // 由选中操作 (SetSelected) 触发的 CellChanged 跳过，避免双重 FullRebuild，但不跳过全选
        if (_suppressCellEvent /*&& !_updatingAllHeaderCheckbox*/) return;
        if (!OnPage(e.RowIndex)) return;
        if (!_indexComparison.TryGetValue(e.RowIndex, out var comparisonControlIndex)) return;

        // 增量更新：仅刷新被修改的那个单元格
        
        if (!_rowControls.TryGetValue(comparisonControlIndex, out var controls)) return;
        if (string.IsNullOrEmpty(e.Key)) return;

        var colIndex = Array.IndexOf(_bindings, e.Key);
        if (colIndex < 0) return;

        // controls 列表布局: [cbCell?, dataCol0_cell, dataCol1_cell, ...]
        var ctrlIndex = (_hasCheckbox ? 1 : 0) + (colIndex - _dataColOffset);
        if (ctrlIndex < 0 || ctrlIndex >= controls.Count) return;

        var cell = controls[ctrlIndex];
        if (cell is Border border && border.Child is Control child)
        {
            var newText = e.NewValue is StringValue s ? s.Value : e.NewValue?.AsString() ?? "";

            if (child is TextBox tb && tb.Text != newText)
                tb.Text = newText;
            else if (child is TextBlock tbk)
                tbk.Text = newText;
            //else if (child is CheckBox checkBox && e.NewValue is BoolValue boolValue)
            //    checkBox.IsChecked = boolValue.Value;
        }
    }

    private void OnReset()
    {
        //_allItems = _tableValue!.Get();
        _currentPage = Math.Min(_currentPage, Math.Max(0, TotalPages - 1));
        FullRebuild();
    }

    // ═════════ Columns ═════════

    private void BuildColumns(ArrayValue cols)
    {
        _innerGrid.ColumnDefinitions.Clear();
        _innerGrid.Children.Clear();
        _innerGrid.RowDefinitions.Clear();
        _innerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        _colCount = cols.Elements.Count;
        _bindings = new string[_colCount];
        _templates = new ICallable?[_colCount];
        _hasCheckbox = false;
        _dataColOffset = 0;
        var colStart = 0;

        if (cols.Elements.Count > 0 && cols.Elements[0] is ObjectValue fc && fc.Properties.TryGetValue("checkbox", out var cbv) && cbv.AsBool())
        {
            _hasCheckbox = true;
            _dataColOffset = 1;
            colStart = 1;

            var ws = fc.Properties.TryGetValue("width", out var wv) ? wv.AsString() : "40";
            _innerGrid.ColumnDefinitions.Add(new ColumnDefinition(GL(ws)));

            var hcb = new CheckBox
            {
                IsEnabled = _selMode != SelectMode.None,
                Margin = new Thickness(4, 0)
            };
            // 全选
            hcb.IsCheckedChanged += (_, _) =>
            {
                if (_updatingHeaderCheckbox) return;  // 跳过由代码触发的事件
                if (_selMode != SelectMode.None)
                {
                    //var o = _suppressCellEvent;
                    //_suppressCellEvent = false;
                    _updatingAllHeaderCheckbox = true;
                    ToggleAll(hcb.IsChecked == true);
                    _updatingAllHeaderCheckbox = false;
                    //_suppressCellEvent = o;
                }
            };

            var bdr = new Border
            {
                Background = Bg(_hdrBg),
                BorderBrush = Bg(_lineColor),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(4, 4),
                Child = hcb
            };

            Grid.SetRow(bdr, 0);
            Grid.SetColumn(bdr, 0);
            _innerGrid.Children.Add(bdr);
        }
        
        for (int c = colStart; c < _colCount; c++)
        {
            if (cols.Elements[c] is not ObjectValue co)
            {
                continue;
            }

            _bindings[c] = co.Properties.TryGetValue("binding", out var bv) ? bv.AsString() : $"col{c}";
            _templates[c] = co.Properties.TryGetValue("template", out var tv) && tv is ICallable tc ? tc : null;

            _innerGrid.ColumnDefinitions.Add(new ColumnDefinition(GL(co.Properties.TryGetValue("width", out var wv2) ? wv2.AsString() : "*")));

            var hdrText = co.Properties.TryGetValue("header", out var hv) ? hv.AsString() : "";
            var capC = c;
            var srt = co.Properties.TryGetValue("sortable", out var sv) && sv.AsBool();

            var arrow = new TextBlock
            {
                FontSize = 10,
                Foreground = Bg("#94a3b8"),
                VerticalAlignment = VerticalAlignment.Center,
                Text = srt ? " ↕" : ""
            };

            var hdr = new Border
            {
                Background = Bg(_hdrBg),
                BorderBrush = Bg(_lineColor),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(6, 4),
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 4,
                    Children =
                    {
                        new TextBlock
                        {
                            Text = hdrText,
                            FontSize = _hdrFontSize,
                            FontWeight = FontWeight.Bold,
                            Foreground = Bg(_hdrFg),
                            VerticalAlignment = VerticalAlignment.Center
                        },
                        arrow
                    }
                }
            };

            if (srt)
            {
                hdr.Cursor = new Cursor(StandardCursorType.Hand);
                // 排序
                hdr.PointerPressed += (_, _) =>
                {
                    if (_sortCol == _bindings[capC])
                    {
                        if (_sortAsc)
                        {
                            _sortAsc = false;
                        }
                        else
                        {
                            _sortCol = null;
                        }
                    }
                    else
                    {
                        _sortCol = _bindings[capC];
                        _sortAsc = true;
                    }

                    UpdateSortArrows();
                    FullRebuild();
                };
            }

            Grid.SetRow(hdr, 0);
            Grid.SetColumn(hdr, c);
            _innerGrid.Children.Add(hdr);
        }
    }

    // ═════════ Row Building ═════════

    private void FullRebuild()
    {
        _rowControls.Clear();

        for (int i = _innerGrid.Children.Count - 1; i >= 0; i--)
        {
            if (Grid.GetRow(_innerGrid.Children[i]) >= 1)
            {
                _innerGrid.Children.RemoveAt(i);
            }
        }

        while (_innerGrid.RowDefinitions.Count > 1)
        {
            _innerGrid.RowDefinitions.RemoveAt(1);
        }

        var av = GetPageItems();
        var rows = GetSorted(av);

        

        var start = _currentPage * _maxCount;

        for (int r = 0; r < rows.Count; r++)
        {
            InsertRow(r, start + r, rows[r]);
        }

        _indexComparison = rows.Select((x, i) => (i, x.Get("__index").As<int>())).ToDictionary(k => k.Item2, v => v.i);

        UpdateSortArrows();
        UpdateHeaderCheckbox();
        BuildPager();
    }

    private void InsertRow(int pageIdx, int gIdx, ObjectValue row)
    {
        var dr = pageIdx + 1;

        while (_innerGrid.RowDefinitions.Count <= dr)
        {
            _innerGrid.RowDefinitions.Insert(_innerGrid.RowDefinitions.Count, new RowDefinition(GridLength.Auto));
        }

        var isSel = IsSelected(gIdx, row);
        var bg = isSel ? Bg(_selBg) : Bg(gIdx % 2 == 0 ? _rowEvenBg : _rowOddBg);
        var controls = new List<Control>();

        if (_hasCheckbox)
        {
            var cb = new CheckBox
            {
                IsChecked = isSel,
                Margin = new Thickness(4, 0)
            };

            cb.IsCheckedChanged += (_, _) =>
            {
                if (_updatingAllHeaderCheckbox) return;
                ToggleRow(gIdx, row);
            };

            var cbCell = new Border
            {
                Background = bg,
                BorderBrush = Bg(_lineColor),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(4, 3),
                Child = cb
            };

            if (_selMode != SelectMode.None)
            {
                cbCell.PointerPressed += (_, _) => ToggleRow(gIdx, row);
            }

            Grid.SetRow(cbCell, dr);
            Grid.SetColumn(cbCell, 0);
            _innerGrid.Children.Add(cbCell);
            controls.Add(cbCell);
        }

        for (int c = _dataColOffset; c < _colCount; c++)
        {
            var key = _bindings[c];
            var raw = row.Properties.TryGetValue(key, out var v) ? v : Value.Null;
            var text = raw is StringValue s ? s.Value : raw.AsString();
            var fg = isSel ? Bg(_selFg) : Bg("#000000");

            Control? child = null;

            if (_templates[c] != null && ScriptEngine != null && Builder != null)
            {
                try
                {
                    var tr = _templates[c]!.CallAsync(ScriptEngine, [StringValue.Create(text), row, NumberValueFactory.Create(pageIdx)]).GetAwaiter().GetResult();

                    if (tr is ObjectValue td)
                    {
                        child = Builder.Build(td);
                    }
                }
                catch
                {
                }
            }

            if (child == null)
            {
                if (_isReadOnly)
                {
                    child = new TextBlock
                    {
                        Text = text,
                        FontSize = _cellFontSize,
                        Foreground = fg,
                        VerticalAlignment = VerticalAlignment.Center
                    };
                }
                else
                {
                    var tb = new TextBox
                    {
                        Text = text,
                        FontSize = _cellFontSize,
                        Foreground = fg,
                        BorderThickness = new Thickness(0),
                        Padding = new Thickness(2, 0),
                        VerticalContentAlignment = VerticalAlignment.Center
                    };

                    var capKey = key;
                    //var capGIdx = gIdx;
                    //var capOld = raw;
                    tb.Tag = row;
                    tb.TextChanged += tb_TextChangedEvent;
                    child = tb;
                    void tb_TextChangedEvent(object? sender, TextChangedEventArgs e)
                    {
                        if (sender is TextBox tb 
                            && tb.Tag is ObjectValue value
                            && value.TryGetValue("__index", out var indexValue)
                            && indexValue is NumberValue<int> index)
                        {
                            var newText = tb.Text ?? "";
                            var oldValue = value.Get(capKey);
                            var oldText = oldValue is StringValue os ? os.Value : raw.AsString();
                            if (newText == oldText)
                            {
                                return;
                            }
                            var newValue = StringValue.Create(newText);
                            _tableValue?.SetCell(index.Value, capKey, oldValue, newValue);
                        }
                        else
                        {
                            return;
                        }
                    }
                }
            }

            var cell = new Border
            {
                Background = bg,
                BorderBrush = Bg(_lineColor),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(4, 3),
                Child = child
            };

            if (_selMode != SelectMode.None)
            {
                cell.PointerPressed += (_, _) => ToggleRow(gIdx, row);
            }

            Grid.SetRow(cell, dr);
            Grid.SetColumn(cell, c);
            _innerGrid.Children.Add(cell);
            controls.Add(cell);
        }

        _rowControls[gIdx] = controls;
    }

    private static IBrush? Bg(string hex)
    {
        return Brush.Parse(hex);
    }

    private static GridLength GL(string w)
    {
        return w == "*"
            ? GridLength.Star
            : w.EndsWith("*") && double.TryParse(w[..^1], out var sw)
                ? new GridLength(sw, GridUnitType.Star)
                : double.TryParse(w, out var pw)
                    ? new GridLength(pw)
                    : GridLength.Star;
    }

    // ═════════ Pagination ═════════

    private ArrayValue GetPageItems()
    {
        var allItems = _tableValue?.Get();
        if (allItems is null || _maxCount <= 0)
        {
            return new ArrayValue([]);
        }

        var s = _currentPage * _maxCount;
        var l = new List<Value>();

        for (int i = s; i < s + _maxCount && i < allItems.Elements.Count; i++)
        {
            l.Add(allItems.Elements[i]);
        }

        return new ArrayValue(l);
    }

    private void BuildPager()
    {
        var allItems = _tableValue?.Get();
        _pagerPanel.Children.Clear();

        if (_maxCount <= 0 || allItems == null)
        {
            return;
        }

        var tp = TotalPages;
        var cp = _currentPage;

        void B(string t, int g, bool e)
        {
            var b = new Button
            {
                Content = t,
                FontSize = 11,
                Padding = new Thickness(6, 2),
                Margin = new Thickness(1),
                IsEnabled = e
            };

            b.Click += (_, _) =>
            {
                _currentPage = g;
                FullRebuild();
            };

            _pagerPanel.Children.Add(b);
        }
        
        B("<<", 0, cp > 0);
        B("<", cp - 1, cp > 0);

        var st = Math.Max(0, cp - 2);
        var en = Math.Min(tp, cp + 3);

        if (st > 0)
        {
            _pagerPanel.Children.Add(new TextBlock
            {
                Text = "...",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2)
            });
        }

        for (int i = st; i < en; i++)
        {
            var cp2 = i;

            var b2 = new Button
            {
                Content = (i + 1).ToString(),
                FontSize = 11,
                Padding = new Thickness(6, 2),
                Margin = new Thickness(1),
                Background = i == cp ? Bg("#6366f1") : Brushes.Transparent,
                Foreground = i == cp ? Brushes.White : Bg("#475569")
            };

            b2.Click += (_, _) =>
            {
                _currentPage = cp2;
                FullRebuild();
            };

            _pagerPanel.Children.Add(b2);
        }

        if (en < tp)
        {
            _pagerPanel.Children.Add(new TextBlock
            {
                Text = "...",
                FontSize = 11,
                VerticalAlignment = VerticalAlignment.Center,
                Margin = new Thickness(2)
            });
        }

        B(">", cp + 1, cp < tp - 1);
        B(">>", tp - 1, cp < tp - 1);

        _pageInput.Text = "";
        _pagerPanel.Children.Add(_pageInput);

        var gb = new Button
        {
            Content = "Go",
            FontSize = 11,
            Padding = new Thickness(4, 2)
        };

        gb.Click += (_, _) => GoToPage();
        _pagerPanel.Children.Add(gb);

        _pageLabel.Text = $"  {allItems.Elements.Count} 条 | {cp + 1}/{tp}";
        _pagerPanel.Children.Add(_pageLabel);
        // 强制重新测量和排列
        _pagerPanel.InvalidateMeasure();
        _pagerPanel.InvalidateArrange();
        InvalidateMeasure();
        InvalidateArrange();
    }

    private void GoToPage()
    {
        if (int.TryParse(_pageInput.Text, out var p) && p >= 1 && p <= TotalPages)
        {
            _currentPage = p - 1;
            FullRebuild();
        }
    }

    // ═════════ Selection ═════════

    private bool IsSelected(int g, ObjectValue? o)
    {
        return _selBinding != null && o != null
            ? o.Properties.TryGetValue(_selBinding, out var b) && b.AsBool()
            : _selected.Contains(g);
    }

    /// <summary>
    /// 增量刷新单行的背景色与前景色（不重建控件）
    /// </summary>
    private void RefreshRowStyle(int gIdx, ObjectValue? row)
    {
        if (!_rowControls.TryGetValue(gIdx, out var controls)) return;

        var isSel = row != null && IsSelected(gIdx, row);
        var bg = isSel ? Bg(_selBg) : Bg(gIdx % 2 == 0 ? _rowEvenBg : _rowOddBg);
        var fg = isSel ? Bg(_selFg) : Bg("#000000");

        foreach (var ctrl in controls)
        {
            if (ctrl is Border border)
            {
                border.Background = bg;
                if (border.Child is TextBlock tbk)
                    tbk.Foreground = fg;
                else if (border.Child is TextBox tb)
                    tb.Foreground = fg;
                //else if (border.Child is CheckBox checkBox )
                //    checkBox.IsChecked = 
            }
        }
    }

    private void SetSelected(int g, ObjectValue? row, bool s)
    {
        if (_selBinding != null && row != null)
        {
            // 兼容行对象尚未初始化 _selBinding 属性的场景（默认为未选中）
            BoolValue capOld;
            if (row.Properties.TryGetValue(_selBinding, out var existing) && existing is BoolValue bv)
            {
                capOld = bv;
                if (capOld.Value == s) return; // 已经是目标状态，跳过
            }
            else
            {
                capOld = BoolValue.False;
            }
            var nv = BoolValue.Create(s);
            row.Properties[_selBinding] = nv;
            _suppressCellEvent = true;
            var index = row.Get("__index").As<int>();
            _tableValue?.SetCell(index, _selBinding, capOld, nv);
            _suppressCellEvent = false;
        }
        else
        {
            if (s)
            {
                _selected.Add(g);
            }
            else
            {
                _selected.Remove(g);
            }
        }
    }

    private bool IsAllSelected()
    {
        var r = GetSorted(GetPageItems());
        var st = _currentPage * _maxCount;

        for (int i = 0; i < r.Count; i++)
        {
            if (!IsSelected(st + i, r[i]))
            {
                return false;
            }
        }

        return r.Count > 0;
    }

    private void ToggleAll(bool s)
    {
        var r = GetSorted(GetPageItems());
        var st = _currentPage * _maxCount;

        for (int i = 0; i < r.Count; i++)
        {
            SetSelected(st + i, r[i], s);
            RefreshRowStyle(st + i, r[i]);
        }

        UpdateHeaderCheckbox();
    }

    private void ToggleRow(int g, ObjectValue o)
    {
        if (_selMode == SelectMode.Single)
        {
            // 先取消旧的选中行
            if (_selBinding != null)
            {
                // 扫描全表找到旧选中行（binding 值为 true 的行），取消选中
                var all = _tableValue?.Get().Elements ?? [];
                for (int i = 0; i < all.Count; i++)
                {
                    if (all[i] is ObjectValue orow
                        && orow.Properties.TryGetValue(_selBinding, out var bv)
                        && bv.AsBool())
                    {
                        SetSelected(i, orow, false);
                        RefreshRowStyle(i, orow);
                        break;
                    }
                }
            }
            else
            {
                foreach (var oldG in _selected.ToList())
                {
                    var oldRow = _tableValue?.Get()?.Elements.ElementAtOrDefault(oldG) as ObjectValue;
                    RefreshRowStyle(oldG, oldRow);
                }
                _selected.Clear();
            }

            SetSelected(g, o, true);
            RefreshRowStyle(g, o);
        }
        else if (_selMode == SelectMode.Multiple)
        {
            SetSelected(g, o, !IsSelected(g, o));
            RefreshRowStyle(g, o);
        }
        else
        {
            return;
        }


        UpdateHeaderCheckbox();
    }

    private void UpdateHeaderCheckbox()
    {
        foreach (var ch in _innerGrid.Children)
        {
            if (Grid.GetColumn(ch) == 0 && Grid.GetRow(ch) == 0
                && ch is Border b && b.Child is CheckBox cb)
            {
                cb.IsEnabled = _selMode != "none";

                _updatingHeaderCheckbox = true;
                cb.IsChecked = IsAllSelected();
                _updatingHeaderCheckbox = false;

                return;
            }
        }
    }

    private void ClearAllSelections()
    {
        _selected.Clear();

        var allItems = _tableValue?.Get();
        if (allItems != null && _selBinding != null)
        {
            for (int g = 0; g < allItems.Elements.Count; g++)
            {
                Value? e = allItems.Elements[g];
                if (e is ObjectValue row)
                {
                    // 兼容行对象尚未初始化 _selBinding 属性的场景（默认为未选中）
                    BoolValue capOld;
                    if (row.Properties.TryGetValue(_selBinding, out var existing) && existing is BoolValue bv)
                    {
                        capOld = bv;
                        if (capOld.Value == false) continue; // 已经是目标状态，跳过
                    }
                    else
                    {
                        capOld = BoolValue.False;
                    }
                    var nv = BoolValue.False;
                    row.Properties[_selBinding] = nv;
                    _suppressCellEvent = true;
                    var index =  row.Get("__index").As<int>();
                    _tableValue?.SetCell(index, _selBinding, capOld, nv);
                    _suppressCellEvent = false;

                    row.Properties[_selBinding!] = BoolValue.False;
                }
            }
        }

        FullRebuild();
    }

    // ═════════ Sorting ═════════

    private void UpdateSortArrows()
    {
        for (int c = _dataColOffset; c < _colCount; c++)
        {
            if (c < _innerGrid.Children.Count && _innerGrid.Children[c] is Border h && h.Child is StackPanel sp && sp.Children.Count > 1 && sp.Children[1] is TextBlock a)
            {
                a.Text = _bindings[c] == _sortCol ? (_sortAsc ? " ▲" : " ▼") : " ↕";
            }
        }
    }

    private List<ObjectValue> GetSorted(ArrayValue? av = null)
    {
        //av ??= GetPageItems();
        av ??= _tableValue?.Get(); 
        var list = new List<ObjectValue>();

        if (av == null)
        {
            return list;
        }
        list = av.Elements.OfType<ObjectValue>().ToList();

        if (_sortCol == null)
        {
            return list;
        }

        var ci = System.Globalization.CultureInfo.CurrentCulture.CompareInfo;

        list.Sort((a, b) =>
        {
            var va = a.Properties.TryGetValue(_sortCol, out var a1) ? a1 : Value.Null;
            var vb = b.Properties.TryGetValue(_sortCol, out var b1) ? b1 : Value.Null;

            if (va.IsNumber && vb.IsNumber)
            {
                var c = va.As<double>().CompareTo(vb.As<double>());
                return _sortAsc ? c : -c;
            }

            return _sortAsc
                ? ci.Compare(va.AsString(), vb.AsString(), System.Globalization.CompareOptions.None)
                : ci.Compare(vb.AsString(), va.AsString(), System.Globalization.CompareOptions.None);
        });

        return list.Take(_maxCount).ToList();
    }
}