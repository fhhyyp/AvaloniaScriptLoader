using System;
using System.Collections.Generic;
using System.Diagnostics;
using ScriptLang.Runtime;

namespace AvaloniaScriptLoader.Model;

/// <summary>行变更事件参数</summary>
public class TableRowEventArgs : EventArgs
{
    public int RowIndex { get; set; }
    public ObjectValue? Row { get; set; }
    public ObjectValue? OldRow { get; set; }
    public int OldIndex { get; set; }
    public int NewIndex { get; set; }
    public string? Key { get; set; }
    public Value? OldValue { get; set; }
    public Value? NewValue { get; set; }

    public TableRowEventArgs() { }
    public TableRowEventArgs(int index, ObjectValue? row) { RowIndex = index; Row = row; }
    public TableRowEventArgs(int index, ObjectValue? oldRow, ObjectValue? newRow) { RowIndex = index; OldRow = oldRow; Row = newRow; }
}

/// <summary>响应式表格数据</summary>
public class TableValue : IDisposable
{
    private readonly List<ObjectValue> _rows = new();
    private readonly List<Action<Value>> _subscribers = new();
    private bool _disposed;

    // 类型化事件
    public event EventHandler<TableRowEventArgs>? RowAdded;
    public event EventHandler<TableRowEventArgs>? RowRemoved;
    public event EventHandler<TableRowEventArgs>? RowReplaced;
    public event EventHandler<TableRowEventArgs>? RowMoved;
    public event EventHandler<TableRowEventArgs>? CellChanged;
    public event Action? CollectionReset;

    public int Count => _rows.Count;

    public ObjectValue Table { get; private set; }

    public TableValue(ArrayValue initial) 
    { 
        foreach (var e in initial.Elements) 
            if (e is ObjectValue obj) _rows.Add(obj);
        Table = WrapTable(this);
    }

    private static ObjectValue WrapTable(TableValue tableInstance)
    {
        var d = new Dictionary<string, Value> 
        { 
            [ControlMeta.TypeKey] = StringValue.Create("table"), 
            ["__table"] = new ClrObjectValue(tableInstance), 
            ["value"] = tableInstance.Get(),
            ["get"] = new FunctionValue("get", tableInstance.Get),
            ["set"] = new FunctionValue("set", (List<Value> a) =>
            {
                if (a.Count > 0 && a[0] is ArrayValue av)
                    tableInstance.Set(av);
            }),
            ["addRow"] = new FunctionValue("addRow", (List<Value> a) =>
            {
                if (a.Count > 0 && a[0] is ObjectValue r)
                    tableInstance.AddRow(r);
            }),
            ["removeRow"] = new FunctionValue("removeRow", (List<Value> a) =>
            {
                if (a.Count > 0 && a[0].IsNumber)
                    tableInstance.RemoveRow(a[0].As<int>());
            }),
        };
       
        return new ObjectValue(d);
    }

    public ArrayValue Get() 
    { 
        var el = new List<Value>(_rows.Count); 
        foreach (var r in _rows) 
            el.Add(r); 
        return new ArrayValue(el); 
    }
    public ObjectValue? GetRow(int index) => 
        index >= 0  && index < _rows.Count ? _rows[index] : null;

    public void Set(ArrayValue av)
    {
        _rows.Clear();
        foreach (var e in av.Elements) if (e is ObjectValue obj) 
            _rows.Add(obj);
        CollectionReset?.Invoke();
        Notify();
    }

    public void AddRow(ObjectValue row) 
    { 
        _rows.Add(row);
        RowAdded?.Invoke(this, new TableRowEventArgs(_rows.Count - 1, row)); 
        Notify();
    }

    public void InsertRow(int index, ObjectValue row)
    {
        if (index < 0 || index > _rows.Count) return;
        _rows.Insert(index, row); 
        RowAdded?.Invoke(this, new TableRowEventArgs(index, row));
        Notify(); 
    }

    public void RemoveRow(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        var removed = _rows[index]; _rows.RemoveAt(index);
        RowRemoved?.Invoke(this, new TableRowEventArgs(index, removed)); 
        Notify();
    }

    public void ReplaceRow(int index, ObjectValue newRow)
    {
        if (index < 0 || index >= _rows.Count) return;
        var old = _rows[index]; _rows[index] = newRow;
        RowReplaced?.Invoke(this, new TableRowEventArgs(index, old, newRow)); 
        Notify();
    }

    public void MoveRow(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= _rows.Count
            || newIndex < 0 || newIndex >= _rows.Count) 
            return;
        var row = _rows[oldIndex]; 
        _rows.RemoveAt(oldIndex); 
        _rows.Insert(newIndex, row);
        RowMoved?.Invoke(this, new TableRowEventArgs(newIndex, row)
        { 
            OldIndex = oldIndex, 
            NewIndex = newIndex 
        });
        Notify();
    }

    public void SetCell(int index, string key, Value oldValue, Value newValue)
    {
        Debug.WriteLine($"{index} {key} {oldValue} {newValue}");
        if (index < 0 || index >= _rows.Count) return;
        _rows[index].Properties[key] = newValue;
        CellChanged?.Invoke(this, new TableRowEventArgs(index, _rows[index]) 
        {
            Key = key, 
            OldValue = oldValue, 
            NewValue = newValue 
        });
        Notify();
    }

    public void OnChange(Action<Value> callback) 
    { 
        lock (_subscribers) 
            _subscribers.Add(callback); 
    }

    private void Notify() 
    {
        if (_disposed) return; 
        var s = Get(); 
        foreach (var cb in _subscribers) 
            cb(s);
    }

    public void Dispose()
    { 
        _disposed = true;
        _rows.Clear(); 
        _subscribers.Clear();
    }
}

