using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    Task ITranslationRunnerStatusPort.DispatchAsync(Action action) => DispatchAsync(action);

    Task ITranslationRunnerFlowControlPort.OnRowUpdatedAsync(long id, StringEntryStatus status, string text)
        => OnRowUpdatedAsync(id, status, text);

    Task ITranslationRunnerFlowControlPort.WaitIfPausedAsync(CancellationToken cancellationToken)
        => WaitIfPausedAsync(cancellationToken);

    void ITranslationRunnerStatusPort.SetStatusMessage(string message) => StatusMessage = message;

    void ITranslationRunnerStatusPort.SetUserFacingError(string operation, Exception ex) => SetUserFacingError(operation, ex);

    IReadOnlyList<TranslationRunnerSavedApiKey> ITranslationRunnerFailoverPort.SavedApiKeys
    {
        get
        {
            var keys = new List<TranslationRunnerSavedApiKey>(SavedApiKeys.Count);
            foreach (var k in SavedApiKeys)
            {
                if (k == null || string.IsNullOrWhiteSpace(k.ApiKey))
                {
                    continue;
                }

                keys.Add(
                    new TranslationRunnerSavedApiKey(
                        Name: k.Name ?? "",
                        ApiKey: k.ApiKey.Trim()
                    )
                );
            }

            return keys;
        }
    }

    void ITranslationRunnerFailoverPort.SelectSavedApiKey(TranslationRunnerSavedApiKey savedKey)
    {
        if (string.IsNullOrWhiteSpace(savedKey.ApiKey))
        {
            return;
        }

        SavedApiKeyViewModel? selected = null;
        foreach (var item in SavedApiKeys)
        {
            if (item == null)
            {
                continue;
            }

            if (string.Equals(item.ApiKey?.Trim(), savedKey.ApiKey.Trim(), StringComparison.Ordinal))
            {
                selected = item;
                break;
            }
        }

        if (selected != null)
        {
            SelectedSavedApiKey = selected;
            ApiKey = selected.ApiKey?.Trim() ?? "";
            return;
        }

        ApiKey = savedKey.ApiKey.Trim();
    }
}
