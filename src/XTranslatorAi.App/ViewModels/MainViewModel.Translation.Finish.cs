using System;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private async Task FinishTranslationUiStateAsync(bool canceled, Exception? error)
    {
        IsTranslating = false;
        IsPaused = false;
        if (!canceled && error == null)
        {
            StatusMessage = "Translation finished.";
        }

        try
        {
            await RefreshTmHitFlagsAsync(CancellationToken.None);
        }
        catch
        {
            // ignore
        }

        if (!HasDirtyGlossary())
        {
            try
            {
                await ReloadGlossaryAsync();
            }
            catch
            {
                // ignore
            }
        }
    }

    private bool HasDirtyGlossary()
    {
        foreach (var g in Glossary)
        {
            if (g.IsDirty)
            {
                return true;
            }
        }

        return false;
    }
}

