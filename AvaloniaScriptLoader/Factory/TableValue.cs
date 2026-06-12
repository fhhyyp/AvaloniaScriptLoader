using System;
using System.Collections.Generic;
using ScriptLang.Runtime;

namespace AvaloniaScriptLoader.Model;

/// <summary>
/// 响应式表格数据 — 类似 InpcValue，封装数组并提供变更通知
/// </summary>
public class TableValue : IDisposable
{
    private readonly List<ObjectValue> _rows = new();
    private readonly List<Action<Value>> _subscribers = new();
    private bool _disposed;

    public int Count => _rows.Count;

    public ObjectValue Table { get; private set; } 

    public TableValue(ArrayValue initial)
    {
        _rows.AddRange(initial.Elements.OfType<ObjectValue>());
        //foreach (var e in initial.Elements)
        //    if (e is ObjectValue obj) _rows.Add(obj);
        Table = WrapTable();
    }

    private ObjectValue WrapTable()
    {
        TableValue tableInstance = this;
        var descriptor = new Dictionary<string, Value>
        {
            [ControlMeta.TypeKey] = StringValue.Create("table"),
            ["__table"] = new ClrObjectValue(tableInstance),
            ["value"] = tableInstance.Get(),
        };

        descriptor["get"] = new FunctionValue("get", () => tableInstance.Get());
        descriptor["set"] = new FunctionValue("set", (List<Value> setArgs) =>
        {
            if (setArgs.Count > 0 && setArgs[0] is ArrayValue newAv) tableInstance.Set(newAv);
            return Value.Null;
        });
        descriptor["addRow"] = new FunctionValue("addRow", (List<Value> a) =>
        {
            if (a.Count > 0 && a[0] is ObjectValue row) tableInstance.AddRow(row);
            return Value.Null;
        });
        descriptor["removeRow"] = new FunctionValue("removeRow", (List<Value> a) =>
        {
            if (a.Count > 0 && a[0].IsNumber)
                tableInstance.RemoveRow(a[0].As<int>());
            return Value.Null;
        });
        descriptor["setCell"] = new FunctionValue("setCell", (List<Value> a) =>
        {
            if (a.Count >= 3 && a[0].IsNumber)
                tableInstance.SetCell(a[0].As<int>(), a[1].AsString(), a[2]);
            return Value.Null;
        });

        return new ObjectValue(descriptor);
    }

    public ArrayValue Get()
    {
        var el = new List<Value>(_rows.Count);
        foreach (var r in _rows) el.Add(r);
        return new ArrayValue(el);
    }

    public void Set(ArrayValue av)
    {
        _rows.Clear();
        foreach (var e in av.Elements) if (e is ObjectValue obj) _rows.Add(obj);
        Notify();
    }

    public void SetCell(int index, string key, Value value)
    { 
        if (index >= 0 && index < _rows.Count) 
        {   
            _rows[index].Properties[key] = value; 
            Notify();
        } 
    }
    public void AddRow(ObjectValue row) 
    { 
        _rows.Add(row); 
        Notify();
    }
    public void RemoveRow(int index) 
    { 
        if (index >= 0 && index < _rows.Count)
        { 
            _rows.RemoveAt(index); 
            Notify();
        } 
    }
    public void Clear() 
    { 
        _rows.Clear();
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
        var s = Table;
        foreach (var cb in _subscribers.ToArray())
        {
            cb(s);
        }
    }
    public void Dispose() { _disposed = true; _rows.Clear(); _subscribers.Clear(); }

    internal void RemoveOnChanged(Action<Value> callback)
    {
        lock (_subscribers)
            _subscribers.Remove(callback);
    }
}
