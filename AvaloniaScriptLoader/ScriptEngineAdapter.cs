using System.Collections.Concurrent;
using System.Diagnostics;
using ScriptLang;
using ScriptLang.Runtime;
using AvaloniaScriptLoader.Model;
using AvaloniaScriptLoader.Modules;
using AvaloniaScriptLoader.Prototype;
using AvaloniaScriptLoader.Wrapper;

namespace AvaloniaScriptLoader;

/// <summary>
/// 脚本执行结果
/// </summary>
public class ScriptResult
{
    public bool Success { get; init; }
    public ObjectValue? RootDescriptor { get; init; }
    public string? ErrorMessage { get; init; }
    public string? ErrorDetail { get; init; }
    public long ExecutionTimeMs { get; init; }

    public static ScriptResult Ok(ObjectValue root, long ms) => new()
    {
        Success = true, RootDescriptor = root, ExecutionTimeMs = ms
    };

    public static ScriptResult Fail(string message, string? detail = null) => new()
    {
        Success = false, ErrorMessage = message, ErrorDetail = detail
    };
}

/// <summary>
/// 脚本引擎适配器 — 管理 ScriptEngine 生命周期、模块注册、脚本执行
/// </summary>
public class ScriptEngineAdapter : IDisposable
{
    private ScriptEngine? _engine;
    private bool _disposed;

    /// <summary>脚本执行超时（秒），0 表示无限制</summary>
    public int ExecutionTimeoutSeconds { get; set; } = 30;

    private readonly ConcurrentDictionary<string, ControlWrapper> _controlRegistry = new();

    public ScriptEngine? Engine => _engine;

    /// <summary>
    /// 初始化引擎、注册内置模块
    /// </summary>
    public void Initialize()
    {
        _engine = new ScriptEngine
        {
            //IsPrintVMInfo = true,
            //IsPrintInputSciptContent = true
        };
        _engine.PrototypeManager.Register<InpcPrototype>();
        _engine.PrototypeManager.Register<TablePrototype>();
        _controlRegistry.Clear();
        RegisterBuiltinModules();
        Log.Info("ScriptEngineAdapter initialized");
    }

    /// <summary>
    /// 执行脚本代码，返回结构化结果
    /// </summary>
    /// <param name="rootPath">脚本所在目录（用于 import 路径解析，可选）</param>
    public async Task<ScriptResult> ExecuteAsync(string scriptPath)
    {
        var sw = Stopwatch.StartNew();

        try
        {
            if (_engine == null)
                return ScriptResult.Fail("引擎未初始化，请先调用 Initialize()");

            // 设置 import 根路径（internal setter，需反射获取非公开访问器）
            /*if (rootPath != null)
            {
                typeof(ScriptLang.Runtime.ImportResolver)
                    .GetProperty("RootPath")?.GetSetMethod(true)
                    ?.Invoke(_engine.ImportResolver, [rootPath]);
            }*/

            _controlRegistry.Clear();
            RegisterBuiltinModules();

            // 编译并执行（带超时保护）
            ScriptTask task;
            try
            {
                task = _engine.CreateTask(scriptPath);
            }
            catch (Exception ex)
            {
                sw.Stop();
                return ScriptResult.Fail(
                    $"脚本编译错误 ({scriptPath})", ex.Message);
            }

            Value result;
            if (ExecutionTimeoutSeconds > 0)
            {
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(ExecutionTimeoutSeconds));
                var executeTask = task.RunAsync();
                var completed = await Task.WhenAny(executeTask, timeoutTask);

                if (completed == timeoutTask)
                {
                    task.Cancel();
                    sw.Stop();
                    return ScriptResult.Fail(
                        $"脚本执行超时（>{ExecutionTimeoutSeconds}秒）", scriptPath);
                }
                result = await executeTask;
            }
            else
            {
                result = await task.RunAsync();
            }

            sw.Stop();

            if (result is ObjectValue obj)
                return ScriptResult.Ok(obj, sw.ElapsedMilliseconds);

            return ScriptResult.Fail(
                $"脚本必须返回控件描述符，但返回了: {result?.GetType().Name ?? "null"}",
                $"脚本最后一条表达式应为 window({{...}}) 或控件描述符");
        }
        catch (Exception ex)
        {
            sw.Stop();
            Log.Error($"ExecuteAsync failed ({scriptPath}): {ex.Message}");

            return ScriptResult.Fail(
                $"脚本执行异常 ({scriptPath})",
                ex.InnerException?.Message ?? ex.Message);
        }
    }

    // ========================================================================
    // 控件注册表
    // ========================================================================

    public void RegisterControl(string name, ControlWrapper wrapper)
    {
        _controlRegistry[name] = wrapper;
    }

    public ControlWrapper? FindControl(string name)
    {
        _controlRegistry.TryGetValue(name, out var wrapper);
        return wrapper;
    }

    /// <summary>
    /// 注销并清理命名控件（移除时调用）
    /// </summary>
    public void UnregisterControl(string name)
    {
        if (_controlRegistry.TryRemove(name, out var wrapper))
        {
            wrapper.Dispose();
        }
    }

    // ========================================================================
    // 内部
    // ========================================================================

    private void RegisterBuiltinModules()
    {
        if (_engine == null) return;

        _engine.ImportResolver.RegisterBuiltinModule("avalonia",
            AvaloniaModule.CreateExports(this));
        _engine.ImportResolver.RegisterBuiltinModule("avalonia.controls",
            ControlsModule.CreateExports());
    }

    // ========================================================================
    // IDisposable
    // ========================================================================

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // 清理所有注册的控件
        foreach (var kv in _controlRegistry)
            kv.Value.Dispose();
        _controlRegistry.Clear();

        _engine?.ClearCache();
        _engine = null;

        Log.Info("ScriptEngineAdapter disposed");
    }
}
