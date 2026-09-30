using System;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Diagnostics;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private void SetPluginUserFacingError(string operation, Exception ex)
    {
        if (PluginUserFacingErrorClassifier.Classify(ex) is not { } error)
        {
            SetUserFacingError(operation, ex);
            return;
        }
        // Only the fixed diagnostic and validated record/StringID identifiers are user-facing.
        // Do not copy raw exception payloads or paths into either the status or this log entry.
        AppLog.Write($"ERROR {operation} ({error.Code}): {error.Message}");
        StatusMessage = $"{operation}({error.Code}): {error.Message}";
    }

    private void SetUserFacingError(string operation, Exception ex)
    {
        var error = UserFacingErrorClassifier.Classify(ex);
        if (error.Code != "E000")
        {
            AppLog.WriteError(error.Code, operation, ex);
        }

        var suffix = error.DetailsInApiLogs ? " (API Logs)" : "";
        StatusMessage = $"{operation}({error.Code}): {error.Message}{suffix}";
    }
}
