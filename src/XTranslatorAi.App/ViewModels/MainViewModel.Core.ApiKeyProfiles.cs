using System;
using XTranslatorAi.App.Services;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    partial void OnSelectedSavedApiKeyChanged(SavedApiKeyViewModel? value)
    {
        if (value == null)
        {
            return;
        }

        ApiKey = value.ApiKey ?? "";
    }

    /// <summary>Saves the key list and the current key; false when they could not be saved.</summary>
    private bool PersistSavedApiKeys()
    {
        if (_settingsUnreadAtStart)
        {
            return false;
        }

        try
        {
            var current = _appSettings.Load();
            var keys = new SavedApiKey[SavedApiKeys.Count];
            for (var i = 0; i < SavedApiKeys.Count; i++)
            {
                keys[i] = new SavedApiKey(
                    Name: string.IsNullOrWhiteSpace(SavedApiKeys[i].Name) ? null : SavedApiKeys[i].Name.Trim(),
                    ApiKey: SavedApiKeys[i].ApiKey?.Trim() ?? ""
                );
            }

            _appSettings.Save(
                current with
                {
                    ApiKey = string.IsNullOrWhiteSpace(ApiKey) ? null : ApiKey.Trim(),
                    ApiKeys = keys.Length == 0 ? null : keys,
                }
            );

            HasSavedApiKey = keys.Length > 0;
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Write($"WARN API 키 목록을 저장하지 못했습니다: {ex.Message}");
            return false;
        }
    }

    private const string KeysNotSavedMessage =
        "API 키를 이번 실행에서만 씁니다. 설정 파일을 읽지 못한 채 시작했거나 저장하지 못해, 저장된 키를 덮어쓰지 않으려고 저장하지 않았습니다. 앱을 다시 시작한 뒤 저장하세요.";

    private string GenerateDefaultSavedKeyName()
    {
        var idx = SavedApiKeys.Count + 1;
        return $"Key {idx}";
    }
}
