using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static bool NeedsPlaceholderSemanticRepair(
        string inputText,
        string outputText,
        string targetLang,
        PlaceholderSemanticRepairMode mode
    )
    {
        if (!ShouldConsiderPlaceholderSemanticRepair(inputText, outputText, targetLang, mode))
        {
            return false;
        }

        var tokens = ExtractPlaceholderTokenSets(inputText);
        if (!tokens.HasAny)
        {
            return false;
        }

        return HasPlaceholderSemanticIssues(outputText, tokens, mode);
    }

    private static bool ShouldConsiderPlaceholderSemanticRepair(
        string inputText,
        string outputText,
        string targetLang,
        PlaceholderSemanticRepairMode mode
    )
    {
        if (mode == PlaceholderSemanticRepairMode.Off)
        {
            return false;
        }

        // Keep this conservative to avoid unnecessary extra requests.
        // This is primarily for Korean magic-effect descriptions where __XT_PH_DUR_####__ must be used as time,
        // and __XT_PH_MAG_####__/__XT_PH_NUM_####__ must be used as numeric magnitude.
        if (!IsKoreanLanguage(targetLang))
        {
            return false;
        }

        if (string.IsNullOrWhiteSpace(inputText) || string.IsNullOrWhiteSpace(outputText))
        {
            return false;
        }

        // Avoid expensive repair on very long texts; long book texts are chunked already and are less likely
        // to contain these Skyrim magic-effect placeholders.
        return inputText.Length <= 2000;
    }

    private readonly record struct PlaceholderTokenSets(
        HashSet<string> MagTokens,
        HashSet<string> DurTokens,
        HashSet<string> NumTokens
    )
    {
        public bool HasAny => MagTokens.Count > 0 || DurTokens.Count > 0 || NumTokens.Count > 0;
    }

    private static PlaceholderTokenSets ExtractPlaceholderTokenSets(string inputText)
    {
        var expectedTokens = ExtractTokens(inputText);
        var magTokens = new HashSet<string>(StringComparer.Ordinal);
        var durTokens = new HashSet<string>(StringComparer.Ordinal);
        var numTokens = new HashSet<string>(StringComparer.Ordinal);

        foreach (var t in expectedTokens)
        {
            if (t.StartsWith("__XT_PH_DUR_", StringComparison.Ordinal))
            {
                durTokens.Add(t);
            }
            else if (t.StartsWith("__XT_PH_MAG_", StringComparison.Ordinal))
            {
                magTokens.Add(t);
            }
            else if (t.StartsWith("__XT_PH_NUM_", StringComparison.Ordinal))
            {
                numTokens.Add(t);
            }
        }

        // Some modes keep Skyrim runtime placeholders visible (e.g., "<mag>", "<dur>").
        // Treat them like semantic placeholders so repair triggers still work.
        if (inputText.IndexOf("<dur>", StringComparison.OrdinalIgnoreCase) >= 0
            || inputText.IndexOf("< dur", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            durTokens.Add("<dur>");
        }

        if (inputText.IndexOf("<mag>", StringComparison.OrdinalIgnoreCase) >= 0
            || inputText.IndexOf("< mag", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            magTokens.Add("<mag>");
        }

        if (inputText.IndexOf("<bur>", StringComparison.OrdinalIgnoreCase) >= 0
            || inputText.IndexOf("< bur", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            // Not a standard vanilla tag, but treat it as a magnitude-like numeric placeholder.
            magTokens.Add("<bur>");
        }

        return new PlaceholderTokenSets(magTokens, durTokens, numTokens);
    }

    private static bool HasPlaceholderSemanticIssues(
        string outputText,
        PlaceholderTokenSets tokens,
        PlaceholderSemanticRepairMode mode
    )
    {
        return HasDurationSemanticIssues(outputText, tokens)
            || HasMagnitudeSemanticIssues(outputText, tokens, mode)
            || HasNumericSemanticIssues(outputText, tokens, mode);
    }

    private static bool HasDurationSemanticIssues(string outputText, PlaceholderTokenSets tokens)
    {
        foreach (var dur in tokens.DurTokens)
        {
            if (outputText.IndexOf(dur, StringComparison.Ordinal) < 0)
            {
                return true;
            }

            // Duration should appear with time words like "초/동안".
            if (!IsTokenInTimeContext(outputText, dur))
            {
                return true;
            }

            // Duration should not be used like an amount ("%/포인트/만큼").
            if (IsTokenInAmountContext(outputText, dur))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasMagnitudeSemanticIssues(
        string outputText,
        PlaceholderTokenSets tokens,
        PlaceholderSemanticRepairMode mode
    )
    {
        foreach (var mag in tokens.MagTokens)
        {
            if (outputText.IndexOf(mag, StringComparison.Ordinal) < 0)
            {
                return true;
            }

            // Magnitude should not be used as time.
            if (IsTokenInTimeContext(outputText, mag))
            {
                return true;
            }

            // Magnitude should not be treated like a standalone noun in Korean (e.g., "__XT_PH_MAG__와(과)", "__XT_PH_MAG__을", "__XT_PH_MAG__에게").
            if (mode == PlaceholderSemanticRepairMode.Strict && IsNumericTokenInBadParticleContext(outputText, mag))
            {
                return true;
            }
        }

        return false;
    }

    private static bool HasNumericSemanticIssues(
        string outputText,
        PlaceholderTokenSets tokens,
        PlaceholderSemanticRepairMode mode
    )
    {
        foreach (var num in tokens.NumTokens)
        {
            if (outputText.IndexOf(num, StringComparison.Ordinal) < 0)
            {
                return true;
            }

            // Numeric placeholders are almost always magnitudes, not durations.
            if (IsTokenInTimeContext(outputText, num))
            {
                return true;
            }

            if (mode == PlaceholderSemanticRepairMode.Strict && IsNumericTokenInBadParticleContext(outputText, num))
            {
                return true;
            }
        }

        return false;
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

    private static bool IsTokenInTimeContext(string text, string token)
    {
        var esc = Regex.Escape(token);
        // token + (초/분/시간/동안)
        if (Regex.IsMatch(text, esc + @"\s*(초간|초|분|시간|일|주|개월|년|동안|간)", RegexOptions.CultureInvariant))
        {
            return true;
        }
        // (동안/초) + token (often indicates the token is being used as time)
        if (Regex.IsMatch(text, @"(초간|초|분|시간|일|주|개월|년|동안)\s*" + esc, RegexOptions.CultureInvariant))
        {
            return true;
        }
        return false;
    }

    private static bool IsTokenInAmountContext(string text, string token)
    {
        var esc = Regex.Escape(token);
        if (Regex.IsMatch(text, esc + @"\s*(%|퍼센트|만큼|점|포인트|수치)", RegexOptions.CultureInvariant))
        {
            return true;
        }
        if (Regex.IsMatch(text, esc + @"\s*의\s*(피해|회복|흡수)", RegexOptions.CultureInvariant))
        {
            return true;
        }
        if (Regex.IsMatch(text, esc + @".{0,8}(피해|회복|증가|감소|강화|약화)", RegexOptions.CultureInvariant))
        {
            return true;
        }
        return false;
    }

    private static bool IsNumericTokenInBadParticleContext(string text, string token)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        var esc = Regex.Escape(token);

        // Strong signals that the numeric token is being treated as a noun phrase.
        if (Regex.IsMatch(text, esc + @"\s*(?:을\(를\)|을|를)", RegexOptions.CultureInvariant))
        {
            return true;
        }
        if (Regex.IsMatch(text, esc + @"\s*(?:에게|한테|께)", RegexOptions.CultureInvariant))
        {
            return true;
        }
        if (Regex.IsMatch(text, esc + @"\s*(?:와\(과\)|과|와)\s*(?:체력|매지카|지구력)", RegexOptions.CultureInvariant))
        {
            return true;
        }

        return false;
    }
}
