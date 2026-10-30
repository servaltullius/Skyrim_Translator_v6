using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services.AgentMode;

/// <summary>One English name a translated text brings, and the rows it is in.</summary>
public sealed record AgentTextName(string Name, IReadOnlyList<long> Ids);

/// <summary>
/// The names inside translated text (books, letters, dialogue) that nothing the translator has knows: no glossary
/// term, TM line, game text or name field of the mod has them, so the run wrote them itself. names lists name fields
/// only, and in the LotD book test the bodies' names (Auryen Morellus, the War of Betony) went unreported; Claude
/// listed them by hand. The English is listed for the agent to say how it wrote each one.
/// </summary>
public static class AgentTextNames
{
    private static readonly Regex WordRegex = new(@"[A-Za-z]+(?:['’][A-Za-z]+)*(?:-[A-Za-z]+(?:['’][A-Za-z]+)*)*", RegexOptions.CultureInvariant);
    private static readonly Regex MarkupRegex = new(@"<[^>]*>|" + ProtectedTextKinds.BracketPageBreakPattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // Between two words, any of these starts a sentence (or a quoted one) at the second.
    private const string SentenceBreaks = ".!?;:\n\"“…";

    // Joined inside a name ("War of Betony", "Isle of the Dead") but never start or end one.
    private static readonly HashSet<string> Joiners = new(StringComparer.Ordinal) { "of", "the", "de", "von", "van", "al" };

    // Capitalized but not names when they lead a run ("The Falmer", "My Lord Bridwell").
    private static readonly HashSet<string> Leaders = new(StringComparer.Ordinal)
    {
        "The", "A", "An", "My", "Our", "Your", "His", "Her", "Its", "Their", "This", "That", "These", "Those",
    };

    private static readonly HashSet<string> NotNames = new(StringComparer.Ordinal) { "I", "I'm", "I've", "I'll", "I'd", "O", "Oh", "Ah", "Mr", "Mrs", "Ms" };

    // Words that start a sentence before a name ("During the War of Betony", "But the Dwemer", "Are the Snow Elves").
    private static readonly HashSet<string> SentenceWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "on", "to", "for", "but", "and", "or", "so", "are", "is", "was", "were", "be", "during", "in", "at", "with", "from",
        "by", "if", "when", "while", "as", "after", "before", "since", "until", "into", "upon", "unto", "under", "over",
        "about", "against", "among", "like", "unlike", "even", "yet", "then", "now", "here", "there", "do", "does", "did",
        "have", "has", "had", "will", "would", "may", "might", "can", "could", "should", "must", "let", "ask", "tell",
    };

    /// <summary>
    /// The capitalized runs of a text that read as names. A lone word that starts a sentence could be any word, so it
    /// counts only when it also shows inside one; the caller drops lone words the texts also write in lowercase.
    /// </summary>
    public static IEnumerable<string> Candidates(string text, Func<string, bool>? isOrdinary = null)
    {
        var plain = MarkupRegex.Replace(text, "\n");
        var words = WordRegex.Matches(plain);
        var inSentence = new HashSet<string>(StringComparer.Ordinal);
        var runs = new List<(List<string> Words, bool AtStart)>();
        List<string>? run = null;
        var runAtStart = false;
        for (var i = 0; i < words.Count; i++)
        {
            var word = words[i].Value;
            var startsSentence = i == 0 || Gap(plain, words[i - 1], words[i]).IndexOfAny(SentenceBreaks.ToCharArray()) >= 0;
            var joined = i > 0 && run != null && IsSpaceOnly(Gap(plain, words[i - 1], words[i]));
            if (IsCapitalized(word) && !NotNames.Contains(word) && !IsCode(word))
            {
                if (run != null && joined && !EndsPossessive(run[^1]))
                {
                    run.Add(word);
                    continue;
                }

                Close();
                run = new List<string> { word };
                runAtStart = startsSentence;
                continue;
            }

            if (run != null && joined && Joiners.Contains(word) && JoinsName(i))
            {
                run.Add(word);
                continue;
            }

            Close();
        }

        Close();
        foreach (var (name, atStart) in runs.Select(r => (Name(r.AtStart ? Unled(r.Words) : r.Words), r.AtStart)))
        {
            if (name == null) continue;
            if (!name.Contains(' ') && atStart && !inSentence.Contains(name)) continue;
            yield return name;
        }

        // "of the" between "Heroes" and "War": joiners up to a capitalized word, all spaces apart.
        bool JoinsName(int at)
        {
            for (var k = at; k + 1 < words.Count && IsSpaceOnly(Gap(plain, words[k], words[k + 1])); k++)
            {
                if (IsCapitalized(words[k + 1].Value)) return true;
                if (!Joiners.Contains(words[k + 1].Value)) return false;
            }

            return false;
        }

        // A sentence's first word before a name is no part of it: a preposition or a word the texts write in lowercase.
        List<string> Unled(List<string> found)
            => found.Count > 1 && (SentenceWords.Contains(found[0]) || isOrdinary?.Invoke(found[0].ToLowerInvariant()) == true)
                ? found.Skip(1).ToList()
                : found;

        void Close()
        {
            if (run != null)
            {
                runs.Add((run, runAtStart));
                if (!runAtStart && Name(run) is { } name) inSentence.Add(name);
            }

            run = null;
        }
    }

    // The run without its leading "The" or "of", trailing joiners and a final possessive; null when nothing name-like is left.
    private static string? Name(List<string> run)
    {
        var words = run.SkipWhile(w => Leaders.Contains(w) || Joiners.Contains(w)).ToList();
        while (words.Count > 0 && Joiners.Contains(words[^1])) words.RemoveAt(words.Count - 1);
        if (words.Count == 0) return null;
        words[^1] = StripPossessive(words[^1]);
        var name = string.Join(" ", words);
        return name.Length < 3 ? null : name;
    }

    private static bool IsCapitalized(string word) => char.IsUpper(word[0]) && word.Skip(1).Any(char.IsLower);

    // An editor ID or script name a topic or a line shows as it is ("AltertToIdle", "ScopeEffect", "AddToList"): a capital
    // inside after four small letters, two capitals inside, or a script verb before a capital ("GetStage", "OnHit"). A
    // name has one, early (McCarthy, MacDonald, FitzGerald).
    private static readonly Regex CodeWordRegex = new(
        @"[a-z]{4}[A-Z]|[A-Z][a-z]+[A-Z][a-z]*[A-Z]|^(?:Get|Set|Is|Has|On|Play|Move|Add|Remove|Start|Stop)[A-Z]",
        RegexOptions.CultureInvariant);

    private static bool IsCode(string word) => CodeWordRegex.IsMatch(word);

    private static bool EndsPossessive(string word) => word.EndsWith("'s", StringComparison.Ordinal) || word.EndsWith("’s", StringComparison.Ordinal);

    private static string StripPossessive(string word) => EndsPossessive(word) ? word[..^2] : word;

    private static string Gap(string text, Match before, Match after)
        => text.Substring(before.Index + before.Length, after.Index - before.Index - before.Length);

    private static bool IsSpaceOnly(string gap) => gap.Length > 0 && gap.All(ch => ch == ' ');

    /// <summary>
    /// The names in the translated non-name rows that no glossary term, TM line, game text or name field of the mod
    /// has: what lookup would answer "없습니다" for. Rows the TM, game text or a Korean release filled are skipped.
    /// </summary>
    public static async Task<IReadOnlyList<AgentTextName>> FindAsync(ProjectDb db, ProjectDb? global, GameStringDictionary? game,
        IReadOnlyList<StringEntry> rows, CancellationToken cancellationToken)
    {
        var given = new HashSet<long>();
        foreach (var kind in StringNotePolicy.KindsOf(StringNotePolicy.NoteGroup.KnownTranslation, StringNotePolicy.NoteGroup.TmFallback))
        {
            given.UnionWith((await db.GetStringNotesByKindAsync(kind, cancellationToken)).Keys);
        }

        var texts = rows
            .Where(r => r.Status is StringEntryStatus.Done or StringEntryStatus.Edited && !string.IsNullOrWhiteSpace(r.DestText))
            .Where(r => !IsNameField(r.Rec) && !given.Contains(r.Id))
            .ToList();
        if (texts.Count == 0) return Array.Empty<AgentTextName>();

        // What the translator knows, as one text a name is looked for in whole words: the glossaries, the TM and the
        // mod's translated name fields. The game dictionary is asked for the whole name, as lookup asks it.
        var known = new StringBuilder();
        foreach (var source in new[] { db, global }.OfType<ProjectDb>())
        {
            foreach (var g in await source.GetGlossaryAsync(cancellationToken))
            {
                if (g.Enabled) known.Append(g.SourceTerm).Append('\n');
            }
        }

        if (global != null)
        {
            var (sourceLang, targetLang) = await AgentSession.LanguagesAsync(db, cancellationToken);
            foreach (var e in await global.GetTranslationMemoryEntriesAsync(sourceLang, targetLang, cancellationToken)) known.Append(e.SourceText).Append('\n');
        }

        // Only translated name fields: an untranslated one says nothing of how the name is written.
        foreach (var r in rows.Where(r => IsNameField(r.Rec) && r.Status is StringEntryStatus.Done or StringEntryStatus.Edited
                                          && !string.IsNullOrWhiteSpace(r.DestText)))
        {
            known.Append(r.SourceText).Append('\n');
        }
        var knownText = known.ToString();

        // A lone word the texts also write in lowercase ("Order", "Dragon") is an ordinary word capitalized.
        var lowercase = texts.SelectMany(r => WordRegex.Matches(r.SourceText).Select(m => m.Value))
            .Where(w => char.IsLower(w[0])).ToHashSet(StringComparer.Ordinal);
        var found = new Dictionary<string, List<long>>(StringComparer.Ordinal);
        var order = new List<string>();
        foreach (var r in texts)
        {
            foreach (var name in Candidates(r.SourceText, lowercase.Contains).Distinct(StringComparer.Ordinal))
            {
                if (!name.Contains(' ') && lowercase.Contains(name.ToLowerInvariant())) continue;
                if (!found.TryGetValue(name, out var ids))
                {
                    if (IsKnown(name)) continue;
                    found[name] = ids = new List<long>();
                    order.Add(name);
                }

                ids.Add(r.Id);
            }
        }

        return order.Select(n => new AgentTextName(n, found[n])).ToList();

        // A plural ("Dragon Priests") is known by its singular.
        bool IsKnown(string name)
            => ContainsWord(knownText, name) || game?.Find(null, null, name) != null
               || (name.EndsWith('s') && name.Length > 4 && (ContainsWord(knownText, name[..^1]) || game?.Find(null, null, name[..^1]) != null));
    }

    private static bool IsNameField(string? rec) => TranslationRowRules.IsNameField(rec);

    private static bool ContainsWord(string text, string word)
    {
        for (var at = text.IndexOf(word, StringComparison.OrdinalIgnoreCase); at >= 0; at = text.IndexOf(word, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            var before = at == 0 || !char.IsLetterOrDigit(text[at - 1]);
            var end = at + word.Length;
            if (before && (end == text.Length || !char.IsLetterOrDigit(text[end]))) return true;
        }

        return false;
    }
}
