using System;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Text;

/// <summary>
/// Fixes common LLM artifacts around percent signs after unmasking (e.g. "50%%" / "% %0f").
/// </summary>
internal static class PercentSignFixer
{
    private static readonly Regex DuplicatePercentRegex = new(
        pattern: @"%(?:\s*%)+",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex StrayPercentAfterPercentPlaceholderRegex = new(
        pattern: @"(?<ph>[+-]?<\s*[0-9]+(?:\.[0-9]+)?\s*%\s*>)(?:\s*%)+",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex StrayPercentAfterWordRegex = new(
        pattern: @"(?<=[가-힣A-Za-z])%(?=(?:\s|[,.!?…:;""'”’\)\]\}]|$))",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex PercentPointGarbageRegex = new(
        pattern: @"(?<pct>\b[0-9]+(?:\.[0-9]+)?%)\s*포인트(?<post>(?:의|가|이|을|를|은|는|도|만|까지|부터)?\b)?",
        options: RegexOptions.CultureInvariant
    );

    internal static string FixDuplicatePercents(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        // Some LLM outputs contain invisible Unicode separators that break simple regex matching
        // (e.g., "<25%>​%" where the zero-width char prevents stray-percent cleanup).
        text = RemoveInvisibleSeparators(text);

        if (text.IndexOf('%') < 0)
        {
            return text;
        }

        var working = DuplicatePercentRegex.Replace(text, "%");
        working = StrayPercentAfterPercentPlaceholderRegex.Replace(
            working,
            m => m.Groups["ph"].Value
        );

        // Clean up common percent-related hallucinations/typos in Korean outputs.
        // - "10%포인트" (percentage points) is rarely intended in this domain and is often a model artifact.
        // - "밀어치기%" is almost always a stray percent sign.
        if (working.IndexOf("포인트", StringComparison.Ordinal) >= 0)
        {
            working = PercentPointGarbageRegex.Replace(
                working,
                m => m.Groups["pct"].Value + m.Groups["post"].Value
            );
        }

        working = StrayPercentAfterWordRegex.Replace(working, "");
        return working;
    }

    private static string RemoveInvisibleSeparators(string text)
    {
        if (text.IndexOf('\u200B') < 0 && text.IndexOf('\uFEFF') < 0 && text.IndexOf('\u2060') < 0)
        {
            return text;
        }

        return text.Replace("\u200B", "", StringComparison.Ordinal)
            .Replace("\uFEFF", "", StringComparison.Ordinal)
            .Replace("\u2060", "", StringComparison.Ordinal);
    }
}
