using System.Reflection;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class GeminiThinkingConfigTests
{
    [Theory]
    [InlineData("gemini-3.0-flash-preview")]
    [InlineData("models/gemini-3.0-flash-preview")]
    [InlineData("gemini-3-flash-preview")]
    [InlineData("models/gemini-3-flash-preview")]
    public void GetThinkingConfigForModel_Gemini3Flash_UsesModelDefault(string modelName)
    {
        var method = typeof(TranslationService).GetMethod(
            "GetThinkingConfigForModel",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.NotNull(method);

        var config = (GeminiThinkingConfig?)method!.Invoke(null, new object?[] { modelName });

        Assert.Null(config);
    }

    [Theory]
    [InlineData("gemini-3.0-flash-preview")]
    [InlineData("models/gemini-3.0-flash-preview")]
    [InlineData("gemini-3-flash-preview")]
    [InlineData("models/gemini-3-flash-preview")]
    public void CostEstimator_GetThinkingConfigForModel_Gemini3Flash_UsesModelDefault(string modelName)
    {
        var method = typeof(TranslationCostEstimator).GetMethod(
            "GetThinkingConfigForModel",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.NotNull(method);

        var config = (GeminiThinkingConfig?)method!.Invoke(null, new object?[] { modelName });

        Assert.Null(config);
    }

    [Theory]
    [InlineData("gemini-3-pro-preview", "low")]
    [InlineData("models/gemini-3-pro-preview", "low")]
    public void GetThinkingConfigForModel_Gemini3Pro_UsesLowThinkingLevel(string modelName, string expectedThinkingLevel)
    {
        var method = typeof(TranslationService).GetMethod(
            "GetThinkingConfigForModel",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.NotNull(method);

        var config = (GeminiThinkingConfig?)method!.Invoke(null, new object?[] { modelName });

        Assert.NotNull(config);
        Assert.Null(config!.ThinkingBudget);
        Assert.Equal(expectedThinkingLevel, config.ThinkingLevel);
    }

    [Theory]
    [InlineData("gemini-2.5-flash", 0)]
    [InlineData("models/gemini-2.5-flash", 0)]
    public void GetThinkingConfigForModel_Gemini25Flash_DisablesThinkingByBudget(string modelName, int expectedThinkingBudget)
    {
        var method = typeof(TranslationService).GetMethod(
            "GetThinkingConfigForModel",
            BindingFlags.NonPublic | BindingFlags.Static
        );

        Assert.NotNull(method);

        var config = (GeminiThinkingConfig?)method!.Invoke(null, new object?[] { modelName });

        Assert.NotNull(config);
        Assert.Equal(expectedThinkingBudget, config!.ThinkingBudget);
    }
}
