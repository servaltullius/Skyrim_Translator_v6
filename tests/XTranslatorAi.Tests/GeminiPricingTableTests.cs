using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class GeminiPricingTableTests
{
    [Theory]
    [InlineData("gemini-2.5-flash-lite")]
    [InlineData("models/gemini-2.5-flash-lite")]
    [InlineData("gemini-2.5-flash-lite-preview-09-2025")]
    public void TryGetPricing_Gemini25FlashLite_ReturnsPricing(string modelName)
    {
        Assert.True(GeminiPricingTable.TryGetPricing(modelName, out var pricing));
        Assert.Equal(0.10, pricing.InputUsdPer1M);
        Assert.Equal(0.40, pricing.OutputUsdPer1M);
    }

    [Theory]
    [InlineData("gemini-3.0-flash-preview")]
    [InlineData("models/gemini-3.0-flash-preview")]
    [InlineData("gemini-3-flash-preview")]
    [InlineData("models/gemini-3-flash-preview")]
    public void TryGetPricing_Gemini3FlashPreview_ReturnsPricing(string modelName)
    {
        Assert.True(GeminiPricingTable.TryGetPricing(modelName, out var pricing));
        Assert.Equal(0.50, pricing.InputUsdPer1M);
        Assert.Equal(3.00, pricing.OutputUsdPer1M);
    }

    [Theory]
    [InlineData("gemini-3.1-flash-lite-preview")]
    [InlineData("models/gemini-3.1-flash-lite-preview")]
    public void TryGetPricing_Gemini31FlashLitePreview_ReturnsPricing(string modelName)
    {
        Assert.True(GeminiPricingTable.TryGetPricing(modelName, out var pricing));
        Assert.Equal(0.25, pricing.InputUsdPer1M);
        Assert.Equal(1.50, pricing.OutputUsdPer1M);
        Assert.Equal(0.125, pricing.BatchInputUsdPer1M);
        Assert.Equal(0.75, pricing.BatchOutputUsdPer1M);
        Assert.Equal(0.025, pricing.CacheUsdPer1M);
    }

    [Theory]
    [InlineData("gemini-3.1-flash-lite-preview")]
    [InlineData("gemini-3-flash-lite-preview")]
    public void TryGetPricing_Gemini3FlashLite_DoesNotMatchGemini3FlashPreview(string modelName)
    {
        Assert.True(GeminiPricingTable.TryGetPricing(modelName, out var pricing));
        // Lite pricing should be cheaper than non-lite flash preview
        Assert.True(pricing.InputUsdPer1M < 0.50);
        Assert.True(pricing.OutputUsdPer1M < 3.00);
    }

    [Fact]
    public void TryGetPricing_UnknownModel_ReturnsFalse()
    {
        Assert.False(GeminiPricingTable.TryGetPricing("unknown-model", out _));
    }
}
