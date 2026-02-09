using System;

namespace XTranslatorAi.Core.Models;

public sealed record StringEntry(
    long Id,
    int OrderIndex,
    string? ListAttr,
    string? PartialAttr,
    string? AttributesJson,
    string? Edid,
    string? Rec,
    string SourceText,
    string DestText,
    StringEntryStatus Status,
    string? ErrorMessage,
    DateTimeOffset UpdatedAt
);

