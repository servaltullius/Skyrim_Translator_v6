using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// An effect or item description that switches from 합니다체 to 해라체 inside one row: "상식을 벗어난 기술입니다.
/// 연속 베기 공격을 빠르게 퍼붓는다." (12 War Ash 3 rows). The field-majority check only reads the last sentence of
/// BOOK/QUST/MESG rows, so descriptions were never compared sentence by sentence. A 합니다체 description that closes a
/// sentence on 가능/불가 is reported too: War Ash 2 wrote "…시작할 수 있습니다. 모든 무기에 사용 가능." in 48 of 50 rows,
/// the description style asks for 합니다체 throughout, and the tone check cannot see it because a noun has no speech
/// level. Labels such as "재사용 대기시간 15초." or "양손 무기 전용." stay allowed.
/// </summary>
internal static class MixedDescriptionToneRule
{
    private static readonly Regex SentenceRegex = new(@"[^.!?\n]+[.!?]?", RegexOptions.CultureInvariant);

    // "사용 가능.", "착용 불가.": a cut-off "사용할 수 있습니다/없습니다".
    private static readonly Regex AbilityNounEnding = new(@"\S+\s+(?:가능|불가능|불가)$", RegexOptions.CultureInvariant);

    public static void Apply(LqaScanEntry entry, string destText, bool isKorean, List<LqaIssue> issues)
    {
        if (!isKorean || !IsDescription(entry.Rec) || string.IsNullOrWhiteSpace(destText))
        {
            return;
        }

        var hamnida = false;
        var plain = false;
        string? abilityNoun = null;
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
                case ToneKind.Unknown:
                    var ending = AbilityNounEnding.Match(m.Value.Trim().TrimEnd('.', '!'));
                    abilityNoun ??= ending.Success ? ending.Value : null;
                    break;
            }
        }

        if (hamnida && plain)
        {
            issues.Add(new LqaIssue(entry.Id, entry.OrderIndex, entry.Edid, entry.Rec, "Warn", "mixed_description_tone",
                "한 설명 안에 합니다체와 해라체(…다)가 섞여 있습니다.", entry.SourceText ?? "", destText));
        }
        else if (hamnida && abilityNoun != null)
        {
            issues.Add(new LqaIssue(entry.Id, entry.OrderIndex, entry.Edid, entry.Rec, "Warn", "mixed_description_tone",
                $"합니다체 설명에 명사로 끝나는 문장('{abilityNoun}.')이 섞여 있습니다. '…할 수 있습니다'처럼 쓰세요.",
                entry.SourceText ?? "", destText));
        }
    }

    private static bool IsDescription(string? rec)
    {
        return TranslationRowRules.FieldType(rec) is "DESC" or "DNAM"
               && TranslationRowRules.RecordType(rec) is not ("BOOK" or "QUST" or "INFO" or "DIAL" or "MESG");
    }
}
