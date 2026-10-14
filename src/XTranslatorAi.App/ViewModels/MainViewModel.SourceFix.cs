using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    /// <summary>
    /// Marks the rows "원문 오류 수정" or takes the mark off. The English of a marked row is wrong (a modder pasted
    /// another record's text, a typo that changes the meaning) and its translation departs from it on purpose, so
    /// the quality check stops comparing it with the English and with other rows; its tags are still checked.
    /// With the selected rows of the strings grid, or the open row when none are passed. Rows that are all marked
    /// are unmarked; otherwise all are marked. Retranslating a row removes its mark.
    /// </summary>
    [RelayCommand(CanExecute = nameof(CanRetranslate))]
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
        try
        {
            foreach (var row in rows)
            {
                if (mark)
                {
                    await db.UpsertStringNoteAsync(row.Id, TranslationConstants.SourceFixNoteKind, "원문 오류 수정", CancellationToken.None);
                }
                else
                {
                    await db.DeleteStringNoteAsync(row.Id, TranslationConstants.SourceFixNoteKind, CancellationToken.None);
                }

                row.IsSourceFix = mark;
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

        StatusMessage = mark
            ? $"{rows.Count}개 행을 원문 오류 수정으로 표시했습니다. 품질 검사는 이 행들의 태그만 확인합니다."
            : $"{rows.Count}개 행의 원문 오류 수정 표시를 해제했습니다. 다른 행과 비교하는 검사는 다음 검사 때 반영됩니다.";
        try
        {
            if (IsLqaScanning)
            {
                await RecheckLqaRowsAsync(rows);
            }
            else if (LqaIssues.Count > 0 || !mark)
            {
                // An unmarked row has no results in the list, so it is checked whether or not it is listed.
                await RecheckLqaRowsCoreAsync(rows);
            }
        }
        catch (Exception ex)
        {
            AppLog.Write($"WARN 원문 오류 수정 표시 후 다시 검사하지 못했습니다: {ex.Message}");
        }
    }
}
