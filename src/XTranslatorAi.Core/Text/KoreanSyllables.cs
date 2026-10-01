namespace XTranslatorAi.Core.Text;

/// <summary>Hangul syllable facts used to choose particles (받침 여부).</summary>
internal static class KoreanSyllables
{
    private const char First = '가'; // 가
    private const char Last = '힣';  // 힣

    public static bool IsHangulSyllable(char c) => c is >= First and <= Last;

    /// <summary>True when a Hangul syllable has a final consonant (받침). False for any other character.</summary>
    public static bool HasFinalConsonant(char c) => IsHangulSyllable(c) && (c - First) % 28 != 0;

    /// <summary>True when the final consonant is ㄹ, which takes "로" rather than "으로".</summary>
    public static bool HasFinalRieul(char c) => IsHangulSyllable(c) && (c - First) % 28 == 8;

    public static bool IsLatinVowel(char c) => char.ToLowerInvariant(c) is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    /// <summary>
    /// Whether the Korean reading of a digit ends in a consonant:
    /// 0 영, 1 일, 3 삼, 6 육, 7 칠, 8 팔 do; 2 이, 4 사, 5 오, 9 구 do not.
    /// </summary>
    public static bool DigitHasFinalConsonant(char digit) => digit is not ('2' or '4' or '5' or '9');
}
