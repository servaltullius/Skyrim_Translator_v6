using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static IReadOnlyList<(string Source, string Target)> CollectPromptOnlyPairs(IReadOnlyList<PendingRepair> needsRepair)
    {
        var repairPromptOnlyPairs = new List<(string Source, string Target)>();
        var repairPromptOnlySet = new HashSet<(string Source, string Target)>(new SourceTargetComparer());
        foreach (var r in needsRepair)
        {
            foreach (var p in r.Glossary.PromptOnlyPairs)
            {
                if (repairPromptOnlySet.Add(p))
                {
                    repairPromptOnlyPairs.Add(p);
                }
            }
        }
        return repairPromptOnlyPairs;
    }

    private List<RepairTranslationItem> BuildRepairBatchItems(IReadOnlyList<PendingRepair> needsRepair)
    {
        var repairBatchItems = new List<RepairTranslationItem>(needsRepair.Count);
        foreach (var r in needsRepair)
        {
            var rec = _useRecStyleHints ? GetRecForId(r.Id) : null;
            repairBatchItems.Add(new RepairTranslationItem(r.Id, r.Masked, r.Current, rec));
        }
        return repairBatchItems;
    }

    private async Task ApplyRepairPassAsync(
        BatchTranslateContext ctx,
        Dictionary<long, (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> byId,
        Dictionary<long, string> results,
        IReadOnlyList<PendingRepair> needsRepair
    )
    {
        var repairPromptOnlyPairs = CollectPromptOnlyPairs(needsRepair);
        var repairBatchItems = BuildRepairBatchItems(needsRepair);

        // Keep the repair prompt bounded even if this batch contains many short magic-effect strings.
        const int repairMaxItems = 8;
        const int repairMaxWeight = 12000;
        foreach (var repairBatch in TranslationBatching.ChunkBy(
                     repairBatchItems,
                     it => it.Source.Length + it.Current.Length,
                     maxItems: repairMaxItems,
                     maxWeight: repairMaxWeight
                 ))
        {
            var repairedMap = await TryRepairBatchAsync(ctx, repairBatch, repairPromptOnlyPairs);
            await ApplyRepairBatchResultsAsync(ctx, byId, results, repairBatch, repairedMap);
        }
    }

    private async Task<IReadOnlyDictionary<long, string>> TryRepairBatchAsync(
        BatchTranslateContext ctx,
        IReadOnlyList<RepairTranslationItem> repairBatch,
        IReadOnlyList<(string Source, string Target)> repairPromptOnlyPairs
    )
    {
        try
        {
            return await RepairBatchWithRetriesAsync(ctx, repairBatch, repairPromptOnlyPairs, maxRetries: 1);
        }
        catch
        {
            // If the repair batch fails (invalid JSON/timeouts), fall back to per-item retries.
            return new Dictionary<long, string>(capacity: repairBatch.Count);
        }
    }

    private static GeminiGenerateContentRequest BuildRepairBatchRequest(
        string userPrompt,
        string? cachedContent,
        string systemPrompt,
        GeminiGenerationConfig generationConfig
    )
    {
        return new GeminiGenerateContentRequest(
            Contents: new List<GeminiContent>
            {
                new(Role: "user", Parts: new List<GeminiPart> { new(userPrompt) }),
            },
            CachedContent: cachedContent,
            SystemInstruction: cachedContent != null
                ? null
                : new GeminiContent(Role: null, Parts: new List<GeminiPart> { new(systemPrompt) }),
            GenerationConfig: generationConfig,
            SafetySettings: new List<GeminiSafetySetting>
            {
                new("HARM_CATEGORY_HARASSMENT", "BLOCK_NONE"),
                new("HARM_CATEGORY_HATE_SPEECH", "BLOCK_NONE"),
                new("HARM_CATEGORY_SEXUALLY_EXPLICIT", "BLOCK_NONE"),
                new("HARM_CATEGORY_DANGEROUS_CONTENT", "BLOCK_NONE"),
            }
        );
    }

    private async Task ApplyRepairBatchResultsAsync(
        BatchTranslateContext ctx,
        Dictionary<long, (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> byId,
        Dictionary<long, string> results,
        IReadOnlyList<RepairTranslationItem> repairBatch,
        IReadOnlyDictionary<long, string> repairedMap
    )
    {
        foreach (var repairedItem in repairBatch)
        {
            if (!byId.TryGetValue(repairedItem.Id, out var original))
            {
                continue;
            }

            if (!repairedMap.TryGetValue(repairedItem.Id, out var repairedText) || string.IsNullOrWhiteSpace(repairedText))
            {
                results[repairedItem.Id] = await TranslateRepairFallbackAsync(ctx, original, $"id={repairedItem.Id} repair-fallback");
                continue;
            }

            try
            {
                results[repairedItem.Id] = EnsureTokensPreservedOrRepair(
                    original.Masked,
                    repairedText,
                    context: $"id={repairedItem.Id} repair-batch",
                    glossaryTokenToReplacement: original.Glossary.TokenToReplacement
                );
            }
            catch (InvalidOperationException)
            {
                results[repairedItem.Id] = await TranslateRepairFallbackAsync(ctx, original, $"id={repairedItem.Id} repair-fallback");
            }
        }
    }

    private async Task<string> TranslateRepairFallbackAsync(
        BatchTranslateContext ctx,
        (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary) original,
        string context
    )
    {
        var rec = _useRecStyleHints ? GetRecForId(original.Id) : null;
        var styleHint = GuessStyleHint(original.Source, rec);
        var request = new TextRequestContext(
            ApiKey: ctx.ApiKey,
            ModelName: ctx.ModelName,
            SystemPrompt: ctx.SystemPrompt,
            PromptCache: ctx.PromptCache,
            Lane: RequestLane.General,
            SourceLang: ctx.SourceLang,
            TargetLang: ctx.TargetLang,
            Temperature: 0.0,
            MaxOutputTokens: ctx.MaxOutputTokens,
            MaxRetries: 1,
            CancellationToken: ctx.CancellationToken
        );

        return await TranslateTextWithSentinelAsync(
            request,
            original.Masked,
            original.Glossary.PromptOnlyPairs,
            new TextWithSentinelContext(
                GlossaryTokenToReplacement: original.Glossary.TokenToReplacement,
                StyleHint: styleHint,
                Context: context,
                SourceTextForTranslationMemory: original.Source
            )
        );
    }

    private async Task FillMissingResultsAsync(
        BatchTranslateContext ctx,
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> batch,
        IReadOnlyDictionary<long, string> map,
        Dictionary<long, string> results
    )
    {
        if (results.Count == batch.Count)
        {
            return;
        }

        foreach (var it in batch)
        {
            if (results.ContainsKey(it.Id))
            {
                continue;
            }

            if (!map.TryGetValue(it.Id, out var output))
            {
                continue;
            }

            try
            {
                results[it.Id] = EnsureTokensPreservedOrRepair(
                    it.Masked,
                    output,
                    context: $"id={it.Id} final",
                    glossaryTokenToReplacement: it.Glossary.TokenToReplacement
                );
            }
            catch (InvalidOperationException)
            {
                if (!ctx.EnableRepairPass)
                {
                    throw;
                }

                results[it.Id] = await TranslateRepairFallbackAsync(ctx, it, $"id={it.Id} final-fallback");
            }
        }
    }

    private async Task<IReadOnlyDictionary<long, string>> RepairBatchWithRetriesAsync(
        BatchTranslateContext ctx,
        IReadOnlyList<RepairTranslationItem> batch,
        IReadOnlyList<(string Source, string Target)> promptOnlyGlossary,
        int maxRetries
    )
    {
        var cancellationToken = ctx.CancellationToken;
        var userPrompt = BuildRepairBatchUserPrompt(ctx, batch, promptOnlyGlossary);
        var currentCtx = ctx;

        Exception? last = null;
        for (var attempt = 0; attempt <= maxRetries; attempt++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await RepairBatchOnceAsync(currentCtx, userPrompt, cancellationToken);
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
                        return await RepairBatchOnceAsync(noCacheCtx, userPrompt, cancellationToken);
                    }
                    catch (Exception ex2)
                    {
                        currentCtx = noCacheCtx;
                        ex = ex2;
                    }
                }

                last = ex;
                if (!ShouldRetry(ex) || attempt >= maxRetries)
                {
                    break;
                }

                await DelayBeforeRetryAsync(ex, attempt, cancellationToken);
            }
        }

        throw new InvalidOperationException($"Repair batch failed: {last?.Message}", last);
    }

    private string BuildRepairBatchUserPrompt(
        BatchTranslateContext ctx,
        IReadOnlyList<RepairTranslationItem> batch,
        IReadOnlyList<(string Source, string Target)> promptOnlyGlossary
    )
    {
        var sources = ExtractRepairSources(batch);
        var mergedPromptOnlyGlossary = MergeSessionPromptOnlyGlossaryForTexts(sources, promptOnlyGlossary);
        return TranslationPrompt.BuildRepairBatchUserPrompt(ctx.SourceLang, ctx.TargetLang, batch, mergedPromptOnlyGlossary);
    }

    private static List<string> ExtractRepairSources(IReadOnlyList<RepairTranslationItem> batch)
    {
        var sources = new List<string>(batch.Count);
        for (var i = 0; i < batch.Count; i++)
        {
            sources.Add(batch[i].Source);
        }
        return sources;
    }

    private async Task<IReadOnlyDictionary<long, string>> RepairBatchOnceAsync(
        BatchTranslateContext ctx,
        string userPrompt,
        CancellationToken cancellationToken
    )
    {
        var cachedContent = ctx.PromptCache != null ? await ctx.PromptCache.GetOrCreateAsync(cancellationToken) : null;
        var request = BuildRepairBatchRequest(
            userPrompt,
            cachedContent,
            ctx.SystemPrompt,
	            new GeminiGenerationConfig(
	                Temperature: GeminiModelPolicy.GetTemperatureForTranslation(ctx.ModelName, 0.0),
	                MaxOutputTokens: ctx.MaxOutputTokens,
	                ResponseMimeType: "application/json",
	                ResponseSchema: ctx.ResponseSchema,
	                ThinkingConfig: GetEffectiveThinkingConfigForModel(ctx.ModelName)
	            )
	        );

        var text = await GenerateContentWithGateAsync(ctx.ApiKey, ctx.ModelName, request, RequestLane.General, cancellationToken);
        return TranslationResultParser.ParseTranslations(text);
    }

    private async Task DelayBeforeRetryAsync(Exception ex, int attempt, CancellationToken cancellationToken)
    {
        var delay = ComputeRetryDelay(ex, attempt);
        if (IsRateLimit(ex))
        {
            RegisterAdaptiveRateLimit();
            ExtendGlobalThrottle(delay);
        }
        await Task.Delay(delay, cancellationToken);
    }
}
