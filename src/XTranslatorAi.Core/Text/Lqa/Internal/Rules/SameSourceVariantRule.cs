using System;
using System.Collections.Generic;
using System.Linq;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// Rows outside dialogue with the same source and record type but different translations: "Honed Dragon Bolt" as
/// 연마된 용의 뇌전 and 드래곤 벼락 낙하, "Fortify Mystic" as 신비 강화 and 강화 신비. Reviews unified 19 such groups by
/// hand. Dialogue is left out, where the same words may need different translations in different scenes, and so are
/// races and NPCs.
/// </summary>
internal static class SameSourceVariantRule
{
    public static Dictionary<long, string> Build(IReadOnlyList<LqaScanEntry> entries)
    {
        var findings = new Dictionary<long, string>();
        var groups = entries
            .Where(e => e.Status is Models.StringEntryStatus.Done or Models.StringEntryStatus.Edited)
            .Where(e => !string.IsNullOrWhiteSpace(e.SourceText) && !string.IsNullOrWhiteSpace(e.DestText))
            // Races and NPCs may share a generic name on purpose (巨人: the Giant and the Lurker).
            .Where(e => LqaScanner.GetRecBase(e.Rec) is not ("INFO" or "DIAL" or "RACE" or "NPC_"))
            .GroupBy(e => ((e.Rec ?? "").Trim().ToUpperInvariant(), e.SourceText.Trim()));

        foreach (var group in groups)
        {
            var variants = group.GroupBy(e => e.DestText.Trim(), StringComparer.Ordinal)
                .OrderByDescending(v => v.Count())
                .ThenBy(v => v.Min(e => e.OrderIndex))
                .ToList();
            if (variants.Count < 2)
            {
                continue;
            }

            var main = variants[0];
            foreach (var other in variants.Skip(1))
            {
                foreach (var entry in other)
                {
                    findings[entry.Id] = $"같은 원문의 다른 행({main.Count()}개)과 번역이 다릅니다: '{Shorten(main.Key)}'";
                }
            }
        }

        return findings;
    }

    public static void Apply(LqaScanEntry entry, IReadOnlyDictionary<long, string> findings, List<LqaIssue> issues)
    {
        if (findings.TryGetValue(entry.Id, out var message))
        {
            issues.Add(new LqaIssue(entry.Id, entry.OrderIndex, entry.Edid, entry.Rec, "Info", "same_source_variant", message,
                entry.SourceText ?? "", entry.DestText ?? ""));
        }
    }

    private static string Shorten(string text) => text.Length <= 40 ? text : text[..40] + "…";
}
