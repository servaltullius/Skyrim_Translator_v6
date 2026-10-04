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
    // Words that end in 인/은 and take the subject particle 이 as a plain noun, so "<word>이?" is a
    // correct short question ("무엇을 원하나, 젊은이?", "그 노인이?"), not a damaged "<word>가?".
    // Compared with the whole word, because the same syllables also end damaged copulas:
    // "이게 전부인이?" (전부인가?) and "장군인이?" (장군인가?) must still be reported.
    private static readonly HashSet<string> NounsTakingQuestionI = new(System.StringComparer.Ordinal)
    {
        "젊은", "늙은", "지은", "엮은",
        "노인", "주인", "집주인", "상인", "부인", "거인", "하인", "죄인", "군인", "범인", "악인",
        "연인", "여인", "은인", "현인", "광인", "타인", "미인", "장인", "성인", "시인", "개인",
        "이방인", "외지인",
    };

    private static readonly (Regex Pattern, string What, System.Func<Match, bool>? IsCorrectText)[] Patterns =
    {
        (new Regex(@"(?:무언|언젠|어딘|누군)이(?![가-힣])", RegexOptions.CultureInvariant), "'~가'로 끝나는 단어가 '~이'로 바뀜", null),
        (new Regex(@"(?<![가-힣])(?<word>[가-힣]*(?:인|은|는|던))이\?", RegexOptions.CultureInvariant), "의문형 '~가?'가 '~이?'로 바뀜",
            m => NounsTakingQuestionI.Contains(m.Groups["word"].Value)),
        // Only after a word that already ended in 가 as a particle or ending (내가, 우리가, 게다가, 돌아가,
        // 누군가가). A noun ending in 가 takes 가 normally: 뭔가가, 대가가, 작가가, 헬가가.
        (new Regex(@"(?:(?<![가-힣])(?:내|네|제|우리|저희|너희|그대|당신|자네|그녀|게다)가|(?<=[가-힣][아어])가|가가)가 (?=[가-힣])",
            RegexOptions.CultureInvariant), "지시어 '이'가 앞 조사에 붙어 '~가가'가 됨", null),
        (new Regex(@"(?:기꺼|가까)가(?![가-힣])", RegexOptions.CultureInvariant), "부사 '~이'가 '~가'로 바뀜", null),
        // A single '.', '!' or '?' only: a word after an ellipsis ("음...알겠어", "음…알겠어") is ordinary
        // hesitant speech, while the old token repair appended terms after a full stop ("…막아냈다.던머노드").
        (new Regex(@"(?:(?<!\.)\.|[!?])[""”’')]?[가-힣]{2,}[ \t]*$", RegexOptions.CultureInvariant | RegexOptions.Multiline),
            "문장 끝 뒤에 용어가 덧붙음", null),
    };

    public static void Apply(LqaScanEntry entry, string sourceText, string destText, bool isKorean, List<LqaIssue> issues)
    {
        if (!isKorean || string.IsNullOrEmpty(destText))
        {
            return;
        }

        foreach (var (pattern, what, isCorrectText) in Patterns)
        {
            var match = pattern.Match(destText);
            while (match.Success && isCorrectText != null && isCorrectText(match))
            {
                match = match.NextMatch();
            }

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
