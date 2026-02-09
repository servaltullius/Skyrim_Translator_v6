using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

internal static class BracketMismatchRule
{
    public static void Apply(LqaScanEntry entry, string sourceText, string destText, List<LqaIssue> issues)
    {
        if (!LqaScanner.HasBracketMismatch(destText))
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
                Code: "bracket_mismatch",
                Message: "괄호/대괄호의 짝이 맞지 않을 수 있습니다.",
                SourceText: sourceText,
                DestText: destText
            )
        );
    }
}
