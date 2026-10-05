using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Text.Lqa.Internal.Rules;

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

    // Multi-word names by their lowercase first word: "sweeter than moon sugar" names Moon Sugar (문 슈거). Only names
    // whose first word the translation spells by sound (moon → 문): the lowercase forms of translated names were ordinary
    // phrases in local projects ("served on a silver platter" → 은제 큰 접시, "bad enough to turn undead" → 언데드 퇴치,
    // "send a note" → 노트). One-word names are not matched in lowercase: dirge and maul are ordinary words.
    private readonly Dictionary<string, List<(string Source, string Target)>> _byLowerFirstWord;

    private ReferenceNameIndex(Dictionary<string, List<(string Source, string Target)>> byFirstWord, int count, Material[] materials)
    {
        _byFirstWord = byFirstWord;
        Count = count;
        _materials = materials;
        _byLowerFirstWord = byFirstWord.Values.SelectMany(list => list)
            .Where(name => name.Source.Contains(' ') && IsSoundedInTarget(name.Source.Split(' ')[0], name.Target))
            .Select(name => (Source: name.Source.ToLowerInvariant(), name.Target))
            .Where(name => char.IsLower(name.Source[0]))
            .GroupBy(name => name.Source.Split(' ')[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.OrderByDescending(name => name.Source.Length).ToList(), StringComparer.Ordinal);
    }

    public int Count { get; }

    public static ReferenceNameIndex Build(IEnumerable<(string Source, string Target)> memory)
    {
        var pairs = memory
            .Select(pair => (Source: (pair.Source ?? "").Trim(), Target: SelfContainedCategory.Replace((pair.Target ?? "").Trim(), "")))
            .Where(pair => pair.Source.Length > 0 && pair.Target.Length > 0)
            .ToList();
        pairs.AddRange(BookTitlesWithoutVolume(pairs).ToList());
        pairs.AddRange(NamesInSentences(pairs).ToList());
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

            var targets = group.GroupBy(pair => pair.Target, StringComparer.Ordinal)
                .Select(t => (Target: t.Key, Count: t.Count()))
                .OrderByDescending(t => t.Count)
                .ToList();
            if (targets.Count > 1 && targets[0].Count == targets[1].Count)
            {
                continue;
            }

            // A single word must also be used as a name inside a sentence ("Speak to Erandur"), or be spelled by
            // sound (Cairine → 카이린); item and menu entries such as Slot (장치), Hawk (매) or Honey (벌꿀) are
            // translated words and only ever appear on their own.
            if (!group.Key.Contains(' ')
                && (OrdinaryWords.Contains(group.Key)
                    || !namedInSentences.Contains(FirstWordOf(group.Key)) && !IsSpelledBySound(group.Key, targets[0].Target)))
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

        foreach (var (word, spelling) in WordsOfFullNames(byFirstWord, lowercaseUse, pairs))
        {
            if (!byFirstWord.TryGetValue(word, out var list))
            {
                list = new List<(string Source, string Target)>();
                byFirstWord[word] = list;
            }

            list.Add((word, spelling));
            count++;
        }

        count -= DropNamesTheSentencesSpellDifferently(byFirstWord, pairs);

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
            var replaced = NameOccurrence(source).Replace(text, token);
            if (!string.Equals(replaced, text, StringComparison.Ordinal))
            {
                text = replaced;
                tokens[token] = target;
            }
        }

        text = ForceMaterials(text, tokens, ref number);
        return string.Equals(text, glossed.Text, StringComparison.Ordinal) ? glossed : glossed with { Text = text, TokenToReplacement = tokens };
    }

    /// <summary>
    /// Applies <paramref name="glossary"/> and then the official names. A name the glossary forces whole keeps the
    /// glossary's translation, but a longer official name around a shorter forced term is replaced first: in MEI the
    /// glossary's "Dibella" and "Black-Briar" broke "Agent of Dibella" (디벨라의 사도) and "Black-Briar Lodge"
    /// (블랙-브라이어 가옥) before the memory could see them, and the model wrote 디벨라의 요원 and 블랙-브라이어 산장.
    /// </summary>
    public GlossaryApplication ApplyWithGlossary(string text, GlossaryApplier glossary)
    {
        var glossed = glossary.Apply(text);
        var broken = FindIn(text, max: 16)
            .Where(name => !glossed.Text.Contains(name.Source, StringComparison.Ordinal))
            .Where(name => !WholeTokenRegex.IsMatch(glossary.Apply(name.Source).Text.Trim()))
            .ToList();
        if (broken.Count == 0)
        {
            return ForceNames(glossed);
        }

        var tokens = new Dictionary<string, string>(StringComparer.Ordinal);
        var number = 0;
        foreach (var (source, target) in broken.OrderByDescending(name => name.Source.Length))
        {
            var token = $"__XT_TERM_R{++number}_0000__";
            var replaced = NameOccurrence(source).Replace(text, token);
            if (!string.Equals(replaced, text, StringComparison.Ordinal))
            {
                text = replaced;
                tokens[token] = target;
            }
        }

        glossed = glossary.Apply(text);
        foreach (var (token, target) in glossed.TokenToReplacement)
        {
            tokens[token] = target;
        }

        return ForceNames(glossed with { TokenToReplacement = tokens });
    }

    private static readonly Regex WholeTokenRegex = new(@"^__XT_[A-Z0-9_]+__$", RegexOptions.CultureInvariant);

    private static Regex NameOccurrence(string source)
        => new(@"(?<![A-Za-z'’\-])" + Regex.Escape(source) + @"(?![A-Za-z\-])(?!['’](?!s\b))", RegexOptions.CultureInvariant);

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
            if (word.Index < coveredUntil)
            {
                continue;
            }

            var firstWord = FirstWordOf(word.Value);
            var candidates = char.IsUpper(word.Value[0]) ? _byFirstWord.GetValueOrDefault(firstWord)
                : char.IsLower(word.Value[0]) ? _byLowerFirstWord.GetValueOrDefault(firstWord)
                : null;
            if (candidates == null)
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

    /// <summary>
    /// The words of indexed full names that the official translation spells by sound: "Ingun Black-Briar"
    /// (잉건 블랙-브라이어) gives Ingun → 잉건, "Jarl Balgruuf" (발그루프 영주) gives Balgruuf → 발그루프 but not
    /// Jarl. The memory names many people only in full, so their first names were left to the model (MEI: 인군 36
    /// times). A word with two spellings, an ordinary word, or a word the index already has is left out, and so is a
    /// word the memory does not spell that way in most of its entries: sounds alone also matched Volkihar with
    /// 발코니 and Grotto with 그레이워터, and capitalized ordinary words (Red, Divine, Companion) are translated
    /// differently from entry to entry.
    /// </summary>
    private static IEnumerable<(string Word, string Spelling)> WordsOfFullNames(
        Dictionary<string, List<(string Source, string Target)>> byFirstWord, HashSet<string> lowercaseUse,
        IReadOnlyList<(string Source, string Target)> memory)
    {
        var known = byFirstWord.Values.SelectMany(list => list).Select(name => name.Source).ToHashSet(StringComparer.Ordinal);
        var spellings = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (source, target) in byFirstWord.Values.SelectMany(list => list))
        {
            var words = source.Split(' ');
            if (words.Length < 2)
            {
                continue;
            }

            foreach (var word in words)
            {
                // Materials (Ebony, Glass) are names only inside item names; ForceMaterials handles them.
                if (word.Length < 3 || !char.IsUpper(word[0]) || !word.All(char.IsAsciiLetter) || known.Contains(word)
                    || Materials.Any(material => string.Equals(material.Word, word, StringComparison.Ordinal))
                    || lowercaseUse.Contains(word.ToLowerInvariant()) || OrdinaryWords.Contains(word)
                    || SpellingOf(word, target) is not { } spelling)
                {
                    continue;
                }

                if (!spellings.TryGetValue(word, out var set))
                {
                    set = new HashSet<string>(StringComparer.Ordinal);
                    spellings[word] = set;
                }

                set.Add(spelling);
            }
        }

        var single = spellings.Where(pair => pair.Value.Count == 1).ToDictionary(pair => pair.Key, pair => pair.Value.Single(), StringComparer.Ordinal);
        if (single.Count == 0)
        {
            return Array.Empty<(string, string)>();
        }

        var uses = single.Keys.ToDictionary(word => word, _ => (Total: 0, Spelled: 0), StringComparer.Ordinal);
        foreach (var (source, target) in memory)
        {
            foreach (var word in WordRegex.Matches(source).Select(m => FirstWordOf(m.Value)).Distinct(StringComparer.Ordinal))
            {
                if (uses.TryGetValue(word, out var use))
                {
                    uses[word] = (use.Total + 1, use.Spelled + (target.Contains(single[word], StringComparison.Ordinal) ? 1 : 0));
                }
            }
        }

        // A spelling one sound off (메이븐 for Maven) is accepted only for a word the memory also writes after a
        // lowercase word ("to Maven"); item words like Charming (→ 지팡) and Sugar (→ 슈거) never are.
        var afterLowercase = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (source, _) in memory)
        {
            var words = WordRegex.Matches(source).ToArray();
            for (var i = 1; i < words.Length; i++)
            {
                if (char.IsUpper(words[i].Value[0]) && char.IsLower(words[i - 1].Value[0])
                    && source[(words[i - 1].Index + words[i - 1].Length)..words[i].Index].Trim().Length == 0)
                {
                    afterLowercase.Add(FirstWordOf(words[i].Value));
                }
            }
        }

        return single.Where(pair => uses[pair.Key] is { Total: >= 2 } use && use.Spelled * 5 >= use.Total * 4)
            .Where(pair => SoundsExactly(pair.Key, pair.Value) || afterLowercase.Contains(pair.Key))
            .Select(pair => (pair.Key, pair.Value));
    }

    private static bool SoundsExactly(string word, string spelling)
        => GlossaryLoanwordRule.KoreanSound(spelling) is { } sound
           && (sound == GlossaryLoanwordRule.EnglishSound(word)
               || sound == GlossaryLoanwordRule.EnglishSound(Regex.Replace(word, "ng(?=[aeiouy])", "ngg", RegexOptions.CultureInvariant)));

    /// <summary>
    /// Drops multi-word names that the memory's own sentences (four words or more) spell another way in more than 70%
    /// of at least three uses: its entry "Imperial Legion" says 임페리얼 while 10 of 12 sentences say 제국군, and
    /// "Word of Power" says 힘의 언어 while every sentence says 힘의 단어. 13 of 5,239 names in the official memory.
    /// Runs after the words of full names are taken, so Ingun keeps 잉건 even when "Ingun Black-Briar" goes.
    /// </summary>
    private static int DropNamesTheSentencesSpellDifferently(
        Dictionary<string, List<(string Source, string Target)>> byFirstWord, IReadOnlyList<(string Source, string Target)> memory)
    {
        var names = byFirstWord.Values.SelectMany(list => list).Where(name => name.Source.Contains(' ')).ToList();
        if (names.Count == 0)
        {
            return 0;
        }

        var bySource = names.ToDictionary(name => name.Source, name => (name.Target, Total: 0, Spelled: 0), StringComparer.Ordinal);
        var byFirst = names.GroupBy(name => name.Source.Split(' ')[0], StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(name => name.Source).ToList(), StringComparer.Ordinal);
        foreach (var (source, target) in memory)
        {
            if (source.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length < 4)
            {
                continue;
            }

            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (Match word in WordRegex.Matches(source))
            {
                if (!byFirst.TryGetValue(word.Value, out var candidates))
                {
                    continue;
                }

                foreach (var name in candidates)
                {
                    if (seen.Contains(name) || string.CompareOrdinal(source, word.Index, name, 0, name.Length) != 0
                        || !EndsAtWordBoundary(source, word.Index + name.Length))
                    {
                        continue;
                    }

                    seen.Add(name);
                    var use = bySource[name];
                    bySource[name] = (use.Target, use.Total + 1, use.Spelled + (target.Contains(use.Target, StringComparison.Ordinal) ? 1 : 0));
                }
            }
        }

        var dropped = 0;
        foreach (var list in byFirstWord.Values)
        {
            dropped += list.RemoveAll(name => bySource.TryGetValue(name.Source, out var use) && use.Total >= 3 && use.Spelled * 10 < use.Total * 3);
        }

        return dropped;
    }

    private static readonly Regex VolumeSource = new(@"^(.+?),\s*v(?:ol(?:ume)?\.?)?\s*\d+$", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
    private static readonly Regex VolumeTarget = new(@"^(.+?),?\s*제\s*\d+\s*권$", RegexOptions.CultureInvariant);

    /// <summary>
    /// Book series appear in the memory only by volume ("The Lusty Argonian Maid, v1" → "음란한 아르고니안 메이드, 제 1권"),
    /// which is no name entry, so MEI wrote "Lusty Argonian Maid" as 음탕한 아르고니안 가정부. The title without the
    /// volume is an entry of its own, and without a leading "The" as well.
    /// </summary>
    private static IEnumerable<(string Source, string Target)> BookTitlesWithoutVolume(IEnumerable<(string Source, string Target)> pairs)
    {
        foreach (var (source, target) in pairs)
        {
            var title = VolumeSource.Match(source);
            var korean = VolumeTarget.Match(target);
            if (!title.Success || !korean.Success)
            {
                continue;
            }

            var english = title.Groups[1].Value.Trim();
            var translated = korean.Groups[1].Value.Trim();
            yield return (english, translated);
            if (english.StartsWith("The ", StringComparison.Ordinal))
            {
                yield return (english[4..], translated);
            }
        }
    }

    // A word of the translation sounds exactly like the English word (moon → 문, tomato → 토마토). Unlike FindSpelling,
    // a spelling that is also a native word counts here (문 is "door").
    private static bool IsSoundedInTarget(string word, string target)
    {
        var sound = GlossaryLoanwordRule.EnglishSound(word);
        return sound.Count(c => c != 'V') >= 2
               && Regex.Matches(target, "[가-힣]+").Any(m => GlossaryLoanwordRule.KoreanSound(m.Value) == sound);
    }

    // The whole translation is the word's sound spelling (From-Deepest-Fathoms → 프롬-디피스트-페덤스); words with fewer
    // than two consonant sounds are too short to tell (Erdi → 어디).
    private static bool IsSpelledBySound(string word, string target)
    {
        var spelled = Regex.Replace(target, "[^가-힣]", "");
        return spelled.Length > 0 && string.Equals(RunNameMemory.FindSpelling(word, spelled), spelled, StringComparison.Ordinal);
    }

    // "ng" before a vowel is two sounds in a name: Ingun is 잉건 (ing-geon), not one nasal. A possessive 의 is not
    // part of the name (아카토쉬의 신전).
    private static string? SpellingOf(string word, string target)
    {
        var spelling = RunNameMemory.FindSpelling(word, target)
                       ?? RunNameMemory.FindSpelling(Regex.Replace(word, "ng(?=[aeiouy])", "ngg", RegexOptions.CultureInvariant), target);
        if (spelling is { Length: > 2 } && spelling.EndsWith('의'))
        {
            spelling = spelling[..^1];
        }

        // A whole word of the translation, not the start of one: Charming matched 지팡 in 지팡이.
        return spelling != null && Regex.Matches(target, "[가-힣]+").Any(m => m.Value == spelling || m.Value == spelling + "의")
            ? spelling
            : null;
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
