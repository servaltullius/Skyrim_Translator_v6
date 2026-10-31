using System;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

/// <summary>
/// Seconds after a value placeholder stand apart, as the Korean patch writes them: "&lt;dur&gt; 초 동안", "&lt;30&gt; 초"
/// (410 of its 439 places, and its review guide). The translator glued them ("&lt;dur&gt;초"), so a mod's effects read
/// differently from the game's and every patch line it filled was changed. A plain number keeps its unit ("30초"), and
/// "초당" before a token is a rate, not these seconds.
/// </summary>
internal static class PlaceholderSecondsSpacing
{
    // Any gap other than one plain space (none, two, a tab, a no-break or full-width space) becomes one space; a line
    // break stays.
    private static readonly Regex SecondsAfterTokenRegex = new(
        pattern: @"(?<tok>[+-]?<\s*(?:dur|mag|[0-9]+(?:\.[0-9]+)?)\s*>)(?<gap>[ \t 　]*)초(?!당)",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase,
        matchTimeout: TimeSpan.FromMilliseconds(250)
    );

    internal static string Apply(string text)
    {
        if (string.IsNullOrEmpty(text) || text.IndexOf('초') < 0 || text.IndexOf('<') < 0)
        {
            return text;
        }

        try
        {
            return SecondsAfterTokenRegex.Replace(text, m => m.Groups["gap"].Value == " " ? m.Value : m.Groups["tok"].Value + " 초");
        }
        catch (RegexMatchTimeoutException)
        {
            // Spacing is cosmetic: a text the pattern cannot finish keeps its spacing rather than failing its row.
            return text;
        }
    }
}
