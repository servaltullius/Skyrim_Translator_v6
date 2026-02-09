using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

internal static class EnglishResidueRule
{
    public static void Apply(LqaScanEntry entry, string sourceText, string destText, bool isKorean, List<LqaIssue> issues)
    {
        if (!isKorean || !LqaScanner.HasEnglishResidue(destText))
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
                Message: "번역문에 영문이 남아있을 수 있습니다.",
                SourceText: sourceText,
                DestText: destText
            )
        );
    }
}
