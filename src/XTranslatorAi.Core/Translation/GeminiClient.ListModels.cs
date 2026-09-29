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
        RequireApiKey(apiKey);
        var startedAt = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        int? statusCode = null;
        try
        {
            var models = new List<GeminiModel>();
            var names = new HashSet<string>(StringComparer.Ordinal);
            var pageTokens = new HashSet<string>(StringComparer.Ordinal);
            string? pageToken = null;
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var url = $"https://generativelanguage.googleapis.com/v1beta/models?pageSize=1000&key={Uri.EscapeDataString(apiKey)}";
                if (pageToken != null)
                {
                    url += $"&pageToken={Uri.EscapeDataString(pageToken)}";
                }
                using var resp = await _httpClient.GetAsync(url, cancellationToken);
                statusCode = (int)resp.StatusCode;
                var body = await resp.Content.ReadAsStringAsync(cancellationToken);
                if (!resp.IsSuccessStatusCode)
                {
                    throw CreateHttpException("ListModels", resp, body);
                }

                var parsed = JsonSerializer.Deserialize<GeminiListModelsResponse>(body, JsonOptions)
                    ?? throw new GeminiException("ListModels returned an empty response.");
                if (parsed.Models != null)
                {
                    foreach (var model in parsed.Models)
                    {
                        if (!string.IsNullOrWhiteSpace(model.Name) && names.Add(model.Name))
                        {
                            models.Add(model);
                        }
                    }
                }

                pageToken = string.IsNullOrWhiteSpace(parsed.NextPageToken) ? null : parsed.NextPageToken;
                if (pageToken != null && !pageTokens.Add(pageToken))
                {
                    throw new GeminiException("ListModels repeated a page token; the model list could not be completed.");
                }
            }
            while (pageToken != null);

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
