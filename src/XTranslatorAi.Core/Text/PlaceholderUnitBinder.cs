using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

/// <summary>
/// Post-translation helpers for unit words (seconds/points/per second) in short Skyrim effect strings.
/// Intentionally narrow and currently only enabled for Korean translation flows.
/// </summary>
internal static class PlaceholderUnitBinder
{
    private const string UnitSecondsTag = "<XT_SEC>";
    private const string UnitPerSecondTag = "<XT_PER_SEC>";
    private const string UnitPointsTag = "<XT_PT>";

    private static readonly Regex PerSecondRegex = new(
        pattern: @"\b(?:per|every|each)\s+second\b",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex AnyPlaceholderRegex = new(
        pattern: @"[+-]?<[^>]+>",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex PlaceholderSecondsRegex = new(
        pattern: @"(?<ph>[+-]?<[^>]+>)\s*seconds?\b",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex NumberSecondsRegex = new(
        pattern: @"(?<n>\b[0-9]+(?:\.[0-9]+)?\b)\s*seconds?\b",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex PlaceholderPointsRegex = new(
        pattern: @"(?<ph>[+-]?<[^>]+>)\s*points?\b",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex NumberPointsRegex = new(
        pattern: @"(?<n>\b[0-9]+(?:\.[0-9]+)?\b)\s*points?\b",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex TightenValueUnitSpacingRegex = new(
        pattern: @"(?<v>[+-]?<[^>]+>|\b[0-9]+(?:\.[0-9]+)?\b)\s+(?<unit>초|포인트)\b",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex SourceHasPointsRegex = new(
        pattern: @"\bpoints?\b",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex PlaceholderPointsKoRegex = new(
        pattern: @"(?<tok>[+-]?<[^>]+>)\s*포인트(?<post>(?:의|가|이|을|를|은|는|도|만|까지|부터)?\b)?",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex NumericAngleRegex = new(
        pattern: @"^(?<sign>[+-]?)<\s*(?<n>[0-9]+)\s*>$",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex DurAngleRegex = new(
        pattern: @"^[+-]?<\s*dur\s*>$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex MagAngleRegex = new(
        pattern: @"^[+-]?<\s*mag\s*>$",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private static readonly Regex HasPerSecondKoRegex = new(
        pattern: @"(?:초\s*당|매\s*초|초\s*마다)\b",
        options: RegexOptions.CultureInvariant
    );

    internal static string InjectUnitsForTranslation(string targetLang, string sourceText)
    {
        // Do not hide unit words behind internal tags before calling the model.
        // Keeping unit words visible improves semantic accuracy (e.g., <dur> is recognized as time).
        return sourceText;
    }

    internal static string EnforceUnitsFromSource(string targetLang, string sourceText, string translatedText)
    {
        if (ShouldSkipUnitEnforcement(targetLang, sourceText, translatedText))
        {
            return translatedText;
        }

        // Some LLM outputs contain invisible Unicode separators that break simple regex matching
        // (e.g., "<dur>​초" where the zero-width char prevents unit detection, causing duplicates).
        var working = RemoveInvisibleSeparators(translatedText);
        var plan = AnalyzeUnitEnforcementPlan(sourceText, working);
        if (!plan.NeedsSecondsFromSource && !plan.NeedsPerSecond && !plan.ShouldStripPointsAfterPlaceholder)
        {
            return working;
        }

        return ApplyUnitEnforcementPlan(sourceText, working, plan);
    }

    private static bool ShouldSkipUnitEnforcement(string targetLang, string sourceText, string translatedText)
    {
        if (!IsKoreanLanguage(targetLang))
        {
            return true;
        }

        if (string.IsNullOrWhiteSpace(sourceText) || string.IsNullOrWhiteSpace(translatedText))
        {
            return true;
        }

        // Keep this narrow and cheap: intended for short magic-effect style strings.
        return sourceText.Length > 2000 || translatedText.Length > 2000;
    }

    private static UnitEnforcementPlan AnalyzeUnitEnforcementPlan(string sourceText, string translatedText)
    {
        // Community translations usually avoid "포인트" unless the source explicitly uses "points".
        // Strip "<mag>포인트" / "<40>포인트" artifacts when the English source does not mention points.
        var shouldStripPointsAfterPlaceholder =
            translatedText.IndexOf('<') >= 0
            && translatedText.IndexOf("포인트", StringComparison.Ordinal) >= 0
            && !SourceHasPointsRegex.IsMatch(sourceText);

        var needsSecondsFromSource = false;
        var needsDurSeconds = false;
        HashSet<string>? numericSeconds = null;

        foreach (Match m in PlaceholderSecondsRegex.Matches(sourceText))
        {
            var ph = m.Groups["ph"].Value;
            if (string.IsNullOrWhiteSpace(ph))
            {
                continue;
            }

            needsSecondsFromSource = true;
            if (DurAngleRegex.IsMatch(ph))
            {
                needsDurSeconds = true;
                continue;
            }

            var numeric = NumericAngleRegex.Match(ph);
            if (numeric.Success)
            {
                numericSeconds ??= new HashSet<string>(StringComparer.Ordinal);
                numericSeconds.Add(numeric.Groups["n"].Value);
            }
        }

        return new UnitEnforcementPlan(
            NeedsSecondsFromSource: needsSecondsFromSource,
            NeedsDurSeconds: needsDurSeconds,
            NumericSeconds: numericSeconds,
            NeedsPerSecond: PerSecondRegex.IsMatch(sourceText),
            ShouldStripPointsAfterPlaceholder: shouldStripPointsAfterPlaceholder
        );
    }

    private static string ApplyUnitEnforcementPlan(string sourceText, string text, UnitEnforcementPlan plan)
    {
        var working = text;

        if (plan.NeedsDurSeconds)
        {
            working = EnsureTimeUnitAfterToken(working, tokenPattern: @"[+-]?<\s*dur\s*>", unitWord: "초");
        }

        if (plan.NumericSeconds is { Count: > 0 })
        {
            foreach (var n in plan.NumericSeconds)
            {
                if (string.IsNullOrWhiteSpace(n))
                {
                    continue;
                }

                working = EnsureTimeUnitAfterToken(working, tokenPattern: @"[+-]?<\s*" + Regex.Escape(n) + @"\s*>", unitWord: "초");
            }
        }

        if (plan.NeedsPerSecond)
        {
            var rateTokenPattern = TryGetRateTokenPatternFromSource(sourceText);
            if (!string.IsNullOrWhiteSpace(rateTokenPattern))
            {
                working = EnsureRateWordBeforeToken(working, tokenPattern: rateTokenPattern, rateWord: "초당");
            }
        }

        if (plan.ShouldStripPointsAfterPlaceholder)
        {
            working = PlaceholderPointsKoRegex.Replace(
                working,
                m => m.Groups["tok"].Value + m.Groups["post"].Value
            );
        }

        return TightenUnitSpacing(working);
    }

    private static string TightenUnitSpacing(string text)
    {
        // Tighten: "<dur> 초" => "<dur>초", "<mag> 포인트" => "<mag>포인트"
        if (text.IndexOf(' ') < 0)
        {
            return text;
        }

        return TightenValueUnitSpacingRegex.Replace(
            text,
            m => m.Groups["v"].Value + m.Groups["unit"].Value
        );
    }

    private readonly record struct UnitEnforcementPlan(
        bool NeedsSecondsFromSource,
        bool NeedsDurSeconds,
        HashSet<string>? NumericSeconds,
        bool NeedsPerSecond,
        bool ShouldStripPointsAfterPlaceholder
    );

    private static string RemoveInvisibleSeparators(string text)
    {
        return text.Replace("\u200B", "", StringComparison.Ordinal)
            .Replace("\uFEFF", "", StringComparison.Ordinal)
            .Replace("\u2060", "", StringComparison.Ordinal);
    }

    private static string? TryGetRateTokenPatternFromSource(string sourceText)
    {
        if (string.IsNullOrWhiteSpace(sourceText))
        {
            return null;
        }

        var perSecond = PerSecondRegex.Match(sourceText);
        if (!perSecond.Success)
        {
            return null;
        }

        var token = "";
        foreach (Match m in AnyPlaceholderRegex.Matches(sourceText))
        {
            if (!m.Success || m.Index >= perSecond.Index)
            {
                break;
            }

            token = m.Value.Trim();
        }

        if (string.IsNullOrWhiteSpace(token))
        {
            return null;
        }

        if (MagAngleRegex.IsMatch(token))
        {
            return @"[+-]?<\s*mag\s*>";
        }

        var numeric = NumericAngleRegex.Match(token);
        if (numeric.Success)
        {
            return @"[+-]?<\s*" + Regex.Escape(numeric.Groups["n"].Value) + @"\s*>";
        }

        return null;
    }

    private static string EnsureRateWordBeforeToken(string text, string tokenPattern, string rateWord)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(tokenPattern) || string.IsNullOrWhiteSpace(rateWord))
        {
            return text;
        }

        // If the translation already expresses per-second semantics, keep it.
        if (HasPerSecondKoRegex.IsMatch(text))
        {
            return text;
        }

        var tokenRegex = new Regex(tokenPattern, RegexOptions.CultureInvariant | RegexOptions.IgnoreCase);
        var m = tokenRegex.Match(text);
        if (!m.Success)
        {
            return text;
        }

        var working = text.Insert(m.Index, rateWord + " ");

        // Improve spacing in common Korean outputs (e.g., "대상에게초당 <mag>" -> "대상에게 초당 <mag>").
        working = Regex.Replace(
            working,
            @"(?<prev>[가-힣A-Za-z])" + Regex.Escape(rateWord) + @"\b",
            m2 => m2.Groups["prev"].Value + " " + rateWord,
            RegexOptions.CultureInvariant
        );

        // Avoid duplicate spaces created by insertion.
        working = Regex.Replace(working, Regex.Escape(rateWord) + @"\s+<", rateWord + " <", RegexOptions.CultureInvariant);

        return working;
    }

    private static string EnsureTimeUnitAfterToken(string text, string tokenPattern, string unitWord)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(tokenPattern) || string.IsNullOrWhiteSpace(unitWord))
        {
            return text;
        }

        // If the token is already followed by a time unit, keep it.
        var pattern =
            @"(?<tok>"
            + tokenPattern
            + @")(?<ws>(?>\s*))(?!(초간|초|분|시간|일|주|개월|년))";

        return Regex.Replace(
            text,
            pattern,
            m => m.Groups["tok"].Value + unitWord + m.Groups["ws"].Value,
            RegexOptions.CultureInvariant
        );
    }

    private static string EnsureAmountUnitAfterToken(string text, string tokenPattern, string unitWord)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(tokenPattern) || string.IsNullOrWhiteSpace(unitWord))
        {
            return text;
        }

        // If the token is already followed by '%' or the amount unit, keep it.
        var pattern =
            @"(?<tok>"
            + tokenPattern
            + @")(?<ws>\s*)(?!(?:%|"
            + Regex.Escape(unitWord)
            + @"))";

        return Regex.Replace(
            text,
            pattern,
            m => m.Groups["tok"].Value + unitWord + m.Groups["ws"].Value,
            RegexOptions.CultureInvariant
        );
    }

    internal static string ReplaceUnitsAfterUnmask(string targetLang, string text)
    {
        if (!IsKoreanLanguage(targetLang))
        {
            return text;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        if (text.IndexOf('<') < 0)
        {
            return text;
        }

        var working = text;
        if (working.Contains(UnitPerSecondTag, StringComparison.Ordinal))
        {
            working = working.Replace(UnitPerSecondTag, "초당", StringComparison.Ordinal);
        }
        if (working.Contains(UnitPointsTag, StringComparison.Ordinal))
        {
            working = working.Replace(UnitPointsTag, "포인트", StringComparison.Ordinal);
        }
        if (working.Contains(UnitSecondsTag, StringComparison.Ordinal))
        {
            working = working.Replace(UnitSecondsTag, "초", StringComparison.Ordinal);
        }

        // Tighten: "<dur> 초" => "<dur>초", "<mag> 포인트" => "<mag>포인트"
        if (working.IndexOf(' ') >= 0)
        {
            working = TightenValueUnitSpacingRegex.Replace(
                working,
                m => m.Groups["v"].Value + m.Groups["unit"].Value
            );
        }

        return working;
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
