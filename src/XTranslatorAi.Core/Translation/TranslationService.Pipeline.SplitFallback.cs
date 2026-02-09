using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private async Task TranslateBatchWithSplitFallbackAsync(
        PipelineContext ctx,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch
    )
    {
        ctx.CancellationToken.ThrowIfCancellationRequested();

        batch = PrepareBatchWithSessionTermForceTokens(batch);

        if (batch.Count == 0)
        {
            return;
        }

        if (batch.Count == 1)
        {
            await TranslateSingleRowAsync(ctx, batch[0]);
            return;
        }

        try
        {
            await TranslateBatchAndPersistAsync(ctx, batch);
            return;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            if (ctx.EnableApiKeyFailover && IsApiKeyFailoverError(ex, ctx.CancellationToken))
            {
                throw;
            }

            // Fall back to smaller batches.
        }

        var (left, right) = SplitBatchByWeight(batch);
        await TranslateBatchWithSplitFallbackAsync(ctx, left);
        await TranslateBatchWithSplitFallbackAsync(ctx, right);
    }

    private static (IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> Left,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> Right) SplitBatchByWeight(
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch
    )
    {
        var splitAt = FindSplitIndexByWeight(batch);
        var left = new List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>(capacity: splitAt);
        var right = new List<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>(
            capacity: batch.Count - splitAt
        );

        for (var i = 0; i < batch.Count; i++)
        {
            if (i < splitAt)
            {
                left.Add(batch[i]);
            }
            else
            {
                right.Add(batch[i]);
            }
        }

        return (left, right);
    }

	    private async Task TranslateBatchAndPersistAsync(
	        PipelineContext ctx,
	        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch
	    )
	    {
	        var batchCtx = CreateBatchTranslateContext(ctx);
	        var translations = await TranslateBatchWithRetriesAsync(batchCtx, batch);
	        var batchById = BuildBatchById(batch);

	        var doneUpdates = await BuildDoneUpdatesAsync(ctx, translations, batchById, batch.Count);
	        await PersistDoneUpdatesAsync(ctx, doneUpdates);
	    }

	    private static BatchTranslateContext CreateBatchTranslateContext(PipelineContext ctx)
	    {
	        return new BatchTranslateContext(
	            ApiKey: ctx.ApiKey,
	            ModelName: ctx.ModelName,
	            SystemPrompt: ctx.SystemPrompt,
	            PromptCache: ctx.PromptCache,
	            SourceLang: ctx.SourceLang,
	            TargetLang: ctx.TargetLang,
	            Temperature: ctx.Temperature,
	            MaxOutputTokens: ctx.MaxOutputTokens,
	            ResponseSchema: ctx.ResponseSchema,
	            MaxRetries: ctx.MaxRetries,
	            EnableRepairPass: ctx.EnableRepairPass,
	            CancellationToken: ctx.CancellationToken
	        );
	    }

	    private async Task<List<(long Id, string DestText, StringEntryStatus Status, string? ErrorMessage)>> BuildDoneUpdatesAsync(
	        PipelineContext ctx,
	        IReadOnlyDictionary<long, string> translations,
	        IReadOnlyDictionary<long, (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batchById,
	        int capacity
	    )
	    {
	        var doneUpdates = new List<(long Id, string DestText, StringEntryStatus Status, string? ErrorMessage)>(capacity: capacity);
	        foreach (var (id, rawText) in translations)
	        {
	            if (!batchById.TryGetValue(id, out var batchItem))
	            {
	                throw new InvalidOperationException($"Model returned unknown id: {id}");
	            }

	            await TryAddDoneUpdateAsync(ctx, batchItem, id, rawText, doneUpdates);
	        }
	        return doneUpdates;
	    }

	    private async Task TryAddDoneUpdateAsync(
	        PipelineContext ctx,
	        (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary) batchItem,
	        long id,
	        string rawText,
	        List<(long Id, string DestText, StringEntryStatus Status, string? ErrorMessage)> doneUpdates
	    )
	    {
	        try
	        {
	            var final = ApplyTokensAndUnmask(rawText, batchItem.Glossary, batchItem.Mask, ctx.PlaceholderMasker, ctx.TargetLang);
	            if (_enableTemplateFixer)
	            {
	                final = MagDurPlaceholderFixer.Fix(batchItem.Source, final, ctx.TargetLang);
	            }
	            final = PlaceholderUnitBinder.EnforceUnitsFromSource(ctx.TargetLang, batchItem.Source, final);
	            final = KoreanProtectFromFixer.Fix(ctx.TargetLang, batchItem.Source, final);
	            final = KoreanTranslationFixer.Fix(ctx.TargetLang, final);
	            ValidateFinalTextIntegrity(batchItem.Source, final, context: $"id={id} post-edits");

	            string? styleHint = null;
	            if (_enableQualityEscalation)
	            {
	                var rec = GetRecForId(id);
	                styleHint = GuessStyleHint(batchItem.Source, _useRecStyleHints ? rec : null);
	                styleHint = AppendDialogueContextToStyleHint(styleHint, GetDialogueContextWindowForId(id));
	            }

	            (rawText, final) = await MaybeApplyQualityEscalationAsync(ctx, batchItem, styleHint, rawText, final);
	            TryLearnSessionTermMemory(id, batchItem.Source, final);
	            doneUpdates.Add((id, final, StringEntryStatus.Done, null));

	            var duplicateDoneUpdates = await BuildDuplicateDoneUpdatesAsync(
	                canonicalId: id,
	                rawText: rawText,
	                glossary: batchItem.Glossary,
	                placeholderMasker: ctx.PlaceholderMasker,
	                targetLang: ctx.TargetLang,
	                onRowUpdated: ctx.OnRowUpdated,
	                awaitNotifications: false,
	                cancellationToken: ctx.CancellationToken
	            );
	            if (duplicateDoneUpdates.Count > 0)
	            {
	                doneUpdates.AddRange(duplicateDoneUpdates);
	    }
}
	        catch (OperationCanceledException)
	        {
	            throw;
	        }
	        catch (Exception ex)
	        {
	            await HandleRowErrorAsync(id, ex, ctx.OnRowUpdated, awaitNotifications: false, ctx.CancellationToken);
	        }
	    }

	    private async Task PersistDoneUpdatesAsync(
	        PipelineContext ctx,
	        IReadOnlyList<(long Id, string DestText, StringEntryStatus Status, string? ErrorMessage)> doneUpdates
	    )
	    {
	        if (doneUpdates.Count == 0)
	        {
	            return;
	        }

	        await _db.UpdateStringTranslationsAsync(doneUpdates, ctx.CancellationToken);
	        if (ctx.OnRowUpdated == null)
	        {
	            return;
	        }

	        foreach (var it in doneUpdates)
	        {
	            NotifyRowUpdated(ctx.OnRowUpdated, it.Id, StringEntryStatus.Done, it.DestText);
	        }
	    }
	}
