using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.App.Services;

namespace XTranslatorAi.App.ViewModels;

/// <summary>
/// Edits typed into the 용어집, 전체 용어집 and 시리즈 TM grids stay in the rows (변경됨) until "저장". Adding a term,
/// importing a file and opening a project reload those lists from the DB, and used to drop such edits without a
/// word, although the end of a run already skips its glossary reload while edits are unsaved. These actions now ask
/// first, as the game-series switch does: 예 saves the edits into the DB the rows came from and continues, 아니요
/// cancels the action.
/// </summary>
public partial class MainViewModel
{
    [Flags]
    private enum EditableLists
    {
        None = 0,
        ProjectGlossary = 1,
        GlobalGlossary = 2,
        FranchiseTranslationMemory = 4,
        All = ProjectGlossary | GlobalGlossary | FranchiseTranslationMemory,
    }

    /// <summary>
    /// Asks before <paramref name="lists"/> are reloaded over unsaved grid edits and saves them on 예. False when the
    /// user cancelled or a save failed; the caller then stops instead of reloading.
    /// </summary>
    /// <param name="whenReloading">What reloads the lists, as "…하면" (e.g. "용어를 추가하면").</param>
    /// <param name="closing">Closing the window: 아니요 then discards the edits and closes, 취소 keeps the window.</param>
    private async Task<bool> TrySaveListEditsBeforeReloadAsync(EditableLists lists, string whenReloading, bool closing = false)
    {
        var glossary = lists.HasFlag(EditableLists.ProjectGlossary) ? Glossary.Where(g => g.IsDirty).ToList() : new();
        var global = lists.HasFlag(EditableLists.GlobalGlossary) ? GlobalGlossary.Where(g => g.IsDirty).ToList() : new();
        var memory = lists.HasFlag(EditableLists.FranchiseTranslationMemory)
            ? FranchiseTranslationMemory.Where(e => e.IsDirty).ToList()
            : new();
        if (glossary.Count + global.Count + memory.Count == 0)
        {
            return true;
        }

        var counts = new List<string>();
        if (glossary.Count > 0) counts.Add($"용어집 {glossary.Count}개");
        if (global.Count > 0) counts.Add($"전체 용어집 {global.Count}개");
        if (memory.Count > 0) counts.Add($"시리즈 TM {memory.Count}개");

        // Closing the window used to drop these edits without a word; there the user may also discard them.
        var answer = closing
            ? _uiInteractionService.ShowMessage(
                $"저장하지 않은 수정이 있습니다: {string.Join(", ", counts)}.\n\n"
                + "- 예: 수정을 저장하고 닫기\n"
                + "- 아니요: 저장하지 않고 닫기\n"
                + "- 취소: 닫지 않기",
                "저장하지 않은 수정",
                UiMessageBoxButton.YesNoCancel,
                UiMessageBoxImage.Warning,
                UiMessageBoxResult.Yes
            )
            : _uiInteractionService.ShowMessage(
                $"저장하지 않은 수정이 있습니다: {string.Join(", ", counts)}.\n\n"
                + $"{whenReloading} 목록을 다시 불러오므로 저장하지 않으면 이 수정은 사라집니다.\n\n"
                + "- 예: 수정을 저장한 뒤 진행\n"
                + "- 아니요: 취소",
                "저장하지 않은 수정",
                UiMessageBoxButton.YesNo,
                UiMessageBoxImage.Warning,
                UiMessageBoxResult.Yes
            );
        if (closing && answer == UiMessageBoxResult.No)
        {
            return true;
        }

        if (answer != UiMessageBoxResult.Yes)
        {
            StatusMessage = "저장하지 않은 용어집·TM 수정이 있어 진행하지 않았습니다.";
            return false;
        }

        try
        {
            if (glossary.Count > 0)
            {
                if (_projectState.Db is not { } db)
                {
                    StatusMessage = "프로젝트가 닫혀 용어집 수정을 저장하지 못했습니다.";
                    return false;
                }

                await SaveGlossaryRowsAsync(db, glossary);
            }

            if (global.Count > 0)
            {
                // Rows of another game's DB must not be written into this one (see _globalGlossaryRowsFranchise).
                if (!AreGlobalGlossaryRowsFromSelectedFranchise())
                {
                    return false;
                }

                await SaveGlobalGlossaryRowsAsync(global);
            }

            if (memory.Count > 0)
            {
                if (!AreFranchiseTmRowsFromSelectedFranchise())
                {
                    return false;
                }

                if (await _globalTranslationMemoryService.TryGetDbAsync(CancellationToken.None) == null)
                {
                    StatusMessage = "시리즈 TM DB를 초기화하지 못했습니다.";
                    return false;
                }

                await SaveFranchiseTranslationMemoryRowsAsync(memory);
            }
        }
        catch (Exception ex)
        {
            SetUserFacingError("용어집·TM 수정 저장", ex);
            return false;
        }

        return true;
    }
}
