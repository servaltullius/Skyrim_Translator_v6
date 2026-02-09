using System;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationCostEstimator
{
    private static bool TryGetPricing(string modelName, out GeminiPricing pricing)
    {
        return GeminiPricingTable.TryGetPricing(modelName, out pricing);
    }

    private readonly record struct ModelCostUsage(
        long SystemPromptTokens,
        int RequestCount,
        long InputTokensUserPrompts,
        OutputTokenEstimate OutputEstimate,
        double PromptCacheTtlHours
    );

    private static ModelCostEstimate ComputeModelCostEstimate(
        string modelName,
        GeminiPricing pricing,
        ModelCostUsage usage
    )
    {
        var cacheCost = (usage.SystemPromptTokens / 1_000_000.0) * pricing.CacheUsdPer1M
                        + (usage.SystemPromptTokens / 1_000_000.0) * pricing.CacheStorageUsdPer1MPerHour * usage.PromptCacheTtlHours;

        var inputCostWithCache = (usage.InputTokensUserPrompts / 1_000_000.0) * pricing.InputUsdPer1M + cacheCost;
        var inputCostNoCache = ((usage.InputTokensUserPrompts + usage.SystemPromptTokens * usage.RequestCount) / 1_000_000.0)
                               * pricing.InputUsdPer1M;

        var outputLowCost = (usage.OutputEstimate.Low / 1_000_000.0) * pricing.OutputUsdPer1M;
        var outputHighCost = (usage.OutputEstimate.High / 1_000_000.0) * pricing.OutputUsdPer1M;

        var batchInputCost = ((usage.InputTokensUserPrompts + usage.SystemPromptTokens * usage.RequestCount) / 1_000_000.0)
                             * pricing.BatchInputUsdPer1M;
        var batchOutputLowCost = (usage.OutputEstimate.Low / 1_000_000.0) * pricing.BatchOutputUsdPer1M;
        var batchOutputHighCost = (usage.OutputEstimate.High / 1_000_000.0) * pricing.BatchOutputUsdPer1M;

        return new ModelCostEstimate(
            ModelName: modelName,
            InputUsdPer1M: pricing.InputUsdPer1M,
            OutputUsdPer1M: pricing.OutputUsdPer1M,
            BatchInputUsdPer1M: pricing.BatchInputUsdPer1M,
            BatchOutputUsdPer1M: pricing.BatchOutputUsdPer1M,
            CacheUsdPer1M: pricing.CacheUsdPer1M,
            CacheStorageUsdPer1MPerHour: pricing.CacheStorageUsdPer1MPerHour,
            PromptCacheTtlHours: usage.PromptCacheTtlHours,
            InputCostUsdWithPromptCache: inputCostWithCache,
            InputCostUsdWithoutPromptCache: inputCostNoCache,
            OutputCostUsdLow: outputLowCost,
            OutputCostUsdHigh: outputHighCost,
            TotalCostUsdLowWithPromptCache: inputCostWithCache + outputLowCost,
            TotalCostUsdHighWithPromptCache: inputCostWithCache + outputHighCost,
            TotalCostUsdLowWithoutPromptCache: inputCostNoCache + outputLowCost,
            TotalCostUsdHighWithoutPromptCache: inputCostNoCache + outputHighCost,
            BatchInputCostUsd: batchInputCost,
            BatchOutputCostUsdLow: batchOutputLowCost,
            BatchOutputCostUsdHigh: batchOutputHighCost,
            TotalCostUsdLowBatch: batchInputCost + batchOutputLowCost,
            TotalCostUsdHighBatch: batchInputCost + batchOutputHighCost
        );
    }
}
