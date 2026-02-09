using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

internal static class GlossaryMissingRule
{
    public static void Apply(
        LqaScanEntry entry,
        string sourceText,
        string destText,
        bool isKorean,
        IReadOnlyList<GlossaryEntry> forceTokenGlossary,
        List<LqaIssue> issues
    )
    {
        if (!isKorean || forceTokenGlossary.Count == 0 || !LqaScanner.HasEnglishResidue(sourceText))
        {
            return;
        }

        var missing = LqaHeuristics.FindMissingForceTokenGlossaryTerm(sourceText, destText, forceTokenGlossary);
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
}
