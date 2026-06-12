using Avalonia.Controls;
using Avalonia.Input;
using ScriptLang.Runtime;
using AvaloniaScriptLoader.Builder;
using AvaloniaScriptLoader.Model;

namespace AvaloniaScriptLoader.Factory;

/// <summary>
/// 样式复用系统 — 模拟 CSS class + 伪类（:pointerover / :focus / :disabled / :pressed）
///
/// 脚本用法:
///   style("btn", {background = "#6366f1", color = "white"})
///   style("btn:pointerover", {background = "#818cf8"})
///   style("btn:disabled", {opacity = 0.5})
///   button({text = "提交", class = "btn"})
/// </summary>
public static class StyleFactory
{
    /// <summary>全局样式注册表: name → properties</summary>
    private static readonly Dictionary<string, Dictionary<string, Value>> _styles = new();

    /// <summary>跟踪每个控件当前激活的伪类集合</summary>
    private static readonly Dictionary<Control, HashSet<string>> _activePseudos = new();

    // ========================================================================
    // 注册 style() 函数
    // ========================================================================

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

    // ========================================================================
    // 获取已注册样式
    // ========================================================================

    public static Dictionary<string, Value>? GetStyle(string name)
    {
        _styles.TryGetValue(name, out var style);
        return style;
    }

    // ========================================================================
    // 展开 "class" → 静态属性合并（Phase 0：Build 时调用）
    // ========================================================================

    public static void ExpandClassProperty(Dictionary<string, Value> descriptor)
    {
        if (!descriptor.TryGetValue("class", out var classValue)) return;

        var classNames = classValue.AsString()?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        foreach (var name in classNames)
        {
            var style = GetStyle(name);
            if (style == null) continue;
            foreach (var kv in style)
            {
                if (!descriptor.ContainsKey(kv.Key))
                    descriptor[kv.Key] = kv.Value;
            }
        }
    }

    // ========================================================================
    // 展开伪类样式（Phase 1：Build 后调用，注册事件驱动的属性切换）
    // ========================================================================

    /// <summary>
    /// 为控件注册伪类样式的事件监听。Build 流程中在 RegisterEvents 之后调用。
    ///
    /// 伪类优先级（低→高）：base &lt; 用户显式属性 &lt; pointerover &lt; focus &lt; pressed &lt; disabled
    /// </summary>
    internal static void ExpandPseudoClassStyles(Control control, ObjectValue descriptor, PropertyBinder binder)
    {
        if (!descriptor.Properties.TryGetValue("class", out var classValue)) return;

        var classNames = classValue.AsString()?.Split(' ', StringSplitOptions.RemoveEmptyEntries) ?? [];
        if (classNames.Length == 0) return;

        // ── 收集所有匹配 class:pseudo 的样式 ──
        var pseudoStyles = new Dictionary<string, Dictionary<string, Dictionary<string, Value>>>();
        foreach (var cn in classNames)
        {
            var prefix = cn + ":";
            foreach (var kv in _styles)
            {
                if (!kv.Key.StartsWith(prefix)) continue;
                var pseudo = kv.Key.Substring(prefix.Length);
                if (!pseudoStyles.ContainsKey(cn))
                    pseudoStyles[cn] = new Dictionary<string, Dictionary<string, Value>>();
                pseudoStyles[cn][pseudo] = kv.Value;
            }
        }

        if (pseudoStyles.Count == 0) return;

        // ── 去重：所有需要监听的伪类 ──
        var allPseudos = new HashSet<string>();
        foreach (var cn in pseudoStyles.Values)
            foreach (var p in cn.Keys)
                allPseudos.Add(p);

        if (!_activePseudos.ContainsKey(control))
            _activePseudos[control] = new HashSet<string>();
        var activeSet = _activePseudos[control];

        // ── 核心：重新计算最终样式并应用 ──
        void Reapply()
        {
            // 第 1 层：从 descriptor 还原 base + 用户属性
            foreach (var dkv in descriptor.Properties)
            {
                var k = dkv.Key;
                if (k.StartsWith("__") || PropertyNamesBase.IsEventOrSetter(k) || k == "class") continue;
                if (InpcFactory.IsObservableWrapper(dkv.Value)) continue; // 保留 computed/inpc 绑定
                binder.SetControlProperty(control, k, dkv.Value);
            }

            // 第 2 层：按优先级叠加激活的伪类样式
            var ordered = new[] { "pointerover", "focus", "pressed", "disabled" };
            foreach (var pseudo in ordered)
            {
                if (!activeSet.Contains(pseudo)) continue;
                foreach (var cn in classNames)
                {
                    if (!pseudoStyles.TryGetValue(cn, out var cnPseudos)) continue;
                    if (!cnPseudos.TryGetValue(pseudo, out var props)) continue;
                    foreach (var pv in props)
                        binder.SetControlProperty(control, pv.Key, pv.Value);
                }
            }
        }

        // ── 注册对应的事件 ──

        if (allPseudos.Contains("pointerover"))
        {
            control.PointerEntered += (_, _) => { activeSet.Add("pointerover"); Reapply(); };
            control.PointerExited  += (_, _) => { activeSet.Remove("pointerover"); Reapply(); };
        }

        if (allPseudos.Contains("focus"))
        {
            control.GotFocus  += (_, _) => { activeSet.Add("focus"); Reapply(); };
            control.LostFocus += (_, _) => { activeSet.Remove("focus"); Reapply(); };
        }

        if (allPseudos.Contains("pressed"))
        {
            control.PointerPressed  += (_, e) =>
            {
                if (e.GetCurrentPoint(control).Properties.IsLeftButtonPressed)
                    { activeSet.Add("pressed"); Reapply(); }
            };
            control.PointerReleased += (_, _) => { activeSet.Remove("pressed"); Reapply(); };
        }

        if (allPseudos.Contains("disabled"))
        {
            control.PropertyChanged += (_, e) =>
            {
                if (e.Property == InputElement.IsEnabledProperty)
                {
                    if (control.IsEnabled) activeSet.Remove("disabled");
                    else activeSet.Add("disabled");
                    Reapply();
                }
            };
            // 初始状态
            if (!control.IsEnabled) { activeSet.Add("disabled"); Reapply(); }
        }
    }

    // ========================================================================
    // 辅助：属性名分类（内联版，避免依赖 PropertyNames）
    // ========================================================================
    private static class PropertyNamesBase
    {
        public static bool IsEventOrSetter(string name) =>
            name switch
            {
                "onClick" or "onChange" or "onSelect" or "onLoad" or "onClose" or
                "onKeyDown" or "onFocus" or "onBlur" => true,
                _ => name.StartsWith("on") && name.Length > 2 && char.IsUpper(name[2])
                     || name.StartsWith("set") && name.Length > 3 && char.IsUpper(name[3])
                     || name == "set",
            };
    }
}
