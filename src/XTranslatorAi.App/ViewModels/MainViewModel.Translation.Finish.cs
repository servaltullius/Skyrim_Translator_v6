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
            StatusMessage = "번역을 중지했습니다. '번역 시작'을 누르면 미완료 항목부터 이어서 번역합니다.";
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

        // The run adjusted the counters row by row; recount once the stopped rows are back to pending.
        RecountProgress();

        if (!canceled && error == null)
        {
            StatusMessage = DescribeFinishedRun();
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

    /// <summary>
    /// "번역을 마쳤습니다." alone hid failed rows: a run that left 300 rows in error looked like a clean finish.
    /// </summary>
    private string DescribeFinishedRun()
    {
        var errors = 0;
        var pending = 0;
        foreach (var entry in Entries)
        {
            if (entry.Status == StringEntryStatus.Error) errors++;
            else if (entry.Status == StringEntryStatus.Pending) pending++;
        }

        if (errors == 0 && pending == 0)
        {
            return "번역을 마쳤습니다.";
        }

        var parts = new System.Collections.Generic.List<string>();
        if (errors > 0) parts.Add($"오류 {errors}개");
        if (pending > 0) parts.Add($"남은 행 {pending}개");
        return $"번역을 마쳤습니다. {string.Join(", ", parts)}가 있습니다."
            + (errors > 0 ? " 상태 필터에서 '오류'를 골라 확인하거나 '번역 시작'으로 다시 번역하세요." : " '번역 시작'을 누르면 이어서 번역합니다.");
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
