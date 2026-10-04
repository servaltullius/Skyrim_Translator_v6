using System;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text.KoreanFix.Internal;

namespace XTranslatorAi.Core.Text.KoreanFix.Internal.Steps;

/// <summary>
/// Fixes the particle after an acronym that is surely read letter by letter: "AP을" → "AP를" (에이피), "MCM를" →
/// "MCM을" (엠시엠). Only acronyms without a vowel letter (NPC, MCM, DLC, HP) or of two letters (AP, UI, ID) are
/// rewritten; a short all-caps word with vowels may be read as a word ("STOP을" 스톱, "PAK을" 팩), so those stay
/// with the quality check's warning, as do mixed-case words ("Rune을", "Nexus를") and roman numerals. The reading
/// is <see cref="KoreanParticleSelector.TryGetAcronymFinalSound"/>, which the quality check uses too.
/// </summary>
internal sealed class AcronymParticleStep : IKoreanFixStep
{
    private static readonly Regex AcronymParticleRegex = new(
        pattern: @"\b(?<word>[A-Za-z][A-Za-z0-9'’\-]*)(?<particle>을|를|은|는|이|가|과|와)(?=$|[\s\p{P}])",
        options: RegexOptions.CultureInvariant,
        matchTimeout: TimeSpan.FromMilliseconds(250)
    );

    public string Apply(KoreanFixContext context, string text)
    {
        if (!HasAsciiUpper(text))
        {
            return text;
        }

        return AcronymParticleRegex.Replace(text, m =>
        {
            var word = m.Groups["word"].Value;
            if (!IsSurelySpelledOut(word) || !KoreanParticleSelector.TryGetAcronymFinalSound(word, out var hasFinal, out _))
            {
                return m.Value;
            }

            var particle = m.Groups["particle"].Value;
            var expected = (particle, hasFinal) switch
            {
                ("을" or "를", true) => "을",
                ("을" or "를", false) => "를",
                ("은" or "는", true) => "은",
                ("은" or "는", false) => "는",
                ("이" or "가", true) => "이",
                ("이" or "가", false) => "가",
                ("과" or "와", true) => "과",
                _ => "와",
            };
            return word + expected;
        });
    }

    private static bool IsSurelySpelledOut(string word)
    {
        var start = word.Length;
        while (start > 0 && word[start - 1] is >= 'A' and <= 'Z')
        {
            start--;
        }

        var letters = word.AsSpan(start);
        return letters.Length == 2 || letters.IndexOfAny("AEIOU") < 0;
    }

    private static bool HasAsciiUpper(string text)
    {
        foreach (var c in text)
        {
            if (c is >= 'A' and <= 'Z')
            {
                return true;
            }
        }

        return false;
    }
}
