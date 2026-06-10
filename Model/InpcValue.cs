using ScriptLang.Runtime;

namespace AvaloniaScriptLoader.Model;

/// <summary>
/// 可观察值 — 模拟 INotifyPropertyChanged 接口（线程安全）
///
/// 包装一个 Value，当值通过 Set() 变更时自动通知：
///   1. UI 订阅者（通过 OnChange 注册）
///   2. 依赖的 ComputedValue（自动追踪，链式失效）
/// </summary>
public class InpcValue : IDisposable
{
    private Value _value;
    private readonly object _lock = new();
    private bool _disposed;

    // UI / PropertyBinder 订阅者（lock 保护）
    private readonly List<Action<Value>> _subscribers = [];

    // 依赖此 InpcValue 的 ComputedValue（lock 保护）
    private readonly HashSet<ComputedValue> _dependents = [];

    public bool IsTwoWay { get; init; }
    public bool IsDisposed => _disposed;

    public InpcValue(Value initialValue)
    {
        _value = initialValue;
    }

    /// <summary>
    /// 获取当前值。若在 ComputedValue 求值期间调用，自动建立依赖关系。
    /// </summary>
    public Value Get()
    {
        if (_disposed) return Value.Null;
        ReactiveTracker.Current?.AddInpcDependency(this);
        return _value;
    }

    /// <summary>
    /// 设置新值并通知所有订阅者和依赖的 ComputedValue。
    /// </summary>
    public void Set(Value newValue, bool forceNotify = false)
    {
        if (_disposed) return;

        bool changed = forceNotify || !ValuesEqual(_value, newValue);
        _value = newValue;

        if (changed)
            NotifyAll();
    }

    /// <summary>
    /// 注册变更回调。回调在 Set() 的调用线程上执行。
    /// </summary>
    public void OnChange(Action<Value> callback)
    {
        if (_disposed) return;
        lock (_lock) { _subscribers.Add(callback); }
    }

    /// <summary>
    /// 移除变更回调
    /// </summary>
    public void RemoveCallback(Action<Value> callback)
    {
        lock (_lock) { _subscribers.Remove(callback); }
    }

    public int SubscriberCount { get { lock (_lock) return _subscribers.Count; } }

    // ========================================================================
    // ComputedValue 依赖管理
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

    // ========================================================================
    // IDisposable
    // ========================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Action<Value>[] subs;
        ComputedValue[] deps;
        lock (_lock)
        {
            subs = _subscribers.ToArray();
            deps = _dependents.ToArray();
            _subscribers.Clear();
            _dependents.Clear();
        }

        // 断开 ComputedValue 的反向引用
        foreach (var cv in deps)
            cv.RemoveInpcDependency(this);

        Log.Debug($"[InpcValue] Disposed: {subs.Length} subscribers, {deps.Length} dependents");
    }

    // ========================================================================
    // 内部
    // ========================================================================

    private void NotifyAll()
    {
        // 快照在锁内获取，回调在锁外执行（防止死锁）
        Action<Value>[] uiSnapshot;
        ComputedValue[] depSnapshot;
        lock (_lock)
        {
            uiSnapshot = _subscribers.ToArray();
            depSnapshot = _dependents.ToArray();
        }

        // 1. 通知 UI 订阅者
        foreach (var cb in uiSnapshot)
        {
            try { cb(_value); }
            catch (Exception ex) { Log.Error($"[InpcValue] UI notify error: {ex.Message}"); }
        }

        // 2. 无效化依赖的 ComputedValue
        foreach (var cv in depSnapshot)
        {
            try { cv.Invalidate(); }
            catch (Exception ex) { Log.Error($"[InpcValue] Computed invalidate error: {ex.Message}"); }
        }
    }

    private static bool ValuesEqual(Value a, Value b)
    {
        if (a.IsArray || b.IsArray) return false;
        if (a.IsObject || b.IsObject) return false;
        if (a.IsNull && b.IsNull) return true;

        if (a.IsNumber && b.IsNumber)
        {
            try { return Math.Abs(a.As<double>() - b.As<double>()) < 1e-10; }
            catch { return false; }
        }
        if (a.IsString && b.IsString) return a.AsString() == b.AsString();
        if (a.IsBool && b.IsBool) return a.AsBool() == b.AsBool();
        return false;
    }
}
