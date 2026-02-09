using System;
using System.Collections.Generic;
using System.Diagnostics;
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
            var completionTokens = ComputeCompletionTokens(promptTokens, totalTokens, result.Usage?.CandidatesTokenCount);
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
            var completionTokens = ComputeCompletionTokens(promptTokens, totalTokens, result.Usage?.CandidatesTokenCount);
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
            return result.Text;
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

        var candidate = parsed?.Candidates?[0];
        if (!string.IsNullOrWhiteSpace(candidate?.FinishReason)
            && string.Equals(candidate!.FinishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase))
        {
            throw new GeminiException("GenerateContent: finishReason=MAX_TOKENS (output truncated).");
        }

        var text = candidate?.Content?.Parts?[0]?.Text;
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new GeminiException($"GenerateContent: missing candidates[0].content.parts[0].text. {Truncate(body)}");
        }

        return (text!, statusCode, parsed?.UsageMetadata);
    }

    private static IReadOnlyList<string> ExtractCandidateTextsOrThrow(GeminiGenerateContentResponse? parsed, string body)
    {
        var texts = new List<string>();
        var sawMaxTokens = false;

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

                var text = candidate.Content?.Parts?[0]?.Text;
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

        throw new GeminiException($"GenerateContent: missing candidates[i].content.parts[0].text. {Truncate(body)}");
    }

    private static int? ComputeCompletionTokens(int? promptTokens, int? totalTokens, int? candidatesTokenCount)
    {
        if (promptTokens is >= 0 && totalTokens is >= 0 && totalTokens >= promptTokens)
        {
            return totalTokens - promptTokens;
        }

        return candidatesTokenCount;
    }
}
