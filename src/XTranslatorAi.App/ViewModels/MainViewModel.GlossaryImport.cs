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
        var importingLabel = statusLabelTrimmed;
        var fileName = Path.GetFileName(glossaryPath);

        try
        {
            StatusMessage = $"{importingLabel} 가져오는 중: {fileName}...";

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
                StatusMessage = "파일에서 용어 쌍을 찾지 못했습니다.";
                return;
            }

            await reloadAsync();
            StatusMessage =
                $"{statusLabelTrimmed} 가져오기 완료: 추가 {result.Value.InsertedCount}, 기존 항목 건너뜀 {result.Value.SkippedExisting}, 충돌 {result.Value.ConflictCount}.";
        }
        catch (Exception ex)
        {
            SetUserFacingError($"{statusLabelTrimmed} 가져오기", ex);
        }
    }

    private string? ResolveGlossaryImportPath(string dialogTitle)
    {
        var defaultPath = "";
        if (!string.IsNullOrWhiteSpace(_projectState.InputPath))
        {
            var dir = Path.GetDirectoryName(_projectState.InputPath);
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
        if (!string.IsNullOrWhiteSpace(_projectState.InputPath))
        {
            var dir = Path.GetDirectoryName(_projectState.InputPath);
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
