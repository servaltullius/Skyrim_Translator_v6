using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// One name inside a "Prefix - Name" template translated differently from row to row: "Learning - Messmer's Assault"
/// as 메스메르의 강습 and "Rim Scroll - Messmer's Assault" as 메스메르의 맹공. The Elden War Ash mods name each skill in
/// the learning effect, the scroll, an annotation and the spell itself; 134 rows in five mods disagreed, and the
/// same-source check missed them because the full source texts differ. A prefix counts as a template when at least
/// three names use it. The spell itself ("Honed Bolt" alone) is what the magic menu shows, so when such rows agree
/// they decide the expected name; otherwise the clear majority does, and a tie notes every row.
/// </summary>
internal static class PrefixedNameVariantRule
{
    private const int MinNamesPerTemplate = 3;

    private static readonly Regex Separator = new(@"\s+-\s+", RegexOptions.CultureInvariant);

    private readonly record struct NamedRow(LqaScanEntry Entry, string Name, string Translation, bool IsBare);

    public static Dictionary<long, string> Build(IReadOnlyList<LqaScanEntry> entries)
    {
        var findings = new Dictionary<long, string>();
        var candidates = entries
            .Where(e => e.Status is Models.StringEntryStatus.Done or Models.StringEntryStatus.Edited)
            .Where(e => !string.IsNullOrWhiteSpace(e.SourceText) && !string.IsNullOrWhiteSpace(e.DestText))
            .Where(e => LqaScanner.GetRecBase(e.Rec) is not ("INFO" or "DIAL" or "RACE" or "NPC_"))
            .ToList();

        var prefixed = new List<(LqaScanEntry Entry, string Prefix, string Name, string Translation)>();
        foreach (var entry in candidates)
        {
            if (TrySplit(entry.SourceText, out var prefix, out var name) && TrySplit(entry.DestText, out _, out var translation))
            {
                prefixed.Add((entry, prefix, name, translation));
            }
        }

        var templates = prefixed
            .GroupBy(p => p.Prefix, StringComparer.Ordinal)
            .Where(g => g.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() >= MinNamesPerTemplate)
            .Select(g => g.Key)
            .ToHashSet(StringComparer.Ordinal);
        if (templates.Count == 0)
        {
            return findings;
        }

        var rows = prefixed
            .Where(p => templates.Contains(p.Prefix))
            .Select(p => new NamedRow(p.Entry, p.Name, p.Translation, IsBare: false))
            .ToList();
        var names = rows.Select(r => r.Name).ToHashSet(StringComparer.Ordinal);
        rows.AddRange(candidates
            .Where(e => names.Contains(e.SourceText.Trim()))
            .Select(e => new NamedRow(e, e.SourceText.Trim(), e.DestText.Trim(), IsBare: true)));

        foreach (var group in rows.GroupBy(r => r.Name, StringComparer.Ordinal))
        {
            // Rows with one and the same source are the same-source check's job.
            if (group.Select(r => r.Entry.SourceText.Trim()).Distinct(StringComparer.Ordinal).Count() < 2)
            {
                continue;
            }

            var variants = group.GroupBy(r => r.Translation, StringComparer.Ordinal)
                .OrderByDescending(v => v.Count())
                .ThenBy(v => v.Min(r => r.Entry.OrderIndex))
                .ToList();
            if (variants.Count < 2)
            {
                continue;
            }

            var expected = ExpectedTranslation(group.ToList(), variants);
            foreach (var row in group)
            {
                if (expected != null)
                {
                    if (!string.Equals(row.Translation, expected.Key, StringComparison.Ordinal))
                    {
                        findings[row.Entry.Id] = $"같은 이름의 다른 행({expected.Count()}개)과 번역이 다릅니다: '{Shorten(expected.Key)}'";
                    }
                }
                else
                {
                    var others = variants.Where(v => !string.Equals(v.Key, row.Translation, StringComparison.Ordinal))
                        .Take(3)
                        .Select(v => $"'{Shorten(v.Key)}'");
                    findings[row.Entry.Id] = $"같은 이름이 다른 행에서 다르게 번역됐습니다: {string.Join(", ", others)}";
                }
            }
        }

        return findings;
    }

    public static void Apply(LqaScanEntry entry, IReadOnlyDictionary<long, string> findings, List<LqaIssue> issues)
    {
        if (findings.TryGetValue(entry.Id, out var message))
        {
            issues.Add(new LqaIssue(entry.Id, entry.OrderIndex, entry.Edid, entry.Rec, "Info", "prefixed_name_variant", message,
                entry.SourceText ?? "", entry.DestText ?? ""));
        }
    }

    private static IGrouping<string, NamedRow>? ExpectedTranslation(List<NamedRow> group, List<IGrouping<string, NamedRow>> variants)
    {
        var bare = group.Where(r => r.IsBare).Select(r => r.Translation).Distinct(StringComparer.Ordinal).ToList();
        if (bare.Count == 1)
        {
            return variants.First(v => string.Equals(v.Key, bare[0], StringComparison.Ordinal));
        }

        return variants[0].Count() > variants[1].Count() ? variants[0] : null;
    }

    private static bool TrySplit(string text, out string prefix, out string rest)
    {
        prefix = rest = "";
        var match = Separator.Match(text);
        if (!match.Success)
        {
            return false;
        }

        prefix = text[..match.Index].Trim();
        rest = text[(match.Index + match.Length)..].Trim();
        return prefix.Length > 0 && rest.Length > 0;
    }

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..40] + "…";
}
