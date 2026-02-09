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

    private void PersistSavedApiKeys()
    {
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
        }
        catch
        {
            // ignore
        }
    }

    private string GenerateDefaultSavedKeyName()
    {
        var idx = SavedApiKeys.Count + 1;
        return $"Key {idx}";
    }
}
