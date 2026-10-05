using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Diagnostics;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services;

public sealed class TranslationRunnerService
{
    private readonly GlobalProjectDbService _globalProjectDbService;

    public TranslationRunnerService(GlobalProjectDbService globalProjectDbService)
    {
        _globalProjectDbService = globalProjectDbService;
    }

    public sealed record Request(
        IProjectDb Db,
        IGeminiClient GeminiClient,
        ITranslationRunnerStatusPort StatusPort,
        ITranslationRunnerFlowControlPort FlowControlPort,
        ITranslationRunnerFailoverPort FailoverPort,
        CancellationToken CancellationToken,
        IReadOnlyList<long> Ids,
        Func<long, string?> GetRecById,
        Func<string?, bool> IsBookFullRec,
        Func<string, string> GetEffectiveBookFullModelName,
        Func<string, int> ComputeMaxOutputTokens,
        string SystemPrompt,
        string PrimaryModel,
        bool EnableBookFullModelOverride,
        bool EnableQualityEscalation,
        string? QualityEscalationModelName,
        int BatchSize,
        int MaxChars,
        int Parallel,
        BethesdaFranchise Franchise,
        string SourceLang,
        string TargetLang,
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
        bool EnableBookBodyModelOverride = false,
        int MaxRetryGenerations = 8,
        int MaxTotalGenerations = 0,
        bool EnableAdaptiveOutputBudget = false,
        bool EnableBookContext = false,
        IReadOnlyDictionary<string, string>? BookTitlesByEdid = null
    );

    public sealed record Result(bool Canceled, Exception? Error);

    /// @critical: Orchestrates translation runs (model overrides, failover).
    public async Task<Result> RunAsync(Request request)
    {
        var triedApiKeys = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            request.CancellationToken.ThrowIfCancellationRequested();
            if (request.EnableBookContext)
            {
                request = request with { BookTitlesByEdid = await CollectBookTitlesAsync(request.Db, request.CancellationToken) };
            }
            var runs = await BuildRunsAsync(request);
            var budget = new TranslationGenerationBudget(request.MaxRetryGenerations, request.MaxTotalGenerations);
            await ExecuteRunsWithFailoverAsync(request, runs, triedApiKeys, budget);

            return new Result(Canceled: false, Error: null);
        }
        catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
        {
            return new Result(Canceled: true, Error: null);
        }
        catch (Exception ex)
        {
            await request.StatusPort.DispatchAsync(() => request.StatusPort.SetUserFacingError("번역", ex));
            return new Result(Canceled: false, Error: ex);
        }
    }

    /// <summary>
    /// Book titles by EditorID, for the body rows of the same books. They were collected only from the rows this
    /// run translates, so a book whose title was translated in an earlier run, edited by hand or skipped had its
    /// body translated without the title. Every BOOK:FULL row of the project counts.
    /// </summary>
    public static async Task<IReadOnlyDictionary<string, string>> CollectBookTitlesAsync(IProjectDb db, CancellationToken cancellationToken)
    {
        var rows = await db.GetBookTitleRowsAsync(cancellationToken);
        return TranslationBookContext.CollectTitles(rows.Select(row => (row.Rec, row.Edid, row.SourceText)));
    }

    private static async Task ExecuteRunsWithFailoverAsync(
        Request request,
        IReadOnlyList<TranslationRun> runs,
        HashSet<string> triedApiKeys,
        TranslationGenerationBudget budget
    )
    {
        foreach (var run in runs)
        {
            await ExecuteSingleRunWithFailoverAsync(request, run, triedApiKeys, budget);
        }
    }

    private static async Task ExecuteSingleRunWithFailoverAsync(
        Request request,
        TranslationRun run,
        HashSet<string> triedApiKeys,
        TranslationGenerationBudget budget
    )
    {
        while (true)
        {
            request.CancellationToken.ThrowIfCancellationRequested();

            try
            {
                triedApiKeys.Add((request.FailoverPort.ApiKey ?? "").Trim());

                await run.Service.TranslateIdsAsync(
                    BuildTranslateIdsRequest(request, run, request.CancellationToken) with { GenerationBudget = budget }
                );

                return;
            }
            catch (OperationCanceledException) when (request.CancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (!await TryFailoverToNextSavedKeyAsync(request, triedApiKeys, ex))
                {
                    throw;
                }

                try
                {
                    await request.Db.ResetInProgressToPendingAsync(request.CancellationToken);
                }
                catch
                {
                    // ignore
                }

                run.Service.ResetGlobalThrottle();
            }
        }
    }

    private sealed record TranslationRun(
        TranslationService Service,
        string SystemPrompt,
        IReadOnlyList<long> Ids,
        string SelectedModel,
        bool EnableQualityEscalation,
        string? QualityEscalationModel,
        int BatchSize,
        int MaxChars,
        int Parallel,
        int MaxOutputTokens,
        IReadOnlyList<GlossaryEntry>? GlobalGlossary,
        IReadOnlyDictionary<string, string>? GlobalTranslationMemory,
        IReadOnlyList<(string Source, string Target)>? ReferenceNameMemory
    );

    private async Task<IReadOnlyList<TranslationRun>> BuildRunsAsync(Request request)
    {
        var ids = request.Ids;
        var primaryModel = (request.PrimaryModel ?? "").Trim();
        var bookFullIds = CollectBookFullIds(request, ids);

        if (!TryGetEffectiveBookFullSplitModel(request, primaryModel, bookFullIds, out var bookModel))
        {
            return new List<TranslationRun> { await BuildPrimaryRunAsync(request, ids, primaryModel) };
        }

        return await BuildSplitBookFullRunsAsync(request, ids, bookFullIds, primaryModel, bookModel);
    }

    private async Task<TranslationRun> BuildPrimaryRunAsync(Request request, IReadOnlyList<long> ids, string primaryModel)
    {
        return await BuildRunAsync(
            request,
            ids,
            request.SystemPrompt,
            primaryModel,
            enableQualityEscalation: request.EnableQualityEscalation,
            qualityEscalationModelName: request.QualityEscalationModelName
        );
    }

    private static List<long> CollectBookFullIds(Request request, IReadOnlyList<long> ids)
    {
        var bookFullIds = new List<long>();
        if (!request.EnableBookFullModelOverride && !request.EnableBookBodyModelOverride)
        {
            return bookFullIds;
        }

        foreach (var id in ids)
        {
            var rec = request.GetRecById(id);
            if (BookModelRouting.ShouldOverride(rec, request.IsBookFullRec(rec),
                request.EnableBookFullModelOverride, request.EnableBookBodyModelOverride))
            {
                bookFullIds.Add(id);
            }
        }

        return bookFullIds;
    }

    private static bool TryGetEffectiveBookFullSplitModel(
        Request request,
        string primaryModel,
        IReadOnlyList<long> bookFullIds,
        out string bookModel
    )
    {
        bookModel = "";

        if ((!request.EnableBookFullModelOverride && !request.EnableBookBodyModelOverride) || bookFullIds.Count <= 0)
        {
            return false;
        }

        var effectiveBookModel = request.GetEffectiveBookFullModelName(primaryModel);
        if (string.Equals(effectiveBookModel, primaryModel, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        bookModel = effectiveBookModel;
        return true;
    }

    private async Task<IReadOnlyList<TranslationRun>> BuildSplitBookFullRunsAsync(
        Request request,
        IReadOnlyList<long> allIds,
        IReadOnlyList<long> bookFullIds,
        string primaryModel,
        string bookModel
    )
    {
        var runs = new List<TranslationRun>();
        var nonBookIds = CollectNonBookIds(allIds, bookFullIds);

        if (nonBookIds.Count > 0)
        {
            runs.Add(await BuildPrimaryRunAsync(request, nonBookIds, primaryModel));
        }

        runs.Add(
            await BuildRunAsync(
                request,
                bookFullIds,
                request.SystemPrompt,
                bookModel,
                enableQualityEscalation: false,
                qualityEscalationModelName: null
            )
        );

        await request.StatusPort.DispatchAsync(
            () => request.StatusPort.SetStatusMessage($"번역 중... (선택한 책 제목/본문 {bookFullIds.Count}개: {bookModel})")
        );

        return runs;
    }

    private static List<long> CollectNonBookIds(IReadOnlyList<long> allIds, IReadOnlyList<long> bookFullIds)
    {
        var bookSet = new HashSet<long>(bookFullIds);
        var nonBookIds = new List<long>(capacity: Math.Max(0, allIds.Count - bookSet.Count));

        foreach (var id in allIds)
        {
            if (!bookSet.Contains(id))
            {
                nonBookIds.Add(id);
            }
        }

        return nonBookIds;
    }

    private async Task<TranslationRun> BuildRunAsync(
        Request request,
        IReadOnlyList<long> ids,
        string systemPrompt,
        string modelName,
        bool enableQualityEscalation,
        string? qualityEscalationModelName
    )
    {
        var service = new TranslationService(request.Db, request.GeminiClient);

        var batchSize = Math.Clamp(request.BatchSize, 1, 100);
        var maxChars = Math.Clamp(request.MaxChars, 1000, 50000);
        var parallel = Math.Clamp(request.Parallel, 1, 8);

        var selectedModel = modelName.Trim();
        var maxOut = request.ComputeMaxOutputTokens(selectedModel);

        var globalGlossary = await TryLoadGlobalGlossaryAsync(request);
        var globalTranslationMemory = await TryLoadGlobalTranslationMemoryAsync(request);
        var referenceNameMemory = await TryLoadReferenceNameMemoryAsync(request);

        return new TranslationRun(
            Service: service,
            SystemPrompt: systemPrompt,
            Ids: ids,
            SelectedModel: selectedModel,
            EnableQualityEscalation: enableQualityEscalation && !string.IsNullOrWhiteSpace(qualityEscalationModelName),
            QualityEscalationModel: string.IsNullOrWhiteSpace(qualityEscalationModelName) ? null : qualityEscalationModelName.Trim(),
            BatchSize: batchSize,
            MaxChars: maxChars,
            Parallel: parallel,
            MaxOutputTokens: maxOut,
            GlobalGlossary: globalGlossary,
            GlobalTranslationMemory: globalTranslationMemory,
            ReferenceNameMemory: referenceNameMemory
        );
    }

    private async Task<IReadOnlyList<GlossaryEntry>?> TryLoadGlobalGlossaryAsync(Request request)
    {
        var globalDb = await _globalProjectDbService.GetOrCreateAsync(request.Franchise, request.CancellationToken);
        if (globalDb == null)
        {
            return null;
        }

        try
        {
            return await globalDb.GetGlossaryAsync(request.CancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"WARN 전체 용어집을 읽지 못해 없이 번역합니다: {ex}");
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>?> TryLoadGlobalTranslationMemoryAsync(Request request)
    {
        var globalDb = await _globalProjectDbService.GetOrCreateAsync(request.Franchise, request.CancellationToken);
        if (globalDb == null)
        {
            return null;
        }

        try
        {
            return await globalDb.GetTranslationMemoryAsync(
                request.SourceLang.Trim(),
                request.TargetLang.Trim(),
                request.CancellationToken
            );
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"WARN 시리즈 TM을 읽지 못해 없이 번역합니다: {ex}");
            return null;
        }
    }

    // The same memory with its original casing, so that names can be told from ordinary words.
    private async Task<IReadOnlyList<(string Source, string Target)>?> TryLoadReferenceNameMemoryAsync(Request request)
    {
        var globalDb = await _globalProjectDbService.GetOrCreateAsync(request.Franchise, request.CancellationToken);
        if (globalDb == null)
        {
            return null;
        }

        try
        {
            var entries = await globalDb.GetTranslationMemoryEntriesAsync(request.SourceLang.Trim(), request.TargetLang.Trim(), request.CancellationToken);
            return entries.Select(entry => (entry.SourceText, entry.DestText)).ToList();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"WARN 공식 이름 색인을 읽지 못해 없이 번역합니다: {ex}");
            return null;
        }
    }

    private static TranslateIdsRequest BuildTranslateIdsRequest(Request request, TranslationRun run, CancellationToken cancellationToken)
    {
        return new TranslateIdsRequest(
            ApiKey: request.FailoverPort.ApiKey.Trim(),
            ModelName: run.SelectedModel,
            SourceLang: request.SourceLang.Trim(),
            TargetLang: request.TargetLang.Trim(),
            SystemPrompt: run.SystemPrompt,
            Ids: run.Ids,
            BatchSize: run.BatchSize,
            MaxChars: run.MaxChars,
            MaxConcurrency: run.Parallel,
            Temperature: 0.1,
            MaxOutputTokens: run.MaxOutputTokens,
            MaxRetries: 3,
            UseRecStyleHints: request.UseRecStyleHints,
            EnableRepairPass: request.EnableRepairPass,
            EnableSessionTermMemory: request.EnableSessionTermMemory,
            OnRowUpdated: request.FlowControlPort.OnRowUpdatedAsync,
            WaitIfPaused: request.FlowControlPort.WaitIfPausedAsync,
            CancellationToken: cancellationToken,
            GlobalGlossary: run.GlobalGlossary,
            GlobalTranslationMemory: run.GlobalTranslationMemory,
            ReferenceNameMemory: run.ReferenceNameMemory,
            SemanticRepairMode: request.SemanticRepairMode,
            EnableTemplateFixer: request.EnableTemplateFixer,
            KeepSkyrimTagsRaw: request.KeepSkyrimTagsRaw,
            EnableDialogueContextWindow: request.EnableDialogueContextWindow,
            EnablePromptCache: request.EnablePromptCache,
            EnableQualityEscalation: run.EnableQualityEscalation,
            QualityEscalationModelName: run.QualityEscalationModel,
            EnableRiskyCandidateRerank: request.EnableRiskyCandidateRerank,
            RiskyCandidateCount: request.RiskyCandidateCount,
            EnableApiKeyFailover: request.FailoverPort.EnableApiKeyFailover,
            MaxRetryGenerations: request.MaxRetryGenerations,
            MaxTotalGenerations: request.MaxTotalGenerations,
            EnableAdaptiveOutputBudget: request.EnableAdaptiveOutputBudget,
            EnableBookContext: request.EnableBookContext,
            BookTitlesByEdid: request.BookTitlesByEdid
        );
    }

    // Not E210/E211 (timeout, network): no other key fixes a lost connection, and a switch left the next saved key,
    // possibly a paid one, in use after a moment of Wi-Fi trouble.
    private static bool ShouldFailover(UserFacingError error)
        => error.Code is "E201" or "E202" or "E203";

    private static async Task<bool> TryFailoverToNextSavedKeyAsync(Request request, HashSet<string> triedApiKeys, Exception ex)
    {
        if (!request.FailoverPort.EnableApiKeyFailover)
        {
            return false;
        }

        var classified = UserFacingErrorClassifier.Classify(ex);
        if (!ShouldFailover(classified))
        {
            return false;
        }

        return await TryFailoverToNextSavedGeminiKeyAsync(request.StatusPort, request.FailoverPort, triedApiKeys, classified);
    }

    internal static async Task<bool> TryFailoverToNextSavedGeminiKeyAsync(ITranslationRunnerStatusPort statusPort,
        ITranslationRunnerFailoverPort failoverPort, HashSet<string> triedApiKeys, UserFacingError classifiedError)
    {
        // The run works on a pool thread; the saved keys are the window's collection, which it can change meanwhile.
        IReadOnlyList<TranslationRunnerSavedApiKey> savedKeys = Array.Empty<TranslationRunnerSavedApiKey>();
        var currentKey = "";
        await statusPort.DispatchAsync(() =>
        {
            savedKeys = failoverPort.SavedApiKeys;
            currentKey = (failoverPort.ApiKey ?? "").Trim();
        });
        if (savedKeys.Count <= 0)
        {
            return false;
        }

        var startIndex = -1;
        for (var i = 0; i < savedKeys.Count; i++)
        {
            if (string.Equals(savedKeys[i].ApiKey.Trim(), currentKey, StringComparison.Ordinal))
            {
                startIndex = i;
                break;
            }
        }

        for (var offset = 1; offset <= savedKeys.Count; offset++)
        {
            var idx = startIndex < 0
                ? offset - 1
                : (startIndex + offset) % savedKeys.Count;

            var candidate = savedKeys[idx];
            var candidateKey = candidate.ApiKey.Trim();
            if (string.IsNullOrWhiteSpace(candidateKey))
            {
                continue;
            }

            if (triedApiKeys.Contains(candidateKey))
            {
                continue;
            }

            triedApiKeys.Add(candidateKey);

            // API 키 변경과 UI 상태 메시지를 UI 스레드에서 수행하되,
            // 완료까지 대기하여 다음 요청이 새 키를 확실히 사용하도록 보장
            await statusPort.DispatchAsync(
                () =>
                {
                    failoverPort.SelectSavedApiKey(candidate);
                    statusPort.SetStatusMessage($"{classifiedError.Message} → 키 전환: {candidate.DisplayLabel}");
                }
            );

            return true;
        }

        return false;
    }
}
