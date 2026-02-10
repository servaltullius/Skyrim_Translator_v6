using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private sealed record BatchTranslateContext(
        string ApiKey,
        string ModelName,
        string SystemPrompt,
        PromptCache? PromptCache,
        string SourceLang,
        string TargetLang,
        double Temperature,
        int MaxOutputTokens,
        System.Text.Json.JsonElement ResponseSchema,
        int MaxRetries,
        bool EnableRepairPass,
        CancellationToken CancellationToken
    );

    private async Task MarkInProgressAsync(
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch,
        CancellationToken cancellationToken,
        Func<long, StringEntryStatus, string, Task>? onRowUpdated
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

        await _db.UpdateStringStatusesAsync(ids, StringEntryStatus.InProgress, errorMessage: null, cancellationToken);

        if (onRowUpdated != null)
        {
            for (var i = 0; i < ids.Count; i++)
            {
                NotifyRowUpdated(onRowUpdated, ids[i], StringEntryStatus.InProgress, "");
            }
        }
    }

    private async Task<IReadOnlyDictionary<long, string>> TranslateBatchWithRetriesAsync(
        BatchTranslateContext ctx,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch
    )
    {
        var userPrompt = BuildBatchUserPrompt(ctx.SourceLang, ctx.TargetLang, batch);
        var currentCtx = ctx;

        Exception? last = null;
        for (var attempt = 0; attempt <= ctx.MaxRetries; attempt++)
        {
            ctx.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await TranslateBatchOnceAsync(currentCtx, batch, userPrompt);
            }
            catch (Exception ex)
            {
                if (currentCtx.PromptCache != null && IsCachedContentInvalid(ex))
                {
                    if (IsCachedContentPermissionDenied(ex))
                    {
                        currentCtx.PromptCache.Disable();
                    }
                    else
                    {
                        currentCtx.PromptCache.Invalidate();
                    }

                    // CachedContent can expire mid-run; retry immediately without it.
                    var noCacheCtx = currentCtx with { PromptCache = null };
                    try
                    {
                        return await TranslateBatchOnceAsync(noCacheCtx, batch, userPrompt);
                    }
                    catch (Exception ex2)
                    {
                        currentCtx = noCacheCtx;
                        ex = ex2;
                    }
                }

                last = ex;
                // Batch timeouts are commonly caused by large payloads; splitting is more effective than retrying.
                if (ex is TaskCanceledException && !currentCtx.CancellationToken.IsCancellationRequested)
                {
                    break;
                }
                if (!ShouldRetry(ex) || attempt >= currentCtx.MaxRetries)
                {
                    break;
                }

                var delay = ComputeRetryDelay(ex, attempt);
                if (IsRateLimit(ex))
                {
                    RegisterAdaptiveRateLimit();
                    ExtendGlobalThrottle(delay);
                }
                await Task.Delay(delay, currentCtx.CancellationToken);
            }
        }

        throw new InvalidOperationException($"Translate batch failed: {last?.Message}", last);
    }

    private string BuildBatchUserPrompt(
        string sourceLang,
        string targetLang,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch
    )
    {
        var requestItems = new List<TranslationItem>(batch.Count);
        var requestTexts = new List<string>(batch.Count);

        var promptOnlyPairs = new List<(string Source, string Target)>();
        var promptOnlySet = new HashSet<(string Source, string Target)>(new SourceTargetComparer());
        foreach (var it in batch)
        {
            var rec = _useRecStyleHints ? GetRecForId(it.Id) : null;
            var dialogueContextWindow = GetDialogueContextWindowForId(it.Id);
            var maskedForPrompt = PlaceholderSemanticHintInjector.Inject(targetLang, it.Masked);
            maskedForPrompt = GlossarySemanticHintInjector.Inject(targetLang, maskedForPrompt, it.Glossary.TokenToReplacement);
            requestItems.Add(new TranslationItem(it.Id, maskedForPrompt, rec, dialogueContextWindow));
            requestTexts.Add(it.Masked);

            foreach (var p in it.Glossary.PromptOnlyPairs)
            {
                if (promptOnlySet.Add(p))
                {
                    promptOnlyPairs.Add(p);
                }
            }
        }

        var mergedPromptOnlyPairs = MergeSessionPromptOnlyGlossaryForTexts(requestTexts, promptOnlyPairs);
        return TranslationPrompt.BuildUserPrompt(sourceLang, targetLang, requestItems, mergedPromptOnlyPairs);
    }

    private GeminiGenerateContentRequest CreateBatchRequest(
        BatchTranslateContext ctx,
        string userPrompt,
        string? cachedContent,
        int candidateCount
    )
    {
        return new GeminiGenerateContentRequest(
            Contents: new List<GeminiContent>
            {
                new(Role: "user", Parts: new List<GeminiPart> { new(userPrompt) }),
            },
            CachedContent: cachedContent,
            SystemInstruction: cachedContent != null ? null : new GeminiContent(Role: null, Parts: new List<GeminiPart> { new(ctx.SystemPrompt) }),
	            GenerationConfig: new GeminiGenerationConfig(
	                Temperature: GeminiModelPolicy.GetTemperatureForTranslation(ctx.ModelName, ctx.Temperature),
	                MaxOutputTokens: ctx.MaxOutputTokens,
	                ResponseMimeType: "application/json",
	                ResponseSchema: ctx.ResponseSchema,
	                ThinkingConfig: GetEffectiveThinkingConfigForModel(ctx.ModelName),
                    CandidateCount: candidateCount > 1 ? candidateCount : null
	            ),
	            SafetySettings: new List<GeminiSafetySetting>
	            {
	                new("HARM_CATEGORY_HARASSMENT", "BLOCK_NONE"),
                new("HARM_CATEGORY_HATE_SPEECH", "BLOCK_NONE"),
                new("HARM_CATEGORY_SEXUALLY_EXPLICIT", "BLOCK_NONE"),
                new("HARM_CATEGORY_DANGEROUS_CONTENT", "BLOCK_NONE"),
            }
        );
    }

    private static Dictionary<long, (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> BuildBatchById(
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch
    )
    {
        var byId = new Dictionary<long, (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>(batch.Count);
        foreach (var it in batch)
        {
            byId[it.Id] = it;
        }
        return byId;
    }

    private static void EnsureBatchMapMatches(IReadOnlyDictionary<long, string> map, int expectedCount)
    {
        if (map.Count != expectedCount)
        {
            throw new InvalidOperationException($"Batch size mismatch: expected {expectedCount}, got {map.Count}.");
        }
    }

    private IReadOnlyDictionary<long, string> ParseAndValidateBatchMap(string text, int expectedCount)
    {
        var map = TranslationResultParser.ParseTranslations(text);
        EnsureBatchMapMatches(map, expectedCount);
        return map;
    }

    private async Task<IReadOnlyDictionary<long, string>> TranslateBatchOnceAsync(
        BatchTranslateContext ctx,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch,
        string userPrompt
    )
    {
        var cachedContent = ctx.PromptCache != null ? await ctx.PromptCache.GetOrCreateAsync(ctx.CancellationToken) : null;
        var candidateCount = GetBatchCandidateCount(ctx, batch);
        var request = CreateBatchRequest(ctx, userPrompt, cachedContent, candidateCount);

        string text;
        if (candidateCount > 1)
        {
            var candidateTexts = await GenerateContentCandidatesWithGateAsync(
                ctx.ApiKey,
                ctx.ModelName,
                request,
                RequestLane.General,
                ctx.CancellationToken
            );
            text = SelectBestBatchCandidateText(ctx, batch, candidateTexts);
        }
        else
        {
            text = await GenerateContentWithGateAsync(
                ctx.ApiKey,
                ctx.ModelName,
                request,
                RequestLane.General,
                ctx.CancellationToken
            );
        }

        var map = ParseAndValidateBatchMap(text, batch.Count);
        var byId = BuildBatchById(batch);

        var results = new Dictionary<long, string>(capacity: batch.Count);
        var needsRepair = ctx.EnableRepairPass ? new List<PendingRepair>() : null;

        PopulateInitialBatchResults(ctx, batch, map, results, needsRepair);

        if (ctx.EnableRepairPass && needsRepair is { Count: > 0 })
        {
            await ApplyRepairPassAsync(ctx, byId, results, needsRepair);
        }

        await FillMissingResultsAsync(ctx, batch, map, results);
        return results;
    }

    private void PopulateInitialBatchResults(
        BatchTranslateContext ctx,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch,
        IReadOnlyDictionary<long, string> map,
        Dictionary<long, string> results,
        List<PendingRepair>? needsRepair
    )
    {
        foreach (var it in batch)
        {
            if (!map.TryGetValue(it.Id, out var output))
            {
                throw new InvalidOperationException($"Model output missing id: {it.Id}");
            }

            output = PlaceholderSemanticHintInjector.Strip(output);
            output = GlossarySemanticHintInjector.Strip(output);

            string candidate;
            try
            {
                candidate = EnsureTokensPreservedOrRepair(
                    it.Masked,
                    output,
                    context: $"id={it.Id}",
                    glossaryTokenToReplacement: it.Glossary.TokenToReplacement
                );
            }
            catch (InvalidOperationException)
            {
                if (!ctx.EnableRepairPass)
                {
                    throw;
                }

                needsRepair!.Add(new PendingRepair(it.Id, it.Source, it.Masked, it.Glossary, output));
                continue;
            }

            if (ctx.EnableRepairPass && NeedsPlaceholderSemanticRepair(it.Masked, candidate, ctx.TargetLang, _semanticRepairMode))
            {
                needsRepair!.Add(new PendingRepair(it.Id, it.Source, it.Masked, it.Glossary, candidate));
                continue;
            }

            results[it.Id] = candidate;
        }
    }
}
