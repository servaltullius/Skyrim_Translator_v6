using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using XTranslatorAi.Core.Translation;

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
    private int _undecryptableKeyCount;
    private readonly object _sync = new();
    private bool _existingFileUnread;

    public AppSettingsStore(string? settingsPath = null)
    {
        _settingsPath = settingsPath ?? GetDefaultSettingsPath();
        _canUseDpapi = OperatingSystem.IsWindows();
    }

    /// <summary>Why the last <see cref="Load"/> started from defaults although a settings file exists; null otherwise.</summary>
    public string? LoadWarning { get; private set; }

    /// <summary>The last <see cref="Load"/> could not read an existing settings file, so it returned defaults.</summary>
    public bool LastLoadLeftFileUnread { get { lock (_sync) return _existingFileUnread; } }

    /// <summary>
    /// Every caller saves by Load → change → Save. Load used to return defaults on any error, so a settings file
    /// that was briefly locked (antivirus, a sync client) or cut short by a crash during a save came back as
    /// defaults and the next save wrote them over it, deleting the stored API keys. A missing file still means
    /// defaults. A file that cannot be read is left alone and saving is refused until it can be read again; a
    /// file that reads but is not valid settings is moved aside to settings.json.corrupt-&lt;time&gt; first.
    /// </summary>
    public AppSettings Load()
    {
        lock (_sync)
        {
            LoadWarning = null;
            _existingFileUnread = false;
            if (!File.Exists(_settingsPath))
            {
                return new AppSettings();
            }

            string json;
            try
            {
                json = ReadSettingsText();
            }
            catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
            {
                return new AppSettings();
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _existingFileUnread = true;
                LoadWarning = "설정 파일을 읽지 못해(다른 프로그램이 사용 중일 수 있음) 기본 설정으로 시작했습니다. "
                    + "저장된 API 키와 설정을 지키려고, 앱을 다시 시작할 때까지 설정과 API 키를 저장하지 않습니다.";
                AppLog.Write($"WARN 설정 파일을 읽지 못해 저장을 보류합니다: {_settingsPath}: {ex.Message}");
                return new AppSettings();
            }

            PersistedAppSettings persisted;
            try
            {
                persisted = JsonSerializer.Deserialize<PersistedAppSettings>(json, JsonOptions)
                    ?? throw new JsonException("settings.json holds no settings object.");
            }
            catch (JsonException ex)
            {
                MoveAsideUnreadableFile(ex);
                return new AppSettings();
            }

            _undecryptableKeyCount = 0;
            var settings = NormalizeTranslationPreferences(ConvertFromPersisted(persisted, out var needsMigration));
            if (_undecryptableKeyCount > 0)
            {
                LoadWarning = $"저장된 API 키 {_undecryptableKeyCount}개를 이 Windows 계정에서 풀 수 없어 비워 두었습니다"
                    + "(다른 PC나 계정에서 옮긴 설정일 수 있음). 키를 다시 입력하세요.";
                AppLog.Write($"WARN 저장된 API 키 {_undecryptableKeyCount}개를 DPAPI로 풀지 못했습니다: {_settingsPath}");
            }

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
    }

    /// <summary>
    /// Writes a temporary file next to the settings and swaps it in, so a crash or a full disk during the write
    /// leaves the previous file intact instead of an empty or half-written one.
    /// </summary>
    public void Save(AppSettings settings)
    {
        lock (_sync)
        {
            if (_existingFileUnread)
            {
                // These settings started from defaults; writing them would replace the stored API keys.
                throw new IOException("설정 파일을 읽지 못해 저장하지 않았습니다. 기존 API 키를 덮어쓰지 않으려는 것입니다.");
            }

            var dir = Path.GetDirectoryName(Path.GetFullPath(_settingsPath));
            if (!string.IsNullOrWhiteSpace(dir))
            {
                Directory.CreateDirectory(dir);
            }

            var persisted = ConvertToPersisted(NormalizeTranslationPreferences(settings));
            var bytes = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false).GetBytes(JsonSerializer.Serialize(persisted, JsonOptions));
            var temporary = _settingsPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
            try
            {
                using (var stream = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough))
                {
                    stream.Write(bytes);
                    stream.Flush(flushToDisk: true);
                }

                File.Move(temporary, _settingsPath, overwrite: true);
            }
            finally
            {
                TryDelete(temporary);
            }
        }
    }

    private string ReadSettingsText()
    {
        // A sharing violation from a scanner or sync client usually clears within moments.
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                return File.ReadAllText(_settingsPath);
            }
            catch (IOException ex) when (attempt < 3 && ex is not (FileNotFoundException or DirectoryNotFoundException))
            {
                Thread.Sleep(50 * attempt);
            }
        }
    }

    private void MoveAsideUnreadableFile(Exception parseError)
    {
        var backup = $"{_settingsPath}.corrupt-{DateTime.Now:yyyyMMdd-HHmmss}";
        if (File.Exists(backup))
        {
            backup += "-" + Guid.NewGuid().ToString("N")[..8];
        }

        try
        {
            File.Move(_settingsPath, backup);
            LoadWarning = $"설정 파일을 읽을 수 없어 {Path.GetFileName(backup)}(으)로 옮겨 두고 기본 설정으로 시작했습니다. "
                + "저장했던 API 키는 다시 입력해야 할 수 있습니다.";
            AppLog.Write($"WARN 설정 파일이 손상돼 {backup}(으)로 옮겼습니다: {parseError.Message}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Not moved, so not backed up: keep it and refuse to save over it.
            _existingFileUnread = true;
            LoadWarning = "설정 파일을 읽을 수 없고 옮기지도 못해 기본 설정으로 시작했습니다. 그 파일을 덮어쓰지 않도록 설정을 저장하지 않습니다.";
            AppLog.Write($"WARN 손상된 설정 파일을 옮기지 못해 저장을 보류합니다: {_settingsPath}: {parseError.Message}; {ex.Message}");
        }
    }

    private static void TryDelete(string path)
    {
        try
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // A leftover temporary file is harmless.
        }
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
            EnableBookBodyModelOverride: persisted.EnableBookBodyModelOverride,
            BookFullModel: persisted.BookFullModel,
            EnablePromptCache: persisted.EnablePromptCache,
            EnableQualityEscalation: persisted.EnableQualityEscalation,
            QualityEscalationModel: persisted.QualityEscalationModel,
            EnableRiskyCandidateRerank: persisted.EnableRiskyCandidateRerank,
            RiskyCandidateCount: persisted.RiskyCandidateCount,
            SelectedModel: persisted.SelectedModel,
            BatchSize: persisted.BatchSize,
            MaxCharsPerBatch: persisted.MaxCharsPerBatch,
            MaxParallelRequests: persisted.MaxParallelRequests,
            MaxOutputTokensOverride: persisted.MaxOutputTokensOverride,
            EnableRepairPass: persisted.EnableRepairPass,
            SemanticRepairMode: persisted.SemanticRepairMode,
            KeepSkyrimTagsRaw: persisted.KeepSkyrimTagsRaw,
            EnableDialogueContextWindow: persisted.EnableDialogueContextWindow,
            EnableSessionTermMemory: persisted.EnableSessionTermMemory,
            UseRecStyleHints: persisted.UseRecStyleHints,
            EnableTemplateFixer: persisted.EnableTemplateFixer,
            EnableProjectContext: persisted.EnableProjectContext,
            EnableAdaptiveOutputBudget: persisted.EnableAdaptiveOutputBudget,
            EnableBookContext: persisted.EnableBookContext,
            MaxRetryGenerations: persisted.MaxRetryGenerations,
            MaxTotalGenerations: persisted.MaxTotalGenerations,
            PluginSourceLanguage: persisted.PluginSourceLanguage,
            PluginTargetLanguage: persisted.PluginTargetLanguage,
            PluginSourceEncoding: persisted.PluginSourceEncoding,
            PluginMetadataEncoding: persisted.PluginMetadataEncoding,
            PluginTargetEncoding: persisted.PluginTargetEncoding,
            PluginStringsDirectory: persisted.PluginStringsDirectory,
            UseCustomPrompt: persisted.UseCustomPrompt,
            CustomPromptText: persisted.CustomPromptText
        );
    }

    private PersistedAppSettings ConvertToPersisted(AppSettings settings)
    {
        var normalizedApiKey = NormalizeApiKey(settings.ApiKey);
        var normalizedApiKeys = NormalizeApiKeys(settings.ApiKeys);

        // Keys are stored DPAPI-protected on Windows and as plain text elsewhere; every other field is the same.
        var persisted = new PersistedAppSettings(
            EnableApiKeyFailover: settings.EnableApiKeyFailover,

            EnableBookFullModelOverride: settings.EnableBookFullModelOverride,

            EnableBookBodyModelOverride: settings.EnableBookBodyModelOverride,

            BookFullModel: settings.BookFullModel,

            EnablePromptCache: settings.EnablePromptCache,

            EnableQualityEscalation: settings.EnableQualityEscalation,

            QualityEscalationModel: settings.QualityEscalationModel,

            EnableRiskyCandidateRerank: settings.EnableRiskyCandidateRerank,

            RiskyCandidateCount: settings.RiskyCandidateCount,

            SelectedModel: settings.SelectedModel,

            BatchSize: settings.BatchSize,

            MaxCharsPerBatch: settings.MaxCharsPerBatch,

            MaxParallelRequests: settings.MaxParallelRequests,

            MaxOutputTokensOverride: settings.MaxOutputTokensOverride,

            EnableRepairPass: settings.EnableRepairPass,

            SemanticRepairMode: settings.SemanticRepairMode,

            KeepSkyrimTagsRaw: settings.KeepSkyrimTagsRaw,

            EnableDialogueContextWindow: settings.EnableDialogueContextWindow,

            EnableSessionTermMemory: settings.EnableSessionTermMemory,

            UseRecStyleHints: settings.UseRecStyleHints,

            EnableTemplateFixer: settings.EnableTemplateFixer,

            EnableProjectContext: settings.EnableProjectContext,

            EnableAdaptiveOutputBudget: settings.EnableAdaptiveOutputBudget,

            EnableBookContext: settings.EnableBookContext,

            MaxRetryGenerations: settings.MaxRetryGenerations,

            MaxTotalGenerations: settings.MaxTotalGenerations,

            PluginSourceLanguage: settings.PluginSourceLanguage,

            PluginTargetLanguage: settings.PluginTargetLanguage,

            PluginSourceEncoding: settings.PluginSourceEncoding,

            PluginMetadataEncoding: settings.PluginMetadataEncoding,

            PluginTargetEncoding: settings.PluginTargetEncoding,

            PluginStringsDirectory: settings.PluginStringsDirectory,
            UseCustomPrompt: settings.UseCustomPrompt,
            CustomPromptText: string.IsNullOrWhiteSpace(settings.CustomPromptText) ? null : settings.CustomPromptText
        );

        return _canUseDpapi
            ? persisted with
            {
                ApiKeyProtected = string.IsNullOrWhiteSpace(normalizedApiKey) ? null : ProtectApiKey(normalizedApiKey),
                ApiKeys = normalizedApiKeys.Count == 0 ? null : BuildProtectedApiKeys(normalizedApiKeys).ToArray(),
            }
            : persisted with
            {
                ApiKey = normalizedApiKey,
                ApiKeys = normalizedApiKeys.Count == 0 ? null : BuildLegacyApiKeys(normalizedApiKeys).ToArray(),
            };
    }

    private static AppSettings NormalizeTranslationPreferences(AppSettings settings) => settings with
    {
        SelectedModel = string.IsNullOrWhiteSpace(settings.SelectedModel) ? null : settings.SelectedModel.Trim(),
        BatchSize = Math.Clamp(settings.BatchSize, 1, 100),
        MaxCharsPerBatch = Math.Clamp(settings.MaxCharsPerBatch, 1000, 50000),
        MaxParallelRequests = Math.Clamp(settings.MaxParallelRequests, 1, 8),
        MaxOutputTokensOverride = settings.MaxOutputTokensOverride <= 0 ? 0 : Math.Clamp(settings.MaxOutputTokensOverride, 256, 65536),
        RiskyCandidateCount = Math.Clamp(settings.RiskyCandidateCount, 2, 8),
        SemanticRepairMode = Enum.IsDefined(settings.SemanticRepairMode) ? settings.SemanticRepairMode : PlaceholderSemanticRepairMode.Soft,
        MaxRetryGenerations = Math.Clamp(settings.MaxRetryGenerations, 0, 100),
        MaxTotalGenerations = Math.Max(0, settings.MaxTotalGenerations),
    };

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

        if (!string.IsNullOrWhiteSpace(protectedValue))
        {
            _undecryptableKeyCount++;
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
        [property: JsonPropertyName("riskyCandidateCount")] int RiskyCandidateCount = 3,
        [property: JsonPropertyName("enableBookBodyModelOverride")] bool EnableBookBodyModelOverride = false,
        [property: JsonPropertyName("selectedModel")] string? SelectedModel = null,
        [property: JsonPropertyName("batchSize")] int BatchSize = 12,
        [property: JsonPropertyName("maxCharsPerBatch")] int MaxCharsPerBatch = 15000,
        [property: JsonPropertyName("maxParallelRequests")] int MaxParallelRequests = 2,
        [property: JsonPropertyName("maxOutputTokensOverride")] int MaxOutputTokensOverride = 0,
        [property: JsonPropertyName("enableRepairPass")] bool EnableRepairPass = true,
        [property: JsonPropertyName("semanticRepairMode")] PlaceholderSemanticRepairMode SemanticRepairMode = PlaceholderSemanticRepairMode.Soft,
        [property: JsonPropertyName("keepSkyrimTagsRaw")] bool KeepSkyrimTagsRaw = true,
        [property: JsonPropertyName("enableDialogueContextWindow")] bool EnableDialogueContextWindow = true,
        [property: JsonPropertyName("enableSessionTermMemory")] bool EnableSessionTermMemory = true,
        [property: JsonPropertyName("useRecStyleHints")] bool UseRecStyleHints = true,
        [property: JsonPropertyName("enableTemplateFixer")] bool EnableTemplateFixer = false,
        [property: JsonPropertyName("enableProjectContext")] bool EnableProjectContext = true,
        [property: JsonPropertyName("enableAdaptiveOutputBudget")] bool EnableAdaptiveOutputBudget = false,
        [property: JsonPropertyName("enableBookContext")] bool EnableBookContext = false,
        [property: JsonPropertyName("maxRetryGenerations")] int MaxRetryGenerations = 8,
        [property: JsonPropertyName("maxTotalGenerations")] int MaxTotalGenerations = 0,
        [property: JsonPropertyName("pluginSourceLanguage")] string PluginSourceLanguage = "english",
        [property: JsonPropertyName("pluginTargetLanguage")] string PluginTargetLanguage = "korean",
        [property: JsonPropertyName("pluginSourceEncoding")] string PluginSourceEncoding = "utf-8",
        [property: JsonPropertyName("pluginMetadataEncoding")] string PluginMetadataEncoding = "windows-1252",
        [property: JsonPropertyName("pluginTargetEncoding")] string PluginTargetEncoding = "utf-8",
        [property: JsonPropertyName("pluginStringsDirectory")] string PluginStringsDirectory = "",
        [property: JsonPropertyName("useCustomPrompt")] bool UseCustomPrompt = false,
        [property: JsonPropertyName("customPromptText")] string? CustomPromptText = null
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
    [property: JsonPropertyName("riskyCandidateCount")] int RiskyCandidateCount = 3,
    [property: JsonPropertyName("enableBookBodyModelOverride")] bool EnableBookBodyModelOverride = false,
    [property: JsonPropertyName("selectedModel")] string? SelectedModel = null,
    [property: JsonPropertyName("batchSize")] int BatchSize = 12,
    [property: JsonPropertyName("maxCharsPerBatch")] int MaxCharsPerBatch = 15000,
    [property: JsonPropertyName("maxParallelRequests")] int MaxParallelRequests = 2,
    [property: JsonPropertyName("maxOutputTokensOverride")] int MaxOutputTokensOverride = 0,
    [property: JsonPropertyName("enableRepairPass")] bool EnableRepairPass = true,
    [property: JsonPropertyName("semanticRepairMode")] PlaceholderSemanticRepairMode SemanticRepairMode = PlaceholderSemanticRepairMode.Soft,
    [property: JsonPropertyName("keepSkyrimTagsRaw")] bool KeepSkyrimTagsRaw = true,
    [property: JsonPropertyName("enableDialogueContextWindow")] bool EnableDialogueContextWindow = true,
    [property: JsonPropertyName("enableSessionTermMemory")] bool EnableSessionTermMemory = true,
    [property: JsonPropertyName("useRecStyleHints")] bool UseRecStyleHints = true,
    [property: JsonPropertyName("enableTemplateFixer")] bool EnableTemplateFixer = false,
    [property: JsonPropertyName("enableProjectContext")] bool EnableProjectContext = true,
    [property: JsonPropertyName("enableAdaptiveOutputBudget")] bool EnableAdaptiveOutputBudget = false,
    [property: JsonPropertyName("enableBookContext")] bool EnableBookContext = false,
    [property: JsonPropertyName("maxRetryGenerations")] int MaxRetryGenerations = 8,
    [property: JsonPropertyName("maxTotalGenerations")] int MaxTotalGenerations = 0,
    [property: JsonPropertyName("pluginSourceLanguage")] string PluginSourceLanguage = "english",
    [property: JsonPropertyName("pluginTargetLanguage")] string PluginTargetLanguage = "korean",
    [property: JsonPropertyName("pluginSourceEncoding")] string PluginSourceEncoding = "utf-8",
    [property: JsonPropertyName("pluginMetadataEncoding")] string PluginMetadataEncoding = "windows-1252",
    [property: JsonPropertyName("pluginTargetEncoding")] string PluginTargetEncoding = "utf-8",
    [property: JsonPropertyName("pluginStringsDirectory")] string PluginStringsDirectory = "",
    [property: JsonPropertyName("useCustomPrompt")] bool UseCustomPrompt = false,
    [property: JsonPropertyName("customPromptText")] string? CustomPromptText = null
);

public sealed record SavedApiKey(
    [property: JsonPropertyName("name")] string? Name = null,
    [property: JsonPropertyName("apiKey")] string ApiKey = ""
);
