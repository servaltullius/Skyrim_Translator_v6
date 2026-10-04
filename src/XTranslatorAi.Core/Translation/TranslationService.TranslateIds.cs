using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private async Task TranslateIdsCoreAsync(TranslateIdsRequest request)
    {
        request = request with { MaxChars = TranslationOutputBudget.GetSourceCharLimit(request.MaxChars, request.MaxOutputTokens, request.EnableAdaptiveOutputBudget) };
        InitializeTranslateIdsRunState(request);

        PromptCache? promptCache = null;
        try
        {
            if (request.EnablePromptCache)
            {
                // GetOrCreateAsync is called by the first actual generation request.
                // TM-only and empty runs must not create a billable cache.
                promptCache = new PromptCache(_gemini, request.ApiKey, request.ModelName,
                    request.SystemPrompt, ttl: TimeSpan.FromHours(2));
            }

            await TranslateIdsCoreBodyAsync(request, promptCache);
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
            await RestoreCancelledRowsAsync(request);
            throw;
        }
        catch (Exception)
        {
            // Includes seed/auth/budget failures before workers start. Never leave
            // unfinished rows stuck InProgress after an aborted run.
            await RestoreCancelledRowsAsync(request);
            throw;
        }
        finally
        {
            await CleanupTranslateIdsRunStateAsync(promptCache);
        }
    }

    private async Task RestoreCancelledRowsAsync(TranslateIdsRequest request)
    {
        // All workers have settled before this path. Keep completed rows and only
        // restore unfinished rows owned by this run, including duplicate rows.
        var statuses = await _db.GetStringStatusesByIdsAsync(request.Ids, CancellationToken.None);
        var pending = new List<long>();
        foreach (var (id, status) in statuses)
        {
            if (status == StringEntryStatus.InProgress)
            {
                pending.Add(id);
            }
        }
        if (pending.Count == 0)
        {
            return;
        }

        await _db.UpdateStringStatusesAsync(pending, StringEntryStatus.Pending, null, CancellationToken.None);
        if (request.OnRowUpdated != null)
        {
            foreach (var id in pending)
            {
                NotifyRowUpdated(request.OnRowUpdated, id, StringEntryStatus.Pending, "");
            }
        }
    }

    private void InitializeTranslateIdsRunState(TranslateIdsRequest request)
    {
        _ctx = new TranslationRunContext
        {
            ThinkingConfigOverride = request.ThinkingConfigOverride,
            EnableSessionTermMemory = request.EnableSessionTermMemory,
            SessionTermMemory = request.EnableSessionTermMemory ? new SessionTermMemory(DefaultSessionTermMemoryMaxTerms) : null,
            SemanticRepairMode = request.EnableRepairPass ? request.SemanticRepairMode : PlaceholderSemanticRepairMode.Off,
            EnableTemplateFixer = request.EnableTemplateFixer,
            UseRecStyleHints = request.UseRecStyleHints,
            EnableDialogueContextWindow = request.EnableDialogueContextWindow,
            EnableQualityEscalation = request.EnableQualityEscalation && !string.IsNullOrWhiteSpace(request.QualityEscalationModelName),
            QualityEscalationModelName = string.IsNullOrWhiteSpace(request.QualityEscalationModelName) ? null : request.QualityEscalationModelName.Trim(),
            EnableRiskyCandidateRerank = request.EnableRiskyCandidateRerank,
            RiskyCandidateCount = Math.Clamp(request.RiskyCandidateCount, 2, 8),
            GenerationBudget = request.GenerationBudget ?? new TranslationGenerationBudget(request.MaxRetryGenerations, request.MaxTotalGenerations),
            EnableAdaptiveOutputBudget = request.EnableAdaptiveOutputBudget,
            EnableBookContext = request.EnableBookContext,
        };

        if (Ctx.EnableSessionTermMemory && Ctx.SessionTermMemory != null && request.PreloadedSessionTerms != null)
        {
            foreach (var (source, target) in request.PreloadedSessionTerms)
            {
                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }
                Ctx.SessionTermMemory.TryLearn(source, target);
            }
        }
    }

    private async Task TranslateIdsCoreBodyAsync(TranslateIdsRequest request, PromptCache? promptCache)
    {
        var projectGlossary = await _db.GetGlossaryAsync(request.CancellationToken);
        var glossary = GlossaryMerger.Merge(projectGlossary, request.GlobalGlossary);
        InitializeSessionAutoGlossary(glossary);

        var placeholderMasker = new PlaceholderMasker(new PlaceholderMaskerOptions(KeepSkyrimTagsRaw: request.KeepSkyrimTagsRaw));
        var glossaryApplier = new GlossaryApplier(glossary);
        Ctx.Glossary = glossaryApplier;
        Ctx.ReferenceNames = request.ReferenceNameMemory is { Count: > 0 } names ? ReferenceNameIndex.Build(names) : null;
        // The official item-name form is a Korean convention.
        Ctx.EnchantmentNames = request.ReferenceNameMemory is { Count: > 0 } memory && LanguageHelper.IsKoreanLanguage(request.TargetLang)
            ? EnchantmentNameIndex.Build(memory)
            : null;
        var translationMemory = MergeTranslationMemory(
            request.GlobalTranslationMemory,
            await LoadTranslationMemoryAsync(request.SourceLang, request.TargetLang, request.CancellationToken)
        );

        var items = await BuildTranslationItemsAsync(
            request,
            placeholderMasker,
            glossaryApplier,
            translationMemory
        );

        if (request.EnableRunNameMemory)
        {
            await InitializeRunNameMemoryAsync(items, request.CancellationToken);
        }

        var schema = TranslationPrompt.BuildResponseSchema();
        foreach (var item in items)
            Ctx.GenerationBudget!.RegisterRow(item.Id, item.Masked.Length, TranslationConstants.XtTokenRegex.Matches(item.Masked).Count);

        var maxConcurrency = Math.Max(1, request.MaxConcurrency);
        Ctx.GenerateContentGate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        Ctx.AdaptiveConcurrency.Configure(maxConcurrency);
        Ctx.LongTextChunkParallelism = maxConcurrency >= 5 ? 2 : 1;
        Ctx.MaskedTokensPerCharHint = null;

        items = await SeedSessionTermMemoryAsync(
            request,
            items,
            promptCache,
            placeholderMasker,
            schema
        );

        var queues = await BuildWorkQueuesAsync(
            request,
            items,
            maxConcurrency
        );

        await RunWorkersAsync(
            request,
            promptCache,
            placeholderMasker,
            schema,
            queues
        );
    }

    private async Task CleanupTranslateIdsRunStateAsync(PromptCache? promptCache)
    {
        await FlushSessionTermAutoGlossaryInsertsAsync();
        _ctx?.Dispose();
        _ctx = null;

        if (promptCache != null)
        {
            try
            {
                using var cleanupTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                await promptCache.DeleteAsync(cleanupTimeout.Token);
            }
            catch
            {
                // Cleanup path in finally block -- swallow all exceptions
                // including OperationCanceledException to avoid masking the original exception.
            }
        }
    }

    private void InitializeSessionAutoGlossary(IReadOnlyList<GlossaryEntry> glossary)
    {
        if (!Ctx.EnableSessionTermMemory || !EnableSessionTermAutoGlossaryPersistence)
        {
            return;
        }

        Ctx.PendingSessionAutoGlossaryInserts = new ConcurrentQueue<(string Source, string Target)>();
        Ctx.SessionAutoGlossaryKnownKeys = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in glossary)
        {
            var key = NormalizeSessionTermKey(g.SourceTerm);
            if (!string.IsNullOrWhiteSpace(key))
            {
                Ctx.SessionAutoGlossaryKnownKeys.TryAdd(key, 0);
            }
        }
    }
}
