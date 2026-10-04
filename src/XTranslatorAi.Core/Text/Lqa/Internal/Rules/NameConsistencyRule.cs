using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// Finds one English name written two ways in Korean. Serana Dialogue Add-On spelled player names and
/// its own characters line by line (셀린/셀레네, 아론/애런, 알리스터/알리스테어); names outside the
/// glossary and the translation memory have no reference, so only consistency can be checked.
/// For each capitalized word the project never writes in lowercase, the Korean words of its rows are
/// counted; two similar spellings (by jamo) that both appear mostly in that name's rows are the same name.
/// </summary>
internal static class NameConsistencyRule
{
    private const int MinRows = 3;
    private const int MinMinorityRows = 1;

    private static readonly Regex WordRegex = new(@"[A-Za-z]+(?:['’][A-Za-z]+)*", RegexOptions.CultureInvariant);
    private static readonly Regex HangulWordRegex = new(@"[가-힣]+", RegexOptions.CultureInvariant);

    // Particles and endings that follow a name; stripped to find the name itself.
    private static readonly string[] Suffixes =
    {
        "이라고요", "라고요", "에게서", "한테서", "이라는", "이라고", "이시여", "에서는", "으로서", "으로는", "께서", "시여", "에게", "한테", "이랑", "이라", "이란",
        "이여", "이야", "라는", "라고", "처럼", "보다", "까지", "부터", "에서", "에선", "에는", "으로", "로서", "로는",
        "이나", "은", "는", "이", "가", "을", "를", "의", "와", "과", "도", "만", "에", "엔", "로", "랑", "나", "아", "야", "여", "님", "씨",
    };

    // The suffixes a word can end with, by its last syllable, in the order of Suffixes (which decides Stem).
    private static readonly Dictionary<char, string[]> SuffixesByLastChar = Suffixes
        .GroupBy(suffix => suffix[^1])
        .ToDictionary(group => group.Key, group => group.ToArray());

    // Interjections are capitalized and never written in lowercase in a mod's lines, but they are not names (으윽/으으).
    internal static readonly HashSet<string> Interjections = new(StringComparer.Ordinal)
    {
        "Ugh", "Urgh", "Argh", "Agh", "Gah", "Bah", "Hah", "Heh", "Hmm", "Hmph", "Huh", "Aww", "Ahh", "Ooh", "Oof", "Ouch", "Wow", "Whoa",
        "Yay", "Yeah", "Yep", "Nope", "Mhm", "Mmm", "Hey", "Haha", "Hehe", "Pfft", "Psst", "Shh", "Tsk", "Eek", "Yikes", "Gods",
    };

    // Verb forms are not spellings of a name: 소환하고/소환하여 for "Summon".
    private static readonly Regex VerbEnding = new(@"(?:하고|하여|하며|하면|하는|하기|해서|했다|합니다|한다|되어|되는|된다)$", RegexOptions.CultureInvariant);

    public static Dictionary<long, string> Build(IReadOnlyList<LqaScanEntry> entries, bool isKorean)
    {
        var findings = new Dictionary<long, string>();
        if (!isKorean)
        {
            return findings;
        }

        var rows = entries
            .Where(e => e.Status is StringEntryStatus.Done or StringEntryStatus.Edited && !string.IsNullOrWhiteSpace(e.DestText))
            .ToList();
        var lowercaseWords = new HashSet<string>(StringComparer.Ordinal);
        var rowsByName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        var titleRowsByName = new Dictionary<string, List<int>>(StringComparer.Ordinal);
        for (var i = 0; i < rows.Count; i++)
        {
            var source = LqaScanner.StripUiTokens(rows[i].SourceText);
            var matches = WordRegex.Matches(source);
            var titleRow = IsTitleCaseRow(matches);
            for (var m = 0; m < matches.Count; m++)
            {
                var match = matches[m];
                var word = match.Value.EndsWith("'s", StringComparison.Ordinal) || match.Value.EndsWith("’s", StringComparison.Ordinal)
                    ? match.Value[..^2]
                    : match.Value;
                if (char.IsLower(word[0]))
                {
                    lowercaseWords.Add(word);
                }
                else if (word.Length >= 3 && word.Skip(1).All(char.IsLower) && !Interjections.Contains(word) && (!titleRow || IsGreetedName(matches, m)))
                {
                    var byName = titleRow ? titleRowsByName : rowsByName;
                    if (!byName.TryGetValue(word, out var list))
                    {
                        list = new List<int>();
                        byName[word] = list;
                    }

                    if (list.Count == 0 || list[^1] != i)
                    {
                        list.Add(i);
                    }
                }
            }
        }

        // A word used as a name in a sentence or on its own is a name when a short title-case row ends with it:
        // Serana Dialogue Add-On greets each player name ("Hey Drelorea!") and spelled it 드렐로레아 there but
        // 드렐로리아 in its lines. Item names put the word first ("Daedric Armor", "Guard Tower") and stay out.
        foreach (var (name, titleRows) in titleRowsByName)
        {
            if (rowsByName.TryGetValue(name, out var list))
            {
                list.AddRange(titleRows);
                list.Sort();
            }
        }

        // Korean words are numbered once so that counting them per name is array work. Book-heavy projects have
        // hundreds of thousands of distinct words and a common name appears in most rows; with string
        // dictionaries this rule took over 30 s (3,000 synthetic book rows) before the scan could report progress.
        var index = new CoreIndex(rows);

        // Rows of the current name that contain each word; reset after every name. The first-seen order of the
        // words breaks ties between spellings that are equally frequent and equally long.
        var counts = new int[index.Count];
        var seen = new List<int>();
        foreach (var (name, nameRows) in rowsByName)
        {
            if (nameRows.Count < MinRows || lowercaseWords.Contains(name.ToLowerInvariant()))
            {
                continue;
            }

            foreach (var row in nameRows)
            {
                foreach (var core in index.RowCores[row])
                {
                    if (counts[core]++ == 0)
                    {
                        seen.Add(core);
                    }
                }
            }

            AddFindings(name, nameRows, rows, index, counts, seen, findings);

            foreach (var core in seen)
            {
                counts[core] = 0;
            }

            seen.Clear();
        }

        return findings;
    }

    private static void AddFindings(
        string name,
        List<int> nameRows,
        List<LqaScanEntry> rows,
        CoreIndex index,
        int[] counts,
        List<int> seen,
        Dictionary<long, string> findings)
    {
        // Korean words found mostly in this name's rows are the name's spellings. A word with a particle
        // ("컬런이") counts as its stem when the stem appears on its own more often.
        bool IsSpelling(int core)
        {
            var word = index.Words[core];
            var value = counts[core];
            if (word.Length < 2 || value < MinMinorityRows || (double)value / index.RowCounts[core] < 0.6 || VerbEnding.IsMatch(word))
            {
                return false;
            }

            foreach (var suffix in SuffixesEndingWith(word[^1]))
            {
                if (word.Length > suffix.Length && word.EndsWith(suffix, StringComparison.Ordinal))
                {
                    var stem = index.Find(word[..^suffix.Length]);
                    if (stem >= 0 && counts[stem] > value)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        bool Outranks(int core, int than)
            => counts[core] > counts[than] || (counts[core] == counts[than] && index.Words[core].Length > index.Words[than].Length);

        // The main spelling: the most frequent, then the longest, then the first seen.
        var main = -1;
        foreach (var core in seen)
        {
            if ((main < 0 || Outranks(core, main)) && IsSpelling(core))
            {
                main = core;
            }
        }

        if (main < 0 || counts[main] < 2 || !SoundsLike(name, index.Words[main]))
        {
            return;
        }

        var mainWord = index.Words[main];
        var mainValue = counts[main];
        var mainStem = Stem(mainWord);
        var mainJamo = Jamo(mainWord);

        // Cheap tests first: nearly all words of a common name's rows are rare words that start with
        // another sound, and only the few left need the suffix and edit-distance checks.
        var others = new List<(int Core, int Seen)>();
        for (var i = 0; i < seen.Count; i++)
        {
            var core = seen[i];
            var word = index.Words[core];
            if (core == main
                || word.Length < 2
                || counts[core] > mainValue / 2 + 1
                || !SoundsLike(name, word)
                || mainWord.Contains(word, StringComparison.Ordinal) || word.Contains(mainWord, StringComparison.Ordinal)
                || !IsSpelling(core)
                || Stem(word) == mainStem
                || !AreSimilar(mainJamo, Jamo(word)))
            {
                continue;
            }

            others.Add((core, i));
        }

        // Same order as a list of spellings sorted by frequency, then length, then first appearance.
        others.Sort((a, b) =>
        {
            var byCount = counts[b.Core].CompareTo(counts[a.Core]);
            if (byCount != 0)
            {
                return byCount;
            }

            var byLength = index.Words[b.Core].Length.CompareTo(index.Words[a.Core].Length);
            return byLength != 0 ? byLength : a.Seen.CompareTo(b.Seen);
        });

        foreach (var (other, _) in others)
        {
            foreach (var row in nameRows)
            {
                if (index.RowContains(row, other) && !index.RowContains(row, main))
                {
                    findings.TryAdd(rows[row].Id,
                        $"같은 이름을 다르게 썼습니다: {name} → '{Display(index.Words[other])}' (다른 {mainValue}행은 '{Display(mainWord)}')");
                }
            }
        }
    }

    private static string[] SuffixesEndingWith(char last)
        => SuffixesByLastChar.TryGetValue(last, out var suffixes) ? suffixes : Array.Empty<string>();

    /// <summary>
    /// Every Korean word of the rows and its forms without a particle ("메로베흐에게" → "메로베흐"), numbered.
    /// </summary>
    private sealed class CoreIndex
    {
        private readonly Dictionary<string, int> _ids = new(StringComparer.Ordinal);
        private readonly int[][] _sortedRowCores;

        public List<string> Words { get; } = new();

        // Number of rows that contain each word.
        public List<int> RowCounts { get; } = new();

        // Each row's distinct words in first-seen order.
        public int[][] RowCores { get; }

        public int Count => Words.Count;

        public CoreIndex(List<LqaScanEntry> rows)
        {
            RowCores = new int[rows.Count][];
            _sortedRowCores = new int[rows.Count][];
            var rowCores = new List<int>();
            var inRow = new HashSet<int>();
            for (var row = 0; row < rows.Count; row++)
            {
                foreach (Match match in HangulWordRegex.Matches(rows[row].DestText))
                {
                    var word = match.Value;
                    Add(word);
                    foreach (var suffix in SuffixesEndingWith(word[^1]))
                    {
                        if (word.Length - suffix.Length >= 2 && word.EndsWith(suffix, StringComparison.Ordinal))
                        {
                            Add(word[..^suffix.Length]);
                        }
                    }
                }

                foreach (var core in rowCores)
                {
                    RowCounts[core]++;
                }

                RowCores[row] = rowCores.ToArray();
                _sortedRowCores[row] = rowCores.ToArray();
                Array.Sort(_sortedRowCores[row]);
                rowCores.Clear();
                inRow.Clear();
            }

            void Add(string core)
            {
                if (!_ids.TryGetValue(core, out var id))
                {
                    id = Words.Count;
                    _ids[core] = id;
                    Words.Add(core);
                    RowCounts.Add(0);
                }

                if (inRow.Add(id))
                {
                    rowCores.Add(id);
                }
            }
        }

        public int Find(string word) => _ids.TryGetValue(word, out var id) ? id : -1;

        public bool RowContains(int row, int core) => Array.BinarySearch(_sortedRowCores[row], core) >= 0;
    }

    private static bool IsGreetedName(MatchCollection words, int index) => words.Count <= 3 && index == words.Count - 1 && index > 0;

    // Titles capitalize ordinary words ("Horror Sign", "The End Maneuver"), so their words are not taken for
    // names. A sentence ("Ask Merovech about the ship.") or a lone name ("Selene.") is used.
    private static bool IsTitleCaseRow(MatchCollection words)
    {
        var content = words.Select(w => w.Value).Where(w => w.Length > 3 || char.IsUpper(w[0])).ToList();
        return content.Count >= 2 && content.All(w => char.IsUpper(w[0]));
    }

    public static void Apply(LqaScanEntry entry, IReadOnlyDictionary<long, string> findings, List<LqaIssue> issues)
    {
        if (!findings.TryGetValue(entry.Id, out var message))
        {
            return;
        }

        issues.Add(new LqaIssue(entry.Id, entry.OrderIndex, entry.Edid, entry.Rec, "Warn", "name_inconsistent", message,
            entry.SourceText ?? "", entry.DestText ?? ""));
    }

    private const string Initials = "ㄱㄲㄴㄷㄸㄹㅁㅂㅃㅅㅆㅇㅈㅉㅊㅋㅌㅍㅎ";

    // A Korean spelling of a name starts with the sound of its English first letter (Merovech → 메, Brelyna → 브).
    // Translated words do not: "Sun" next to 보물, "Rim" next to 착용하면.
    private static bool SoundsLike(string name, string spelling)
    {
        var code = spelling[0] - 0xAC00;
        if (code < 0 || code > 11171)
        {
            return false;
        }

        var initial = Initials[code / 588];
        var expected = char.ToLowerInvariant(name[0]) switch
        {
            'a' or 'e' or 'i' or 'o' or 'u' or 'y' or 'w' => "ㅇ",
            'b' or 'v' => "ㅂㅃ",
            'c' => "ㅋㄱㅅㅊ",
            'd' => "ㄷㄸ",
            'f' or 'p' => "ㅍ",
            'g' => "ㄱㄲㅈ",
            'h' => "ㅎ",
            'j' => "ㅈ",
            'k' or 'q' => "ㅋㄱㄲ",
            'l' or 'r' => "ㄹ",
            'm' => "ㅁ",
            'n' => "ㄴ",
            's' => "ㅅㅆ",
            't' => "ㅌㄷㅅ",
            'x' or 'z' => "ㅈㅅ",
            _ => "",
        };
        return expected.Contains(initial);
    }

    // Particles that never end a name, dropped for the message: "브릴리나는" → "브릴리나".
    private static readonly string[] DisplaySuffixes = { "에게서", "에게", "한테", "께서", "이랑", "처럼", "보다", "까지", "부터", "은", "는", "을", "를", "의", "와", "과", "도", "만" };

    private static string Display(string word)
    {
        foreach (var suffix in DisplaySuffixes)
        {
            if (word.Length - suffix.Length >= 2 && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                return word[..^suffix.Length];
            }
        }

        return word;
    }

    // "넌은" and "넌을" are one name with different particles, even for a one-syllable name.
    private static string Stem(string word)
    {
        if (word.Length == 0)
        {
            return word;
        }

        foreach (var suffix in SuffixesEndingWith(word[^1]))
        {
            if (word.Length > suffix.Length && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                return word[..^suffix.Length];
            }
        }

        return word;
    }

    // Spellings of one name differ in a vowel or a final consonant: 셀린/셀레네, 아론/애런.
    // Both arguments are already split into jamo.
    private static bool AreSimilar(string x, string y)
    {
        if (x.Length == 0 || y.Length == 0 || x[0] != y[0])
        {
            return false;
        }

        return (double)Distance(x, y) / Math.Max(x.Length, y.Length) <= 0.4;
    }

    private static string Jamo(string text)
    {
        var chars = new List<char>();
        foreach (var ch in text)
        {
            var code = ch - 0xAC00;
            if (code < 0 || code > 11171)
            {
                continue;
            }

            chars.Add((char)(0x1100 + code / 588));
            chars.Add((char)(0x1161 + code % 588 / 28));
            if (code % 28 != 0)
            {
                chars.Add((char)(0x11A7 + code % 28));
            }
        }

        return new string(chars.ToArray());
    }

    private static int Distance(string a, string b)
    {
        var previous = Enumerable.Range(0, b.Length + 1).ToArray();
        for (var i = 1; i <= a.Length; i++)
        {
            var current = new int[b.Length + 1];
            current[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                current[j] = Math.Min(Math.Min(current[j - 1] + 1, previous[j] + 1), previous[j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
            }

            previous = current;
        }

        return previous[b.Length];
    }
}
