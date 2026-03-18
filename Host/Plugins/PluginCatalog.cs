using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Host.Plugins;

public sealed record PluginEntry(
    string PluginDir,
    string ManifestPath,
    PluginManifestModel Manifest
);

public sealed class PluginCatalog
{
    private readonly IReadOnlyList<PluginEntry> _plugins;

    private PluginCatalog(IReadOnlyList<PluginEntry> plugins)
    {
        _plugins = plugins;
    }

    public IReadOnlyList<PluginEntry> Plugins => _plugins;

    public static PluginCatalog LoadFromOutput(string baseDir)
    {
        var pluginsDir = Path.Combine(baseDir, "plugins");
        if (!Directory.Exists(pluginsDir))
        {
            return new PluginCatalog(Array.Empty<PluginEntry>());
        }

        var manifestPaths = Directory.GetFiles(pluginsDir, "manifest.json", SearchOption.AllDirectories);
        var entries = new List<PluginEntry>(manifestPaths.Length);

        foreach (var manifestPath in manifestPaths)
        {
            try
            {
                var json = File.ReadAllText(manifestPath);
                var manifest = JsonSerializer.Deserialize<PluginManifestModel>(
                    json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true }
                );

                if (manifest is null || string.IsNullOrWhiteSpace(manifest.PluginId))
                {
                    continue;
                }

                if (!manifest.SupportsTerminationOnCancel)
                {
                    Console.WriteLine($"[plugins] Skip plugin (no termination on cancel): {manifest.PluginId} ({manifestPath})");
                    continue;
                }

                var pluginDir = Path.GetDirectoryName(manifestPath) ?? pluginsDir;
                entries.Add(new PluginEntry(pluginDir, manifestPath, manifest));
            }
            catch
            {
                // ignore malformed
            }
        }

        return new PluginCatalog(entries);
    }

    public void PrintSummary()
    {
        var pluginsDir = Path.Combine(AppContext.BaseDirectory, "plugins");
        if (!Directory.Exists(pluginsDir))
        {
            WriteLine($"[plugins] Directory not found: {pluginsDir}");
            return;
        }

        if (_plugins.Count == 0)
        {
            WriteLine($"[plugins] No manifest.json found under: {pluginsDir}");
            return;
        }

        foreach (var entry in _plugins)
        {
            WriteLine($"[plugins] Found manifest: {entry.ManifestPath}");

            var extensions = entry.Manifest.SupportedInputExtensions?.Length > 0
                ? string.Join(", ", entry.Manifest.SupportedInputExtensions)
                : "(none)";

            var formats = entry.Manifest.SupportedTargetFormats?.Length > 0
                ? string.Join(", ", entry.Manifest.SupportedTargetFormats.Select(f => f.Id))
                : "(none)";

            WriteLine($"[plugins] pluginId={entry.Manifest.PluginId}");
            WriteLine($"[plugins] supportedInputExtensions={extensions}");
            WriteLine($"[plugins] supportedTargetFormats={formats}");
        }
    }

    private static void WriteLine(string line)
    {
        Console.WriteLine(line);
        Trace.WriteLine(line);
    }
}

