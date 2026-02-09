using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand]
    private void ClearApiCallLogs()
    {
        _apiCallLogService.Clear();
        OnPropertyChanged(nameof(ApiCallLogTotalsSummary));
        OnPropertyChanged(nameof(ApiCallLogTotalsToolTip));
    }

    public string ApiCallLogTotalsSummary => _apiCallLogService.TotalsSummary;
    public string ApiCallLogTotalsToolTip => _apiCallLogService.TotalsToolTip;

    [RelayCommand]
    private void SaveApiKey()
    {
        var key = ApiKey?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(key))
        {
            StatusMessage = "API 키가 비어있습니다.";
            return;
        }

        var existing = (SavedApiKeyViewModel?)null;
        foreach (var k in SavedApiKeys)
        {
            if (string.Equals(k.ApiKey, key, System.StringComparison.Ordinal))
            {
                existing = k;
                break;
            }
        }

        if (existing == null)
        {
            var name = (SavedApiKeyName ?? "").Trim();
            if (string.IsNullOrWhiteSpace(name))
            {
                name = GenerateDefaultSavedKeyName();
            }

            existing = new SavedApiKeyViewModel { Name = name, ApiKey = key };
            SavedApiKeys.Add(existing);
        }
        else if (!string.IsNullOrWhiteSpace(SavedApiKeyName))
        {
            existing.Name = SavedApiKeyName.Trim();
        }

        SelectedSavedApiKey = existing;
        PersistSavedApiKeys();
        SavedApiKeyName = "";
        StatusMessage = "API 키를 목록에 저장했습니다.";
    }

    [RelayCommand]
    private void ClearSavedApiKey()
    {
        var selected = SelectedSavedApiKey;
        if (selected == null && !string.IsNullOrWhiteSpace(ApiKey))
        {
            var currentKey = ApiKey.Trim();
            foreach (var k in SavedApiKeys)
            {
                if (string.Equals(k.ApiKey, currentKey, System.StringComparison.Ordinal))
                {
                    selected = k;
                    break;
                }
            }
        }

        if (selected == null)
        {
            StatusMessage = "삭제할 저장 키가 선택되지 않았습니다.";
            return;
        }

        SavedApiKeys.Remove(selected);
        if (ReferenceEquals(SelectedSavedApiKey, selected))
        {
            SelectedSavedApiKey = null;
        }

        PersistSavedApiKeys();
        StatusMessage = "저장된 API 키를 목록에서 삭제했습니다.";
    }

    [RelayCommand]
    private void OnApiCallLog(GeminiCallLogEntry entry)
    {
        var keyLabel = TryResolveSavedKeyDisplayLabel(entry.ApiKeyMask, SavedApiKeys);
        if (keyLabel == null && !string.IsNullOrWhiteSpace(entry.ApiKeyMask))
        {
            keyLabel = "Manual (" + entry.ApiKeyMask + ")";
        }

        OnApiCallLog(
            new ApiCallLogRow(
                StartedAt: entry.StartedAt,
                Duration: entry.Duration,
                Provider: "Gemini",
                Operation: entry.Operation.ToString(),
                ModelName: entry.ModelName,
                ApiKeyLabel: keyLabel,
                StatusCode: entry.StatusCode,
                Success: entry.Success,
                ErrorMessage: entry.ErrorMessage,
                PromptTokens: entry.PromptTokens,
                CompletionTokens: entry.CompletionTokens,
                TotalTokens: entry.TotalTokens,
                CostUsd: entry.CostUsd
            )
        );
    }

    private static string? TryResolveSavedKeyDisplayLabel(string? apiKeyMask, System.Collections.Generic.IEnumerable<SavedApiKeyViewModel> savedKeys)
    {
        if (string.IsNullOrWhiteSpace(apiKeyMask))
        {
            return null;
        }

        foreach (var k in savedKeys)
        {
            if (k == null)
            {
                continue;
            }

            var mask = MaskApiKey(k.ApiKey);
            if (string.Equals(mask, apiKeyMask.Trim(), System.StringComparison.Ordinal))
            {
                return k.DisplayLabel;
            }
        }

        return null;
    }

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "…";
        }

        var trimmed = apiKey.Trim();
        if (trimmed.Length <= 6)
        {
            return "…" + trimmed;
        }

        return "…" + trimmed.Substring(trimmed.Length - 6);
    }

    private void OnApiCallLog(ApiCallLogRow row)
    {
        if (!EnableApiCallLogging)
        {
            return;
        }

        _ = DispatchAsync(
            () =>
            {
                _apiCallLogService.Add(row);
                OnPropertyChanged(nameof(ApiCallLogTotalsSummary));
                OnPropertyChanged(nameof(ApiCallLogTotalsToolTip));
            }
        );
    }

    private sealed class UiGeminiCallLogger : IGeminiCallLogger
    {
        private readonly MainViewModel _vm;

        public UiGeminiCallLogger(MainViewModel vm)
        {
            _vm = vm;
        }

        public void Log(GeminiCallLogEntry entry)
        {
            _vm.OnApiCallLog(entry);
        }
    }
}
