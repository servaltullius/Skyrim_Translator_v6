using System;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private async Task FinishTranslationUiStateAsync(bool canceled, Exception? error)
    {
        // Flush this run's queued notifications before completing or allowing a project switch.
        while (!_rowUpdates.IsEmpty)
        {
            DrainRowUpdates();
        }

        if (canceled)
        {
            StatusMessage = "번역을 중지했습니다. Start를 누르면 미완료 항목부터 이어서 번역합니다.";
        }
        else if (error == null)
        {
            StatusMessage = "Translation finished.";
        }

        try
        {
            if (_projectState.Db is { } db)
            {
                await db.ResetInProgressToPendingAsync(CancellationToken.None);
                foreach (var entry in Entries)
                {
                    if (entry.Status == StringEntryStatus.InProgress)
                    {
                        entry.Status = StringEntryStatus.Pending;
                    }
                }
            }
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
