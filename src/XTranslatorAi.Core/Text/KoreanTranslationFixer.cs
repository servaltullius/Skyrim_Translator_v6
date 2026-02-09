using System;
using System.Text.RegularExpressions;
using XTranslatorAi.Core.Text.KoreanFix.Internal;
using XTranslatorAi.Core.Text.KoreanFix.Internal.Steps;

namespace XTranslatorAi.Core.Text;

/// <summary>
/// Small, conservative post-edits for common Korean artifacts in game effect strings.
/// Intentionally narrow: avoids broad grammar rewriting.
/// </summary>
internal static class KoreanTranslationFixer
{
    private static readonly Regex DuplicateEffectWordRegex = new(
        pattern: @"효과\s+효과",
        options: RegexOptions.CultureInvariant
    );

    private static readonly IKoreanFixStep[] StepPipeline =
    {
        new ParenthesizedParticleStep(),
        new AttachedSeparatedParticleStep(),
        new StatAndSubjectParticleStep(),
        new DurationProbabilityStep(),
        new ArtifactCleanupStep(),
    };

    internal static string Fix(string targetLang, string text)
    {
        if (!IsKoreanLanguage(targetLang))
        {
            return text;
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            return text;
        }

        var context = new KoreanFixContext(targetLang);
        var working = RemoveInvisibleSeparators(text);

        // Collapse common duplicated words from LLM outputs (e.g., "효과 효과").
        if (working.IndexOf("효과", StringComparison.Ordinal) >= 0)
        {
            working = DuplicateEffectWordRegex.Replace(working, "효과");
        }

        foreach (var step in StepPipeline)
        {
            working = step.Apply(context, working);
        }

        return working;
    }

    private static string RemoveInvisibleSeparators(string text)
    {
        // Some LLM outputs contain invisible Unicode separators that break simple regex matching.
        // Keep this conservative: strip only the most common zero-width characters.
        return text.Replace("\u200B", "", StringComparison.Ordinal)
            .Replace("\uFEFF", "", StringComparison.Ordinal)
            .Replace("\u2060", "", StringComparison.Ordinal);
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
