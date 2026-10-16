using System.Collections.Generic;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed record TranslationCostEstimateRequest(
    string ApiKey,
    string ModelName,
    string SourceLang,
    string TargetLang,
    string SystemPrompt,
    int BatchSize,
    int MaxChars,
    int MaxOutputTokens,
    bool RunSampleToEstimateOutputTokens,
    bool IncludeCompletedItems,
    IReadOnlyList<GlossaryEntry>? GlobalGlossary = null,
    bool KeepSkyrimTagsRaw = true,
    // The player's game translation: rows it covers are finished without a request, as in a run.
    XTranslatorAi.Core.Plugins.GameStringDictionary? GameStrings = null
);
