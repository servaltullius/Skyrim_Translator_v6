using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// An effect or item description that switches from 합니다체 to 해라체 inside one row: "상식을 벗어난 기술입니다.
/// 연속 베기 공격을 빠르게 퍼붓는다." (12 War Ash 3 rows). The field-majority check only reads the last sentence of
/// BOOK/QUST/MESG rows, so descriptions were never compared sentence by sentence.
/// </summary>
internal static class MixedDescriptionToneRule
{
    private static readonly Regex SentenceRegex = new(@"[^.!?\n]+[.!?]?", RegexOptions.CultureInvariant);

    public static void Apply(LqaScanEntry entry, string destText, bool isKorean, List<LqaIssue> issues)
    {
        if (!isKorean || !IsDescription(entry.Rec) || string.IsNullOrWhiteSpace(destText))
        {
            return;
        }

        var hamnida = false;
        var plain = false;
        foreach (Match m in SentenceRegex.Matches(destText))
        {
            switch (LqaToneClassifier.Classify(m.Value))
            {
                case ToneKind.Hamnida:
                    hamnida = true;
                    break;
                case ToneKind.PlainDa:
                    plain = true;
                    break;
            }
        }

        if (hamnida && plain)
        {
            issues.Add(new LqaIssue(entry.Id, entry.OrderIndex, entry.Edid, entry.Rec, "Warn", "mixed_description_tone",
                "한 설명 안에 합니다체와 해라체(…다)가 섞여 있습니다.", entry.SourceText ?? "", destText));
        }
    }

    private static bool IsDescription(string? rec)
    {
        var r = (rec ?? "").Trim().ToUpperInvariant();
        var colon = r.IndexOf(':');
        if (colon < 0)
        {
            return false;
        }

        var (family, subtype) = (r[..colon], r[(colon + 1)..]);
        return subtype is "DESC" or "DNAM" && family is not ("BOOK" or "QUST" or "INFO" or "DIAL" or "MESG");
    }
}
