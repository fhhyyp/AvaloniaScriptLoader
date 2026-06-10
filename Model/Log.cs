using System.Runtime.CompilerServices;

namespace AvaloniaScriptLoader.Model;

/// <summary>
/// 轻量日志抽象 — 生产环境可替换为 ILogger 实现
/// Debug 构建输出到 Debug.WriteLine，Release 构建可由宿主注入自定义 Action
/// </summary>
public static class Log
{
    /// <summary>宿主可注入的日志输出（替换默认 Debug.WriteLine）</summary>
    public static Action<string>? OnLog { get; set; }

    /// <summary>宿主可注入的错误输出</summary>
    public static Action<string>? OnError { get; set; }

    public static void Debug(string message, [CallerMemberName] string caller = "")
    {
#if DEBUG
        System.Diagnostics.Debug.WriteLine(message);
#endif
        OnLog?.Invoke(message);
    }

    public static void Info(string message)
    {
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[INFO] {message}");
#endif
        OnLog?.Invoke($"[INFO] {message}");
    }

    public static void Warn(string message)
    {
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[WARN] {message}");
#endif
        OnLog?.Invoke($"[WARN] {message}");
    }

    public static void Error(string message)
    {
#if DEBUG
        System.Diagnostics.Debug.WriteLine($"[ERROR] {message}");
#endif
        OnError?.Invoke($"[ERROR] {message}");
    }

    /// <summary>
    /// 脚本执行错误（带脚本名和行号的结构化信息）
    /// </summary>
    public static void ScriptError(string scriptName, int line, int col, string message)
    {
        var formatted = $"[SCRIPT_ERROR] {scriptName}:{line}:{col} — {message}";
#if DEBUG
        System.Diagnostics.Debug.WriteLine(formatted);
#endif
        OnError?.Invoke(formatted);
    }
}
