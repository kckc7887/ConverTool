using Host.Plugins;

namespace Host;

public static class AppServices
{
    public static I18nService I18n { get; } = new();

    public static PluginCatalog Plugins { get; set; } = PluginCatalog.LoadFromOutput(System.AppContext.BaseDirectory);

    public static PluginI18nService PluginI18n { get; } = new();
}

