using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.Services;

public sealed class ProjectGlossaryService
{
    private readonly GlossaryImportService _importService;

    public ProjectGlossaryService(GlossaryImportService importService)
    {
        _importService = importService;
    }

    public Task<IReadOnlyList<GlossaryEntry>> GetAsync(ProjectDb db, CancellationToken cancellationToken)
        => db.GetGlossaryAsync(cancellationToken);

    public Task UpsertAsync(ProjectDb db, GlossaryUpsertRequest request, CancellationToken cancellationToken)
        => db.UpsertGlossaryAsync(request, cancellationToken);

    public Task BulkUpdateAsync(
        ProjectDb db,
        IEnumerable<(long Id, string? Category, string SourceTerm, string TargetTerm, bool Enabled, int Priority, int MatchMode, int ForceMode, string? Note)> rows,
        CancellationToken cancellationToken
    ) => db.BulkUpdateGlossaryAsync(rows, cancellationToken);

    public Task DeleteAsync(ProjectDb db, long id, CancellationToken cancellationToken)
        => db.DeleteGlossaryEntryAsync(id, cancellationToken);

    public Task<GlossaryImportService.GlossaryImportResult?> ImportFromFileAsync(
        ProjectDb db,
        string glossaryPath,
        GlossaryImportService.GlossaryImportOptions options,
        CancellationToken cancellationToken
    ) => _importService.ImportFromFileAsync(db, glossaryPath, options, cancellationToken);
}

