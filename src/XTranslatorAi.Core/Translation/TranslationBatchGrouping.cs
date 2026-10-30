using System;

namespace XTranslatorAi.Core.Translation;

internal static class TranslationBatchGrouping
{
    private const int MinSeriesRows = 3;

    /// <summary>
    /// Sort keys that keep related names in one batch, so the parts are translated together:
    /// <list type="bullet">
    /// <item>One skill written in several forms ("Learning - Waterfowl Dance", "Waterfowl Dance (1H Sword)", "Weapon Art -
    /// Waterfowl Dance (Charged)") shares the skill's name. Grouped by prefix, Elden Rim got 물새 난격 and 물새 난무, 에오히드의
    /// 검무 and 에오히드의 춤추는 칼날. The prefixes themselves are glossary terms or seeded as session terms.</item>
    /// <item>Otherwise a name series ("AttackDebuff - Knockback", "AttackDebuff - Drag", ...) shares its prefix even when its
    /// records use unrelated EditorIDs or record types; before, Elden Rim's series came out in mixed wording.</item>
    /// </list>
    /// A record's other rows (its description) follow its name. Other rows keep <see cref="ComputeGroupKey"/>.
    /// </summary>
    public static IReadOnlyList<string> ComputeGroupKeys(IReadOnlyList<(string? Rec, string? Edid, string SourceText)> items)
    {
        var stems = items.Select(it => SeriesStem(it.Rec, it.SourceText)).ToArray();
        var counts = stems
            .Where(s => s.Length > 0)
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        bool IsSeries(string stem) => stem.Length > 0 && counts[stem] >= MinSeriesRows;

        // A one-word name is a skill's only when the mod never writes the word in lowercase: "Kaiten" (회천/카이텐 in
        // two batches) is one, "Fire" in "Enchanting - Fire" and "Blade Oil - Fire" is not.
        var lowercase = new HashSet<string>(StringComparer.Ordinal);
        foreach (var it in items)
        {
            foreach (System.Text.RegularExpressions.Match word in LowercaseWord.Matches(it.SourceText))
            {
                lowercase.Add(word.Value);
            }
        }

        var names = items.Select(it => SkillName(it.Rec, it.SourceText) is var name
                                       && (name.Contains(' ') || name.Length > 0 && !lowercase.Contains(name.ToLowerInvariant()))
            ? name
            : "").ToArray();
        var forms = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++)
        {
            if (names[i].Length > 0)
            {
                (forms.TryGetValue(names[i], out var set) ? set : forms[names[i]] = new HashSet<string>(StringComparer.Ordinal))
                    .Add(items[i].SourceText.Trim());
            }
        }

        string? OwnKey(int i) => names[i].Length > 0 && forms[names[i]].Count >= 2 ? names[i] : IsSeries(stems[i]) ? stems[i] : null;

        var keyByRecord = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++)
        {
            var record = NormalizeEdid(items[i].Edid);
            if (record.Length > 0 && OwnKey(i) is { } key)
            {
                keyByRecord.TryAdd(record, key);
            }
        }

        var keys = new string[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var (rec, edid, source) = items[i];
            keys[i] = OwnKey(i)
                      ?? (keyByRecord.TryGetValue(NormalizeEdid(edid), out var recordKey) ? recordKey : ComputeGroupKey(rec, edid, source));
        }

        return keys;
    }

    private static readonly System.Text.RegularExpressions.Regex LowercaseWord = new(@"\b[a-z]+\b",
        System.Text.RegularExpressions.RegexOptions.CultureInvariant);

    // "Rim Scroll- Crescent Cleave": three of Elden Rim's scrolls lack the space before the dash.
    private static readonly string[] PrefixSeparators = { " - ", " – ", " — ", ": ", "- " };

    // The name after a prefix and without a trailing qualifier: "Weapon Art - Waterfowl Dance (Charged)" → "Waterfowl Dance".
    // A one-word name is returned too; ComputeGroupKeys keeps it only for a word the mod never writes in lowercase.
    private static string SkillName(string? rec, string sourceText)
    {
        var text = sourceText.Trim();
        if (TranslationRowRules.IsDialogue(rec) || text.Length > 80 || text.Contains('\n'))
        {
            return "";
        }

        var cut = -1;
        var separatorLength = 0;
        foreach (var separator in PrefixSeparators)
        {
            var index = text.IndexOf(separator, StringComparison.Ordinal);
            if (index > 0 && (cut < 0 || index < cut))
            {
                (cut, separatorLength) = (index, separator.Length);
            }
        }

        var name = cut > 0 ? text[(cut + separatorLength)..] : text;
        var qualifier = name.LastIndexOf(" (", StringComparison.Ordinal);
        if (name.EndsWith(')') && qualifier > 0)
        {
            name = name[..qualifier];
        }

        name = name.Trim();
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // "World-Shaking" is one word too (천지진동 and 천지개벽 in two batches).
        return words.Length >= 2 || words.Length == 1 && cut > 0 && char.IsUpper(name[0]) && name.All(c => char.IsLetter(c) || c is '-' or '\'')
            ? name
            : "";
    }

    // Dialogue lines belong with their topic; a shared "Yes (Persuade)" prefix says nothing about them.
    private static string SeriesStem(string? rec, string sourceText)
        => TranslationRowRules.IsDialogue(rec) ? "" : NormalizeSourceStem(sourceText);

    private static string NormalizeEdid(string? edid) => TranslationRowRules.EditorId(edid);

    public static string ComputeGroupKey(string? rec, string? edid, string sourceText)
    {
        var edidStem = NormalizeEdidStem(edid);
        if (!string.IsNullOrWhiteSpace(edidStem))
        {
            return edidStem;
        }

        var textStem = NormalizeSourceStem(sourceText);
        if (!string.IsNullOrWhiteSpace(textStem))
        {
            return textStem;
        }

        return "";
    }

    public static string NormalizeEdidStem(string? edid)
    {
        if (string.IsNullOrWhiteSpace(edid))
        {
            return "";
        }

        // "//" marks a commented-out record; without this "//ES_debuff5" sorted away from its "ES_debuff" series.
        var value = NormalizeEdid(edid);

        var end = value.Length;
        while (end > 0 && char.IsDigit(value[end - 1]))
        {
            end--;
        }

        if (end <= 0)
        {
            return "";
        }

        value = value[..end].TrimEnd('_', '-', ' ');
        return value;
    }

    public static string NormalizeSourceStem(string? sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return "";
        }

        var value = sourceText.Trim();

        var cut = FindFirstSeparatorIndex(value);
        if (cut <= 0)
        {
            return "";
        }

        value = value[..cut].Trim();
        const int maxLen = 64;
        if (value.Length > maxLen)
        {
            value = value[..maxLen];
        }

        return value;
    }

    private static int FindFirstSeparatorIndex(string value)
    {
        // Prefer grouping "X - Y", "X: Y", etc. We keep this conservative to avoid
        // over-grouping unrelated sentences.
        var best = -1;
        foreach (var sep in new[] { " - ", " – ", " — ", ": ", " (", " [" })
        {
            var idx = value.IndexOf(sep, StringComparison.Ordinal);
            if (idx > 0 && (best < 0 || idx < best))
            {
                best = idx;
            }
        }

        return best;
    }
}
