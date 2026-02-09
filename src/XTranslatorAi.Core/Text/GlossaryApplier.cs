using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

public sealed class GlossaryApplier
{
    private static readonly Regex XtTokenRegex = new(
        pattern: @"__XT_(?:PH|TERM)(?:_[A-Z0-9]+)?_[0-9]{4}__",
        options: RegexOptions.CultureInvariant
    );

    private readonly IReadOnlyList<CompiledGlossaryEntry> _entries;

    public GlossaryApplier(IEnumerable<GlossaryEntry> entries)
    {
        _entries = entries
            .Where(e => e.Enabled)
            .OrderByDescending(e => e.Priority)
            .ThenByDescending(e => e.SourceTerm.Length)
            .Select(e => new CompiledGlossaryEntry(e, CreateRegex(e)))
            .ToList();
    }

    public GlossaryApplication Apply(string text)
    {
        if (_entries.Count == 0)
        {
            return new GlossaryApplication(
                Text: text,
                TokenToReplacement: new Dictionary<string, string>(),
                PromptOnlyPairs: Array.Empty<(string Source, string Target)>()
            );
        }

        var tokenToReplacement = new Dictionary<string, string>(StringComparer.Ordinal);
        var entryIdToToken = new Dictionary<long, string>();
        var promptOnlyPairs = new List<(string Source, string Target)>();

        var working = text;
        foreach (var compiled in _entries)
        {
            var entry = compiled.Entry;
            if (entry.ForceMode == GlossaryForceMode.PromptOnly)
            {
                if (ContainsInPlainText(working, entry.SourceTerm))
                {
                    promptOnlyPairs.Add((entry.SourceTerm, entry.TargetTerm));
                }
                continue;
            }

            working = ReplaceAllMatchesTokenSafe(working, compiled, tokenToReplacement, entryIdToToken);
        }

        return new GlossaryApplication(
            Text: working,
            TokenToReplacement: tokenToReplacement,
            PromptOnlyPairs: promptOnlyPairs
        );
    }

    private static string ReplaceAllMatchesTokenSafe(
        string input,
        CompiledGlossaryEntry entry,
        Dictionary<string, string> tokenToReplacement,
        Dictionary<long, string> entryIdToToken
    )
    {
        if (string.IsNullOrEmpty(input))
        {
            return input;
        }

        var pieces = SplitIntoTokenAndTextPieces(input);
        var sb = new StringBuilder(capacity: input.Length);

        foreach (var (text, isToken) in pieces)
        {
            if (isToken)
            {
                sb.Append(text);
                continue;
            }

            sb.Append(ReplaceAllMatchesInPlainText(text, entry, tokenToReplacement, entryIdToToken));
        }

        return sb.ToString();
    }

    private static string ReplaceAllMatchesInPlainText(
        string input,
        CompiledGlossaryEntry entry,
        Dictionary<string, string> tokenToReplacement,
        Dictionary<long, string> entryIdToToken
    )
    {
        var matchMode = entry.Entry.MatchMode;
        if (matchMode == GlossaryMatchMode.Substring)
        {
            if (input.IndexOf(entry.Entry.SourceTerm, StringComparison.OrdinalIgnoreCase) < 0)
            {
                return input;
            }

            var token = GetOrCreateEntryToken(entry.Entry, tokenToReplacement, entryIdToToken);
            return ReplaceSubstring(input, entry.Entry.SourceTerm, token);
        }

        if (matchMode == GlossaryMatchMode.WordBoundary
            && input.IndexOf(entry.Entry.SourceTerm, StringComparison.OrdinalIgnoreCase) < 0)
        {
            return input;
        }

        var regex = entry.Regex ?? throw new InvalidOperationException($"Missing regex for match mode: {matchMode}");
        return regex.Replace(
            input,
            m =>
            {
                if (ShouldSuppressBuiltInDefaultGlossaryReplacement(input, entry.Entry, m))
                {
                    return m.Value;
                }

                return GetOrCreateEntryToken(entry.Entry, tokenToReplacement, entryIdToToken);
            }
        );
    }

    private static bool ShouldSuppressBuiltInDefaultGlossaryReplacement(string input, GlossaryEntry entry, Match match)
    {
        if (string.IsNullOrWhiteSpace(entry.Note)
            || !entry.Note.StartsWith("Built-in default glossary", StringComparison.Ordinal))
        {
            return false;
        }

        // "Reach" is a place name in Skyrim, but also a very common English word (reach).
        // Avoid forcing the place-name translation in common-phrase contexts.
        if (!string.Equals(entry.SourceTerm, "Reach", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        // Require exact casing to avoid matching general "reach" occurrences.
        if (!string.Equals(match.Value, "Reach", StringComparison.Ordinal))
        {
            return true;
        }

        // Suppress obvious verb/common-noun patterns: "reach of", "Reach level ...".
        var nextWord = ReadNextAsciiWord(input, match.Index + match.Length);
        return string.Equals(nextWord, "of", StringComparison.OrdinalIgnoreCase)
               || string.Equals(nextWord, "level", StringComparison.OrdinalIgnoreCase)
               || string.Equals(nextWord, "levels", StringComparison.OrdinalIgnoreCase);
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

    private static string ReplaceSubstring(string input, string sourceTerm, string token)
    {
        if (string.IsNullOrEmpty(sourceTerm))
        {
            return input;
        }

        var first = input.IndexOf(sourceTerm, StringComparison.OrdinalIgnoreCase);
        if (first < 0)
        {
            return input;
        }

        var sb = new StringBuilder(capacity: input.Length);
        sb.Append(input.AsSpan(0, first));
        sb.Append(token);

        var cursor = first + sourceTerm.Length;
        while (cursor < input.Length)
        {
            var next = input.IndexOf(sourceTerm, cursor, StringComparison.OrdinalIgnoreCase);
            if (next < 0)
            {
                sb.Append(input.AsSpan(cursor));
                break;
            }

            sb.Append(input.AsSpan(cursor, next - cursor));
            sb.Append(token);
            cursor = next + sourceTerm.Length;
        }

        return sb.ToString();
    }

    private static string GetOrCreateEntryToken(
        GlossaryEntry entry,
        Dictionary<string, string> tokenToReplacement,
        Dictionary<long, string> entryIdToToken
    )
    {
        if (entryIdToToken.TryGetValue(entry.Id, out var existing))
        {
            return existing;
        }

        var token = $"__XT_TERM_G{entry.Id}_0000__";
        entryIdToToken[entry.Id] = token;
        tokenToReplacement[token] = entry.TargetTerm;
        return token;
    }

    private static Regex? CreateRegex(GlossaryEntry entry)
    {
        if (entry.ForceMode == GlossaryForceMode.PromptOnly)
        {
            return null;
        }

        var matchMode = entry.MatchMode;
        if (matchMode == GlossaryMatchMode.Substring)
        {
            return null;
        }

        var pattern = matchMode switch
        {
            // \b only matches at word/non-word boundaries and fails for terms ending with punctuation (e.g., "...most.").
            // Use \w-based guards instead so terms like "A skill beyond the reach of most." can still match as a whole.
            GlossaryMatchMode.WordBoundary => $@"(?<!\w){Regex.Escape(entry.SourceTerm)}(?!\w)",
            GlossaryMatchMode.Regex => entry.SourceTerm,
            _ => throw new ArgumentOutOfRangeException(nameof(entry.MatchMode), entry.MatchMode, null),
        };

        return new Regex(pattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    }

    private static bool ContainsInPlainText(string text, string needle)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(needle))
        {
            return false;
        }

        foreach (var (piece, isToken) in SplitIntoTokenAndTextPieces(text))
        {
            if (!isToken && piece.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static IEnumerable<(string Text, bool IsToken)> SplitIntoTokenAndTextPieces(string text)
    {
        var idx = 0;
        foreach (Match m in XtTokenRegex.Matches(text))
        {
            if (m.Index > idx)
            {
                yield return (text.Substring(idx, m.Index - idx), false);
            }

            yield return (m.Value, true);
            idx = m.Index + m.Length;
        }

        if (idx < text.Length)
        {
            yield return (text.Substring(idx), false);
        }
    }

    private sealed record CompiledGlossaryEntry(GlossaryEntry Entry, Regex? Regex);
}

public sealed record GlossaryApplication(
    string Text,
    IReadOnlyDictionary<string, string> TokenToReplacement,
    IReadOnlyList<(string Source, string Target)> PromptOnlyPairs
);
