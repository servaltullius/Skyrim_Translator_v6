using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

/// <summary>
/// Separates protected text whose order is part of the layout (line breaks, page breaks,
/// formatting tags) from runtime values that may move with target-language word order
/// (&lt;Alias=…&gt;, %s, {name}, $VAR$, custom value tags).
/// </summary>
internal static class ProtectedTextKinds
{
    private static readonly Regex MarkupTagNameRegex = new(
        pattern: @"^[+-]?<\s*(?<closing>/)?\s*(?<name>[A-Za-z][A-Za-z0-9:_-]*)(?=[\s=/>])",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly string[] FormattingTagNames =
    {
        "a", "b", "i", "u", "s", "font", "p", "br", "img", "textformat", "li", "ul", "ol",
        "span", "div", "strong", "em", "sub", "sup", "hr", "body", "html",
    };

    /// <summary>Known formatting tags plus any tag that the same text also closes (a custom paired tag).</summary>
    internal static HashSet<string> FormattingNamesFor(IEnumerable<string> placeholders)
    {
        var names = new HashSet<string>(FormattingTagNames, StringComparer.OrdinalIgnoreCase);
        foreach (var placeholder in placeholders)
        {
            var tag = MarkupTagNameRegex.Match(placeholder);
            if (tag.Success && tag.Groups["closing"].Success)
            {
                names.Add(tag.Groups["name"].Value);
            }
        }
        return names;
    }

    internal static bool IsLayout(string placeholder, ISet<string> formattingNames)
    {
        if (placeholder is "\r\n" or "\r" or "\n"
            || placeholder.Equals("[pagebreak]", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        var tag = MarkupTagNameRegex.Match(placeholder);
        return tag.Success && formattingNames.Contains(tag.Groups["name"].Value);
    }
}
