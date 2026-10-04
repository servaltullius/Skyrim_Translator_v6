using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
        EditableLists reloadedList,
        Func<Task> reloadAsync
    )
    {
        var glossaryPath = ResolveGlossaryImportPath(dialogTitle);
        if (string.IsNullOrWhiteSpace(glossaryPath))
        {
            return;
        }

        if (!await TrySaveListEditsBeforeReloadAsync(reloadedList, "파일에서 가져오면"))
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
            var imported = result.Value;
            StatusMessage =
                $"{statusLabelTrimmed} 가져오기 완료: 추가 {imported.InsertedCount}, 같은 항목 건너뜀 {imported.SkippedExisting}, 파일 안 충돌 {imported.ConflictCount}."
                + DescribeExistingGlossaryConflicts(imported.ExistingConflicts);
        }
        catch (Exception ex)
        {
            SetUserFacingError($"{statusLabelTrimmed} 가져오기", ex);
        }
    }

    /// <summary>Names a few sources that were not imported because the glossary already gives them another target.</summary>
    private static string DescribeExistingGlossaryConflicts(IReadOnlyList<string>? conflicts)
    {
        var sources = (conflicts ?? Array.Empty<string>()).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (sources.Count == 0)
        {
            return "";
        }

        var sample = string.Join(", ", sources.Take(5)) + (sources.Count > 5 ? " 등" : "");
        return $" 이미 다른 번역어로 있는 원문 {sources.Count}개는 가져오지 않았습니다({sample}). 바꾸려면 표에서 직접 고치세요.";
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
