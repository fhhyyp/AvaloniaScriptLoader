using ScriptLang.Runtime;
using AvaloniaScriptLoader.Model;

namespace AvaloniaScriptLoader.Factory;

/// <summary>
/// 响应式值工厂 — 创建 inpc() / computed() / twoway 包装对象
///
/// 脚本侧 ObjectValue 结构:
///   inpc:  { __type:"inpc",  __inpc:ClrObjectValue(InpcValue),      get, set }
///   computed: { __type:"computed", __computed:ClrObjectValue(ComputedValue), get }
/// </summary>
public static class InpcFactory
{
    // ========================================================================
    // inpc(initialValue, mode?) — 可观察值
    // ========================================================================

    /// <summary>
    /// 创建 inpc() 脚本函数
    /// 用法: var name = inpc("初始值")
    ///       var name = inpc("初始值", "twoway")  // 双向绑定
    /// </summary>
    public static FunctionValue CreateInpcFunction()
    {
        return new FunctionValue("inpc", args =>
        {
            var initialValue = args.FirstOrDefault() ?? Value.Null;
            bool isTwoWay = args.Count > 1 && args[1].AsString() == "twoway";

            var inpcInstance = new InpcValue(initialValue)
            {
                IsTwoWay = isTwoWay
            };

            return WrapInpc(inpcInstance, isTwoWay);
        });
    }

    // ========================================================================
    // computed(() => expr) — 计算属性
    // ========================================================================

    /// <summary>
    /// 创建 computed() 脚本函数
    /// 用法: var fullName = computed(() => { return a.get() + b.get() })
    ///
    /// 使用 SyncResultFull 构造函数以获取 ScriptEngine 引用，
    /// ComputedValue 在依赖变更时通过 engine 重新调用脚本 Lambda。
    /// </summary>
    public static FunctionValue CreateComputedFunction()
    {
        return new FunctionValue("computed", (engine, args) =>
        {
            // computed 接收一个无参 Lambda
            var func = args.FirstOrDefault();
            if (func is not ICallable callable)
                throw new ArgumentException("computed() 需要一个函数参数，例如: computed(() => { ... })");

            // 创建 ComputedValue，compute 委托持有 engine 引用以重新执行 Lambda
            var computedInstance = new ComputedValue(() =>
            {
                // 脚本 Lambda 执行是同步的（无 I/O），.Result 不会死锁
                var task = callable.CallAsync(engine, []);
                return task.GetAwaiter().GetResult();
            });

            return WrapComputed(computedInstance);
        });
    }

    // ========================================================================
    // 包装方法
    // ========================================================================

    /// <summary>
    /// 将 InpcValue 包装为脚本可用的 ObjectValue
    /// </summary>
    public static ObjectValue WrapInpc(InpcValue inpcInstance, bool isTwoWay = false)
    {
        var descriptor = new Dictionary<string, Value>
        {
            [ControlMeta.TypeKey] = StringValue.Create(isTwoWay ? "inpc_twoway" : "inpc"),
            ["__inpc"] = new ClrObjectValue(inpcInstance),
            ["value"] = inpcInstance.Get(),
        };

        // get() → 返回当前值
        descriptor["get"] = new FunctionValue("get",
            () => inpcInstance.Get());

        // set(newValue) → 设置新值 + 通知
        descriptor["set"] = new FunctionValue("set", args =>
        {
            var newValue = args.FirstOrDefault() ?? Value.Null;
            inpcInstance.Set(newValue);
            descriptor["value"] = newValue;
        });

        return new ObjectValue(descriptor);
    }

    /// <summary>
    /// 将 ComputedValue 包装为脚本可用的 ObjectValue（只读，无 set）
    /// </summary>
    public static ObjectValue WrapComputed(ComputedValue computedInstance)
    {
        var descriptor = new Dictionary<string, Value>
        {
            [ControlMeta.TypeKey] = StringValue.Create("computed"),
            ["__computed"] = new ClrObjectValue(computedInstance),
            ["value"] = computedInstance.Get(),
        };

        // get() → 返回当前计算值（触发依赖追踪）
        descriptor["get"] = new FunctionValue("get",
            () => computedInstance.Get());

        // 无 set() —— computed 是只读的

        return new ObjectValue(descriptor);
    }

    // ========================================================================
    // 检测与提取（供 PropertyBinder 使用）
    // ========================================================================

    /// <summary>
    /// 判断 Value 是否为可观察包装（inpc / inpc_twoway / computed）
    /// </summary>
    public static bool IsObservableWrapper(Value value)
    {
        if (value is not ObjectValue obj) return false;
        if (!obj.Properties.TryGetValue(ControlMeta.TypeKey, out var type)) return false;

        var typeStr = type.AsString();
        return typeStr is "inpc" or "inpc_twoway" or "computed";
    }

    /// <summary>
    /// 判断是否为双向绑定模式
    /// </summary>
    public static bool IsTwoWay(Value value)
    {
        if (value is ObjectValue obj
            && obj.Properties.TryGetValue(ControlMeta.TypeKey, out var type))
        {
            return type.AsString() == "inpc_twoway";
        }
        return false;
    }

    /// <summary>
    /// 从包装对象中提取 InpcValue（inpc / inpc_twoway）
    /// </summary>
    public static InpcValue? ExtractInpc(Value value)
    {
        if (value is ObjectValue obj
            && obj.Properties.TryGetValue("__inpc", out var inpcValue)
            && inpcValue is ClrObjectValue clr
            && clr.Value is InpcValue inpc)
        {
            return inpc;
        }
        return null;
    }

    /// <summary>
    /// 从包装对象中提取 ComputedValue
    /// </summary>
    public static ComputedValue? ExtractComputed(Value value)
    {
        if (value is ObjectValue obj
            && obj.Properties.TryGetValue("__computed", out var computedValue)
            && computedValue is ClrObjectValue clr
            && clr.Value is ComputedValue cv)
        {
            return cv;
        }
        return null;
    }
}
