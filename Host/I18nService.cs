using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace Host;

public sealed class I18nService
{
    private readonly Dictionary<string, string> _strings = new(StringComparer.OrdinalIgnoreCase);

    public string Locale { get; private set; } = "en-US";

    public static readonly string[] SupportedLocales = ["zh-CN", "en-US"];

    public event EventHandler? LocaleChanged;

    public void Initialize(string baseDir)
    {
        var systemLocale = CultureInfo.CurrentUICulture.Name;
        SetLocale(baseDir, PickSupportedLocale(systemLocale));
    }

    public void SetLocale(string baseDir, string locale)
    {
        var normalized = PickSupportedLocale(locale);
        if (string.Equals(Locale, normalized, StringComparison.OrdinalIgnoreCase) && _strings.Count > 0)
        {
            return;
        }

        Locale = normalized;
        LoadStrings(baseDir, normalized);
        LocaleChanged?.Invoke(this, EventArgs.Empty);
    }

    public string T(string key)
    {
        if (_strings.TryGetValue(key, out var v) && !string.IsNullOrWhiteSpace(v))
        {
            return v;
        }
        return key;
    }

    private static string PickSupportedLocale(string locale)
    {
        if (SupportedLocales.Any(l => string.Equals(l, locale, StringComparison.OrdinalIgnoreCase)))
        {
            return locale;
        }

        // Handle "zh" / "en" fallbacks.
        var two = locale.Length >= 2 ? locale[..2].ToLowerInvariant() : locale.ToLowerInvariant();
        return two switch
        {
            "zh" => "zh-CN",
            "en" => "en-US",
            _ => "en-US"
        };
    }

    private void LoadStrings(string baseDir, string locale)
    {
        _strings.Clear();

        // We copy `Host/locales/<locale>.json` to output directory.
        var path = Path.Combine(baseDir, "locales", $"{locale}.json");
        if (!File.Exists(path))
        {
            return;
        }

        using var stream = File.OpenRead(path);
        using var doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("strings", out var stringsEl) || stringsEl.ValueKind != JsonValueKind.Object)
        {
            return;
        }

        foreach (var prop in stringsEl.EnumerateObject())
        {
            if (prop.Value.ValueKind == JsonValueKind.String)
            {
                _strings[prop.Name] = prop.Value.GetString() ?? "";
            }
        }
    }
}

