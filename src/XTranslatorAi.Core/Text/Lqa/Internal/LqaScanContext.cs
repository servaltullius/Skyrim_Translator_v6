using System;
using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.Lqa.Internal;

internal sealed class LqaScanContext
{
    public LqaScanContext(
        IReadOnlyList<LqaScanEntry> entries,
        string targetLang,
        IReadOnlyList<GlossaryEntry> forceTokenGlossary,
        Action<int>? onProgress,
        IReadOnlyDictionary<long, string>? tmFallbackNotes
    )
    {
        Entries = entries;
        TargetLang = targetLang;
        ForceTokenGlossary = forceTokenGlossary;
        OnProgress = onProgress;
        TmFallbackNotes = tmFallbackNotes;
    }

    public IReadOnlyList<LqaScanEntry> Entries { get; }

    public string TargetLang { get; }

    public IReadOnlyList<GlossaryEntry> ForceTokenGlossary { get; }

    public Action<int>? OnProgress { get; }

    public IReadOnlyDictionary<long, string>? TmFallbackNotes { get; }
}
