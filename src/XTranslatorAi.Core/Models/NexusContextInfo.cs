using System;

namespace XTranslatorAi.Core.Models;

public sealed record NexusContextInfo(
    string GameDomain,
    long ModId,
    string? ModUrl,
    string? ModName,
    string? Summary,
    string ContextText,
    DateTimeOffset UpdatedAt
);

