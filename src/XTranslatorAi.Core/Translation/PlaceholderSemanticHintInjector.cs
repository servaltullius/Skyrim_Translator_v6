using System;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

/// <summary>
/// Adds/removes temporary semantic hint markers next to placeholder tokens to help the model
/// treat MAG/NUM as numeric magnitudes and DUR as time, especially for Korean.
/// These markers are NOT persisted; they are stripped from model output before saving.
/// </summary>
internal static class PlaceholderSemanticHintInjector
{
    // Rare bracket markers unlikely to appear in Skyrim strings.
    private const string HintPrefix = "⟦";
    private const string HintSuffix = "⟧";

    private static readonly Regex PlaceholderTokenRegex = new(
        pattern: @"__XT_PH_(?<kind>MAG|DUR|NUM)_[0-9]{4}__",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex HintRegex = new(
        pattern: @"⟦XT_(?:MAG|DUR|NUM)=[0-9]+⟧",
        options: RegexOptions.CultureInvariant
    );

    internal static bool ShouldInject(string targetLang, string text)
    {
        if (!IsKoreanLanguage(targetLang))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        // Keep this narrow: it is intended for short effect strings.
        if (text.Length > 1200)
        {
            return false;
        }

        return text.Contains("__XT_PH_MAG_", StringComparison.Ordinal)
               || text.Contains("__XT_PH_DUR_", StringComparison.Ordinal)
               || text.Contains("__XT_PH_NUM_", StringComparison.Ordinal);
    }

    internal static string Inject(string targetLang, string text)
    {
        if (!ShouldInject(targetLang, text))
        {
            return text;
        }

        return PlaceholderTokenRegex.Replace(
            text,
            m =>
            {
                var kind = m.Groups["kind"].Value;
                var sample = kind switch
                {
                    "MAG" => 100,
                    "DUR" => 5,
                    "NUM" => 10,
                    _ => 0,
                };

                return m.Value + HintPrefix + "XT_" + kind + "=" + sample + HintSuffix;
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

