using System;
using System.Threading;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private readonly record struct LongTextChunkContext(
        string ApiKey,
        string ModelName,
        string SystemPrompt,
        PromptCache? PromptCache,
        string SourceLang,
        string TargetLang,
        (long Id, string Source, string Masked, MaskedText Mask, GlossaryApplication Glossary) Row,
        double Temperature,
        int MaxOutputTokens,
        int MaxRetries,
        string? StyleHint,
        CancellationToken CancellationToken
    );

    private static bool IsCjkLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return false;
        }

        var s = lang.Trim().ToLowerInvariant();
        if (s is "korean" or "japanese" or "chinese" or "zh" or "ja" or "ko")
        {
            return true;
        }

        return s.StartsWith("ko", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("ja", StringComparison.OrdinalIgnoreCase)
               || s.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
    }

    private static int GetLongTextTargetOutputTokens(int maxOutputTokens)
    {
        if (maxOutputTokens <= 0)
        {
            return 2048;
        }

        // Keep well below the model's max output tokens to reduce MAX_TOKENS truncation.
        var target = (int)Math.Floor(maxOutputTokens * 0.35);
        target = Math.Clamp(target, 512, 6000);

        // Leave some headroom for the model to finish naturally (and for sentinel / tokens).
        var headroom = Math.Min(512, Math.Max(128, maxOutputTokens / 10));
        target = Math.Min(target, Math.Max(256, maxOutputTokens - headroom));

        return Math.Max(256, target);
    }

    private static string? GuessStyleHint(string sourceText, string? rec)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return null;
        }

        string? styleHint = null;
        string? recFamily = null;

        if (!string.IsNullOrWhiteSpace(rec))
        {
            var r = rec.Trim();
            var colon = r.IndexOf(':');
            if (colon >= 0)
            {
                r = r.Substring(0, colon);
            }
            r = r.ToUpperInvariant();
            recFamily = r;

            if (r == "BOOK")
            {
                styleHint = "REC=BOOK (in-game book/lore/guide). Use a consistent written narrative tone in Korean (문어체). Prefer 서술체(…다/…한다) and keep sentence endings consistent. Avoid casual fillers like \"말이지/야/해/지\" except inside quoted dialogue. Avoid adding explanatory parentheses like \"(English term)\" unless they exist in the source; prefer natural in-universe rendering for proper nouns.";
            }

            if (r is "INFO" or "DIAL")
            {
                styleHint = "REC=INFO/DIAL (dialogue/subtitles). Use natural spoken Korean. Keep register consistent (do not randomly switch between 존댓말/반말 within this item unless the source clearly switches).";
            }

            if (r == "QUST")
            {
                styleHint = "REC=QUST (quest/journal/objective). Keep it concise and instructional. Avoid unnecessary embellishment; keep the tone consistent within the item.";
            }

            if (r == "MESG")
            {
                styleHint = "REC=MESG (UI message). Keep it short, clear, and game-UI friendly. Avoid long literary phrasing.";
            }
        }

        // Heuristic: xTranslator book exports often include [pagebreak] and book UI image tags.
        // When chunking, each chunk is translated independently; a small explicit style anchor
        // helps keep register consistent across chunks.
        if (sourceText.IndexOf("[pagebreak]", StringComparison.OrdinalIgnoreCase) >= 0
            || sourceText.IndexOf("img://Textures/Interface/Books", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            styleHint ??= "This is an in-game book/lore/guide text. Use a neutral written narrative tone in Korean and keep sentence endings consistent. Avoid chatty fillers like \"말이지/야/해\" outside of quoted dialogue. Avoid adding explanatory parentheses like \"(English term)\" unless they exist in the source; prefer natural in-universe rendering for proper nouns.";
        }

        if (styleHint == null)
        {
            return null;
        }

        var isBookLike = string.Equals(recFamily, "BOOK", StringComparison.OrdinalIgnoreCase)
                         || sourceText.IndexOf("[pagebreak]", StringComparison.OrdinalIgnoreCase) >= 0
                         || sourceText.IndexOf("img://Textures/Interface/Books", StringComparison.OrdinalIgnoreCase) >= 0;

        if (isBookLike && ContainsMultilineItalicBlock(sourceText))
        {
            styleHint +=
                "\n\nFor any <i>...</i> block that is a poem/riddle/inscription, use a solemn archaic literary register (예언/주문/비문 느낌). Prefer endings like \"…리라\", \"…지어다\", \"…것이요/…보여주리라\" and keep line breaks inside the <i> block as-is.";
        }

        return styleHint;
    }

    private static bool ContainsMultilineItalicBlock(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var start = text.IndexOf("<i>", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return false;
        }

        var end = text.IndexOf("</i>", start + 3, StringComparison.OrdinalIgnoreCase);
        if (end < 0)
        {
            return false;
        }

        var inner = text.Substring(start + 3, end - (start + 3));
        return inner.IndexOf('\n') >= 0 || inner.IndexOf('\r') >= 0;
    }
}
