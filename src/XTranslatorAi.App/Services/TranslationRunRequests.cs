using System;
using System.Collections.Generic;
using System.Threading;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Plugins;
using XTranslatorAi.Core.Translation;

namespace XTranslatorAi.App.Services;

/// <summary>
/// The translation run the GUI's 번역 시작 and agent mode both start, its settings taken from the user's settings
/// (<see cref="AppSettings"/>) in one place. Each built its own request of some forty arguments: a setting added to the
/// run had to be passed in both, and one with a default (MaxRetryGenerations, EnableBookContext) could be left out of
/// one without a word. What agent mode fixes, so that a recording and its replay make the same requests, is
/// <see cref="ForAgent"/>.
/// </summary>
public static class TranslationRunRequests
{
    /// <summary>Where the run writes, asks its model, reports, pauses and switches API keys, and what stops it.</summary>
    public sealed record Ports(IProjectDb Db, IGeminiClient Client, ITranslationRunnerStatusPort Status,
        ITranslationRunnerFlowControlPort FlowControl, ITranslationRunnerFailoverPort Failover, CancellationToken CancellationToken);

    /// <summary>
    /// The model and its book routing: the GUI's settings for them, agent mode's fixed profile. They decide the model,
    /// not the settings' SelectedModel, BookFullModel and MaxOutputTokensOverride, which Create does not read.
    /// </summary>
    public sealed record Model(string Primary, Func<string?, bool> IsBookFullRec, Func<string, string> BookFullModelName,
        Func<string, int> MaxOutputTokens);

    public static TranslationRunnerService.Request Create(Ports ports, AppSettings settings, Model model, IReadOnlyList<long> ids,
        Func<long, string?> getRecById, string systemPrompt, BethesdaFranchise franchise, string sourceLang, string targetLang,
        GameStringDictionary? gameStrings, IReadOnlySet<long>? nameMemoryRows = null,
        Action<IReadOnlyDictionary<long, IReadOnlyList<long>>>? onDuplicateRows = null)
        => new(
            Db: ports.Db,
            GeminiClient: ports.Client,
            StatusPort: ports.Status,
            FlowControlPort: ports.FlowControl,
            FailoverPort: ports.Failover,
            CancellationToken: ports.CancellationToken,
            Ids: ids,
            GetRecById: getRecById,
            IsBookFullRec: model.IsBookFullRec,
            GetEffectiveBookFullModelName: model.BookFullModelName,
            ComputeMaxOutputTokens: model.MaxOutputTokens,
            SystemPrompt: systemPrompt,
            PrimaryModel: model.Primary,
            EnableBookFullModelOverride: settings.EnableBookFullModelOverride,
            EnableBookBodyModelOverride: settings.EnableBookBodyModelOverride,
            EnableQualityEscalation: settings.EnableQualityEscalation,
            QualityEscalationModelName: settings.QualityEscalationModel,
            BatchSize: settings.BatchSize,
            MaxChars: settings.MaxCharsPerBatch,
            Parallel: settings.MaxParallelRequests,
            Franchise: franchise,
            SourceLang: sourceLang,
            TargetLang: targetLang,
            UseRecStyleHints: settings.UseRecStyleHints,
            EnableRepairPass: settings.EnableRepairPass,
            EnableSessionTermMemory: settings.EnableSessionTermMemory,
            SemanticRepairMode: settings.SemanticRepairMode,
            EnableTemplateFixer: settings.EnableTemplateFixer,
            KeepSkyrimTagsRaw: settings.KeepSkyrimTagsRaw,
            EnableDialogueContextWindow: settings.EnableDialogueContextWindow,
            EnablePromptCache: settings.EnablePromptCache,
            EnableRiskyCandidateRerank: settings.EnableRiskyCandidateRerank,
            RiskyCandidateCount: settings.RiskyCandidateCount,
            MaxRetryGenerations: settings.MaxRetryGenerations,
            MaxTotalGenerations: settings.MaxTotalGenerations,
            EnableAdaptiveOutputBudget: settings.EnableAdaptiveOutputBudget,
            EnableBookContext: settings.EnableBookContext,
            GameStrings: gameStrings,
            NameMemoryRows: nameMemoryRows,
            OnDuplicateRows: onDuplicateRows);

    /// <summary>
    /// The user's settings as agent mode runs them: what would make a recording and its replay differ is fixed or off.
    /// One request at a time, no prompt cache, no candidate rerank, no quality escalation, no session terms, no book
    /// model, and the agent's own batch.
    /// </summary>
    public static AppSettings ForAgent(AppSettings settings, int batchSize, int maxChars) => settings with
    {
        EnableBookFullModelOverride = false,
        EnableBookBodyModelOverride = false,
        EnableQualityEscalation = false,
        QualityEscalationModel = null,
        BatchSize = batchSize,
        MaxCharsPerBatch = maxChars,
        MaxParallelRequests = 1,
        EnableSessionTermMemory = false,
        EnablePromptCache = false,
        EnableRiskyCandidateRerank = false,
        RiskyCandidateCount = 1,
    };
}
