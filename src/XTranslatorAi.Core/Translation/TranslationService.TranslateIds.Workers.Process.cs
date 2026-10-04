using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private async Task WorkerAsync(WorkerRunContext ctx, BatchPreference preference)
    {
        var ct = ctx.Request.CancellationToken;
        while (true)
        {
            ct.ThrowIfCancellationRequested();

            // Another worker stopped the run on a rate-limit streak; the queued rows stay Pending.
            if (Ctx.RateLimitAborted)
            {
                return;
            }

            if (!TryDequeueBatch(ctx.Queues, preference, out var batch, out var source))
            {
                return;
            }

            ReleaseReservedGateSlotsIfNeeded(ctx, source);
            await ProcessBatchAsync(ctx, batch!);
        }
    }

    private async Task ProcessBatchAsync(
        WorkerRunContext ctx,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch
    )
    {
        var ct = ctx.Request.CancellationToken;
        ct.ThrowIfCancellationRequested();

        if (ctx.Request.WaitIfPaused != null)
        {
            await ctx.Request.WaitIfPaused(ct);
        }

        if (Ctx.RateLimitAborted)
        {
            return;
        }

        await MarkInProgressAsync(batch, ct, ctx.Request.OnRowUpdated);

        try
        {
            var pipeline = CreatePipelineContext(ctx, ct);
            await TranslateBatchAsync(pipeline, batch);

            await FlushSessionTermAutoGlossaryInsertsAsync();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // Both stop the run; TranslateIdsCoreAsync puts this batch's unfinished rows back to Pending.
            if (IsRunGenerationLimit(ex) || IsRateLimitAbort(ex)) throw;
            if (IsCredentialError(ex))
            {
                await RevertBatchToPendingAsync(ctx.Request, batch, ct);
                throw;
            }

            if (ctx.Request.EnableApiKeyFailover && IsApiKeyFailoverError(ex, ct))
            {
                throw;
            }

            ThrowIfRateLimitStreakReached(ex, ctx.Request.EnableApiKeyFailover);
            await HandleBatchFailureAsync(ctx.Request, batch, ex, ct);
        }
    }

    private static PipelineContext CreatePipelineContext(WorkerRunContext ctx, CancellationToken ct)
    {
        return new PipelineContext(
            ApiKey: ctx.Request.ApiKey,
            ModelName: ctx.Request.ModelName,
            SystemPrompt: ctx.Request.SystemPrompt,
            EnableApiKeyFailover: ctx.Request.EnableApiKeyFailover,
            PromptCache: ctx.PromptCache,
            SourceLang: ctx.Request.SourceLang,
            TargetLang: ctx.Request.TargetLang,
            MaxChars: ctx.Request.MaxChars,
            Temperature: ctx.Request.Temperature,
            MaxOutputTokens: ctx.Request.MaxOutputTokens,
            ResponseSchema: ctx.ResponseSchema,
            MaxRetries: ctx.Request.MaxRetries,
            EnableRepairPass: ctx.Request.EnableRepairPass,
            PlaceholderMasker: ctx.PlaceholderMasker,
            OnRowUpdated: ctx.Request.OnRowUpdated,
            CancellationToken: ct
        );
    }

    private async Task TranslateBatchAsync(
        PipelineContext pipeline,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch
    )
    {
        if (batch.Count == 1)
        {
            await TranslateSingleRowAsync(pipeline, batch[0]);
            return;
        }

        await TranslateBatchWithSplitFallbackAsync(pipeline, batch);
    }

    private async Task RevertBatchToPendingAsync(
        TranslateIdsRequest request,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch,
        CancellationToken ct
    )
    {
        if (batch.Count == 0)
        {
            return;
        }

        var ids = new List<long>(capacity: batch.Count);
        for (var i = 0; i < batch.Count; i++)
        {
            ids.Add(batch[i].Id);
            var dups = GetDuplicateRows(batch[i].Id);
            for (var j = 0; j < dups.Count; j++)
            {
                ids.Add(dups[j].Id);
            }
        }

        var statuses = await _db.GetStringStatusesByIdsAsync(ids, ct);
        ids.RemoveAll(id => !statuses.TryGetValue(id, out var status) || status != StringEntryStatus.InProgress);
        if (ids.Count == 0)
        {
            return;
        }

        await _db.UpdateStringStatusesAsync(ids, StringEntryStatus.Pending, errorMessage: null, ct);

        if (request.OnRowUpdated == null)
        {
            return;
        }

        for (var i = 0; i < ids.Count; i++)
        {
            NotifyRowUpdated(request.OnRowUpdated, ids[i], StringEntryStatus.Pending, "");
        }
    }

    private async Task HandleBatchFailureAsync(
        TranslateIdsRequest request,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch,
        Exception ex,
        CancellationToken ct
    )
    {
        var msg = FormatError(ex);
        var ids = new List<long>(capacity: batch.Count);
        for (var i = 0; i < batch.Count; i++)
        {
            ids.Add(batch[i].Id);
            var dups = GetDuplicateRows(batch[i].Id);
            for (var j = 0; j < dups.Count; j++)
            {
                ids.Add(dups[j].Id);
            }
        }

        // A split batch can have committed some rows before a later request fails.
        // Keep those results and only mark the unfinished part of this batch.
        var statuses = await _db.GetStringStatusesByIdsAsync(ids, CancellationToken.None);
        ids.RemoveAll(id => !statuses.TryGetValue(id, out var status) || status != StringEntryStatus.InProgress);
        if (ids.Count == 0)
        {
            return;
        }

        // Use CancellationToken.None to ensure error status is persisted even during cancellation.
        await _db.UpdateStringStatusesAsync(ids, StringEntryStatus.Error, msg, CancellationToken.None);

        if (request.OnRowUpdated == null)
        {
            return;
        }

        for (var i = 0; i < ids.Count; i++)
        {
            NotifyRowUpdated(request.OnRowUpdated, ids[i], StringEntryStatus.Error, msg);
        }
    }
}
