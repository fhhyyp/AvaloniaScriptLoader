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
            var scriptCode = await File.ReadAllTextAsync(scriptPath);
            var scriptName = Path.GetFileName(scriptPath);

            // 3. 执行脚本（后台线程） → 获取 ObjectValue 树
            var rootDescriptor = await Task.Run(() =>
                _adapter.ExecuteAsync(scriptCode, scriptName));

            StatusText.Text = "正在构建 UI...";

            // 4. 在 UI 线程构建控件树
            var controlBuilder = new ControlBuilder(_adapter);
            var scriptWindow = controlBuilder.BuildWindow(rootDescriptor);

            if (scriptWindow != null)
            {
                // 5. 显示脚本生成的窗口
                scriptWindow.Show();

                // 关闭启动窗口
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

        // 优先 Counter.script
        var counterPath = Path.Combine(samplesDir, "ComputedBinding.script");
        //var counterPath = Path.Combine(samplesDir, "Counter.script");
        if (File.Exists(counterPath))
            return counterPath;

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
