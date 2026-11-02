using System;

namespace XTranslatorAi.Core.Text;

/// <summary>
/// How a time unit right after a number reads, shared by the template fixer (MagDurPlaceholderFixer) and the answer
/// check (TokenSanitizer): the 1.16.40 fix reached only the fixer, and the check kept sending back "&lt;mag&gt; 주의력".
/// </summary>
internal static class KoreanTimeUnits
{
    /// <summary>
    /// A one-syllable unit followed by 의 stays a unit ("&lt;mag&gt;일의 기간") unless the 의 goes on into a word
    /// ("&lt;mag&gt; 주의력") or makes a fraction ("&lt;mag&gt;분의 1"). <paramref name="afterUnit"/> starts right after the unit.
    /// </summary>
    public static bool OfMakesAWordOrFraction(ReadOnlySpan<char> afterUnit)
        => afterUnit.Length > 0 && afterUnit[0] == '의'
            && ((afterUnit.Length > 1 && KoreanSyllables.IsHangulSyllable(afterUnit[1]))
                || (afterUnit[1..].TrimStart() is [var first, ..] && char.IsDigit(first)));
}
