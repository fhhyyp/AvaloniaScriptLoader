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
    private readonly object _lock = new();
    private readonly HashSet<ComputedValue> _dependents = [];
    /// <summary>对象引用 → 行索引映射（替代 ObjectValue.__index，避免多表共享对象时互相覆盖）</summary>
    private readonly Dictionary<ObjectValue, int> _objectToIndex = [];
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
        for (int i = 0; i < initial.Elements.Count; i++)
        {
            Value? e = initial.Elements[i];
            if (e is ObjectValue obj)
            {
                _objectToIndex[obj] = i;
                _rows.Add(obj);
            }
        }

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
        // 自动注册到 ReactiveTracker，使 computed() 能追踪此 TableValue 为依赖
        ReactiveTracker.Current?.AddTableDependency(this);
        return new ArrayValue(_rows.Select(x => (Value)x).ToList());
    }
    public ObjectValue? GetRow(int index) =>
        index >= 0  && index < _rows.Count ? _rows[index] : null;

    /// <summary>
    /// 获取行对象在当前表中的索引。若对象不在表中返回 -1。
    /// 替代原有的 row.Get("__index") 模式，使用对象引用查找，避免多表共享对象时 __index 被覆盖。
    /// </summary>
    public int GetRowIndex(ObjectValue row)
    {
        lock (_lock) { return _objectToIndex.TryGetValue(row, out var idx) ? idx : -1; }
    }

    private void SetRowIndex(ObjectValue row, int index)
    {
        lock (_lock) { _objectToIndex[row] = index; }
    }

    private void RemoveRowIndex(ObjectValue row)
    {
        lock (_lock) { _objectToIndex.Remove(row); }
    }

    public void Set(ArrayValue av)
    {
        _rows.Clear();
        lock (_lock) { _objectToIndex.Clear(); }
        for (int i = 0; i < av.Elements.Count; i++)
        {
            if (av.Elements[i] is ObjectValue obj)
            {
                _objectToIndex[obj] = i;
                _rows.Add(obj);
            }
        }

        CollectionReset?.Invoke();
        Notify();
    }

    public void AddRow(ObjectValue row)
    {
        _rows.Add(row);
        var index = _rows.Count - 1;
        SetRowIndex(row, index);
        RowAdded?.Invoke(this, new TableRowEventArgs(index, row));
        Notify();
    }

    public void InsertRow(int index, ObjectValue row)
    {
        if (index < 0 || index > _rows.Count) return;
        _rows.Insert(index, row);
        SetRowIndex(row, index);
        for (int i = index + 1; i < _rows.Count; i++)
        {
            SetRowIndex(_rows[i], i);
        }

        RowAdded?.Invoke(this, new TableRowEventArgs(index, row));
        Notify();
    }

    public void RemoveRow(int index)
    {
        if (index < 0 || index >= _rows.Count) return;
        var removed = _rows[index];
        _rows.RemoveAt(index);
        RemoveRowIndex(removed);
        for (int i = index; i < _rows.Count; i++)
        {
            SetRowIndex(_rows[i], i);
        }
        RowRemoved?.Invoke(this, new TableRowEventArgs(index, removed));
        Notify();
    }

    public void ReplaceRow(int index, ObjectValue newRow)
    {
        if (index < 0 || index >= _rows.Count) return;
        var old = _rows[index];
        _rows[index] = newRow;
        RemoveRowIndex(old);
        SetRowIndex(newRow, index);
        RowReplaced?.Invoke(this, new TableRowEventArgs(index, old, newRow));
        Notify();
    }

    public void MoveRow(int oldIndex, int newIndex)
    {
        if (oldIndex < 0 || oldIndex >= _rows.Count
            || newIndex < 0 || newIndex >= _rows.Count)
            return;
        SetRowIndex(_rows[newIndex], oldIndex);
        SetRowIndex(_rows[oldIndex], newIndex);
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
        var t = this._rows;
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

    // ========================================================================
    // ComputedValue 依赖管理（与 InpcValue 同模式）
    // ========================================================================

    internal void AddDependent(ComputedValue cv)
    {
        if (_disposed) return;
        lock (_lock) { _dependents.Add(cv); }
    }

    internal void RemoveDependent(ComputedValue cv)
    {
        lock (_lock) { _dependents.Remove(cv); }
    }

    private void Notify()
    {
        if (_disposed) return;
        var s = Get();

        // 1. 通知 OnChange 订阅者
        Action<Value>[] subs;
        lock (_subscribers) subs = _subscribers.ToArray();
        foreach (var cb in subs) cb(s);

        // 2. 无效化依赖的 ComputedValue（链式传播）
        ComputedValue[] deps;
        lock (_lock) deps = _dependents.ToArray();
        foreach (var cv in deps)
        {
            try { cv.Invalidate(); }
            catch (Exception ex) { Debug.WriteLine($"[TableValue] Computed invalidate error: {ex.Message}"); }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _rows.Clear();
        _subscribers.Clear();

        ComputedValue[] deps;
        lock (_lock) { deps = _dependents.ToArray(); _dependents.Clear(); _objectToIndex.Clear(); }
        foreach (var cv in deps) cv.RemoveTableDependency(this);
    }
}

