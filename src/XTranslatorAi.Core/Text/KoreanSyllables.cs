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

    /// <summary>The final consonant's index (0 none, 8 ㄹ, 17 ㅂ, 18 ㅄ, 20 ㅆ, 27 ㅎ ...), or -1 for a character that is not a syllable.</summary>
    public static int FinalConsonant(char c) => IsHangulSyllable(c) ? (c - First) % 28 : -1;

    /// <summary>The initial consonant's index of a Hangul syllable (11 ㅇ), or -1 for any other character.</summary>
    public static int InitialConsonant(char c) => IsHangulSyllable(c) ? (c - First) / 588 : -1;

    /// <summary>The index of ㅇ, the initial consonant of a syllable that starts with its vowel (오, 아, 으).</summary>
    public const int SilentInitial = 11;

    /// <summary>The vowel's index of a Hangul syllable (8 ㅗ), or -1 for any other character.</summary>
    public static int Vowel(char c) => IsHangulSyllable(c) ? (c - First) % 588 / 28 : -1;

    /// <summary>True when the final consonant is ㅂ, as in the polite endings 합니다, 됩니다, 갑시다.</summary>
    public static bool HasFinalBieup(char c) => IsHangulSyllable(c) && (c - First) % 28 == 17;

    public static bool IsLatinVowel(char c) => char.ToLowerInvariant(c) is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    /// <summary>
    /// Whether the Korean reading of a digit ends in a consonant:
    /// 0 영, 1 일, 3 삼, 6 육, 7 칠, 8 팔 do; 2 이, 4 사, 5 오, 9 구 do not.
    /// </summary>
    public static bool DigitHasFinalConsonant(char digit) => digit is not ('2' or '4' or '5' or '9');
}
