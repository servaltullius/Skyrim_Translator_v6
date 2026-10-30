using System;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Which rows may define a term (a short name in a name field) and how a term is keyed, for the run's session terms and
/// the project context scan alike. The scan had a copy that let dialogue in: a short INFO line a person edited was
/// taken for a term's definition.
/// </summary>
internal static class SessionTermRules
{
    internal static readonly Regex EnglishPhrase = new(
        pattern:
            @"\b(?:[A-Z][A-Za-z0-9'’\-]*)(?:\s+(?:[A-Z][A-Za-z0-9'’\-]*|of|the|and|or|to|a|an|in|on|for|with|from|at|by|de|la|le|du|van|von))+\b",
        options: RegexOptions.CultureInvariant
    );

    internal static readonly Regex EnglishTitleWord = new(
        pattern: @"\b[A-Z][A-Za-z0-9'’\-]{2,}\b",
        options: RegexOptions.CultureInvariant
    );

    internal static bool IsTermRec(string? rec)
    {
        if (string.IsNullOrWhiteSpace(rec))
        {
            return false;
        }

        // Dialogue text is not a term definition, even when it is short and lacks punctuation.
        if (TranslationRowRules.IsDialogue(rec))
        {
            return false;
        }

        return rec.IndexOf(":FULL", StringComparison.OrdinalIgnoreCase) >= 0
            || rec.IndexOf(":NAME", StringComparison.OrdinalIgnoreCase) >= 0
            || rec.IndexOf(":NAM", StringComparison.OrdinalIgnoreCase) >= 0
            || rec.IndexOf(":TITLE", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    internal static bool IsDefinitionText(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return false;
        }

        var s = sourceText.Trim();
        if (s.Length is < 3 or > 60)
        {
            return false;
        }

        if (s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0)
        {
            return false;
        }

        if (TranslationConstants.XtTokenRegex.IsMatch(s))
        {
            return false;
        }

        if (s.IndexOf(' ') < 0 && !IsSingleWordCandidate(s))
        {
            return false;
        }

        // Avoid obvious sentences.
        if (s.IndexOfAny(new[] { '.', '!', '?', ':', ';' }) >= 0)
        {
            return false;
        }

        // Keep conservative: allow letters/digits/spaces plus common name punctuation.
        foreach (var ch in s)
        {
            if (char.IsLetterOrDigit(ch) || ch is ' ' or '-' or '\'' or '’')
            {
                continue;
            }
            return false;
        }

        return true;
    }

    internal static bool IsSingleWordCandidate(string term)
    {
        if (string.IsNullOrWhiteSpace(term))
        {
            return false;
        }

        var s = term.Trim();
        if (s.Length is < 3 or > 40)
        {
            return false;
        }

        // Require TitleCase-ish; automatic learning also filters common UI/mechanics words.
        if (s[0] is < 'A' or > 'Z')
        {
            return false;
        }

        var hasLower = false;
        for (var i = 0; i < s.Length; i++)
        {
            var ch = s[i];
            if (ch is >= 'a' and <= 'z')
            {
                hasLower = true;
                break;
            }
        }

        return hasLower;
    }

    internal static bool IsTranslationText(string translatedText)
    {
        if (string.IsNullOrWhiteSpace(translatedText))
        {
            return false;
        }

        var s = translatedText.Trim();
        if (s.Length is < 1 or > 80)
        {
            return false;
        }

        if (s.IndexOf('\r') >= 0 || s.IndexOf('\n') >= 0)
        {
            return false;
        }

        if (TranslationConstants.XtTokenRegex.IsMatch(s))
        {
            return false;
        }

        return true;
    }

    internal static string NormalizeKey(string term)
    {
        var s = (term ?? "").Trim();
        if (string.IsNullOrWhiteSpace(s))
        {
            return "";
        }

        if (s.StartsWith("The ", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(4).TrimStart();
        }
        else if (s.StartsWith("An ", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(3).TrimStart();
        }
        else if (s.StartsWith("A ", StringComparison.OrdinalIgnoreCase))
        {
            s = s.Substring(2).TrimStart();
        }

        return s.Trim();
    }
}
