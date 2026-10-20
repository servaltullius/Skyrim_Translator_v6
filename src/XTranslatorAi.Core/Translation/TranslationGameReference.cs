using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Formats the base game's Korean for a long text the mod copied with changes (see GameStringDictionary.FindSimilar)
/// as reference context. LotD's copy of The Falmer: A Study fixes a typo in the base game's English, so the Korean
/// patch's text was not applied, and every model translated the book anew; none knew the patch calls the War of the
/// Crag 크래그 전쟁.
/// </summary>
public static class TranslationGameReference
{
    // A whole book: the reference is meant to be followed paragraph by paragraph.
    private const int MaxChars = 12000;

    // Same rule as the earlier-translation reference: strip protected tokens and raw markup so the reference cannot add output tokens.
    private static readonly Regex Markers = new(
        @"__XT_[A-Z0-9_]+__|<[^>]*>|\[page ?break\]",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    public static string? Build(string? koreanPatchText)
    {
        if (string.IsNullOrWhiteSpace(koreanPatchText))
        {
            return null;
        }

        // Paragraphs stay apart, so each lines up with the source's.
        var text = Markers.Replace(koreanPatchText.Replace("\r\n", "\n").Replace('\r', '\n'), " ");
        text = Regex.Replace(text, @"[ \t]+", " ");
        text = Regex.Replace(text, @" *\n[ \n]*\n *", "\n\n").Trim();
        if (text.Length == 0)
        {
            return null;
        }

        if (text.Length > MaxChars)
        {
            text = text[..MaxChars] + "…";
        }

        return "The base game has this same text with small differences, and the Korean patch translated it (reference only; the source may have changed). "
               + "Where a paragraph of the source says what the reference's paragraph says, follow its wording, names and terms; "
               + "translate any part the reference lacks or says differently from the current source, and keep the source's markup and line breaks.\n"
               + "Korean patch text:\n" + text;
    }
}
