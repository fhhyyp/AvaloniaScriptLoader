using ScriptLang.Runtime;
using AvaloniaScriptLoader.Model;

namespace AvaloniaScriptLoader.Factory;

/// <summary>
/// InpcValue 工厂 — 创建脚本侧可用的 inpc() 包装对象
///
/// 脚本调用 inpc(initialValue) 返回的 ObjectValue 结构:
/// {
///     "__type": "inpc",
///     "__inpc": ClrObjectValue(InpcValue 实例),
///     "value":  <当前值>,
///     "get":    FunctionValue → 返回当前值,
///     "set":    FunctionValue → 设置新值 + 通知订阅者,
///     "update": FunctionValue → 原地更新 + 通知,
/// }
/// </summary>
public static class InpcFactory
{
    /// <summary>
    /// 创建 inpc() 脚本函数
    /// 用法: import { inpc } from "avalonia"
    ///       var name = inpc("初始值")
    /// </summary>
    public static FunctionValue CreateInpcFunction()
    {
        return new FunctionValue("inpc", args =>
        {
            var initialValue = args.FirstOrDefault() ?? Value.Null;
            var inpcInstance = new InpcValue(initialValue);

            return WrapInpc(inpcInstance);
        });
    }

    /// <summary>
    /// 将 InpcValue 实例包装为脚本可用的 ObjectValue
    /// </summary>
    public static ObjectValue WrapInpc(InpcValue inpcInstance)
    {
        var descriptor = new Dictionary<string, Value>
        {
            [ControlMeta.TypeKey] = StringValue.Create("inpc"),
            ["__inpc"] = new ClrObjectValue(inpcInstance),
            ["value"] = inpcInstance.Get(),
        };

        // get() → 返回当前值
        descriptor["get"] = new FunctionValue("get",
            () => inpcInstance.Get());

        // set(newValue) → 设置新值 + 通知订阅者
        descriptor["set"] = new FunctionValue("set", args =>
        {
            var newValue = args.FirstOrDefault() ?? Value.Null;
            inpcInstance.Set(newValue);
            // 同步更新 ObjectValue 中的 value 属性
            descriptor["value"] = newValue;
        });

        // update(fn) → 原地更新（用于数值递增等场景）
        // 用法: count.update(v => v + 1)
        // 注意：脚本 Lambda 返回 CompiledFunctionValue，不能直接作为 Func<Value,Value>
        // 因此 update 主要通过 set + get 组合实现
        // 简化版：update 接受新值，等同 set
        descriptor["update"] = new FunctionValue("update", args =>
        {
            var newValue = args.FirstOrDefault() ?? Value.Null;
            inpcInstance.Set(newValue);
            descriptor["value"] = newValue;
        });

        return new ObjectValue(descriptor);
    }

    /// <summary>
    /// 判断一个 Value 是否为 inpc 包装对象
    /// </summary>
    public static bool IsInpcWrapper(Value value)
    {
        return value is ObjectValue obj
            && obj.Properties.TryGetValue(ControlMeta.TypeKey, out var type)
            && type.AsString() == "inpc"
            && obj.Properties.ContainsKey("__inpc");
    }

    /// <summary>
    /// 从 inpc 包装对象中提取 InpcValue 实例
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
}
