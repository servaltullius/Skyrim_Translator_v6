using System;
using System.Collections.Generic;
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

    // Protected percent text: named variables (%PLAYERNAME%) and printf specs (%d, %.1f, %s).
    private static readonly Regex ProtectedPercentRegex = new(
        pattern: @"%[A-Za-z0-9_]+%|%(?:[0-9]+\$)?[-+0-9.]*[A-Za-z]",
        options: RegexOptions.CultureInvariant
    );

    private const char ShieldOpen = '\uE000';
    private const char ShieldClose = '\uE001';

    /// <param name="sourceText">
    /// The source, so its protected percent text survives: the stray-percent rule cut "%PLAYERNAME%." to
    /// "%PLAYERNAME." and the duplicate rule turned "%d%%" into "%d%", and the final check then failed those rows
    /// on every retry.
    /// </param>
    internal static string FixDuplicatePercents(string text, string? sourceText = null)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var shielded = new List<string>();
        var working = ProtectedPercentRegex.Replace(text, m =>
        {
            var isNamedVariable = m.Value.Length > 2 && m.Value[^1] == '%';
            if (sourceText == null ? !isNamedVariable : sourceText.IndexOf(m.Value, StringComparison.Ordinal) < 0)
            {
                return m.Value;
            }

            shielded.Add(m.Value);
            return $"{ShieldOpen}{shielded.Count - 1}{ShieldClose}";
        });

        var keepDoubledPercent = sourceText?.Contains("%%", StringComparison.Ordinal) == true;
        working = FixUnprotectedPercents(working, keepDoubledPercent);
        for (var i = shielded.Count - 1; i >= 0; i--)
        {
            working = working.Replace($"{ShieldOpen}{i}{ShieldClose}", shielded[i], StringComparison.Ordinal);
        }

        return working;
    }

    private static string FixUnprotectedPercents(string text, bool keepDoubledPercent)
    {

        // Some LLM outputs contain invisible Unicode separators that break simple regex matching
        // (e.g., "<25%>​%" where the zero-width char prevents stray-percent cleanup).
        text = RemoveInvisibleSeparators(text);

        if (text.IndexOf('%') < 0)
        {
            return text;
        }

        var working = keepDoubledPercent ? text : DuplicatePercentRegex.Replace(text, "%");
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
        => TranslationConstants.RemoveInvisibleSeparators(text);
}
