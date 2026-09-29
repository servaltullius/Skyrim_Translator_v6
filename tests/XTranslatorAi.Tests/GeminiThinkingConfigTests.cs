using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class GeminiThinkingConfigTests
{
    [Theory]
    [InlineData("gemini-3.8-flash", "low")]
    [InlineData("models/gemini-3.8-flash", "low")]
    [InlineData("gemini-3.1-flash-lite", "minimal")]
    [InlineData("models/gemini-3.1-flash-lite", "minimal")]
    public void StableDefaults_UseSameThinkingInTranslationAndEstimation(string model, string expected)
    {
        Assert.Equal(expected, TranslationService.GetThinkingConfigForModel(model)?.ThinkingLevel);
        Assert.Equal(expected, TranslationCostEstimator.GetThinkingConfigForModel(model)?.ThinkingLevel);
    }

    [Theory]
    [InlineData("gemini-3.0-flash-preview")]
    [InlineData("models/gemini-3.0-flash-preview")]
    [InlineData("gemini-3-flash-preview")]
    [InlineData("models/gemini-3-flash-preview")]
    public void GetThinkingConfigForModel_Gemini3Flash_UsesModelDefault(string modelName)
    {
        var config = TranslationService.GetThinkingConfigForModel(modelName);
        Assert.Null(config);
    }

    [Theory]
    [InlineData("gemini-3.0-flash-preview")]
    [InlineData("models/gemini-3.0-flash-preview")]
    [InlineData("gemini-3-flash-preview")]
    [InlineData("models/gemini-3-flash-preview")]
    public void CostEstimator_GetThinkingConfigForModel_Gemini3Flash_UsesModelDefault(string modelName)
    {
        var config = TranslationCostEstimator.GetThinkingConfigForModel(modelName);
        Assert.Null(config);
    }

    [Theory]
    [InlineData("gemini-3.1-flash-lite-preview", "high")]
    [InlineData("models/gemini-3.1-flash-lite-preview", "high")]
    public void GetThinkingConfigForModel_Gemini3FlashLite_UsesHighThinkingLevel(string modelName, string expectedThinkingLevel)
    {
        var config = TranslationService.GetThinkingConfigForModel(modelName);

        Assert.NotNull(config);
        Assert.Null(config!.ThinkingBudget);
        Assert.Equal(expectedThinkingLevel, config.ThinkingLevel);
    }

    [Theory]
    [InlineData("gemini-3-pro-preview", "low")]
    [InlineData("models/gemini-3-pro-preview", "low")]
    public void GetThinkingConfigForModel_Gemini3Pro_UsesLowThinkingLevel(string modelName, string expectedThinkingLevel)
    {
        var config = TranslationService.GetThinkingConfigForModel(modelName);

        Assert.NotNull(config);
        Assert.Null(config!.ThinkingBudget);
        Assert.Equal(expectedThinkingLevel, config.ThinkingLevel);
    }

    [Theory]
    [InlineData("gemini-2.5-flash", 0)]
    [InlineData("models/gemini-2.5-flash", 0)]
    public void GetThinkingConfigForModel_Gemini25Flash_DisablesThinkingByBudget(string modelName, int expectedThinkingBudget)
    {
        var config = TranslationService.GetThinkingConfigForModel(modelName);

        Assert.NotNull(config);
        Assert.Equal(expectedThinkingBudget, config!.ThinkingBudget);
    }

    [Theory]
    [InlineData("gemini-3.0-flash-preview", false)]
    [InlineData("gemini-3-flash-preview", false)]
    [InlineData("gemini-3.1-flash-lite-preview", false)]
    [InlineData("gemini-3-pro-preview", false)]
    [InlineData("models/gemini-3-flash-preview", false)]
    [InlineData("gemini-2.5-flash", true)]
    [InlineData("gemini-2.0-flash", true)]
    [InlineData("models/gemini-2.5-flash", true)]
    public void SupportsMultipleCandidates_ReturnsExpected(string modelName, bool expected)
    {
        Assert.Equal(expected, GeminiModelPolicy.SupportsMultipleCandidates(modelName));
    }
}
