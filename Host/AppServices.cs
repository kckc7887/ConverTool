using Host.Plugins;
using Host.Services;

namespace Host;

public static class AppServices
{
    public static I18nService I18n => ServiceLocator.GetService<I18nService>();

    public static PluginCatalog Plugins => ServiceLocator.GetPluginCatalog();

    public static PluginI18nService PluginI18n => ServiceLocator.GetService<PluginI18nService>();

    public static void ReloadPlugins(string baseDir)
    {
        ServiceLocator.ReloadPluginCatalog(baseDir);
    }
}
