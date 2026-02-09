using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

internal static class TokenMismatchRule
{
    public static void Apply(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        if (!LqaScanner.HasTokenMismatch(sourceText, destText))
        {
            return;
        }

        issues.Add(
            new LqaIssue(
                Id: entry.Id,
                OrderIndex: entry.OrderIndex,
                Edid: entry.Edid,
                Rec: entry.Rec,
                Severity: "Error",
                Code: "token_mismatch",
                Message: "원문/번역 태그·토큰(<...>, [pagebreak], __XT_*__)이 일치하지 않습니다.",
                SourceText: sourceText,
                DestText: destText
            )
        );
    }
}
