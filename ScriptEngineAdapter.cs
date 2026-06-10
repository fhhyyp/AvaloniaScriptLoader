using System.Collections.Concurrent;
using ScriptLang;
using ScriptLang.Runtime;
using AvaloniaScriptLoader.Modules;
using AvaloniaScriptLoader.Wrapper;

namespace AvaloniaScriptLoader;

/// <summary>
/// 脚本引擎适配器 — 管理 ScriptEngine 生命周期、模块注册、脚本执行
/// 参照 ExcelScriptLoader.ScriptEngineAdapter 的集成模式
/// </summary>
public class ScriptEngineAdapter : IDisposable
{
    private ScriptEngine? _engine;
    private bool _disposed;

    /// <summary>控件注册表（用于 app.find 和 setter 回调）</summary>
    private readonly ConcurrentDictionary<string, ControlWrapper> _controlRegistry = new();

    /// <summary>引擎实例</summary>
    public ScriptEngine? Engine => _engine;

    /// <summary>
    /// 初始化引擎、注册内置模块
    /// </summary>
    public void Initialize()
    {
        _engine = new ScriptEngine();
        _controlRegistry.Clear();

        // 注册内置模块
        RegisterBuiltinModules();
    }

    /// <summary>
    /// 执行脚本代码，返回根 ObjectValue 描述符
    /// </summary>
    /// <param name="scriptCode">脚本源代码</param>
    /// <param name="sourceName">源名称（用于错误报告）</param>
    /// <returns>脚本返回的根描述符（通常是 Window ObjectValue）</returns>
    public async Task<ObjectValue> ExecuteAsync(string scriptCode, string sourceName)
    {
        if (_engine == null)
            throw new InvalidOperationException("引擎未初始化，请先调用 Initialize()");

        // 清空上次的编译缓存和控件注册表
        _engine.ClearCache();
        _controlRegistry.Clear();

        // 刷新内置模块（每次执行重新注册以获取最新状态）
        RegisterBuiltinModules();

        // 编译并执行
        var task = _engine.CreateTaskFromSource(scriptCode, sourceName);
        var result = await task.RunAsync();

        // 脚本最后一个表达式是返回值
        if (result is ObjectValue obj)
            return obj;

        throw new InvalidOperationException(
            $"脚本必须返回一个控件描述符（ObjectValue），但返回了: {result?.GetType().Name ?? "null"}");
    }

    /// <summary>
    /// 注册命名控件（用于 app.find）
    /// </summary>
    public void RegisterControl(string name, ControlWrapper wrapper)
    {
        _controlRegistry[name] = wrapper;
    }

    /// <summary>
    /// 按名称查找控件
    /// </summary>
    public ControlWrapper? FindControl(string name)
    {
        _controlRegistry.TryGetValue(name, out var wrapper);
        return wrapper;
    }

    // ==================== 内部方法 ====================

    /// <summary>
    /// 注册所有内置模块到 ImportResolver
    /// </summary>
    private void RegisterBuiltinModules()
    {
        if (_engine == null) return;

        // 注册 "avalonia" 系统模块
        var avaloniaExports = AvaloniaModule.CreateExports(this);
        _engine.ImportResolver.RegisterBuiltinModule("avalonia", avaloniaExports);

        // 注册 "avalonia.controls" 控件模块
        var controlsExports = ControlsModule.CreateExports();
        _engine.ImportResolver.RegisterBuiltinModule("avalonia.controls", controlsExports);
    }

    /// <summary>
    /// 释放所有资源
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _engine?.ClearCache();
        _controlRegistry.Clear();
        _engine = null;
    }
}
