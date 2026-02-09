# PACK

- Project: `Skyrim_Translator_v6`
- Generated: `2026-01-20 08:02:44`
- Scope: `recent` (12 files)

## Files
- `src/XTranslatorAi.App/ViewModels/MainViewModel.GlobalGlossary.cs` (loc=304, mtime=2026-01-20 07:52:18)
  - symbols: public class XTranslatorAi.App.ViewModels.MainViewModel @L13 | private Task AddGlobalGlossaryAsync() @L16 | private bool CanAddGlobalGlossary() @L60 | private Task ImportGlobalGlossaryAsync() @L64 | private bool CanImportGlobalGlossary() @L86
- `src/XTranslatorAi.App/ViewModels/MainViewModel.Glossary.cs` (loc=655, mtime=2026-01-20 07:51:59)
  - symbols: public class XTranslatorAi.App.ViewModels.MainViewModel @L14 | private bool ShouldDefaultGlossaryUsePromptOnly(string sourceTerm) @L23 | private Task AddGlossaryAsync() @L26 | private bool CanAddGlossary() @L70 | private Task ImportGlossaryAsync() @L74
- `src/XTranslatorAi.App/ViewModels/MainViewModel.Nexus.cs` (loc=384, mtime=2026-01-20 07:56:17)
  - symbols: public class XTranslatorAi.App.ViewModels.MainViewModel @L15 | private Task FetchNexusContextAsync() @L19 | private Task<string?> TryGetModDescriptionHtmlAsync(NexusModsClient client, string apiKey, string domain, long modId) @L69 | private NexusContextInfo CreateNexusContextInfo(string domain, long modId, string url, NexusMod mod, string? descriptionHtml) @L86 | private Task SearchNexusModByAddonNameAsync() @L106
- `src/XTranslatorAi.Core/Data/ProjectDb.Project.cs` (loc=135, mtime=2026-01-20 07:45:35)
  - complexity: UpsertProjectAsync (lines=57, nesting=1, params=2)
  - symbols: public class XTranslatorAi.Core.Data.ProjectDb @L9 | public Task<ProjectInfo?> TryGetProjectAsync(CancellationToken cancellationToken) @L33 | public Task<long> UpsertProjectAsync(ProjectInfo project, CancellationToken cancellationToken) @L76 | private ProjectInfo ReadProjectInfo(SqliteDataReader reader) @L55 | private DateTimeOffset ReadUtcTimestamp(SqliteDataReader reader, int ordinal) @L73
- `src/XTranslatorAi.Core/Text/PairedSlashListExpander.cs` (loc=388, mtime=2026-01-20 07:53:56)
  - symbols: public class XTranslatorAi.Core.Text.PairedSlashListExpander @L7 | public string Expand(string text) @L14 | private bool TryExpandAt(string text, Match match, out string replacement, out int replaceEnd) @L54 | private bool TryGetNumericValues(Match match, out List<string> values) @L80 | private record XTranslatorAi.Core.Text.struct @L99
- `src/XTranslatorAi.Core/Text/TokenAwareTextSplitter.cs` (loc=187, mtime=2026-01-20 07:49:03)
  - symbols: public class XTranslatorAi.Core.Text.TokenAwareTextSplitter @L7 | public IReadOnlyList<string> Split(string text, int maxChunkChars, int? maxTokensPerChunk = null) @L27 | private class XTranslatorAi.Core.Text.SplitState @L14 | public SplitState(int maxChunkChars) @L17 | private IReadOnlyList<string> SplitCore(string text, int maxChunkChars, int? maxTokensPerChunk) @L46
- `src/XTranslatorAi.Core/Translation/GeminiClient.cs` (loc=615, mtime=2026-01-20 07:58:07)
  - symbols: public class XTranslatorAi.Core.Translation.GeminiClient @L12 | public Task<IReadOnlyList<GeminiModel>> ListModelsAsync(string apiKey, CancellationToken cancellationToken) @L28 | public Task<string> CreateCachedContentAsync(string apiKey, string modelName, string systemInstructionText, TimeSpan ttl, CancellationToken cancellationToken) @L78 | public Task DeleteCachedContentAsync(string apiKey, string cacheName, CancellationToken cancellationToken) @L163 | public Task<string> GenerateContentAsync(string apiKey, string modelName, GeminiGenerateContentRequest request, CancellationToken cancellationToken) @L231
- `src/XTranslatorAi.Core/Translation/TranslationCostEstimator.Types.cs` (loc=139, mtime=2026-01-20 07:55:39)
  - symbols: public record XTranslatorAi.Core.Translation.TranslationCostEstimate @L6 | public record TranslationCostEstimate(string ScopeLabel, string ModelName, int ItemCount, int BatchSize, int MaxCharsPerBatch, int MaxOutputTokens, long TotalSourceChars, long TotalMaskedChars, int BatchRequestCount, int TextRequestCount, long SystemPromptTokens, long InputTokensBatchPrompts, long InputTokensTextPrompts, OutputTokenEstimate OutputTokens, IReadOnlyList<ModelCostEstimate> CostEstimates) @L6 | public string ToHumanReadableString() @L25 | public record XTranslatorAi.Core.Translation.OutputTokenEstimate @L91 | public record XTranslatorAi.Core.Translation.struct @L100
- `src/XTranslatorAi.Core/Translation/TranslationService.Pipeline.SingleRow.cs` (loc=249, mtime=2026-01-20 07:47:43)
  - complexity: TrySemanticRepairAsync (lines=53, nesting=1, params=4)
  - symbols: public class XTranslatorAi.Core.Translation.TranslationService @L8 | private int ComputeLongTextInitialChunkChars(PipelineContext ctx, int tokenCount) @L218 | else if(tokenCount >= 40) @L241
- `src/XTranslatorAi.Core/Translation/TranslationService.SessionTermMemory.cs` (loc=721, mtime=2026-01-20 07:56:49)
  - complexity: FlushSessionTermAutoGlossaryInsertsAsync (lines=56, nesting=2, params=0)
  - symbols: public class XTranslatorAi.Core.Translation.TranslationService @L11 | private bool IsSessionTermRec(string? rec) @L34 | private bool IsSessionTermDefinitionText(string sourceText) @L47 | private bool IsSessionTermSingleWordCandidate(string term) @L94 | private bool IsSessionTermTranslationText(string translatedText) @L127
- `tests/XTranslatorAi.Tests/ProjectDbGlossarySessionAutoInsertTests.cs` (loc=79, mtime=2026-01-20 07:53:20)
  - symbols: public class XTranslatorAi.Tests.ProjectDbGlossarySessionAutoInsertTests @L10 | public Task TryInsertGlossaryIfMissingAsync_InsertsOnce_CaseInsensitive() @L13 | private GlossaryUpsertRequest CreateAutoGlossaryRequest(string source, string target) @L51 | private void TryDelete(string path) @L63
- `tests/XTranslatorAi.Tests/TranslationServiceBatchGroupingTests.cs` (loc=344, mtime=2026-01-20 07:46:29)
  - symbols: public class XTranslatorAi.Tests.TranslationServiceBatchGroupingTests @L18 | public Task TranslateIdsAsync_SortsBatchItemsByEdidStem_ForConsistency() @L22 | private Task<IReadOnlyList<long>> GetPendingIdsAsync(ProjectDb db, int expectedCount) @L89 | private Task SeedProjectAsync(ProjectDb db) @L96 | private TranslateIdsRequest CreateTranslateIdsRequest(IReadOnlyList<long> ids, int batchSize) @L120

## Commands
- Full scan: `python3 scripts/vibe.py doctor --full`
- Tests: `dotnet test tests/XTranslatorAi.Tests/XTranslatorAi.Tests.csproj -c Release`
- Search: `python3 scripts/vibe.py search <query>`

## Notes
- Treat runtime placeholders/tokens as a contract (`<mag>`, `<dur>`, `__XT_*__`, `[pagebreak]`).
