using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

internal static class UntranslatedRule
{
    public static bool ApplyAndShouldShortCircuit(
        LqaScanEntry entry,
        string sourceText,
        string destText,
        bool isKorean,
        List<LqaIssue> issues
    )
    {
        if (!isKorean || !LqaHeuristics.IsLikelyUntranslated(sourceText, destText))
        {
            return false;
        }

        issues.Add(
            new LqaIssue(
                Id: entry.Id,
                OrderIndex: entry.OrderIndex,
                Edid: entry.Edid,
                Rec: entry.Rec,
                Severity: "Warn",
                Code: "untranslated",
                Message: "번역문이 원문과 동일합니다. (미번역 가능성)",
                SourceText: sourceText,
                DestText: destText
            )
        );

        return true;
    }
}
