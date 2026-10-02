using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

internal static class EnglishResidueRule
{
    public static void Apply(LqaScanEntry entry, string sourceText, string destText, bool isKorean, List<LqaIssue> issues)
    {
        var residue = isKorean ? LqaScanner.FindEnglishResidue(destText, sourceText) : null;
        if (residue == null)
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
                Code: "english_residue",
                Message: $"번역되지 않은 영문이 남아 있습니다: '{residue}'",
                SourceText: sourceText,
                DestText: destText
            )
        );
    }
}
