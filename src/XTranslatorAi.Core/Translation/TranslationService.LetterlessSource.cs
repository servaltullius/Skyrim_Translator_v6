using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// 3,848 of 624,924 fields in 4,913 local mods have no letters outside tags and placeholders ("...", a space, "???",
/// "11", "&lt;p align='center'&gt;&lt;/p&gt;"). They keep their source text instead of going to the model.
/// A stage direction or player option in angle brackets ("&lt; Recruit character as a follower. &gt;") is text, not
/// a tag: counting it as markup kept MEI's 17 options in English.
/// </summary>
public sealed partial class TranslationService
{
    private static readonly Regex NonTextMarkupRegex = new(
        @"<" + TranslationConstants.StageDirectionGuard + @"[^<>]*>|" + XTranslatorAi.Core.Text.ProtectedTextKinds.BracketPageBreakPattern + @"|%[-+0-9.]*[a-zA-Z]|\{[^{}]*\}",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    // <mag>, <dur>, numbers and variables: their particles and word order may still need the model.
    private static readonly Regex SemanticPlaceholderTokenRegex = new(
        @"__XT_PH_[A-Z]+_[0-9]{4}__", RegexOptions.CultureInvariant);

    internal static bool HasLettersToTranslate(string? sourceText)
        => !string.IsNullOrEmpty(sourceText) && NonTextMarkupRegex.Replace(sourceText, " ").Any(char.IsLetter);

    /// <summary>
    /// A row that is only forced names or glossary terms once they are tokens ("Iron Sword", "Spell Tome: Maven")
    /// has no words left for the model, which only echoed the tokens back: 21 of 379 items in the 1.12 eval run.
    /// </summary>
    internal static bool IsOnlyForcedTerms(string maskedText)
        => maskedText.Contains("__XT_TERM_", StringComparison.Ordinal)
           && !SemanticPlaceholderTokenRegex.IsMatch(maskedText)
           && !TranslationConstants.XtTokenRegex.Replace(maskedText, " ").Any(char.IsLetter);
}
