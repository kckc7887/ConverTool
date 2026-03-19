using Avalonia;
using System;
using System.IO;
using Host.Plugins;

namespace Host;

class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            AppServices.Plugins = PluginCatalog.LoadFromOutput(AppContext.BaseDirectory);
            AppServices.Plugins.PrintSummary();

            // Step 4 verification hook:
            // `dotnet run --project Host/Host.csproj -c Debug -- route <path>`
            if (args.Length >= 2 && string.Equals(args[0], "route", StringComparison.OrdinalIgnoreCase))
            {
                var inputPath = args[1];
                var match = PluginRouter.RouteByInputPath(AppServices.Plugins, inputPath);
                Console.WriteLine(match is null
                    ? $"[route] No plugin matched input: {inputPath}"
                    : $"[route] Matched pluginId={match.Manifest.PluginId} for input: {inputPath}");
            }

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // WinExe 下没有控制台时，用户看不到异常；因此落盘到文件，便于你排查。
            try
            {
                var logPath = Path.Combine(AppContext.BaseDirectory, "startup-error.log");
                var text =
                    $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] Startup failed\r\n" +
                    $"BaseDirectory: {AppContext.BaseDirectory}\r\n" +
                    $"CurrentDirectory: {Environment.CurrentDirectory}\r\n" +
                    $"Args: {string.Join(" ", args)}\r\n\r\n" +
                    ex;
                File.WriteAllText(logPath, text);
            }
            catch
            {
                // best effort
            }

            Console.Error.WriteLine(ex);
            throw;
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
