using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.Services;

public sealed class GlobalGlossaryService
{
    private readonly GlobalProjectDbService _globalDbService;
    private readonly ProjectGlossaryService _projectGlossaryService;

    public GlobalGlossaryService(GlobalProjectDbService globalDbService, ProjectGlossaryService projectGlossaryService)
    {
        _globalDbService = globalDbService;
        _projectGlossaryService = projectGlossaryService;
    }

    public async Task<ProjectDb?> TryGetDbAsync(CancellationToken cancellationToken)
        => await _globalDbService.GetOrCreateAsync(cancellationToken);

    public async Task<IReadOnlyList<GlossaryEntry>> GetAsync(CancellationToken cancellationToken)
    {
        var db = await _globalDbService.GetOrCreateAsync(cancellationToken);
        if (db == null)
        {
            return Array.Empty<GlossaryEntry>();
        }

        return await _projectGlossaryService.GetAsync(db, cancellationToken);
    }

    public async Task UpsertAsync(GlossaryUpsertRequest request, CancellationToken cancellationToken)
    {
        var db = await RequireDbAsync(cancellationToken);
        await _projectGlossaryService.UpsertAsync(db, request, cancellationToken);
    }

    public async Task BulkUpdateAsync(
        IEnumerable<(long Id, string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note)> rows,
        CancellationToken cancellationToken
    )
    {
        var db = await RequireDbAsync(cancellationToken);
        await _projectGlossaryService.BulkUpdateAsync(db, rows, cancellationToken);
    }

    public async Task DeleteAsync(long id, CancellationToken cancellationToken)
    {
        var db = await RequireDbAsync(cancellationToken);
        await _projectGlossaryService.DeleteAsync(db, id, cancellationToken);
    }

    public async Task<GlossaryImportService.GlossaryImportResult?> ImportFromFileAsync(
        string glossaryPath,
        GlossaryImportService.GlossaryImportOptions options,
        CancellationToken cancellationToken
    )
    {
        var db = await RequireDbAsync(cancellationToken);
        return await _projectGlossaryService.ImportFromFileAsync(db, glossaryPath, options, cancellationToken);
    }

    private async Task<ProjectDb> RequireDbAsync(CancellationToken cancellationToken)
    {
        var db = await _globalDbService.GetOrCreateAsync(cancellationToken);
        if (db == null)
        {
            throw new InvalidOperationException("Global DB is not available.");
        }

        return db;
    }
}

