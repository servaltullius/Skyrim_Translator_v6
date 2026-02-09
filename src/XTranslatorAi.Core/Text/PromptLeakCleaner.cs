using System;

namespace XTranslatorAi.Core.Text;

public static class PromptLeakCleaner
{
    public static string StripLeakedPlaceholderInstructions(string sourceText, string translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText) || string.IsNullOrWhiteSpace(sourceText))
        {
            return translatedText;
        }

        // If the source itself contains these concepts, don't try to strip them from the translation.
        if (ContainsAny(sourceText, "placeholder", "token", "__XT_", "자리표시자", "토큰"))
        {
            return translatedText;
        }

        var idx = FindLeakStartIndex(translatedText);
        if (idx < 0)
        {
            return translatedText;
        }

        var tail = translatedText.Substring(idx);
        if (!LooksLikeLeakTail(tail))
        {
            return translatedText;
        }

        var prefix = translatedText.Substring(0, idx).TrimEnd();
        prefix = TrimTrailingSeparators(prefix);
        return prefix;
    }

    private static int FindLeakStartIndex(string text)
    {
        var idx = IndexOfIgnoreCase(text, "Do NOT modify");
        if (idx >= 0)
        {
            return idx;
        }

        idx = IndexOfIgnoreCase(text, "Do not modify");
        if (idx >= 0)
        {
            return idx;
        }

        idx = IndexOfIgnoreCase(text, "Preserve all placeholders");
        if (idx >= 0)
        {
            return idx;
        }

        idx = IndexOfIgnoreCase(text, "placeholder token");
        if (idx >= 0)
        {
            return idx;
        }

        idx = IndexOfIgnoreCase(text, "__XT_PH_");
        if (idx >= 0)
        {
            return idx;
        }

        idx = IndexOfIgnoreCase(text, "__XT_TERM_");
        if (idx >= 0)
        {
            return idx;
        }

        idx = text.IndexOf("자리표시자", StringComparison.Ordinal);
        if (idx >= 0)
        {
            return idx;
        }

        idx = text.IndexOf("서식 자리표시자", StringComparison.Ordinal);
        if (idx >= 0)
        {
            return idx;
        }

        return -1;
    }

    private static bool LooksLikeLeakTail(string tail)
    {
        if (string.IsNullOrWhiteSpace(tail))
        {
            return false;
        }

        // English instruction leak
        if (IndexOfIgnoreCase(tail, "placeholder") >= 0
            && (IndexOfIgnoreCase(tail, "Do not") >= 0
                || IndexOfIgnoreCase(tail, "Preserve") >= 0
                || IndexOfIgnoreCase(tail, "Output only") >= 0))
        {
            return true;
        }

        // Korean instruction leak (common in our app when a domains/template block gets echoed)
        if (tail.IndexOf("자리표시자", StringComparison.Ordinal) >= 0
            && (tail.IndexOf("토큰", StringComparison.Ordinal) >= 0
                || tail.IndexOf("순서", StringComparison.Ordinal) >= 0
                || tail.IndexOf("유지", StringComparison.Ordinal) >= 0
                || tail.IndexOf("제거", StringComparison.Ordinal) >= 0
                || tail.IndexOf("변경", StringComparison.Ordinal) >= 0
                || tail.IndexOf("줄바꿈", StringComparison.Ordinal) >= 0
                || tail.IndexOf("페이지", StringComparison.Ordinal) >= 0))
        {
            return true;
        }

        return false;
    }

    private static bool ContainsAny(string text, params string[] needles)
    {
        foreach (var n in needles)
        {
            if (!string.IsNullOrEmpty(n) && IndexOfIgnoreCase(text, n) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static int IndexOfIgnoreCase(string haystack, string needle)
        => haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase);

    private static string TrimTrailingSeparators(string s)
    {
        if (string.IsNullOrEmpty(s))
        {
            return s;
        }

        var trimmed = s;
        while (trimmed.Length > 0)
        {
            var last = trimmed[^1];
            if (last is ' ' or '\t' or '/' or '-' or '•' or '·' or ':' or ';')
            {
                trimmed = trimmed.Substring(0, trimmed.Length - 1).TrimEnd();
                continue;
            }

            break;
        }

        return trimmed;
    }
}

