using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.ProjectContext;

public sealed record ProjectContextRecCount(string Rec, int Count);

public sealed record ProjectContextTermInfo(string Source, int Count, string? Target);

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

