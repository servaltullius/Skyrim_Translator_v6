using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Diagnostics;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    /// <summary>The rows a generation call carries, and which of them it retries.</summary>
    private sealed record GenerationScope(IReadOnlyList<long> RowIds, IReadOnlySet<long> RetriedRowIds);
    private readonly AsyncLocal<GenerationScope?> _generationScope = new();
    private static readonly IReadOnlySet<long> NoRows = new HashSet<long>();

    private IDisposable EnterGenerationScope(IEnumerable<long>? ids = null, bool recovery = false)
    {
        var previous = _generationScope.Value;
        var rowIds = ids?.ToArray() ?? previous?.RowIds ?? Array.Empty<long>();
        _generationScope.Value = new GenerationScope(rowIds, ResolveRetriedRows(rowIds, recovery));
        return new ScopeRestore(() => _generationScope.Value = previous);
    }

    /// <summary>
    /// A request is a retry row by row. Treating the whole batch as a retry when one row had been sent before
    /// (after an API-key switch) charged every new row a recovery call, and one row at its limit failed them all.
    /// </summary>
    private IReadOnlySet<long> ResolveRetriedRows(IReadOnlyList<long> rowIds, bool recovery)
    {
        if (recovery)
        {
            return rowIds.ToHashSet();
        }

        var previous = _generationScope.Value;
        if (previous != null)
        {
            return previous.RetriedRowIds.Count == 0 ? NoRows : rowIds.Where(previous.RetriedRowIds.Contains).ToHashSet();
        }

        return Ctx.GenerationBudget?.GetAttempted(rowIds) ?? NoRows;
    }

    /// <summary>
    /// The first pass over a long text's chunks is one attempt at a smaller granularity, not one retry
    /// per chunk: a long book needs more chunks than the per-row recovery limit even when it arrives
    /// through a recovery path. Retries of a failed chunk enter recovery again; the run-wide limit
    /// still bounds the total.
    /// </summary>
    private IDisposable EnterChunkFirstPassScope()
    {
        var previous = _generationScope.Value;
        _generationScope.Value = new GenerationScope(previous?.RowIds ?? Array.Empty<long>(), NoRows);
        return new ScopeRestore(() => _generationScope.Value = previous);
    }

    private sealed class ScopeRestore(Action restore) : IDisposable
    {
        public void Dispose() => restore();
    }

    private void ConsumeGenerationBudget()
    {
        var scope = _generationScope.Value;
        Ctx.GenerationBudget?.Consume(scope?.RowIds ?? Array.Empty<long>(), scope?.RetriedRowIds ?? NoRows);
    }

    /// <summary>
    /// Marks the rows of <paramref name="batch"/> that would be retried but have no recovery call left as Error
    /// and returns the others. Otherwise the request for the batch failed as a whole, every split level rethrew,
    /// and rows that were never sent ended as Error with the exhausted row.
    /// </summary>
    private async Task<IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>> SkipRowsOutOfRetriesAsync(
        PipelineContext ctx,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch
    )
    {
        var budget = Ctx.GenerationBudget;
        if (budget == null || batch.Count == 0)
        {
            return batch;
        }

        var retried = ResolveRetriedRows(batch.Select(row => row.Id).ToArray(), recovery: false);
        var exhausted = retried.Count == 0 ? NoRows : budget.GetRowsOutOfRecoveryCalls(retried);
        if (exhausted.Count == 0)
        {
            return batch;
        }

        foreach (var row in batch.Where(row => exhausted.Contains(row.Id)))
        {
            await HandleRowErrorAsync(row.Id, budget.CreateRowLimitException(row.Id), ctx.OnRowUpdated, awaitNotifications: false, ctx.CancellationToken);
        }

        return batch.Where(row => !exhausted.Contains(row.Id)).ToArray();
    }

    private static bool IsRunGenerationLimit(Exception ex)
        => ExceptionTraversal.Enumerate(ex).Any(e => e is TranslationGenerationLimitException { IsRunLimit: true });

    private static bool IsRowGenerationLimit(Exception ex)
        => ExceptionTraversal.Enumerate(ex).Any(e => e is TranslationGenerationLimitException { IsRunLimit: false });

    // Transport/auth/quota/model errors are not fixed by translating smaller pieces.
    // Their bounded transport retries have already run before reaching recovery.
    private static bool MustStopRecovery(Exception ex)
        => ExceptionTraversal.Enumerate(ex).Any(e => e is TranslationGenerationLimitException
            or GeminiHttpException or HttpRequestException);
}
