using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Data;
using XTranslatorAi.Core.Models;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationCostEstimator
{
    private readonly IProjectDb _db;
    private readonly IGeminiClient _gemini;

    public TranslationCostEstimator(IProjectDb db, IGeminiClient gemini)
    {
        _db = db;
        _gemini = gemini;
    }

    public async Task<TranslationCostEstimate> EstimateAsync(
        TranslationCostEstimateRequest request,
        CancellationToken cancellationToken
    )
    {
        ValidateEstimateRequest(request);
        var (batchSize, maxChars) = NormalizeBatchAndCharLimits(request);

        var loaded = await LoadRowsForEstimationAsync(request, cancellationToken);
        var (rows, gameStringRows, previousReleaseRows) = await DropKnownTranslationsAsync(request, loaded, cancellationToken);
        var prepared = await PrepareEstimationInputAsync(request, rows, cancellationToken);

        var (batchPrompts, textPrompts) = await BuildEstimationPromptsAsync(
            request.ApiKey,
            request.ModelName,
            request.SourceLang,
            request.TargetLang,
            prepared,
            batchSize,
            maxChars,
            request.MaxOutputTokens,
            cancellationToken
        );

        var tokenCounts = await CountInputTokensAsync(request, batchPrompts, textPrompts, cancellationToken);
        var requestCountBatch = batchPrompts.Count;
        var requestCountText = textPrompts.Count;
        var requestCountTotal = requestCountBatch + requestCountText;

        var outputEstimateInputs = new OutputTokenEstimateInputs(
            request, batchPrompts, textPrompts, tokenCounts.InputTokensBatch, tokenCounts.InputTokensText, requestCountTotal, cancellationToken
        );
        var outputEstimate = await EstimateOutputTokensAsync(outputEstimateInputs);

        var costEstimates = BuildCostEstimates(
            request,
            tokenCounts.SystemPromptTokens,
            tokenCounts.InputTokensBatch + tokenCounts.InputTokensText,
            outputEstimate,
            requestCountTotal
        );

        var resultInputs = new TranslationCostEstimateBuildInputs(
            request, prepared, batchSize, maxChars, requestCountBatch, requestCountText, tokenCounts, outputEstimate, costEstimates
        );
        return BuildTranslationCostEstimate(resultInputs) with
        {
            CountTokensFallbacks = _countTokensFallbacks,
            GameStringRows = gameStringRows,
            PreviousReleaseRows = previousReleaseRows,
        };
    }

    /// <summary>
    /// Leaves out the rows a run fills without a request, in the run's order: the game's own translation, then the
    /// earlier translation where the English did not change (see TranslationService.BuildTranslationItemsAsync).
    /// A text that fails the tag check there goes to the model after all, so this is the most the run can skip. A game
    /// text that spells a project term otherwise goes to the model too, and is counted with the rows to translate.
    /// </summary>
    private async Task<(IReadOnlyList<(long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status)> Rows, int GameStrings, int PreviousRelease)>
        DropKnownTranslationsAsync(
            TranslationCostEstimateRequest request,
            IReadOnlyList<(long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status)> rows,
            CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return (rows, 0, 0);
        }

        var game = request.GameStrings == null
            ? new Dictionary<long, XTranslatorAi.Core.Plugins.GameStringMatch>()
            : await request.GameStrings.MatchAsync(_db, rows.Select(row => row.Id).ToList(), cancellationToken);
        var previous = await _db.GetReusablePreviousTranslationsByStringIdAsync(cancellationToken);
        if (game.Count == 0 && previous.Count == 0)
        {
            return (rows, 0, 0);
        }

        var projectTerms = ProjectTermCheck.Build(await _db.GetGlossaryAsync(cancellationToken));
        int gameRows = 0, previousRows = 0;
        var kept = new List<(long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status)>(rows.Count);
        foreach (var row in rows)
        {
            if (game.TryGetValue(row.Id, out var match) && ProjectTermCheck.Keeps(projectTerms, row.SourceText, match.Text))
            {
                gameRows++;
            }
            else if (previous.ContainsKey(row.Id))
            {
                previousRows++;
            }
            else
            {
                kept.Add(row);
            }
        }

        return (kept, gameRows, previousRows);
    }

    private readonly record struct TranslationCostEstimateBuildInputs(
        TranslationCostEstimateRequest Request,
        PreparedEstimationInput Prepared,
        int BatchSize,
        int MaxCharsPerBatch,
        int BatchRequestCount,
        int TextRequestCount,
        InputTokenCounts TokenCounts,
        OutputTokenEstimate OutputEstimate,
        IReadOnlyList<ModelCostEstimate> CostEstimates
    );

    private static TranslationCostEstimate BuildTranslationCostEstimate(TranslationCostEstimateBuildInputs inputs)
    {
        return new TranslationCostEstimate(
            ScopeLabel: inputs.Request.IncludeCompletedItems ? "전체(모든 상태)" : "남은 항목(Pending+Error)",
            ModelName: inputs.Request.ModelName,
            ItemCount: inputs.Prepared.Items.Count,
            BatchSize: inputs.BatchSize,
            MaxCharsPerBatch: inputs.MaxCharsPerBatch,
            MaxOutputTokens: inputs.Request.MaxOutputTokens,
            TotalSourceChars: inputs.Prepared.TotalSourceChars,
            TotalMaskedChars: inputs.Prepared.TotalMaskedChars,
            BatchRequestCount: inputs.BatchRequestCount,
            TextRequestCount: inputs.TextRequestCount,
            SystemPromptTokens: inputs.TokenCounts.SystemPromptTokens,
            InputTokensBatchPrompts: inputs.TokenCounts.InputTokensBatch,
            InputTokensTextPrompts: inputs.TokenCounts.InputTokensText,
            OutputTokens: inputs.OutputEstimate,
            CostEstimates: inputs.CostEstimates
        );
    }

    private static void ValidateEstimateRequest(TranslationCostEstimateRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.ApiKey))
        {
            throw new ArgumentException("API key is required.", nameof(request));
        }
        if (string.IsNullOrWhiteSpace(request.ModelName))
        {
            throw new ArgumentException("Model name is required.", nameof(request));
        }
    }

    private static (int BatchSize, int MaxChars) NormalizeBatchAndCharLimits(TranslationCostEstimateRequest request)
    {
        return (
            BatchSize: TranslationLimits.BatchSize(request.BatchSize),
            MaxChars: TranslationLimits.MaxChars(request.MaxChars)
        );
    }

    private async Task<IReadOnlyList<(long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status)>> LoadRowsForEstimationAsync(
        TranslationCostEstimateRequest request,
        CancellationToken cancellationToken
    )
    {
        var statuses = request.IncludeCompletedItems
            ? new[]
            {
                StringEntryStatus.Pending,
                StringEntryStatus.InProgress,
                StringEntryStatus.Done,
                StringEntryStatus.Skipped,
                StringEntryStatus.Error,
                StringEntryStatus.Edited,
            }
            : new[] { StringEntryStatus.Pending, StringEntryStatus.Error };

        // The run keeps rows without letters and hidden topic identifiers as they are, without a request.
        var rows = await _db.GetStringSourceContextsByStatusAsync(statuses, cancellationToken);
        return rows
            .Where(row => TranslationService.HasLettersToTranslate(row.SourceText)
                && !XTranslatorAi.Core.Text.Lqa.Internal.Rules.HiddenTopicRule.IsHiddenTopicIdentifier(row.Rec, row.SourceText))
            .ToList();
    }

    private async Task<PreparedEstimationInput> PrepareEstimationInputAsync(
        TranslationCostEstimateRequest request,
        IReadOnlyList<(long Id, string SourceText, string? Rec, string? Edid, StringEntryStatus Status)> rows,
        CancellationToken cancellationToken
    )
    {
        var projectGlossaryEntries = await _db.GetGlossaryAsync(cancellationToken);
        var glossaryEntries = GlossaryMerger.Merge(projectGlossaryEntries, request.GlobalGlossary);
        var placeholderMasker = new PlaceholderMasker(new PlaceholderMaskerOptions(KeepSkyrimTagsRaw: request.KeepSkyrimTagsRaw));
        var glossaryApplier = new GlossaryApplier(glossaryEntries);
        return PrepareEstimationRows(rows, request.TargetLang, placeholderMasker, glossaryApplier, cancellationToken);
    }

    private readonly record struct InputTokenCounts(
        long SystemPromptTokens,
        long InputTokensBatch,
        long InputTokensText
    );

    private async Task<InputTokenCounts> CountInputTokensAsync(
        TranslationCostEstimateRequest request,
        IReadOnlyList<string> batchPrompts,
        IReadOnlyList<string> textPrompts,
        CancellationToken cancellationToken
    )
    {
        var systemPromptTokens = await SafeCountTokensAsync(request.ApiKey, request.ModelName, request.SystemPrompt, cancellationToken);
        var inputTokensBatch = await CountTokensForManyTextsAsync(request.ApiKey, request.ModelName, batchPrompts, cancellationToken);
        var inputTokensText = await CountTokensForManyTextsAsync(request.ApiKey, request.ModelName, textPrompts, cancellationToken);
        return new InputTokenCounts(systemPromptTokens, inputTokensBatch, inputTokensText);
    }

    private static List<ModelCostEstimate> BuildCostEstimates(
        TranslationCostEstimateRequest request,
        long systemPromptTokens,
        long inputTokensUserPrompts,
        OutputTokenEstimate outputEstimate,
        int requestCountTotal
    )
    {
        var modelsForCost = new[]
        {
            request.ModelName,
            GeminiModelCatalog.DefaultModel,
            GeminiModelCatalog.LowCostModel,
        };

        var costEstimates = new List<ModelCostEstimate>();
        foreach (var m in modelsForCost.Distinct(StringComparer.OrdinalIgnoreCase))
        {
            if (!TryGetPricing(m, out var pricing))
            {
                continue;
            }

            costEstimates.Add(
                ComputeModelCostEstimate(
                    modelName: m,
                    pricing: pricing,
                    usage: new ModelCostUsage(
                        SystemPromptTokens: systemPromptTokens,
                        RequestCount: requestCountTotal,
                        InputTokensUserPrompts: inputTokensUserPrompts,
                        OutputEstimate: outputEstimate,
                        PromptCacheTtlHours: 2
                    )
                )
            );
        }

        return costEstimates;
    }

    private readonly record struct OutputTokenEstimateInputs(
        TranslationCostEstimateRequest Request,
        IReadOnlyList<string> BatchPrompts,
        IReadOnlyList<string> TextPrompts,
        long InputTokensBatch,
        long InputTokensText,
        int RequestCountTotal,
        CancellationToken CancellationToken
    );

    private async Task<OutputTokenEstimate> EstimateOutputTokensAsync(OutputTokenEstimateInputs inputs)
    {
        var outputEstimate = EstimateOutputTokensHeuristic(inputs.InputTokensBatch + inputs.InputTokensText, inputs.Request.TargetLang);
        if (!inputs.Request.RunSampleToEstimateOutputTokens || inputs.RequestCountTotal <= 0)
        {
            return outputEstimate;
        }

        var sampledEstimate = await TryEstimateOutputTokensWithSampleAsync(inputs);
        return sampledEstimate ?? outputEstimate;
    }

    private async Task<OutputTokenEstimate?> TryEstimateOutputTokensWithSampleAsync(OutputTokenEstimateInputs inputs)
    {
        try
        {
            var sampleRequest = new OutputTokenSampleRequest(
                ApiKey: inputs.Request.ApiKey,
                ModelName: inputs.Request.ModelName,
                SystemPrompt: inputs.Request.SystemPrompt,
                BatchPrompts: inputs.BatchPrompts,
                TextPrompts: inputs.TextPrompts,
                MaxOutputTokens: inputs.Request.MaxOutputTokens,
                CancellationToken: inputs.CancellationToken
            );
            var sampled = await EstimateOutputTokensWithSampleAsync(sampleRequest);

            if (!sampled.HasAny)
            {
                return null;
            }

            return BuildOutputTokenEstimateFromSample(
                inputs.InputTokensBatch,
                inputs.InputTokensText,
                sampled.BatchRatio,
                sampled.TextRatio
            );
        }
        catch (OperationCanceledException) when (inputs.CancellationToken.IsCancellationRequested) { throw; }
        catch
        {
            return null;
        }
    }

    private static OutputTokenEstimate BuildOutputTokenEstimateFromSample(
        long inputTokensBatch,
        long inputTokensText,
        double? batchRatio,
        double? textRatio
    )
    {
        var estBatch = batchRatio is > 0 ? (long)Math.Round(inputTokensBatch * batchRatio.Value) : -1;
        var estText = textRatio is > 0 ? (long)Math.Round(inputTokensText * textRatio.Value) : -1;

        if (estBatch < 0 && textRatio is > 0)
        {
            estBatch = (long)Math.Round(inputTokensBatch * textRatio.Value);
        }
        if (estText < 0 && batchRatio is > 0)
        {
            estText = (long)Math.Round(inputTokensText * batchRatio.Value);
        }

        var point = Math.Max(0, estBatch) + Math.Max(0, estText);
        // A sample extrapolation band, not a confidence interval or a retry allowance.
        var low = (long)Math.Floor(point * 0.9);
        var high = (long)Math.Ceiling(point * 1.15);
        return new OutputTokenEstimate(
            Low: low,
            High: high,
            Point: point,
            BatchRatio: batchRatio,
            TextRatio: textRatio,
            UsedSample: true
        );
    }
}
