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
    [RelayCommand(CanExecute = nameof(CanAddGlossary))]
    private async Task AddGlossaryAsync()
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        var src = GlossarySourceTerm.Trim();
        var dst = GlossaryTargetTerm.Trim();
        var category = GlossaryCategory.Trim();
        if (string.IsNullOrWhiteSpace(src) || string.IsNullOrWhiteSpace(dst))
        {
            return;
        }

        if (GlossaryMatchMode == GlossaryMatchMode.Regex && !GlossaryApplier.IsValidRegexPattern(src))
        {
            StatusMessage = $"정규식이 올바르지 않아 추가하지 않았습니다: {src}";
            return;
        }

        if (!await TrySaveListEditsBeforeReloadAsync(EditableLists.ProjectGlossary, "용어를 추가하면"))
        {
            return;
        }

        try
        {
            var outcome = await _projectGlossaryService.UpsertAsync(
                db,
                request: new GlossaryUpsertRequest(
                    Category: string.IsNullOrWhiteSpace(category) ? null : category,
                    SourceTerm: src,
                    TargetTerm: dst,
                    Enabled: true,
                    Priority: GlossaryPriority,
                    MatchMode: GlossaryMatchMode,
                    ForceMode: GlossaryForceMode,
                    Note: null
                ),
                cancellationToken: CancellationToken.None
            );

            GlossarySourceTerm = "";
            GlossaryTargetTerm = "";
            GlossaryCategory = "";
            await ReloadGlossaryAsync();
            StatusMessage = DescribeGlossaryUpsert("용어집", src, dst, outcome);
        }
        catch (Exception ex)
        {
            SetUserFacingError("용어집 수정", ex);
        }
    }

    /// <summary>Says whether the term was added or replaced an entry with the same source, so no change looks like a no-op.</summary>
    private string DescribeGlossaryUpsert(string glossaryLabel, string source, string target, GlossaryUpsertOutcome outcome)
    {
        var message = !outcome.Updated
            ? $"{glossaryLabel}에 추가했습니다: {source} → {target}"
            : string.Equals((outcome.PreviousTarget ?? "").Trim(), target, StringComparison.OrdinalIgnoreCase)
                ? $"{glossaryLabel}에 이미 있는 용어의 설정을 바꿨습니다: {source} → {target}"
                : $"{glossaryLabel}에 같은 원문이 있어 번역어를 바꿨습니다: {source} → {target} (이전: {outcome.PreviousTarget})";
        return IsTranslating ? message + " 번역을 다시 시작하면 적용됩니다." : message;
    }

    private bool CanAddGlossary() => IsProjectLoaded
        && !string.IsNullOrWhiteSpace(GlossarySourceTerm)
        && !string.IsNullOrWhiteSpace(GlossaryTargetTerm);

    [RelayCommand(CanExecute = nameof(CanImportGlossary))]
    private async Task ImportGlossaryAsync()
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        await ImportGlossaryFromFileAsync(
            db: db,
            statusLabel: "용어집",
            dialogTitle: "Import glossary file",
            priority: GlossaryPriority,
            matchMode: GlossaryMatchMode,
            forceMode: GlossaryForceMode,
            reloadedList: EditableLists.ProjectGlossary,
            reloadAsync: ReloadGlossaryAsync
        );
    }

    private bool CanImportGlossary() => IsProjectLoaded && !IsTranslating;

    [RelayCommand(CanExecute = nameof(CanSaveGlossaryChanges))]
    private async Task SaveGlossaryChangesAsync()
    {
        var db = _projectState.Db;
        if (db == null)
        {
            return;
        }

        var dirty = Glossary.Where(g => g.IsDirty).ToList();
        if (dirty.Count == 0)
        {
            StatusMessage = "저장할 용어집 변경 사항이 없습니다.";
            return;
        }

        try
        {
            await SaveGlossaryRowsAsync(db, dirty);
            StatusMessage = IsTranslating
                ? $"Glossary saved: {dirty.Count} updated. (Restart translation to apply.)"
                : $"Glossary saved: {dirty.Count} updated.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("용어집 저장", ex);
        }
    }

    private bool CanSaveGlossaryChanges() => IsProjectLoaded;

    private async Task SaveGlossaryRowsAsync(ProjectDb db, IReadOnlyList<GlossaryEntryViewModel> dirty)
    {
        await _projectGlossaryService.BulkUpdateAsync(db, dirty.Select(ToGlossaryUpdateRow).ToList(), CancellationToken.None);

        foreach (var g in dirty)
        {
            g.MarkClean();
        }

        RebuildGlossaryCategoryFilters();
        GlossaryView.Refresh();
    }

    [RelayCommand(CanExecute = nameof(CanDeleteGlossaryEntry))]
    private async Task DeleteGlossaryEntryAsync()
    {
        var db = _projectState.Db;
        if (db == null || SelectedGlossaryEntry == null)
        {
            return;
        }

        var confirm = _uiInteractionService.ShowMessage(
            $"선택한 용어집 항목을 삭제할까요?\n\n- {SelectedGlossaryEntry.SourceTerm} => {SelectedGlossaryEntry.TargetTerm}",
            "용어집 삭제",
            UiMessageBoxButton.YesNo,
            UiMessageBoxImage.Warning
        );
        if (confirm != UiMessageBoxResult.Yes)
        {
            return;
        }

        try
        {
            await _projectGlossaryService.DeleteAsync(db, SelectedGlossaryEntry.Id, CancellationToken.None);
            Glossary.Remove(SelectedGlossaryEntry);
            SelectedGlossaryEntry = null;

            RebuildGlossaryCategoryFilters();
            GlossaryView.Refresh();
            StatusMessage = "용어집 항목을 삭제했습니다.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("용어집 삭제", ex);
        }
    }

    private bool CanDeleteGlossaryEntry() => IsProjectLoaded && SelectedGlossaryEntry != null;

    [RelayCommand(CanExecute = nameof(CanExportGlossary))]
    private async Task ExportGlossaryAsync()
    {
        var path = ResolveGlossaryExportPath(title: "Export glossary", defaultFileName: "glossary.tsv");
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            var rows = Glossary
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
            StatusMessage = $"용어집을 내보냈습니다: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            SetUserFacingError("용어집 내보내기", ex);
        }
    }

    private bool CanExportGlossary() => IsProjectLoaded && !IsTranslating;

    private string? ResolveGlossaryExportPath(string title, string defaultFileName)
    {
        var initialDirectory = (string?)null;
        if (!string.IsNullOrWhiteSpace(_projectState.InputPath))
        {
            var dir = Path.GetDirectoryName(_projectState.InputPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                initialDirectory = dir;
            }
        }

        return _uiInteractionService.ShowSaveFileDialog(
            new SaveFileDialogRequest(
                Filter: "TSV files (*.tsv)|*.tsv|All files (*.*)|*.*",
                Title: title,
                FileName: defaultFileName,
                InitialDirectory: initialDirectory
            )
        );
    }
}
