using System;

namespace XTranslatorAi.Core.Translation;

public static class GeminiPricingTable
{
    // Standard text rates, not Free Tier eligibility or an actual billing quote.
    // https://ai.google.dev/gemini-api/docs/pricing (verified 2026-09-28)
    public static DateOnly VerifiedOn { get; } = new(2026, 9, 28);

    public static bool TryGetPricing(string modelName, out GeminiPricing pricing)
        => TryGetPricing(modelName, DateOnly.FromDateTime(DateTime.UtcNow), out pricing);

    public static bool TryGetPricing(string modelName, DateOnly effectiveDate, out GeminiPricing pricing)
    {
        // Explicit IDs keep future models, image/TTS variants and moving aliases
        // from silently inheriting another model's price.
        var model = GeminiModelPolicy.NormalizeModelName(modelName).ToLowerInvariant();
        switch (model)
        {
            case "gemini-3.8-flash":
            case "gemini-3.7-flash":
            case "gemini-3.6-flash":
                pricing = effectiveDate < new DateOnly(2027, 1, 1)
                    ? TextPricing(0.75, 3.75, 0.075, 0.50)
                    : TextPricing(1.50, 7.50, 0.15, 1.00);
                return true;
            case "gemini-3.5-flash":
                pricing = TextPricing(1.50, 9.00, 0.15, 1.00);
                return true;
            case "gemini-3.5-flash-lite":
                pricing = TextPricing(0.30, 2.50, 0.03, 1.00);
                return true;
            case "gemini-3.1-flash-lite":
                pricing = TextPricing(0.25, 1.50, 0.025, 1.00);
                return true;
            case "gemini-2.5-flash":
                pricing = TextPricing(0.30, 2.50, 0.03, 1.00);
                return true;
            case "gemini-2.5-flash-lite":
                pricing = TextPricing(0.10, 0.40, 0.01, 1.00);
                return true;
            default:
                pricing = default;
                return false;
        }
    }

    private static GeminiPricing TextPricing(double input, double output, double cache, double storage)
        => new(input, output, input / 2, output / 2, cache, storage, BatchSupportsContextCaching: true);
}
