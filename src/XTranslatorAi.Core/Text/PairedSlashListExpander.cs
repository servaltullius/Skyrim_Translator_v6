using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

public static partial class PairedSlashListExpander
{
    private static readonly Regex ValuesAndPerRegex = new(
        pattern: @"(?<values>(?:__XT_PH_NUM_[0-9]{4}__\s*/\s*)+__XT_PH_NUM_[0-9]{4}__)\s*per\s+(?<unit>point|points|level|levels)\s+of\s+(?:the\s+)?",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    public static string Expand(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var sb = new StringBuilder(text.Length);
        var lastAppend = 0;
        var searchStart = 0;

        while (true)
        {
            var match = ValuesAndPerRegex.Match(text, searchStart);
            if (!match.Success)
            {
                break;
            }

            if (!TryExpandAt(text, match, out var replacement, out var replaceEnd))
            {
                searchStart = match.Index + 1;
                continue;
            }

            sb.Append(text.AsSpan(lastAppend, match.Index - lastAppend));
            sb.Append(replacement);
            lastAppend = replaceEnd;
            searchStart = replaceEnd;
        }

        if (lastAppend == 0)
        {
            return text;
        }

        sb.Append(text.AsSpan(lastAppend));
        return sb.ToString();
    }

    private static bool TryExpandAt(string text, Match match, out string replacement, out int replaceEnd)
    {
        replacement = "";
        replaceEnd = 0;

        if (!TryGetNumericValues(match, out var values))
        {
            return false;
        }

        var unit = match.Groups["unit"].Value;
        var unitWord =
            unit.StartsWith("point", StringComparison.OrdinalIgnoreCase)
                ? "point"
                : "level";

        if (!TryReadLabelsAndSuffix(text, match, values.Count, out var labelsAndSuffix))
        {
            return false;
        }

        replacement = BuildReplacement(unitWord, labelsAndSuffix.Labels, values, labelsAndSuffix.Suffix);
        replaceEnd = labelsAndSuffix.SuffixEnd;
        return true;
    }
}
