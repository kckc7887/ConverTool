using System.Text.Json;
using System.Text.Json.Serialization;
using Host.Settings;
using Xunit;

namespace Host.Tests;

/// <summary>
/// 与 <see cref="SettingManager"/> 中 <c>JsonOptions</c> 保持一致，用于 JSON 读写断言。
/// </summary>
public sealed class PersistValueSettingTests
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

    private static Dictionary<string, SettingItem> DictFromJson(string json)
    {
        var config = JsonSerializer.Deserialize<ConfigFile>(json, JsonOptions)
                     ?? throw new InvalidOperationException("deserialize");
        var settings = new Dictionary<string, SettingItem>(StringComparer.OrdinalIgnoreCase);
        foreach (var item in config.Settings)
        {
            if (!string.IsNullOrWhiteSpace(item.Key))
                settings[item.Key] = item;
        }
        return settings;
    }

    [Fact]
    public void Deserialize_ReadsPersistValueFalse()
    {
        const string json = """{"settings":[{"key":"K","value":"from-file","persistValue":false}]}""";
        var d = DictFromJson(json);
        Assert.True(d.TryGetValue("K", out var item));
        Assert.Equal(false, item.PersistValue);
        Assert.Equal("from-file", SettingManager.GetValue<string>(d, "K"));
    }

    [Fact]
    public void Deserialize_OmittedPersistValue_IsNull_TreatedAsWritable()
    {
        const string json = """{"settings":[{"key":"K","value":"from-file"}]}""";
        var d = DictFromJson(json);
        Assert.True(d.TryGetValue("K", out var item));
        Assert.Null(item.PersistValue);
        SettingManager.SetValue(d, "K", "updated");
        Assert.Equal("updated", SettingManager.GetValue<string>(d, "K"));
    }

    [Fact]
    public void Deserialize_PersistValueTrue_IsWritable()
    {
        const string json = """{"settings":[{"key":"K","value":"from-file","persistValue":true}]}""";
        var d = DictFromJson(json);
        SettingManager.SetValue(d, "K", "updated");
        Assert.Equal("updated", SettingManager.GetValue<string>(d, "K"));
    }

    [Fact]
    public void SetValue_PersistFalse_PreventsValueChange()
    {
        var d = new Dictionary<string, SettingItem>(StringComparer.OrdinalIgnoreCase)
        {
            ["K"] = new SettingItem
            {
                Key = "K",
                Value = "orig",
                PersistValue = false
            }
        };
        SettingManager.SetValue(d, "K", "attempt");
        Assert.Equal("orig", SettingManager.GetValue<string>(d, "K"));
    }

    [Fact]
    public void SetValue_PersistFalse_DoesNotDropPersistFlag()
    {
        var d = new Dictionary<string, SettingItem>(StringComparer.OrdinalIgnoreCase)
        {
            ["K"] = new SettingItem { Key = "K", Value = "orig", PersistValue = false }
        };
        SettingManager.SetValue(d, "K", "nope");
        Assert.True(d.TryGetValue("K", out var item));
        Assert.Equal(false, item.PersistValue);
    }

    [Fact]
    public void SerializeRoundTrip_PersistFalse_KeepsOriginalValueAndFlag()
    {
        var d = DictFromJson(
            """{"settings":[{"key":"A","value":"keep","persistValue":false},{"key":"B","value":"b0","persistValue":true}]}""");
        SettingManager.SetValue(d, "A", "should-not-apply");
        SettingManager.SetValue(d, "B", "b1");

        var items = d.Values.Select(s => new SettingItem
        {
            Key = s.Key,
            Value = s.Value,
            Default = s.Default,
            PersistValue = s.PersistValue
        }).ToList();
        var json = JsonSerializer.Serialize(new ConfigFile { Settings = items }, JsonOptions);
        var back = DictFromJson(json);

        Assert.Equal("keep", SettingManager.GetValue<string>(back, "A"));
        Assert.True(back["A"].PersistValue == false);
        Assert.Equal("b1", SettingManager.GetValue<string>(back, "B"));
    }

    [Fact]
    public void JsonOutput_ContainsPersistValueFalseWhenSet()
    {
        var d = new Dictionary<string, SettingItem>(StringComparer.OrdinalIgnoreCase)
        {
            ["K"] = new SettingItem { Key = "K", Value = "v", PersistValue = false }
        };
        var items = d.Values.Select(s => new SettingItem
        {
            Key = s.Key,
            Value = s.Value,
            Default = s.Default,
            PersistValue = s.PersistValue
        }).ToList();
        var json = JsonSerializer.Serialize(new ConfigFile { Settings = items }, JsonOptions);
        Assert.Contains("\"persistValue\": false", json);
    }
}
