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
        string apiKey, string modelName, GeminiGenerateContentRequest request, CancellationToken cancellationToken)
    {
        var result = await GenerateAndLogAsync(apiKey, modelName, request,
            parsed => ExtractCandidateTextsOrThrow(parsed), cancellationToken);
        return result.Value;
    }

    public async Task<string> GenerateContentAsync(
        string apiKey, string modelName, GeminiGenerateContentRequest request, CancellationToken cancellationToken)
        => (await GenerateContentWithUsageAsync(apiKey, modelName, request, cancellationToken)).Text;

    public async Task<GeminiGenerationResult> GenerateContentWithUsageAsync(
        string apiKey, string modelName, GeminiGenerateContentRequest request, CancellationToken cancellationToken)
    {
        var result = await GenerateAndLogAsync(apiKey, modelName, request, ExtractSingleTextOrThrow, cancellationToken);
        var usage = result.Usage;
        return new GeminiGenerationResult(result.Value, usage?.PromptTokenCount,
            ComputeCompletionTokens(usage?.PromptTokenCount, usage?.TotalTokenCount,
                usage?.CandidatesTokenCount, usage?.ThoughtsTokenCount), usage?.CachedContentTokenCount);
    }

    private async Task<(T Value, GeminiUsageMetadata? Usage)> GenerateAndLogAsync<T>(
        string apiKey, string modelName, GeminiGenerateContentRequest request,
        Func<GeminiGenerateContentResponse?, T> selectResult, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        int? statusCode = null;
        GeminiGenerateContentResponse? parsed = null;
        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(modelName)}:generateContent?key={Uri.EscapeDataString(apiKey)}";
            using var response = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, cancellationToken);
            statusCode = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(cancellationToken);
            if (!response.IsSuccessStatusCode)
                throw CreateHttpException("GenerateContent", response, body);

            // Retain usage before validating the translation. Rejected/truncated output
            // can still consume billed tokens; it must be logged once, as a failure.
            parsed = DeserializeOrThrow<GeminiGenerateContentResponse>("GenerateContent", body);
            var value = selectResult(parsed);
            LogGeneration(success: true, error: null);
            return (value, parsed?.UsageMetadata);
        }
        catch (Exception ex)
        {
            statusCode ??= TryGetStatusCode(ex);
            LogGeneration(success: false, error: Truncate(ex.Message, 800));
            throw;
        }

        void LogGeneration(bool success, string? error)
        {
            var usage = parsed?.UsageMetadata;
            var promptTokens = usage?.PromptTokenCount;
            var completionTokens = ComputeCompletionTokens(promptTokens, usage?.TotalTokenCount,
                usage?.CandidatesTokenCount, usage?.ThoughtsTokenCount);
            var reasons = parsed?.Candidates?.Where(c => !string.IsNullOrWhiteSpace(c?.FinishReason))
                .Select(c => c.FinishReason!).Distinct().ToArray();
            LogCall(new GeminiCallLogEntry(
                StartedAt: startedAt, Duration: sw.Elapsed, Operation: GeminiCallOperation.GenerateContent,
                ModelName: modelName, StatusCode: statusCode, Success: success, ErrorMessage: error,
                ApiKeyMask: MaskApiKey(apiKey), PromptTokens: promptTokens, CompletionTokens: completionTokens,
                TotalTokens: usage?.TotalTokenCount ?? (promptTokens is >= 0 && completionTokens is >= 0 ? promptTokens + completionTokens : null),
                CachedContentTokens: usage?.CachedContentTokenCount,
                CostUsd: GeminiUsageCost.TryEstimateUsd(modelName, promptTokens, completionTokens, usage?.CachedContentTokenCount),
                Purpose: request.Purpose, FinishReason: reasons is { Length: > 0 } ? string.Join(", ", reasons) : null,
                OutputTokens: usage?.CandidatesTokenCount, ThoughtsTokens: usage?.ThoughtsTokenCount));
        }
    }

    private static string ExtractSingleTextOrThrow(GeminiGenerateContentResponse? parsed)
    {
        var candidate = parsed?.Candidates?.FirstOrDefault();
        if (string.Equals(candidate?.FinishReason, "MAX_TOKENS", StringComparison.OrdinalIgnoreCase))
            throw new GeminiException("GenerateContent: finishReason=MAX_TOKENS (output truncated).");
        if (!IsCompletedCandidate(candidate))
            throw new GeminiException($"GenerateContent: finishReason={candidate?.FinishReason} (incomplete response).");
        var text = ExtractFinalText(candidate);
        if (string.IsNullOrWhiteSpace(text))
            throw new GeminiException("GenerateContent: missing final text in response parts.");
        return text;
    }

    private static IReadOnlyList<string> ExtractCandidateTextsOrThrow(GeminiGenerateContentResponse? parsed)
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
