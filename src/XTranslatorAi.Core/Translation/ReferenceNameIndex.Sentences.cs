using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

public sealed partial class ReferenceNameIndex
{
    private static readonly HashSet<string> Connectors = new(StringComparer.Ordinal) { "of", "the", "and" };
    private static readonly Regex PossessiveEnd = new(@"['’]s$", RegexOptions.CultureInvariant);
    private static readonly Regex TokenEdges = new(@"^[^\p{L}\p{N}]+|[^\p{L}\p{N}]+$", RegexOptions.CultureInvariant);
    private static readonly Regex HangulWord = new(@"^[가-힣]+(?:-[가-힣]+)*$", RegexOptions.CultureInvariant);

    // Particles after a name in a sentence (지구를, 호수로, 강도단들의), longest first.
    private static readonly string[] Particles = new[]
        {
            "은", "는", "이", "가", "을", "를", "의", "에", "로", "와", "과", "도", "만", "야", "으로", "에서", "에게", "까지", "부터",
            "처럼", "보다", "한테", "에는", "에서는", "으로는", "로는", "에게는", "이나", "이랑", "랑", "들", "들의", "들은", "들이",
            "들을", "들과", "들에게", "들도",
        }
        .OrderByDescending(particle => particle.Length)
        .ToArray();

    /// <summary>
    /// Names the memory has only inside sentences, with the translation its sentences share: MEI wrote "Gray Quarter"
    /// and "Lake Honrich" its own way while the memory says 잿빛 지구 (three sentences) and 혼리크 호수 (two). A name
    /// counts where it stands alone in a sentence, not at the start of one ("Join Barbas") and not as part of a longer
    /// capitalized name ("Gloves of Major Destruction"). The translation is the run of as many Korean words as the
    /// name has capitalized words that at least two and 80% of the sentences hold, less the particles after it, and it
    /// must be the only such run. It must not end in a possessive: "King Olaf's Verse" is 올라프 왕의 시, and the two
    /// words of "Olaf's Verse" would give 올라프 왕의. A name inside longer official names must be written as in them:
    /// the sentences call the "Guild Master" 길드 지도자, but its armor is 길드 마스터의 방어구. On the official memory
    /// this finds 26 names, among them 흘랄루 가문 (House Hlaalu) and 블랙블러드 강도단 (Blackblood Marauders).
    /// </summary>
    private static IEnumerable<(string Source, string Target)> NamesInSentences(IReadOnlyList<(string Source, string Target)> pairs)
    {
        var uses = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var (source, target) in pairs)
        {
            foreach (var name in NamesStandingAloneIn(source))
            {
                if (!uses.TryGetValue(name, out var targets))
                {
                    targets = new List<string>();
                    uses[name] = targets;
                }

                targets.Add(target);
            }
        }

        var sources = pairs.Select(pair => pair.Source).ToHashSet(StringComparer.Ordinal);
        var entries = pairs.Where(IsNameEntry).ToList();
        foreach (var (name, targets) in uses)
        {
            if (targets.Count < 2 || sources.Contains(name) || TranslationTheSentencesShare(name, targets) is not { } translation)
            {
                continue;
            }

            var occurrence = NameOccurrence(name);
            if (entries.Any(entry => entry.Source.Length > name.Length && occurrence.IsMatch(entry.Source)
                                     && !entry.Target.Contains(translation, StringComparison.Ordinal)))
            {
                continue;
            }

            yield return (name, translation);
        }
    }

    // Two to four words that NamePattern accepts, inside a sentence (a lowercase word other than a connector), not at its
    // start, and with no capitalized word joined on either side.
    private static IEnumerable<string> NamesStandingAloneIn(string source)
    {
        var words = WordRegex.Matches(source).ToArray();
        if (!words.Any(word => char.IsLower(word.Value[0]) && !Connectors.Contains(word.Value)))
        {
            yield break;
        }

        bool Joined(int left) => source[(words[left].Index + words[left].Length)..words[left + 1].Index] == " ";
        bool Capitalized(int i) => char.IsUpper(words[i].Value[0]);
        bool NameBefore(int first) => first > 0 && Joined(first - 1)
                                      && (Capitalized(first - 1) || Connectors.Contains(words[first - 1].Value) && first > 1 && Joined(first - 2) && Capitalized(first - 2));
        bool NameAfter(int last) => last + 1 < words.Length && Joined(last)
                                    && (Capitalized(last + 1) || Connectors.Contains(words[last + 1].Value) && last + 2 < words.Length && Joined(last + 1) && Capitalized(last + 2));

        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var first = 0; first < words.Length; first++)
        {
            var before = source[..words[first].Index].TrimEnd();
            if (!Capitalized(first) || before.Length == 0 || ".!?:\"“([-—…'".Contains(before[^1]) || NameBefore(first))
            {
                continue;
            }

            for (var last = first + 1; last < words.Length && last < first + 4 && Joined(last - 1); last++)
            {
                if (!Capitalized(last) || NameAfter(last))
                {
                    continue;
                }

                var name = source[words[first].Index..(words[last].Index + words[last].Length)];
                if (name != source && NamePattern.IsMatch(name) && !PossessiveEnd.IsMatch(name) && seen.Add(name))
                {
                    yield return name;
                }
            }
        }
    }

    private static string? TranslationTheSentencesShare(string name, IReadOnlyList<string> targets)
    {
        var length = name.Split(' ').Count(word => char.IsUpper(word[0]));
        var support = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var target in targets)
        {
            var tokens = target.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Select(token => TokenEdges.Replace(token, "")).ToArray();
            var runs = new HashSet<string>(StringComparer.Ordinal);
            for (var start = 0; start + length <= tokens.Length; start++)
            {
                var inner = tokens[start..(start + length - 1)];
                if (!inner.All(word => HangulWord.IsMatch(word)))
                {
                    continue;
                }

                foreach (var last in WithoutParticles(tokens[start + length - 1]).Where(word => HangulWord.IsMatch(word)))
                {
                    runs.Add(string.Join(' ', inner.Append(last)));
                }
            }

            foreach (var run in runs)
            {
                support[run] = support.GetValueOrDefault(run) + 1;
            }
        }

        var shared = support.Where(pair => pair.Value >= 2 && pair.Value * 5 >= targets.Count * 4).ToList();
        if (shared.Count == 0)
        {
            return null;
        }

        var most = shared.Max(pair => pair.Value);
        var best = shared.Where(pair => pair.Value == most).Select(pair => pair.Key).ToList();

        // 잿빛 지구 and 잿빛 지구를 are one run; the form without the particle is the name.
        var roots = best.Where(run => !best.Any(other => other.Length < run.Length && run.StartsWith(other, StringComparison.Ordinal)
                                                         && !run[other.Length..].Contains(' '))).ToList();
        // A possessive at the end leaves out what it belongs to: King Olaf's Verse is 올라프 왕의 시, not 올라프 왕의.
        return roots.Count == 1 && !roots[0].EndsWith('의') ? roots[0] : null;
    }

    private static IEnumerable<string> WithoutParticles(string token)
    {
        yield return token;
        foreach (var particle in Particles)
        {
            if (token.Length - particle.Length >= 2 && token.EndsWith(particle, StringComparison.Ordinal))
            {
                yield return token[..^particle.Length];
            }
        }
    }
}
