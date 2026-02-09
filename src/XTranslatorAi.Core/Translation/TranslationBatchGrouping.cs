using System;

namespace XTranslatorAi.Core.Translation;

internal static class TranslationBatchGrouping
{
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

        var value = edid.Trim();

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
