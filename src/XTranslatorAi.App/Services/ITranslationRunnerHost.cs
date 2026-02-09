using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.Services;

public interface ITranslationRunnerStatusPort
{
    Task DispatchAsync(Action action);
    void SetStatusMessage(string message);
    void SetUserFacingError(string operation, Exception ex);
}

public interface ITranslationRunnerFlowControlPort
{
    Task OnRowUpdatedAsync(long id, StringEntryStatus status, string text);
    Task WaitIfPausedAsync(CancellationToken cancellationToken);
}

public sealed record TranslationRunnerSavedApiKey(string Name, string ApiKey)
{
    public string DisplayLabel
    {
        get
        {
            var label = string.IsNullOrWhiteSpace(Name) ? "Key" : Name.Trim();
            return $"{label} ({Mask(ApiKey)})";
        }
    }

    private static string Mask(string apiKey)
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
}

public interface ITranslationRunnerFailoverPort
{
    string ApiKey { get; set; }
    bool EnableApiKeyFailover { get; }
    IReadOnlyList<TranslationRunnerSavedApiKey> SavedApiKeys { get; }
    void SelectSavedApiKey(TranslationRunnerSavedApiKey savedKey);
}

public interface ITranslationRunnerHost : ITranslationRunnerStatusPort, ITranslationRunnerFlowControlPort, ITranslationRunnerFailoverPort;
