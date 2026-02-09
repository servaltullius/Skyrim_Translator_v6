using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationCostEstimator
{
    private sealed record OutputTokenSampleRequest(
        string ApiKey,
        string ModelName,
        string SystemPrompt,
        IReadOnlyList<string> BatchPrompts,
        IReadOnlyList<string> TextPrompts,
        int MaxOutputTokens,
        CancellationToken CancellationToken
    );

    private static List<GeminiSafetySetting> BuildDefaultSafetySettings()
    {
        return new List<GeminiSafetySetting>
        {
            new("HARM_CATEGORY_HARASSMENT", "BLOCK_NONE"),
            new("HARM_CATEGORY_HATE_SPEECH", "BLOCK_NONE"),
            new("HARM_CATEGORY_SEXUALLY_EXPLICIT", "BLOCK_NONE"),
            new("HARM_CATEGORY_DANGEROUS_CONTENT", "BLOCK_NONE"),
        };
    }

    private static GeminiGenerateContentRequest BuildSampleRequest(
        string userPrompt,
        string? cachedContent,
        string systemPrompt,
        GeminiGenerationConfig generationConfig
    )
    {
        return new GeminiGenerateContentRequest(
            Contents: new List<GeminiContent> { new(Role: "user", Parts: new List<GeminiPart> { new(userPrompt) }) },
            CachedContent: cachedContent,
            SystemInstruction: cachedContent != null
                ? null
                : new GeminiContent(Role: null, Parts: new List<GeminiPart> { new(systemPrompt ?? "") }),
            GenerationConfig: generationConfig,
            SafetySettings: BuildDefaultSafetySettings()
        );
    }

    private async Task<(long InTokens, long OutTokens)> TryAccumulateSampleTokensAsync(
        string apiKey,
        string modelName,
        string systemPrompt,
        IReadOnlyList<string> prompts,
        string? cachedContent,
        GeminiGenerationConfig generationConfig,
        CancellationToken cancellationToken
    )
    {
        var totalIn = 0L;
        var totalOut = 0L;
        foreach (var p in prompts)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (string.IsNullOrWhiteSpace(p))
            {
                continue;
            }

            var inTokens = await SafeCountTokensAsync(apiKey, modelName, p, cancellationToken);
            var req = BuildSampleRequest(p, cachedContent, systemPrompt, generationConfig);

            try
            {
                var outText = await _gemini.GenerateContentAsync(apiKey, modelName, req, cancellationToken);
                var outTokens = await SafeCountTokensAsync(apiKey, modelName, outText, cancellationToken);
                totalIn += inTokens;
                totalOut += outTokens;
            }
            catch
            {
                // ignore sample failures
            }
        }

        return (totalIn, totalOut);
    }

    private async Task<string?> TryCreateSampleCachedContentAsync(
        string apiKey,
        string modelName,
        string systemPrompt,
        CancellationToken cancellationToken
    )
    {
        try
        {
            return await _gemini.CreateCachedContentAsync(
                apiKey,
                modelName,
                systemPrompt,
                ttl: TimeSpan.FromMinutes(20),
                cancellationToken
            );
        }
        catch
        {
            return null;
        }
    }

    private async Task<SampleRatios> EstimateOutputTokensWithSampleAsync(OutputTokenSampleRequest request)
    {
        var schema = TranslationPrompt.BuildResponseSchema();
        var thinkingConfig = GetThinkingConfigForModel(request.ModelName);
        var batchConfig = BuildBatchSampleConfig(request.ModelName, request.MaxOutputTokens, schema, thinkingConfig);
        var textConfig = BuildTextSampleConfig(request.ModelName, request.MaxOutputTokens, thinkingConfig);
        // Sampling can be very slow on huge projects (multiple GenerateContent calls).
        // Prefer fewer, more representative prompts as the workload grows.
        var batchDesired = request.BatchPrompts.Count switch
        {
            <= 0 => 0,
            <= 20 => 3,
            <= 200 => 2,
            _ => 1
        };
        var textDesired = request.TextPrompts.Count switch
        {
            <= 0 => 0,
            <= 20 => 2,
            _ => 1
        };

        var batchSample = PickQuantileSamples(request.BatchPrompts, desired: batchDesired);
        var textSample = PickQuantileSamples(request.TextPrompts, desired: textDesired);

        var cachedContent = await TryCreateSampleCachedContentAsync(
            request.ApiKey,
            request.ModelName,
            request.SystemPrompt,
            request.CancellationToken
        );

        try
        {
            var batchRatio = await TryEstimateSampleRatioAsync(
                request,
                batchSample,
                cachedContent,
                batchConfig
            );
            var textRatio = await TryEstimateSampleRatioAsync(
                request,
                textSample,
                cachedContent,
                textConfig
            );
            return new SampleRatios(batchRatio, textRatio);
        }
        finally
        {
            if (!string.IsNullOrWhiteSpace(cachedContent))
            {
                try
                {
                    await _gemini.DeleteCachedContentAsync(request.ApiKey, cachedContent!, request.CancellationToken);
                }
                catch
                {
                    // ignore
                }
            }
        }
    }

    private static GeminiGenerationConfig BuildBatchSampleConfig(
        string modelName,
        int maxOutputTokens,
        JsonElement schema,
        GeminiThinkingConfig? thinkingConfig
    )
    {
        return new GeminiGenerationConfig(
            Temperature: GeminiModelPolicy.GetTemperatureForTranslation(modelName, 0.1),
            MaxOutputTokens: maxOutputTokens,
            ResponseMimeType: "application/json",
            ResponseSchema: schema,
            ThinkingConfig: thinkingConfig
        );
    }

    private static GeminiGenerationConfig BuildTextSampleConfig(
        string modelName,
        int maxOutputTokens,
        GeminiThinkingConfig? thinkingConfig
    )
    {
        return new GeminiGenerationConfig(
            Temperature: GeminiModelPolicy.GetTemperatureForTranslation(modelName, 0.05),
            MaxOutputTokens: maxOutputTokens,
            ResponseMimeType: null,
            ResponseSchema: null,
            ThinkingConfig: thinkingConfig
        );
    }

    private async Task<double?> TryEstimateSampleRatioAsync(
        OutputTokenSampleRequest request,
        IReadOnlyList<string> prompts,
        string? cachedContent,
        GeminiGenerationConfig generationConfig
    )
    {
        var (inTokens, outTokens) = await TryAccumulateSampleTokensAsync(
            request.ApiKey,
            request.ModelName,
            request.SystemPrompt,
            prompts,
            cachedContent,
            generationConfig,
            request.CancellationToken
        );

        if (inTokens <= 0 || outTokens <= 0)
        {
            return null;
        }

        return (double)outTokens / inTokens;
    }

    private static IReadOnlyList<string> PickQuantileSamples(IReadOnlyList<string> prompts, int desired)
    {
        if (desired <= 0 || prompts.Count == 0)
        {
            return Array.Empty<string>();
        }

        if (prompts.Count <= desired)
        {
            return prompts;
        }

        var ordered = prompts
            .Select((p, idx) => (Len: p?.Length ?? 0, Idx: idx))
            .OrderBy(t => t.Len)
            .ToList();

        // Avoid pathological extremes on large runs. The longest prompts can produce very large (and slow)
        // sample outputs, but add little value for estimating a global output/input ratio.
        var minQuantile = prompts.Count >= 20 ? 0.1 : 0.0;
        var maxQuantile = prompts.Count >= 20 ? 0.9 : 1.0;

        var picks = new List<string>(capacity: desired);
        for (var i = 0; i < desired; i++)
        {
            var q = desired <= 1
                ? 0.5
                : minQuantile + (maxQuantile - minQuantile) * (i / (double)(desired - 1));
            var pos = (int)Math.Round(q * (ordered.Count - 1));
            pos = Math.Clamp(pos, 0, ordered.Count - 1);
            picks.Add(prompts[ordered[pos].Idx]);
        }

        return picks;
    }

    private static GeminiThinkingConfig? GetThinkingConfigForModel(string modelName)
    {
        return GeminiModelPolicy.GetThinkingConfigForTranslation(modelName);
    }
}
