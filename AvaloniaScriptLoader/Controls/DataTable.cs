using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using ScriptLang.Runtime;

namespace AvaloniaScriptLoader.Controls;

/// <summary>
/// 数据表格控件 — Grid + ScrollViewer 组合，支持 inpc 响应式数据绑定、可编辑单元格
///
/// 用法:
///   datatable({
///       height = 300,
///       columns = [{header="姓名", binding="name", width="120"}, ...],
///       items = inpc([{name="张三",...}, ...], "twoway"),
///   })
/// </summary>
public class DataTable : Grid
{
    private readonly ScrollViewer _scroller;
    private readonly Grid _innerGrid;
    private int _columnCount;
    private string[] _bindings = [];

    public DataTable()
    {
        _scroller = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
        };
        _innerGrid = new Grid();
        _scroller.Content = _innerGrid;

        RowDefinitions.Add(new RowDefinition(GridLength.Star));
        Children.Add(_scroller);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        _scroller.Arrange(new Rect(finalSize));
        return finalSize;
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        _scroller.Measure(availableSize);
        return _scroller.DesiredSize;
    }

    internal void SetColumns(ArrayValue cols)
    {
        _columnCount = cols.Elements.Count;
        _bindings = new string[_columnCount];
        _innerGrid.ColumnDefinitions.Clear();

        for (int c = 0; c < _columnCount; c++)
        {
            if (cols.Elements[c] is not ObjectValue colObj) continue;
            var w = colObj.Properties.TryGetValue("width", out var wv) ? wv.AsString() : "*";
            _bindings[c] = colObj.Properties.TryGetValue("binding", out var bv) ? bv.AsString() : $"col{c}";

            _innerGrid.ColumnDefinitions.Add(new ColumnDefinition(
                w == "*" ? GridLength.Star : new GridLength(double.Parse(w))));

            // Header
            var header = colObj.Properties.TryGetValue("header", out var hv) ? hv.AsString() : "";
            var headerLabel = new TextBlock
            {
                Text = header,
                FontSize = 12,
                FontWeight = FontWeight.Bold,
                Foreground = Brush.Parse("#475569"),
                Margin = new Thickness(4, 6),
            };
            Grid.SetRow(headerLabel, 0);
            Grid.SetColumn(headerLabel, c);
            _innerGrid.Children.Add(headerLabel);
        }

        // Header separator
        var sep = new Border
        {
            Height = 1,
            Background = Brush.Parse("#e2e8f0"),
        };
        Grid.SetRow(sep, 1);
        Grid.SetColumnSpan(sep, _columnCount);
        _innerGrid.Children.Add(sep);
        _innerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));  // header
        _innerGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));  // separator
    }

    internal void SetItems(ArrayValue av)
    {
        // Remove old data rows (keep header row 0 + separator row 1)
        for (int i = _innerGrid.Children.Count - 1; i >= 0; i--)
        {
            var child = _innerGrid.Children[i];
            var row = Grid.GetRow(child);
            if (row > 1)
                _innerGrid.Children.RemoveAt(i);
        }
        // Remove old row defs
        while (_innerGrid.RowDefinitions.Count > 2)
            _innerGrid.RowDefinitions.RemoveAt(_innerGrid.RowDefinitions.Count - 1);

        // Build rows
        for (int r = 0; r < av.Elements.Count; r++)
        {
            if (av.Elements[r] is not ObjectValue obj) continue;
            var rowDef = new RowDefinition(GridLength.Auto);
            _innerGrid.RowDefinitions.Add(rowDef);
            var dataRow = r + 2; // offset for header + separator

            // Row background
            var bg = r % 2 == 0 ? Brush.Parse("#ffffff") : Brush.Parse("#f8fafc");
            var rowBg = new Border
            {
                Background = bg,
            };
            Grid.SetRow(rowBg, dataRow);
            Grid.SetColumnSpan(rowBg, _columnCount);
            _innerGrid.Children.Add(rowBg);

            // Cells
            for (int c = 0; c < _columnCount; c++)
            {
                var key = _bindings[c];
                var rawValue = obj.Properties.TryGetValue(key, out var v) ? v : Value.Null;
                var cellValue = rawValue is StringValue s ? s.Value
                    : rawValue.IsNumber ? rawValue.AsString()
                    : rawValue.AsString();

                var tb = new TextBox
                {
                    Text = cellValue,
                    FontSize = 12,
                    Margin = new Thickness(4),
                };
                Grid.SetRow(tb, dataRow);
                Grid.SetColumn(tb, c);

                // Capture key + obj for twoway writeback
                var capturedKey = key;
                var capturedObj = obj;
                tb.TextChanged += (_, _) =>
                {
                    capturedObj.Properties[capturedKey] = StringValue.Create(tb.Text ?? "");
                };

                _innerGrid.Children.Add(tb);
            }
        }
    }
}
