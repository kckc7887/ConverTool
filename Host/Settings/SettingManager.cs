using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using Host.Plugins;

namespace Host.Settings;

/// <summary>
/// 配置项定义，包含键、当前值和默认值
/// </summary>
public class SettingItem
{
    /// <summary>配置项键名</summary>
    public string Key { get; set; } = string.Empty;
    
    /// <summary>当前值</summary>
    public object Value { get; set; } = string.Empty;
    
    /// <summary>默认值（当Value为空时使用）</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public object? Default { get; set; }

    /// <summary>
    /// 为 <c>false</c> 时保存不更新 <see cref="Value"/>（仍使用从文件读入的值）；为 <c>true</c> 或未写时与 UI 同步写回。
    /// </summary>
    public bool? PersistValue { get; set; }
}

/// <summary>
/// 配置文件根对象
/// </summary>
public class ConfigFile
{
    /// <summary>配置项列表</summary>
    public List<SettingItem> Settings { get; set; } = new();
}

/// <summary>
/// 配置管理器 - 统一管理本体和插件的配置读写
/// </summary>
public static class SettingManager
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

    #region 路径管理

    /// <summary>
    /// 获取本体配置文件路径：与可执行文件同目录的 config.json（与 PluginCatalog 使用的 plugins 目录一致）。
    /// </summary>
    public static string GetHostConfigPath()
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configPath = Path.Combine(baseDir, "config.json");
        Console.WriteLine($"[Config] Host config path: {configPath}");
        Console.WriteLine($"[Config] Host config exists: {File.Exists(configPath)}");
        return configPath;
    }

    /// <summary>获取插件配置文件路径：plugins/&lt;pluginId&gt;/config.json（仅当无法从 <see cref="PluginCatalog"/> 取得目录时使用）。</summary>
    public static string GetPluginConfigPath(string pluginId)
    {
        var baseDir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var configPath = Path.Combine(baseDir, "plugins", pluginId, "config.json");
        Console.WriteLine($"[Config] Plugin {pluginId} config path: {configPath}");
        Console.WriteLine($"[Config] Plugin {pluginId} config exists: {File.Exists(configPath)}");
        return configPath;
    }

    /// <summary>与磁盘上的插件目录一致：&lt;pluginDirectory&gt;/config.json</summary>
    public static string GetPluginConfigPathForPluginDirectory(string pluginDirectory)
    {
        var dir = pluginDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return Path.Combine(dir, "config.json");
    }

    #endregion

    #region 配置加载

    /// <summary>
    /// 加载本体配置（首次运行时自动创建默认配置文件）
    /// </summary>
    /// <returns>配置字典，键为配置项名称</returns>
    public static Dictionary<string, SettingItem> LoadHostSettings()
    {
        var configPath = GetHostConfigPath();
        if (!File.Exists(configPath))
        {
            CreateDefaultHostConfig(configPath);
        }
        return LoadSettingsFromFile(configPath);
    }

    /// <summary>
    /// 创建默认的 Host 配置文件
    /// </summary>
    private static void CreateDefaultHostConfig(string filePath)
    {
        var defaultSettings = new List<SettingItem>
        {
            new() { Key = "Locale", Value = "zh-CN", Default = "zh-CN" },
            new() { Key = "OutputDir", Value = "%USERPROFILE%\\Documents\\convertool\\output", Default = "%USERPROFILE%\\Documents\\convertool\\output" },
            new() { Key = "UseInputDirAsOutput", Value = true, Default = true },
            new() { Key = "NamingTemplate", Value = "{base}", Default = "{base}" },
            new() { Key = "CustomToken1Text", Value = "", Default = "" },
            new() { Key = "CustomToken2Text", Value = "", Default = "" },
            new() { Key = "CustomToken3Text", Value = "", Default = "" },
            new() { Key = "EnableParallelProcessing", Value = false, Default = false },
            new() { Key = "Parallelism", Value = 2, Default = 2 },
            new() { Key = "KeepTemp", Value = false, Default = false },
            new() { Key = "Plugins", Value = new Dictionary<string, object>(), Default = new Dictionary<string, object>() }
        };

        var config = new ConfigFile { Settings = defaultSettings };
        var json = JsonSerializer.Serialize(config, JsonOptions);
        
        var dir = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }
        
        File.WriteAllText(filePath, json);
        Console.WriteLine($"[Config] Created default config: {filePath}");
    }

    /// <summary>
    /// 加载指定插件的配置
    /// </summary>
    /// <param name="pluginId">插件ID</param>
    /// <returns>配置字典</returns>
    public static Dictionary<string, SettingItem> LoadPluginSettings(string pluginId) => 
        LoadSettingsFromFile(GetPluginConfigPath(pluginId));

    /// <summary>
    /// 从插件目录加载 <c>config.json</c>（路径与 <see cref="PluginCatalog"/> 扫描到的 <c>PluginDir</c> 一致，避免 manifest 的 pluginId 与文件夹名不一致时读错文件）。
    /// </summary>
    public static Dictionary<string, SettingItem> LoadPluginSettingsFromPluginDirectory(string pluginDirectory) =>
        LoadSettingsFromFile(GetPluginConfigPathForPluginDirectory(pluginDirectory));

    /// <summary>
    /// 从文件加载配置
    /// </summary>
    private static Dictionary<string, SettingItem> LoadSettingsFromFile(string filePath)
    {
        var settings = new Dictionary<string, SettingItem>(StringComparer.OrdinalIgnoreCase);

        try
        {
            Console.WriteLine($"[Config] Loading from: {filePath}");
            if (!File.Exists(filePath))
            {
                Console.WriteLine($"[Config] File does not exist: {filePath}");
                return settings;
            }

            var json = File.ReadAllText(filePath);
            Console.WriteLine($"[Config] Read {json.Length} characters");
            var config = JsonSerializer.Deserialize<ConfigFile>(json, JsonOptions);

            if (config?.Settings != null)
            {
                Console.WriteLine($"[Config] Loaded {config.Settings.Count} settings");
                foreach (var item in config.Settings)
                {
                    if (!string.IsNullOrWhiteSpace(item.Key))
                    {
                        settings[item.Key] = item;
                    }
                }
            }
            else
            {
                Console.WriteLine("[Config] No settings found in file");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Error loading config: {ex.Message}");
            Console.WriteLine($"[Config] Stack trace: {ex.StackTrace}");
        }

        Console.WriteLine($"[Config] Returning {settings.Count} settings");
        return settings;
    }

    /// <summary>
    /// 加载当前已扫描到的所有插件的配置（每个插件一条记录，即使 <c>config.json</c> 为空或仅有 <c>settings: []</c>）。
    /// 使用 <see cref="PluginEntry.PluginDir"/> 定位文件，与插件加载逻辑一致。
    /// </summary>
    public static Dictionary<string, Dictionary<string, SettingItem>> LoadAllPluginSettings(PluginCatalog catalog)
    {
        var allSettings = new Dictionary<string, Dictionary<string, SettingItem>>(StringComparer.OrdinalIgnoreCase);

        try
        {
            foreach (var entry in catalog.Plugins)
            {
                var id = entry.Manifest.PluginId;
                if (string.IsNullOrWhiteSpace(id))
                    continue;

                var settings = LoadPluginSettingsFromPluginDirectory(entry.PluginDir);
                allSettings[id] = settings;
            }
        }
        catch
        {
            // 忽略错误
        }

        return allSettings;
    }

    #endregion

    #region 配置保存

    /// <summary>
    /// 保存本体配置
    /// </summary>
    /// <param name="settings">配置字典</param>
    public static void SaveHostSettings(Dictionary<string, SettingItem> settings) =>
        SaveSettingsToFile(GetHostConfigPath(), settings, mergeHostCanonicalDefaults: true);

    /// <summary>
    /// 保存插件配置
    /// </summary>
    /// <param name="pluginId">插件ID</param>
    /// <param name="settings">配置字典</param>
    public static void SavePluginSettings(string pluginId, Dictionary<string, SettingItem> settings) => 
        SaveSettingsToFile(GetPluginConfigPath(pluginId), settings);

    /// <summary>将配置写入 <c>&lt;pluginDirectory&gt;/config.json</c>。</summary>
    public static void SavePluginSettingsToPluginDirectory(string pluginDirectory, Dictionary<string, SettingItem> settings) =>
        SaveSettingsToFile(GetPluginConfigPathForPluginDirectory(pluginDirectory), settings);

    /// <summary>Host 内置 default，用于首次保存或内存中 Default 为 null 时写回文件。</summary>
    private static object? GetHostCanonicalDefault(string key) =>
        key switch
        {
            "Locale" => "zh-CN",
            "OutputDir" => "%USERPROFILE%\\Documents\\convertool\\output",
            "UseInputDirAsOutput" => true,
            "NamingTemplate" => "{base}",
            "CustomToken1Text" => "",
            "CustomToken2Text" => "",
            "CustomToken3Text" => "",
            "EnableParallelProcessing" => false,
            "Parallelism" => 2,
            "KeepTemp" => false,
            "Plugins" => JsonSerializer.Deserialize<JsonElement>("{}", JsonOptions),
            _ => null
        };

    /// <summary>
    /// 保存配置到文件
    /// </summary>
    private static void SaveSettingsToFile(string filePath, Dictionary<string, SettingItem> settings, bool mergeHostCanonicalDefaults = false)
    {
        try
        {
            // 构建配置对象，保持 Default（或合并内置 default），只更新 Value
            var items = settings.Values.Select(s =>
            {
                object? def = s.Default;
                if (def is null && mergeHostCanonicalDefaults)
                    def = GetHostCanonicalDefault(s.Key);
                return new SettingItem
                {
                    Key = s.Key,
                    Value = s.Value,
                    Default = def,
                    PersistValue = s.PersistValue
                };
            }).ToList();

            var config = new ConfigFile { Settings = items };
            var json = JsonSerializer.Serialize(config, JsonOptions);

            // 确保目录存在
            var dir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrWhiteSpace(dir) && !Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }

            File.WriteAllText(filePath, json);
        }
        catch
        {
            // 保存失败静默处理
        }
    }

    /// <summary>
    /// 保存所有插件配置（使用 catalog 中的插件目录，与读取路径一致）。
    /// </summary>
    public static void SaveAllPluginSettings(PluginCatalog catalog, Dictionary<string, Dictionary<string, SettingItem>> allSettings)
    {
        foreach (var (pluginId, settings) in allSettings)
        {
            var entry = catalog.Plugins.FirstOrDefault(e =>
                string.Equals(e.Manifest.PluginId, pluginId, StringComparison.OrdinalIgnoreCase));
            if (entry is not null)
                SavePluginSettingsToPluginDirectory(entry.PluginDir, settings);
            else
                SavePluginSettings(pluginId, settings);
        }
    }

    #endregion

    #region 配置读取

    /// <summary>
    /// 判断 Value/Default 是否表示“已配置”（空字符串视为未配置，会回退到 Default）。
    /// </summary>
    private static bool IsMeaningfulConfigurationValue<T>(T? value)
    {
        if (value is null) return false;
        if (value is string s) return !string.IsNullOrWhiteSpace(s);
        return true;
    }

    /// <summary>
    /// System.Text.Json 将 object 反序列化为 <see cref="JsonElement"/>，不能直接用 Convert.ChangeType。
    /// </summary>
    private static bool TryGetClr<T>(object? raw, out T? result)
    {
        result = default;
        if (raw is null) return false;

        if (raw is JsonElement je)
        {
            if (je.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
                return false;

            // JsonSerializer.Deserialize<string>(je.GetRawText(), options) 在部分全局 options 下会失败。
            // 直接读字符串值；JSON 数字等非字符串也转成字符串表示，便于配置项统一。
            if (typeof(T) == typeof(string))
            {
                result = (T)(object)(je.ValueKind switch
                {
                    JsonValueKind.String => je.GetString() ?? "",
                    JsonValueKind.Number => je.GetRawText(),
                    JsonValueKind.True => "true",
                    JsonValueKind.False => "false",
                    _ => je.GetRawText()
                });
                return true;
            }

            try
            {
                result = JsonSerializer.Deserialize<T>(je.GetRawText(), JsonOptions);
                return true;
            }
            catch
            {
                return false;
            }
        }

        try
        {
            if (typeof(T) == typeof(string))
            {
                result = (T)(object)(raw.ToString() ?? string.Empty);
                return true;
            }

            if (raw is T t)
            {
                result = t;
                return true;
            }

            result = (T)Convert.ChangeType(raw, typeof(T));
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 获取配置值
    /// 优先级：Value > Default > 类型默认值
    /// </summary>
    /// <typeparam name="T">目标类型</typeparam>
    /// <param name="settings">配置字典</param>
    /// <param name="key">配置键</param>
    /// <returns>配置值，不存在则返回类型默认值</returns>
    public static T? GetValue<T>(Dictionary<string, SettingItem> settings, string key)
    {
        if (!settings.TryGetValue(key, out var item))
        {
            Console.WriteLine($"[Config] Key not found: {key}");
            return default;
        }

        try
        {
            if (TryGetClr<T>(item.Value, out var fromValue) && IsMeaningfulConfigurationValue(fromValue))
                return fromValue;

            if (TryGetClr<T>(item.Default, out var fromDefault) && IsMeaningfulConfigurationValue(fromDefault))
                return fromDefault;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[Config] Error converting {key}: {ex.Message}");
        }

        return default;
    }

    /// <summary>
    /// 从插件 <c>config.json</c> 读取某字段的持久化字符串（用于恢复插件 UI）。
    /// 与 <see cref="GetValue{T}"/> 不同：显式保存的空字符串会原样返回，不会被当成「未配置」而忽略。
    /// 键不存在或 Value/Default 均无可用内容时返回 null（不覆盖控件默认值）。
    /// </summary>
    public static string? GetPluginFieldPersistedString(Dictionary<string, SettingItem> settings, string key)
    {
        if (!settings.TryGetValue(key, out var item))
            return null;

        if (TryGetPluginFieldRawString(item.Value, out var fromValue))
            return fromValue;
        if (TryGetPluginFieldRawString(item.Default, out var fromDefault))
            return fromDefault;
        return null;
    }

    private static bool TryGetPluginFieldRawString(object? raw, out string s)
    {
        s = "";
        if (raw is null)
            return false;

        if (raw is JsonElement je)
        {
            switch (je.ValueKind)
            {
                case JsonValueKind.String:
                    s = je.GetString() ?? "";
                    return true;
                case JsonValueKind.False:
                    s = "false";
                    return true;
                case JsonValueKind.True:
                    s = "true";
                    return true;
                case JsonValueKind.Number:
                    s = je.GetRawText();
                    return true;
                default:
                    return false;
            }
        }

        if (raw is string str)
        {
            s = str;
            return true;
        }

        if (raw is bool b)
        {
            s = b ? "true" : "false";
            return true;
        }

        try
        {
            s = Convert.ToString(raw, System.Globalization.CultureInfo.InvariantCulture) ?? "";
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>
    /// 设置配置值（只修改Value，保持Default不变）
    /// </summary>
    public static void SetValue<T>(Dictionary<string, SettingItem> settings, string key, T value)
    {
        object? defaultValue = null;
        bool? persistValue = null;
        if (settings.TryGetValue(key, out var existing))
        {
            if (existing.PersistValue == false)
                return;
            defaultValue = existing.Default;
            persistValue = existing.PersistValue;
        }

        object? stored = value;
        // null 字符串序列化时会被 WhenWritingNull 省略，导致下次读配置与 UI 不一致
        if (typeof(T) == typeof(string))
            stored = (string?)(object?)value ?? string.Empty;

        settings[key] = new SettingItem
        {
            Key = key,
            Value = stored!,
            Default = defaultValue,
            PersistValue = persistValue
        };
    }

    #endregion


}
