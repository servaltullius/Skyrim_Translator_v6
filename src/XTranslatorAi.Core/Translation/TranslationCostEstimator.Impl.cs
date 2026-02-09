using System;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationCostEstimator
{
    private static OutputTokenEstimate EstimateOutputTokensHeuristic(long inputTokens, string targetLang)
    {
        // Heuristic only: output tokens vary by language, style, and long-text chunking.
        // Keep the band wide enough to be useful without pretending to be exact.
        var isCjk = IsCjkLanguage(targetLang);
        var lowRatio = isCjk ? 0.8 : 0.7;
        var highRatio = isCjk ? 1.8 : 1.5;

        var low = (long)Math.Floor(inputTokens * lowRatio);
        var high = (long)Math.Ceiling(inputTokens * highRatio);
        var point = (long)Math.Round(inputTokens * (isCjk ? 1.25 : 1.1));
        return new OutputTokenEstimate(low, high, point, BatchRatio: null, TextRatio: null, UsedSample: false);
    }
}
