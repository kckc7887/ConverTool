using System.Text.Json;
using System.Text.Json.Serialization;

namespace Host;

public sealed class PluginManifestModel
{
    [JsonPropertyName("pluginId")]
    public string PluginId { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("assembly")]
    public string Assembly { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "";

    [JsonPropertyName("supportedInputExtensions")]
    public string[] SupportedInputExtensions { get; set; } = [];

    [JsonPropertyName("supportedTargetFormats")]
    public TargetFormatModel[] SupportedTargetFormats { get; set; } = [];

    [JsonPropertyName("configSchema")]
    public ConfigSchemaModel? ConfigSchema { get; set; }

    [JsonPropertyName("supportedLocales")]
    public string[] SupportedLocales { get; set; } = [];

    [JsonPropertyName("i18n")]
    public I18nModel? I18n { get; set; }

    // Host legality check: plugin must be able to stop any external processes it spawns
    // when the Host cancels ExecuteAsync via CancellationToken.
    // If false/missing, the Host will skip loading the plugin.
    [JsonPropertyName("supportsTerminationOnCancel")]
    public bool SupportsTerminationOnCancel { get; set; } = false;
}

public sealed class TargetFormatModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("displayNameKey")]
    public string DisplayNameKey { get; set; } = "";

    [JsonPropertyName("descriptionKey")]
    public string? DescriptionKey { get; set; }
}

public sealed class I18nModel
{
    [JsonPropertyName("localesFolder")]
    public string LocalesFolder { get; set; } = "locales";
}

public sealed class ConfigSchemaModel
{
    [JsonPropertyName("sections")]
    public ConfigSectionModel[] Sections { get; set; } = [];
}

public sealed class ConfigSectionModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("titleKey")]
    public string? TitleKey { get; set; }

    [JsonPropertyName("descriptionKey")]
    public string? DescriptionKey { get; set; }

    [JsonPropertyName("collapsedByDefault")]
    public bool CollapsedByDefault { get; set; }

    [JsonPropertyName("fields")]
    public ConfigFieldModel[] Fields { get; set; } = [];
}

public sealed class ConfigFieldModel
{
    [JsonPropertyName("key")]
    public string Key { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = "Text";

    [JsonPropertyName("labelKey")]
    public string LabelKey { get; set; } = "";

    [JsonPropertyName("helpKey")]
    public string? HelpKey { get; set; }

    [JsonPropertyName("defaultValue")]
    public JsonElement? DefaultValue { get; set; }

    [JsonPropertyName("options")]
    public ConfigOptionModel[]? Options { get; set; }

    [JsonPropertyName("range")]
    public RangeValidationModel? Range { get; set; }

    [JsonPropertyName("path")]
    public PathFieldModel? Path { get; set; }
}

public sealed class ConfigOptionModel
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("labelKey")]
    public string LabelKey { get; set; } = "";
}

public sealed class PathFieldModel
{
    [JsonPropertyName("kind")]
    public string Kind { get; set; } = "File"; // File | Folder

    [JsonPropertyName("mustExist")]
    public bool MustExist { get; set; } = true;
}

public sealed class RangeValidationModel
{
    [JsonPropertyName("min")]
    public double Min { get; set; }

    [JsonPropertyName("max")]
    public double Max { get; set; }

    [JsonPropertyName("step")]
    public double Step { get; set; } = 1;
}

