using System.Collections.Generic;
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

    [JsonPropertyName("titleKey")]
    public string? TitleKey { get; set; }

    [JsonPropertyName("descriptionKey")]
    public string? DescriptionKey { get; set; }

    [JsonPropertyName("author")]
    public string? Author { get; set; }

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

    [JsonPropertyName("visibleIf")]
    public VisibleIfModel? VisibleIf { get; set; }
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

    [JsonPropertyName("fieldBoolRelations")]
    public FieldBoolRelationModel[] FieldBoolRelations { get; set; } = [];

    /// <summary>
    /// When a rule's <see cref="FieldPersistOverrideModel.When"/> matches current UI, Host applies
    /// <see cref="FieldPersistOverrideModel.Fields"/> to the serialized user-settings snapshot (and after restore),
    /// without changing in-session UI during save — see Host implementation.
    /// </summary>
    [JsonPropertyName("fieldPersistOverrides")]
    public FieldPersistOverrideModel[] FieldPersistOverrides { get; set; } = [];
}

/// <summary>
/// Mirrors <c>PluginAbstractions.FieldPersistOverrideRule</c> for JSON manifests.
/// </summary>
public sealed class FieldPersistOverrideModel
{
    [JsonPropertyName("when")]
    public VisibleIfModel When { get; set; } = new();

    /// <summary>Field keys and JSON values as stored in user-settings (e.g. checkbox booleans).</summary>
    [JsonPropertyName("fields")]
    public Dictionary<string, JsonElement>? Fields { get; set; }
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

    [JsonPropertyName("visibleIf")]
    public VisibleIfModel? VisibleIf { get; set; }
}

public sealed class VisibleIfModel
{
    [JsonPropertyName("fieldKey")]
    public string FieldKey { get; set; } = "";

    [JsonPropertyName("equals")]
    public bool Expected { get; set; }
}

public sealed class FieldBoolRelationModel
{
    [JsonPropertyName("if")]
    public VisibleIfModel If { get; set; } = new();

    [JsonPropertyName("then")]
    public FieldBoolThenModel Then { get; set; } = new();

    // "save" in our manifests; we apply immediately in UI anyway.
    [JsonPropertyName("applyWhen")]
    public string? ApplyWhen { get; set; }
}

public sealed class FieldBoolThenModel
{
    [JsonPropertyName("targetKey")]
    public string TargetKey { get; set; } = "";

    [JsonPropertyName("value")]
    public bool Value { get; set; }
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

