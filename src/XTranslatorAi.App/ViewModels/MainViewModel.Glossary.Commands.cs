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

        try
        {
            await _projectGlossaryService.UpsertAsync(
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
            StatusMessage = IsTranslating
                ? "Glossary updated. (Restart translation to apply.)"
                : "Glossary updated.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Glossary update", ex);
        }
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
            statusLabel: "Glossary",
            dialogTitle: "Import glossary file",
            priority: GlossaryPriority,
            matchMode: GlossaryMatchMode,
            forceMode: GlossaryForceMode,
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
            StatusMessage = "No glossary changes to save.";
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

            await _projectGlossaryService.BulkUpdateAsync(db, rows, CancellationToken.None);

            foreach (var g in dirty)
            {
                g.MarkClean();
            }

            RebuildGlossaryCategoryFilters();
            GlossaryView.Refresh();
            StatusMessage = IsTranslating
                ? $"Glossary saved: {rows.Count} updated. (Restart translation to apply.)"
                : $"Glossary saved: {rows.Count} updated.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Glossary save", ex);
        }
    }

    private bool CanSaveGlossaryChanges() => IsProjectLoaded;

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
            StatusMessage = "Glossary entry deleted.";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Glossary delete", ex);
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
            StatusMessage = $"Glossary exported: {Path.GetFileName(path)}";
        }
        catch (Exception ex)
        {
            SetUserFacingError("Glossary export", ex);
        }
    }

    private bool CanExportGlossary() => IsProjectLoaded && !IsTranslating;

    private string? ResolveGlossaryExportPath(string title, string defaultFileName)
    {
        var initialDirectory = (string?)null;
        if (!string.IsNullOrWhiteSpace(_projectState.InputXmlPath))
        {
            var dir = Path.GetDirectoryName(_projectState.InputXmlPath);
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
