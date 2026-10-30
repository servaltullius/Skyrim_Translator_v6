using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Marks the rows "원문 오류 수정" or takes the mark off. The English of a marked row is wrong (a modder pasted
    /// another record's text, a typo that changes the meaning) and its translation departs from it on purpose, so
    /// the quality check stops comparing it with the English and with other rows; its tags are still checked.
    /// With the selected rows of the strings grid, or the open row when none are passed. Rows that are all marked
    /// are unmarked; otherwise all are marked. A marked row's translation is taken out of the project TM. Retranslating
    /// a row removes its mark.
    /// </summary>
    // Not CanRetranslate: marking during a quality check is allowed (the row is checked again when it ends).
    [RelayCommand(CanExecute = nameof(CanToggleSourceFix))]
    private async Task ToggleSourceFixAsync(IList? selectedRows)
    {
        var db = _projectState.Db;
        var rows = selectedRows?.OfType<StringEntryViewModel>().ToList() is { Count: > 0 } selected
            ? selected
            : SelectedEntry is { } open ? new List<StringEntryViewModel> { open } : new List<StringEntryViewModel>();
        if (db == null || rows.Count == 0)
        {
            StatusMessage = "원문 오류 수정으로 표시할 행을 먼저 선택하세요.";
            return;
        }

        var mark = !rows.All(row => row.IsSourceFix);
        // Only a translated row is marked (ProjectDb.SetSourceFixAsync): a 대기 row marked kept the mark on the model's
        // translation, and the check skipped it.
        var untranslated = mark ? rows.Where(row => !row.IsSourceFix && !CanMarkSourceFix(row)).ToList() : new List<StringEntryViewModel>();
        rows = rows.Except(untranslated).ToList();
        if (rows.Count == 0)
        {
            StatusMessage = "번역이 없는 행은 원문 오류 수정으로 표시할 수 없습니다. 번역(또는 직접 수정)한 뒤 표시하세요.";
            return;
        }

        var changed = new List<StringEntryViewModel>();
        try
        {
            foreach (var row in rows)
            {
                var marked = await db.SetSourceFixAsync(row.Id, mark, CancellationToken.None);
                row.IsSourceFix = marked;
                row.SourceFixNeedsCheck = false;
                if (marked == mark) changed.Add(row);
            }
        }
        catch (Exception ex)
        {
            SetUserFacingError("원문 오류 수정 표시", ex);
            return;
        }

        if (!ReferenceEquals(db, _projectState.Db))
        {
            return;
        }

        var notMarked = untranslated.Count + rows.Count - changed.Count;
        rows = changed;
        StatusMessage = (mark
            ? $"{rows.Count}개 행을 원문 오류 수정으로 표시했습니다. 품질 검사는 이 행들의 태그만 확인합니다."
            : $"{rows.Count}개 행의 원문 오류 수정 표시를 해제했습니다. 다른 행과 비교하는 검사는 다음 검사 때 반영됩니다.")
            + (notMarked > 0 ? $" 번역이 없는 {notMarked}개 행은 표시하지 않았습니다." : "");
        if (rows.Count == 0)
        {
            return;
        }
        try
        {
            if (IsLqaScanning)
            {
                await RecheckLqaRowsAsync(rows);
            }
            else if (_lqaHasFullScan)
            {
                // An unmarked row has no results in the list, so it is checked whether or not it is listed. Not without a
                // whole check: in a project never checked (or cleared) its results alone showed as "전체 N건".
                await RecheckLqaRowsCoreAsync(rows);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"WARN 원문 오류 수정 표시 후 다시 검사하지 못했습니다: {ex.Message}");
        }
    }

    private bool CanToggleSourceFix() => IsProjectLoaded && !IsTranslating && IsWorkspaceInteractive;

    /// <summary>A translated row, as the quality check reads it (an XML's row that came with its translation too), not held.</summary>
    private static bool CanMarkSourceFix(StringEntryViewModel row)
        => LqaStatus(row) is StringEntryStatus.Done or StringEntryStatus.Edited && !string.IsNullOrEmpty(row.DestText);
}
