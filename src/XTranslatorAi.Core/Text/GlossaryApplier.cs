using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

public sealed class GlossaryApplier
{
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

        var tokenToReplacement = new SortedDictionary<string, string>(StringComparer.Ordinal);
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

            working = ReplaceAllMatchesTokenSafe(working, compiled, tokenToReplacement, entryIdToToken, out var skippedCommonWord);
            if (skippedCommonWord)
            {
                // Not forced, but the model may still need the term if the word is used in that sense.
                promptOnlyPairs.Add((entry.SourceTerm, entry.TargetTerm));
            }
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
        IDictionary<string, string> tokenToReplacement,
        Dictionary<long, string> entryIdToToken,
        out bool skippedCommonWord
    )
    {
        skippedCommonWord = false;
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

            sb.Append(ReplaceAllMatchesInPlainText(text, entry, tokenToReplacement, entryIdToToken, ref skippedCommonWord));
        }

        return sb.ToString();
    }

    private static string ReplaceAllMatchesInPlainText(
        string input,
        CompiledGlossaryEntry entry,
        IDictionary<string, string> tokenToReplacement,
        Dictionary<long, string> entryIdToToken,
        ref bool skippedCommonWord
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
        var skipped = false;
        var replaced = regex.Replace(
            input,
            m =>
            {
                switch (ClassifyBuiltInDefaultGlossaryMatch(input, entry.Entry, m))
                {
                    case BuiltInMatch.CommonWord:
                        skipped = true;
                        return m.Value;
                    case BuiltInMatch.NotTheTerm:
                        return m.Value;
                    default:
                        return GetOrCreateEntryToken(entry.Entry, tokenToReplacement, entryIdToToken);
                }
            }
        );
        skippedCommonWord |= skipped;
        return replaced;
    }

    private enum BuiltInMatch
    {
        Force,
        CommonWord,
        NotTheTerm,
    }

    internal static bool IsBuiltInDefaultEntry(GlossaryEntry entry)
        => !string.IsNullOrWhiteSpace(entry.Note)
           && entry.Note.StartsWith("Built-in default glossary", StringComparison.Ordinal);

    /// <summary>
    /// Many single-word game terms are also ordinary English words: Fine (하급), Master (달인),
    /// Superior (중급), Destruction (파괴마법), Ward (방어막), Sneak (은신), Reach (리치).
    /// Game text writes the term capitalized, so for built-in terms only that exact casing is forced;
    /// "fine blond hair" or "superior officer" is left to the model with the term only as a hint.
    /// </summary>
    internal static bool ForcesOnlyExactCase(GlossaryEntry entry)
        => IsBuiltInDefaultEntry(entry) && IsCapitalizedSingleWord(entry.SourceTerm);

    private static BuiltInMatch ClassifyBuiltInDefaultGlossaryMatch(string input, GlossaryEntry entry, Match match)
    {
        if (!IsBuiltInDefaultEntry(entry))
        {
            return BuiltInMatch.Force;
        }

        if (ForcesOnlyExactCase(entry)
            && !string.Equals(match.Value, entry.SourceTerm, StringComparison.Ordinal))
        {
            return BuiltInMatch.CommonWord;
        }

        // "Reach" is also capitalized at the start of a sentence: "Reach level 10", "Reach of ...".
        if (string.Equals(entry.SourceTerm, "Reach", StringComparison.OrdinalIgnoreCase))
        {
            var nextWord = ReadNextAsciiWord(input, match.Index + match.Length);
            if (string.Equals(nextWord, "of", StringComparison.OrdinalIgnoreCase)
                || string.Equals(nextWord, "level", StringComparison.OrdinalIgnoreCase)
                || string.Equals(nextWord, "levels", StringComparison.OrdinalIgnoreCase))
            {
                return BuiltInMatch.NotTheTerm;
            }
        }

        return BuiltInMatch.Force;
    }

    private static bool IsCapitalizedSingleWord(string term)
    {
        var trimmed = term.Trim();
        return trimmed.Length > 1
               && trimmed[0] is >= 'A' and <= 'Z'
               && !trimmed.Any(char.IsWhiteSpace);
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
        IDictionary<string, string> tokenToReplacement,
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
            _ => throw new ArgumentOutOfRangeException(nameof(entry), entry.MatchMode, "Unsupported glossary match mode."),
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
        foreach (Match m in TranslationConstants.XtTokenRegex.Matches(text))
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
