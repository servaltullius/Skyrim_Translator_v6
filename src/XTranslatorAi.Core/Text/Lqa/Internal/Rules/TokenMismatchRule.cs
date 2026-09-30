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
                Message: "원문/번역 보호 요소가 일치하지 않습니다: 태그·토큰(<...>, [pagebreak], __XT_*__), 변수(%s, {name}), 줄바꿈 또는 서식 순서를 확인하세요.",
                SourceText: sourceText,
                DestText: destText
            )
        );
    }
}
