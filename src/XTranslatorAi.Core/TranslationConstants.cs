using System;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core;

public static class TranslationConstants
{
    public static readonly Regex XtTokenRegex = new(
        pattern: @"__XT_(?:PH|TERM)(?:_[A-Z0-9]+)?_[0-9]{4}__",
        options: RegexOptions.CultureInvariant | RegexOptions.Compiled
    );

    public static readonly Regex UiTagTokenRegex = new(
        pattern: @"[+-]?<\s*[^>]+\s*>|\[pagebreak\]|__XT_[A-Za-z0-9_]+__",
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
