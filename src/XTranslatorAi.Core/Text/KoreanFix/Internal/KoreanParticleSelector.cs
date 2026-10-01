using System;
using static XTranslatorAi.Core.Text.KoreanSyllables;

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

    public static string FixSubjectParticleSafely(string noun, string particle)
    {
        var expected = ChooseSubjectParticle(noun);
        if (string.Equals(particle, expected, StringComparison.Ordinal))
        {
            return particle;
        }

        // Both directions are risky on single-syllable nouns:
        // "가" could be verb 가다, "이" could be copula 이다.
        if (noun.Length < 2)
        {
            return particle;
        }

        return expected;
    }

    public static string FixSubjectParticleSafelyLatin(string noun, string particle)
    {
        var expected = HasFinalConsonantLatin(noun) ? "이" : "가";
        return string.Equals(particle, expected, StringComparison.Ordinal) ? particle : expected;
    }

    // Nouns that end in "을" themselves; in compounds ("시골마을") the regex splits them as noun + "을".
    private static readonly string[] WordsEndingInEul = { "마을", "가을", "고을", "노을" };

    public static string FixObjectParticleSafely(string noun, string particle)
    {
        if (string.Equals(particle, "을", StringComparison.Ordinal))
        {
            foreach (var word in WordsEndingInEul)
            {
                if (noun.EndsWith(word[..1], StringComparison.Ordinal))
                {
                    return particle;
                }
            }
        }

        return FixParticleSafely(noun, particle, ChooseObjectParticle, unsafeParticle: "을", unsafeExpected: "를");
    }

    public static string FixObjectParticleSafelyLatin(string noun, string particle)
        => FixParticleSafely(noun, particle, ChooseObjectParticleLatin, unsafeParticle: "을", unsafeExpected: "를");

    public static string FixTopicParticleSafely(string noun, string particle)
    {
        var expected = ChooseTopicParticle(noun);
        if (string.Equals(particle, expected, StringComparison.Ordinal))
        {
            return particle;
        }

        // "…는" after a consonant is almost always an attributive verb ending
        // ("살아남는", "잡아먹는", "있는"), not a wrong topic particle. Never rewrite it to "은".
        if (string.Equals(particle, "는", StringComparison.Ordinal))
        {
            return particle;
        }

        return FixParticleSafely(noun, particle, ChooseTopicParticle, unsafeParticle: "은", unsafeExpected: "는");
    }

    // Particle pairs as (after a final consonant, after a vowel). Longer forms come first so
    // "으로" is not read as "으" + "로" and "이라" is not read as "이" + "라".
    private static readonly (string Consonant, string Vowel)[] TermParticlePairs =
    {
        ("으로", "로"), ("이라", "라"), ("이나", "나"),
        ("이", "가"), ("은", "는"), ("을", "를"), ("과", "와"),
    };

    // Endings that may follow a particle without a boundary ("로부터", "과의", "이라고").
    private static readonly string[] DirectionalContinuations = { "부터", "서", "써", "의", "는", "도", "만" };
    private static readonly string[] ConjunctionContinuations = { "의", "는", "도" };
    private static readonly string[] CopulaContinuations = { "고", "는", "면", "서" };

    /// <summary>
    /// Chooses the particle that directly follows a term we substituted into the model output.
    /// The model wrote that particle while seeing only a placeholder token, so its choice cannot be trusted,
    /// but the position is certain: unlike free-text fixes, the syllable here is known to follow a noun.
    /// </summary>
    public static bool TryFixParticleAfterTerm(string term, string text, int start, out string particle, out int length)
    {
        particle = "";
        length = 0;
        if (!TryGetFinalSound(term, out var hasFinal, out var finalRieul))
        {
            return false;
        }

        foreach (var (consonantForm, vowelForm) in TermParticlePairs)
        {
            foreach (var written in new[] { consonantForm, vowelForm })
            {
                if (string.CompareOrdinal(text, start, written, 0, written.Length) != 0)
                {
                    continue;
                }

                var end = start + written.Length;
                if (!IsParticleEnd(text, end, ContinuationsFor(consonantForm)))
                {
                    continue;
                }

                var useConsonantForm = consonantForm == "으로" ? hasFinal && !finalRieul : hasFinal;
                particle = useConsonantForm ? consonantForm : vowelForm;
                length = written.Length;
                return true;
            }
        }

        return false;
    }

    private static string[] ContinuationsFor(string consonantForm) => consonantForm switch
    {
        "으로" => DirectionalContinuations,
        "과" => ConjunctionContinuations,
        "이라" => CopulaContinuations,
        _ => Array.Empty<string>(),
    };

    private static bool IsParticleEnd(string text, int end, string[] continuations)
    {
        if (end >= text.Length || !IsHangulSyllable(text[end]))
        {
            return true;
        }

        foreach (var continuation in continuations)
        {
            if (string.CompareOrdinal(text, end, continuation, 0, continuation.Length) == 0
                && (end + continuation.Length >= text.Length || !IsHangulSyllable(text[end + continuation.Length])))
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetFinalSound(string term, out bool hasFinal, out bool finalRieul)
    {
        hasFinal = false;
        finalRieul = false;
        if (string.IsNullOrEmpty(term))
        {
            return false;
        }

        var last = term[^1];
        if (IsHangulSyllable(last))
        {
            hasFinal = HasFinalConsonant(last);
            finalRieul = HasFinalRieul(last);
            return true;
        }

        if (char.IsDigit(last))
        {
            hasFinal = DigitHasFinalConsonant(last);
            finalRieul = last is '1' or '7' or '8';
            return true;
        }

        if (last is >= 'A' and <= 'Z' or >= 'a' and <= 'z')
        {
            var lower = char.ToLowerInvariant(last);
            hasFinal = !IsLatinVowel(lower);
            finalRieul = lower == 'l';
            return true;
        }

        // Terms ending in brackets or punctuation: the right particle depends on how the reader
        // pronounces them, so leave the model's choice alone.
        return false;
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

}
