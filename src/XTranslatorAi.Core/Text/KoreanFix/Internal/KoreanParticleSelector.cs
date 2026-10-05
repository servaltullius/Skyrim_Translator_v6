using System;
using System.Collections.Generic;
using System.Linq;
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

    // Only for a marker written both ways ("NPC을(를)"). A single particle the model wrote after a Latin
    // word is never rewritten: it chose by pronunciation (NPC는, Rune을), which the spelling cannot overrule.
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

    // Words whose last syllable looks like 을/은 after a vowel but is part of the word:
    // nouns (마을, 수은), ㅅ-irregular verb forms (더 나은, 뒤이은, 죄지은, 관련지을 수) and 모으다
    // (끌어모은 군대, 끌어모을 수). The step regexes split "시골마을" or "뒤이은" as noun + particle,
    // which rewrote 뒤이은 혼란 to 뒤이는 and 끌어모을 수 to 끌어모를.
    private static readonly HashSet<string> VowelThenEulEunWords = new(StringComparer.Ordinal)
    {
        "마을", "가을", "고을", "노을", "나을", "지을", "이을", "부을", "그을", "저을", "모을",
        "수은", "보은", "나은", "지은", "이은", "부은", "그은", "저은", "모은",
    };

    /// <summary>
    /// True when the 을/은 after <paramref name="word"/> ends a word such as 마을, 뒤이은 or 끌어모을
    /// rather than being a particle. The fixer and the quality check share this list so they agree.
    /// </summary>
    public static bool EndsWithEulEunWord(string word, string particle)
        => word.Length > 0 && VowelThenEulEunWords.Contains(word[^1] + particle);

    public static string FixObjectParticleSafely(string noun, string particle)
    {
        if (EndsWithEulEunWord(noun, particle))
        {
            return particle;
        }

        return FixParticleSafely(noun, particle, ChooseObjectParticle, unsafeParticle: "을", unsafeExpected: "를");
    }

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

        if (EndsWithEulEunWord(noun, particle))
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
        if (!TryGetFinalSound(term, out var hasFinal, out var finalRieul, out var certain))
        {
            return false;
        }

        foreach (var (consonantForm, vowelForm) in TermParticlePairs)
        {
            // Written both ways ("을(를)", "(이)가", "을/를"): the model could not see the term behind its token.
            // Fixing only the first form left "히얄마치를(를)".
            foreach (var written in BothForms(consonantForm, vowelForm).Concat(new[] { consonantForm, vowelForm }))
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

                // After a Latin term read as a word, its last letter is only a guess ("Rune" is 룬, so
                // Rune을; "Nexus" is 넥서스, so Nexus를). Keep the single particle the model wrote and
                // resolve only a marker written both ways, which has to become one form or the other.
                if (!certain && (written == consonantForm || written == vowelForm))
                {
                    return false;
                }

                var useConsonantForm = consonantForm == "으로" ? hasFinal && !finalRieul : hasFinal;
                particle = useConsonantForm ? consonantForm : vowelForm;
                length = written.Length;
                return true;
            }
        }

        return false;
    }

    // The copula after a term, as (written after a vowel, needed after a final consonant, what may follow): the model
    // wrote the vowel form behind the token, and MEI got "네가 사빈겠군". 겠 and 였 always continue an ending; 예요 and
    // 다 end the word; 지 ends it or goes on as 지만, 지요. 야 is left alone: after a consonant it may be the vocative
    // (사빈아) as well as the copula (사빈이야).
    private static readonly (string Vowel, string Consonant, string[]? Continuations)[] CopulaForms =
    {
        ("겠", "이겠", null), ("였", "이었", null), ("예요", "이에요", Array.Empty<string>()),
        ("다", "이다", Array.Empty<string>()), ("지", "이지", new[] { "만", "요" }),
    };

    /// <summary>
    /// Adds the copula's 이 after a term that ends in a final consonant ("사빈겠군" → "사빈이겠군"). A term ending in a
    /// vowel, or one whose final sound is only guessed, is left as written.
    /// </summary>
    public static bool TryFixCopulaAfterTerm(string term, string text, int start, out string copula, out int length)
    {
        copula = "";
        length = 0;
        if (!TryGetFinalSound(term, out var hasFinal, out _, out var certain) || !hasFinal || !certain)
        {
            return false;
        }

        foreach (var (vowel, consonant, continuations) in CopulaForms)
        {
            if (string.CompareOrdinal(text, start, vowel, 0, vowel.Length) != 0
                || continuations != null && !IsParticleEnd(text, start + vowel.Length, continuations))
            {
                continue;
            }

            copula = consonant;
            length = vowel.Length;
            return true;
        }

        return false;
    }

    private static IEnumerable<string> BothForms(string consonantForm, string vowelForm)
    {
        foreach (var (first, second) in new[] { (consonantForm, vowelForm), (vowelForm, consonantForm) })
        {
            yield return first + "(" + second + ")";
            yield return "(" + first + ")" + second;
            yield return first + "/" + second;
        }

        // "(으)로", "(이)라", "(이)나": the extra syllable in parentheses.
        if (consonantForm.Length == vowelForm.Length + 1 && consonantForm.EndsWith(vowelForm, StringComparison.Ordinal))
        {
            yield return "(" + consonantForm[0] + ")" + vowelForm;
        }
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

    /// <param name="certain">False when the sound is only guessed from the last Latin letter of a word.</param>
    private static bool TryGetFinalSound(string term, out bool hasFinal, out bool finalRieul, out bool certain)
    {
        hasFinal = false;
        finalRieul = false;
        certain = true;
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

        if (IsAsciiLetter(last))
        {
            if (!TryGetAcronymFinalSound(term, out hasFinal, out finalRieul))
            {
                var lower = char.ToLowerInvariant(last);
                hasFinal = !IsLatinVowel(lower);
                finalRieul = lower == 'l';
                certain = false;
            }

            return true;
        }

        // Terms ending in brackets or punctuation: the right particle depends on how the reader
        // pronounces them, so leave the model's choice alone.
        return false;
    }

    // Longer all-caps words are more often shouted words (WARNING, SKYRIM) than acronyms.
    private const int MaxAcronymLength = 5;

    /// <summary>
    /// Reads the final sound of a word that ends in an all-caps acronym such as NPC, HP or MCM. Korean
    /// readers say an acronym letter by letter, and of the letter names only L, M, N and R end in a
    /// consonant (엘, 엠, 엔, 알): NPC는 (엔피시), HP가 (에이치피), DLC를 (디엘시), MCM을 (엠시엠).
    /// Returns false for any other word, which is read as a word: Rune을 (룬), Nexus를 (넥서스).
    /// </summary>
    public static bool TryGetAcronymFinalSound(string word, out bool hasFinal, out bool finalRieul)
    {
        hasFinal = false;
        finalRieul = false;

        var start = word.Length;
        while (start > 0 && IsAsciiLetter(word[start - 1]))
        {
            start--;
        }

        var letters = word.Length - start;
        if (letters is 0 or > MaxAcronymLength)
        {
            return false;
        }

        var romanNumeral = letters >= 2;
        for (var i = start; i < word.Length; i++)
        {
            if (word[i] is not (>= 'A' and <= 'Z'))
            {
                return false;
            }

            romanNumeral &= word[i] is 'I' or 'V' or 'X';
        }

        // Roman numerals are read as numbers, not letter names: "Septim VII" is 셉팀 칠세.
        if (romanNumeral)
        {
            return false;
        }

        hasFinal = word[^1] is 'L' or 'M' or 'N' or 'R';
        finalRieul = word[^1] is 'L' or 'R';
        return true;
    }

    // Resolves a marker written both ways after a Latin word: by the acronym reading when there is
    // one ("NPC을(를)" -> "NPC를"), otherwise by the last letter, which is right more often than not.
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

            if (IsAsciiLetter(c))
            {
                return TryGetAcronymFinalSound(noun[..(i + 1)], out var hasFinal, out _)
                    ? hasFinal
                    : !IsLatinVowel(c);
            }
        }

        return true;
    }

    private static bool IsAsciiLetter(char c) => c is >= 'A' and <= 'Z' or >= 'a' and <= 'z';

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
