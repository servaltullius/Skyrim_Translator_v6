using System.Text.Json.Serialization;

namespace XTranslatorAi.Core.NexusMods;

public sealed record NexusMod(
    [property: JsonPropertyName("mod_id")] long ModId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("author")] string? Author,
    [property: JsonPropertyName("uploaded_by")] string? UploadedBy,
    [property: JsonPropertyName("domain_name")] string? DomainName,
    [property: JsonPropertyName("category_id")] long? CategoryId,
    [property: JsonPropertyName("contains_adult_content")] bool? ContainsAdultContent
);

public sealed record NexusModSearchResult(
    [property: JsonPropertyName("mod_id")] long ModId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("summary")] string? Summary,
    [property: JsonPropertyName("version")] string? Version,
    [property: JsonPropertyName("domain_name")] string? DomainName
);

