using System;
using System.Collections.Generic;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

internal static class NameVerbEndingRule
{
    private static readonly HashSet<string> ItemRecFamilies = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACTI", "MISC", "WEAP", "ARMO", "AMMO", "INGR", "ALCH", "FLOR", "CONT", "FURN", "DOOR",
    };

    private static readonly string[] VerbEndings =
    {
        "하기", "되기", "합니다", "됩니다", "하다", "한다", "된다",
    };

    public static void Apply(LqaScanEntry entry, string destText, bool isKorean, List<LqaIssue> issues)
    {
        if (!isKorean || string.IsNullOrWhiteSpace(destText))
        {
            return;
        }

        if (!IsItemFullOrName(entry.Rec))
        {
            return;
        }

        var trimmed = destText.TrimEnd();
        if (trimmed.Length == 0)
        {
            return;
        }

        foreach (var ending in VerbEndings)
        {
            if (trimmed.EndsWith(ending, StringComparison.Ordinal))
            {
                issues.Add(new LqaIssue(
                    Id: entry.Id,
                    OrderIndex: entry.OrderIndex,
                    Edid: entry.Edid,
                    Rec: entry.Rec,
                    Severity: "Warn",
                    Code: "name_verb_ending",
                    Message: $"아이템/오브젝트 이름에 동사형 어미 사용: \"{ending}\"",
                    SourceText: entry.SourceText,
                    DestText: destText
                ));
                return;
            }
        }
    }

    private static bool IsItemFullOrName(string? rec)
    {
        if (string.IsNullOrWhiteSpace(rec))
        {
            return false;
        }

        var colon = rec.IndexOf(':');
        if (colon <= 0)
        {
            return false;
        }

        var family = rec[..colon].Trim();
        if (!ItemRecFamilies.Contains(family))
        {
            return false;
        }

        // Exclude QUST (quest names can be verb-like)
        if (string.Equals(family, "QUST", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var subtype = rec[(colon + 1)..].Trim();
        return string.Equals(subtype, "FULL", StringComparison.OrdinalIgnoreCase)
               || string.Equals(subtype, "NAME", StringComparison.OrdinalIgnoreCase);
    }
}
