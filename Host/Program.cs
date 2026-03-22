using Avalonia;
using System;
using System.IO;
using Host.Plugins;
using Host.Services;

namespace Host;

class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            ServiceLocator.Initialize(AppContext.BaseDirectory);
            ServiceLocator.GetService<PluginCatalog>().PrintSummary();

            if (args.Length >= 2 && string.Equals(args[0], "route", StringComparison.OrdinalIgnoreCase))
            {
                var inputPath = args[1];
                var match = PluginRouter.RouteByInputPath(ServiceLocator.GetService<PluginCatalog>(), inputPath);
                Console.WriteLine(match is null
                    ? $"[route] No plugin matched input: {inputPath}"
                    : $"[route] Matched pluginId={match.Manifest.PluginId} for input: {inputPath}");
            }

            BuildAvaloniaApp()
                .StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
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
            }

            Console.Error.WriteLine(ex);
            throw;
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
