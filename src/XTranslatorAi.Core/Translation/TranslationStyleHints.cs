using System;

namespace XTranslatorAi.Core.Translation;

public static class TranslationStyleHints
{
    public static string? Get(string sourceText, string? rec)
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

            if (r == "BOOK" && GetRecSubtype(rec) is "FULL" or "NAME" or "TITLE")
            {
                return "REC=BOOK title. Translate as a book title or short noun phrase, not a narrative sentence. Preserve the source meaning and punctuation.";
            }

            if (r == "BOOK")
            {
                styleHint = "REC=BOOK (in-game book/lore/guide). Use a consistent written narrative tone in Korean (문어체). Prefer 서술체(…다/…한다) and keep sentence endings consistent. Avoid casual fillers like \"말이지/야/해/지\" except inside quoted dialogue. Avoid adding explanatory parentheses like \"(English term)\" unless they exist in the source; prefer natural in-universe rendering for proper nouns.";
            }

            if (r is "INFO" or "DIAL")
            {
                styleHint = r == "DIAL" && GetRecSubtype(rec) == "FULL"
                    ? "REC=DIAL:FULL (dialogue topic label). Translate as a short topic label, not a spoken response."
                    : "REC=INFO/DIAL (dialogue/subtitles). Use natural spoken Korean. Keep register consistent (do not randomly switch between 존댓말/반말 within this item unless the source clearly switches).";
            }

            if (r == "QUST")
            {
                styleHint = GetRecSubtype(rec) switch
                {
                    "FULL" => "REC=QUST:FULL (quest title). Translate as a concise title; do not force an objective-style -하기 ending.",
                    "NNAM" => "REC=QUST:NNAM (quest objective). Use a concise action phrase (prefer ~하기 in Korean) and preserve the target and completion condition.",
                    "CNAM" => "REC=QUST:CNAM (quest journal). Preserve the source tense and whether events are pending or completed; do not rewrite it as an instruction.",
                    _ => "REC=QUST (quest text). Choose the style from the source meaning; preserve tense and conditions.",
                };
            }

            if (r == "MESG")
            {
                styleHint = "REC=MESG (UI message). Keep it short, clear, and game-UI friendly. Avoid long literary phrasing.";
            }

            // Effect and item descriptions mixed 합니다체 with noun endings ("…도약합니다. 대검에 사용 가능.") in
            // 48 of 50 War Ash rows; the reviews and the official descriptions use 합니다체 throughout.
            if (styleHint == null && GetRecSubtype(rec) is "DESC" or "DNAM"
                && r is "MGEF" or "SPEL" or "PERK" or "ENCH" or "SCRL" or "SHOU" or "WEAP" or "ARMO" or "AMMO" or "ALCH" or "INGR" or "MISC")
            {
                styleHint = "REC=*:DESC/DNAM (effect or item description shown in menus). Write every sentence in 합니다체 "
                            + "(-ㅂ니다/-습니다), including short fragments: 'Usable on two-handed weapons.' → '양손 무기에 사용할 수 있습니다.' "
                            + "Do not end a sentence with a bare noun such as '사용 가능.' "
                            + "'N% of X' is a share, not an increase: '80% of the effect' → '효과의 80%'.";
            }

            // Item/object name records: ACTI, MISC, WEAP, ARMO, AMMO, INGR, ALCH, FLOR, CONT, FURN, DOOR
            // with :FULL or :NAME subtype → short noun phrase style.
            // Exclude QUST (quest names can be verb-like) and NPC_ (nicknames).
            if (styleHint == null
                && r is "ACTI" or "MISC" or "WEAP" or "ARMO" or "AMMO" or "INGR" or "ALCH" or "FLOR" or "CONT" or "FURN" or "DOOR")
            {
                var subtype = GetRecSubtype(rec);
                if (subtype is "FULL" or "NAME")
                {
                    styleHint = "REC=*:FULL/NAME (item/object name). 짧은 명사구로 번역. "
                                + "동사형 어미(-하기, -하다, -됩니다) 금지. 예: '부서진 문', '낡은 전등', '탐험가 발굴'";
                }
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

    private static string? GetRecSubtype(string? rec)
    {
        if (string.IsNullOrWhiteSpace(rec))
        {
            return null;
        }

        var colon = rec.IndexOf(':');
        if (colon < 0 || colon + 1 >= rec.Length)
        {
            return null;
        }

        return rec[(colon + 1)..].Trim().ToUpperInvariant();
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
