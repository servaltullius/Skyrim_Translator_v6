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
        ProjectDb Db,
        GeminiClient GeminiClient,
        ITranslationRunnerStatusPort StatusPort,
        ITranslationRunnerFlowControlPort FlowControlPort,
        ITranslationRunnerFailoverPort FailoverPort,
        CancellationTokenSource CancellationTokenSource,
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
        int RiskyCandidateCount
    );

    public sealed record Result(bool Canceled, Exception? Error);

    /// @critical: Orchestrates translation runs (model overrides, failover).
    public async Task<Result> RunAsync(Request request)
    {
        var triedApiKeys = new HashSet<string>(StringComparer.Ordinal);

        try
        {
            var runs = await BuildRunsAsync(request);
            await ExecuteRunsWithFailoverAsync(request, runs, triedApiKeys);

            return new Result(Canceled: false, Error: null);
        }
        catch (OperationCanceledException) when (request.CancellationTokenSource.IsCancellationRequested)
        {
            return new Result(Canceled: true, Error: null);
        }
        catch (Exception ex)
        {
            await request.StatusPort.DispatchAsync(() => request.StatusPort.SetUserFacingError("번역", ex));
            return new Result(Canceled: false, Error: ex);
        }
    }

    private static async Task ExecuteRunsWithFailoverAsync(
        Request request,
        IReadOnlyList<TranslationRun> runs,
        HashSet<string> triedApiKeys
    )
    {
        foreach (var run in runs)
        {
            await ExecuteSingleRunWithFailoverAsync(request, run, triedApiKeys);
        }
    }

    private static async Task ExecuteSingleRunWithFailoverAsync(
        Request request,
        TranslationRun run,
        HashSet<string> triedApiKeys
    )
    {
        while (true)
        {
            request.CancellationTokenSource.Token.ThrowIfCancellationRequested();

            try
            {
                triedApiKeys.Add((request.FailoverPort.ApiKey ?? "").Trim());

                await run.Service.TranslateIdsAsync(
                    BuildTranslateIdsRequest(request, run, request.CancellationTokenSource.Token)
                );

                return;
            }
            catch (Exception ex) when (!request.CancellationTokenSource.IsCancellationRequested && TryFailoverToNextSavedKey(request, triedApiKeys, ex))
            {
                try
                {
                    await request.Db.ResetInProgressToPendingAsync(CancellationToken.None);
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
        IReadOnlyDictionary<string, string>? GlobalTranslationMemory
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
        if (!request.EnableBookFullModelOverride)
        {
            return bookFullIds;
        }

        foreach (var id in ids)
        {
            if (request.IsBookFullRec(request.GetRecById(id)))
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

        if (!request.EnableBookFullModelOverride || bookFullIds.Count <= 0)
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
            () => request.StatusPort.SetStatusMessage($"Translating... (BOOK:FULL uses {bookModel})")
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
            GlobalTranslationMemory: globalTranslationMemory
        );
    }

    private async Task<IReadOnlyList<GlossaryEntry>?> TryLoadGlobalGlossaryAsync(Request request)
    {
        var globalDb = await _globalProjectDbService.GetOrCreateAsync(request.Franchise, CancellationToken.None);
        if (globalDb == null)
        {
            return null;
        }

        try
        {
            return await globalDb.GetGlossaryAsync(CancellationToken.None);
        }
        catch
        {
            return null;
        }
    }

    private async Task<IReadOnlyDictionary<string, string>?> TryLoadGlobalTranslationMemoryAsync(Request request)
    {
        var globalDb = await _globalProjectDbService.GetOrCreateAsync(request.Franchise, CancellationToken.None);
        if (globalDb == null)
        {
            return null;
        }

        try
        {
            return await globalDb.GetTranslationMemoryAsync(
                request.SourceLang.Trim(),
                request.TargetLang.Trim(),
                CancellationToken.None
            );
        }
        catch
        {
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
            SemanticRepairMode: request.SemanticRepairMode,
            EnableTemplateFixer: request.EnableTemplateFixer,
            KeepSkyrimTagsRaw: request.KeepSkyrimTagsRaw,
            EnableDialogueContextWindow: request.EnableDialogueContextWindow,
            EnablePromptCache: request.EnablePromptCache,
            EnableQualityEscalation: run.EnableQualityEscalation,
            QualityEscalationModelName: run.QualityEscalationModel,
            EnableRiskyCandidateRerank: request.EnableRiskyCandidateRerank,
            RiskyCandidateCount: request.RiskyCandidateCount,
            EnableApiKeyFailover: request.FailoverPort.EnableApiKeyFailover
        );
    }

    private static bool ShouldFailover(UserFacingError error)
        => error.Code is "E201" or "E202" or "E203" or "E210" or "E211";

    private static bool TryFailoverToNextSavedKey(Request request, HashSet<string> triedApiKeys, Exception ex)
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

        return TryFailoverToNextSavedGeminiKey(request, triedApiKeys, classified);
    }

    private static bool TryFailoverToNextSavedGeminiKey(Request request, HashSet<string> triedApiKeys, UserFacingError classifiedError)
    {
        var savedKeys = request.FailoverPort.SavedApiKeys;
        if (savedKeys.Count <= 0)
        {
            return false;
        }

        var currentKey = (request.FailoverPort.ApiKey ?? "").Trim();
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

            _ = request.StatusPort.DispatchAsync(
                () =>
                {
                    request.FailoverPort.SelectSavedApiKey(candidate);
                    request.StatusPort.SetStatusMessage($"{classifiedError.Message} → 키 전환: {candidate.DisplayLabel}");
                }
            );

            return true;
        }

        return false;
    }
}
