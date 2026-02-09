using System.Text.Json.Serialization;

namespace XTranslatorAi.Core.Translation;

public sealed record TranslationItem(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("text")] string Text,
    [property: JsonPropertyName("rec")] string? Rec = null,
    [property: JsonPropertyName("ctx")] string? Ctx = null
);

public sealed record RepairTranslationItem(
    [property: JsonPropertyName("id")] long Id,
    [property: JsonPropertyName("source")] string Source,
    [property: JsonPropertyName("current")] string Current,
    [property: JsonPropertyName("rec")] string? Rec = null
);
