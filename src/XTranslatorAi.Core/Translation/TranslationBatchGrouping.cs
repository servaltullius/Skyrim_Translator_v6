using System;

namespace XTranslatorAi.Core.Translation;

internal static class TranslationBatchGrouping
{
    private const int MinSeriesRows = 3;

    /// <summary>
    /// Sort keys that put a name series ("AttackDebuff - Knockback", "AttackDebuff - Drag", ...) into one run even when
    /// its records use unrelated EditorIDs or record types. A record's other rows (its description) follow its name.
    /// Rows outside any series keep <see cref="ComputeGroupKey"/>. Elden Rim's series were spread across the whole
    /// sort order before, so each part was translated in a different batch and the series came out in mixed wording.
    /// </summary>
    public static IReadOnlyList<string> ComputeGroupKeys(IReadOnlyList<(string? Rec, string? Edid, string SourceText)> items)
    {
        var stems = items.Select(it => SeriesStem(it.Rec, it.SourceText)).ToArray();
        var counts = stems
            .Where(s => s.Length > 0)
            .GroupBy(s => s, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(g => g.Key, g => g.Count(), StringComparer.OrdinalIgnoreCase);
        bool IsSeries(string stem) => stem.Length > 0 && counts[stem] >= MinSeriesRows;

        var seriesByRecord = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < items.Count; i++)
        {
            var record = NormalizeEdid(items[i].Edid);
            if (record.Length > 0 && IsSeries(stems[i]))
            {
                seriesByRecord.TryAdd(record, stems[i]);
            }
        }

        var keys = new string[items.Count];
        for (var i = 0; i < items.Count; i++)
        {
            var (rec, edid, source) = items[i];
            keys[i] = IsSeries(stems[i]) ? stems[i]
                : seriesByRecord.TryGetValue(NormalizeEdid(edid), out var series) ? series
                : ComputeGroupKey(rec, edid, source);
        }

        return keys;
    }

    // Dialogue lines belong with their topic; a shared "Yes (Persuade)" prefix says nothing about them.
    private static string SeriesStem(string? rec, string sourceText)
        => NormalizeRecBase(rec)?.ToUpperInvariant() is "INFO" or "DIAL" ? "" : NormalizeSourceStem(sourceText);

    private static string NormalizeEdid(string? edid) => (edid ?? "").Trim().TrimStart('/').Trim();

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

    public static string? NormalizeRecBase(string? rec)
    {
        if (string.IsNullOrWhiteSpace(rec))
        {
            return null;
        }

        var trimmed = rec.Trim();
        var idx = trimmed.IndexOf(':', StringComparison.Ordinal);
        if (idx > 0)
        {
            trimmed = trimmed[..idx];
        }

        return trimmed;
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
