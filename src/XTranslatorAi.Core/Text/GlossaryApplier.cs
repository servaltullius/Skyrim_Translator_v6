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
    /// Many single-word game terms are also ordinary English words: Fine (초급), Master (달인),
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
            && (!string.Equals(match.Value, entry.SourceTerm, StringComparison.Ordinal)
                || IsCapitalizedOnlyByPosition(input, entry, match.Index, match.Length)))
        {
            return BuiltInMatch.CommonWord;
        }

        return IsBuiltInTermUsedOtherwise(input, entry, match.Index, match.Length) ? BuiltInMatch.NotTheTerm : BuiltInMatch.Force;
    }

    private static readonly Regex ExclamationBeforeRealm = new(
        @"\b(?:what|why|who|how|where|when)\s+(?:the\s+)?(?:in|on)\s+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    private static readonly Regex TheBefore = new(@"\b(?:the|this|that|other)\s+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // "Master Alteration" and the other skill levels are 달인; before a name, Master is a title.
    private static readonly HashSet<string> MasterLevelWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "Alteration", "Conjuration", "Destruction", "Illusion", "Restoration", "Alchemy", "Enchanting", "Smithing", "Archery",
        "Block", "Sneak", "Lockpicking", "Pickpocket", "Speech", "Trainer", "Level",
    };

    /// <summary>
    /// Built-in terms in a sense the glossary target does not have. Forcing them broke Serana Dialogue
    /// Add-On lines: "What in Oblivion is this place?" became "오블리비언의 여긴 어디야?", "How on Nirn"
    /// became "넌에서", and Harkon's "I trust you have the Scroll?" (the Elder Scroll) became "주문서".
    /// "Reach" is also a verb at the start of a sentence: "Reach level 10", "Reach of ...".
    /// "Master Neloth" is a title (넬로스 스승, 넬로스 주인 in the official translation), not 달인.
    /// </summary>
    internal static bool IsBuiltInTermUsedOtherwise(string text, GlossaryEntry entry, int index, int length)
    {
        if (!IsBuiltInDefaultEntry(entry))
        {
            return false;
        }

        var term = entry.SourceTerm.Trim();
        var nextWord = ReadNextAsciiWord(text, index + length);
        if (string.Equals(term, "Reach", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(nextWord, "of", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(nextWord, "level", StringComparison.OrdinalIgnoreCase)
                   || string.Equals(nextWord, "levels", StringComparison.OrdinalIgnoreCase);
        }

        if (term == "Master")
        {
            return text[index] == 'M' && nextWord.Length > 1 && char.IsUpper(nextWord[0]) && !MasterLevelWords.Contains(nextWord);
        }

        var start = Math.Max(0, index - 40);
        var before = text.Substring(start, index - start);
        if (term is "Oblivion" or "Nirn")
        {
            return ExclamationBeforeRealm.IsMatch(before);
        }

        return term == "Scroll" && TheBefore.IsMatch(before) && !string.Equals(nextWord, "of", StringComparison.OrdinalIgnoreCase);
    }

    private static readonly Regex LayoutTokenAtEnd = new(@"__XT_PH_[0-9]{4}__$", RegexOptions.CultureInvariant);

    // Built-in terms that are also ordinary English words. Names (Talos, Keening, Sanguine), skills and attributes
    // ("Shalidor's Insights: Illusion", "Health, Magicka, and Stamina") are forced wherever they stand.
    private static readonly HashSet<string> OrdinaryWordTerms = new(StringComparer.Ordinal)
    {
        "Fine", "Superior", "Exquisite", "Flawless", "Epic", "Legendary", "Pale", "Master", "Expert", "Adept", "Apprentice",
        "Champion", "Reach", "Fortify", "Resist", "Block", "Sneak", "Barter", "Ward", "Flesh", "Bash", "Parry", "Perk",
        "Hood", "Cape", "Cloak", "Boots", "Mace",
    };

    // Of those, adjectives: before any lowercase word they describe it ("Pale light", "Fine work").
    private static readonly HashSet<string> AdjectiveTerms = new(StringComparer.Ordinal)
    {
        "Fine", "Superior", "Exquisite", "Flawless", "Epic", "Legendary", "Pale",
    };

    // Words after a sentence-initial verb or noun that show it is not the game term ("Reach the summit", "Resist the urge").
    // "and"/"or" are left out: a line can start a list of skills ("Sneak and Speech").
    private static readonly HashSet<string> FunctionWords = new(StringComparer.Ordinal)
    {
        "the", "a", "an", "your", "my", "his", "her", "its", "our", "their", "this", "that", "these", "those", "it", "me",
        "you", "him", "us", "them", "yourself", "myself", "to", "with", "up", "down", "out", "back",
        "now", "all", "every", "some", "any", "no", "not", "past", "over", "away",
    };

    /// <summary>
    /// A built-in term that is also an ordinary word, capitalized only because of where it stands: the first word of a
    /// sentence used in its ordinary sense ("Fine. I'll do it." became "하급.", "Reach the summit" 리치, "Pale light"
    /// 페일) or a word of address ("Yes, Master." became "네, 달인."). Item, effect and skill uses stay forced: a
    /// capitalized next word or a value ("Fine Iron Sword", "Fortify Health", "Master of Stealth", "Resist &lt;mag&gt;%"),
    /// a skill before its noun ("Destruction spells"), or a text that is the term alone (an item tier, a skill level).
    /// </summary>
    internal static bool IsCapitalizedOnlyByPosition(string text, GlossaryEntry entry, int index, int length)
    {
        var term = entry.SourceTerm.Trim();
        if (!ForcesOnlyExactCase(entry) || !OrdinaryWordTerms.Contains(term) || text.Trim().Length == length)
        {
            return false;
        }

        var after = index + length;
        var next = after;
        while (next < text.Length && text[next] == ' ')
        {
            next++;
        }

        if (next > after && next < text.Length && StartsName(text, next))
        {
            return false;
        }

        // Address ends the sentence ("Yes, Master."); "A, B, and C" is a list.
        var endsSentence = next >= text.Length || text[next] is '.' or '!' or '?' or '…';
        var before = text.AsSpan(0, index).TrimEnd();
        if (before.EndsWith(",") && endsSentence)
        {
            return true;
        }

        // A colon introduces a label or title ("Rank: Master"), so it does not start a sentence here.
        before = before.TrimEnd("\"'“‘([*-—".AsSpan()).TrimEnd();
        var startsSentence = before.IsEmpty || before[^1] is '.' or '!' or '?' or '…' || LayoutTokenAtEnd.IsMatch(before.ToString());
        if (!startsSentence)
        {
            return false;
        }

        var nextWord = ReadNextAsciiWord(text, after);
        return endsSentence || next < text.Length && text[next] == ','
               || FunctionWords.Contains(nextWord)
               || AdjectiveTerms.Contains(term) && nextWord.Length > 0 && char.IsAsciiLetterLower(nextWord[0]);
    }

    // A capitalized word, a value or a token; "of" counts when a capitalized word follows it ("Master of Stealth").
    private static bool StartsName(string text, int at)
    {
        if (char.IsAsciiLetterUpper(text[at]) || char.IsAsciiDigit(text[at]) || text.AsSpan(at).StartsWith("__XT_") || text[at] == '<')
        {
            return true;
        }

        return text.AsSpan(at).StartsWith("of ") && at + 3 < text.Length && char.IsAsciiLetterUpper(text[at + 3]);
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
