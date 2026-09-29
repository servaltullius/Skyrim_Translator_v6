using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.Translation;

public sealed partial class GeminiClient
{
    public async Task<IReadOnlyList<string>> GenerateContentCandidatesAsync(
        string apiKey,
        string modelName,
        GeminiGenerateContentRequest request,
        CancellationToken cancellationToken
    )
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        int? statusCode = null;
        try
        {
            var result = await GenerateContentCandidatesCoreAsync(apiKey, modelName, request, cancellationToken);
            statusCode = result.StatusCode;

            var promptTokens = result.Usage?.PromptTokenCount;
            var totalTokens = result.Usage?.TotalTokenCount;
            var completionTokens = ComputeCompletionTokens(promptTokens, totalTokens, result.Usage?.CandidatesTokenCount, result.Usage?.ThoughtsTokenCount);
            var cachedTokens = result.Usage?.CachedContentTokenCount;
            var costUsd = GeminiUsageCost.TryEstimateUsd(
                modelName,
                promptTokens: promptTokens,
                completionTokens: completionTokens,
                cachedContentTokens: cachedTokens
            );

            LogCall(
                new GeminiCallLogEntry(
                    StartedAt: startedAt,
                    Duration: sw.Elapsed,
                    Operation: GeminiCallOperation.GenerateContent,
                    ModelName: modelName,
                    StatusCode: statusCode,
                    Success: true,
                    ErrorMessage: null,
                    ApiKeyMask: MaskApiKey(apiKey),
                    PromptTokens: promptTokens,
                    CompletionTokens: completionTokens,
                    TotalTokens: totalTokens ?? (promptTokens is >= 0 && completionTokens is >= 0 ? promptTokens + completionTokens : null),
                    CachedContentTokens: cachedTokens,
                    CostUsd: costUsd
                )
            );

            return result.CandidateTexts;
        }
        catch (Exception ex)
        {
            statusCode ??= TryGetStatusCode(ex);
            LogCall(
                new GeminiCallLogEntry(
                    StartedAt: startedAt,
                    Duration: sw.Elapsed,
                    Operation: GeminiCallOperation.GenerateContent,
                    ModelName: modelName,
                    StatusCode: statusCode,
                    Success: false,
                    ErrorMessage: Truncate(ex.Message, 800),
                    ApiKeyMask: MaskApiKey(apiKey)
                )
            );
            throw;
        }
    }

    public async Task<string> GenerateContentAsync(
        string apiKey,
        string modelName,
        GeminiGenerateContentRequest request,
        CancellationToken cancellationToken
    ) => (await GenerateContentWithUsageAsync(apiKey, modelName, request, cancellationToken)).Text;

    public async Task<GeminiGenerationResult> GenerateContentWithUsageAsync(
        string apiKey,
        string modelName,
        GeminiGenerateContentRequest request,
        CancellationToken cancellationToken
    )
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        int? statusCode = null;
        try
        {
            var result = await GenerateContentCoreAsync(apiKey, modelName, request, cancellationToken);
            statusCode = result.StatusCode;

            var promptTokens = result.Usage?.PromptTokenCount;
            var totalTokens = result.Usage?.TotalTokenCount;
            var completionTokens = ComputeCompletionTokens(promptTokens, totalTokens, result.Usage?.CandidatesTokenCount, result.Usage?.ThoughtsTokenCount);
            var cachedTokens = result.Usage?.CachedContentTokenCount;
            var costUsd = GeminiUsageCost.TryEstimateUsd(
                modelName,
                promptTokens: promptTokens,
                completionTokens: completionTokens,
                cachedContentTokens: cachedTokens
            );

            LogCall(
                new GeminiCallLogEntry(
                    StartedAt: startedAt,
                    Duration: sw.Elapsed,
                    Operation: GeminiCallOperation.GenerateContent,
                    ModelName: modelName,
                    StatusCode: statusCode,
                    Success: true,
                    ErrorMessage: null,
                    ApiKeyMask: MaskApiKey(apiKey),
                    PromptTokens: promptTokens,
                    CompletionTokens: completionTokens,
                    TotalTokens: totalTokens ?? (promptTokens is >= 0 && completionTokens is >= 0 ? promptTokens + completionTokens : null),
                    CachedContentTokens: cachedTokens,
                    CostUsd: costUsd
                )
            );
            return new GeminiGenerationResult(result.Text, promptTokens, completionTokens, cachedTokens);
        }
        catch (Exception ex)
        {
            statusCode ??= TryGetStatusCode(ex);
            LogCall(
                new GeminiCallLogEntry(
                    StartedAt: startedAt,
                    Duration: sw.Elapsed,
                    Operation: GeminiCallOperation.GenerateContent,
                    ModelName: modelName,
                    StatusCode: statusCode,
                    Success: false,
                    ErrorMessage: Truncate(ex.Message, 800),
                    ApiKeyMask: MaskApiKey(apiKey)
                )
            );
            throw;
        }
    }

    private async Task<(IReadOnlyList<string> CandidateTexts, int StatusCode, GeminiUsageMetadata? Usage)> GenerateContentCandidatesCoreAsync(
        string apiKey,
        string modelName,
        GeminiGenerateContentRequest request,
        CancellationToken cancellationToken
    )
    {
        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(modelName)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

        using var resp = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, cancellationToken);
        var statusCode = (int)resp.StatusCode;
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            throw CreateHttpException("GenerateContent", resp, body);
        }

        var parsed = DeserializeOrThrow<GeminiGenerateContentResponse>("GenerateContent", body);
        var candidateTexts = ExtractCandidateTextsOrThrow(parsed, body);
        return (candidateTexts, statusCode, parsed?.UsageMetadata);
    }

    private async Task<(string Text, int StatusCode, GeminiUsageMetadata? Usage)> GenerateContentCoreAsync(
        string apiKey,
        string modelName,
        GeminiGenerateContentRequest request,
        CancellationToken cancellationToken
    )
    {
        var url =
            $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(modelName)}:generateContent?key={Uri.EscapeDataString(apiKey)}";

        using var resp = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, cancellationToken);
        var statusCode = (int)resp.StatusCode;
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            throw CreateHttpException("GenerateContent", resp, body);
        }

        var parsed = DeserializeOrThrow<GeminiGenerateContentResponse>("GenerateContent", body);

        var candidate = parsed?.Candidates?.FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(candidate?.FinishReason)
            && string.Equals(candidate!.FinishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase))
        {
            throw new GeminiException("GenerateContent: finishReason=MAX_TOKENS (output truncated).");
        }

        if (!IsCompletedCandidate(candidate))
            throw new GeminiException($"GenerateContent: finishReason={candidate?.FinishReason} (incomplete response).");

        var text = ExtractFinalText(candidate);
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new GeminiException("GenerateContent: missing final text in response parts.");
        }

        return (text!, statusCode, parsed?.UsageMetadata);
    }

    private static IReadOnlyList<string> ExtractCandidateTextsOrThrow(GeminiGenerateContentResponse? parsed, string body)
    {
        var texts = new List<string>();
        var sawMaxTokens = false;
        var sawIncomplete = false;

        var candidates = parsed?.Candidates;
        if (candidates != null)
        {
            for (var i = 0; i < candidates.Count; i++)
            {
                var candidate = candidates[i];
                if (candidate == null)
                {
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(candidate.FinishReason)
                    && string.Equals(candidate.FinishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase))
                {
                    sawMaxTokens = true;
                    continue;
                }

                if (!IsCompletedCandidate(candidate))
                {
                    sawIncomplete = true;
                    continue;
                }
                var text = ExtractFinalText(candidate);
                if (!string.IsNullOrWhiteSpace(text))
                {
                    texts.Add(text!);
                }
            }
        }

        if (texts.Count > 0)
        {
            return texts;
        }

        if (sawMaxTokens)
        {
            throw new GeminiException("GenerateContent: finishReason=MAX_TOKENS (all candidates truncated).");
        }

        if (sawIncomplete)
            throw new GeminiException("GenerateContent: no complete candidates were returned.");
        throw new GeminiException("GenerateContent: missing final text in response parts.");
    }

    private static bool IsCompletedCandidate(GeminiCandidate? candidate)
        => candidate != null && (string.IsNullOrWhiteSpace(candidate.FinishReason)
            || string.Equals(candidate.FinishReason, "STOP", StringComparison.OrdinalIgnoreCase));

    private static string ExtractFinalText(GeminiCandidate? candidate)
        // A response may contain several text parts and optional thought summaries.
        // Only concatenate final-output parts; never use a thought as translated text.
        => candidate?.Content?.Parts == null ? "" : string.Concat(candidate.Content.Parts
            .Where(part => part != null && part.Thought != true).Select(part => part.Text));

    private static int? ComputeCompletionTokens(int? promptTokens, int? totalTokens, int? candidatesTokenCount, int? thoughtsTokenCount)
    {
        if (promptTokens is >= 0 && totalTokens is >= 0 && totalTokens >= promptTokens)
        {
            return totalTokens - promptTokens;
        }

        return candidatesTokenCount is >= 0
            ? candidatesTokenCount + Math.Max(0, thoughtsTokenCount ?? 0)
            : null;
    }
}
