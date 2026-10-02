using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.ProjectContext;

public sealed record ProjectContextRecCount(string Rec, int Count);

/// <param name="Target">The glossary or hand-edited translation of the term, when one exists.</param>
/// <param name="PreviousTranslations">
/// For a term without a target: names containing it with their translation in the linked earlier release,
/// as "source => translation". This is the evidence for the term's established translation.
/// </param>
public sealed record ProjectContextTermInfo(string Source, int Count, string? Target, IReadOnlyList<string>? PreviousTranslations = null);

public sealed record ProjectContextSample(string? Rec, string Text);

public sealed record ProjectContextScanReport(
    string? AddonName,
    string? InputFile,
    string SourceLang,
    string TargetLang,
    int TotalStrings,
    IReadOnlyList<ProjectContextRecCount> TopRec,
    IReadOnlyList<ProjectContextTermInfo> TopTerms,
    IReadOnlyList<ProjectContextSample> Samples,
    string? NexusContext
);

public sealed record ProjectContextScanOptions(
    string? AddonName,
    string? InputFile,
    string SourceLang,
    string TargetLang,
    string? NexusContext
);

