using System.Collections.Generic;
using System;
using System.IO;
using System.Text.Json;
using Host.Plugins;

namespace Host;

public sealed class PluginI18nService
{
    private readonly Dictionary<string, Dictionary<string, string>> _cache = new(StringComparer.OrdinalIgnoreCase);

    public string T(PluginEntry entry, string key, string locale)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return key;
        }

        var dict = GetStrings(entry, locale);
        if (dict.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
        {
            return v;
        }

        // fallback to en-US, then raw key
        if (!string.Equals(locale, "en-US", StringComparison.OrdinalIgnoreCase))
        {
            var en = GetStrings(entry, "en-US");
            if (en.TryGetValue(key, out var v2) && !string.IsNullOrWhiteSpace(v2))
            {
                return v2;
            }
        }

        return key;
    }

    private Dictionary<string, string> GetStrings(PluginEntry entry, string locale)
    {
        var cacheKey = $"{entry.Manifest.PluginId}::{locale}";
        if (_cache.TryGetValue(cacheKey, out var existing))
        {
            return existing;
        }

        var dict = LoadStrings(entry, locale);
        _cache[cacheKey] = dict;
        return dict;
    }

    private static Dictionary<string, string> LoadStrings(PluginEntry entry, string locale)
    {
        try
        {
            var localesFolder = entry.Manifest.I18n?.LocalesFolder;
            if (string.IsNullOrWhiteSpace(localesFolder))
            {
                localesFolder = "locales";
            }

            var path = Path.Combine(entry.PluginDir, localesFolder, $"{locale}.json");
            if (!File.Exists(path))
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream);
            if (!doc.RootElement.TryGetProperty("strings", out var stringsEl) || stringsEl.ValueKind != JsonValueKind.Object)
            {
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            }

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in stringsEl.EnumerateObject())
            {
                if (prop.Value.ValueKind == JsonValueKind.String)
                {
                    dict[prop.Name] = prop.Value.GetString() ?? "";
                }
            }

            return dict;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        }
    }
}

