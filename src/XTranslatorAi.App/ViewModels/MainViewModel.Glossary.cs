using System;
using System.Collections.Generic;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private static readonly HashSet<string> DefaultGlossaryPromptOnlySources = new(StringComparer.OrdinalIgnoreCase)
    {
        // Very common terms: forcing tokens can increase cost and, when token repair kicks in,
        // may produce odd trailing fragments (e.g., an extra "드래곤" at the end). Prefer prompt-only hints.
        "Block",
        "Dragon",
    };

    private static bool ShouldDefaultGlossaryUsePromptOnly(string sourceTerm)
        => DefaultGlossaryPromptOnlySources.Contains((sourceTerm ?? "").Trim());
}
