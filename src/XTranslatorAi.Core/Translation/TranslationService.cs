using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static readonly Regex XtTokenRegex = new(
        pattern: @"__XT_(?:PH|TERM)(?:_[A-Z0-9]+)?_[0-9]{4}__",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex RawMarkupTagRegex = new(
        pattern: @"<[^>]+>",
        options: RegexOptions.CultureInvariant
    );

    private static readonly Regex RawPagebreakRegex = new(
        pattern: @"\[pagebreak\]",
        options: RegexOptions.CultureInvariant | RegexOptions.IgnoreCase
    );

    private const string EndSentinelToken = "__XT_PH_9999__";

    private readonly ProjectDb _db;
    private readonly GeminiClient _gemini;
    private GeminiThinkingConfig? _thinkingConfigOverride;

    private SemaphoreSlim? _generateContentGate;
    private SemaphoreSlim? _veryLongRequestGate;
    private int _longTextChunkParallelism = 1;
    private int _adaptiveConcurrencyMax = 1;
    private int _adaptiveConcurrencyLimit = 1;
    private int _adaptiveInFlight;
    private int _adaptiveSuccessStreak;
    private double? _maskedTokensPerCharHint;
    private Dictionary<long, RowContext>? _rowContextById;
    private IReadOnlyDictionary<long, IReadOnlyList<(long Id, string Source, MaskedText Mask)>>? _duplicateRowsByCanonicalId;
    private IReadOnlyDictionary<long, string>? _dialogueContextWindowById;

    private bool _useRecStyleHints = true;
    private bool _enableDialogueContextWindow = true;
    private PlaceholderSemanticRepairMode _semanticRepairMode = PlaceholderSemanticRepairMode.Strict;
    private bool _enableTemplateFixer = true;
    private bool _enableQualityEscalation;
    private string? _qualityEscalationModelName;
    private bool _enableRiskyCandidateRerank = true;
    private int _riskyCandidateCount = 3;

    public TranslationService(ProjectDb db, GeminiClient gemini)
    {
        _db = db;
        _gemini = gemini;
    }

    /// @critical: Core translation pipeline entrypoint.
    public Task TranslateIdsAsync(TranslateIdsRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return TranslateIdsCoreAsync(request);
    }
}
