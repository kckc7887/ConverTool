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

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace();
}
