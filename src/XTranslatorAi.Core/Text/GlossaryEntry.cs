namespace XTranslatorAi.Core.Text;

public sealed record GlossaryEntry(
    long Id,
    string? Category,
    string SourceTerm,
    string TargetTerm,
    bool Enabled,
    GlossaryMatchMode MatchMode,
    GlossaryForceMode ForceMode,
    int Priority,
    string? Note
);
