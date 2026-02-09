using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
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

        try
        {
            await _globalGlossaryService.UpsertAsync(
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
            StatusMessage = IsTranslating
                ? "Global glossary updated. (Restart translation to apply.)"
                : "Global glossary updated.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Global glossary update", ex);
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
            statusLabel: "Global glossary",
            dialogTitle: "Import global glossary file",
            priority: GlobalGlossaryPriority,
            matchMode: GlobalGlossaryMatchMode,
            forceMode: GlobalGlossaryForceMode,
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
            StatusMessage = "No global glossary changes to save.";
            return;
        }

        try
        {
            var rows = dirty.Select(
                    g =>
                    (
                        g.Id,
                        Category: string.IsNullOrWhiteSpace(g.Category) ? null : g.Category.Trim(),
                        SourceTerm: (g.SourceTerm ?? "").Trim(),
                        TargetTerm: (g.TargetTerm ?? "").Trim(),
                        g.Enabled,
                        g.Priority,
                        MatchMode: (int)g.MatchMode,
                        ForceMode: (int)g.ForceMode,
                        g.Note
                    )
                )
                .ToList();

            await _globalGlossaryService.BulkUpdateAsync(rows, CancellationToken.None);

            foreach (var g in dirty)
            {
                g.MarkClean();
            }

            RebuildGlobalGlossaryCategoryFilters();
            GlobalGlossaryView.Refresh();
            StatusMessage = IsTranslating
                ? $"Global glossary saved: {rows.Count} updated. (Restart translation to apply.)"
                : $"Global glossary saved: {rows.Count} updated.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Global glossary save", ex);
        }
    }

    private bool CanSaveGlobalGlossaryChanges() => IsProjectLoaded;

    [RelayCommand(CanExecute = nameof(CanDeleteGlobalGlossaryEntry))]
    private async Task DeleteGlobalGlossaryEntryAsync()
    {
        if (SelectedGlobalGlossaryEntry == null) return;

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
            StatusMessage = "Global glossary entry deleted.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Global glossary delete", ex);
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
            StatusMessage = $"Global glossary exported: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Global glossary export", ex);
        }
    }

    private bool CanExportGlobalGlossary() => IsProjectLoaded && !IsTranslating;

    private async Task ReloadGlobalGlossaryAsync()
    {
        var rows = await _globalGlossaryService.GetAsync(CancellationToken.None);
        var list = rows
            .Select(
                g =>
                {
                    var vm = new GlossaryEntryViewModel(g.Id);
                    vm.BeginUpdate();
                    vm.Category = g.Category ?? "";
                    vm.SourceTerm = g.SourceTerm;
                    vm.TargetTerm = g.TargetTerm;
                    vm.Enabled = g.Enabled;
                    vm.MatchMode = g.MatchMode;
                    vm.ForceMode = g.ForceMode;
                    vm.Priority = g.Priority;
                    vm.Note = g.Note;
                    vm.EndUpdate();
                    vm.MarkClean();
                    return vm;
                }
            )
            .ToList();

        GlobalGlossary.ReplaceAll(list);
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

        var categoryFilter = (GlobalGlossaryFilterCategory ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(categoryFilter) && categoryFilter != GlossaryCategoryAll)
        {
            if (categoryFilter == GlossaryCategoryNone)
            {
                if (!string.IsNullOrWhiteSpace(entry.Category))
                {
                    return false;
                }
            }
            else
            {
                if (!string.Equals(entry.Category?.Trim() ?? "", categoryFilter, StringComparison.Ordinal))
                {
                    return false;
                }
            }
        }

        var q = (GlobalGlossaryFilterText ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(q))
        {
            if ((entry.Category ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (entry.SourceTerm ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (entry.TargetTerm ?? "").Contains(q, StringComparison.OrdinalIgnoreCase)
                || (entry.Note ?? "").Contains(q, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        return true;
    }

    private void RebuildGlobalGlossaryCategoryFilters()
    {
        var categories = GlobalGlossary
            .Select(g => (g.Category ?? "").Trim())
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Distinct(StringComparer.Ordinal)
            .OrderBy(c => c, StringComparer.Ordinal)
            .ToList();

        var list = new List<string> { GlossaryCategoryAll, GlossaryCategoryNone };
        list.AddRange(categories);
        GlobalGlossaryCategoryFilterValues.ReplaceAll(list);

        if (!list.Contains(GlobalGlossaryFilterCategory, StringComparer.Ordinal))
        {
            GlobalGlossaryFilterCategory = GlossaryCategoryAll;
        }
    }
}
