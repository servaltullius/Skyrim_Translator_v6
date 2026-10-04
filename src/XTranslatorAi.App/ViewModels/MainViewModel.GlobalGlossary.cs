using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    // Row Ids are only meaningful in the game DB the rows were read from. Saving or deleting rows of the
    // previous game after a switch (before the reload finished, or when it failed) changed unrelated rows of
    // the new game's glossary that happened to share those Ids.
    private BethesdaFranchise _globalGlossaryRowsFranchise = BethesdaFranchise.ElderScrolls;

    private bool AreGlobalGlossaryRowsFromSelectedFranchise()
    {
        if (_globalGlossaryRowsFranchise == _globalProjectDbService.SelectedFranchise)
        {
            return true;
        }

        StatusMessage = "전체 용어집 목록이 이전 게임 시리즈의 것이라 저장·삭제하지 않았습니다. 게임 시리즈를 다시 선택해 목록을 불러오세요.";
        return false;
    }

    [RelayCommand(CanExecute = nameof(CanAddGlobalGlossary))]
    private async Task AddGlobalGlossaryAsync()
    {
        var src = GlobalGlossarySourceTerm.Trim();
        var dst = GlobalGlossaryTargetTerm.Trim();
        var category = GlobalGlossaryCategory.Trim();
        if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
        {
            return;
        }

        if (GlobalGlossaryMatchMode == GlossaryMatchMode.Regex && !GlossaryApplier.IsValidRegexPattern(src))
        {
            StatusMessage = $"정규식이 올바르지 않아 추가하지 않았습니다: {src}";
            return;
        }

        if (!await TrySaveListEditsBeforeReloadAsync(EditableLists.GlobalGlossary, "용어를 추가하면"))
        {
            return;
        }

        try
        {
            var outcome = await _globalGlossaryService.UpsertAsync(
                request: new GlossaryUpsertRequest(
                    Category: string.IsNullOrWhiteSpace(category) ? null : category,
                    SourceTerm: src,
                    TargetTerm: dst,
                    Enabled: true,
                    Priority: GlobalGlossaryPriority,
                    MatchMode: GlobalGlossaryMatchMode,
                    ForceMode: GlobalGlossaryForceMode,
                    Note: null
                ),
                cancellationToken: CancellationToken.None
            );

            GlobalGlossarySourceTerm = "";
            GlobalGlossaryTargetTerm = "";
            GlobalGlossaryCategory = "";
            await ReloadGlobalGlossaryAsync();
            StatusMessage = DescribeGlossaryUpsert("전체 용어집", src, dst, outcome);
        }
        catch (Exception ex)
        {
            SetUserFacingError("전체 용어집 수정", ex);
        }
    }

    private bool CanAddGlobalGlossary() => IsProjectLoaded
        && !string.IsNullOrWhiteSpace(GlobalGlossarySourceTerm)
        && !string.IsNullOrWhiteSpace(GlobalGlossaryTargetTerm);

    [RelayCommand(CanExecute = nameof(CanImportGlobalGlossary))]
    private async Task ImportGlobalGlossaryAsync()
    {
        var globalDb = await _globalGlossaryService.TryGetDbAsync(CancellationToken.None);
        if (globalDb == null) return;

        await ImportGlossaryFromFileAsync(
            db: globalDb,
            statusLabel: "전체 용어집",
            dialogTitle: "Import global glossary file",
            priority: GlobalGlossaryPriority,
            matchMode: GlobalGlossaryMatchMode,
            forceMode: GlobalGlossaryForceMode,
            reloadedList: EditableLists.GlobalGlossary,
            reloadAsync: ReloadGlobalGlossaryAsync
        );
    }

    private bool CanImportGlobalGlossary() => IsProjectLoaded && !IsTranslating;

    [RelayCommand(CanExecute = nameof(CanSaveGlobalGlossaryChanges))]
    private async Task SaveGlobalGlossaryChangesAsync()
    {
        var dirty = GlobalGlossary.Where(g => g.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            StatusMessage = "저장할 전체 용어집 변경 사항이 없습니다.";
            return;
        }

        if (!AreGlobalGlossaryRowsFromSelectedFranchise())
        {
            return;
        }

        try
        {
            await SaveGlobalGlossaryRowsAsync(dirty);
            StatusMessage = IsTranslating
                ? $"Global glossary saved: {dirty.Count} updated. (Restart translation to apply.)"
                : $"Global glossary saved: {dirty.Count} updated.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("전체 용어집 저장", ex);
        }
    }

    private bool CanSaveGlobalGlossaryChanges() => IsProjectLoaded;

    private async Task SaveGlobalGlossaryRowsAsync(IReadOnlyList<GlossaryEntryViewModel> dirty)
    {
        await _globalGlossaryService.BulkUpdateAsync(dirty.Select(ToGlossaryUpdateRow).ToList(), CancellationToken.None);

        foreach (var g in dirty)
        {
            g.MarkClean();
        }

        RebuildGlobalGlossaryCategoryFilters();
        GlobalGlossaryView.Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteGlobalGlossaryEntry))]
    private async Task DeleteGlobalGlossaryEntryAsync()
    {
        if (SelectedGlobalGlossaryEntry == null || !AreGlobalGlossaryRowsFromSelectedFranchise()) return;

        var confirm = _uiInteractionService.ShowMessage(
            $"선택한 전역 용어집 항목을 삭제할까요?\n\n- {SelectedGlobalGlossaryEntry.SourceTerm} => {SelectedGlobalGlossaryEntry.TargetTerm}",
            "전역 용어집 삭제",
            UiMessageBoxButton.YesNo,
            UiMessageBoxImage.Warning
        );
        if (confirm != UiMessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _globalGlossaryService.DeleteAsync(SelectedGlobalGlossaryEntry.Id, CancellationToken.None);
            GlobalGlossary.Remove(SelectedGlobalGlossaryEntry);
            SelectedGlobalGlossaryEntry = null;

            RebuildGlobalGlossaryCategoryFilters();
            GlobalGlossaryView.Refresh();
            StatusMessage = "전체 용어집 항목을 삭제했습니다.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("전체 용어집 삭제", ex);
        }
    }

    private bool CanDeleteGlobalGlossaryEntry() => IsProjectLoaded && SelectedGlobalGlossaryEntry != null;

    [RelayCommand(CanExecute = nameof(CanExportGlobalGlossary))]
    private async Task ExportGlobalGlossaryAsync()
    {
        var path = ResolveGlossaryExportPath(title: "Export global glossary", defaultFileName: "global-glossary.tsv");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var rows = GlobalGlossary
                .Select(
                    g =>
                    (
                        Category: string.IsNullOrWhiteSpace(g.Category) ? null : g.Category.Trim(),
                        SourceTerm: g.SourceTerm ?? "",
                        TargetTerm: g.TargetTerm ?? "",
                        g.Enabled,
                        g.Priority,
                        g.MatchMode,
                        g.ForceMode,
                        g.Note
                    )
                );
            await File.WriteAllTextAsync(path, GlossaryFileService.BuildGlossaryTsv(rows), CancellationToken.None);
            StatusMessage = $"전체 용어집을 내보냈습니다: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            SetUserFacingError("전체 용어집 내보내기", ex);
        }
    }

    private bool CanExportGlobalGlossary() => IsProjectLoaded && !IsTranslating;

    private async Task ReloadGlobalGlossaryAsync()
    {
        var franchise = _globalProjectDbService.SelectedFranchise;
        var rows = await _globalGlossaryService.GetAsync(CancellationToken.None);
        // A later game-series switch started its own reload; the earlier game's rows must not replace it.
        if (franchise != _globalProjectDbService.SelectedFranchise)
        {
            return;
        }

        var list = rows.Select(MapGlossaryToViewModel).ToList();

        GlobalGlossary.ReplaceAll(list);
        _globalGlossaryRowsFranchise = franchise;
        RebuildGlobalGlossaryCategoryFilters();
        GlobalGlossaryView.Refresh();
        RebuildGlossaryLookupResults();
    }

    partial void OnGlobalGlossaryFilterTextChanged(string value) => GlobalGlossaryView.Refresh();
    partial void OnGlobalGlossaryFilterCategoryChanged(string value) => GlobalGlossaryView.Refresh();

    private bool GlobalGlossaryFilter(object obj)
    {
        if (obj is not GlossaryEntryViewModel entry)
        {
            return true;
        }

        return MatchGlossaryFilter(entry, (GlobalGlossaryFilterCategory ?? "").Trim(), (GlobalGlossaryFilterText ?? "").Trim());
    }

    private void RebuildGlobalGlossaryCategoryFilters()
    {
        var list = BuildCategoryFilterValues(GlobalGlossary);
        GlobalGlossaryCategoryFilterValues.ReplaceAll(list);

        if (!list.Contains(GlobalGlossaryFilterCategory, StringComparer.Ordinal))
        {
            GlobalGlossaryFilterCategory = GlossaryCategoryAll;
        }
    }
}
