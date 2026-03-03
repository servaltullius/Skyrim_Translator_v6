using System.Text.RegularExpressions;

namespace XTranslatorAi.Core;

public static class TranslationConstants
{
    public static readonly Regex XtTokenRegex = new(
        pattern: @"__XT_(?:PH|TERM)(?:_[A-Z0-9]+)?_[0-9]{4}__",
        options: RegexOptions.CultureInvariant
    );

    public const string EndSentinelToken = "__XT_PH_9999__";

    public const string TmHitNoteKind = "tm_hit";

    public const string TmFallbackNoteKind = "tm_fallback";
}
