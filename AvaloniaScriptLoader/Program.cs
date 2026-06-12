using System;
using Avalonia;
using ScriptLang;

namespace AvaloniaScriptLoader;

internal class Program
{
    [STAThread]
    public static async Task Main(string[] args)
    {
        //ScriptLog.IsWriteLogFile = true;
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
