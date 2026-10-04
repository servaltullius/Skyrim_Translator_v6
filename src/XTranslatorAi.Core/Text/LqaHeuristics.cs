using System;
using System.Collections.Generic;
using System.Text;
using System.Linq;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text.KoreanFix.Internal;
using static XTranslatorAi.Core.Text.KoreanSyllables;

namespace XTranslatorAi.Core.Text;

public static class LqaHeuristics
{
    private static readonly Regex UiTagTokenRegex = TranslationConstants.UiTagTokenRegex;

    // Only pairs that are wrong after any noun. "야를을" (Jarl + 을), "지팡이가", "성과와" and
    // "그대가 이 책" are correct, so 를을/이가/과와/가이 and spaced pairs are checked after known terms only.
    private static readonly Regex DoubledParticleRegex = new(
        pattern: @"을를|은는|와과",
        options: RegexOptions.CultureInvariant
    );

    private static readonly string[] DoubledParticlePairs = { "을를", "를을", "은는", "는은", "이가", "가이", "과와", "와과" };

    private static readonly Regex HangulParticleRegex = new(
        pattern: @"(?<word>[가-힣]{1,20})(?<particle>을|를|은|와)(?=$|[\s\p{P}])",
        options: RegexOptions.CultureInvariant
    );

    // A Hangul word closed by a quote or bracket: the particle after it certainly belongs to that word.
    // A closing parenthesis is left out ("도끼(양손)를" takes the particle of the word before the parenthesis).
    private static readonly Regex QuotedWordEndRegex = new(
        pattern: @"(?<word>[가-힣]+)(?<close>['""’”\]」』])",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex RomanParticleRegex = new(
        pattern: @"\b(?<word>[A-Za-z][A-Za-z0-9'’\-]*)(?<particle>을|를|은|는|이|가|과|와)(?=$|[\s\p{P}])",
        options: RegexOptions.CultureInvariant
    );

    private static readonly string[] UnresolvedParticleMarkers =
    {
        "을(를)", "를(을)", "은(는)", "는(은)", "이(가)", "가(이)", "과(와)", "와(과)", "으로(로)", "로(으로)",
        "을/를", "를/을", "은/는", "는/은", "이/가", "가/이", "과/와", "와/과", "으로/로", "로/으로",
    };

    // The second word must end there, alone or with a particle ("효과 효과가", "<dur>초 초 동안").
    // Without that, "<dur>초 초과하면", "60초 초과입니다" and "효과 효과적으로" were reported, and
    // quality escalation paid for a re-translation of a correct row.
    private static readonly Regex DuplicationArtifactRegex = new(
        pattern: @"(?:효과\s+효과|초\s+초)(?=$|[^가-힣]|(?:가|는|를|와|로|의|에|에서|도|만)(?![가-힣]))",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex PercentArtifactRegex = new(
        pattern: @"(?:<\s*\d+\s*>|\d+)\s*%\s*포인트|[가-힣]{2,}%",
        options: RegexOptions.CultureInvariant
    );

    public static bool IsLikelyUntranslated(string sourceText, string destText)
    {
        var src = NormalizeComparableText(sourceText);
        var dst = NormalizeComparableText(destText);

        if (src.Length < 6)
        {
            return false;
        }

        if (!ContainsAsciiLetter(src) || IsInternalIdentifier(sourceText.Trim()))
        {
            return false;
        }

        return string.Equals(src, dst, StringComparison.Ordinal);
    }

    // Hidden topic and effect names such as "SDA_CellTrackMGEFTG" are meant to stay as they are.
    private static bool IsInternalIdentifier(string text)
        => text.Length > 0 && !text.Any(char.IsWhiteSpace)
           && (text.Contains('_') || text.Zip(text.Skip(1)).Any(pair => char.IsLower(pair.First) && char.IsUpper(pair.Second)));

    /// <param name="terms">Glossary target terms; right after these the syllables are certainly particles.</param>
    public static string? FindDoubledParticleExample(string destText, IReadOnlyList<string>? terms = null)
    {
        if (string.IsNullOrWhiteSpace(destText))
        {
            return null;
        }

        var m = DoubledParticleRegex.Match(destText);
        if (m.Success)
        {
            return m.Value;
        }

        foreach (var (term, end) in EnumerateTermEnds(destText, terms))
        {
            foreach (var pair in DoubledParticlePairs)
            {
                if (string.CompareOrdinal(destText, end, pair, 0, pair.Length) == 0
                    && (end + pair.Length >= destText.Length || !IsHangulSyllable(destText[end + pair.Length])))
                {
                    return term + pair;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// A syllable after a word is usually not a particle: 효과, 증가, 전문가, 아이, 기꺼이 end in 과/가/이,
    /// and 있는/받는 are verbs. So after an unknown word only the pairs that are rarely part of a word
    /// are checked (받침 + 를/와, no 받침 + 을/은 outside a few words); after a glossary term,
    /// where the next syllable is certainly a particle, every particle is checked.
    /// </summary>
    /// <param name="terms">Glossary target terms, longest first.</param>
    public static string? FindHangulParticleMismatchSuggestion(string destText, IReadOnlyList<string>? terms = null)
    {
        if (string.IsNullOrWhiteSpace(destText))
        {
            return null;
        }

        foreach (var (term, end) in EnumerateTermEnds(destText, terms))
        {
            if (KoreanParticleSelector.TryFixParticleAfterTerm(term, destText, end, out var expected, out var length)
                && destText.Substring(end, length) is var written
                && written != expected)
            {
                return $"{term}{written} → {term}{expected}";
            }
        }

        foreach (Match m in QuotedWordEndRegex.Matches(destText))
        {
            var word = m.Groups["word"].Value;
            var end = m.Index + m.Length;
            if (KoreanParticleSelector.TryFixParticleAfterTerm(word, destText, end, out var expected, out var length)
                && destText.Substring(end, length) is var written
                && written != expected
                // Direct quotation takes 라고 whatever the final sound ("가자"라고, "안녕"이라고 for naming).
                && !written.EndsWith("라", StringComparison.Ordinal))
            {
                var close = m.Groups["close"].Value;
                return $"{word}{close}{written} → {word}{close}{expected}";
            }
        }

        foreach (Match m in HangulParticleRegex.Matches(destText))
        {
            var word = m.Groups["word"].Value;
            var particle = m.Groups["particle"].Value;
            if (word.Length == 0 || KoreanParticleSelector.EndsWithEulEunWord(word, particle))
            {
                continue;
            }

            var expected = GetExpectedParticleForHangul(particle, HasFinalConsonant(word[^1]));
            if (expected == null)
            {
                continue;
            }

            return $"{word}{particle} → {word}{expected}";
        }

        return null;
    }

    /// <summary>Positions right after each Hangul-final term that starts a word.</summary>
    private static IEnumerable<(string Term, int End)> EnumerateTermEnds(string text, IReadOnlyList<string>? terms)
    {
        if (terms == null)
        {
            yield break;
        }

        foreach (var term in terms)
        {
            var idx = 0;
            while ((idx = text.IndexOf(term, idx, StringComparison.Ordinal)) >= 0)
            {
                var end = idx + term.Length;
                if (idx == 0 || !IsHangulSyllable(text[idx - 1]))
                {
                    yield return (term, end);
                }

                idx = end;
            }
        }
    }

    /// <summary>
    /// Target terms worth checking particles after: Hangul-final (a Latin or digit ending is read
    /// differently from how it is spelled, e.g. NPC → 엔피시) and at least two syllables.
    /// </summary>
    public static IReadOnlyList<string> BuildParticleCheckTerms(IReadOnlyList<GlossaryEntry> glossary)
        => glossary
            .Select(e => (e.TargetTerm ?? "").Trim())
            .Where(t => t.Length >= 2 && IsHangulSyllable(t[^1]))
            .Distinct(StringComparer.Ordinal)
            .OrderByDescending(t => t.Length)
            .ToList();

    /// <summary>
    /// A particle after a Latin word depends on how the word is read, which its spelling does not tell:
    /// "Rune Stone을" (스톤) and "Nexus를" (넥서스) are correct although Stone ends in e and Nexus in s.
    /// Only an all-caps acronym has a certain reading, letter by letter (NPC를, MCM을), so only those are checked.
    /// </summary>
    public static string? FindRomanParticleMismatchSuggestion(string destText)
    {
        if (string.IsNullOrWhiteSpace(destText))
        {
            return null;
        }

        foreach (Match m in RomanParticleRegex.Matches(destText))
        {
            var word = m.Groups["word"].Value;
            var particle = m.Groups["particle"].Value;
            if (!KoreanParticleSelector.TryGetAcronymFinalSound(word, out var hasFinal, out _))
            {
                continue;
            }

            var expected = GetExpectedParticleForHangul(particle, hasFinal);
            if (expected == null)
            {
                continue;
            }

            return $"{word}{particle} → {word}{expected}";
        }

        return null;
    }

    public static bool HasUnresolvedParticleMarkers(string destText)
    {
        if (string.IsNullOrWhiteSpace(destText))
        {
            return false;
        }

        foreach (var marker in UnresolvedParticleMarkers)
        {
            for (var at = destText.IndexOf(marker, StringComparison.Ordinal); at >= 0; at = destText.IndexOf(marker, at + 1, StringComparison.Ordinal))
            {
                if (!IsAfterRuntimeNumber(destText, at))
                {
                    return true;
                }
            }
        }

        return false;
    }

    // A value the game fills in (%d, %.0f, <mag>, <dur>, <25>): its last digit, and so the particle, is unknown, and
    // Elden Rim's "기가 %.0f/%.0f(으)로 상승했습니다" writes it both ways on purpose.
    private static readonly Regex RuntimeNumberAtEndRegex = new(
        pattern: @"(?:%(?:[0-9]+\$)?[-+0-9.]*[dfFiueEgGxX]|<\s*(?:mag|dur|bur|[0-9.]+%?)\s*>)\s*$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    internal static bool IsAfterRuntimeNumber(string text, int index)
        => index > 0 && RuntimeNumberAtEndRegex.IsMatch(text.AsSpan(Math.Max(0, index - 24), index - Math.Max(0, index - 24)).ToString());

    public static string? FindDuplicationArtifactExample(string destText)
    {
        if (string.IsNullOrWhiteSpace(destText))
        {
            return null;
        }

        var m = DuplicationArtifactRegex.Match(destText);
        if (!m.Success || string.IsNullOrWhiteSpace(m.Value))
        {
            return null;
        }

        return Regex.Replace(m.Value, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    public static string? FindPercentArtifactExample(string destText)
    {
        if (string.IsNullOrWhiteSpace(destText))
        {
            return null;
        }

        var m = PercentArtifactRegex.Match(destText);
        if (!m.Success || string.IsNullOrWhiteSpace(m.Value))
        {
            return null;
        }

        return Regex.Replace(m.Value, @"\s+", " ", RegexOptions.CultureInvariant).Trim();
    }

    public static GlossaryEntry? FindMissingForceTokenGlossaryTerm(
        string sourceText,
        string destText,
        IReadOnlyList<GlossaryEntry> glossaryEntries
    )
    {
        if (glossaryEntries == null || glossaryEntries.Count == 0)
        {
            return null;
        }

        var src = StripUiTokens(sourceText);
        if (string.IsNullOrWhiteSpace(src))
        {
            return null;
        }

        var dst = StripUiTokens(destText);

        // Mirror GlossaryApplier: entries arrive in its order (priority, then longer terms first), and
        // text an earlier entry replaced is not matched again ("Elder Scroll" before "Scroll").
        foreach (var entry in glossaryEntries)
        {
            if (!entry.Enabled)
            {
                continue;
            }

            if (entry.ForceMode != GlossaryForceMode.ForceToken)
            {
                continue;
            }

            var sourceTerm = (entry.SourceTerm ?? "").Trim();
            var targetTerm = (entry.TargetTerm ?? "").Trim();
            if (string.IsNullOrWhiteSpace(sourceTerm) || string.IsNullOrWhiteSpace(targetTerm))
            {
                continue;
            }

            if (entry.MatchMode == GlossaryMatchMode.Regex)
            {
                // Regex match can be powerful but is also easy to over-match.
                // Skip for LQA heuristics to keep false positives low.
                continue;
            }

            var spans = FindSourceTermSpans(src, sourceTerm, entry);
            if (spans.Count == 0)
            {
                continue;
            }

            if (!ContainsIgnoreCase(dst, targetTerm))
            {
                return entry;
            }

            var masked = src.ToCharArray();
            foreach (var (start, length) in spans)
            {
                Array.Fill(masked, '\u0001', start, length);
            }

            src = new string(masked);
        }

        return null;
    }

    private static string? GetExpectedParticleForHangul(string particle, bool hasFinalConsonant)
    {
        if (hasFinalConsonant)
        {
            return particle switch
            {
                "를" => "을",
                "는" => "은",
                "가" => "이",
                "와" => "과",
                _ => null,
            };
        }

        return particle switch
        {
            "을" => "를",
            "은" => "는",
            "이" => "가",
            "과" => "와",
            _ => null,
        };
    }

    private static string StripUiTokens(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        return UiTagTokenRegex.Replace(text, "");
    }

    private static string NormalizeComparableText(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }

        var stripped = StripUiTokens(text);
        if (stripped.Length == 0)
        {
            return "";
        }

        var sb = new StringBuilder(capacity: stripped.Length);
        var inWhitespace = false;
        foreach (var ch in stripped)
        {
            var c = ch;
            if (c == '\r' || c == '\n')
            {
                c = ' ';
            }

            if (char.IsWhiteSpace(c))
            {
                if (!inWhitespace)
                {
                    sb.Append(' ');
                    inWhitespace = true;
                }
                continue;
            }

            inWhitespace = false;
            sb.Append(c);
        }

        return sb.ToString().Trim().ToLowerInvariant();
    }

    private static bool ContainsAsciiLetter(string s)
    {
        foreach (var c in s)
        {
            if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z'))
            {
                return true;
            }
        }

        return false;
    }

    private static List<(int Start, int Length)> FindSourceTermSpans(string sourceText, string sourceTerm, GlossaryEntry entry)
    {
        var spans = new List<(int Start, int Length)>();
        if (entry.MatchMode is not (GlossaryMatchMode.Substring or GlossaryMatchMode.WordBoundary))
        {
            return spans;
        }

        var comparison = GlossaryApplier.ForcesOnlyExactCase(entry) ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        var idx = 0;
        while ((idx = sourceText.IndexOf(sourceTerm, idx, comparison)) >= 0)
        {
            var end = idx + sourceTerm.Length;
            var counts = entry.MatchMode == GlossaryMatchMode.Substring || IsWordBoundary(sourceText, idx, sourceTerm.Length);

            // Same exceptions as GlossaryApplier ("Reach level 10", "What in Oblivion", "the Scroll", "Yes, Master.").
            if (counts && (GlossaryApplier.IsBuiltInTermUsedOtherwise(sourceText, entry, idx, sourceTerm.Length)
                           || GlossaryApplier.IsCapitalizedOnlyByPosition(sourceText, entry, idx, sourceTerm.Length)))
            {
                counts = false;
            }

            if (counts)
            {
                spans.Add((idx, sourceTerm.Length));
            }

            idx = end;
        }

        return spans;
    }

    private static bool IsWordBoundary(string text, int matchIndex, int matchLength)
    {
        var before = matchIndex - 1;
        if (before >= 0 && IsWordChar(text[before]))
        {
            return false;
        }

        var after = matchIndex + matchLength;
        if (after < text.Length && IsWordChar(text[after]))
        {
            return false;
        }

        return true;
    }

    private static bool IsWordChar(char c)
        => char.IsLetterOrDigit(c) || c == '_';

    private static bool ContainsIgnoreCase(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
}
