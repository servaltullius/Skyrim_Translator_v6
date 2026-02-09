using System;

namespace XTranslatorAi.Core.Models;

public sealed record ProjectInfo(
    long Id,
    string InputXmlPath,
    string? AddonName,
    BethesdaFranchise? Franchise,
    string SourceLang,
    string DestLang,
    string XmlVersion,
    bool XmlHasBom,
    string XmlPrologLine,
    string ModelName,
    string BasePromptText,
    string? CustomPromptText,
    bool UseCustomPrompt,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt
);
