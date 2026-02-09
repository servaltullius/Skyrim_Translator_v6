using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

internal static class GlossarySemanticHintInjector
{
    private const string HintPrefix = "⟦";
    private const string HintSuffix = "⟧";

    private static readonly Regex TermTokenRegex = new(
        pattern: @"__XT_TERM(?:_[A-Z0-9]+)?_[0-9]{4}__",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex HintRegex = new(
        pattern: @"⟦XT_TERM=[^⟧]{0,80}⟧",
        options: RegexOptions.CultureInvariant
    );

    internal static bool ShouldInject(string targetLang, string text, IReadOnlyDictionary<string, string>? tokenToReplacement)
    {
        if (!IsKoreanLanguage(targetLang))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        if (tokenToReplacement == null || tokenToReplacement.Count == 0)
        {
            return false;
        }

        if (text.Length > 2000)
        {
            return false;
        }

        return text.Contains("__XT_TERM_", StringComparison.Ordinal);
    }

    internal static string Inject(string targetLang, string text, IReadOnlyDictionary<string, string>? tokenToReplacement)
    {
        if (!ShouldInject(targetLang, text, tokenToReplacement))
        {
            return text;
        }

        return TermTokenRegex.Replace(
            text,
            m =>
            {
                var token = m.Value;
                if (!tokenToReplacement!.TryGetValue(token, out var replacement) || string.IsNullOrWhiteSpace(replacement))
                {
                    return token;
                }

                var hint = SanitizeHintValue(replacement);
                if (hint.Length == 0)
                {
                    return token;
                }

                return token + HintPrefix + "XT_TERM=" + hint + HintSuffix;
            }
        );
    }

    internal static string Strip(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        if (text.IndexOf(HintPrefix, StringComparison.Ordinal) < 0)
        {
            return text;
        }

        return HintRegex.Replace(text, "");
    }

    private static string SanitizeHintValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return "";
        }

        var s = value.Replace("\r", " ", StringComparison.Ordinal)
            .Replace("\n", " ", StringComparison.Ordinal)
            .Replace(HintPrefix, "", StringComparison.Ordinal)
            .Replace(HintSuffix, "", StringComparison.Ordinal)
            .Trim();

        if (s.Length == 0)
        {
            return "";
        }

        if (s.Length > 60)
        {
            s = s.Substring(0, 60).TrimEnd();
        }

        return s;
    }

    private static bool IsKoreanLanguage(string lang)
    {
        if (string.IsNullOrWhiteSpace(lang))
        {
            return false;
        }

        var s = lang.Trim();
        if (string.Equals(s, "korean", StringComparison.OrdinalIgnoreCase) || string.Equals(s, "ko", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (s.StartsWith("ko-", StringComparison.OrdinalIgnoreCase) || s.StartsWith("ko_", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (s.IndexOf("korean", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return string.Equals(s, "한국어", StringComparison.OrdinalIgnoreCase)
               || s.IndexOf("한국", StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
