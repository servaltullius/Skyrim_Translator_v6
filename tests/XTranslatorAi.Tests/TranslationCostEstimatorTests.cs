using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;
using Xunit;

namespace XTranslatorAi.Tests;

public class TranslationCostEstimatorTests
{
    [Fact]
    public async Task CacheEstimate_ChargesEveryRequestAndUsesCurrentComparisonModels()
    {
        var estimator = new TranslationCostEstimator(new StubProjectDb { RowCount = 3 }, new StubGeminiClient());
        var result = await estimator.EstimateAsync(MakeRequest("fixture", GeminiModelCatalog.DefaultModel) with { BatchSize = 1 }, CancellationToken.None);
        Assert.Equal(3, result.TextRequestCount);
        var cost = Assert.Single(result.CostEstimates, c => c.ModelName == GeminiModelCatalog.DefaultModel);
        var expected = (result.InputTokensTextPrompts * cost.InputUsdPer1M
            + result.SystemPromptTokens * 3 * cost.CacheUsdPer1M
            + result.SystemPromptTokens * cost.CacheStorageUsdPer1MPerHour * cost.PromptCacheTtlHours) / 1_000_000;
        Assert.Equal(expected, cost.InputCostUsdWithPromptCache, 10);
        Assert.Equal(new[] { GeminiModelCatalog.DefaultModel, GeminiModelCatalog.LowCostModel }, result.CostEstimates.Select(c => c.ModelName));
    }

    // Rows without letters and hidden topic identifiers are kept without a request (b81cf4b, 05ef695); the estimate
    // counted them as work.
    [Fact]
    public async Task RowsTheRunKeepsAsIs_AreNotEstimated()
    {
        var db = new StubProjectDb { RowCount = 2, ExtraRows = new[] { ("...", "MESG:DESC"), (" ", "WEAP:FULL"), ("SDA_DA09IntroTopic00", "DIAL:FULL") } };
        var result = await new TranslationCostEstimator(db, new StubGeminiClient())
            .EstimateAsync(MakeRequest("fixture", GeminiModelCatalog.DefaultModel) with { BatchSize = 1 }, CancellationToken.None);
        Assert.Equal(2, result.ItemCount);
    }

    // A row that is only forced glossary terms is finished from its tokens without a request.
    [Fact]
    public async Task RowsThatAreOnlyForcedTerms_AreNotEstimated()
    {
        var db = new StubProjectDb { RowCount = 2, ExtraRows = new[] { ("Iron Sword", "WEAP:FULL"), ("Iron Sword of Fire", "WEAP:FULL") } };
        var request = MakeRequest("fixture", GeminiModelCatalog.DefaultModel) with
        {
            BatchSize = 1,
            GlobalGlossary = new[] { new GlossaryEntry(1, null, "Iron Sword", "철검", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null) },
        };
        var result = await new TranslationCostEstimator(db, new StubGeminiClient()).EstimateAsync(request, CancellationToken.None);
        Assert.Equal(3, result.ItemCount);
    }

    // The run fills these rows without a request: the game's own translation, and the earlier translation where the
    // English did not change. With an earlier English release linked that is most of a mod update, and the estimate
    // counted every row as model work.
    [Fact]
    public async Task RowsFilledFromTheGameOrTheEarlierRelease_AreNotEstimated()
    {
        var db = new StubProjectDb
        {
            RowCount = 3,
            ExtraRows = new[] { ("Ice Storm", "SPEL:FULL") },
            ReusableIds = new[] { 1L },
        };
        var game = XTranslatorAi.Core.Plugins.GameStringDictionary.Build(new[]
        {
            new XTranslatorAi.Core.Plugins.GameStringPlugin("Skyrim.esm", Array.Empty<string>(), new[]
            {
                new XTranslatorAi.Core.Plugins.PluginField("SPEL/0002B96C/0/FULL/0", 0, "SPEL", "FULL", 0x0002B96Cu, null, 0, 0,
                    "Ice Storm", XTranslatorAi.Core.Plugins.PluginStringTableKind.Strings, 3),
            }, new Dictionary<(XTranslatorAi.Core.Plugins.PluginStringTableKind, uint), string>
            {
                [(XTranslatorAi.Core.Plugins.PluginStringTableKind.Strings, 3)] = "얼음 폭풍",
            }),
        });

        var result = await new TranslationCostEstimator(db, new StubGeminiClient())
            .EstimateAsync(MakeRequest("fixture", GeminiModelCatalog.DefaultModel) with { BatchSize = 1, GameStrings = game }, CancellationToken.None);

        Assert.Equal(2, result.ItemCount);
        Assert.Equal((1, 1), (result.GameStringRows, result.PreviousReleaseRows));
        Assert.Contains("요청 없이 채울 행: 2 (게임 번역 1, 이전 번역 그대로 1)", result.ToHumanReadableString());
    }

    // A game text that spells a project term otherwise goes to the model (Frenzy → 격분 for the patch's 광분), and the
    // estimate left it out with the rows filled without a request.
    [Fact]
    public async Task GameRowThatSpellsAProjectTermOtherwise_IsEstimated()
    {
        var db = new StubProjectDb
        {
            RowCount = 3,
            ExtraRows = new[] { ("Ice Storm", "SPEL:FULL") },
            Glossary = new[] { new GlossaryEntry(1, null, "Ice Storm", "빙결 폭풍", true, GlossaryMatchMode.WordBoundary, GlossaryForceMode.ForceToken, 10, null) },
        };
        var game = XTranslatorAi.Core.Plugins.GameStringDictionary.Build(new[]
        {
            new XTranslatorAi.Core.Plugins.GameStringPlugin("Skyrim.esm", Array.Empty<string>(), new[]
            {
                new XTranslatorAi.Core.Plugins.PluginField("SPEL/0002B96C/0/FULL/0", 0, "SPEL", "FULL", 0x0002B96Cu, null, 0, 0,
                    "Ice Storm", XTranslatorAi.Core.Plugins.PluginStringTableKind.Strings, 3),
            }, new Dictionary<(XTranslatorAi.Core.Plugins.PluginStringTableKind, uint), string>
            {
                [(XTranslatorAi.Core.Plugins.PluginStringTableKind.Strings, 3)] = "얼음 폭풍",
            }),
        });

        var result = await new TranslationCostEstimator(db, new StubGeminiClient())
            .EstimateAsync(MakeRequest("fixture", GeminiModelCatalog.DefaultModel) with { BatchSize = 1, GameStrings = game }, CancellationToken.None);

        Assert.Equal(0, result.GameStringRows);
    }

    [Fact]
    public async Task EmptyWork_HasNoCacheStorageOrGenerationCost()
    {
        var result = await new TranslationCostEstimator(new StubProjectDb(), new StubGeminiClient())
            .EstimateAsync(MakeRequest("fixture", GeminiModelCatalog.DefaultModel), CancellationToken.None);
        Assert.All(result.CostEstimates, c => Assert.Equal(0, c.TotalCostUsdHighWithPromptCache));
    }

    [Fact]
    public async Task Sample_UsesUsageRatherThanCountingVisibleText()
    {
        var client = new StubGeminiClient { CompletionTokens = 700 };
        var result = await new TranslationCostEstimator(new StubProjectDb { RowCount = 3 }, client)
            .EstimateAsync(MakeRequest("fixture", GeminiModelCatalog.DefaultModel) with { BatchSize = 1, RunSampleToEstimateOutputTokens = true }, CancellationToken.None);
        Assert.True(result.OutputTokens.UsedSample);
        // Concatenated countTokens inputs include separators; allow that small
        // extrapolation overhead, but require all three ~700-token outputs.
        Assert.InRange(result.OutputTokens.Point, 2100, 2120);
        Assert.Contains("추론 포함 사용량", result.ToHumanReadableString());
    }

    // countTokens failures fell back to "characters / 4" silently while the result still said countTokens.
    [Fact]
    public async Task FailedTokenCounts_AreEstimatedAndReported()
    {
        var client = new StubGeminiClient
        {
            CountTokensError = new GeminiHttpException("CountTokens", 503, "Service Unavailable", null, "CountTokens failed: HTTP 503"),
        };
        var result = await new TranslationCostEstimator(new StubProjectDb { RowCount = 3 }, client)
            .EstimateAsync(MakeRequest("fixture", GeminiModelCatalog.DefaultModel), CancellationToken.None);

        Assert.True(result.CountTokensFallbacks > 0);
        Assert.True(result.SystemPromptTokens > 0);
        Assert.Contains("문자 수로 추정했습니다", result.ToHumanReadableString());
    }

    [Fact]
    public async Task InvalidApiKey_IsNotHiddenByTheCharacterEstimate()
    {
        var client = new StubGeminiClient
        {
            CountTokensError = new GeminiHttpException("CountTokens", 400, "Bad Request", null,
                "CountTokens failed: HTTP 400 Bad Request. {\"error\":{\"message\":\"API key not valid. Please pass a valid API key.\"}}"),
        };

        await Assert.ThrowsAsync<GeminiHttpException>(() => new TranslationCostEstimator(new StubProjectDb { RowCount = 3 }, client)
            .EstimateAsync(MakeRequest("fixture", GeminiModelCatalog.DefaultModel), CancellationToken.None));
    }

    [Fact]
    public async Task SampleWithoutUsage_FallsBackToExplicitlyLabelledHeuristic()
    {
        var result = await new TranslationCostEstimator(new StubProjectDb { RowCount = 1 }, new StubGeminiClient())
            .EstimateAsync(MakeRequest("fixture", GeminiModelCatalog.DefaultModel) with { RunSampleToEstimateOutputTokens = true }, CancellationToken.None);
        Assert.False(result.OutputTokens.UsedSample);
        Assert.Contains("휴리스틱, 추론 미포함", result.ToHumanReadableString());
    }

    [Fact]
    public async Task CancelledSample_PropagatesCancellationAndCleansCache()
    {
        using var cancellation = new CancellationTokenSource();
        var client = new StubGeminiClient { CancelDuringSample = cancellation };
        var estimator = new TranslationCostEstimator(new StubProjectDb { RowCount = 1 }, client);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => estimator.EstimateAsync(
            MakeRequest("fixture", GeminiModelCatalog.DefaultModel) with { RunSampleToEstimateOutputTokens = true }, cancellation.Token));
        Assert.True(client.CacheDeleted);
    }

    [Fact]
    public async Task EstimateAsync_EmptyApiKey_ThrowsArgumentException()
    {
        var estimator = new TranslationCostEstimator(new StubProjectDb(), new StubGeminiClient());
        var request = MakeRequest(apiKey: "", modelName: "gemini-3.0-flash-preview");

        await Assert.ThrowsAsync<ArgumentException>(() => estimator.EstimateAsync(request, CancellationToken.None));
    }

    [Fact]
    public async Task EstimateAsync_EmptyModelName_ThrowsArgumentException()
    {
        var estimator = new TranslationCostEstimator(new StubProjectDb(), new StubGeminiClient());
        var request = MakeRequest(apiKey: "test-key", modelName: "");

        await Assert.ThrowsAsync<ArgumentException>(() => estimator.EstimateAsync(request, CancellationToken.None));
    }

    [Fact]
    public void GetThinkingConfigForModel_Gemini3Flash_ReturnsNull()
    {
        var config = TranslationCostEstimator.GetThinkingConfigForModel("gemini-3.0-flash-preview");

        Assert.Null(config);
    }

    [Fact]
    public void GetThinkingConfigForModel_Gemini3FlashLite_ReturnsHighThinking()
    {
        var config = TranslationCostEstimator.GetThinkingConfigForModel("gemini-3.1-flash-lite-preview");

        Assert.NotNull(config);
        Assert.Equal("high", config!.ThinkingLevel);
    }

    [Fact]
    public void GetThinkingConfigForModel_Gemini25Flash_DisablesThinking()
    {
        var config = TranslationCostEstimator.GetThinkingConfigForModel("gemini-2.5-flash");

        Assert.NotNull(config);
        Assert.Equal(0, config!.ThinkingBudget);
    }

    [Fact]
    public void TranslationCostEstimate_ToHumanReadableString_ContainsExpectedFields()
    {
        var estimate = new TranslationCostEstimate(
            ScopeLabel: "남은 항목(Pending+Error)",
            ModelName: "gemini-3.0-flash-preview",
            ItemCount: 100,
            BatchSize: 10,
            MaxCharsPerBatch: 5000,
            MaxOutputTokens: 8192,
            TotalSourceChars: 10000,
            TotalMaskedChars: 9500,
            BatchRequestCount: 10,
            TextRequestCount: 0,
            SystemPromptTokens: 500,
            InputTokensBatchPrompts: 8000,
            InputTokensTextPrompts: 0,
            OutputTokens: new OutputTokenEstimate(
                Low: 6400,
                High: 14400,
                Point: 10000,
                BatchRatio: null,
                TextRatio: null,
                UsedSample: false
            ),
            CostEstimates: new List<ModelCostEstimate>()
        );

        var text = estimate.ToHumanReadableString();

        Assert.Contains("남은 항목", text);
        Assert.Contains("gemini-3.0-flash-preview", text);
        Assert.Contains("100", text);
        Assert.Contains("휴리스틱", text);
    }

    [Fact]
    public void TranslationCostEstimate_ToHumanReadableString_WithSample_ContainsSampleLabel()
    {
        var estimate = new TranslationCostEstimate(
            ScopeLabel: "전체(모든 상태)",
            ModelName: "gemini-3.0-flash-preview",
            ItemCount: 50,
            BatchSize: 10,
            MaxCharsPerBatch: 5000,
            MaxOutputTokens: 8192,
            TotalSourceChars: 5000,
            TotalMaskedChars: 4800,
            BatchRequestCount: 5,
            TextRequestCount: 0,
            SystemPromptTokens: 500,
            InputTokensBatchPrompts: 4000,
            InputTokensTextPrompts: 0,
            OutputTokens: new OutputTokenEstimate(
                Low: 3200,
                High: 7200,
                Point: 5000,
                BatchRatio: 1.2,
                TextRatio: null,
                UsedSample: true
            ),
            CostEstimates: new List<ModelCostEstimate>()
        );

        var text = estimate.ToHumanReadableString();

        Assert.Contains("샘플 기반", text);
        Assert.Contains("샘플 비율", text);
    }

    private static TranslationCostEstimateRequest MakeRequest(string apiKey, string modelName)
    {
        return new TranslationCostEstimateRequest(
            ApiKey: apiKey,
            ModelName: modelName,
            SourceLang: "english",
            TargetLang: "korean",
            SystemPrompt: "Translate to Korean.",
            BatchSize: 10,
            MaxChars: 5000,
            MaxOutputTokens: 8192,
            RunSampleToEstimateOutputTokens: false,
            IncludeCompletedItems: false
        );
    }

    private sealed class StubProjectDb : IProjectDb
    {
        public int RowCount { get; init; }
        public (string Source, string Rec)[] ExtraRows { get; init; } = Array.Empty<(string, string)>();
        public long[] ReusableIds { get; init; } = Array.Empty<long>();

        public Task<IReadOnlyDictionary<long, string>> GetReusablePreviousTranslationsByStringIdAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<long, string>>(ReusableIds.ToDictionary(id => id, _ => "이전 번역"));

        private IEnumerable<(long Id, string Source, string Rec)> AllRows()
            => Enumerable.Range(1, RowCount).Select(i => ((long)i, "source " + i, "INFO:NAM1"))
                .Concat(ExtraRows.Select((r, i) => ((long)(1000 + i), r.Source, r.Rec)));
        public GlossaryEntry[] Glossary { get; init; } = Array.Empty<GlossaryEntry>();

        public Task<IReadOnlyList<GlossaryEntry>> GetGlossaryAsync(CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GlossaryEntry>>(Glossary);

        public Task<bool> TryInsertGlossaryIfMissingAsync(GlossaryUpsertRequest request, CancellationToken cancellationToken)
            => Task.FromResult(false);

        public Task UpdateStringTranslationAsync(long id, string destText, StringEntryStatus status, string? errorMessage, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UpdateStringTranslationsAsync(IReadOnlyList<(long Id, string DestText, StringEntryStatus Status, string? ErrorMessage)> rows, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task FillKnownTranslationsAsync(string kind, IReadOnlyList<(long Id, string Text, string Note)> filled,
            IReadOnlyList<(long Id, string Note)> setAside, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UpdateStringStatusAsync(long id, StringEntryStatus status, string? errorMessage, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task UpdateStringStatusesAsync(IReadOnlyList<long> ids, StringEntryStatus status, string? errorMessage, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyDictionary<long, (long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status, string? DialogueScope)>>
            GetStringTranslationContextsByIdsAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<long, (long, string, string?, string?, StringEntryStatus, string?)>>(
                AllRows().Where(row => ids.Contains(row.Id)).ToDictionary(row => row.Id,
                    row => (row.Id, row.Source, (string?)row.Rec, (string?)null, StringEntryStatus.Pending, (string?)null)));

        public Task<IReadOnlyDictionary<long, StringEntryStatus>> GetStringStatusesByIdsAsync(IReadOnlyList<long> ids, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<long, StringEntryStatus>>(new Dictionary<long, StringEntryStatus>());

        public Task<IReadOnlyDictionary<string, string>> GetTranslationMemoryAsync(string sourceLang, string destLang, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<string, string>>(new Dictionary<string, string>());

        public Task<IReadOnlyDictionary<long, string>> GetStringNotesByKindAsync(string kind, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyDictionary<long, string>>(new Dictionary<long, string>());

        public Task UpsertStringNoteAsync(long stringId, string kind, string message, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task DeleteStringNoteAsync(long stringId, string kind, CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task ResetInProgressToPendingAsync(CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<IReadOnlyList<(long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status)>>
            GetStringSourceContextsByStatusAsync(IReadOnlyList<StringEntryStatus> statuses, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<(long, string, string?, string?, StringEntryStatus)>>(
                Enumerable.Range(1, RowCount).Select(i => ((long)i, "source " + i, (string?)"INFO:NAM1", (string?)null, StringEntryStatus.Pending))
                    .Concat(ExtraRows.Select((r, i) => ((long)(1000 + i), r.Source, (string?)r.Rec, (string?)null, StringEntryStatus.Pending)))
                    .ToArray());
    }

    private sealed class StubGeminiClient : IGeminiClient
    {
        public int? CompletionTokens { get; init; }
        public CancellationTokenSource? CancelDuringSample { get; init; }
        public bool CacheDeleted { get; private set; }

        public Task<GeminiGenerationResult> GenerateContentWithUsageAsync(string apiKey, string modelName, GeminiGenerateContentRequest request, CancellationToken cancellationToken)
        {
            CancelDuringSample?.Cancel();
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(new GeminiGenerationResult("번역", 100, CompletionTokens, null));
        }
        public Task<string> GenerateContentAsync(string apiKey, string modelName, GeminiGenerateContentRequest request, CancellationToken cancellationToken)
            => Task.FromResult("");

        public Task<IReadOnlyList<string>> GenerateContentCandidatesAsync(string apiKey, string modelName, GeminiGenerateContentRequest request, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<string>>(Array.Empty<string>());

        public Exception? CountTokensError { get; init; }

        public Task<int> CountTokensAsync(string apiKey, string modelName, string text, CancellationToken cancellationToken)
            => CountTokensError != null ? Task.FromException<int>(CountTokensError) : Task.FromResult(text.Length / 4);

        public Task<IReadOnlyList<GeminiModel>> ListModelsAsync(string apiKey, CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<GeminiModel>>(Array.Empty<GeminiModel>());

        public Task<string> CreateCachedContentAsync(string apiKey, string modelName, string systemInstructionText, TimeSpan ttl, CancellationToken cancellationToken)
            => Task.FromResult("cached-content-name");

        public Task DeleteCachedContentAsync(string apiKey, string cacheName, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CacheDeleted = true;
            return Task.CompletedTask;
        }
    }
}
