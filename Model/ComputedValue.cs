using ScriptLang.Runtime;

namespace AvaloniaScriptLoader.Model;

/// <summary>
/// 计算属性 — 模拟 Vue computed()
///
/// 自动追踪依赖（InpcValue + ComputedValue），依赖变更时自动重算并通知订阅者。
/// 支持嵌套 ComputedValue（computed 内部读取另一个 computed）。
///
/// 脚本用法:
///   var fullName = computed(() => {
///       return firstName.get() + lastName.get()
///   })
///   var greeting = computed(() => {
///       return "你好 " + fullName.get()  // 嵌套 computed 依赖 ✅
///   })
/// </summary>
public class ComputedValue
{
    private readonly Func<Value> _compute;
    private Value _cachedValue = Value.Null;
    private bool _dirty = true;
    private bool _firstRun = true;

    // UI / PropertyBinder 订阅者
    private readonly List<Action<Value>> _subscribers = [];

    // 依赖：当前追踪到的 InpcValue
    private readonly HashSet<InpcValue> _inpcDeps = [];
    // 依赖：当前追踪到的 ComputedValue（支持嵌套 computed）
    private readonly HashSet<ComputedValue> _computedDeps = [];
    // 反向依赖：依赖此 ComputedValue 的其他 ComputedValue
    private readonly HashSet<ComputedValue> _dependents = [];

    /// <summary>依赖数量（调试用）</summary>
    public int DependencyCount => _inpcDeps.Count + _computedDeps.Count;

    public ComputedValue(Func<Value> compute)
    {
        _compute = compute ?? throw new ArgumentNullException(nameof(compute));
    }

    /// <summary>
    /// 获取当前计算值。首次调用或 dirty 时触发求值。
    /// 自动注册到 ReactiveTracker（若有正在求值的父 ComputedValue）。
    /// </summary>
    public Value Get()
    {
        // === 关键修复：ComputedValue → ComputedValue 依赖追踪 ===
        // 若当前有正在求值的 ComputedValue（ReactiveTracker.Current），
        // 将 this 注册为该父 ComputedValue 的依赖。
        // 这样当 this 变更时，父 ComputedValue 会被自动 Invalidated。
        ReactiveTracker.Current?.AddComputedDependency(this);

        if (_dirty)
            Recompute();
        return _cachedValue;
    }

    /// <summary>
    /// 注册变更回调（由 PropertyBinder 调用）
    /// </summary>
    public void OnChange(Action<Value> callback)
    {
        _subscribers.Add(callback);
        if (_firstRun)
        {
            _firstRun = false;
            Get(); // 触发首次求值，建立依赖图
        }
    }

    /// <summary>
    /// 移除变更回调
    /// </summary>
    public void RemoveCallback(Action<Value> callback)
    {
        _subscribers.Remove(callback);
    }

    // ========================================================================
    // 依赖管理（内部，由 ReactiveTracker 驱动）
    // ========================================================================

    /// <summary>InpcValue.Get() 调用此方法注册依赖</summary>
    internal void AddInpcDependency(InpcValue inpc)
    {
        _inpcDeps.Add(inpc);
        inpc.AddDependent(this);
    }

    /// <summary>ComputedValue.Get() 调用此方法注册 Computed→Computed 依赖</summary>
    internal void AddComputedDependency(ComputedValue cv)
    {
        if (cv == this) return; // 防止自依赖
        _computedDeps.Add(cv);
        cv._dependents.Add(this);
    }

    /// <summary>InpcValue.Set() / ComputedValue.Invalidate() 调用</summary>
    internal void Invalidate()
    {
        if (_dirty) return;
        _dirty = true;

        Recompute();          // 重新求值
        NotifySubscribers();  // 通知 UI（PropertyBinder）

        // 链式无效化：通知依赖此 ComputedValue 的其他 ComputedValue
        foreach (var dep in _dependents.ToArray())
            dep.Invalidate();
    }

    // ========================================================================
    // 内部方法
    // ========================================================================

    /// <summary>
    /// 重新求值：清空旧依赖 → Push tracker → 运行 compute → 收集新依赖 → Pop tracker
    /// </summary>
    private void Recompute()
    {
        // 1. 清空旧依赖关系
        foreach (var dep in _inpcDeps)
            dep.RemoveDependent(this);
        _inpcDeps.Clear();

        foreach (var dep in _computedDeps)
            dep._dependents.Remove(this);
        _computedDeps.Clear();

        // 2. 将自己推入追踪栈（使 InpcValue.Get() / ComputedValue.Get() 能发现当前求值上下文）
        ReactiveTracker.Push(this);

        try
        {
            _cachedValue = _compute();
            _dirty = false;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine(
                $"[ComputedValue] 求值异常: {ex.Message}");
            _cachedValue = Value.Null;
        }
        finally
        {
            // 3. 从追踪栈弹出
            ReactiveTracker.Pop();
        }
    }

    private void NotifySubscribers()
    {
        var snapshot = _subscribers.ToArray();
        foreach (var cb in snapshot)
        {
            try { cb(_cachedValue); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[ComputedValue] 通知异常: {ex.Message}");
            }
        }
    }

    public override string ToString()
        => $"ComputedValue(inpcDeps={_inpcDeps.Count}, computedDeps={_computedDeps.Count}, dirty={_dirty})";
}
