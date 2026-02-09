using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static readonly Regex NumericXtTokenBadParticleRegex = new(
        pattern: @"(?<t>__XT_PH_(?:MAG|NUM)_[0-9]{4}__)\s*(?:에게서|에게|에서|으로|로)",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex RawMagOrBurBadParticleRegex = new(
        pattern: @"(?<t>[+-]?<\s*(?:mag|bur)\s*>)\s*(?:에게서|에게|에서|으로|로)",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex RawMagTagRegex = new(
        pattern: @"[+-]?<\s*mag\s*>",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex RawDurTagRegex = new(
        pattern: @"[+-]?<\s*dur\s*>",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static string RepairMagDurSemanticMixups(string outputText, string inputText)
    {
        // For Skyrim localization, <mag> (magnitude) and <dur> (duration) are runtime numeric placeholders used in
        // magic effect descriptions. Models sometimes swap these, producing e.g. "<mag>초" or "<dur>%".
        // If it looks like a straight swap and we can repair deterministically, do so to avoid costly retries.
        if (!TryGetSingleMagAndDurPlaceholders(inputText, out var magToken, out var durToken))
        {
            return outputText;
        }

        if (outputText.IndexOf(magToken, StringComparison.Ordinal) < 0 || outputText.IndexOf(durToken, StringComparison.Ordinal) < 0)
        {
            return outputText;
        }

        if (!LooksLikeMagDurSwap(outputText, magToken, durToken))
        {
            return outputText;
        }

        return SwapTokens(outputText, magToken, durToken);
    }

    private static string RepairKoreanBadParticlesOnNumericPlaceholders(string outputText, string inputText)
    {
        // Korean models sometimes attach particles directly to numeric placeholders, producing things like:
        // - "__XT_PH_MAG_0000__에게" / "<mag>에서" (treating a number like a person/place)
        // These are almost always wrong. Remove the particle but keep the placeholder.
        if (string.IsNullOrWhiteSpace(outputText) || string.IsNullOrWhiteSpace(inputText))
        {
            return outputText;
        }

        var working = outputText;

        if (inputText.Contains("__XT_PH_MAG_", StringComparison.Ordinal) || inputText.Contains("__XT_PH_NUM_", StringComparison.Ordinal))
        {
            working = NumericXtTokenBadParticleRegex.Replace(working, m => m.Groups["t"].Value);
        }

        if (inputText.IndexOf("<mag", StringComparison.OrdinalIgnoreCase) >= 0
            || inputText.IndexOf("<bur", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            working = RawMagOrBurBadParticleRegex.Replace(working, m => m.Groups["t"].Value);
        }

        return working;
    }

    private static string RepairDurTokenMisplacedAfterKoreanTimePhrase(string outputText, string inputText)
    {
        // Some models produce Korean like "늑대인간초 동안 __XT_PH_DUR_0000__의 ...", which treats the duration
        // token like a noun ("...의") instead of a time value. Repair conservatively by moving the DUR token to
        // the front of the "초 동안" phrase: "__XT_PH_DUR_0000__초 동안 늑대인간의 ..."
        if (string.IsNullOrWhiteSpace(outputText) || string.IsNullOrWhiteSpace(inputText))
        {
            return outputText;
        }

        if (outputText.IndexOf('초') < 0 && outputText.IndexOf("동안", StringComparison.Ordinal) < 0)
        {
            return outputText;
        }

        var durToken = TryGetSingleDurToken(inputText);
        if (durToken == null)
        {
            return outputText;
        }

        var esc = Regex.Escape(durToken);

        var subjectPattern = @"(?<subject>[\p{L}\p{N}][\p{L}\p{N} \-'\u2019]{0,40})";
        var timeUnitPattern = @"(?:초간|초|분|시간|일|주|개월|년)";

        // Case A: "...초 동안 DUR의 ..." -> "DUR초 동안 ...의 ..."
        var patternA = subjectPattern + @"\s*초\s*동안\s*" + esc + @"\s*" + timeUnitPattern + @"?\s*의";
        var replaced = Regex.Replace(
            outputText,
            patternA,
            m => $"{durToken}초 동안 {m.Groups["subject"].Value.Trim()}의",
            RegexOptions.CultureInvariant
        );

        // Case B: "...초 동안 DUR ..." (no '의') -> "DUR초 동안 ..."
        var patternB = subjectPattern + @"\s*초\s*동안\s*" + esc + @"\s*" + timeUnitPattern + @"?";
        replaced = Regex.Replace(
            replaced,
            patternB,
            m => $"{durToken}초 동안 {m.Groups["subject"].Value.Trim()}",
            RegexOptions.CultureInvariant
        );

        return replaced;
    }

    private static string? TryGetSingleDurToken(string inputText)
    {
        var expectedTokens = ExtractTokens(inputText);
        var durToken = default(string);
        foreach (var t in expectedTokens)
        {
            if (!t.StartsWith("__XT_PH_DUR_", StringComparison.Ordinal))
            {
                continue;
            }

            if (durToken == null)
            {
                durToken = t;
                continue;
            }

            if (!string.Equals(durToken, t, StringComparison.Ordinal))
            {
                return null;
            }
        }

        if (durToken != null)
        {
            return durToken;
        }

        // Support raw <dur> mode (no __XT_PH_DUR_####__ token in the input).
        var rawMatches = RawDurTagRegex.Matches(inputText);
        if (rawMatches.Count == 1)
        {
            return "<dur>";
        }

        return null;
    }

    private static bool TryGetSingleMagAndDurPlaceholders(string inputText, out string magToken, out string durToken)
    {
        magToken = "";
        durToken = "";

        var expectedTokens = ExtractTokens(inputText);
        var magTokens = new List<string>();
        var durTokens = new List<string>();
        foreach (var t in expectedTokens)
        {
            if (t.StartsWith("__XT_PH_MAG_", StringComparison.Ordinal))
            {
                if (!magTokens.Contains(t))
                {
                    magTokens.Add(t);
                }
            }
            else if (t.StartsWith("__XT_PH_DUR_", StringComparison.Ordinal))
            {
                if (!durTokens.Contains(t))
                {
                    durTokens.Add(t);
                }
            }
        }

        if (magTokens.Count == 1 && durTokens.Count == 1)
        {
            magToken = magTokens[0];
            durToken = durTokens[0];
            return true;
        }

        // Support raw <mag>/<dur> mode (no __XT_PH_* tokens in the input).
        var rawMag = RawMagTagRegex.Matches(inputText);
        var rawDur = RawDurTagRegex.Matches(inputText);
        if (rawMag.Count == 1 && rawDur.Count == 1)
        {
            magToken = "<mag>";
            durToken = "<dur>";
            return true;
        }

        return false;
    }

    private static bool LooksLikeMagDurSwap(string text, string magToken, string durToken)
    {
        // Strong signals:
        // - MAG next to time words ("초", "동안") => likely wrong
        // - DUR next to amount words ("%", "만큼", "점/포인트", "피해") => likely wrong
        var magInTime = IsTokenInTimeContext(text, magToken);
        var durInAmount = IsTokenInAmountContext(text, durToken);
        if (!magInTime || !durInAmount)
        {
            return false;
        }

        // If both tokens also appear in their correct contexts, don't guess.
        var durInTime = IsTokenInTimeContext(text, durToken);
        var magInAmount = IsTokenInAmountContext(text, magToken);
        return !(durInTime && magInAmount);
    }

    private static string SwapTokens(string text, string a, string b)
    {
        const string tmp = "__XT_SWAP_TMP__";
        var working = text.Replace(a, tmp, StringComparison.Ordinal);
        working = working.Replace(b, a, StringComparison.Ordinal);
        working = working.Replace(tmp, b, StringComparison.Ordinal);
        return working;
    }
}
