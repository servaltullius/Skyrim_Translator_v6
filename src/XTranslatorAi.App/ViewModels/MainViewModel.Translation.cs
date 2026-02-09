using System;
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
        var db = _projectState.Db;
        if (db == null || _projectState.XmlInfo == null)
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

        await TryPreloadContextsAsync();

        BeginTranslationUiState();
        await SaveProjectInfoAsync();

        await ResetNonEditedTranslationsAsync();
        var ids = await LoadPendingIdsAsync();

        var systemPrompt = BuildSystemPrompt();
        var cts = new CancellationTokenSource();
        _translationCts = cts;

        var primaryModel = (SelectedModel ?? "").Trim();
        var request = new TranslationRunnerService.Request(
            Db: db,
            GeminiClient: _geminiClient,
            StatusPort: this,
            FlowControlPort: this,
            FailoverPort: this,
            CancellationTokenSource: cts,
            Ids: ids,
            GetRecById: id => _projectState.TryGetById(id, out var vm) ? vm.Rec : null,
            IsBookFullRec: IsBookFullRec,
            GetEffectiveBookFullModelName: GetEffectiveBookFullModelName,
            ComputeMaxOutputTokens: ComputeMaxOutputTokens,
            SystemPrompt: systemPrompt,
            PrimaryModel: primaryModel,
            EnableBookFullModelOverride: EnableBookFullModelOverride,
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
            RiskyCandidateCount: RiskyCandidateCount
        );

        _ = Task.Run(
            async () =>
            {
                try
                {
                    var result = await _translationRunnerService.RunAsync(request);
                    await DispatchAsync(() => FinishTranslationUiStateAsync(result.Canceled, result.Error));
                }
                finally
                {
                    cts.Dispose();
                    if (ReferenceEquals(_translationCts, cts))
                    {
                        _translationCts = null;
                    }
                }
            }
        );
    }
    private bool CanStartTranslation() => IsProjectLoaded && !IsTranslating && !HasPromptLintBlockingIssues;

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

        var candidates = new[]
        {
            "gemini-3-flash-preview",
            "gemini-3-flash",
            "gemini-3.0-flash-preview",
        };

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
