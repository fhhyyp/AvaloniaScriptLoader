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

    private readonly object _lock = new();

    // UI 订阅者（lock 保护）
    private readonly List<Action<Value>> _subscribers = [];

    // 依赖追踪（lock 保护）
    private readonly HashSet<InpcValue> _inpcDeps = [];
    private readonly HashSet<ComputedValue> _computedDeps = [];
    private readonly HashSet<ComputedValue> _dependents = [];

    public int DependencyCount { get { lock (_lock) return _inpcDeps.Count + _computedDeps.Count; } }

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

    internal void RemoveInpcDependency(InpcValue inpc)
    {
        lock (_lock) { _inpcDeps.Remove(inpc); }
    }

    internal void Invalidate()
    {
        if (_disposed || _dirty) return;
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

    // ========================================================================
    // IDisposable
    // ========================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        InpcValue[] inpcDeps;
        ComputedValue[] compDeps;
        lock (_lock)
        {
            inpcDeps = _inpcDeps.ToArray();
            compDeps = _computedDeps.ToArray();
            _inpcDeps.Clear();
            _computedDeps.Clear();
            _subscribers.Clear();
            _dependents.Clear();
        }

        foreach (var d in inpcDeps) d.RemoveDependent(this);
        foreach (var d in compDeps) lock (d._lock) { d._dependents.Remove(this); }

        Log.Debug($"[ComputedValue] Disposed: {inpcDeps.Length} inpc + {compDeps.Length} computed deps");
    }

    // ========================================================================
    // 内部
    // ========================================================================

    private void Recompute()
    {
        // 清空旧依赖
        InpcValue[] oldInpc;
        ComputedValue[] oldComp;
        lock (_lock)
        {
            oldInpc = _inpcDeps.ToArray();
            oldComp = _computedDeps.ToArray();
            _inpcDeps.Clear();
            _computedDeps.Clear();
        }
        foreach (var d in oldInpc) d.RemoveDependent(this);
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
