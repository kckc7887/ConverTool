using System;
using System.IO;
using System.Linq;

namespace Host.Plugins;

public static class PluginRouter
{
    public static PluginEntry? RouteByInputPath(PluginCatalog catalog, string inputPath)
    {
        var ext = NormalizeExtension(Path.GetExtension(inputPath));
        if (string.IsNullOrWhiteSpace(ext))
        {
            return null;
        }

        return catalog.Plugins
            .Where(p => p.Manifest.SupportedInputExtensions.Any(e => string.Equals(e, ext, StringComparison.OrdinalIgnoreCase)))
            .OrderBy(p => p.Manifest.SupportedInputExtensions.Length)
            .ThenBy(p => p.Manifest.PluginId, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static string NormalizeExtension(string extWithDot)
    {
        if (string.IsNullOrWhiteSpace(extWithDot))
        {
            return "";
        }

        var ext = extWithDot.StartsWith('.') ? extWithDot[1..] : extWithDot;
        return ext.Trim().ToLowerInvariant();
    }
}

