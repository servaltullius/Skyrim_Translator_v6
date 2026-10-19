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
        XTranslatorAi.Core.Translation.ReferenceNameIndex? referenceNames = null,
        IReadOnlyList<XTranslatorAi.Core.Data.SpeakerStyle>? speakerStyles = null
    )
    {
        SpeakerStyles = speakerStyles ?? Array.Empty<XTranslatorAi.Core.Data.SpeakerStyle>();
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

    /// <summary>The speech levels decided per speaker and listener; the speaker check compares a line with them.</summary>
    public IReadOnlyList<XTranslatorAi.Core.Data.SpeakerStyle> SpeakerStyles { get; }
}
