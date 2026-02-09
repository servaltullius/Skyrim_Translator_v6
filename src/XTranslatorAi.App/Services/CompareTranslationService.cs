using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services;

public sealed class CompareTranslationService
{
    private const long CompareRowId = 1;

    private readonly ProjectGlossaryService _projectGlossaryService;

    public CompareTranslationService(ProjectGlossaryService projectGlossaryService)
    {
        _projectGlossaryService = projectGlossaryService;
    }

    public sealed record Request(
        GeminiClient GeminiClient,
        ProjectDb? ProjectDb,
        string ApiKey,
        string ModelName,
        bool ThinkingOff,
        string SourceLang,
        string TargetLang,
        string SystemPrompt,
        string SourceText,
        string? Edid,
        string? Rec,
        int BatchSize,
        int MaxChars,
        int Parallel,
        int MaxOutputTokens,
        bool UseRecStyleHints,
        bool EnableRepairPass,
        bool EnableSessionTermMemory,
        PlaceholderSemanticRepairMode SemanticRepairMode,
        bool EnableTemplateFixer,
        bool KeepSkyrimTagsRaw,
        bool EnableDialogueContextWindow,
        bool EnablePromptCache,
        bool EnableRiskyCandidateRerank,
        int RiskyCandidateCount,
        bool IncludeProjectGlossary,
        IReadOnlyList<GlossaryEntry>? GlobalGlossary,
        IReadOnlyDictionary<string, string>? GlobalTranslationMemory
    );

    public sealed record Result(
        bool HasRow,
        StringEntryStatus Status,
        string DestText,
        string? ErrorMessage,
        bool TranslationMemoryHit
    );

    public async Task<Result> RunAsync(Request request, CancellationToken cancellationToken)
    {
        var tempDbPath = CreateTempCompareDbPath();

        try
        {
            await using var tempDb = await ProjectDb.OpenOrCreateAsync(tempDbPath, cancellationToken);

            await SeedTempDbAsync(tempDb, request, cancellationToken);
            await TryInsertProjectGlossaryAsync(tempDb, request, cancellationToken);

            var translationService = new TranslationService(tempDb, request.GeminiClient);
            await translationService.TranslateIdsAsync(BuildTranslateRequest(request, cancellationToken));

            var rows = await tempDb.GetStringsAsync(limit: 1, offset: 0, cancellationToken);
            if (rows.Count == 0)
            {
                return new Result(
                    HasRow: false,
                    Status: StringEntryStatus.Pending,
                    DestText: "",
                    ErrorMessage: null,
                    TranslationMemoryHit: false
                );
            }

            var row = rows[0];
            var tmHitMap = await tempDb.GetStringNotesByKindAsync("tm_hit", cancellationToken);

            return new Result(
                HasRow: true,
                Status: row.Status,
                DestText: row.DestText ?? "",
                ErrorMessage: row.ErrorMessage,
                TranslationMemoryHit: tmHitMap.ContainsKey(CompareRowId)
            );
        }
        finally
        {
            TryDeleteTempCompareDb(tempDbPath);
        }
    }

    private static async Task SeedTempDbAsync(ProjectDb tempDb, Request request, CancellationToken cancellationToken)
    {
        await tempDb.BulkInsertStringsAsync(
            new[]
            {
                (
                    OrderIndex: 0,
                    ListAttr: (string?)null,
                    PartialAttr: (string?)null,
                    AttributesJson: (string?)null,
                    Edid: string.IsNullOrWhiteSpace(request.Edid) ? null : request.Edid.Trim(),
                    Rec: string.IsNullOrWhiteSpace(request.Rec) ? null : request.Rec.Trim(),
                    SourceText: request.SourceText ?? "",
                    DestText: "",
                    Status: StringEntryStatus.Pending,
                    RawStringXml: "<String />"
                ),
            },
            cancellationToken
        );
    }

    private async Task TryInsertProjectGlossaryAsync(ProjectDb tempDb, Request request, CancellationToken cancellationToken)
    {
        if (!request.IncludeProjectGlossary || request.ProjectDb == null)
        {
            return;
        }

        var glossary = await _projectGlossaryService.GetAsync(request.ProjectDb, cancellationToken);
        await tempDb.BulkInsertGlossaryAsync(
            glossary.Select(
                g => (
                    Category: g.Category,
                    SourceTerm: g.SourceTerm ?? "",
                    TargetTerm: g.TargetTerm ?? "",
                    Enabled: g.Enabled,
                    Priority: g.Priority,
                    MatchMode: (int)g.MatchMode,
                    ForceMode: (int)g.ForceMode,
                    Note: g.Note
                )
            ),
            cancellationToken
        );
    }

    private static TranslateIdsRequest BuildTranslateRequest(Request request, CancellationToken cancellationToken)
    {
        var thinkingOverride = request.ThinkingOff ? new GeminiThinkingConfig(ThinkingBudget: 0) : null;

        return new TranslateIdsRequest(
            ApiKey: request.ApiKey.Trim(),
            ModelName: request.ModelName.Trim(),
            SourceLang: request.SourceLang.Trim(),
            TargetLang: request.TargetLang.Trim(),
            SystemPrompt: request.SystemPrompt,
            Ids: new[] { CompareRowId },
            BatchSize: Math.Clamp(request.BatchSize, 1, 100),
            MaxChars: Math.Clamp(request.MaxChars, 1000, 50000),
            MaxConcurrency: Math.Clamp(request.Parallel, 1, 8),
            Temperature: 0.1,
            MaxOutputTokens: request.MaxOutputTokens,
            MaxRetries: 3,
            UseRecStyleHints: request.UseRecStyleHints,
            EnableRepairPass: request.EnableRepairPass,
            EnableSessionTermMemory: request.EnableSessionTermMemory,
            OnRowUpdated: null,
            WaitIfPaused: null,
            CancellationToken: cancellationToken,
            GlobalGlossary: request.GlobalGlossary,
            GlobalTranslationMemory: request.GlobalTranslationMemory,
            SemanticRepairMode: request.SemanticRepairMode,
            EnableTemplateFixer: request.EnableTemplateFixer,
            KeepSkyrimTagsRaw: request.KeepSkyrimTagsRaw,
            EnableDialogueContextWindow: request.EnableDialogueContextWindow,
            EnablePromptCache: request.EnablePromptCache,
            EnableRiskyCandidateRerank: request.EnableRiskyCandidateRerank,
            RiskyCandidateCount: request.RiskyCandidateCount,
            EnableQualityEscalation: false,
            QualityEscalationModelName: null,
            EnableApiKeyFailover: false,
            ThinkingConfigOverride: thinkingOverride
        );
    }

    private static string CreateTempCompareDbPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "TulliusTranslator", "compare");
        Directory.CreateDirectory(dir);
        return Path.Combine(dir, $"compare-{Guid.NewGuid():N}.sqlite");
    }

    private static void TryDeleteTempCompareDb(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            File.Delete(path);
        }
        catch
        {
            // ignore
        }

        try
        {
            File.Delete(path + "-wal");
        }
        catch
        {
            // ignore
        }

        try
        {
            File.Delete(path + "-shm");
        }
        catch
        {
            // ignore
        }
    }
}
