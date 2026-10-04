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
        "에게서", "한테서", "이라는", "이시여", "께서", "시여", "에게", "한테", "이랑", "이라", "이란", "이여", "이야", "라는", "처럼", "보다", "까지", "부터",
        "이나", "은", "는", "이", "가", "을", "를", "의", "와", "과", "도", "만", "에", "로", "랑", "나", "아", "야", "여", "님", "씨",
    };

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
                else if (word.Length >= 3 && word.Skip(1).All(char.IsLower) && (!titleRow || IsGreetedName(matches, m)))
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

        var coresByRow = new Dictionary<int, HashSet<string>>();
        HashSet<string> CoresOf(int row)
        {
            if (!coresByRow.TryGetValue(row, out var cores))
            {
                cores = new HashSet<string>(StringComparer.Ordinal);
                foreach (Match word in HangulWordRegex.Matches(rows[row].DestText))
                {
                    cores.Add(word.Value);
                    foreach (var suffix in Suffixes)
                    {
                        if (word.Value.Length - suffix.Length >= 2 && word.Value.EndsWith(suffix, StringComparison.Ordinal))
                        {
                            cores.Add(word.Value[..^suffix.Length]);
                        }
                    }
                }

                coresByRow[row] = cores;
            }

            return cores;
        }

        var rowsByCore = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in Enumerable.Range(0, rows.Count))
        {
            foreach (var core in CoresOf(row))
            {
                rowsByCore[core] = rowsByCore.GetValueOrDefault(core) + 1;
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

        foreach (var (name, nameRows) in rowsByName)
        {
            if (nameRows.Count < MinRows || lowercaseWords.Contains(name.ToLowerInvariant()))
            {
                continue;
            }

            // Korean words found mostly in this name's rows: the name's spellings.
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var row in nameRows)
            {
                foreach (var core in CoresOf(row))
                {
                    counts[core] = counts.GetValueOrDefault(core) + 1;
                }
            }

            // A word with a particle ("컬런이") counts as its stem when the stem appears on its own as well.
            var spellings = counts
                .Where(c => c.Key.Length >= 2 && c.Value >= MinMinorityRows && (double)c.Value / rowsByCore[c.Key] >= 0.6)
                .Where(c => !Suffixes.Any(suffix => c.Key.Length > suffix.Length && c.Key.EndsWith(suffix, StringComparison.Ordinal)
                                                    && counts.GetValueOrDefault(c.Key[..^suffix.Length]) > c.Value))
                .OrderByDescending(c => c.Value).ThenByDescending(c => c.Key.Length)
                .ToList();
            if (spellings.Count < 2)
            {
                continue;
            }

            var main = spellings[0];
            if (main.Value < 2)
            {
                continue;
            }

            foreach (var other in spellings.Skip(1))
            {
                if (other.Value > main.Value / 2 + 1
                    || main.Key.Contains(other.Key, StringComparison.Ordinal) || other.Key.Contains(main.Key, StringComparison.Ordinal)
                    || Stem(main.Key) == Stem(other.Key)
                    || !SoundsLike(name, main.Key) || !SoundsLike(name, other.Key)
                    || !AreSimilar(main.Key, other.Key))
                {
                    continue;
                }

                foreach (var row in nameRows.Where(r => CoresOf(r).Contains(other.Key) && !CoresOf(r).Contains(main.Key)))
                {
                    findings.TryAdd(rows[row].Id, $"같은 이름을 다르게 썼습니다: {name} → '{Display(other.Key)}' (다른 {main.Value}행은 '{Display(main.Key)}')");
                }
            }
        }

        return findings;
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
        foreach (var suffix in Suffixes)
        {
            if (word.Length > suffix.Length && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                return word[..^suffix.Length];
            }
        }

        return word;
    }

    // Spellings of one name differ in a vowel or a final consonant: 셀린/셀레네, 아론/애런.
    private static bool AreSimilar(string a, string b)
    {
        var x = Jamo(a);
        var y = Jamo(b);
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
