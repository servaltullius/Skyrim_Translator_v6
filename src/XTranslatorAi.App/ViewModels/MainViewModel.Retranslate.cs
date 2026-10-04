using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.ViewModels;

/// <summary>
/// Translations are saved to the project DB as they arrive, so reopening a plugin shows them again and
/// Start only picks up pending rows. These commands put finished rows back in the queue; the next
/// Start translates them, so nothing is spent until the user starts.
/// </summary>
public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanRetranslate))]
    private Task RetranslateSelectedAsync(IList? selectedRows)
        => RetranslateAsync(selectedRows?.OfType<StringEntryViewModel>().ToList() ?? new List<StringEntryViewModel>(), "선택한 행");

    [RelayCommand(CanExecute = nameof(CanRetranslate))]
    private Task RetranslateVisibleAsync()
        => RetranslateAsync(EntriesView.OfType<StringEntryViewModel>().ToList(), "목록에 보이는 행");

    private bool CanRetranslate() => IsProjectLoaded && !IsTranslating && IsWorkspaceInteractive;

    private async Task RetranslateAsync(IReadOnlyList<StringEntryViewModel> rows, string scope)
    {
        // Save a typed edit first: it then counts as a manual edit the confirmation asks about, instead of
        // a finished row whose unsaved text is silently cleared.
        if (!await TryCommitPendingDestEditsAsync())
        {
            return;
        }

        var resettable = rows.Where(row => row.Status is StringEntryStatus.Done or StringEntryStatus.Error or StringEntryStatus.Skipped).ToList();
        var edited = rows.Where(row => row.Status == StringEntryStatus.Edited).ToList();
        if (resettable.Count == 0 && edited.Count == 0)
        {
            StatusMessage = rows.Count == 0
                ? "다시 번역할 행을 먼저 선택하세요."
                : "다시 번역할 행이 없습니다. 이미 대기 중인 행은 '번역 시작'을 누르면 번역됩니다.";
            return;
        }

        if (!TryConfirmRetranslation(scope, resettable.Count, edited.Count, out var includeEdited))
        {
            return;
        }

        var targets = includeEdited ? resettable.Concat(edited).ToList() : resettable;
        if (targets.Count == 0)
        {
            StatusMessage = "직접 수정한 행만 있어 바꾸지 않았습니다.";
            return;
        }

        await RunProjectOperationAsync("다시 번역 준비", async cancellationToken =>
        {
            var db = _projectState.Db;
            if (db == null)
            {
                return;
            }

            var reset = await db.ResetForRetranslationAsync(targets.Select(row => row.Id).ToList(), includeEdited, cancellationToken);
            if (!ReferenceEquals(db, _projectState.Db))
            {
                return;
            }

            foreach (var row in targets)
            {
                row.DestText = "";
                row.Status = StringEntryStatus.Pending;
                row.ErrorMessage = null;
                row.IsTranslationMemoryApplied = false;
            }

            DoneCount = Entries.Count(row => row.Status is StringEntryStatus.Done or StringEntryStatus.Edited);
            PendingCount = Entries.Count(row => row.Status == StringEntryStatus.Pending);
            EntriesView.Refresh();
            StatusMessage = $"{reset}개 행을 다시 번역 대기로 돌렸습니다. '번역 시작'을 누르면 번역합니다.";
        });
    }

    private bool TryConfirmRetranslation(string scope, int resettableCount, int editedCount, out bool includeEdited)
    {
        const string consequences =
            "해당 행의 번역문은 지워지고 '대기' 상태가 됩니다. '번역 시작'을 누르면 다시 번역합니다(API 비용 발생).\n"
            + "다시 번역하기 전에 ESP나 XML을 저장하면 이 행들은 번역되지 않은 채로 저장됩니다.";

        if (editedCount == 0)
        {
            includeEdited = false;
            var answer = _uiInteractionService.ShowMessage(
                $"{scope} {resettableCount}개를 다시 번역할까요?\n\n{consequences}",
                "다시 번역",
                UiMessageBoxButton.YesNo,
                UiMessageBoxImage.Question,
                UiMessageBoxResult.No
            );
            return answer == UiMessageBoxResult.Yes;
        }

        var choice = _uiInteractionService.ShowMessage(
            $"{scope} 중 다시 번역할 수 있는 행이 {resettableCount}개, 직접 수정한 행이 {editedCount}개입니다.\n\n"
            + "- 예: 직접 수정한 행도 다시 번역\n"
            + "- 아니요: 직접 수정한 행은 그대로 두기\n"
            + "- 취소: 아무것도 바꾸지 않기\n\n"
            + consequences,
            "다시 번역",
            UiMessageBoxButton.YesNoCancel,
            UiMessageBoxImage.Question,
            UiMessageBoxResult.Cancel
        );
        includeEdited = choice == UiMessageBoxResult.Yes;
        return choice is UiMessageBoxResult.Yes or UiMessageBoxResult.No;
    }
}
