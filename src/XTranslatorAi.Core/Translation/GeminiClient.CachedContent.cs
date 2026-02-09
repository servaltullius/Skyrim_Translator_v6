using System;
using System.Diagnostics;
using System.Net.Http.Json;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.Translation;

public sealed partial class GeminiClient
{
    public async Task<string> CreateCachedContentAsync(
        string apiKey,
        string modelName,
        string systemInstructionText,
        TimeSpan ttl,
        CancellationToken cancellationToken
    )
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        int? statusCode = null;
        try
        {
            var result = await CreateCachedContentCoreAsync(apiKey, modelName, systemInstructionText, ttl, cancellationToken);
            statusCode = result.StatusCode;
            LogCall(
                new GeminiCallLogEntry(
                    StartedAt: startedAt,
                    Duration: sw.Elapsed,
                    Operation: GeminiCallOperation.CreateCachedContent,
                    ModelName: modelName,
                    StatusCode: statusCode,
                    Success: true,
                    ErrorMessage: null,
                    ApiKeyMask: MaskApiKey(apiKey)
                )
            );
            return result.Name;
        }
        catch (Exception ex)
        {
            statusCode ??= TryGetStatusCode(ex);
            LogCall(
                new GeminiCallLogEntry(
                    StartedAt: startedAt,
                    Duration: sw.Elapsed,
                    Operation: GeminiCallOperation.CreateCachedContent,
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

    private async Task<(string Name, int StatusCode)> CreateCachedContentCoreAsync(
        string apiKey,
        string modelName,
        string systemInstructionText,
        TimeSpan ttl,
        CancellationToken cancellationToken
    )
    {
        RequireApiKey(apiKey);
        RequireModelName(modelName);

        var request = new GeminiCreateCachedContentRequest(
            Model: NormalizeModelResourceName(modelName),
            Contents: null,
            SystemInstruction: CreateSystemInstruction(systemInstructionText),
            Ttl: FormatSecondsTtl(ttl),
            DisplayName: "Tullius Translator prompt cache"
        );

        var url = $"https://generativelanguage.googleapis.com/v1beta/cachedContents?key={Uri.EscapeDataString(apiKey)}";
        using var resp = await _httpClient.PostAsJsonAsync(url, request, JsonOptions, cancellationToken);
        var statusCode = (int)resp.StatusCode;
        var body = await resp.Content.ReadAsStringAsync(cancellationToken);
        if (!resp.IsSuccessStatusCode)
        {
            throw CreateHttpException("CreateCachedContent", resp, body);
        }

        var parsed = DeserializeOrThrow<GeminiCachedContent>("CreateCachedContent", body);

        var name = parsed?.Name;
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new GeminiException($"CreateCachedContent: missing name. {Truncate(body)}");
        }

        return (name, statusCode);
    }

    public async Task DeleteCachedContentAsync(string apiKey, string cacheName, CancellationToken cancellationToken)
    {
        var startedAt = DateTimeOffset.UtcNow;
        var sw = Stopwatch.StartNew();
        int? statusCode = null;
        try
        {
            statusCode = await DeleteCachedContentCoreAsync(apiKey, cacheName, cancellationToken);
            if (statusCode == null)
            {
                return;
            }

            LogCall(
                new GeminiCallLogEntry(
                    StartedAt: startedAt,
                    Duration: sw.Elapsed,
                    Operation: GeminiCallOperation.DeleteCachedContent,
                    ModelName: null,
                    StatusCode: statusCode,
                    Success: true,
                    ErrorMessage: null,
                    ApiKeyMask: MaskApiKey(apiKey)
                )
            );
        }
        catch (Exception ex)
        {
            statusCode ??= TryGetStatusCode(ex);
            LogCall(
                new GeminiCallLogEntry(
                    StartedAt: startedAt,
                    Duration: sw.Elapsed,
                    Operation: GeminiCallOperation.DeleteCachedContent,
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

    private async Task<int?> DeleteCachedContentCoreAsync(
        string apiKey,
        string cacheName,
        CancellationToken cancellationToken
    )
    {
        RequireApiKey(apiKey);
        if (string.IsNullOrWhiteSpace(cacheName))
        {
            return null;
        }

        var normalized = NormalizeCachedContentResourceName(cacheName);
        var url = $"https://generativelanguage.googleapis.com/v1beta/{normalized}?key={Uri.EscapeDataString(apiKey)}";
        using var resp = await _httpClient.DeleteAsync(url, cancellationToken);
        var statusCode = (int)resp.StatusCode;
        if (!resp.IsSuccessStatusCode)
        {
            var body = await resp.Content.ReadAsStringAsync(cancellationToken);
            throw CreateHttpException("DeleteCachedContent", resp, body);
        }

        return statusCode;
    }
}
