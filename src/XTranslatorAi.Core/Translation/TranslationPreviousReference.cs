using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Formats an earlier translation of the same entry (for example the previous release of a mod's Korean
/// patch) as reference context. It carries names a human translator settled on, such as 시산혈해 for
/// "Corpse mountain Blood Sea", that the model cannot infer from the English alone.
/// </summary>
public static class TranslationPreviousReference
{
    private const int MaxChars = 600;

    // Same rule as the book reference: strip protected tokens and raw markup so the reference cannot add output tokens.
    private static readonly Regex Markers = new(
        @"__XT_[A-Z0-9_]+__|<[^>]*>|\[page ?break\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    public static string? Build(string? previousTranslation)
    {
        if (string.IsNullOrWhiteSpace(previousTranslation))
        {
            return null;
        }

        var text = Regex.Replace(Markers.Replace(previousTranslation, " "), @"\s+", " ").Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length > MaxChars)
        {
            text = text[..MaxChars] + "…";
        }

        return "Earlier translation of this same entry from a previous release of this mod's translation (reference only; the source may have changed). "
               + "Keep its established names and terms where they still fit the current source. Translate the current source for meaning and numbers, "
               + "and do not repeat its mistakes such as wrong particles, ambiguous particle markers or untranslated words.\n"
               + "Earlier translation: " + text;
    }
}
