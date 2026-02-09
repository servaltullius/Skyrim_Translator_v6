using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

public static partial class PairedSlashListExpander
{
    private static bool TryGetNumericValues(Match match, out List<string> values)
    {
        values = SplitAndTrim(match.Groups["values"].Value);
        if (values.Count < 2)
        {
            return false;
        }

        foreach (var v in values)
        {
            if (!IsNumericPlaceholderToken(v))
            {
                return false;
            }
        }

        return true;
    }

    private readonly record struct LabelsAndSuffix(List<string> Labels, string Suffix, int SuffixEnd);

    private static bool TryReadLabelsAndSuffix(string text, Match match, int expectedCount, out LabelsAndSuffix result)
    {
        result = default;

        var labelStart = match.Index + match.Length;
        if (labelStart < 0 || labelStart > text.Length)
        {
            return false;
        }

        if (!TryReadSlashSeparatedItems(text, labelStart, expectedCount, out var labels, out var afterLabels))
        {
            return false;
        }

        var suffixEnd = FindSuffixEnd(text, afterLabels);
        var suffix = text.Substring(afterLabels, suffixEnd - afterLabels).Trim();
        result = new LabelsAndSuffix(labels, suffix, suffixEnd);
        return true;
    }

    private static string BuildReplacement(
        string unitWord,
        IReadOnlyList<string> labels,
        IReadOnlyList<string> values,
        string suffix
    )
    {
        var perPart = string.IsNullOrWhiteSpace(suffix)
            ? $"per {unitWord}"
            : $"per {unitWord} of {suffix}";

        var pairs = new List<string>(capacity: values.Count);
        for (var i = 0; i < values.Count; i++)
        {
            pairs.Add($"{labels[i]}: {values[i]}");
        }

        return perPart + ": " + string.Join("; ", pairs);
    }

    private static List<string> SplitAndTrim(string text)
    {
        var parts = text.Split('/');
        var list = new List<string>(parts.Length);
        foreach (var p in parts)
        {
            var t = p.Trim();
            if (t.Length > 0)
            {
                list.Add(t);
            }
        }
        return list;
    }

    private static bool TryReadSlashSeparatedItems(
        string text,
        int start,
        int count,
        out List<string> items,
        out int end
    )
    {
        items = new List<string>(capacity: Math.Max(0, count));
        end = start;

        if (count <= 0)
        {
            return false;
        }

        var cursor = start;
        for (var i = 0; i < count; i++)
        {
            cursor = SkipWhitespace(text, cursor);
            var isLast = i == count - 1;
            if (!TryReadSlashSeparatedItem(text, cursor, isLast, out var item, out var nextCursor))
            {
                return false;
            }

            items.Add(item);
            cursor = nextCursor;
        }

        end = cursor;
        return items.Count == count;
    }

    private static bool TryReadSlashSeparatedItem(
        string text,
        int start,
        bool isLast,
        out string item,
        out int nextStart
    )
    {
        item = "";
        nextStart = start;

        if (isLast)
        {
            var end = FindLabelItemEnd(text, start);
            item = text.Substring(start, end - start).Trim();
            if (!IsValidLabelItem(item))
            {
                return false;
            }

            nextStart = end;
            return true;
        }

        var slashIdx = text.IndexOf('/', start);
        if (slashIdx < 0)
        {
            return false;
        }

        item = text.Substring(start, slashIdx - start).Trim();
        if (!IsValidLabelItem(item))
        {
            return false;
        }

        nextStart = slashIdx + 1;
        return true;
    }
}
