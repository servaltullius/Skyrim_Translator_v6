using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;

namespace XTranslatorAi.Core.Translation;

public sealed partial class GeminiClient
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly HttpClient _httpClient;
    private readonly IGeminiCallLogger? _logger;

    public GeminiClient(HttpClient httpClient, IGeminiCallLogger? logger = null)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    private static string Truncate(string s, int max = 500) => s.Length <= max ? s : s[..max];

    private static void RequireApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            throw new ArgumentException("API key is required.", nameof(apiKey));
        }
    }

    private static string MaskApiKey(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "…";
        }

        var trimmed = apiKey.Trim();
        if (trimmed.Length <= 6)
        {
            return "…" + trimmed;
        }

        return "…" + trimmed.Substring(trimmed.Length - 6);
    }

    private static void RequireModelName(string modelName)
    {
        if (string.IsNullOrWhiteSpace(modelName))
        {
            throw new ArgumentException("Model name is required.", nameof(modelName));
        }
    }

    private static string NormalizeModelResourceName(string modelName)
    {
        return modelName.StartsWith("models/", StringComparison.Ordinal) ? modelName : "models/" + modelName;
    }

    private static string NormalizeCachedContentResourceName(string cacheName)
    {
        return cacheName.StartsWith("cachedContents/", StringComparison.Ordinal)
            ? cacheName
            : "cachedContents/" + cacheName;
    }

    private static GeminiContent CreateSystemInstruction(string systemInstructionText)
    {
        return new GeminiContent(
            Role: null,
            Parts: new List<GeminiPart> { new(systemInstructionText ?? "") }
        );
    }

    private static string FormatSecondsTtl(TimeSpan ttl)
    {
        return $"{Math.Max(1, (int)Math.Ceiling(ttl.TotalSeconds))}s";
    }

    private static T? DeserializeOrThrow<T>(string operation, string body)
    {
        try
        {
            return JsonSerializer.Deserialize<T>(body, JsonOptions);
        }
        catch (JsonException e)
        {
            throw new GeminiException($"{operation}: invalid JSON response. {Truncate(body)}", e);
        }
    }

    private void LogCall(GeminiCallLogEntry entry)
    {
        try
        {
            _logger?.Log(entry);
        }
        catch
        {
            // ignore
        }
    }

    private static int? TryGetStatusCode(Exception ex)
    {
        return ex is GeminiHttpException http ? http.StatusCode : null;
    }

    private static readonly Regex RetryInSecondsRegex = new(
        pattern: @"retry\s+in\s+(?<sec>[0-9]+(?:\.[0-9]+)?)s",
        options: RegexOptions.IgnoreCase | RegexOptions.CultureInvariant
    );

    private static GeminiHttpException CreateHttpException(string operation, HttpResponseMessage resp, string body)
    {
        return new GeminiHttpException(
            operation: operation,
            statusCode: (int)resp.StatusCode,
            reasonPhrase: resp.ReasonPhrase,
            retryAfter: TryGetRetryAfter(resp, body),
            message: $"{operation} failed: HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}. {Truncate(body)}"
        );
    }

    private static TimeSpan? TryGetRetryAfter(HttpResponseMessage resp, string body)
    {
        var header = TryGetRetryAfter(resp);
        var fromBody = TryGetRetryAfterFromBody(body);

        if (header is not { } h)
        {
            return fromBody;
        }

        if (fromBody is not { } b)
        {
            return h;
        }

        return h >= b ? h : b;
    }

    private static TimeSpan? TryGetRetryAfter(HttpResponseMessage resp)
    {
        var header = resp.Headers.RetryAfter;
        if (header == null)
        {
            return null;
        }

        if (header.Delta is { } delta)
        {
            return delta <= TimeSpan.Zero ? null : delta;
        }

        if (header.Date is { } date)
        {
            var deltaFromNow = date - DateTimeOffset.UtcNow;
            return deltaFromNow <= TimeSpan.Zero ? null : deltaFromNow;
        }

        return null;
    }

    private static TimeSpan? TryGetRetryAfterFromBody(string body)
    {
        if (string.IsNullOrWhiteSpace(body))
        {
            return null;
        }

        try
        {
            using var doc = JsonDocument.Parse(body);
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (!doc.RootElement.TryGetProperty("error", out var error) || error.ValueKind != JsonValueKind.Object)
            {
                return null;
            }

            if (error.TryGetProperty("details", out var details) && details.ValueKind == JsonValueKind.Array)
            {
                foreach (var item in details.EnumerateArray())
                {
                    if (item.ValueKind != JsonValueKind.Object)
                    {
                        continue;
                    }

                    if (!item.TryGetProperty("retryDelay", out var retryDelay) || retryDelay.ValueKind != JsonValueKind.String)
                    {
                        continue;
                    }

                    if (TryParseDurationSeconds(retryDelay.GetString(), out var delay))
                    {
                        return delay;
                    }
                }
            }

            // Fallback: some responses include human text like "Please retry in 16.45s." in error.message
            if (error.TryGetProperty("message", out var message) && message.ValueKind == JsonValueKind.String)
            {
                if (TryParseRetryInSeconds(message.GetString() ?? "", out var delay))
                {
                    return delay;
                }
            }
        }
        catch
        {
            // ignore
        }

        // Last resort: scan the raw body text
        if (TryParseRetryInSeconds(body, out var bodyDelay))
        {
            return bodyDelay;
        }

        return null;
    }

    private static bool TryParseRetryInSeconds(string text, out TimeSpan delay)
    {
        delay = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var m = RetryInSecondsRegex.Match(text);
        if (!m.Success)
        {
            return false;
        }

        var secStr = m.Groups["sec"].Value;
        return TryParseSeconds(secStr, out delay);
    }

    private static bool TryParseDurationSeconds(string? duration, out TimeSpan delay)
    {
        delay = default;
        if (string.IsNullOrWhiteSpace(duration))
        {
            return false;
        }

        var s = duration.Trim();
        if (s.EndsWith("s", StringComparison.OrdinalIgnoreCase))
        {
            s = s[..^1];
        }

        return TryParseSeconds(s, out delay);
    }

    private static bool TryParseSeconds(string secondsText, out TimeSpan delay)
    {
        delay = default;
        if (string.IsNullOrWhiteSpace(secondsText))
        {
            return false;
        }

        if (!double.TryParse(secondsText.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out var seconds))
        {
            return false;
        }

        if (seconds <= 0)
        {
            return false;
        }

        delay = TimeSpan.FromSeconds(seconds);
        return true;
    }
}
