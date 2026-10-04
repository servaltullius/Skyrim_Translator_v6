using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

public static class FortifyListExpander
{
    // A list item is a short capitalized term ("Smithing", "Light Armor", "One-handed"). Items used to be any
    // run of words, so "Fortify Sneak is active, and enemies and guards are less alert." read "Sneak is active",
    // "and enemies" and "guards" as a list and became "…, Fortify and enemies and Fortify guards are…".
    private const string ListItemPattern = @"[A-Z][A-Za-z0-9'\-]*(?: [A-Z][A-Za-z0-9'\-]*){0,2}";

    private static readonly Regex FortifySharedPrefixListRegex = new(
        pattern:
        @"\b(?<fortify>[Ff]ortify)\s+(?<list>" + ListItemPattern + @"(?:\s*,\s*" + ListItemPattern + @")*(?:\s*,?\s+(?i:and|or)\s+" + ListItemPattern + @")?)\s+(?<verb>(?i:is|are))\b",
        options: RegexOptions.CultureInvariant
    );

    private static readonly HashSet<string> NonTermWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "and", "or", "is", "are", "was", "were", "be", "been", "being", "has", "have", "had", "do", "does", "did",
        "can", "will", "get", "gets", "become", "becomes",
    };

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

                    if (items.Count <= 1 || !items.All(IsShortCapitalizedTerm))
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

    /// <summary>
    /// One to three capitalized words, none of them a verb or a conjunction: title-case text such as
    /// "Fortify Sneak Is Active, And Enemies Are…" still fits the pattern.
    /// </summary>
    private static bool IsShortCapitalizedTerm(string item)
    {
        var words = item.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length is >= 1 and <= 3
               && words.All(w => char.IsUpper(w[0]) && !NonTermWords.Contains(w));
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
