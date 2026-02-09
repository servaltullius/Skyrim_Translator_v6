using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private sealed record TextRequestContext(
        string ApiKey,
        string ModelName,
        string SystemPrompt,
        PromptCache? PromptCache,
        RequestLane Lane,
        string SourceLang,
        string TargetLang,
        double Temperature,
        int MaxOutputTokens,
        int MaxRetries,
        CancellationToken CancellationToken,
        int CandidateCount = 1
    );

    private readonly record struct TextWithSentinelContext(
        IReadOnlyDictionary<string, string>? GlossaryTokenToReplacement,
        string? StyleHint,
        string Context,
        string? SourceTextForTranslationMemory
    );

    private async Task<string> TranslateTextWithRetriesAsync(
        TextRequestContext request,
        string text,
        IReadOnlyList<(string Source, string Target)> promptOnlyGlossary,
        string? styleHint
    )
    {
        var userPrompt = TranslationPrompt.BuildTextOnlyUserPrompt(
            request.SourceLang,
            request.TargetLang,
            text,
            promptOnlyGlossary,
            styleHint
        );
        return await TranslateUserPromptWithRetriesAsync(request, userPrompt);
    }

    private async Task<IReadOnlyList<string>> TranslateTextCandidatesWithRetriesAsync(
        TextRequestContext request,
        string text,
        IReadOnlyList<(string Source, string Target)> promptOnlyGlossary,
        string? styleHint
    )
    {
        var userPrompt = TranslationPrompt.BuildTextOnlyUserPrompt(
            request.SourceLang,
            request.TargetLang,
            text,
            promptOnlyGlossary,
            styleHint
        );

        return await TranslateUserPromptCandidatesWithRetriesAsync(request, userPrompt);
    }

    private async Task<string> TranslateUserPromptWithRetriesAsync(
        TextRequestContext request,
        string userPrompt
    )
    {
        var currentRequest = request;
        Exception? last = null;
        for (var attempt = 0; attempt <= request.MaxRetries; attempt++)
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await TranslateUserPromptOnceAsync(currentRequest, userPrompt);
            }
            catch (Exception ex)
            {
                if (currentRequest.PromptCache != null && IsCachedContentInvalid(ex))
                {
                    if (IsCachedContentPermissionDenied(ex))
                    {
                        currentRequest.PromptCache.Disable();
                    }
                    else
                    {
                        currentRequest.PromptCache.Invalidate();
                    }

                    // If the prompt cache resource expired / got invalidated mid-run, retry immediately
                    // without cachedContent so the translation can continue instead of failing the row.
                    var noCacheRequest = currentRequest with { PromptCache = null };
                    try
                    {
                        return await TranslateUserPromptOnceAsync(noCacheRequest, userPrompt);
                    }
                    catch (Exception ex2)
                    {
                        currentRequest = noCacheRequest;
                        ex = ex2;
                    }
                }

                last = ex;
                if (ShouldStopRetryingTextRequest(currentRequest, ex, attempt))
                {
                    break;
                }

                await DelayBeforeTextRetryAsync(ex, attempt, currentRequest.CancellationToken);
            }
        }

        throw new InvalidOperationException($"Translate text failed: {last?.Message}", last);
    }

    private async Task<string> TranslateUserPromptOnceAsync(TextRequestContext request, string userPrompt)
    {
        var cachedContent = request.PromptCache != null
            ? await request.PromptCache.GetOrCreateAsync(request.CancellationToken)
            : null;

        var geminiRequest = BuildTextOnlyGenerateContentRequest(request, userPrompt, cachedContent);
        var modelText = await GenerateContentWithGateAsync(
            request.ApiKey,
            request.ModelName,
            geminiRequest,
            request.Lane,
            request.CancellationToken
        );
        return NormalizeTextOnlyOutput(modelText);
    }

    private async Task<IReadOnlyList<string>> TranslateUserPromptCandidatesWithRetriesAsync(
        TextRequestContext request,
        string userPrompt
    )
    {
        var currentRequest = request;
        Exception? last = null;
        for (var attempt = 0; attempt <= request.MaxRetries; attempt++)
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            try
            {
                return await TranslateUserPromptCandidatesOnceAsync(currentRequest, userPrompt);
            }
            catch (Exception ex)
            {
                if (currentRequest.PromptCache != null && IsCachedContentInvalid(ex))
                {
                    if (IsCachedContentPermissionDenied(ex))
                    {
                        currentRequest.PromptCache.Disable();
                    }
                    else
                    {
                        currentRequest.PromptCache.Invalidate();
                    }

                    var noCacheRequest = currentRequest with { PromptCache = null };
                    try
                    {
                        return await TranslateUserPromptCandidatesOnceAsync(noCacheRequest, userPrompt);
                    }
                    catch (Exception ex2)
                    {
                        currentRequest = noCacheRequest;
                        ex = ex2;
                    }
                }

                last = ex;
                if (ShouldStopRetryingTextRequest(currentRequest, ex, attempt))
                {
                    break;
                }

                await DelayBeforeTextRetryAsync(ex, attempt, currentRequest.CancellationToken);
            }
        }

        throw new InvalidOperationException($"Translate text candidates failed: {last?.Message}", last);
    }

    private async Task<IReadOnlyList<string>> TranslateUserPromptCandidatesOnceAsync(TextRequestContext request, string userPrompt)
    {
        var cachedContent = request.PromptCache != null
            ? await request.PromptCache.GetOrCreateAsync(request.CancellationToken)
            : null;

        var geminiRequest = BuildTextOnlyGenerateContentRequest(request, userPrompt, cachedContent);
        var modelTexts = await GenerateContentCandidatesWithGateAsync(
            request.ApiKey,
            request.ModelName,
            geminiRequest,
            request.Lane,
            request.CancellationToken
        );

        var results = new List<string>(capacity: modelTexts.Count);
        for (var i = 0; i < modelTexts.Count; i++)
        {
            var text = modelTexts[i];
            if (string.IsNullOrWhiteSpace(text))
            {
                continue;
            }

            results.Add(NormalizeTextOnlyOutput(text));
        }

        if (results.Count == 0)
        {
            throw new InvalidOperationException("Translate text candidates failed: model returned no text candidates.");
        }

        return results;
    }

    private static bool ShouldStopRetryingTextRequest(TextRequestContext request, Exception ex, int attempt)
    {
        // For long chunks, timeouts are usually size-related; prefer splitting over retrying the same request.
        if (request.Lane == RequestLane.VeryLong && ex is TaskCanceledException && !request.CancellationToken.IsCancellationRequested)
        {
            return true;
        }

        return !ShouldRetry(ex) || attempt >= request.MaxRetries;
    }

    private async Task DelayBeforeTextRetryAsync(Exception ex, int attempt, CancellationToken cancellationToken)
    {
        var delay = ComputeRetryDelay(ex, attempt);
        if (IsRateLimit(ex))
        {
            ExtendGlobalThrottle(delay);
        }
        await Task.Delay(delay, cancellationToken);
    }

    private GeminiGenerateContentRequest BuildTextOnlyGenerateContentRequest(
        TextRequestContext request,
        string userPrompt,
        string? cachedContent
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
                : new GeminiContent(Role: null, Parts: new List<GeminiPart> { new(request.SystemPrompt) }),
	            GenerationConfig: new GeminiGenerationConfig(
	                Temperature: GeminiModelPolicy.GetTemperatureForTranslation(request.ModelName, request.Temperature),
	                MaxOutputTokens: request.MaxOutputTokens,
	                ResponseMimeType: null,
	                ResponseSchema: null,
	                ThinkingConfig: GetEffectiveThinkingConfigForModel(request.ModelName),
                    CandidateCount: request.CandidateCount > 1 ? request.CandidateCount : null
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

    private async Task<string> TranslateTextWithSentinelAsync(
        TextRequestContext request,
        string text,
        IReadOnlyList<(string Source, string Target)> promptOnlyGlossary,
        TextWithSentinelContext sentinelContext
    )
    {
        // Add a small separator so the sentinel doesn't end up adjacent to another __XT_* token,
        // which some models occasionally mangle at chunk boundaries.
        var textForPrompt = PlaceholderSemanticHintInjector.Inject(request.TargetLang, text);
        textForPrompt = GlossarySemanticHintInjector.Inject(request.TargetLang, textForPrompt, sentinelContext.GlossaryTokenToReplacement);
        var promptTextWithSentinel = textForPrompt + " " + EndSentinelToken;
        var validateInputWithSentinel = text + " " + EndSentinelToken;
        var mergedPromptOnlyGlossary = MergeSessionPromptOnlyGlossaryForText(text, promptOnlyGlossary);
        if (request.CandidateCount > 1)
        {
            var translatedCandidates = await TranslateTextCandidatesWithRetriesAsync(
                request,
                promptTextWithSentinel,
                mergedPromptOnlyGlossary,
                sentinelContext.StyleHint
            );

            return SelectBestTextWithSentinelCandidate(
                request,
                text,
                validateInputWithSentinel,
                sentinelContext,
                translatedCandidates
            );
        }

        var translated = await TranslateTextWithRetriesAsync(
            request,
            promptTextWithSentinel,
            mergedPromptOnlyGlossary,
            sentinelContext.StyleHint
        );

        return NormalizeSingleTextWithSentinelCandidate(
            request,
            text,
            validateInputWithSentinel,
            sentinelContext,
            translated
        );
    }

    private string SelectBestTextWithSentinelCandidate(
        TextRequestContext request,
        string text,
        string validateInputWithSentinel,
        TextWithSentinelContext sentinelContext,
        IReadOnlyList<string> translatedCandidates
    )
    {
        if (translatedCandidates.Count == 0)
        {
            throw new InvalidOperationException("Translate text candidates failed: no candidates.");
        }

        var bestText = "";
        var bestScore = int.MinValue;
        var hasBest = false;

        for (var i = 0; i < translatedCandidates.Count; i++)
        {
            if (!TryNormalizeAndScoreTextWithSentinelCandidate(
                    request,
                    text,
                    validateInputWithSentinel,
                    sentinelContext,
                    translatedCandidates[i],
                    out var normalized,
                    out var score))
            {
                continue;
            }

            if (!hasBest || score > bestScore)
            {
                hasBest = true;
                bestScore = score;
                bestText = normalized;
            }
        }

        if (hasBest)
        {
            return bestText;
        }

        // Fallback: preserve previous behavior by validating the first candidate directly.
        return NormalizeSingleTextWithSentinelCandidate(
            request,
            text,
            validateInputWithSentinel,
            sentinelContext,
            translatedCandidates[0]
        );
    }

    private bool TryNormalizeAndScoreTextWithSentinelCandidate(
        TextRequestContext request,
        string text,
        string validateInputWithSentinel,
        TextWithSentinelContext sentinelContext,
        string translated,
        out string normalized,
        out int score
    )
    {
        normalized = "";
        score = 0;

        try
        {
            normalized = NormalizeSingleTextWithSentinelCandidate(
                request,
                text,
                validateInputWithSentinel,
                sentinelContext,
                translated
            );
        }
        catch
        {
            return false;
        }

        score = 100;
        if (translated.IndexOf(EndSentinelToken, StringComparison.Ordinal) < 0)
        {
            score -= 10;
        }

        if (NeedsPlaceholderSemanticRepair(text, normalized, request.TargetLang, _semanticRepairMode))
        {
            score -= 40;
        }

        if (!string.IsNullOrWhiteSpace(sentinelContext.SourceTextForTranslationMemory))
        {
            var sourceText = sentinelContext.SourceTextForTranslationMemory!;
            if (TryGetQualityEscalationTrigger(sourceText, normalized, request.TargetLang, out _, out _))
            {
                score -= 40;
            }

            if (LqaHeuristics.IsLikelyUntranslated(sourceText, normalized))
            {
                score -= 80;
            }
        }

        if (LqaHeuristics.HasUnresolvedParticleMarkers(normalized))
        {
            score -= 30;
        }

        return true;
    }

    private string NormalizeSingleTextWithSentinelCandidate(
        TextRequestContext request,
        string text,
        string validateInputWithSentinel,
        TextWithSentinelContext sentinelContext,
        string translated
    )
    {
        var cleaned = SanitizeModelTranslationText(translated, text);
        cleaned = PlaceholderSemanticHintInjector.Strip(cleaned);
        cleaned = GlossarySemanticHintInjector.Strip(cleaned);
        if (!cleaned.Contains(EndSentinelToken, StringComparison.Ordinal))
        {
            // The sentinel can be dropped/mangled even when the translation is otherwise fine.
            // If the output passes token integrity + anti-omission checks without the sentinel, accept it.
            // Otherwise, the caller will split and retry with smaller chunks.
            var ensured = EnsureTokensPreservedOrRepair(
                text,
                cleaned,
                sentinelContext.Context,
                sentinelContext.GlossaryTokenToReplacement
            );

            return ensured.TrimEnd();
        }

        var validated = EnsureTokensPreservedOrRepair(
            validateInputWithSentinel,
            cleaned,
            sentinelContext.Context,
            sentinelContext.GlossaryTokenToReplacement
        );
        var withoutSentinel = validated.Replace(EndSentinelToken, "", StringComparison.Ordinal);
        return withoutSentinel.TrimEnd();
    }

    private static string NormalizeTextOnlyOutput(string modelText)
    {
        var raw = modelText;
        if (raw.StartsWith("```", StringComparison.Ordinal))
        {
            raw = StripCodeFence(raw);
        }

        var trimmed = raw.Trim();

        // Sometimes the model still returns JSON even if we asked for plain text.
        if (trimmed.IndexOf("\"translations\"", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            try
            {
                var map = TranslationResultParser.ParseTranslations(raw);
                if (map.Count == 1)
                {
                    foreach (var v in map.Values)
                    {
                        return v;
                    }
                }
            }
            catch
            {
                // ignore
            }
        }

        // Sometimes it's a quoted JSON string.
        if (trimmed.StartsWith("\"", StringComparison.Ordinal) && trimmed.EndsWith("\"", StringComparison.Ordinal))
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(trimmed);
                if (doc.RootElement.ValueKind == System.Text.Json.JsonValueKind.String)
                {
                    return doc.RootElement.GetString() ?? "";
                }
            }
            catch
            {
                // ignore
            }
        }

        return raw;
    }

    private static string StripCodeFence(string raw)
    {
        var s = raw;
        var firstNewline = s.IndexOf('\n');
        if (firstNewline >= 0)
        {
            s = s[(firstNewline + 1)..];
        }
        if (s.EndsWith("```", StringComparison.Ordinal))
        {
            s = s[..^3];
        }
        return s.Trim();
    }

    private static GeminiThinkingConfig? GetThinkingConfigForModel(string modelName)
    {
        return GeminiModelPolicy.GetThinkingConfigForTranslation(modelName);
    }

    private GeminiThinkingConfig? GetEffectiveThinkingConfigForModel(string modelName)
    {
        return _thinkingConfigOverride ?? GetThinkingConfigForModel(modelName);
    }
}
