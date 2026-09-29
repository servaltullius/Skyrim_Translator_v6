using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public class GeminiPricingTableTests
{
    [Theory]
    [InlineData("gemini-2.5-flash-lite", 0.10, 0.40, 0.01)]
    [InlineData("models/gemini-2.5-flash-lite", 0.10, 0.40, 0.01)]
    [InlineData("gemini-2.5-flash", 0.30, 2.50, 0.03)]
    [InlineData("gemini-3.1-flash-lite", 0.25, 1.50, 0.025)]
    [InlineData("models/gemini-3.1-flash-lite", 0.25, 1.50, 0.025)]
    [InlineData("gemini-3.5-flash-lite", 0.30, 2.50, 0.03)]
    [InlineData("gemini-3.5-flash", 1.50, 9.00, 0.15)]
    public void StableTextModel_UsesItsOwnPublishedPrice(string model, double input, double output, double cache)
    {
        Assert.True(GeminiPricingTable.TryGetPricing(model, new DateOnly(2026, 9, 28), out var pricing));
        Assert.Equal(input, pricing.InputUsdPer1M);
        Assert.Equal(output, pricing.OutputUsdPer1M);
        Assert.Equal(input / 2, pricing.BatchInputUsdPer1M);
        Assert.Equal(output / 2, pricing.BatchOutputUsdPer1M);
        Assert.Equal(cache, pricing.CacheUsdPer1M);
        Assert.Equal(1.00, pricing.CacheStorageUsdPer1MPerHour);
    }

    [Theory]
    [InlineData("gemini-3.8-flash")]
    [InlineData("gemini-3.7-flash")]
    [InlineData("gemini-3.6-flash")]
    [InlineData("models/gemini-3.8-flash")]
    public void IntroductoryPricing_ChangesOnJanuaryFirst2027(string model)
    {
        Assert.True(GeminiPricingTable.TryGetPricing(model, new DateOnly(2026, 12, 31), out var introductory));
        Assert.Equal(0.75, introductory.InputUsdPer1M);
        Assert.Equal(3.75, introductory.OutputUsdPer1M);
        Assert.Equal(0.075, introductory.CacheUsdPer1M);
        Assert.Equal(0.50, introductory.CacheStorageUsdPer1MPerHour);
        Assert.Equal(0.375, introductory.BatchInputUsdPer1M);
        Assert.Equal(1.875, introductory.BatchOutputUsdPer1M);

        Assert.True(GeminiPricingTable.TryGetPricing(model, new DateOnly(2027, 1, 1), out var regular));
        Assert.Equal(1.50, regular.InputUsdPer1M);
        Assert.Equal(7.50, regular.OutputUsdPer1M);
        Assert.Equal(0.15, regular.CacheUsdPer1M);
        Assert.Equal(1.00, regular.CacheStorageUsdPer1MPerHour);
        Assert.Equal(0.75, regular.BatchInputUsdPer1M);
        Assert.Equal(3.75, regular.BatchOutputUsdPer1M);
    }

    [Theory]
    [InlineData("unknown-model")]
    [InlineData("gemini-3.9-flash")]
    [InlineData("gemini-3.9-flash-lite")]
    [InlineData("gemini-3.5-flash-lite-image")]
    [InlineData("gemini-3.8-flash-tts")]
    [InlineData("gemini-3.1-flash-lite-preview")]
    [InlineData("gemini-3-flash-lite-preview")]
    [InlineData("gemini-2.5-flash-lite-preview-09-2025")]
    [InlineData("gemini-flash-latest")]
    public void UnverifiedOrRetiredModel_DoesNotInheritAnotherModelsPrice(string model)
        => Assert.False(GeminiPricingTable.TryGetPricing(model, new DateOnly(2026, 9, 28), out _));
}
