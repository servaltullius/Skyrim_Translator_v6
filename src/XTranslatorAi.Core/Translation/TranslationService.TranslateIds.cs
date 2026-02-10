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
        InitializeTranslateIdsRunState(request);

        PromptCache? promptCache = null;
        try
        {
            if (request.EnablePromptCache)
            {
                promptCache = await TryCreatePromptCacheAsync(
                    request.ApiKey,
                    request.ModelName,
                    request.SystemPrompt,
                    request.CancellationToken
                );
            }

            await TranslateIdsCoreBodyAsync(request, promptCache);
        }
        finally
        {
            await CleanupTranslateIdsRunStateAsync(promptCache);
        }
    }

    private void InitializeTranslateIdsRunState(TranslateIdsRequest request)
    {
        _thinkingConfigOverride = request.ThinkingConfigOverride;
        _enableSessionTermMemory = request.EnableSessionTermMemory;
        _sessionTermMemory = request.EnableSessionTermMemory ? new SessionTermMemory(DefaultSessionTermMemoryMaxTerms) : null;
        _pendingSessionAutoGlossaryInserts = null;
        _sessionAutoGlossaryKnownKeys = null;
        _semanticRepairMode = request.EnableRepairPass ? request.SemanticRepairMode : PlaceholderSemanticRepairMode.Off;
        _enableTemplateFixer = request.EnableTemplateFixer;
        _useRecStyleHints = request.UseRecStyleHints;
        _enableDialogueContextWindow = request.EnableDialogueContextWindow;
        _enableQualityEscalation = request.EnableQualityEscalation && !string.IsNullOrWhiteSpace(request.QualityEscalationModelName);
        _qualityEscalationModelName = string.IsNullOrWhiteSpace(request.QualityEscalationModelName) ? null : request.QualityEscalationModelName.Trim();
        _enableRiskyCandidateRerank = request.EnableRiskyCandidateRerank;
        _riskyCandidateCount = Math.Clamp(request.RiskyCandidateCount, 2, 8);

        if (_enableSessionTermMemory && _sessionTermMemory != null && request.PreloadedSessionTerms != null)
        {
            foreach (var (source, target) in request.PreloadedSessionTerms)
            {
                if (string.IsNullOrWhiteSpace(source) || string.IsNullOrWhiteSpace(target))
                {
                    continue;
                }
                _sessionTermMemory.TryLearn(source, target);
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

        var schema = TranslationPrompt.BuildResponseSchema();

        var maxConcurrency = Math.Max(1, request.MaxConcurrency);
        _generateContentGate = new SemaphoreSlim(maxConcurrency, maxConcurrency);
        ConfigureAdaptiveConcurrency(maxConcurrency);
        _longTextChunkParallelism = maxConcurrency >= 5 ? 2 : 1;
        _maskedTokensPerCharHint = null;

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
        _generateContentGate?.Dispose();
        _generateContentGate = null;
        _veryLongRequestGate?.Dispose();
        _veryLongRequestGate = null;
        _rowContextById = null;
        _dialogueContextWindowById = null;
        _sessionTermMemory = null;
        _enableSessionTermMemory = false;
        _enableQualityEscalation = false;
        _qualityEscalationModelName = null;
        _enableRiskyCandidateRerank = true;
        _riskyCandidateCount = 3;
        _pendingSessionAutoGlossaryInserts = null;
        _sessionAutoGlossaryKnownKeys = null;
        _thinkingConfigOverride = null;
        ResetAdaptiveConcurrency();

        if (promptCache != null)
        {
            try
            {
                await promptCache.DeleteAsync(CancellationToken.None);
            }
            catch
            {
                // ignore
            }
        }
    }

    private async Task<PromptCache?> TryCreatePromptCacheAsync(
        string apiKey,
        string modelName,
        string systemPrompt,
        CancellationToken cancellationToken
    )
    {
        try
        {
            var cache = new PromptCache(_gemini, apiKey, modelName, systemPrompt, ttl: TimeSpan.FromHours(2));
            _ = await cache.GetOrCreateAsync(cancellationToken);
            return cache;
        }
        catch (Exception ex)
        {
            if (IsCredentialError(ex))
            {
                throw;
            }

            return null;
        }
    }

	    private void InitializeSessionAutoGlossary(IReadOnlyList<GlossaryEntry> glossary)
	    {
	        if (!_enableSessionTermMemory || !EnableSessionTermAutoGlossaryPersistence)
        {
            return;
        }

        _pendingSessionAutoGlossaryInserts = new ConcurrentQueue<(string Source, string Target)>();
        _sessionAutoGlossaryKnownKeys = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
        foreach (var g in glossary)
        {
            var key = NormalizeSessionTermKey(g.SourceTerm);
            if (!string.IsNullOrWhiteSpace(key))
            {
                _sessionAutoGlossaryKnownKeys.TryAdd(key, 0);
            }
        }
    }
}
