using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Host.Settings;

/// <summary>
/// Persist Host UI + per-plugin config under LocalApplicationData.
/// </summary>
public static class UserSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        PropertyNameCaseInsensitive = true
    };

    public static string GetSettingsPath()
        => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "ConverTool",
            "user-settings.json");

    public static UserSettingsFile? TryLoad()
    {
        try
        {
            var path = GetSettingsPath();
            if (!File.Exists(path))
            {
                return null;
            }

            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<UserSettingsFile>(json, JsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public static void Save(UserSettingsFile settings)
    {
        var path = GetSettingsPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(path, json);
    }
}

public sealed class UserSettingsFile
{
    public int? Version { get; set; }

    public string? Locale { get; set; }

    public string? InputPaths { get; set; }

    public string? OutputDir { get; set; }

    public bool? UseInputDirAsOutput { get; set; }

    public string? NamingTemplate { get; set; }

    public string? CustomToken1Text { get; set; }
    public string? CustomToken2Text { get; set; }
    public string? CustomToken3Text { get; set; }

    public bool? EnableParallelProcessing { get; set; }

    public int? Parallelism { get; set; }

    public bool? KeepTemp { get; set; }

    /// <summary>pluginId -> saved UI state</summary>
    public Dictionary<string, PluginUserSettings>? Plugins { get; set; }

    /// <summary>Enable context menu integration</summary>
    public bool? EnableContextMenu { get; set; }

    /// <summary>Allowed source extensions for context menu</summary>
    public List<string>? AllowedSourceExtensions { get; set; }

    /// <summary>Allowed target formats for context menu</summary>
    public Dictionary<string, List<string>>? AllowedTargetFormats { get; set; }
}

public sealed class PluginUserSettings
{
    public string? TargetFormatId { get; set; }

    /// <summary>config field key -> string representation</summary>
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}
