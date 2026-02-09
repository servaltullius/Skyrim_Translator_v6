using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels;

public sealed record GlossaryLookupResultViewModel(
    string Scope,
    string Category,
    string SourceTerm,
    string TargetTerm,
    bool Enabled,
    GlossaryMatchMode MatchMode,
    GlossaryForceMode ForceMode,
    int Priority,
    string Note
);
