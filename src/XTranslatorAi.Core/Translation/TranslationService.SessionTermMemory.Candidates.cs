using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static readonly Regex EnglishPhraseRegex = new(
        pattern:
            @"\b(?:[A-Z][A-Za-z0-9'’\-]*)(?:\s+(?:[A-Z][A-Za-z0-9'’\-]*|of|the|and|or|to|a|an|in|on|for|with|from|at|by|de|la|le|du|van|von))+\b",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex EnglishTitleWordRegex = new(
        pattern: @"\b[A-Z][A-Za-z0-9'’\-]{2,}\b",
        options: RegexOptions.CultureInvariant
    );

    private static bool IsSessionTermRec(string? rec)
    {
        if (string.IsNullOrWhiteSpace(rec))
        {
            return false;
        }

        return rec.IndexOf(":FULL", StringComparison.OrdinalIgnoreCase) >= 0
            || rec.IndexOf(":NAME", StringComparison.OrdinalIgnoreCase) >= 0
            || rec.IndexOf(":NAM", StringComparison.OrdinalIgnoreCase) >= 0
            || rec.IndexOf(":TITLE", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static bool IsSessionTermDefinitionText(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return false;
        }

        var s = sourceText.Trim();
        if (s.Length is < 3 or > 60)
        {
            return false;
        }

        if (s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0)
        {
            return false;
        }

        if (XtTokenRegex.IsMatch(s))
        {
            return false;
        }

        if (s.IndexOf(' ') < 0 && !IsSessionTermSingleWordCandidate(s))
        {
            return false;
        }

        // Avoid obvious sentences.
        if (s.IndexOfAny(new[] { '.', '!', '?', ':', ';' }) >= 0)
        {
            return false;
        }

        // Keep conservative: allow letters/digits/spaces plus common name punctuation.
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '\'' or '’')
            {
                continue;
            }
            return false;
        }

        return true;
    }

    private static bool IsSessionTermSingleWordCandidate(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        var s = term.Trim();
        if (s.Length is < 3 or > 40)
        {
            return false;
        }

        // Require TitleCase-ish to avoid learning generic words.
        if (s[0] is < 'A' or > 'Z')
        {
            return false;
        }

        var hasLower = false;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch is >= 'a' and <= 'z')
            {
                hasLower = true;
                break;
            }
        }

        return hasLower;
    }

    private static bool IsSessionTermTranslationText(string translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            return false;
        }

        var s = translatedText.Trim();
        if (s.Length is < 1 or > 80)
        {
            return false;
        }

        if (s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0)
        {
            return false;
        }

        if (XtTokenRegex.IsMatch(s))
        {
            return false;
        }

        return true;
    }

    private static string NormalizeSessionTermKey(string term)
    {
        var s = (term ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s))
        {
            return "";
        }

        if (s.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(4).TrimStart();
        }
        else if (s.StartsWith("An ", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(3).TrimStart();
        }
        else if (s.StartsWith("A ", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(2).TrimStart();
        }

        return s.Trim();
    }

    private IReadOnlyList<(string Source, string Target)> MergeSessionPromptOnlyGlossaryForText(
        string text,
        IReadOnlyList<(string Source, string Target)> basePromptOnlyGlossary
    )
    {
        if (!_enableSessionTermMemory || _sessionTermMemory == null)
        {
            return basePromptOnlyGlossary;
        }

        return _sessionTermMemory.MergeForText(text, basePromptOnlyGlossary);
    }

    private IReadOnlyList<(string Source, string Target)> MergeSessionPromptOnlyGlossaryForTexts(
        IReadOnlyList<string> texts,
        IReadOnlyList<(string Source, string Target)> basePromptOnlyGlossary
    )
    {
        if (!_enableSessionTermMemory || _sessionTermMemory == null)
        {
            return basePromptOnlyGlossary;
        }

        return _sessionTermMemory.MergeForTexts(texts, basePromptOnlyGlossary);
    }

    private IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> SelectSessionTermSeedItems(
        IReadOnlyList<(long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)> items,
        int maxSeedCount
    )
    {
        if (!_enableSessionTermMemory || maxSeedCount <= 0)
        {
            return Array.Empty<(long, string, string, MaskedText, GlossaryApplication)>();
        }

        // Pick definition rows (FULL/NAM/TITLE) that correspond to term candidates.
        var definitionByKey =
            new Dictionary<string, (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary)>(
                StringComparer.OrdinalIgnoreCase
            );
        foreach (var it in items)
        {
            if (!IsSessionTermRec(GetRecForId(it.Id)))
            {
                continue;
            }

            if (!IsSessionTermDefinitionText(it.Source))
            {
                continue;
            }

            // Seeding is only helpful when the definition row is short enough to complete quickly.
            if (it.Masked.Length > 6000)
            {
                continue;
            }

            var key = NormalizeSessionTermKey(it.Source);
            if (!definitionByKey.ContainsKey(key))
            {
                definitionByKey[key] = it;
            }
        }

        if (definitionByKey.Count == 0)
        {
            return Array.Empty<(long, string, string, MaskedText, GlossaryApplication)>();
        }

        var definitionKeys = new HashSet<string>(definitionByKey.Keys, StringComparer.OrdinalIgnoreCase);

        // Count title-like English phrases/words across all sources, but only when we have a definition row for it.
        var termCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var it in items)
        {
            if (string.IsNullOrWhiteSpace(it.Source))
            {
                continue;
            }

            foreach (Match m in EnglishPhraseRegex.Matches(it.Source))
            {
                var key = NormalizeSessionTermKey(m.Value);
                if (key.Length is < 3 or > 60 || !definitionKeys.Contains(key))
                {
                    continue;
                }

                if (termCounts.TryGetValue(key, out var count))
                {
                    termCounts[key] = count + 1;
                }
                else
                {
                    termCounts[key] = 1;
                }
            }

            foreach (Match m in EnglishTitleWordRegex.Matches(it.Source))
            {
                var key = NormalizeSessionTermKey(m.Value);
                if (key.Length is < 3 or > 60 || !definitionKeys.Contains(key))
                {
                    continue;
                }

                if (termCounts.TryGetValue(key, out var count))
                {
                    termCounts[key] = count + 1;
                }
                else
                {
                    termCounts[key] = 1;
                }
            }
        }

        var candidates = new List<(int Count, int Len, (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary) Item)>();
        foreach (var (key, count) in termCounts)
        {
            if (count < 2)
            {
                continue;
            }

            if (definitionByKey.TryGetValue(key, out var def))
            {
                candidates.Add((Count: count, Len: key.Length, Item: def));
            }
        }

        if (candidates.Count == 0)
        {
            return Array.Empty<(long, string, string, MaskedText, GlossaryApplication)>();
        }

        candidates.Sort((a, b) =>
        {
            var byCount = b.Count.CompareTo(a.Count);
            if (byCount != 0)
            {
                return byCount;
            }
            return b.Len.CompareTo(a.Len);
        });

        var take = Math.Min(maxSeedCount, candidates.Count);
        var result = new (long, string, string, MaskedText, GlossaryApplication)[take];
        for (var i = 0; i < take; i++)
        {
            result[i] = candidates[i].Item;
        }

        return result;
    }
}

