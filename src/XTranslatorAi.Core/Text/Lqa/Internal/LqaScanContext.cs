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
        IReadOnlyDictionary<long, string>? tmFallbackNotes,
        XTranslatorAi.Core.Translation.ReferenceNameIndex? referenceNames = null
    )
    {
        Entries = entries;
        TargetLang = targetLang;
        ForceTokenGlossary = forceTokenGlossary;
        OnProgress = onProgress;
        TmFallbackNotes = tmFallbackNotes;
        ReferenceNames = referenceNames;
    }

    public IReadOnlyList<LqaScanEntry> Entries { get; }

    public string TargetLang { get; }

    public IReadOnlyList<GlossaryEntry> ForceTokenGlossary { get; }

    public Action<int>? OnProgress { get; }

    public IReadOnlyDictionary<long, string>? TmFallbackNotes { get; }

    public XTranslatorAi.Core.Translation.ReferenceNameIndex? ReferenceNames { get; }
}
