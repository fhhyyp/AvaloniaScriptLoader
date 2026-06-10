using ScriptLang.Runtime;
using AvaloniaScriptLoader.Model;

namespace AvaloniaScriptLoader.Factory;

/// <summary>
/// 结构化指令工厂 — v-if / v-for / component 等模板指令
///
/// 每个指令返回特殊的 ObjectValue 描述符，由 ControlBuilder 识别并处理。
/// </summary>
public static class StructureFactory
{
    // ========================================================================
    // vif(condition, element) — 条件渲染
    // ========================================================================

    /// <summary>
    /// 创建 vif() 脚本函数
    /// 用法: vif(showWarning, label({"text" = "警告!"}))
    ///
    /// 返回描述符:
    ///   { __type: "vif", __condition: observable, __element: childDesc }
    /// </summary>
    public static FunctionValue CreateVifFunction()
    {
        return new FunctionValue("vif", (engine, args) =>
        {
            if (args.Count < 2)
                throw new ArgumentException("vif() 需要两个参数: vif(condition, element)");

            var condition = args[0];
            var element = args[1];

            // condition 必须是可观察对象（inpc / computed）
            if (!InpcFactory.IsObservableWrapper(condition))
                throw new ArgumentException("vif() 的第一个参数必须是 inpc() 或 computed()");

            // element 必须是控件描述符
            if (element is not ObjectValue)
                throw new ArgumentException("vif() 的第二个参数必须是一个控件");

            return new ObjectValue(new Dictionary<string, Value>
            {
                [ControlMeta.TypeKey] = StringValue.Create("vif"),
                ["__condition"] = condition,
                ["__element"] = element,
            });
        });
    }

    /// <summary>
    /// 判断 Value 是否为 vif 描述符
    /// </summary>
    public static bool IsVif(Value value)
    {
        return value is ObjectValue obj
            && obj.Properties.TryGetValue(ControlMeta.TypeKey, out var type)
            && type.AsString() == "vif";
    }

    // ========================================================================
    // vfor(array, templateFn) — 列表渲染
    // ========================================================================

    /// <summary>
    /// 创建 vfor() 脚本函数 — 立即求值返回 ArrayValue（放在 children 中）。
    /// 用法: vfor(items, (item, index) => element)
    ///
    /// 初始渲染由脚本时求值保证（全局变量可用），
    /// 数据变更后的刷新由脚本事发者通过重新调用 vfor 手动触发。
    /// </summary>
    public static FunctionValue CreateVforFunction()
    {
        return new FunctionValue("vfor", (engine, args) =>
        {
            if (args.Count < 2)
                throw new ArgumentException("vfor() 需要两个参数: vfor(array, (item, index) => element)");

            var arrayWrapper = args[0];
            var template = args[1] as ICallable
                ?? throw new ArgumentException("vfor() 的第二个参数必须是一个函数");

            var inpc = InpcFactory.ExtractInpc(arrayWrapper);
            var computed = InpcFactory.ExtractComputed(arrayWrapper);
            Value arrayVal = inpc?.Get() ?? computed?.Get() ?? Value.Null;

            if (arrayVal is not ArrayValue av)
                return new ArrayValue([]);

            var result = new List<Value>();
            for (int i = 0; i < av.Elements.Count; i++)
            {
                try
                {
                    var task = template.CallAsync(engine, [av.Elements[i], NumberValueFactory.Create(i)]);
                    var rendered = task.GetAwaiter().GetResult();
                    if (rendered is not NullValue)
                        result.Add(rendered);
                }
                catch (Exception ex)
                {
                    Log.Warn($"[vfor] template failed index={i}: {ex.Message}");
                }
            }
            return new ArrayValue(result);
        });
    }

    public static bool IsVfor(Value value) => false; // 不再使用旧格式

    // ========================================================================
    // component(renderFn) — 组件定义
    // ========================================================================

    /// <summary>
    /// 创建 component() 脚本函数。
    /// 用法: let FormField = component((props) => { return stackpanel({...}) })
    ///       FormField({"label" = "姓:", "value" = firstName})
    ///
    /// 返回一个 FunctionValue，调用时传递 props ObjectValue，返回渲染结果。
    /// </summary>
    public static FunctionValue CreateComponentFunction()
    {
        return new FunctionValue("component", (engine, args) =>
        {
            var renderFn = args.FirstOrDefault();
            if (renderFn is not ICallable renderCallable)
                throw new ArgumentException("component() 需要一个函数参数: component((props) => { ... })");

            // 返回一个 FunctionValue 作为组件工厂
            // 调用时传入 props ObjectValue，渲染并返回结果
            return new FunctionValue("<component>", (compEngine, compArgs) =>
            {
                var props = compArgs.FirstOrDefault() as ObjectValue
                    ?? new ObjectValue([]);

                try
                {
                    var task = renderCallable.CallAsync(compEngine, [props]);
                    return task.GetAwaiter().GetResult();
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine(
                        $"[Component] 渲染异常: {ex.Message}");
                    return Value.Null;
                }
            });
        });
    }
}
