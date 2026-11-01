using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

/// <summary>
/// Text in a font whose glyphs are runes for the Latin letters. The game's Interface/fontconfig.txt maps $DaedricFont,
/// $DragonFont, $DwemerFont, $FalmerFont and $MageScriptFont to such fonts, and they have no Hangul. The game writes
/// English in them as a cipher (the Atronach Forge recipes, a Mythic Dawn commentary's title word, the Falmer books,
/// Dwemer Inquiries' poem); a translation there shows boxes or nothing, as the Korean patch's recipes did. The official
/// Japanese release keeps all of these in English. So the text stays the source's: the masker protects it as one
/// piece, a known translation (TM, the game's text, an earlier release), the post-edit reapply and the export take it
/// back from the source.
/// The fonts and paragraphs are read as a stack, and a span is text whose innermost face is a rune font:
/// - a &lt;/font&gt; closes the innermost font ("&lt;font face='$DaedricFont'&gt;CHIM&lt;/font&gt;");
/// - another face inside it ends the runes until it closes: Dwemer Inquiries goes from its runic poem to
///   "&lt;font face='$SkyrimBooks'&gt;" for its text, and an Almsivi note writes a comma in a handwriting font between
///   Daedric words;
/// - the end of a paragraph closes the fonts opened inside it: the commentary's
///   "&lt;font face='$DaedricFont'&gt;GHARTOK&lt;/p&gt;" never closes its font, and the book's text after the title is not
///   runes; a paragraph opened inside the font (the Falmer journals' "&lt;font face='$FalmerFont'&gt;&lt;p align="left"&gt;…
///   &lt;/font&gt;") does not end it.
/// </summary>
public static class CipherFontSpans
{
    /// <summary>How a span ends. A translation whose span ends otherwise is not the source's layout.</summary>
    public enum End
    {
        ClosingTag,
        OtherFace,
        ParagraphEnd,
        TextEnd,
    }

    private static readonly Regex TagRegex = new(
        pattern: @"<(?<closing>/?)\s*(?<name>font|p)\b(?<attrs>[^>]*)>",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex FaceRegex = new(
        pattern: @"\bface\s*=\s*['""]?(?<face>[^'"" >/]+)",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex CipherFaceRegex = new(
        pattern: @"^\$(?:Daedric|Dragon|Dwemer|Falmer|MageScript)Font$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex HangulRegex = new("[가-힣ㄱ-ㆎ]", RegexOptions.CultureInvariant);

    // A frame on the stack: a paragraph, or a font with its face (null when it sets only a size or color).
    private readonly record struct Frame(bool Paragraph, string? Face);

    /// <summary>The spans in text order: start, length, and how each ends.</summary>
    public static IReadOnlyList<(int Start, int Length, End End)> Find(string? text)
    {
        var spans = new List<(int Start, int Length, End End)>();
        if (string.IsNullOrEmpty(text) || text.IndexOf("Font", StringComparison.OrdinalIgnoreCase) < 0)
        {
            return spans;
        }

        var stack = new List<Frame>();
        int? start = null;
        foreach (Match tag in TagRegex.Matches(text))
        {
            var closing = tag.Groups["closing"].Length > 0;
            var paragraph = tag.Groups["name"].Value.Equals("p", StringComparison.OrdinalIgnoreCase);
            if (!closing && tag.Value.EndsWith("/>", StringComparison.Ordinal))
            {
                // A self-closing tag sets nothing for the text after it.
                continue;
            }

            var wasRunes = InRunes(stack);
            var end = End.ClosingTag;
            if (!closing && paragraph)
            {
                stack.Add(new Frame(true, null));
            }
            else if (!closing)
            {
                var face = FaceRegex.Match(tag.Groups["attrs"].Value);
                stack.Add(new Frame(false, face.Success ? face.Groups["face"].Value : null));
                end = End.OtherFace;
            }
            else if (paragraph)
            {
                // A paragraph's end closes what was opened inside it; one with no paragraph open closes everything.
                var open = stack.FindLastIndex(frame => frame.Paragraph);
                stack.RemoveRange(Math.Max(open, 0), stack.Count - Math.Max(open, 0));
                end = End.ParagraphEnd;
            }
            else
            {
                var open = stack.FindLastIndex(frame => !frame.Paragraph);
                if (open >= 0)
                {
                    stack.RemoveAt(open);
                }
            }

            var isRunes = InRunes(stack);
            if (!wasRunes && isRunes)
            {
                // A rune font's tag belongs to its span; after another face closes, the runes go on after the tag.
                start = closing ? tag.Index + tag.Length : tag.Index;
            }
            else if (wasRunes && !isRunes && start is { } begin)
            {
                var stop = end == End.ClosingTag ? tag.Index + tag.Length : tag.Index;
                spans.Add((begin, stop - begin, end));
                start = null;
            }
        }

        if (start is { } last)
        {
            spans.Add((last, text.Length - last, End.TextEnd));
        }

        return spans;
    }

    private static bool InRunes(List<Frame> stack)
    {
        for (var i = stack.Count - 1; i >= 0; i--)
        {
            if (stack[i].Face is { } face)
            {
                return CipherFaceRegex.IsMatch(face);
            }
        }

        return false;
    }

    /// <summary>The text without its spans (for checks that read the translatable words).</summary>
    public static string Remove(string? text)
    {
        var spans = Find(text);
        if (spans.Count == 0)
        {
            return text ?? "";
        }

        var builder = new StringBuilder(text!.Length);
        var last = 0;
        foreach (var (start, length, _) in spans)
        {
            builder.Append(text, last, start - last);
            last = start + length;
        }

        return builder.Append(text, last, text.Length - last).ToString();
    }

    /// <summary>Whether a span of the text holds Hangul, which a rune font cannot show.</summary>
    public static bool HasHangul(string? text)
        => Find(text).Any(span => HangulRegex.IsMatch(text!.AsSpan(span.Start, span.Length).ToString()));

    /// <summary>
    /// The translation with each span given back the source's, in order. It is left as it is, for the integrity check to
    /// refuse, when the spans differ in number or in how they end (a &lt;/font&gt; left out would take the translated
    /// text after it into the span), or when a span holding Hangul has taken the text the source has right after its
    /// span: giving the span back would drop that translation.
    /// </summary>
    public static string RestoreFromSource(string? sourceText, string translatedText)
    {
        if (string.IsNullOrEmpty(translatedText))
        {
            return translatedText;
        }

        var sourceSpans = Find(sourceText);
        if (sourceSpans.Count == 0)
        {
            return translatedText;
        }

        var translatedSpans = Find(translatedText);
        if (translatedSpans.Count != sourceSpans.Count)
        {
            return translatedText;
        }

        for (var i = 0; i < sourceSpans.Count; i++)
        {
            var (start, length, end) = translatedSpans[i];
            if (end != sourceSpans[i].End
                || (HangulRegex.IsMatch(translatedText.AsSpan(start, length).ToString())
                    && HasWordsAfter(sourceText!, sourceSpans[i].Start + sourceSpans[i].Length)
                    && !HasWordsAfter(translatedText, start + length)))
            {
                return translatedText;
            }
        }

        var builder = new StringBuilder(translatedText.Length);
        var last = 0;
        for (var i = 0; i < translatedSpans.Count; i++)
        {
            var (start, length, _) = translatedSpans[i];
            builder.Append(translatedText, last, start - last).Append(sourceText, sourceSpans[i].Start, sourceSpans[i].Length);
            last = start + length;
        }

        var restored = builder.Append(translatedText, last, translatedText.Length - last).ToString();
        return string.Equals(restored, translatedText, StringComparison.Ordinal) ? translatedText : restored;
    }

    // Letters before the next tag after the position.
    private static bool HasWordsAfter(string text, int position)
    {
        var next = text.IndexOf('<', position);
        for (var i = position; i < (next < 0 ? text.Length : next); i++)
        {
            if (char.IsLetter(text[i]))
            {
                return true;
            }
        }

        return false;
    }
}
