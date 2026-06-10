using Avalonia.Threading;
using ScriptLang.Runtime;

namespace AvaloniaScriptLoader.Modules;

/// <summary>
/// "avalonia" 内置模块 — 提供系统级 API
/// 脚本用法: import { app } from "avalonia"
/// </summary>
public static class AvaloniaModule
{
    /// <summary>
    /// 创建模块导出对象
    /// </summary>
    public static ObjectValue CreateExports(ScriptEngineAdapter adapter)
    {
        var properties = new Dictionary<string, Value>
        {
            ["app"] = CreateAppObject(adapter),
        };

        return new ObjectValue(properties);
    }

    /// <summary>
    /// 创建 app 对象（showMessage / log / find）
    /// </summary>
    private static ObjectValue CreateAppObject(ScriptEngineAdapter adapter)
    {
        return new ObjectValue(new Dictionary<string, Value>
        {
            // === showMessage(text) ===
            ["showMessage"] = new FunctionValue("showMessage", args =>
            {
                var text = args.FirstOrDefault()?.AsString() ?? "";

                // 在 UI 线程显示消息框
                Dispatcher.UIThread.Post(() =>
                {
                    // 使用 Avalonia 内置消息框或简单 Console 输出
                    System.Diagnostics.Debug.WriteLine($"[Script Message] {text}");
                });
            }),

            // === log(text) ===
            ["log"] = new FunctionValue("log", args =>
            {
                var text = args.FirstOrDefault()?.AsString() ?? "";
                System.Diagnostics.Debug.WriteLine($"[Script] {text}");
            }),

            // === find(name) → ObjectValue ===
            ["find"] = new FunctionValue("find", args =>
            {
                var name = args.FirstOrDefault()?.AsString() ?? "";
                var wrapper = adapter.FindControl(name);
                return wrapper?.Descriptor ?? Value.Null;
            }),
        });
    }
}
