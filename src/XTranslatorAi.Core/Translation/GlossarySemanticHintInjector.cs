using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

internal static class GlossarySemanticHintInjector
{
    private const string HintPrefix = SemanticHintConstants.HintPrefix;
    private const string HintSuffix = SemanticHintConstants.HintSuffix;
    private const int LongTextLength = 2000;

    private static readonly Regex TermTokenRegex = new(
        pattern: @"__XT_TERM(?:_[A-Z0-9]+)?_[0-9]{4}__",
        options: RegexOptions.CultureInvariant
    );

    // The model sometimes closes the hint like markup ("결의⟦/XT_TERM⟧지"), so closing and bare forms go too.
    private static readonly Regex HintRegex = new(
        pattern: @"⟦/?XT_TERM(?:=[^⟧]{0,80})?⟧",
        options: RegexOptions.CultureInvariant
    );

    internal static bool ShouldInject(string targetLang, string text, IReadOnlyDictionary<string, string>? tokenToReplacement)
    {
        if (!LanguageHelper.IsKoreanLanguage(targetLang))
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

        return text.Contains("__XT_TERM_", StringComparison.Ordinal);
    }

    internal static string Inject(string targetLang, string text, IReadOnlyDictionary<string, string>? tokenToReplacement)
    {
        if (!ShouldInject(targetLang, text, tokenToReplacement))
        {
            return text;
        }

        // A long text (books) hints each token once, so the prompt does not grow with every repeat; it got no hints at all
        // over 2,000 characters, and the model saw only bare tokens for 스카이림 in Children of the Sky.
        var hinted = text.Length > LongTextLength ? new HashSet<string>(StringComparer.Ordinal) : null;
        return TermTokenRegex.Replace(
            text,
            m =>
            {
                var token = m.Value;
                if (hinted != null && !hinted.Add(token))
                {
                    return token;
                }

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
}
