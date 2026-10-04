using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Text.ProjectContext;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private readonly ProjectContextScanner _projectContextScanner = new();

    private async Task<ProjectContextScanReport> BuildProjectContextScanReportAsync(CancellationToken cancellationToken)
    {
        var db = _projectState.Db;
        if (db == null || !_projectState.HasSource)
        {
            throw new InvalidOperationException("Project is not loaded.");
        }

        var options = new ProjectContextScanOptions(
            AddonName: _projectState.AddonName?.Trim(),
            InputFile: _projectState.InputPath != null ? Path.GetFileName(_projectState.InputPath) : null,
            SourceLang: SourceLang?.Trim() ?? "",
            TargetLang: TargetLang?.Trim() ?? ""
        );

        var globalDb = await _globalProjectDbService.GetOrCreateAsync(cancellationToken);
        return await _projectContextScanner.ScanAsync(db, globalDb, options, cancellationToken);
    }
}
