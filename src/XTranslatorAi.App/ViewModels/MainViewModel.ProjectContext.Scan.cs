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
        var xmlInfo = _projectState.XmlInfo;
        if (db == null || xmlInfo == null)
        {
            throw new InvalidOperationException("Project is not loaded.");
        }

        var options = new ProjectContextScanOptions(
            AddonName: xmlInfo.AddonName?.Trim(),
            InputFile: _projectState.InputXmlPath != null ? Path.GetFileName(_projectState.InputXmlPath) : null,
            SourceLang: SourceLang?.Trim() ?? "",
            TargetLang: TargetLang?.Trim() ?? "",
            NexusContext: null
        );

        var globalDb = await _globalProjectDbService.GetOrCreateAsync(cancellationToken);
        return await _projectContextScanner.ScanAsync(db, globalDb, options, cancellationToken);
    }
}
