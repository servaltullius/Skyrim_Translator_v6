using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace XTranslatorAi.Core.Translation;

public sealed record GeminiListModelsResponse(
    [property: JsonPropertyName("models")] List<GeminiModel>? Models
);

public sealed record GeminiModel(
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("displayName")] string? DisplayName,
    [property: JsonPropertyName("description")] string? Description,
    [property: JsonPropertyName("inputTokenLimit")] int? InputTokenLimit,
    [property: JsonPropertyName("outputTokenLimit")] int? OutputTokenLimit,
    [property: JsonPropertyName("supportedGenerationMethods")] List<string>? SupportedGenerationMethods
);

