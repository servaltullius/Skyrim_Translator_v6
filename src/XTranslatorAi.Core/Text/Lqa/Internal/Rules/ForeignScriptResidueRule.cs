using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// Japanese kana or Chinese characters in a Korean translation that the source does not have: Serana's
/// "スペルブレイカー를 본 적이 없어서" (Spellbreaker came out in katakana) and glosses such as "범인(凡人)".
/// The English residue check only looks at Latin letters, so these were never reported.
/// </summary>
internal static class ForeignScriptResidueRule
{
    private static readonly Regex ForeignRunRegex = new(
        @"[぀-ヿㇰ-ㇿ㐀-䶿一-鿿豈-﫿]+",
        RegexOptions.CultureInvariant
    );

    public static void Apply(LqaScanEntry entry, string sourceText, string destText, bool isKorean, List<LqaIssue> issues)
    {
        if (!isKorean || string.IsNullOrEmpty(destText))
        {
            return;
        }

        foreach (Match m in ForeignRunRegex.Matches(destText))
        {
            // A Chinese or Japanese source may keep a name or term in its own script on purpose.
            if ((sourceText ?? "").Contains(m.Value, System.StringComparison.Ordinal) || ForeignRunRegex.IsMatch(sourceText ?? ""))
            {
                return;
            }

            issues.Add(new LqaIssue(entry.Id, entry.OrderIndex, entry.Edid, entry.Rec, "Warn", "foreign_script_residue",
                $"원문에 없는 한자·가나가 남아 있습니다: '{m.Value}'", sourceText ?? "", destText));
            return;
        }
    }
}
