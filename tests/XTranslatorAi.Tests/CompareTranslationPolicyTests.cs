using System.Reflection;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public class CompareTranslationPolicyTests
{
    [Theory]
    [InlineData("gemini-3.8-flash", "low", null)]
    [InlineData("gemini-3.1-flash-lite", "minimal", null)]
    [InlineData("gemini-3-flash-preview", "low", null)]
    [InlineData("gemini-2.5-flash-lite", null, 0)]
    [InlineData("custom-model", null, null)]
    public void ThinkingMinimization_UsesOnlyModelSupportedSetting(string model, string? level, int? budget)
    {
        var request = new CompareTranslationService.Request(
            GeminiClient: null!, ProjectDb: null, ApiKey: "test", ModelName: model, ThinkingOff: true,
            SourceLang: "english", TargetLang: "korean", SystemPrompt: "test", SourceText: "Iron Sword",
            Edid: null, Rec: "WEAP:FULL", BatchSize: 1, MaxChars: 1000, Parallel: 1, MaxOutputTokens: 1024,
            UseRecStyleHints: false, EnableRepairPass: false, EnableSessionTermMemory: false,
            SemanticRepairMode: PlaceholderSemanticRepairMode.Soft, EnableTemplateFixer: false, KeepSkyrimTagsRaw: true,
            EnableDialogueContextWindow: false, EnablePromptCache: false, EnableRiskyCandidateRerank: false,
            RiskyCandidateCount: 2, IncludeProjectGlossary: false, GlobalGlossary: null, GlobalTranslationMemory: null);

        var built = (TranslateIdsRequest)typeof(CompareTranslationService)
            .GetMethod("BuildTranslateRequest", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, new object[] { request, CancellationToken.None })!;

        Assert.Equal(level, built.ThinkingConfigOverride?.ThinkingLevel);
        Assert.Equal(budget, built.ThinkingConfigOverride?.ThinkingBudget);
        if (level == null && budget == null) Assert.Null(built.ThinkingConfigOverride);
    }
}
