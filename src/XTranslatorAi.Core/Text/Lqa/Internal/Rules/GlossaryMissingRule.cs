using System;
using System.Collections.Generic;
using System.Linq;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// A force-token glossary term the translation does not have. A term inside a name the game translation (TM) spells
/// otherwise is the name's: Meridia's Order's "Jester's Boots" as the game's 광대의 신발 was flagged for Boots => 전투화.
/// </summary>
internal static class GlossaryMissingRule
{
    public static void Apply(
        LqaScanEntry entry,
        string sourceText,
        string destText,
        bool isKorean,
        IReadOnlyList<GlossaryEntry> forceTokenGlossary,
        GlossaryApplier forcedTerms,
        ReferenceNameIndex? referenceNames,
        List<LqaIssue> issues
    )
    {
        if (!isKorean || forceTokenGlossary.Count == 0 || !LqaScanner.HasEnglishResidue(sourceText))
        {
            return;
        }

        var missing = LqaHeuristics.FindMissingForceTokenGlossaryTerm(sourceText, destText, forcedTerms);
        if (missing != null && WithoutTranslatedNames(sourceText, destText, referenceNames, forceTokenGlossary) is { } rest)
        {
            missing = LqaHeuristics.FindMissingForceTokenGlossaryTerm(rest, destText, forcedTerms);
        }

        if (missing == null)
        {
            return;
        }

        issues.Add(
            new LqaIssue(
                Id: entry.Id,
                OrderIndex: entry.OrderIndex,
                Edid: entry.Edid,
                Rec: entry.Rec,
                Severity: "Warn",
                Code: "glossary_missing",
                Message: $"용어 누락: {missing.SourceTerm} => {missing.TargetTerm}",
                SourceText: sourceText,
                DestText: destText
            )
        );
    }

    // The source with each TM name whose spelling the translation has blanked out (null when there is none). A name that
    // is a glossary term itself is the glossary's to decide.
    private static string? WithoutTranslatedNames(string sourceText, string destText, ReferenceNameIndex? referenceNames,
        IReadOnlyList<GlossaryEntry> glossary)
    {
        if (referenceNames == null)
        {
            return null;
        }

        var rest = sourceText;
        foreach (var (source, target) in referenceNames.FindIn(sourceText).Where(name => destText.Contains(name.Target, StringComparison.Ordinal)
                     && !glossary.Any(term => string.Equals(term.SourceTerm.Trim(), name.Source.Trim(), StringComparison.OrdinalIgnoreCase))))
        {
            rest = rest.Replace(source, new string(' ', source.Length), StringComparison.Ordinal);
        }

        return ReferenceEquals(rest, sourceText) || rest == sourceText ? null : rest;
    }
}
