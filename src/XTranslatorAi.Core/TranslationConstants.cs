using System;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core;

public static class TranslationConstants
{
    public static readonly Regex XtTokenRegex = new(
        pattern: @"__XT_(?:PH|TERM)(?:_[A-Z0-9]+)?_[0-9]{4}__",
        options: RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    /// <summary>
    /// Angle brackets around two or more plain words are a stage direction shown as text ("<Take a deep breath>"
    /// in Serana Dialogue Add-On), not a tag, so they are translated. Tags keep a single word, '=', '/', a quote,
    /// a digit or '%': &lt;mag&gt;, &lt;Alias=Player&gt;, &lt;font face='$HandwrittenFont'&gt;, &lt;br /&gt;, &lt;100%&gt;.
    /// Legacy of the Dragonborn's "&lt;page break&gt;" marker stays a tag.
    /// </summary>
    public const string StageDirectionGuard = @"(?!(?!\s*[Pp]age\s+[Bb]reak\s*>)\s*[^\s<>=/'""\d%]+(?:\s+[^\s<>=/'""\d%]+)+\s*>)";

    public static readonly Regex UiTagTokenRegex = new(
        pattern: @"[+-]?<" + StageDirectionGuard + @"\s*[^>]+\s*>|\[pagebreak\]|__XT_[A-Za-z0-9_]+__",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    public const string EndSentinelToken = "__XT_PH_9999__";

    public const string TmHitNoteKind = "tm_hit";

    public const string TmFallbackNoteKind = "tm_fallback";

    public static string RemoveInvisibleSeparators(string text)
    {
        if (text.IndexOf('\u200B') < 0 && text.IndexOf('\uFEFF') < 0 && text.IndexOf('\u2060') < 0)
        {
            return text;
        }

        return text.Replace("\u200B", "", StringComparison.Ordinal)
            .Replace("\uFEFF", "", StringComparison.Ordinal)
            .Replace("\u2060", "", StringComparison.Ordinal);
    }
}
