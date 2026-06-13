using ScriptLang.Runtime;

namespace AvaloniaScriptLoader.Model;

/// <summary>
/// 计算属性 — 模拟 Vue computed()（线程安全）
///
/// 自动追踪依赖（InpcValue + ComputedValue），依赖变更时自动重算并通知订阅者。
/// 支持嵌套 ComputedValue。
/// </summary>
public class ComputedValue : IDisposable
{
    private readonly Func<Value> _compute;
    private Value _cachedValue = Value.Null;
    private bool _dirty = true;
    private bool _firstRun = true;
    private bool _disposed;
    private bool _invalidating;

    private readonly object _lock = new();

    // UI 订阅者（lock 保护）
    private readonly List<Action<Value>> _subscribers = [];

    // 依赖追踪（lock 保护）
    private readonly HashSet<InpcValue> _inpcDeps = [];
    private readonly HashSet<TableValue> _tableDeps = [];
    private readonly HashSet<ComputedValue> _computedDeps = [];
    private readonly HashSet<ComputedValue> _dependents = [];

    public int DependencyCount { get { lock (_lock) return _inpcDeps.Count + _tableDeps.Count + _computedDeps.Count; } }

    public ComputedValue(Func<Value> compute)
    {
        _compute = compute ?? throw new ArgumentNullException(nameof(compute));
    }

    /// <summary>
    /// 获取当前计算值。自动注册到 ReactiveTracker。
    /// </summary>
    public Value Get()
    {
        if (_disposed) return Value.Null;
        ReactiveTracker.Current?.AddComputedDependency(this);

        if (_dirty)
            Recompute();
        return _cachedValue;
    }

    public void OnChange(Action<Value> callback)
    {
        if (_disposed) return;
        lock (_lock) { _subscribers.Add(callback); }
        if (_firstRun)
        {
            _firstRun = false;
            Get();
        }
    }

    public void RemoveCallback(Action<Value> callback)
    {
        lock (_lock) { _subscribers.Remove(callback); }
    }

    // ========================================================================
    // 依赖管理
    // ========================================================================

    internal void AddInpcDependency(InpcValue inpc)
    {
        if (_disposed) return;
        lock (_lock) { _inpcDeps.Add(inpc); }
        inpc.AddDependent(this);
    }

    internal void AddComputedDependency(ComputedValue cv)
    {
        if (_disposed || cv == this) return;
        lock (_lock) { _computedDeps.Add(cv); }
        lock (cv._lock) { cv._dependents.Add(this); }
    }

    internal void AddTableDependency(TableValue tv)
    {
        if (_disposed) return;
        lock (_lock) { _tableDeps.Add(tv); }
        tv.AddDependent(this);
    }

    internal void RemoveInpcDependency(InpcValue inpc)
    {
        lock (_lock) { _inpcDeps.Remove(inpc); }
    }

    internal void RemoveTableDependency(TableValue tv)
    {
        lock (_lock) { _tableDeps.Remove(tv); }
    }

    internal void Invalidate()
    {
        // _invalidating 防重入（替代 _dirty 检查：即使上次 Recompute 失败导致 _dirty 残留，
        // 仍必须通知订阅者和传播链式失效，否则 UI 永久不刷新）
        if (_disposed || _invalidating) return;
        _invalidating = true;
        try
        {
            _dirty = true;
            Recompute();

            // 通知 UI 订阅者
            Action<Value>[] snapshot;
            lock (_lock) { snapshot = _subscribers.ToArray(); }
            foreach (var cb in snapshot)
            {
                try { cb(_cachedValue); }
                catch (Exception ex) { Log.Error($"[ComputedValue] Notify error: {ex.Message}"); }
            }

            // 链式无效化依赖的 ComputedValue
            ComputedValue[] depSnapshot;
            lock (_lock) { depSnapshot = _dependents.ToArray(); }
            foreach (var dep in depSnapshot)
            {
                try { dep.Invalidate(); }
                catch (Exception ex) { Log.Error($"[ComputedValue] Chain error: {ex.Message}"); }
            }
        }
        finally
        {
            _invalidating = false;
        }
    }

    // ========================================================================
    // IDisposable
    // ========================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        InpcValue[] inpcDeps;
        TableValue[] tableDeps;
        ComputedValue[] compDeps;
        lock (_lock)
        {
            inpcDeps = _inpcDeps.ToArray();
            tableDeps = _tableDeps.ToArray();
            compDeps = _computedDeps.ToArray();
            _inpcDeps.Clear();
            _tableDeps.Clear();
            _computedDeps.Clear();
            _subscribers.Clear();
            _dependents.Clear();
        }

        foreach (var d in inpcDeps) d.RemoveDependent(this);
        foreach (var d in tableDeps) d.RemoveDependent(this);
        foreach (var d in compDeps) lock (d._lock) { d._dependents.Remove(this); }

        Log.Debug($"[ComputedValue] Disposed: {inpcDeps.Length} inpc + {tableDeps.Length} table + {compDeps.Length} computed deps");
    }

    // ========================================================================
    // 内部
    // ========================================================================

    private void Recompute()
    {
        // 清空旧依赖
        InpcValue[] oldInpc;
        TableValue[] oldTable;
        ComputedValue[] oldComp;
        lock (_lock)
        {
            oldInpc = _inpcDeps.ToArray();
            oldTable = _tableDeps.ToArray();
            oldComp = _computedDeps.ToArray();
            _inpcDeps.Clear();
            _tableDeps.Clear();
            _computedDeps.Clear();
        }
        foreach (var d in oldInpc) d.RemoveDependent(this);
        foreach (var d in oldTable) d.RemoveDependent(this);
        foreach (var d in oldComp) lock (d._lock) { d._dependents.Remove(this); }

        // 求值（追踪栈）
        ReactiveTracker.Push(this);
        try
        {
            _cachedValue = _compute();
            _dirty = false;
        }
        catch (Exception ex)
        {
            Log.Error($"[ComputedValue] Evaluate error: {ex.Message}");
            // 保持旧值（stale-while-revalidate）
        }
        finally
        {
            ReactiveTracker.Pop();
        }
    }
}
