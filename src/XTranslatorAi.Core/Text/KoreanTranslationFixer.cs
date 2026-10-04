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
        // The second word must end there, alone or with a particle, as in the quality check's duplicate rule:
        // "효과 효과적으로" is a different word and became "효과적으로".
        pattern: @"효과\s+효과(?=$|[^가-힣]|(?:가|는|를|와|로|의|에|에서|도|만)(?![가-힣]))",
        options: RegexOptions.CultureInvariant
    );

    private static readonly IKoreanFixStep[] StepPipeline =
    {
        new ParenthesizedParticleStep(),
        new AttachedSeparatedParticleStep(),
        new StatAndSubjectParticleStep(),
        new DurationProbabilityStep(),
        new ArtifactCleanupStep(),
        new SpellingFixStep(),
    };

    internal static string Fix(string targetLang, string text)
    {
        if (!LanguageHelper.IsKoreanLanguage(targetLang))
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
        => TranslationConstants.RemoveInvisibleSeparators(text);

}
