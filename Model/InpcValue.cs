using ScriptLang.Runtime;

namespace AvaloniaScriptLoader.Model;

/// <summary>
/// 可观察值 — 模拟 INotifyPropertyChanged 接口
///
/// 包装一个 Value，当值通过 Set() 变更时自动通知：
///   1. UI 订阅者（通过 OnChange 注册）
///   2. 依赖的 ComputedValue（自动追踪，链式失效）
///
/// 脚本用法:
///   var name = inpc("张三")
///   label({"text" = name})        // 自动绑定
///   name.set("李四")              // 值变更 → UI 自动更新
/// </summary>
public class InpcValue
{
    private Value _value;

    // UI / PropertyBinder 订阅者
    private readonly List<Action<Value>> _subscribers = [];

    // 依赖此 InpcValue 的 ComputedValue（自动追踪）
    private readonly HashSet<ComputedValue> _dependents = [];

    /// <summary>是否为双向绑定模式</summary>
    public bool IsTwoWay { get; init; }

    /// <summary>
    /// 创建可观察值
    /// </summary>
    /// <param name="initialValue">初始值</param>
    public InpcValue(Value initialValue)
    {
        _value = initialValue;
    }

    /// <summary>
    /// 获取当前值。
    /// 若在 ComputedValue 求值期间调用，自动建立依赖关系。
    /// </summary>
    public Value Get()
    {
        // 依赖追踪：若当前有正在求值的 ComputedValue，注册依赖
        var current = ReactiveTracker.Current;
        current?.AddInpcDependency(this);

        return _value;
    }

    /// <summary>
    /// 设置新值并通知所有订阅者和依赖的 ComputedValue。
    /// </summary>
    /// <param name="newValue">新值</param>
    /// <param name="forceNotify">强制通知（即使值相同）</param>
    public void Set(Value newValue, bool forceNotify = false)
    {
        bool changed = forceNotify || !ValuesEqual(_value, newValue);
        _value = newValue;

        if (changed)
        {
            NotifyAll();
        }
    }

    /// <summary>
    /// 注册变更回调（UI 层订阅）。回调在 Set() 的调用线程上执行。
    /// </summary>
    public void OnChange(Action<Value> callback)
    {
        _subscribers.Add(callback);
    }

    /// <summary>
    /// 移除变更回调
    /// </summary>
    public void RemoveCallback(Action<Value> callback)
    {
        _subscribers.Remove(callback);
    }

    /// <summary>获取订阅者数量（调试用）</summary>
    public int SubscriberCount => _subscribers.Count;

    // ========================================================================
    // ComputedValue 依赖管理（内部，由 ReactiveTracker 驱动）
    // ========================================================================

    internal void AddDependent(ComputedValue cv) => _dependents.Add(cv);
    internal void RemoveDependent(ComputedValue cv) => _dependents.Remove(cv);

    // ========================================================================
    // 内部方法
    // ========================================================================

    /// <summary>
    /// 通知所有订阅者 + 无效化依赖的 ComputedValue
    /// </summary>
    private void NotifyAll()
    {
        // 1. 通知 UI 订阅者（PropertyBinder 回调）
        var uiSnapshot = _subscribers.ToArray();
        foreach (var cb in uiSnapshot)
        {
            try { cb(_value); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[InpcValue] 通知 UI 异常: {ex.Message}");
            }
        }

        // 2. 无效化依赖的 ComputedValue（自动触发链式更新）
        var depSnapshot = _dependents.ToArray();
        foreach (var cv in depSnapshot)
        {
            try { cv.Invalidate(); }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[InpcValue] 无效化 Computed 异常: {ex.Message}");
            }
        }
    }

    /// <summary>
    /// 值相等判断。对于不可变类型进行值比较，
    /// 对于可变类型（ArrayValue/ObjectValue）始终返回 false（不等）。
    /// </summary>
    private static bool ValuesEqual(Value a, Value b)
    {
        if (a.IsArray || b.IsArray) return false;
        if (a.IsObject || b.IsObject) return false;
        if (a.IsNull && b.IsNull) return true;

        if (a.IsNumber && b.IsNumber)
        {
            try
            {
                double da = a.As<double>();
                double db = b.As<double>();
                return Math.Abs(da - db) < 1e-10;
            }
            catch { return false; }
        }
        if (a.IsString && b.IsString)
            return a.AsString() == b.AsString();
        if (a.IsBool && b.IsBool)
            return a.AsBool() == b.AsBool();

        return false;
    }
}
