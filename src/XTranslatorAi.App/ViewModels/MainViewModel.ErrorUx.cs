using System;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Diagnostics;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
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

