using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XTranslatorAi.App.Services;

public sealed class AppSettingsStore
{
    private const string DpapiPrefix = "dpapi:";
    private static readonly byte[] DpapiEntropy = Encoding.UTF8.GetBytes("TulliusTranslator.ApiKey.v1");

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private readonly string _settingsPath;
    private readonly bool _canUseDpapi;

    public AppSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? GetDefaultSettingsPath();
        _canUseDpapi = OperatingSystem.IsWindows();
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
            var persisted = JsonSerializer.Deserialize<PersistedAppSettings>(json, JsonOptions) ?? new PersistedAppSettings();
            var settings = ConvertFromPersisted(persisted, out var needsMigration);
            if (needsMigration)
            {
                try
                {
                    Save(settings);
                }
                catch
                {
                    // best-effort migration
                }
            }

            return settings;
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

        var persisted = ConvertToPersisted(settings);
        var json = JsonSerializer.Serialize(persisted, JsonOptions);
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

    private AppSettings ConvertFromPersisted(PersistedAppSettings persisted, out bool needsMigration)
    {
        needsMigration = false;

        var apiKey = ReadApiKey(persisted.ApiKeyProtected, persisted.ApiKey, ref needsMigration);
        var savedApiKeys = ReadSavedApiKeys(persisted.ApiKeys, ref needsMigration);

        return new AppSettings(
            ApiKey: string.IsNullOrWhiteSpace(apiKey) ? null : apiKey,
            ApiKeys: savedApiKeys.Count == 0 ? null : savedApiKeys.ToArray(),
            EnableApiKeyFailover: persisted.EnableApiKeyFailover,
            EnableBookFullModelOverride: persisted.EnableBookFullModelOverride,
            BookFullModel: persisted.BookFullModel,
            EnablePromptCache: persisted.EnablePromptCache,
            EnableQualityEscalation: persisted.EnableQualityEscalation,
            QualityEscalationModel: persisted.QualityEscalationModel,
            EnableRiskyCandidateRerank: persisted.EnableRiskyCandidateRerank,
            RiskyCandidateCount: persisted.RiskyCandidateCount
        );
    }

    private PersistedAppSettings ConvertToPersisted(AppSettings settings)
    {
        var normalizedApiKey = NormalizeApiKey(settings.ApiKey);
        var normalizedApiKeys = NormalizeApiKeys(settings.ApiKeys);

        if (_canUseDpapi)
        {
            return new PersistedAppSettings(
                ApiKey: null,
                ApiKeyProtected: string.IsNullOrWhiteSpace(normalizedApiKey) ? null : ProtectApiKey(normalizedApiKey),
                ApiKeys: normalizedApiKeys.Count == 0 ? null : BuildProtectedApiKeys(normalizedApiKeys).ToArray(),
                EnableApiKeyFailover: settings.EnableApiKeyFailover,
                EnableBookFullModelOverride: settings.EnableBookFullModelOverride,
                BookFullModel: settings.BookFullModel,
                EnablePromptCache: settings.EnablePromptCache,
                EnableQualityEscalation: settings.EnableQualityEscalation,
                QualityEscalationModel: settings.QualityEscalationModel,
                EnableRiskyCandidateRerank: settings.EnableRiskyCandidateRerank,
                RiskyCandidateCount: settings.RiskyCandidateCount
            );
        }

        // Fallback for non-Windows environments where DPAPI is unavailable.
        return new PersistedAppSettings(
            ApiKey: normalizedApiKey,
            ApiKeyProtected: null,
            ApiKeys: normalizedApiKeys.Count == 0 ? null : BuildLegacyApiKeys(normalizedApiKeys).ToArray(),
            EnableApiKeyFailover: settings.EnableApiKeyFailover,
            EnableBookFullModelOverride: settings.EnableBookFullModelOverride,
            BookFullModel: settings.BookFullModel,
            EnablePromptCache: settings.EnablePromptCache,
            EnableQualityEscalation: settings.EnableQualityEscalation,
            QualityEscalationModel: settings.QualityEscalationModel,
            EnableRiskyCandidateRerank: settings.EnableRiskyCandidateRerank,
            RiskyCandidateCount: settings.RiskyCandidateCount
        );
    }

    private static string? NormalizeApiKey(string? apiKey)
    {
        var trimmed = apiKey?.Trim();
        return string.IsNullOrWhiteSpace(trimmed) ? null : trimmed;
    }

    private static List<SavedApiKey> NormalizeApiKeys(SavedApiKey[]? apiKeys)
    {
        var list = new List<SavedApiKey>();
        if (apiKeys == null || apiKeys.Length == 0)
        {
            return list;
        }

        for (var i = 0; i < apiKeys.Length; i++)
        {
            var item = apiKeys[i];
            if (item == null)
            {
                continue;
            }

            var key = NormalizeApiKey(item.ApiKey);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(item.Name) ? null : item.Name.Trim();
            list.Add(new SavedApiKey(Name: name, ApiKey: key));
        }

        return list;
    }

    private string? ReadApiKey(string? protectedValue, string? legacyValue, ref bool needsMigration)
    {
        if (!string.IsNullOrWhiteSpace(protectedValue))
        {
            if (TryUnprotectApiKey(protectedValue!, out var plaintext))
            {
                return NormalizeApiKey(plaintext);
            }
        }

        var legacy = NormalizeApiKey(legacyValue);
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            needsMigration |= _canUseDpapi;
            return legacy;
        }

        return null;
    }

    private List<SavedApiKey> ReadSavedApiKeys(PersistedSavedApiKey[]? persistedKeys, ref bool needsMigration)
    {
        var list = new List<SavedApiKey>();
        if (persistedKeys == null || persistedKeys.Length == 0)
        {
            return list;
        }

        for (var i = 0; i < persistedKeys.Length; i++)
        {
            var item = persistedKeys[i];
            if (item == null)
            {
                continue;
            }

            var key = ReadApiKey(item.ApiKeyProtected, item.ApiKey, ref needsMigration);
            if (string.IsNullOrWhiteSpace(key))
            {
                continue;
            }

            var name = string.IsNullOrWhiteSpace(item.Name) ? null : item.Name.Trim();
            list.Add(new SavedApiKey(Name: name, ApiKey: key));
        }

        return list;
    }

    private IEnumerable<PersistedSavedApiKey> BuildProtectedApiKeys(List<SavedApiKey> apiKeys)
    {
        for (var i = 0; i < apiKeys.Count; i++)
        {
            yield return new PersistedSavedApiKey(
                Name: apiKeys[i].Name,
                ApiKey: null,
                ApiKeyProtected: ProtectApiKey(apiKeys[i].ApiKey)
            );
        }
    }

    private static IEnumerable<PersistedSavedApiKey> BuildLegacyApiKeys(List<SavedApiKey> apiKeys)
    {
        for (var i = 0; i < apiKeys.Count; i++)
        {
            yield return new PersistedSavedApiKey(
                Name: apiKeys[i].Name,
                ApiKey: apiKeys[i].ApiKey,
                ApiKeyProtected: null
            );
        }
    }

    private static string AddDpapiPrefix(string protectedBase64)
    {
        return DpapiPrefix + protectedBase64;
    }

    private static bool HasDpapiPrefix(string value)
    {
        return value.StartsWith(DpapiPrefix, StringComparison.Ordinal);
    }

    private static string RemoveDpapiPrefix(string value)
    {
        return value.Substring(DpapiPrefix.Length);
    }

    private string ProtectApiKey(string plaintext)
    {
        if (!_canUseDpapi)
        {
            return plaintext;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(plaintext);
            var protectedBytes = ProtectedData.Protect(bytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            return AddDpapiPrefix(Convert.ToBase64String(protectedBytes));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("Failed to protect API key with Windows DPAPI.", ex);
        }
    }

    private bool TryUnprotectApiKey(string storedValue, out string plaintext)
    {
        plaintext = "";

        if (!_canUseDpapi)
        {
            // Non-Windows fallback: treat legacy plaintext as-is, but skip DPAPI payload.
            if (HasDpapiPrefix(storedValue))
            {
                return false;
            }

            plaintext = storedValue;
            return true;
        }

        if (string.IsNullOrWhiteSpace(storedValue))
        {
            return false;
        }

        if (!HasDpapiPrefix(storedValue))
        {
            plaintext = storedValue;
            return true;
        }

        try
        {
            var base64 = RemoveDpapiPrefix(storedValue);
            var protectedBytes = Convert.FromBase64String(base64);
            var unprotectedBytes = ProtectedData.Unprotect(protectedBytes, DpapiEntropy, DataProtectionScope.CurrentUser);
            plaintext = Encoding.UTF8.GetString(unprotectedBytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private sealed record PersistedAppSettings(
        [property: JsonPropertyName("apiKey")] string? ApiKey = null,
        [property: JsonPropertyName("apiKeyProtected")] string? ApiKeyProtected = null,
        [property: JsonPropertyName("apiKeys")] PersistedSavedApiKey[]? ApiKeys = null,
        [property: JsonPropertyName("enableApiKeyFailover")] bool EnableApiKeyFailover = true,
        [property: JsonPropertyName("enableBookFullModelOverride")] bool EnableBookFullModelOverride = false,
        [property: JsonPropertyName("bookFullModel")] string? BookFullModel = null,
        [property: JsonPropertyName("enablePromptCache")] bool EnablePromptCache = true,
        [property: JsonPropertyName("enableQualityEscalation")] bool EnableQualityEscalation = false,
        [property: JsonPropertyName("qualityEscalationModel")] string? QualityEscalationModel = null,
        [property: JsonPropertyName("enableRiskyCandidateRerank")] bool EnableRiskyCandidateRerank = true,
        [property: JsonPropertyName("riskyCandidateCount")] int RiskyCandidateCount = 3
    );

    private sealed record PersistedSavedApiKey(
        [property: JsonPropertyName("name")] string? Name = null,
        [property: JsonPropertyName("apiKey")] string? ApiKey = null,
        [property: JsonPropertyName("apiKeyProtected")] string? ApiKeyProtected = null
    );
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
