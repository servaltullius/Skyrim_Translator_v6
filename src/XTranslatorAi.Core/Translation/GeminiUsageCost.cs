using System;

namespace XTranslatorAi.Core.Translation;

internal static class GeminiUsageCost
{
    public static double? TryEstimateUsd(string modelName, int? promptTokens, int? completionTokens, int? cachedContentTokens)
    {
        if (promptTokens is null || completionTokens is null)
        {
            return null;
        }

        if (!GeminiPricingTable.TryGetPricing(modelName, out var pricing))
        {
            return null;
        }

        var prompt = Math.Max(0, promptTokens.Value);
        var completion = Math.Max(0, completionTokens.Value);
        var cached = Math.Max(0, cachedContentTokens ?? 0);
        cached = Math.Min(cached, prompt);

        var nonCachedPrompt = prompt - cached;

        var inputUsd =
            (nonCachedPrompt / 1_000_000.0) * pricing.InputUsdPer1M
            + (cached / 1_000_000.0) * pricing.CacheUsdPer1M;

        var outputUsd = (completion / 1_000_000.0) * pricing.OutputUsdPer1M;

        return inputUsd + outputUsd;
    }
}

