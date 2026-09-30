using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.Tests;

public sealed class ComparePluginDialogueTests
{
    [Theory]
    [InlineData(true, "INFO:NAM1", true)]
    [InlineData(true, "DIAL:FULL", true)]
    [InlineData(true, " info:rnam ", true)]
    [InlineData(false, "INFO:NAM1", false)]
    [InlineData(false, "DIAL:FULL", false)]
    [InlineData(true, "WEAP:FULL", false)]
    [InlineData(false, "WEAP:FULL", false)]
    public async Task Compare_UsesModelForDirectDialogueAndRetainsTmForXmlOrItems(
        bool directPlugin, string rec, bool shouldGenerate)
    {
        var client = new FakeGeminiClient();
        var service = new CompareTranslationService(
            new ProjectGlossaryService(new GlossaryImportService(new GlossaryFileService())));
        var tm = new Dictionary<string, string> { ["yes."] = "예." };
        var request = new CompareTranslationService.Request(
            GeminiClient: client, ProjectDb: null, ApiKey: "test", ModelName: "gemini-3.8-flash", ThinkingOff: true,
            SourceLang: "english", TargetLang: "korean", SystemPrompt: "Translate.", SourceText: "Yes.",
            Edid: null, Rec: rec, BatchSize: 1, MaxChars: 1000, Parallel: 1, MaxOutputTokens: 1024,
            UseRecStyleHints: false, EnableRepairPass: false, EnableSessionTermMemory: false,
            SemanticRepairMode: PlaceholderSemanticRepairMode.Off, EnableTemplateFixer: false, KeepSkyrimTagsRaw: true,
            EnableDialogueContextWindow: true, EnablePromptCache: false, EnableRiskyCandidateRerank: false,
            RiskyCandidateCount: 2, IncludeProjectGlossary: false, GlobalGlossary: null, GlobalTranslationMemory: tm,
            IsDirectPluginSource: directPlugin);

        var result = await service.RunAsync(request, CancellationToken.None);

        Assert.True(result.HasRow);
        Assert.Equal(StringEntryStatus.Done, result.Status);
        Assert.Equal(shouldGenerate ? 1 : 0, client.GenerateCalls);
        Assert.Equal(!shouldGenerate, result.TranslationMemoryHit);
        Assert.Equal(shouldGenerate ? "모델 번역입니다." : "예.", result.DestText);
        // Skipping TM for one comparison must not change the caller's memory or settings.
        Assert.Same(tm, request.GlobalTranslationMemory);
        Assert.Equal("예.", Assert.Single(tm).Value);
    }

    private sealed class FakeGeminiClient : IGeminiClient
    {
        public int GenerateCalls { get; private set; }

        public Task<string> GenerateContentAsync(string apiKey, string modelName,
            GeminiGenerateContentRequest request, CancellationToken cancellationToken)
        {
            GenerateCalls++;
            return Task.FromResult("모델 번역입니다. __XT_PH_9999__");
        }

        public async Task<IReadOnlyList<string>> GenerateContentCandidatesAsync(string apiKey, string modelName,
            GeminiGenerateContentRequest request, CancellationToken cancellationToken)
            => new[] { await GenerateContentAsync(apiKey, modelName, request, cancellationToken) };

        public Task<int> CountTokensAsync(string apiKey, string modelName, string text, CancellationToken cancellationToken)
            => Task.FromResult(32);

        public Task<IReadOnlyList<GeminiModel>> ListModelsAsync(string apiKey, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Comparison must not discover models.");

        public Task<string> CreateCachedContentAsync(string apiKey, string modelName, string systemInstructionText,
            TimeSpan ttl, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Comparison test has prompt caching disabled.");

        public Task DeleteCachedContentAsync(string apiKey, string cacheName, CancellationToken cancellationToken)
            => throw new InvalidOperationException("Comparison test has prompt caching disabled.");
    }
}
