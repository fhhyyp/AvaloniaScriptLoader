using Avalonia;
using System;

namespace AvaloniaScriptLoader;

internal class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        DateTime dt1 = DateTime.Now;
        DateTime dt2 = DateTime.Now;
        TimeSpan sp = dt1 - dt2;
        
        BuildAvaloniaApp()
            .StartWithClassicDesktopLifetime(args);
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
