using ScriptLang.Runtime;
using AvaloniaScriptLoader.Model;

namespace AvaloniaScriptLoader.Factory;

/// <summary>
/// 样式复用系统 — 模拟 CSS class
///
/// 脚本用法:
///   style("primary", {"background" = "#1976D2", "color" = "white", "fontSize" = 14})
///   button({"text" = "提交", "class" = "primary"})
///
/// PropertyBinder 遇到 "class" 属性时自动展开已注册样式。
/// </summary>
public static class StyleFactory
{
    /// <summary>全局样式注册表: className → properties</summary>
    private static readonly Dictionary<string, Dictionary<string, Value>> _styles = new();

    /// <summary>
    /// 创建 style() 脚本函数
    /// </summary>
    public static FunctionValue CreateStyleFunction()
    {
        return new FunctionValue("style", args =>
        {
            var name = args.FirstOrDefault()?.AsString() ?? "";
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("style() 需要样式名: style(\"primary\", {...})");

            if (args.Count > 1 && args[1] is ObjectValue props)
            {
                var dict = new Dictionary<string, Value>();
                foreach (var kv in props.Properties)
                    dict[kv.Key] = kv.Value;
                _styles[name] = dict;
                Log.Debug($"[Style] Registered '{name}': {dict.Count} properties");
            }
        });
    }

    /// <summary>
    /// 获取已注册的样式属性
    /// </summary>
    public static Dictionary<string, Value>? GetStyle(string name)
    {
        _styles.TryGetValue(name, out var style);
        return style;
    }

    /// <summary>
    /// 展开 "class" 属性，将样式属性合并到描述符中（不覆盖已有属性）
    /// </summary>
    public static void ExpandClassProperty(Dictionary<string, Value> descriptor)
    {
        if (!descriptor.TryGetValue("class", out var classValue)) return;

        // 支持单个 class 或多个（空格分隔）
        var classNames = classValue.AsString()?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        foreach (var name in classNames)
        {
            var style = GetStyle(name);
            if (style == null) continue;
            foreach (var kv in style)
            {
                // 不覆盖用户显式设置的属性
                if (!descriptor.ContainsKey(kv.Key))
                    descriptor[kv.Key] = kv.Value;
            }
        }
    }
}
