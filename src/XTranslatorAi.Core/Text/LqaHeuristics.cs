using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

public static class LqaHeuristics
{
    private static readonly Regex UiTagTokenRegex = new(
        pattern: @"[+-]?<\s*[^>]+\s*>|\[pagebreak\]|__XT_[A-Za-z0-9_]+__",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex DoubledParticleRegex = new(
        pattern: @"을\s*를|를\s*을|은\s*는|는\s*은|이\s*가|가\s*이|와\s*과|과\s*와",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex HangulParticleRegex = new(
        pattern: @"(?<word>[가-힣]{1,20})(?<particle>을|를|은|는|이|가|와|과)(?=$|[\s\p{P}])",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex RomanVowelConsonantParticleRegex = new(
        pattern: @"\b(?<word>[A-Za-z][A-Za-z0-9'’\-]{1,})(?<particle>을|은|이|과)(?=$|[\s\p{P}])",
        options: RegexOptions.CultureInvariant
    );

    private static readonly string[] UnresolvedParticleMarkers =
    {
        "을(를)", "를(을)", "은(는)", "는(은)", "이(가)", "가(이)", "과(와)", "와(과)", "으로(로)", "로(으로)",
        "을/를", "를/을", "은/는", "는/은", "이/가", "가/이", "과/와", "와/과", "으로/로", "로/으로",
    };

    private static readonly Regex DuplicationArtifactRegex = new(
        pattern: @"(?:효과\s+효과|초\s+초)",
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

        if (!ContainsAsciiLetter(src))
        {
            return false;
        }

        return string.Equals(src, dst, StringComparison.Ordinal);
    }

    public static string? FindDoubledParticleExample(string destText)
    {
        if (string.IsNullOrWhiteSpace(destText))
        {
            return null;
        }

        var m = DoubledParticleRegex.Match(destText);
        if (!m.Success || string.IsNullOrWhiteSpace(m.Value))
        {
            return null;
        }

        return Regex.Replace(m.Value, @"\s+", "", RegexOptions.CultureInvariant);
    }

    public static string? FindHangulParticleMismatchSuggestion(string destText)
    {
        if (string.IsNullOrWhiteSpace(destText))
        {
            return null;
        }

        foreach (Match m in HangulParticleRegex.Matches(destText))
        {
            if (!m.Success)
            {
                continue;
            }

            var word = m.Groups["word"].Value;
            var particle = m.Groups["particle"].Value;
            if (string.IsNullOrWhiteSpace(word) || string.IsNullOrWhiteSpace(particle))
            {
                continue;
            }

            var last = word[^1];
            var hasJongseong = HasFinalConsonant(last);
            var expected = GetExpectedParticleForHangul(particle, hasJongseong);
            if (expected == null)
            {
                continue;
            }

            return $"{word}{particle} → {word}{expected}";
        }

        return null;
    }

    public static string? FindRomanVowelParticleMismatchSuggestion(string destText)
    {
        if (string.IsNullOrWhiteSpace(destText))
        {
            return null;
        }

        foreach (Match m in RomanVowelConsonantParticleRegex.Matches(destText))
        {
            if (!m.Success)
            {
                continue;
            }

            var word = m.Groups["word"].Value;
            var particle = m.Groups["particle"].Value;
            if (string.IsNullOrWhiteSpace(word) || string.IsNullOrWhiteSpace(particle))
            {
                continue;
            }

            var last = word[^1];
            if (!IsAsciiVowel(last))
            {
                continue;
            }

            var expected = GetExpectedParticleForHangul(particle, hasFinalConsonant: false);
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
            if (destText.Contains(marker, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

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

            if (ContainsIgnoreCase(dst, targetTerm))
            {
                continue;
            }

            if (!ContainsSourceTerm(src, sourceTerm, entry))
            {
                continue;
            }

            return entry;
        }

        return null;
    }

    private static bool HasFinalConsonant(char syllable)
    {
        if (syllable < '가' || syllable > '힣')
        {
            return false;
        }

        var code = syllable - '가';
        return (code % 28) != 0;
    }

    private static bool IsAsciiVowel(char c)
    {
        c = char.ToLowerInvariant(c);
        return c is 'a' or 'e' or 'i' or 'o' or 'u' or 'y';
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

    private static bool ContainsSourceTerm(string sourceText, string sourceTerm, GlossaryEntry entry)
    {
        if (entry.MatchMode == GlossaryMatchMode.Substring)
        {
            return ContainsIgnoreCase(sourceText, sourceTerm);
        }

        if (entry.MatchMode != GlossaryMatchMode.WordBoundary)
        {
            return false;
        }

        if (IsBuiltInDefaultGlossary(entry) && string.Equals(sourceTerm, "Reach", StringComparison.OrdinalIgnoreCase))
        {
            return ContainsReachPlaceNameOccurrence(sourceText);
        }

        return ContainsWordBoundaryIgnoreCase(sourceText, sourceTerm);
    }

    private static bool IsBuiltInDefaultGlossary(GlossaryEntry entry)
    {
        return !string.IsNullOrWhiteSpace(entry.Note)
               && entry.Note.StartsWith("Built-in default glossary", StringComparison.Ordinal);
    }

    private static bool ContainsReachPlaceNameOccurrence(string sourceText)
    {
        const string term = "Reach";
        var idx = 0;
        while (idx < sourceText.Length)
        {
            var hit = sourceText.IndexOf(term, idx, StringComparison.OrdinalIgnoreCase);
            if (hit < 0)
            {
                return false;
            }

            if (!IsWordBoundary(sourceText, hit, term.Length))
            {
                idx = hit + term.Length;
                continue;
            }

            // Built-in suppression rules (match GlossaryApplier behavior):
            // - Require exact casing "Reach" to avoid matching general "reach".
            if (!string.Equals(sourceText.Substring(hit, term.Length), term, StringComparison.Ordinal))
            {
                idx = hit + term.Length;
                continue;
            }

            // - Suppress obvious verb/common-noun patterns: "reach of", "Reach level ...".
            var nextWord = ReadNextAsciiWord(sourceText, hit + term.Length);
            if (string.Equals(nextWord, "of", StringComparison.OrdinalIgnoreCase)
                || string.Equals(nextWord, "level", StringComparison.OrdinalIgnoreCase)
                || string.Equals(nextWord, "levels", StringComparison.OrdinalIgnoreCase))
            {
                idx = hit + term.Length;
                continue;
            }

            return true;
        }

        return false;
    }

    private static string ReadNextAsciiWord(string text, int startIndex)
    {
        if (string.IsNullOrEmpty(text) || startIndex < 0)
        {
            return "";
        }

        var i = Math.Min(startIndex, text.Length);
        while (i < text.Length && char.IsWhiteSpace(text[i]))
        {
            i++;
        }

        var start = i;
        while (i < text.Length && ((text[i] >= 'A' && text[i] <= 'Z') || (text[i] >= 'a' && text[i] <= 'z')))
        {
            i++;
        }

        return start < i ? text.Substring(start, i - start) : "";
    }

    private static bool ContainsWordBoundaryIgnoreCase(string text, string term)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(term))
        {
            return false;
        }

        var idx = 0;
        while (idx < text.Length)
        {
            var hit = text.IndexOf(term, idx, StringComparison.OrdinalIgnoreCase);
            if (hit < 0)
            {
                return false;
            }

            if (IsWordBoundary(text, hit, term.Length))
            {
                return true;
            }

            idx = hit + term.Length;
        }

        return false;
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
