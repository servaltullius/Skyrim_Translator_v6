using System;

namespace XTranslatorAi.Core.Text;

/// <summary>
/// How a time unit right after a number reads, shared by the template fixer (MagDurPlaceholderFixer) and the answer
/// check (TokenSanitizer): the 1.16.40 fix reached only the fixer, and the check kept sending back "&lt;mag&gt; 주의력",
/// while the fixer still took "&lt;mag&gt; 초과", "년도", "간격" as times and missed "일째".
/// </summary>
internal static class KoreanTimeUnits
{
    public static readonly string[] TimeUnitsAfterToken = ["초간", "초", "분", "시간", "일", "주", "개월", "년", "동안", "간"];

    // Syllables that can follow a one-syllable unit and keep it a unit: particles, endings and time words ("초간",
    // "일째", "분의", "초로", "초면", "초인 경우"). Not 과: "<mag> 초과" is exceeding.
    private const string TimeUnitFollowers = "간후뒤의가이을를에씩내도은는만째로와당였쯤면인나";

    // Followers read in full: their first syllable alone also begins a word after the same unit ("<mag>% 일부",
    // "<mag>% 일정 확률", "<mag> 초보", "<mag> 초입"), and the fixer swapped the tags of such lines.
    private static readonly string[] TimeUnitFollowerWords = [
        "동안", "마다", "까지", "부터", "보다", "정도", "입니", "짜리", "라도", "라면", "라는", "밖에", "미만", "일 때", "일 경우",
        "지만", "거나", "처럼", "뿐",
    ];

    /// <summary>
    /// A one-syllable unit followed by 의 stays a unit ("&lt;mag&gt;일의 기간") unless the 의 goes on into a word
    /// ("&lt;mag&gt; 주의력") or makes a fraction ("&lt;mag&gt;분의 1"). <paramref name="afterUnit"/> starts right after the unit.
    /// </summary>
    public static bool OfMakesAWordOrFraction(ReadOnlySpan<char> afterUnit)
        => afterUnit.Length > 0 && afterUnit[0] == '의'
            && ((afterUnit.Length > 1 && KoreanSyllables.IsHangulSyllable(afterUnit[1]))
                || (afterUnit[1..].TrimStart() is [var first, ..] && char.IsDigit(first)));

    // "레벨이 <mag>일 때", "<mag>일 경우", "<mag>일 수 있습니다": 일 is 이다 there, not a day, and the fixer swapped the
    // tags of such lines.
    private static readonly string[] WordsAfterCopulaIl = ["때", "경우", "수", "뿐", "것"];

    private static bool IsCopulaIl(ReadOnlySpan<char> afterUnit)
    {
        if (afterUnit.Length == 0 || afterUnit[0] != ' ') return false;
        var word = afterUnit.TrimStart();
        foreach (var next in WordsAfterCopulaIl)
        {
            if (word.StartsWith(next.AsSpan(), StringComparison.Ordinal)) return true;
        }

        return false;
    }

    /// <summary>
    /// Whether <paramref name="text"/> right after a number token starts with a Korean time unit.
    /// A one-syllable unit that runs on into another syllable is a word ("피해를 &lt;mag&gt; 주고", "&lt;mag&gt; 일격",
    /// "&lt;mag&gt; 분노", "&lt;mag&gt; 초과", "&lt;mag&gt; 년도", "&lt;mag&gt; 간격"), not a unit, unless that syllable
    /// is in <see cref="TimeUnitFollowers"/> or begins one of <see cref="TimeUnitFollowerWords"/>. "주로" is mainly, and
    /// "주면", "주나", "주지만", "주거나", "주라는" are 주다 ("주인" an owner), no weeks; "년차" is a year's count.
    /// Set <paramref name="allowPerUnit"/> to false where "초당", "분당" are rates.
    /// </summary>
    public static bool StartsWithTimeUnit(ReadOnlySpan<char> text, bool allowPerUnit = true)
    {
        foreach (var unit in TimeUnitsAfterToken)
        {
            if (!text.StartsWith(unit.AsSpan(), StringComparison.Ordinal))
            {
                continue;
            }

            if (unit == "일" && IsCopulaIl(text[1..]))
            {
                continue;
            }

            if (unit.Length > 1 || text.Length == unit.Length || !KoreanSyllables.IsHangulSyllable(text[1]))
            {
                return true;
            }

            var next = text[1];
            if (OfMakesAWordOrFraction(text[1..]))
            {
                continue;
            }

            if ((next == '당' && !allowPerUnit)
                || (unit == "주" && next is '는' or '도' or '로' or '면' or '인' or '나' or '지' or '거' or '라')
                || (unit == "년" && next == '도'))
            {
                continue;
            }

            if (TimeUnitFollowers.Contains(next) || (unit == "년" && next == '차'))
            {
                return true;
            }

            foreach (var word in TimeUnitFollowerWords)
            {
                if (text[1..].StartsWith(word.AsSpan(), StringComparison.Ordinal))
                {
                    return true;
                }
            }
        }

        return false;
    }
}
