using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed record TranslateIdsRequest(
    string ApiKey,
    string ModelName,
    string SourceLang,
    string TargetLang,
    string SystemPrompt,
    IReadOnlyList<long> Ids,
    int BatchSize,
    int MaxChars,
    int MaxConcurrency,
    double Temperature,
    int MaxOutputTokens,
    int MaxRetries,
    bool UseRecStyleHints,
    bool EnableRepairPass,
    bool EnableSessionTermMemory,
    Func<long, StringEntryStatus, string, Task>? OnRowUpdated,
    Func<CancellationToken, Task>? WaitIfPaused,
    CancellationToken CancellationToken,
    IReadOnlyList<GlossaryEntry>? GlobalGlossary = null,
    IReadOnlyDictionary<string, string>? GlobalTranslationMemory = null,
    IReadOnlyList<(string Source, string Target)>? PreloadedSessionTerms = null,
    PlaceholderSemanticRepairMode SemanticRepairMode = PlaceholderSemanticRepairMode.Strict,
    bool EnableTemplateFixer = false,
    bool KeepSkyrimTagsRaw = true,
    bool EnableDialogueContextWindow = true,
    bool EnablePromptCache = true,
    bool EnableQualityEscalation = false,
    string? QualityEscalationModelName = null,
    bool EnableRiskyCandidateRerank = true,
    int RiskyCandidateCount = 3,
    bool EnableApiKeyFailover = false,
    GeminiThinkingConfig? ThinkingConfigOverride = null
);
