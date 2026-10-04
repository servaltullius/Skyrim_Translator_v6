using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.Translation;

public sealed partial class GeminiClient
{
    public async Task<int> CountTokensAsync(string apiKey, string modelName, string text, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        int? statusCode = null;
        try
        {
            var result = await CountTokensCoreAsync(apiKey, modelName, text, cancellationToken);
            statusCode = result.StatusCode;

            LogCall(
                new GeminiCallLogEntry(
                    StartedAt: startedAt,
                    Duration: sw.Elapsed,
                    Operation: GeminiCallOperation.CountTokens,
                    ModelName: modelName,
                    StatusCode: statusCode,
                    Success: true,
                    ErrorMessage: null,
                    ApiKeyMask: MaskApiKey(apiKey)
                )
            );
            return result.Total;
        }
        catch (Exception ex)
        {
            statusCode ??= TryGetStatusCode(ex);
            LogCall(
                new GeminiCallLogEntry(
                    StartedAt: startedAt,
                    Duration: sw.Elapsed,
                    Operation: GeminiCallOperation.CountTokens,
                    ModelName: modelName,
                    StatusCode: statusCode,
                    Success: false,
                    ErrorMessage: Truncate(ex.Message, 800),
                    ApiKeyMask: MaskApiKey(apiKey)
                )
            );
            throw;
        }
    }

    private async Task<(int Total, int StatusCode)> CountTokensCoreAsync(
        string apiKey,
        string modelName,
        string text,
        CancellationToken cancellationToken
    )
    {
        RequireApiKey(apiKey);
        RequireModelName(modelName);

        var request = new GeminiCountTokensRequest(
            Contents: new List<GeminiContent>
            {
                new(Role: null, Parts: new List<GeminiPart> { new(text ?? "") }),
            }
        );

        using var resp = await SendAsync(HttpMethod.Post, $"models/{Uri.EscapeDataString(modelName)}:countTokens", apiKey, request,
            cancellationToken);
        var statusCode = (int)resp.StatusCode;
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            throw CreateHttpException("CountTokens", resp, body);
        }

        var parsed = DeserializeOrThrow<GeminiCountTokensResponse>("CountTokens", body);

        var total = parsed?.TotalTokens;
        if (total is null || total < 0)
        {
            throw new GeminiException($"CountTokens: missing totalTokens. {Truncate(body)}");
        }

        return (total.Value, statusCode);
    }
}
