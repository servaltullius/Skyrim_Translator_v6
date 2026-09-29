using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.Core.Translation;

public interface IGeminiClient
{
    // Clients without usage metadata remain usable, but cannot supply a billed-token sample.
    async Task<GeminiGenerationResult> GenerateContentWithUsageAsync(
        string apiKey,
        string modelName,
        GeminiGenerateContentRequest request,
        CancellationToken cancellationToken
    ) => new(await GenerateContentAsync(apiKey, modelName, request, cancellationToken), null, null, null);

    Task<string> GenerateContentAsync(
        string apiKey,
        string modelName,
        GeminiGenerateContentRequest request,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<string>> GenerateContentCandidatesAsync(
        string apiKey,
        string modelName,
        GeminiGenerateContentRequest request,
        CancellationToken cancellationToken
    );

    Task<int> CountTokensAsync(
        string apiKey,
        string modelName,
        string text,
        CancellationToken cancellationToken
    );

    Task<IReadOnlyList<GeminiModel>> ListModelsAsync(
        string apiKey,
        CancellationToken cancellationToken
    );

    Task<string> CreateCachedContentAsync(
        string apiKey,
        string modelName,
        string systemInstructionText,
        TimeSpan ttl,
        CancellationToken cancellationToken
    );

    Task DeleteCachedContentAsync(
        string apiKey,
        string cacheName,
        CancellationToken cancellationToken
    );
}
