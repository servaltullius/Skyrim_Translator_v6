using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Text;
using XTranslatorAi.App.Services;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private async Task ImportGlossaryFromFileAsync(
        ProjectDb db,
        string statusLabel,
        string dialogTitle,
        int priority,
        GlossaryMatchMode matchMode,
        GlossaryForceMode forceMode,
        Func<Task> reloadAsync
    )
    {
        var glossaryPath = ResolveGlossaryImportPath(dialogTitle);
        if (string.IsNullOrWhiteSpace(glossaryPath))
        {
            return;
        }

        var statusLabelTrimmed = (statusLabel ?? "").Trim();
        var importingLabel = statusLabelTrimmed.ToLowerInvariant();
        var fileName = Path.GetFileName(glossaryPath);

        try
        {
            StatusMessage = $"Importing {importingLabel}: {fileName}...";

            var result = await _projectGlossaryService.ImportFromFileAsync(
                db,
                glossaryPath,
                new GlossaryImportService.GlossaryImportOptions(
                    Priority: priority,
                    MatchMode: matchMode,
                    ForceMode: forceMode,
                    Note: $"Imported from {fileName}"
                ),
                CancellationToken.None
            );
            if (result == null)
            {
                StatusMessage = "No glossary pairs found in file.";
                return;
            }

            await reloadAsync();
            StatusMessage =
                $"{statusLabelTrimmed} imported: +{result.Value.InsertedCount}, skipped {result.Value.SkippedExisting}, conflicts {result.Value.ConflictCount}.";
        }
        catch (Exception ex)
        {
            SetUserFacingError($"{statusLabelTrimmed} import", ex);
        }
    }

    private string? ResolveGlossaryImportPath(string dialogTitle)
    {
        var defaultPath = "";
        if (!string.IsNullOrWhiteSpace(_projectState.InputXmlPath))
        {
            var dir = Path.GetDirectoryName(_projectState.InputXmlPath);
            if (!string.IsNullOrWhiteSpace(dir))
            {
                defaultPath = Path.Combine(dir, "번역용어집 신규.md");
            }
        }

        if (!string.IsNullOrWhiteSpace(defaultPath) && File.Exists(defaultPath))
        {
            return defaultPath;
        }

        var initialDirectory = (string?)null;
        if (!string.IsNullOrWhiteSpace(_projectState.InputXmlPath))
        {
            var dir = Path.GetDirectoryName(_projectState.InputXmlPath);
            if (!string.IsNullOrWhiteSpace(dir) && Directory.Exists(dir))
            {
                initialDirectory = dir;
            }
        }

        return _uiInteractionService.ShowOpenFileDialog(
            new OpenFileDialogRequest(
                Filter: "Glossary files (*.md;*.txt;*.json;*.tsv)|*.md;*.txt;*.json;*.tsv|All files (*.*)|*.*",
                Title: dialogTitle,
                FileName: "번역용어집 신규.md",
                InitialDirectory: initialDirectory
            )
        );
    }
}
