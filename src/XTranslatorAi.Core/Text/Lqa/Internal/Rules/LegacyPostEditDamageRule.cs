using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// Finds damage that builds before the 2026-10 fixes wrote into translations: the old particle
/// fixer rewrote ordinary words (무언가→무언이, "그대가 이 책"→"그대가가 책", 무엇인가?→무엇인이?,
/// 기꺼이→기꺼가) and the old token repair appended missing terms after the last sentence
/// ("…막아냈다.던머노드"). Re-translating those rows with a current build fixes them.
/// The patterns matched 18–22 rows per run of the 2026-09-30 evaluation and none of ~2,100 rows
/// translated after the fixes.
/// </summary>
internal static class LegacyPostEditDamageRule
{
    private static readonly (Regex Pattern, string What)[] Patterns =
    {
        (new Regex(@"(?:무언|언젠|어딘|누군)이(?![가-힣])", RegexOptions.CultureInvariant), "'~가'로 끝나는 단어가 '~이'로 바뀜"),
        (new Regex(@"(?:인|은|는|던)이\?", RegexOptions.CultureInvariant), "의문형 '~가?'가 '~이?'로 바뀜"),
        (new Regex(@"(?<!누군|무언|어딘|언젠)(?<=[가-힣])가가 (?=[가-힣])", RegexOptions.CultureInvariant), "지시어 '이'가 앞 조사에 붙어 '~가가'가 됨"),
        (new Regex(@"(?:기꺼|가까)가(?![가-힣])", RegexOptions.CultureInvariant), "부사 '~이'가 '~가'로 바뀜"),
        (new Regex(@"[.!?…][""”’')]?[가-힣]{2,}[ \t]*$", RegexOptions.CultureInvariant | RegexOptions.Multiline), "문장 끝 뒤에 용어가 덧붙음"),
    };

    public static void Apply(LqaScanEntry entry, string sourceText, string destText, bool isKorean, List<LqaIssue> issues)
    {
        if (!isKorean || string.IsNullOrEmpty(destText))
        {
            return;
        }

        foreach (var (pattern, what) in Patterns)
        {
            var match = pattern.Match(destText);
            if (!match.Success)
            {
                continue;
            }

            var start = System.Math.Max(0, match.Index - 8);
            var end = System.Math.Min(destText.Length, match.Index + match.Length + 4);
            var excerpt = destText[start..end].Replace("\r", "").Replace('\n', ' ').Trim();
            issues.Add(new LqaIssue(
                Id: entry.Id,
                OrderIndex: entry.OrderIndex,
                Edid: entry.Edid,
                Rec: entry.Rec,
                Severity: "Warn",
                Code: "legacy_postedit_damage",
                Message: $"이전 버전의 자동 교정으로 손상됐을 수 있습니다({what}): '{excerpt}'. 다시 번역하거나 직접 고치세요.",
                SourceText: sourceText,
                DestText: destText
            ));
            return;
        }
    }
}
