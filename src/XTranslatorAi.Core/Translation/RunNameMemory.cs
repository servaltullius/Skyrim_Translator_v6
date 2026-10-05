using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Text.Lqa.Internal.Rules;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Remembers how a run spells the names it translates, so later rows write each name the same way. Names
/// without an official translation are left to the model row by row, and Serana Dialogue Add-On spelled its
/// player names one way in greetings and another in lines (드렐로레아/드렐로리아, 벨레트/벨레스); three reviews
/// fixed about 120 such rows.
/// A name is a capitalized word the run never writes in lowercase, used inside a sentence, on its own
/// ("Drelorea.") or at the end of a short greeting ("Hey Drelorea!"). Its spelling is the Korean word of a
/// translated row that sounds like it (see <see cref="GlossaryLoanwordRule.EnglishSound"/>); translated names
/// such as 그림자 속을 헤엄치는 자 have no such word and are not learned. Spellings come from the project's
/// translated rows (the most common one) or from the first row of the run, and later rows get them as term tokens.
/// </summary>
internal sealed class RunNameMemory
{
    private static readonly Regex WordRegex = new(@"[A-Za-z]+(?:['’][A-Za-z]+)*", RegexOptions.CultureInvariant);
    private static readonly Regex HangulWordRegex = new(@"[가-힣]+", RegexOptions.CultureInvariant);
    private static readonly Regex NameRegex = new(@"^[A-Z][a-z]{2,}$", RegexOptions.CultureInvariant);

    // Particles and vocatives after a name: 드렐로레아는, 벨레트야, 메로벡에게.
    private static readonly string[] Suffixes =
    {
        "에게서", "한테서", "이라는", "이라고", "께서", "라고", "에게", "한테", "이랑", "이라", "이란", "이여", "이야", "라는", "처럼", "보다", "까지", "부터", "으로",
        "이나", "은", "는", "이", "가", "을", "를", "의", "와", "과", "도", "만", "에", "로", "랑", "나", "아", "야", "여", "님", "씨",
    };

    private readonly HashSet<string> _names;
    private readonly ConcurrentDictionary<string, (string Target, string Token)> _spellings = new(StringComparer.Ordinal);
    private int _nextToken;

    private RunNameMemory(HashSet<string> names) => _names = names;

    public int NameCount => _names.Count;

    public bool IsEmpty => _spellings.IsEmpty;

    public IReadOnlyDictionary<string, string> Spellings => _spellings.ToDictionary(kv => kv.Key, kv => kv.Value.Target, StringComparer.Ordinal);

    public static RunNameMemory Build(IEnumerable<string> sources)
    {
        var lowercase = new HashSet<string>(StringComparer.Ordinal);
        var named = new HashSet<string>(StringComparer.Ordinal);
        foreach (var source in sources)
        {
            var text = LqaScanner.StripUiTokens(source ?? "");
            var words = WordRegex.Matches(text);
            for (var i = 0; i < words.Count; i++)
            {
                var word = StripPossessive(words[i].Value);
                if (char.IsLower(word[0]))
                {
                    lowercase.Add(word);
                }
                else if (NameRegex.IsMatch(word) && !NameConsistencyRule.Interjections.Contains(word) && IsNamedPosition(text, words, i))
                {
                    named.Add(word);
                }
            }
        }

        named.RemoveWhere(name => lowercase.Contains(name.ToLowerInvariant()));
        return new RunNameMemory(named);
    }

    /// <summary>Takes the most common spelling of each name in rows the project has already translated.</summary>
    public void Preload(IEnumerable<(string Source, string Dest)> rows)
    {
        var counts = new Dictionary<string, Dictionary<string, int>>(StringComparer.Ordinal);
        foreach (var (source, dest) in rows)
        {
            foreach (var (name, spelling) in FindSpellings(source, dest))
            {
                if (!counts.TryGetValue(name, out var bySpelling))
                {
                    bySpelling = new Dictionary<string, int>(StringComparer.Ordinal);
                    counts[name] = bySpelling;
                }

                bySpelling[spelling] = bySpelling.GetValueOrDefault(spelling) + 1;
            }
        }

        foreach (var (name, bySpelling) in counts)
        {
            var ordered = bySpelling.OrderByDescending(kv => kv.Value).ToList();
            if (ordered.Count == 1 || ordered[0].Value > ordered[1].Value)
            {
                Add(name, ordered[0].Key);
            }
        }
    }

    /// <summary>Remembers the spellings of a row the run has just translated; the first spelling of a name stays.</summary>
    public void Learn(string source, string dest)
    {
        foreach (var (name, spelling) in FindSpellings(source, dest))
        {
            Add(name, spelling);
        }
    }

    /// <summary>Replaces the remembered names in <paramref name="glossed"/> with term tokens for their spelling.</summary>
    public GlossaryApplication Force(GlossaryApplication glossed)
    {
        if (_spellings.IsEmpty || string.IsNullOrEmpty(glossed.Text))
        {
            return glossed;
        }

        TokenValidator.SplitByTokens(glossed.Text, out var texts, out var tokens);
        var used = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var i = 0; i < texts.Count; i++)
        {
            texts[i] = WordRegex.Replace(texts[i], m =>
            {
                var word = StripPossessive(m.Value);
                if (!_spellings.TryGetValue(word, out var entry) || IsJoinedByHyphen(texts[i], m.Index, m.Length)
                    || IsPartOfLongerName(texts[i], m))
                {
                    return m.Value;
                }

                used[entry.Token] = entry.Target;
                return entry.Token + m.Value[word.Length..];
            });
        }

        if (used.Count == 0)
        {
            return glossed;
        }

        var tokenToReplacement = new Dictionary<string, string>(glossed.TokenToReplacement, StringComparer.Ordinal);
        foreach (var (token, target) in used)
        {
            tokenToReplacement[token] = target;
        }

        return glossed with { Text = TokenValidator.JoinTextAndTokens(texts, tokens), TokenToReplacement = tokenToReplacement };
    }

    private IEnumerable<(string Name, string Spelling)> FindSpellings(string source, string dest)
    {
        if (string.IsNullOrEmpty(source) || string.IsNullOrEmpty(dest))
        {
            yield break;
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (Match match in WordRegex.Matches(source))
        {
            var word = StripPossessive(match.Value);
            if (_names.Contains(word) && seen.Add(word) && FindSpelling(word, dest) is { } spelling)
            {
                yield return (word, spelling);
            }
        }
    }

    /// <summary>
    /// The Korean word of <paramref name="dest"/> that sounds like <paramref name="name"/>: the same sound, or one
    /// sound off for a longer name (벨레스 for Byleth), and no other word as close.
    /// </summary>
    internal static string? FindSpelling(string name, string dest)
    {
        var sound = GlossaryLoanwordRule.EnglishSound(name);
        if (sound.Count(c => c != 'V') < 2)
        {
            return null;
        }

        var allowed = sound.Length >= 5 ? 1 : 0;
        string? best = null;
        var bestDistance = int.MaxValue;
        var tie = false;
        foreach (var stem in HangulWordRegex.Matches(dest).Select(m => m.Value).SelectMany(Stems).Distinct(StringComparer.Ordinal))
        {
            var stemSound = stem.Length >= 2 ? GlossaryLoanwordRule.KoreanSound(stem) : null;
            // 으으 has no sound at all (no consonant, and ㅡ is not counted).
            if (stemSound is not { Length: > 0 } || stemSound[0] != sound[0] || GlossaryLoanwordRule.IsWrittenLikeNativeWord(stem))
            {
                continue;
            }

            var distance = Distance(stemSound, sound);
            if (distance < bestDistance)
            {
                (best, bestDistance, tie) = (stem, distance, false);
            }
            else if (distance == bestDistance && !string.Equals(best, stem, StringComparison.Ordinal) && !IsParticleForm(best!, stem))
            {
                tie = true;
            }
        }

        return best != null && bestDistance <= allowed && !tie ? best : null;
    }

    private void Add(string name, string spelling)
    {
        if (_spellings.ContainsKey(name))
        {
            return;
        }

        var token = $"__XT_TERM_NAME_{Interlocked.Increment(ref _nextToken):0000}__";
        _spellings.TryAdd(name, (spelling, token));
    }

    private static IEnumerable<string> Stems(string word)
    {
        yield return word;
        foreach (var suffix in Suffixes)
        {
            if (word.Length - suffix.Length >= 2 && word.EndsWith(suffix, StringComparison.Ordinal))
            {
                yield return word[..^suffix.Length];
            }
        }
    }

    // 드렐로레아 and 드렐로레아는 found in one row are one spelling.
    private static bool IsParticleForm(string a, string b)
        => (a.Length < b.Length ? (a, b) : (b, a)) is var (shorter, longer) && longer.StartsWith(shorter, StringComparison.Ordinal);

    // A name inside a sentence ("Ask Merovech about"), alone ("Drelorea."), or ending a short greeting ("Hey Drelorea!").
    private static bool IsNamedPosition(string text, MatchCollection words, int index)
    {
        if (words.Count == 1 || (words.Count <= 3 && index == words.Count - 1))
        {
            return true;
        }

        if (index == 0)
        {
            return false;
        }

        var previous = words[index - 1];
        var between = text[(previous.Index + previous.Length)..words[index].Index];
        return char.IsLower(previous.Value[0]) && between.Trim().Trim(',').Length == 0;
    }

    // A word of a longer name or title keeps the whole name together: forcing Ripper alone left "Jack the" in
    // English ("Jack the 리퍼"), and Mare alone would split the Bannered Mare. Names the memory knows may stand
    // next to each other ("Erkeem Avidnvir"), and a capitalized word starting a sentence is not part of a name.
    private bool IsPartOfLongerName(string text, Match match)
    {
        var words = WordRegex.Matches(text);
        var index = -1;
        for (var i = 0; i < words.Count; i++)
        {
            if (words[i].Index == match.Index)
            {
                index = i;
                break;
            }
        }

        if (index < 0)
        {
            return false;
        }

        bool Adjacent(int a, int b) => text[(words[a].Index + words[a].Length)..words[b].Index].Trim().Length == 0;
        bool IsForeignCapital(int i) => char.IsUpper(words[i].Value[0]) && !_spellings.ContainsKey(StripPossessive(words[i].Value));
        bool StartsSentence(int i) => i == 0 || text[(words[i - 1].Index + words[i - 1].Length)..words[i].Index].IndexOfAny(new[] { '.', '!', '?', ':', ';' }) >= 0;
        bool IsLink(int i) => words[i].Value is "the" or "of";

        if (index > 0 && Adjacent(index - 1, index))
        {
            if (IsForeignCapital(index - 1) && !StartsSentence(index - 1))
            {
                return true;
            }

            if (IsLink(index - 1) && index > 1 && Adjacent(index - 2, index - 1) && char.IsUpper(words[index - 2].Value[0]))
            {
                return true;
            }
        }

        if (index + 1 < words.Count && Adjacent(index, index + 1))
        {
            if (IsForeignCapital(index + 1))
            {
                return true;
            }

            if (IsLink(index + 1) && index + 2 < words.Count && Adjacent(index + 1, index + 2) && char.IsUpper(words[index + 2].Value[0]))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsJoinedByHyphen(string text, int index, int length)
        => (index > 0 && text[index - 1] == '-') || (index + length < text.Length && text[index + length] == '-');

    private static string StripPossessive(string word)
        => word.EndsWith("'s", StringComparison.Ordinal) || word.EndsWith("’s", StringComparison.Ordinal) ? word[..^2] : word;

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
