using ScriptLang.Runtime;
using AvaloniaScriptLoader.Model;

namespace AvaloniaScriptLoader.Factory;

/// <summary>
/// 响应式值工厂 — 创建 inpc() / computed() / table() 包装对象。
///
/// 所有包装器直接返回 ClrObjectValue，脚本侧方法/属性通过
/// Prototype 扩展机制（InpcPrototype / TablePrototype）注入，
/// 不再手动构建 FunctionValue 字典。
/// </summary>
public static class InpcFactory
{
    // ========================================================================
    // inpc(initialValue, mode?) — 可观察值
    // ========================================================================

    /// <summary>
    /// 创建 inpc() 脚本函数。
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

            return new ClrObjectValue(inpcInstance);
        });
    }

    // ========================================================================
    // computed(() => expr) — 计算属性
    // ========================================================================

    /// <summary>
    /// 创建 computed() 脚本函数。
    /// 用法: var fullName = computed(() => { return a.get() + b.get() })
    /// </summary>
    public static FunctionValue CreateComputedFunction()
    {
        return new FunctionValue("computed", (engine, args) =>
        {
            var func = args.FirstOrDefault();
            if (func is not ICallable callable)
                throw new ArgumentException("computed() 需要一个函数参数，例如: computed(() => { ... })");

            // 缓存 VM 实例复用
            ScriptLang.Runtime.ByteCode.VM? cachedVm = null;

            var computedInstance = new ComputedValue(() =>
            {
                if (callable is ScriptLang.Runtime.CompiledFunctionValue cfv)
                {
                    cachedVm ??= new ScriptLang.Runtime.ByteCode.VM(engine);
                    var valueTask = cachedVm.InvokeCompiledFunctionAsync(cfv, []);
                    return valueTask.GetAwaiter().GetResult();
                }
                var task = callable.CallAsync(engine, []);
                return task.GetAwaiter().GetResult();
            });

            return new ClrObjectValue(computedInstance);
        });
    }

    // ========================================================================
    // table(initialArray, sourceTable?) — 响应式表格数组
    // ========================================================================

    public static FunctionValue CreateTableFunction()
    {
        return new FunctionValue("table", args =>
        {
            if (args.Count < 1 || args[0] is not ArrayValue av)
                throw new ArgumentException("table() 期望 1 个数组参数");

            var t = new TableValue(av);

            // 可选第 2 参数：源表引用，用于派生表编辑时向源表传播（双向同步）
            if (args.Count > 1)
            {
                t.SourceTable = ExtractTable(args[1]);
            }

            return t.Table;
        });
    }

    // ========================================================================
    // 提取方法（从 ClrObjectValue 直接读取，不再经过 ObjectValue 包装层）
    // ========================================================================

    /// <summary>从 ClrObjectValue 包装中提取 TableValue</summary>
    public static TableValue? ExtractTable(Value value)
    {
        return (value as ClrObjectValue)?.Value as TableValue;
    }

    /// <summary>从 ClrObjectValue 包装中提取 InpcValue</summary>
    public static InpcValue? ExtractInpc(Value value)
    {
        return (value as ClrObjectValue)?.Value as InpcValue;
    }

    /// <summary>从 ClrObjectValue 包装中提取 ComputedValue</summary>
    public static ComputedValue? ExtractComputed(Value value)
    {
        return (value as ClrObjectValue)?.Value as ComputedValue;
    }

    /// <summary>判断 Value 是否为可观察包装（InpcValue / ComputedValue / TableValue）</summary>
    public static bool IsObservableWrapper(Value value)
    {
        return value is ClrObjectValue clr
            && clr.Value is InpcValue or ComputedValue or TableValue;
    }

    /// <summary>判断 InpcValue 是否为双向绑定模式</summary>
    public static bool IsTwoWay(Value value)
    {
        return (value as ClrObjectValue)?.Value is InpcValue { IsTwoWay: true };
    }
}
