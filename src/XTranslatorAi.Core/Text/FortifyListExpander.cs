using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

public static class FortifyListExpander
{
    private static readonly Regex FortifySharedPrefixListRegex = new(
        pattern:
        @"\b(?<fortify>Fortify)\s+(?<list>[A-Za-z][A-Za-z0-9\-' ]*(?:\s*,\s*[A-Za-z0-9][A-Za-z0-9\-' ]*)*(?:\s*,?\s*(?:and|or)\s*[A-Za-z0-9][A-Za-z0-9\-' ]*)?)\s+(?<verb>is|are)\b",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    public static string Expand(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (text.IndexOf("Fortify", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return text;
        }

        var anyChanged = false;
        var output =
            FortifySharedPrefixListRegex.Replace(
                text,
                m =>
                {
                    var list = m.Groups["list"].Value;
                    if (string.IsNullOrWhiteSpace(list))
                    {
                        return m.Value;
                    }

                    // If already expanded (contains "Fortify" inside the list), leave unchanged.
                    if (list.IndexOf("Fortify", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return m.Value;
                    }

                    var hasSeparator =
                        list.IndexOf(',', StringComparison.Ordinal) >= 0
                        || list.IndexOf(" and ", StringComparison.OrdinalIgnoreCase) >= 0
                        || list.IndexOf(" or ", StringComparison.OrdinalIgnoreCase) >= 0;
                    if (!hasSeparator)
                    {
                        return m.Value;
                    }

                    if (!TryParseList(list, out var items, out var conjunction, out var oxfordComma))
                    {
                        return m.Value;
                    }

                    if (items.Count <= 1)
                    {
                        return m.Value;
                    }

                    var expandedList = BuildExpandedList(items, conjunction, oxfordComma);
                    if (string.Equals(expandedList, list, StringComparison.Ordinal))
                    {
                        return m.Value;
                    }

                    anyChanged = true;
                    return $"{m.Groups["fortify"].Value} {expandedList} {m.Groups["verb"].Value}";
                }
            );

        return anyChanged ? output : text;
    }

    private static bool TryParseList(string list, out List<string> items, out string conjunction, out bool oxfordComma)
    {
        items = new List<string>();
        conjunction = "and";
        oxfordComma = false;

        if (string.IsNullOrWhiteSpace(list))
        {
            return false;
        }

        var andIdx = LastIndexOfIgnoreCase(list, " and ");
        var orIdx = LastIndexOfIgnoreCase(list, " or ");

        var conjIdx = Math.Max(andIdx, orIdx);
        string conjWord;
        if (conjIdx >= 0)
        {
            conjWord = conjIdx == andIdx ? "and" : "or";
            conjunction = conjWord;

            var left = list.Substring(0, conjIdx);
            var right = list.Substring(conjIdx + (conjWord == "and" ? 5 : 4)); // " and " / " or "
            oxfordComma = left.TrimEnd().EndsWith(",", StringComparison.Ordinal);

            items.AddRange(SplitCommaItems(left));
            items.AddRange(SplitCommaItems(right));
        }
        else
        {
            items.AddRange(SplitCommaItems(list));
        }

        items = items
            .Select(i => i.Trim())
            .Where(i => !string.IsNullOrWhiteSpace(i))
            .Distinct(StringComparer.Ordinal)
            .ToList();

        // Safety: if we didn't detect a conjunction but a comma-split item begins with "and/or",
        // it's likely a formatting edge case ("X, Y,and Z") and we should not rewrite.
        if (conjIdx < 0
            && items.Any(
                i => i.StartsWith("and ", StringComparison.OrdinalIgnoreCase)
                     || i.StartsWith("or ", StringComparison.OrdinalIgnoreCase)
            ))
        {
            items.Clear();
            return false;
        }

        return items.Count > 0;
    }

    private static IEnumerable<string> SplitCommaItems(string segment)
    {
        if (string.IsNullOrWhiteSpace(segment))
        {
            yield break;
        }

        foreach (var part in segment.Split(','))
        {
            var trimmed = part.Trim();
            if (string.IsNullOrWhiteSpace(trimmed))
            {
                continue;
            }

            yield return trimmed;
        }
    }

    private static string BuildExpandedList(IReadOnlyList<string> items, string conjunction, bool oxfordComma)
    {
        if (items.Count == 0)
        {
            return "";
        }

        if (items.Count == 1)
        {
            return items[0];
        }

        var conj = string.IsNullOrWhiteSpace(conjunction) ? "and" : conjunction.Trim();

        string PrefixIfNeeded(string item)
            => item.StartsWith("Fortify ", StringComparison.OrdinalIgnoreCase) ? item : "Fortify " + item;

        var sb = new StringBuilder();
        sb.Append(items[0]);

        if (items.Count == 2)
        {
            sb.Append(' ');
            sb.Append(conj);
            sb.Append(' ');
            sb.Append(PrefixIfNeeded(items[1]));
            return sb.ToString();
        }

        for (var i = 1; i < items.Count - 1; i++)
        {
            sb.Append(", ");
            sb.Append(PrefixIfNeeded(items[i]));
        }

        if (oxfordComma)
        {
            sb.Append(", ");
        }
        else
        {
            sb.Append(' ');
        }

        sb.Append(conj);
        sb.Append(' ');
        sb.Append(PrefixIfNeeded(items[^1]));
        return sb.ToString();
    }

    private static int LastIndexOfIgnoreCase(string text, string value)
    {
        if (string.IsNullOrEmpty(text) || string.IsNullOrEmpty(value))
        {
            return -1;
        }

        return text.LastIndexOf(value, StringComparison.OrdinalIgnoreCase);
    }
}
