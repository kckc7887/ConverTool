using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Host.Services;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace Host;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        ServiceLocator.Initialize(AppContext.BaseDirectory);
        ServiceLocator.GetService<I18nService>().Initialize(AppContext.BaseDirectory);

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            desktop.MainWindow = new MainWindow();

            // Mark Avalonia ready before handling any incoming IPC, so pipe thread won't poison Dispatcher.
            Program.MarkAvaloniaReadyAndDrainIncoming();

            // “无进程右键启动”也走同一条 HandleCommandLineArgs 路径（与管道投递一致）
            if (Program.ConsumeInitialArgsForFirstInstance() is { Length: > 0 } initialArgs)
            {
                DiagLog.Write($"App init: consuming initialArgs len={initialArgs.Length}");
                Dispatcher.UIThread.Post(() => HandleCommandLineArgs(initialArgs));
            }
        }

        base.OnFrameworkInitializationCompleted();
    }
    
    public void HandleCommandLineArgs(string[] args)
    {
        DiagLog.Write($"HandleCommandLineArgs called. argsLen={args.Length} head={(args.Length > 0 ? args[0] : "")}");
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // 确保主窗口存在
            if (desktop.MainWindow == null)
            {
                DiagLog.Write("HandleCommandLineArgs: MainWindow is null, creating new MainWindow.");
                desktop.MainWindow = new MainWindow();
                desktop.MainWindow.Opened += async (_, _) =>
                {
                    await ProcessCommandLineArgsAsync(args);
                };
            }
            else
            {
                // 窗口已存在，直接处理
                DiagLog.Write("HandleCommandLineArgs: MainWindow exists, processing args.");
                _ = ProcessCommandLineArgsAsync(args);
            }
            
            // 激活主窗口
            desktop.MainWindow.Activate();
        }
    }
    
    private async Task ProcessCommandLineArgsAsync(string[] args)
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop 
            && desktop.MainWindow is MainWindow mainWindow)
        {
            // 处理命令行参数
            if (args.Length >= 2 && string.Equals(args[0], "convert", StringComparison.OrdinalIgnoreCase))
            {
                // 提取所有文件路径参数
                var filePaths = args.Skip(1).ToArray();
                DiagLog.Write($"ProcessCommandLineArgsAsync: convert fileCount={filePaths.Length}");
                // 延迟500ms执行，确保窗口完全初始化
                await Task.Delay(500);
                try
                {
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        mainWindow.AddInputPaths(filePaths);
                    });
                    DiagLog.Write("ProcessCommandLineArgsAsync: AddInputPaths done.");
                }
                catch (Exception ex)
                {
                    DiagLog.Write("ProcessCommandLineArgsAsync: exception " + ex);
                    throw;
                }
            }
        }
    }
}
