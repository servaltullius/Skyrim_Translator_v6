using System;
using System.Collections.Generic;
using System.Linq;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Core.Text.Lqa.Internal.Rules;

/// <summary>
/// A vanilla name in the source (Jorrvaskr, Nightgate Inn, Erandur) whose official Korean spelling is missing from the
/// translation. Translation now fixes these names, but rows translated before, taken from a TM or edited by hand do
/// not get that; in Serana's review 246 rows were corrected to the official name and the check saw none of them.
/// Only dialogue and books are checked: a mod's own item and spell names may reuse a vanilla word on purpose, and a
/// name the glossary sets is the user's choice.
/// </summary>
internal static class OfficialNameRule
{
    public static HashSet<string> BuildGlossarySources(IReadOnlyList<GlossaryEntry> glossary)
        => glossary.Select(e => (e.SourceTerm ?? "").Trim()).Where(s => s.Length > 0).ToHashSet(StringComparer.OrdinalIgnoreCase);

    public static void Apply(
        LqaScanEntry entry,
        string sourceText,
        string destText,
        bool isKorean,
        ReferenceNameIndex? names,
        IReadOnlySet<string> glossarySources,
        List<LqaIssue> issues
    )
    {
        if (!isKorean || names == null || string.IsNullOrWhiteSpace(destText) || !IsCheckedRecord(entry.Rec))
        {
            return;
        }

        var compactDest = destText.Replace(" ", "", StringComparison.Ordinal);
        foreach (var (source, target) in names.FindIn(sourceText))
        {
            if (glossarySources.Contains(source) || compactDest.Contains(target.Replace(" ", "", StringComparison.Ordinal), StringComparison.Ordinal))
            {
                continue;
            }

            issues.Add(new LqaIssue(entry.Id, entry.OrderIndex, entry.Edid, entry.Rec, "Info", "official_name_missing",
                $"공식 이름 표기가 없습니다: '{source}' → '{target}'", sourceText, destText));
            return;
        }
    }

    private static bool IsCheckedRecord(string? rec)
        => LqaScanner.GetRecBase(rec) is "INFO" or "DIAL" or "BOOK";
}
