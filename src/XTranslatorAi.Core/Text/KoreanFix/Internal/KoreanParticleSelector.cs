using System;

namespace XTranslatorAi.Core.Text.KoreanFix.Internal;

internal static class KoreanParticleSelector
{
    public static string ChooseSubjectParticle(string noun)
    {
        if (string.IsNullOrEmpty(noun))
        {
            return "가";
        }

        var last = noun[^1];
        if (!IsHangulSyllable(last))
        {
            return "가";
        }

        return HasFinalConsonant(last) ? "이" : "가";
    }

    public static string ChooseObjectParticle(string noun)
    {
        if (string.IsNullOrEmpty(noun))
        {
            return "를";
        }

        var last = noun[^1];
        if (!IsHangulSyllable(last))
        {
            return "를";
        }

        return HasFinalConsonant(last) ? "을" : "를";
    }

    public static string ChooseObjectParticleLatin(string noun)
        => HasFinalConsonantLatin(noun) ? "을" : "를";

    public static string ChooseTopicParticleLatin(string noun)
        => HasFinalConsonantLatin(noun) ? "은" : "는";

    public static string ChooseConjunctionParticle(string noun)
    {
        if (string.IsNullOrEmpty(noun))
        {
            return "와";
        }

        var last = noun[^1];
        if (!IsHangulSyllable(last))
        {
            return "와";
        }

        return HasFinalConsonant(last) ? "과" : "와";
    }

    public static string ChooseDirectionalParticle(string noun)
    {
        if (string.IsNullOrEmpty(noun))
        {
            return "로";
        }

        var last = noun[^1];
        if (!IsHangulSyllable(last))
        {
            return "로";
        }

        if (!HasFinalConsonant(last) || HasFinalRieul(last))
        {
            return "로";
        }

        return "으로";
    }

    public static string ChooseTopicParticle(string noun)
    {
        if (string.IsNullOrEmpty(noun))
        {
            return "는";
        }

        var last = noun[^1];
        if (!IsHangulSyllable(last))
        {
            return "는";
        }

        return HasFinalConsonant(last) ? "은" : "는";
    }

    public static string FixObjectParticleSafely(string noun, string particle)
        => FixParticleSafely(noun, particle, ChooseObjectParticle, unsafeParticle: "을", unsafeExpected: "를");

    public static string FixObjectParticleSafelyLatin(string noun, string particle)
        => FixParticleSafely(noun, particle, ChooseObjectParticleLatin, unsafeParticle: "을", unsafeExpected: "를");

    public static string FixTopicParticleSafely(string noun, string particle)
    {
        var expected = ChooseTopicParticle(noun);
        if (string.Equals(particle, expected, StringComparison.Ordinal))
        {
            return particle;
        }

        // "…는" can be an attributive verb ending (e.g., "있는/없는/떠있는") rather than a topic particle.
        // Be conservative: avoid rewriting "는" -> "은" in these ambiguous verb-like cases to reduce false positives.
        if (string.Equals(particle, "는", StringComparison.Ordinal) && string.Equals(expected, "은", StringComparison.Ordinal))
        {
            if (noun.Length < 2
                || noun.EndsWith("있", StringComparison.Ordinal)
                || noun.EndsWith("없", StringComparison.Ordinal))
            {
                return particle;
            }
        }

        return FixParticleSafely(noun, particle, ChooseTopicParticle, unsafeParticle: "은", unsafeExpected: "는");
    }

    public static string FixTopicParticleSafelyLatin(string noun, string particle)
        => FixParticleSafely(noun, particle, ChooseTopicParticleLatin, unsafeParticle: "은", unsafeExpected: "는");

    private static bool HasFinalConsonantLatin(string noun)
    {
        if (string.IsNullOrWhiteSpace(noun))
        {
            return true;
        }

        for (var i = noun.Length - 1; i >= 0; i--)
        {
            var c = noun[i];
            if (char.IsDigit(c))
            {
                return DigitHasFinalConsonant(c);
            }

            if (c is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
            {
                var lower = char.ToLowerInvariant(c);
                return !IsLatinVowel(lower);
            }
        }

        return true;
    }

    private static bool IsLatinVowel(char c) => c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';

    private static bool DigitHasFinalConsonant(char digit)
    {
        // Korean digit readings:
        // 0=영(ㅇ), 1=일(ㄹ), 2=이(없음), 3=삼(ㅁ), 4=사(없음),
        // 5=오(없음), 6=육(ㄱ), 7=칠(ㄹ), 8=팔(ㄹ), 9=구(없음)
        return digit switch
        {
            '0' => true,
            '1' => true,
            '2' => false,
            '3' => true,
            '4' => false,
            '5' => false,
            '6' => true,
            '7' => true,
            '8' => true,
            '9' => false,
            _ => true,
        };
    }

    private static string FixParticleSafely(
        string noun,
        string particle,
        Func<string, string> expectedSelector,
        string unsafeParticle,
        string unsafeExpected
    )
    {
        var expected = expectedSelector(noun);
        if (string.Equals(particle, expected, StringComparison.Ordinal))
        {
            return particle;
        }

        // Conservative guard:
        // single-syllable + (을->를, 은->는) can be a real word ending with that syllable (e.g., "가을", "가은").
        if (noun.Length < 2
            && string.Equals(particle, unsafeParticle, StringComparison.Ordinal)
            && string.Equals(expected, unsafeExpected, StringComparison.Ordinal))
        {
            return particle;
        }

        return expected;
    }

    private static bool IsHangulSyllable(char c) => c >= 0xAC00 && c <= 0xD7A3;

    private static bool HasFinalConsonant(char hangulSyllable)
    {
        var index = hangulSyllable - 0xAC00;
        var jong = index % 28;
        return jong != 0;
    }

    private static bool HasFinalRieul(char hangulSyllable)
    {
        var index = hangulSyllable - 0xAC00;
        var jong = index % 28;
        return jong == 8;
    }
}
