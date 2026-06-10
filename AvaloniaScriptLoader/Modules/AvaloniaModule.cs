using Avalonia.Controls;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using ScriptLang.Runtime;
using AvaloniaScriptLoader.Factory;
using AvaloniaScriptLoader.Model;

namespace AvaloniaScriptLoader.Modules;

/// <summary>
/// "avalonia" 内置模块 — 系统级 API + 对话框 + 焦点管理
/// 脚本用法: import { app, inpc, computed, vif, vfor, component } from "avalonia"
/// </summary>
public static class AvaloniaModule
{
    /// <summary>主窗口引用（用于对话框的父窗口）</summary>
    internal static Window? MainWindow { get; set; }

    public static ObjectValue CreateExports(ScriptEngineAdapter adapter)
    {
        var properties = new Dictionary<string, Value>
        {
            ["app"]       = CreateAppObject(adapter),
            ["inpc"]      = InpcFactory.CreateInpcFunction(),
            ["computed"]  = InpcFactory.CreateComputedFunction(),
            ["vif"]       = StructureFactory.CreateVifFunction(),
            ["vfor"]      = StructureFactory.CreateVforFunction(),
            ["component"] = StructureFactory.CreateComponentFunction(),
            ["style"]     = StyleFactory.CreateStyleFunction(),
            ["fetch"]     = HttpModule.CreateExports().Properties["fetch"],
        };
        return new ObjectValue(properties);
    }

    private static ObjectValue CreateAppObject(ScriptEngineAdapter adapter)
    {
        return new ObjectValue(new Dictionary<string, Value>
        {
            // === showMessage(text, title?) ===
            ["showMessage"] = new FunctionValue("showMessage", args =>
            {
                var text = args.FirstOrDefault()?.AsString() ?? "";
                var title = args.Count > 1 ? args[1].AsString() : "消息";
                ShowMessageDialog(title, text);
            }),

            // === showConfirm(text, title?) → bool ===
            ["showConfirm"] = new FunctionValue("showConfirm", args =>
            {
                var text = args.FirstOrDefault()?.AsString() ?? "";
                var title = args.Count > 1 ? args[1].AsString() : "确认";
                return BoolValue.Create(ShowConfirmDialog(title, text));
            }),

            // === openFile(title?, filter?) → string | null ===
            ["openFile"] = new FunctionValue("openFile", async args =>
            {
                var title = args.FirstOrDefault()?.AsString() ?? "打开文件";
                var filters = args.Count > 1 ? ParseFileFilters(args[1]) : null;
                var result = await OpenFileDialogAsync(title, filters);
                return result != null ? StringValue.Create(result) : Value.Null;
            }),

            // === saveFile(title?, filter?) → string | null ===
            ["saveFile"] = new FunctionValue("saveFile", async args =>
            {
                var title = args.FirstOrDefault()?.AsString() ?? "保存文件";
                var filters = args.Count > 1 ? ParseFileFilters(args[1]) : null;
                var result = await SaveFileDialogAsync(title, filters);
                return result != null ? StringValue.Create(result) : Value.Null;
            }),

            // === showDialog(content, title?) — 自定义内容对话框 ===
            ["showDialog"] = new FunctionValue("showDialog", args =>
            {
                var content = args.FirstOrDefault() as ObjectValue;
                var title = args.Count > 1 ? args[1].AsString() : "对话框";
                if (content == null) return;
                ShowCustomDialog(title, content, adapter);
            }),

            // === log(text) ===
            ["log"] = new FunctionValue("log", args =>
            {
                Log.Info($"[Script] {args.FirstOrDefault()?.AsString() ?? ""}");
            }),

            // === find(name) → ObjectValue ===
            ["find"] = new FunctionValue("find", args =>
            {
                var name = args.FirstOrDefault()?.AsString() ?? "";
                var wrapper = adapter.FindControl(name);
                return wrapper?.Descriptor ?? Value.Null;
            }),

            // === focus(name) ===
            ["focus"] = new FunctionValue("focus", args =>
            {
                var name = args.FirstOrDefault()?.AsString() ?? "";
                Dispatcher.UIThread.Post(() =>
                {
                    adapter.FindControl(name)?.Control.Focus();
                });
            }),
        });
    }

    private static void ShowCustomDialog(string title, ObjectValue contentDesc, ScriptEngineAdapter adapter)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var builder = new Builder.ControlBuilder(adapter);
            var content = builder.Build(contentDesc);

            var dialog = new Window
            {
                Title = title,
                Width = 500, Height = 350,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = content,
            };
            dialog.ShowDialog(MainWindow);
        });
    }

    // ========================================================================
    // 对话框实现
    // ========================================================================

    private static void ShowMessageDialog(string title, string text)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var dialog = new Window
            {
                Title = title,
                Width = 350, Height = 150,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Spacing = 15, Margin = new Avalonia.Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        new Button { Content = "确定", HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center }
                    }
                }
            };
            if (dialog.Content is StackPanel sp && sp.Children[1] is Button btn)
                btn.Click += (_, _) => dialog.Close();
            dialog.ShowDialog(MainWindow);
        });
    }

    private static bool ShowConfirmDialog(string title, string text)
    {
        bool result = false;
        var tcs = new TaskCompletionSource<bool>();

        Dispatcher.UIThread.Post(() =>
        {
            var dialog = new Window
            {
                Title = title,
                Width = 380, Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Content = new StackPanel
                {
                    Spacing = 15, Margin = new Avalonia.Thickness(20),
                    Children =
                    {
                        new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                        new StackPanel
                        {
                            Orientation = Avalonia.Layout.Orientation.Horizontal,
                            Spacing = 10, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                            Children =
                            {
                                new Button { Content = "确定" },
                                new Button { Content = "取消" },
                            }
                        }
                    }
                }
            };
            if (dialog.Content is StackPanel sp && sp.Children[1] is StackPanel btnRow)
            {
                if (btnRow.Children[0] is Button ok) ok.Click += (_, _) => { result = true; dialog.Close(); };
                if (btnRow.Children[1] is Button cancel) cancel.Click += (_, _) => { dialog.Close(); };
            }
            dialog.ShowDialog(MainWindow);
            tcs.SetResult(result);
        });

        // 同步等待对话框关闭（在 UI 线程上直接运行）
        if (Dispatcher.UIThread.CheckAccess())
        {
            var dialog = new Window
            {
                Title = title, Width = 380, Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
            };
            var okBtn = new Button { Content = "确定" };
            var cancelBtn = new Button { Content = "取消" };
            okBtn.Click += (_, _) => { result = true; dialog.Close(); };
            cancelBtn.Click += (_, _) => { dialog.Close(); };
            dialog.Content = new StackPanel
            {
                Spacing = 15, Margin = new Avalonia.Thickness(20),
                Children =
                {
                    new TextBlock { Text = text, TextWrapping = Avalonia.Media.TextWrapping.Wrap },
                    new StackPanel
                    {
                        Orientation = Avalonia.Layout.Orientation.Horizontal,
                        Spacing = 10, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Center,
                        Children = { okBtn, cancelBtn }
                    }
                }
            };
            dialog.ShowDialog(MainWindow);
            return result;
        }

        return false;
    }

    private static async Task<string?> OpenFileDialogAsync(string title, List<FilePickerFileType>? filters)
    {
        if (MainWindow == null) return null;
        var storage = MainWindow.StorageProvider;
        var options = new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
        };
        if (filters != null) options.FileTypeFilter = filters;
        var files = await storage.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].Path.LocalPath : null;
    }

    private static async Task<string?> SaveFileDialogAsync(string title, List<FilePickerFileType>? filters)
    {
        if (MainWindow == null) return null;
        var storage = MainWindow.StorageProvider;
        var options = new FilePickerSaveOptions
        {
            Title = title,
        };
        var file = await storage.SaveFilePickerAsync(options);
        return file?.Path.LocalPath;
    }

    private static List<FilePickerFileType>? ParseFileFilters(Value filterValue)
    {
        if (filterValue.AsString() is not string s || string.IsNullOrEmpty(s)) return null;
        // 格式: "描述|*.ext|描述2|*.ext2" 或 "所有文件|*.*"
        var parts = s.Split('|');
        var result = new List<FilePickerFileType>();
        for (int i = 0; i + 1 < parts.Length; i += 2)
        {
            var desc = parts[i];
            var patterns = parts[i + 1].Split(';');
            result.Add(new FilePickerFileType(desc) { Patterns = [.. patterns] });
        }
        return result;
    }
}
