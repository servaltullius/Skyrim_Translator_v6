using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

internal sealed class TranslationRunContext : IDisposable
{
    // Run configuration (set from TranslateIdsRequest)
    public GeminiThinkingConfig? ThinkingConfigOverride { get; set; }
    public PlaceholderSemanticRepairMode SemanticRepairMode { get; set; } = PlaceholderSemanticRepairMode.Strict;
    public bool UseRecStyleHints { get; set; } = true;
    public bool EnableDialogueContextWindow { get; set; } = true;
    public bool EnableTemplateFixer { get; set; } = true;
    public bool EnableQualityEscalation { get; set; }
    public string? QualityEscalationModelName { get; set; }
    public bool EnableRiskyCandidateRerank { get; set; } = true;
    public int RiskyCandidateCount { get; set; } = 3;
    public TranslationGenerationBudget? GenerationBudget { get; set; }
    public bool EnableAdaptiveOutputBudget { get; set; }
    public bool EnableBookContext { get; set; }
    public IReadOnlyDictionary<string, string>? BookTitlesByEdid { get; set; }

    // Concurrency / gates
    public SemaphoreSlim? GenerateContentGate { get; set; }
    public SemaphoreSlim? VeryLongRequestGate { get; set; }
    public int LongTextChunkParallelism { get; set; } = 1;
    public AdaptiveConcurrencyController AdaptiveConcurrency { get; } = new();
    public double? MaskedTokensPerCharHint { get; set; }

    // Data built during run
    public Dictionary<long, TranslationService.RowContext>? RowContextById { get; set; }
    public IReadOnlyDictionary<long, IReadOnlyList<(long Id, string Source, MaskedText Mask)>>? DuplicateRowsByCanonicalId { get; set; }
    public IReadOnlyDictionary<long, string>? DialogueContextWindowById { get; set; }

    // Session Term Memory
    public bool EnableSessionTermMemory { get; set; }
    public TranslationService.SessionTermMemory? SessionTermMemory { get; set; }
    public ConcurrentQueue<(string Source, string Target)>? PendingSessionAutoGlossaryInserts { get; set; }
    public ConcurrentDictionary<string, byte>? SessionAutoGlossaryKnownKeys { get; set; }

    public void Dispose()
    {
        GenerateContentGate?.Dispose();
        VeryLongRequestGate?.Dispose();
    }
}
