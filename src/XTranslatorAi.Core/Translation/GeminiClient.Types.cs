using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace XTranslatorAi.Core.Translation;

public class GeminiException : Exception
{
    public GeminiException(string message, Exception? inner = null) : base(message, inner) { }
}

public sealed class GeminiHttpException : GeminiException
{
    public GeminiHttpException(
        string operation,
        int statusCode,
        string? reasonPhrase,
        TimeSpan? retryAfter,
        string message,
        Exception? inner = null
    ) : base(message, inner)
    {
        Operation = operation;
        StatusCode = statusCode;
        ReasonPhrase = reasonPhrase;
        RetryAfter = retryAfter;
    }

    public string Operation { get; }
    public int StatusCode { get; }
    public string? ReasonPhrase { get; }
    public TimeSpan? RetryAfter { get; }
}

public enum GeminiCallOperation
{
    ListModels,
    CreateCachedContent,
    DeleteCachedContent,
    GenerateContent,
    CountTokens,
}

public sealed record GeminiCallLogEntry(
    DateTimeOffset StartedAt,
    TimeSpan Duration,
    GeminiCallOperation Operation,
    string? ModelName,
    int? StatusCode,
    bool Success,
    string? ErrorMessage,
    string? ApiKeyMask = null,
    int? PromptTokens = null,
    int? CompletionTokens = null,
    int? TotalTokens = null,
    int? CachedContentTokens = null,
    double? CostUsd = null
);

public interface IGeminiCallLogger
{
    void Log(GeminiCallLogEntry entry);
}

public sealed record GeminiGenerateContentRequest(
    [property: JsonPropertyName("contents")] List<GeminiContent> Contents,
    [property: JsonPropertyName("cachedContent")] string? CachedContent,
    [property: JsonPropertyName("systemInstruction")] GeminiContent? SystemInstruction,
    [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig? GenerationConfig,
    [property: JsonPropertyName("safetySettings")] List<GeminiSafetySetting>? SafetySettings
);

public sealed record GeminiContent(
    [property: JsonPropertyName("role")] string? Role,
    [property: JsonPropertyName("parts")] List<GeminiPart> Parts
);

public sealed record GeminiPart(
    [property: JsonPropertyName("text")] string Text
);

public sealed record GeminiGenerationConfig(
    [property: JsonPropertyName("temperature")] double? Temperature,
    [property: JsonPropertyName("maxOutputTokens")] int? MaxOutputTokens,
    [property: JsonPropertyName("responseMimeType")] string? ResponseMimeType,
    [property: JsonPropertyName("responseSchema")] JsonElement? ResponseSchema,
    [property: JsonPropertyName("thinkingConfig")] GeminiThinkingConfig? ThinkingConfig,
    [property: JsonPropertyName("candidateCount")] int? CandidateCount = null
);

public sealed record GeminiThinkingConfig(
    [property: JsonPropertyName("thinkingBudget")] int? ThinkingBudget,
    [property: JsonPropertyName("thinkingLevel")] string? ThinkingLevel = null
);

public sealed record GeminiSafetySetting(
    [property: JsonPropertyName("category")] string Category,
    [property: JsonPropertyName("threshold")] string Threshold
);

public sealed record GeminiGenerateContentResponse(
    [property: JsonPropertyName("candidates")] List<GeminiCandidate>? Candidates,
    [property: JsonPropertyName("usageMetadata")] GeminiUsageMetadata? UsageMetadata = null
);

public sealed record GeminiUsageMetadata(
    [property: JsonPropertyName("promptTokenCount")] int? PromptTokenCount,
    [property: JsonPropertyName("candidatesTokenCount")] int? CandidatesTokenCount,
    [property: JsonPropertyName("totalTokenCount")] int? TotalTokenCount,
    [property: JsonPropertyName("cachedContentTokenCount")] int? CachedContentTokenCount,
    // Vertex/Gemini sometimes reports additional breakdown fields; keep optional for forward-compat.
    [property: JsonPropertyName("thoughtsTokenCount")] int? ThoughtsTokenCount = null,
    [property: JsonPropertyName("toolUsePromptTokenCount")] int? ToolUsePromptTokenCount = null
);

public sealed record GeminiCandidate(
    [property: JsonPropertyName("content")] GeminiContent? Content,
    [property: JsonPropertyName("finishReason")] string? FinishReason
);

public sealed record GeminiCountTokensRequest(
    [property: JsonPropertyName("contents")] List<GeminiContent> Contents
);

public sealed record GeminiCountTokensResponse(
    [property: JsonPropertyName("totalTokens")] int? TotalTokens
);

public sealed record GeminiCreateCachedContentRequest(
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("contents")] List<GeminiContent>? Contents,
    [property: JsonPropertyName("systemInstruction")] GeminiContent? SystemInstruction,
    [property: JsonPropertyName("ttl")] string? Ttl,
    [property: JsonPropertyName("displayName")] string? DisplayName
);

public sealed record GeminiCachedContent(
    [property: JsonPropertyName("name")] string? Name
);
