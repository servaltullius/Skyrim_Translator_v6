using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Official names from the franchise translation memory, written into the names a row mentions. The memory
/// itself only applies when a whole row matches, so names inside sentences were left to the model: Serana
/// Dialogue Add-On came back with 에란두르 (Erandur → 에란더), 키케로 (Cicero → 시세로) and 사역마 소환
/// (Conjure Familiar → 늑대 소환), and 232 of its 593 name fixes were such memory names. Offered only as
/// reference terms, the model still kept 에란두르 in most Erandur lines (official names 57 of 67 in a 60-row
/// sample), so names are replaced by term tokens like forced glossary entries.
/// </summary>
public sealed partial class ReferenceNameIndex
{
    private const int MaxNameLength = 40;
    private const int MaxTargetLength = 30;

    // One to four words; the first and last capitalized, inner words capitalized or a short connector.
    private static readonly Regex NamePattern = new(
        @"^[A-Z][A-Za-z]*(?:['’-][A-Za-z]+)*(?: (?:(?:of|the|and)|[A-Z][A-Za-z]*(?:['’-][A-Za-z]+)*)){0,2}(?: [A-Z][A-Za-z]*(?:['’-][A-Za-z]+)*)?$",
        RegexOptions.CultureInvariant
    );

    // A name, not an objective ("에스번을 찾기") or a sentence. Hyphens join words as in 블랙-브라이어.
    // Inventory names carry a category ("아뮬렛 - 마라", "열쇠 - 블랙-브라이어 저택") that does not belong in a sentence.
    private static readonly Regex TargetPattern = new(@"^[가-힣]+(?:(?: ?· ?| |-)[가-힣]+)*$", RegexOptions.CultureInvariant);
    private static readonly Regex ObjectiveOrSentence = new(@"(?:을|를|에게|에서|으로|로|와|과)\s|(?:기|다|요|오|까)$", RegexOptions.CultureInvariant);

    // Categories whose remaining name still says everything: "술 - 허닝브루 벌꿀술" names Honningbrew Mead and
    // "보석 - 완벽한 다이아몬드" a Flawless Diamond. Other categories carry part of the name ("열쇠 - 버려진 감옥" is
    // the Abandoned Prison Key, "반지 - 하급 궁술" a Ring of Archery), so those entries stay out.
    private static readonly Regex SelfContainedCategory = new(@"^(?:술|보석) - (?=[가-힣])", RegexOptions.CultureInvariant);

    // Ordinary words the memory also uses as names: the Brotherhood (형제), an Imperial (임페리얼) next to
    // "Imperial song", the city of Anvil next to the smithing anvil (모루), the Flames spell, a Keeper.
    private static readonly HashSet<string> OrdinaryWords = new(StringComparer.Ordinal) { "Brotherhood", "Imperial", "Anvil", "Flames", "Keeper" };

    private static readonly Regex WordRegex = new(@"[A-Za-z]+(?:['’-][A-Za-z]+)*", RegexOptions.CultureInvariant);

    private readonly Dictionary<string, List<(string Source, string Target)>> _byFirstWord;
    private readonly Material[] _materials;

    private ReferenceNameIndex(Dictionary<string, List<(string Source, string Target)>> byFirstWord, int count, Material[] materials)
    {
        _byFirstWord = byFirstWord;
        Count = count;
        _materials = materials;
    }

    public int Count { get; }

    public static ReferenceNameIndex Build(IEnumerable<(string Source, string Target)> memory)
    {
        var pairs = memory
            .Select(pair => (Source: (pair.Source ?? "").Trim(), Target: SelfContainedCategory.Replace((pair.Target ?? "").Trim(), "")))
            .Where(pair => pair.Source.Length > 0 && pair.Target.Length > 0)
            .ToList();
        var lowercaseUse = CollectLowercaseUse(pairs.Select(pair => pair.Source));
        var namedInSentences = CollectWordsNamedInSentences(pairs.Select(pair => pair.Source));

        var byFirstWord = new Dictionary<string, List<(string Source, string Target)>>(StringComparer.Ordinal);
        var count = 0;
        foreach (var group in pairs.Where(IsNameEntry).GroupBy(pair => pair.Source, StringComparer.Ordinal))
        {
            // A word or phrase the memory also uses in lowercase is an ordinary word: Ghost, Wolf, Fine, Flames.
            if (lowercaseUse.Contains(group.Key.ToLowerInvariant()))
            {
                continue;
            }

            // A single word must also be used as a name inside a sentence ("Speak to Erandur"); item and menu
            // entries such as Slot (장치), Hawk (매) or Honey (벌꿀) only ever appear on their own.
            if (!group.Key.Contains(' ') && (!namedInSentences.Contains(FirstWordOf(group.Key)) || OrdinaryWords.Contains(group.Key)))
            {
                continue;
            }

            var targets = group.GroupBy(pair => pair.Target, StringComparer.Ordinal)
                .Select(t => (Target: t.Key, Count: t.Count()))
                .OrderByDescending(t => t.Count)
                .ToList();
            if (targets.Count > 1 && targets[0].Count == targets[1].Count)
            {
                continue;
            }

            var firstWord = FirstWordOf(group.Key.Split(' ')[0]);
            if (!byFirstWord.TryGetValue(firstWord, out var list))
            {
                list = new List<(string Source, string Target)>();
                byFirstWord[firstWord] = list;
            }

            list.Add((group.Key, targets[0].Target));
            count++;
        }

        foreach (var list in byFirstWord.Values)
        {
            list.Sort((a, b) => b.Source.Length.CompareTo(a.Source.Length));
        }

        return new ReferenceNameIndex(byFirstWord, count, ConfirmedMaterials(pairs));
    }

    /// <summary>
    /// Replaces the names in <paramref name="glossed"/>, and the materials of item names the memory does not hold
    /// whole, with term tokens for their official translation. The glossary ran first, so the names it forces
    /// are already tokens and keep the glossary's translation.
    /// </summary>
    public GlossaryApplication ForceNames(GlossaryApplication glossed)
    {
        var names = FindIn(glossed.Text, max: 16);
        if (names.Count == 0 && _materials.Length == 0)
        {
            return glossed;
        }

        var text = glossed.Text;
        var tokens = new Dictionary<string, string>(glossed.TokenToReplacement, StringComparer.Ordinal);
        var number = 0;
        foreach (var (source, target) in names.OrderByDescending(name => name.Source.Length))
        {
            var token = $"__XT_TERM_N{++number}_0000__";
            var replaced = Regex.Replace(text, @"(?<![A-Za-z'’\-])" + Regex.Escape(source) + @"(?![A-Za-z\-])(?!['’](?!s\b))", token,
                RegexOptions.CultureInvariant);
            if (!string.Equals(replaced, text, StringComparison.Ordinal))
            {
                text = replaced;
                tokens[token] = target;
            }
        }

        text = ForceMaterials(text, tokens, ref number);
        return string.Equals(text, glossed.Text, StringComparison.Ordinal) ? glossed : glossed with { Text = text, TokenToReplacement = tokens };
    }

    /// <summary>Names written exactly as in the memory (capitalized) in <paramref name="text"/>, longest first, without overlaps.</summary>
    public IReadOnlyList<(string Source, string Target)> FindIn(string text, int max = 8)
    {
        var found = new List<(string Source, string Target)>();
        if (string.IsNullOrEmpty(text) || _byFirstWord.Count == 0)
        {
            return found;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var coveredUntil = 0;
        foreach (Match word in WordRegex.Matches(text))
        {
            if (word.Index < coveredUntil || !char.IsUpper(word.Value[0]))
            {
                continue;
            }

            var firstWord = FirstWordOf(word.Value);
            if (!_byFirstWord.TryGetValue(firstWord, out var candidates))
            {
                continue;
            }

            foreach (var candidate in candidates)
            {
                if (string.CompareOrdinal(text, word.Index, candidate.Source, 0, candidate.Source.Length) != 0
                    || !EndsAtWordBoundary(text, word.Index + candidate.Source.Length))
                {
                    continue;
                }

                coveredUntil = word.Index + candidate.Source.Length;
                if (seen.Add(candidate.Source))
                {
                    found.Add(candidate);
                }

                break;
            }

            if (found.Count >= max)
            {
                break;
            }
        }

        return found;
    }

    private static bool IsNameEntry((string Source, string Target) pair)
        => pair.Source.Length <= MaxNameLength && pair.Target.Length <= MaxTargetLength
           && NamePattern.IsMatch(pair.Source) && TargetPattern.IsMatch(pair.Target) && !ObjectiveOrSentence.IsMatch(pair.Target);

    // Lowercase words and two- to four-word lowercase phrases the memory uses, e.g. "ghost" or "dragon priest".
    private static HashSet<string> CollectLowercaseUse(IEnumerable<string> sources)
    {
        var used = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var words = WordRegex.Matches(source).Select(m => m.Value).ToArray();
            for (var i = 0; i < words.Length; i++)
            {
                if (!char.IsLower(words[i][0]))
                {
                    continue;
                }

                var phrase = words[i];
                used.Add(phrase);
                for (var n = 1; n < 4 && i + n < words.Length && char.IsLower(words[i + n][0]); n++)
                {
                    phrase += " " + words[i + n];
                    used.Add(phrase);
                }
            }
        }

        return used;
    }

    // Capitalized words between lowercase words, not at the start of a line or a sentence: "Speak to Erandur".
    private static HashSet<string> CollectWordsNamedInSentences(IEnumerable<string> sources)
    {
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var words = WordRegex.Matches(source).ToArray();
            for (var i = 1; i < words.Length; i++)
            {
                var word = words[i].Value;
                if (!char.IsUpper(word[0]) || !char.IsLower(words[i - 1].Value[0]))
                {
                    continue;
                }

                var between = source[(words[i - 1].Index + words[i - 1].Length)..words[i].Index];
                var nextIsCapitalized = i + 1 < words.Length && char.IsUpper(words[i + 1].Value[0])
                                        && source[(words[i].Index + words[i].Length)..words[i + 1].Index] == " ";
                if (between.Trim().Length == 0 && !nextIsCapitalized)
                {
                    named.Add(FirstWordOf(word));
                }
            }
        }

        return named;
    }

    private static string FirstWordOf(string word)
    {
        var end = word.IndexOfAny(new[] { '\'', '’' });
        return end > 0 && word.Length > end + 1 && word[(end + 1)..] is "s" ? word[..end] : word;
    }

    // "Erandur's goddess" still names Erandur.
    private static bool EndsAtWordBoundary(string text, int end)
    {
        if (end >= text.Length || !char.IsAsciiLetter(text[end]) && text[end] is not ('\'' or '’' or '-'))
        {
            return true;
        }

        return text[end] is '\'' or '’'
               && end + 1 < text.Length && text[end + 1] == 's'
               && (end + 2 >= text.Length || !char.IsAsciiLetter(text[end + 2]));
    }
}
