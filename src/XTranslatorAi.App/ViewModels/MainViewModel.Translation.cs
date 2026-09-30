using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    /// @critical: Translation entrypoint (Start button).
    [RelayCommand(CanExecute = nameof(CanStartTranslation))]
    private async Task StartTranslationAsync()
    {
        if (!CanStartTranslation() || _translationOperation.IsRunning)
        {
            return;
        }

        var db = _projectState.Db;
        if (db == null || !_projectState.HasSource)
        {
            return;
        }

        if (!TryValidateApiKey())
        {
            return;
        }

        if (!TryValidatePromptLintBeforeStart())
        {
            return;
        }

        await _translationOperation.RunAsync(async cancellationToken =>
        {
            var generation = Interlocked.Increment(ref _rowUpdateGeneration);
            BeginTranslationUiState();
            var canceled = false;
            var nothingToTranslate = false;
            Exception? error = null;
            try
            {
                await SaveProjectInfoAsync(cancellationToken);
                await PrepareTranslationsForResumeAsync(cancellationToken);
                var ids = await LoadPendingIdsAsync(cancellationToken);
                if (ids.Count == 0)
                {
                    nothingToTranslate = true;
                    return;
                }

                await TryPreloadContextsAsync(cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                var systemPrompt = BuildSystemPrompt();
                var recById = Entries.ToDictionary(entry => entry.Id, entry => entry.Rec);
                var primaryModel = (SelectedModel ?? "").Trim();
                var request = new TranslationRunnerService.Request(
                    Db: db,
                    GeminiClient: _geminiClient,
                    StatusPort: this,
                    FlowControlPort: new RunFlowControlPort(this, generation),
                    FailoverPort: this,
                    CancellationToken: cancellationToken,
                    Ids: ids,
                    GetRecById: id => recById.GetValueOrDefault(id),
                    IsBookFullRec: IsBookFullRec,
                    GetEffectiveBookFullModelName: GetEffectiveBookFullModelName,
                    ComputeMaxOutputTokens: ComputeMaxOutputTokens,
                    SystemPrompt: systemPrompt,
                    PrimaryModel: primaryModel,
                    EnableBookFullModelOverride: EnableBookFullModelOverride,
                    EnableBookBodyModelOverride: EnableBookBodyModelOverride,
                    EnableQualityEscalation: EnableQualityEscalation,
                    QualityEscalationModelName: QualityEscalationModel,
                    BatchSize: BatchSize,
                    MaxChars: MaxCharsPerBatch,
                    Parallel: MaxParallelRequests,
                    Franchise: SelectedFranchise,
                    SourceLang: SourceLang,
                    TargetLang: TargetLang,
                    UseRecStyleHints: UseRecStyleHints,
                    EnableRepairPass: EnableRepairPass,
                    EnableSessionTermMemory: EnableSessionTermMemory,
                    SemanticRepairMode: SemanticRepairMode,
                    EnableTemplateFixer: EnableTemplateFixer,
                    KeepSkyrimTagsRaw: KeepSkyrimTagsRaw,
                    EnableDialogueContextWindow: EnableDialogueContextWindow,
                    EnablePromptCache: EnablePromptCache,
                    EnableRiskyCandidateRerank: EnableRiskyCandidateRerank,
                    RiskyCandidateCount: RiskyCandidateCount,
                    MaxRetryGenerations: MaxRetryGenerations,
                    MaxTotalGenerations: MaxTotalGenerations,
                    EnableAdaptiveOutputBudget: EnableAdaptiveOutputBudget,
                    EnableBookContext: EnableBookContext
                );

                var result = await Task.Run(() => _translationRunnerService.RunAsync(request), cancellationToken);
                canceled = result.Canceled;
                error = result.Error;
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                canceled = true;
            }
            catch (Exception ex)
            {
                error = ex;
                SetUserFacingError("번역 준비/실행", ex);
            }
            finally
            {
                try
                {
                    await FinishTranslationUiStateAsync(canceled, error);
                    if (nothingToTranslate)
                    {
                        StatusMessage = "번역할 미완료 항목이 없습니다. 기존 번역과 편집 내용을 유지했습니다.";
                    }
                }
                finally
                {
                    IsTranslating = false;
                    IsPaused = false;
                    _resumeTcs?.TrySetResult(true);
                    _resumeTcs = null;
                }
            }
        });
    }
    private bool CanStartTranslation() => IsProjectLoaded && !IsTranslating && IsWorkspaceInteractive
        && !_projectOperations.IsRunning && !HasPromptLintBlockingIssues;

    private static bool IsBookFullRec(string? rec)
    {
        if (string.IsNullOrWhiteSpace(rec))
        {
            return false;
        }

        var trimmed = rec.Trim();
        var colon = trimmed.IndexOf(':');
        if (colon <= 0 || colon >= trimmed.Length - 1)
        {
            return false;
        }

        var head = trimmed.Substring(0, colon).Trim();
        var tail = trimmed.Substring(colon + 1).Trim();

        return head.Equals("BOOK", StringComparison.OrdinalIgnoreCase)
               && tail.StartsWith("FULL", StringComparison.OrdinalIgnoreCase);
    }

    private string GetEffectiveBookFullModelName(string primaryModel)
    {
        var configured = (BookFullModel ?? "").Trim();
        if (!string.IsNullOrWhiteSpace(configured))
        {
            return configured;
        }

        var candidates = GeminiModelCatalog.FullModels;

        foreach (var c in candidates)
        {
            if (_modelInfoByName.ContainsKey(c))
            {
                return c;
            }
        }

        return primaryModel;
    }
}
