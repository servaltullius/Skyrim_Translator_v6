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
    private static readonly Regex XtTokenRegex = new(
        pattern: @"__XT_(?:PH|TERM)(?:_[A-Z0-9]+)?_[0-9]{4}__",
        options: RegexOptions.CultureInvariant
    );

    private const string EndSentinelToken = "__XT_PH_9999__";

    private readonly ProjectDb _db;
    private readonly GeminiClient _gemini;

    public TranslationCostEstimator(ProjectDb db, GeminiClient gemini)
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

        var rows = await LoadRowsForEstimationAsync(request, cancellationToken);
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
        return BuildTranslationCostEstimate(resultInputs);
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
            throw new ArgumentException("API key is required.", nameof(request.ApiKey));
        }
        if (string.IsNullOrWhiteSpace(request.ModelName))
        {
            throw new ArgumentException("Model name is required.", nameof(request.ModelName));
        }
    }

    private static (int BatchSize, int MaxChars) NormalizeBatchAndCharLimits(TranslationCostEstimateRequest request)
    {
        return (
            BatchSize: Math.Clamp(request.BatchSize, 1, 100),
            MaxChars: Math.Clamp(request.MaxChars, 1000, 50000)
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

        return await _db.GetStringSourceContextsByStatusAsync(statuses, cancellationToken);
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
            "gemini-2.5-flash-lite",
            "gemini-3.0-flash-preview",
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
        // Provide a small safety band; real runs include retries and occasional longer outputs.
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
