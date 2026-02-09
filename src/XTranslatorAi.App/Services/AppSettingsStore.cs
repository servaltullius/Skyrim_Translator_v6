using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XTranslatorAi.App.Services;

public sealed class AppSettingsStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly string _settingsPath;

    public AppSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? GetDefaultSettingsPath();
    }

    public AppSettings Load()
    {
        try
        {
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            var json = File.ReadAllText(_settingsPath);
            return JsonSerializer.Deserialize<AppSettings>(json, JsonOptions) ?? new AppSettings();
        }
        catch
        {
            return new AppSettings();
        }
    }

    public void Save(AppSettings settings)
    {
        var dir = Path.GetDirectoryName(_settingsPath);
        if (!string.IsNullOrWhiteSpace(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(settings, JsonOptions);
        File.WriteAllText(_settingsPath, json);
    }

    public void SaveApiKey(string apiKey)
    {
        var settings = Load() with { ApiKey = apiKey };
        Save(settings);
    }

    public void DeleteApiKey()
    {
        var settings = Load();
        if (string.IsNullOrWhiteSpace(settings.ApiKey))
        {
            return;
        }

        Save(settings with { ApiKey = null });
    }

    private static string GetDefaultSettingsPath()
    {
        var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TulliusTranslator");
        return Path.Combine(dir, "settings.json");
    }
}

public sealed record AppSettings(
    [property: JsonPropertyName("apiKey")] string? ApiKey = null,
    [property: JsonPropertyName("apiKeys")] SavedApiKey[]? ApiKeys = null,
    [property: JsonPropertyName("enableApiKeyFailover")] bool EnableApiKeyFailover = true,
    [property: JsonPropertyName("enableBookFullModelOverride")] bool EnableBookFullModelOverride = false,
    [property: JsonPropertyName("bookFullModel")] string? BookFullModel = null,
    [property: JsonPropertyName("enablePromptCache")] bool EnablePromptCache = true,
    [property: JsonPropertyName("enableQualityEscalation")] bool EnableQualityEscalation = false,
    [property: JsonPropertyName("qualityEscalationModel")] string? QualityEscalationModel = null,
    [property: JsonPropertyName("enableRiskyCandidateRerank")] bool EnableRiskyCandidateRerank = true,
    [property: JsonPropertyName("riskyCandidateCount")] int RiskyCandidateCount = 3
);

public sealed record SavedApiKey(
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("apiKey")] string ApiKey = ""
);
