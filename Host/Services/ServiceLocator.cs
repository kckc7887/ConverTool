using System;
using System.Linq;
using Host.Plugins;
using Microsoft.Extensions.DependencyInjection;

namespace Host.Services;

public static class ServiceLocator
{
    private static IServiceProvider? _provider;
    private static ServiceCollection? _services;
    private static PluginCatalog? _pluginCatalog;

    public static IServiceProvider Provider
    {
        get
        {
            if (_provider is null)
            {
                throw new InvalidOperationException("ServiceLocator not initialized. Call Initialize first.");
            }
            return _provider;
        }
    }

    public static bool IsInitialized => _provider is not null;

    public static void Initialize(string baseDir)
    {
        if (_provider is not null)
        {
            return;
        }

        _services = new ServiceCollection();
        _pluginCatalog = PluginCatalog.LoadFromOutput(baseDir);

        _services.AddSingleton<I18nService>(sp => new I18nService());
        _services.AddSingleton<PluginI18nService>(sp => new PluginI18nService());
        _services.AddSingleton<PluginCatalog>(sp => _pluginCatalog!);

        _provider = _services.BuildServiceProvider();
    }

    public static void ReloadPluginCatalog(string baseDir)
    {
        _pluginCatalog = PluginCatalog.LoadFromOutput(baseDir);
    }

    public static T GetService<T>() where T : class
    {
        return Provider.GetRequiredService<T>();
    }

    public static T? GetServiceOrNull<T>() where T : class
    {
        return Provider.GetService<T>();
    }
}
