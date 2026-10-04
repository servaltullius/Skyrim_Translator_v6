using System;

namespace XTranslatorAi.Core.Text;

public static partial class PairedSlashListExpander
{
    private static int SkipWhitespace(string text, int index)
    {
        while (index < text.Length && char.IsWhiteSpace(text[index]))
        {
            index++;
        }
        return index;
    }

    private static bool IsNumericPlaceholderToken(string token)
    {
        // We only expand numeric percent lists, which are masked as __XT_PH_NUM_####__.
        if (string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var s = token.AsSpan();
        if (!s.StartsWith("__XT_PH_NUM_", StringComparison.Ordinal))
        {
            return false;
        }

        if (!s.EndsWith("__", StringComparison.Ordinal))
        {
            return false;
        }

        // __XT_PH_NUM_0000__
        if (s.Length != "__XT_PH_NUM_0000__".Length)
        {
            return false;
        }

        for (var i = "__XT_PH_NUM_".Length; i < "__XT_PH_NUM_".Length + 4; i++)
        {
            var c = s[i];
            if (c < '0' || c > '9')
            {
                return false;
            }
        }

        return true;
    }

    private static bool IsValidLabelItem(string item)
    {
        if (string.IsNullOrWhiteSpace(item))
        {
            return false;
        }

        // If this contains '/', we failed to isolate list items (counts mismatch or unexpected structure).
        if (item.Contains('/', StringComparison.Ordinal))
        {
            return false;
        }

        // A masked line break, tag or value inside means the item ran into the next line.
        if (item.Contains("__XT_PH_", StringComparison.Ordinal))
        {
            return false;
        }

        return true;
    }

    private static bool IsXtTokenStart(string text, int index)
        => string.CompareOrdinal(text, index, "__XT_", 0, 5) == 0;

    private static int FindLabelItemEnd(string text, int start)
    {
        if (start < 0)
        {
            return 0;
        }

        if (start >= text.Length)
        {
            return text.Length;
        }

        // A label may itself be a glossary term ("__XT_TERM_0002__"); any other token ends it. Line breaks are
        // masked, so "…of One-handed/Two-handed/Archery⏎Requires Smithing 50" used to take
        // "Archery⏎Requires Smithing 50" as the last label.
        var idx = start;
        var leadingTerm = TranslationConstants.XtTokenRegex.Match(text, start);
        if (leadingTerm.Success && leadingTerm.Index == start && text.AsSpan(start).StartsWith("__XT_TERM_", StringComparison.Ordinal))
        {
            idx += leadingTerm.Length;
        }

        // Stop at punctuation that usually ends the label list, or before "skill/level" suffix.
        while (idx < text.Length)
        {
            var c = text[idx];
            if (c is ',' or '.' or ';' or ':' or ')' or '(' or '\r' or '\n')
            {
                return idx;
            }

            if (IsXtTokenStart(text, idx) || IsSuffixWordStart(text, idx))
            {
                return idx;
            }

            idx++;
        }

        return text.Length;
    }

    private static bool IsSuffixWordStart(string text, int index)
    {
        if (index <= 0 || index >= text.Length)
        {
            return false;
        }

        // Only treat as suffix boundary at a word boundary.
        var prev = text[index - 1];
        var curr = text[index];
        if (char.IsLetterOrDigit(prev) || prev == '_')
        {
            return false;
        }

        // Common suffixes after skill names: "skill", "skills", "level", "levels".
        return StartsWithWord(text, index, "skill")
               || StartsWithWord(text, index, "skills")
               || StartsWithWord(text, index, "level")
               || StartsWithWord(text, index, "levels");
    }

    private static bool StartsWithWord(string text, int index, string word)
    {
        if (index < 0 || index + word.Length > text.Length)
        {
            return false;
        }

        if (!text.AsSpan(index, word.Length).Equals(word, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        var after = index + word.Length;
        if (after >= text.Length)
        {
            return true;
        }

        // A masked line break right after the word ("skill__XT_PH_0004__") is a boundary too.
        var c = text[after];
        return (!char.IsLetterOrDigit(c) && c != '_') || IsXtTokenStart(text, after);
    }

    private static int FindSuffixEnd(string text, int start)
    {
        var idx = start;
        while (idx < text.Length)
        {
            var c = text[idx];
            // A token ends the suffix too: "…/Archery skill⏎Requires Smithing 50" keeps the next line out.
            if (c is ',' or '.' or ';' or ':' or ')' or '(' or '\r' or '\n' || IsXtTokenStart(text, idx))
            {
                return idx;
            }

            idx++;
        }

        return text.Length;
    }
}
