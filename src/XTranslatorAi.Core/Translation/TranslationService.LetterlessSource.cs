using System.Linq;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// 3,848 of 624,924 fields in 4,913 local mods have no letters outside tags and placeholders ("...", a space, "???",
/// "11", "&lt;p align='center'&gt;&lt;/p&gt;"). They keep their source text instead of going to the model.
/// </summary>
public sealed partial class TranslationService
{
    private static readonly Regex NonTextMarkupRegex = new(
        @"<[^<>]*>|\[pagebreak\]|%[-+0-9.]*[a-zA-Z]|\{[^{}]*\}",
        RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);

    internal static bool HasLettersToTranslate(string? sourceText)
        => !string.IsNullOrEmpty(sourceText) && NonTextMarkupRegex.Replace(sourceText, " ").Any(char.IsLetter);
}
