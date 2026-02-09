using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.Translation;

public sealed partial class GeminiClient
{
    public async Task<IReadOnlyList<GeminiModel>> ListModelsAsync(string apiKey, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        int? statusCode = null;
        try
        {
            var url = $"https://generativelanguage.googleapis.com/v1beta/models?key={Uri.EscapeDataString(apiKey)}";
            using var resp = await _httpClient.GetAsync(url, cancellationToken);
            statusCode = (int)resp.StatusCode;
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                throw CreateHttpException("ListModels", resp, body);
            }

            var parsed = JsonSerializer.Deserialize<GeminiListModelsResponse>(body, JsonOptions);
            var models = (IReadOnlyList<GeminiModel>?)parsed?.Models ?? Array.Empty<GeminiModel>();

            LogCall(
                new GeminiCallLogEntry(
                    StartedAt: startedAt,
                    Duration: sw.Elapsed,
                    Operation: GeminiCallOperation.ListModels,
                    ModelName: null,
                    StatusCode: statusCode,
                    Success: true,
                    ErrorMessage: null,
                    ApiKeyMask: MaskApiKey(apiKey)
                )
            );

            return models;
        }
        catch (Exception ex)
        {
            statusCode ??= TryGetStatusCode(ex);
            LogCall(
                new GeminiCallLogEntry(
                    StartedAt: startedAt,
                    Duration: sw.Elapsed,
                    Operation: GeminiCallOperation.ListModels,
                    ModelName: null,
                    StatusCode: statusCode,
                    Success: false,
                    ErrorMessage: Truncate(ex.Message, 800),
                    ApiKeyMask: MaskApiKey(apiKey)
                )
            );
            throw;
        }
    }
}
