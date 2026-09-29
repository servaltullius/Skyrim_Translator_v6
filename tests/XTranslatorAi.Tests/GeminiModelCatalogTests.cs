using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public sealed class GeminiModelCatalogTests
{
    [Theory]
    [InlineData("gemini-3.8-flash", true)]
    [InlineData("models/gemini-3.5-flash-lite", true)]
    [InlineData("gemma-4-31b-it", true)]
    [InlineData("gemini-3.8-flash-tts", false)]
    [InlineData("gemini-3.1-flash-image", false)]
    [InlineData("gemini-3.1-flash-lite-image", false)]
    [InlineData("gemini-3.8-live", false)]
    [InlineData("gemini-omni-1.1-flash", false)]
    public void TranslationChoices_ExcludeNonTextEndpoints(string name, bool usable)
    {
        var model = new GeminiModel(name, null, null, null, null, new List<string> { "generateContent" });
        Assert.Equal(usable, GeminiModelCatalog.TryGetTextTranslationModelName(model, out _));
    }

    [Fact]
    public void ModelWithoutGenerateContent_IsNotAUsableTranslationChoice()
    {
        var model = new GeminiModel("gemini-embedding-2", null, null, null, null, new List<string> { "embedContent" });
        Assert.False(GeminiModelCatalog.TryGetTextTranslationModelName(model, out _));
    }

    [Fact]
    public void Presets_UseStableModelsAndKeepDistinctLowCostChoices()
    {
        Assert.Equal("gemini-3.8-flash", GeminiModelCatalog.PreferredModels[0]);
        Assert.Equal(new[] { "gemini-3.1-flash-lite", "gemini-3.5-flash-lite" }, GeminiModelCatalog.LowCostModels);
        Assert.DoesNotContain(GeminiModelCatalog.PreferredModels, model => model.Contains("preview", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("gemini-3.8-flash")]
    [InlineData("gemini-3.7-flash")]
    [InlineData("gemini-3.5-flash-lite")]
    [InlineData("gemini-3.1-flash-lite")]
    public void Gemini3SamplingControls_AreOmittedAndLowThinkingNeverUsesBudgetZero(string model)
    {
        Assert.Null(GeminiTranslationPolicy.GetTemperatureForTranslation(model, 0.1));
        Assert.False(GeminiModelPolicy.SupportsMultipleCandidates(model));
        var lowThinking = GeminiTranslationPolicy.GetLowThinkingConfigForTranslation(model);
        Assert.NotNull(lowThinking);
        Assert.Null(lowThinking.ThinkingBudget);
        Assert.Equal(model == GeminiModelCatalog.LowCostModel ? "minimal" : "low", lowThinking.ThinkingLevel);
    }

    [Fact]
    public void Gemini25Flash_LowThinkingUsesSupportedBudgetZero()
    {
        var lowThinking = GeminiTranslationPolicy.GetLowThinkingConfigForTranslation("gemini-2.5-flash");
        Assert.Equal(0, lowThinking!.ThinkingBudget);
        Assert.Null(lowThinking.ThinkingLevel);
        Assert.Null(GeminiTranslationPolicy.GetLowThinkingConfigForTranslation("unknown"));
    }

    [Theory]
    [InlineData("gemini-flash-latest")]
    [InlineData("models/gemini-flash-lite-latest")]
    [InlineData("gemini-pro-latest")]
    [InlineData("gemini-4.0-flash")]
    [InlineData("gemini-2.5-flash-new-variant")]
    [InlineData("gemma-4-31b-it")]
    [InlineData("unknown")]
    public void UnknownModelsAndMutableAliases_DoNotInheritLegacySamplingControls(string model)
    {
        Assert.Null(GeminiTranslationPolicy.GetTemperatureForTranslation(model, 0.1));
        Assert.False(GeminiModelPolicy.SupportsMultipleCandidates(model));
        Assert.False(GeminiPricingTable.TryGetPricing(model, new DateOnly(2026, 9, 28), out _));
    }

    [Theory]
    [InlineData("gemini-2.5-flash", 0.1)]
    [InlineData("models/gemini-2.5-pro", 0.1)]
    [InlineData("gemini-2.5-flash-lite", null)]
    [InlineData("gemini-2.0-flash", 0.1)]
    public void KnownLegacyModels_RetainSupportedSamplingPolicy(string model, double? temperature)
    {
        Assert.Equal(temperature, GeminiTranslationPolicy.GetTemperatureForTranslation(model, 0.1));
        Assert.True(GeminiModelPolicy.SupportsMultipleCandidates(model));
    }
}
