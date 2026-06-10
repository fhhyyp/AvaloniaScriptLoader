using Avalonia.Controls;
using Avalonia.Threading;
using AvaloniaScriptLoader.Builder;

namespace AvaloniaScriptLoader;

public partial class MainWindow : Window
{
    private ScriptEngineAdapter? _adapter;

    public MainWindow()
    {
        InitializeComponent();
        Loaded += OnLoaded;
    }

    private Window? _scriptWindow;

    protected override void OnClosed(EventArgs e)
    {
        // 不在 Loader 窗口关闭时 dispose adapter — 脚本窗口可能还在使用
        // 只有当脚本窗口也关闭时才清理
        if (_scriptWindow == null)
            _adapter?.Dispose();
        base.OnClosed(e);
    }

    private async void OnLoaded(object? sender, EventArgs e)
    {
        try
        {
            StatusText.Text = "正在初始化脚本引擎...";

            // 1. 初始化引擎
            _adapter = new ScriptEngineAdapter();
            _adapter.Initialize();

            StatusText.Text = "正在加载并执行脚本...";

            // 2. 加载并执行示例脚本
            var scriptPath = FindScriptFile();
            //var scriptCode = await File.ReadAllTextAsync(scriptPath);
            //var scriptName = Path.GetFileName(scriptPath);
            //
            // 3. 执行脚本（后台线程，带超时保护）
            //var scriptDir = Path.GetDirectoryName(scriptPath);
            var scriptResult = await Task.Run(() =>
                _adapter.ExecuteAsync(scriptPath));

            if (!scriptResult.Success)
            {
                StatusText.Text = "脚本执行失败";
                ErrorText.Text = scriptResult.ErrorMessage + "\n\n" + scriptResult.ErrorDetail;
                return;
            }

            StatusText.Text = $"正在构建 UI... (耗时 {scriptResult.ExecutionTimeMs}ms)";

            // 4. 在 UI 线程构建控件树
            var controlBuilder = new ControlBuilder(_adapter);
            var scriptWindow = controlBuilder.BuildWindow(scriptResult.RootDescriptor!);

            if (scriptWindow != null)
            {
                _scriptWindow = scriptWindow;

                // 脚本窗口关闭时清理 adapter
                scriptWindow.Closed += (s, args) =>
                {
                    _adapter?.Dispose();
                };

                scriptWindow.Show();
                Close();
            }
        }
        catch (Exception ex)
        {
            StatusText.Text = "脚本执行失败";
            ErrorText.Text = ex.Message;

            System.Diagnostics.Debug.WriteLine($"[AvaloniaScriptLoader] 错误: {ex}");
        }
    }

    /// <summary>
    /// 查找示例脚本文件
    /// </summary>
    private static string FindScriptFile()
    {
        // 优先使用命令行参数、环境变量，回退到 Samples 目录

        // 1. 环境变量
        var envPath = Environment.GetEnvironmentVariable("SCRIPT_PATH");
        if (!string.IsNullOrEmpty(envPath) && File.Exists(envPath))
            return envPath;

        // 2. 查找 Samples 目录
        var baseDir = AppContext.BaseDirectory;
        var samplesDir = Path.Combine(baseDir, "Samples");

        // 默认: demo/main.script
        var demoPath = Path.Combine(samplesDir, "demo", "main.script");
        if (File.Exists(demoPath))
            return demoPath;

        // 回退 HelloWorld.script
        var helloPath = Path.Combine(samplesDir, "HelloWorld.script");
        if (File.Exists(helloPath))
            return helloPath;

        // 3. 开发环境中的相对路径
        var devSamples = Path.Combine(baseDir, "..", "..", "..", "Samples", "Counter.script");
        var fullDevSamples = Path.GetFullPath(devSamples);
        if (File.Exists(fullDevSamples))
            return fullDevSamples;

        throw new FileNotFoundException(
            "未找到示例脚本文件。请设置 SCRIPT_PATH 环境变量或确保 Samples 目录存在。");
    }
}
