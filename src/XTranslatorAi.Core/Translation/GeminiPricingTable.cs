using System;

namespace XTranslatorAi.Core.Translation;

internal static class GeminiPricingTable
{
    public static bool TryGetPricing(string modelName, out GeminiPricing pricing)
    {
        // Pricing source: https://ai.google.dev/gemini-api/docs/pricing (accessed 2026-01-17)
        // Only include models we actively recommend for this app.
        var m = GeminiModelPolicy.NormalizeModelName(modelName);

        if (m.StartsWith("gemini-2.5-flash-lite", StringComparison.OrdinalIgnoreCase))
        {
            pricing = new GeminiPricing(
                InputUsdPer1M: 0.10,
                OutputUsdPer1M: 0.40,
                BatchInputUsdPer1M: 0.05,
                BatchOutputUsdPer1M: 0.20,
                CacheUsdPer1M: 0.01,
                CacheStorageUsdPer1MPerHour: 1.00,
                BatchSupportsContextCaching: false
            );
            return true;
        }

        if (GeminiModelPolicy.IsGemini3FlashPreview(m))
        {
            pricing = new GeminiPricing(
                InputUsdPer1M: 0.50,
                OutputUsdPer1M: 3.00,
                BatchInputUsdPer1M: 0.25,
                BatchOutputUsdPer1M: 1.50,
                CacheUsdPer1M: 0.05,
                CacheStorageUsdPer1MPerHour: 1.00,
                BatchSupportsContextCaching: false
            );
            return true;
        }

        pricing = default;
        return false;
    }
}

