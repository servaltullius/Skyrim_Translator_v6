using System.Collections.Generic;
using System.Text;
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
        @"__XT_[A-Z0-9_]+__|<[^>]*>|" + XTranslatorAi.Core.Text.ProtectedTextKinds.BracketPageBreakPattern,
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    // Each changed paragraph as the request lists it.
    private const int MaxChangedChars = 800;

    /// <summary>
    /// The reference, telling the model to copy the patch's paragraphs and translate only the changed ones: given the
    /// whole text as a loose reference, gemini-3.8-flash kept 56% of the patch's wording and still wrote 암벽 전쟁 for
    /// the patch's 크래그 전쟁.
    /// </summary>
    public static string? Build(string? koreanPatchText, IReadOnlyList<string>? changedParagraphs = null)
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

        var sb = new StringBuilder();
        sb.Append("This source is the base game's text with small edits, and the Korean patch translated the base game's text (below). ");
        sb.Append("Copy the Korean patch text paragraph by paragraph: keep its sentences, wording, names and terms exactly as written, ");
        sb.Append("and keep only the source's own markup, tokens and line breaks around them. Do not retranslate or reword a paragraph the source did not change.\n");
        if (changedParagraphs is { Count: > 0 })
        {
            sb.Append("Only these source paragraphs differ from the base game's; translate them, keeping the patch's wording wherever the English is still the same:\n");
            foreach (var paragraph in changedParagraphs)
            {
                sb.Append("- ").Append(paragraph.Length > MaxChangedChars ? paragraph[..MaxChangedChars] + "…" : paragraph).Append('\n');
            }
        }
        else
        {
            sb.Append("Short lines such as titles may differ; translate those as the source says.\n");
        }

        return sb.Append("Korean patch text:\n").Append(text).ToString();
    }
}
