using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private static TimeSpan ComputeRetryDelay(Exception ex, int attempt)
    {
        if (attempt < 0)
        {
            attempt = 0;
        }

        var baseSeconds = Math.Min(30, 1.5 * attempt + 1);
        if (IsRateLimit(ex))
        {
            baseSeconds = Math.Min(90, 10 * (attempt + 1));
        }

        var delay = TimeSpan.FromSeconds(baseSeconds);
        if (TryGetRetryAfter(ex, out var retryAfter) && retryAfter > delay)
        {
            delay = retryAfter;
        }

        return AddJitter(delay);
    }

    private static bool ShouldRetry(Exception ex)
    {
        if (IsOutputValidationError(ex))
        {
            return false;
        }

        // GeminiException includes HTTP status in message; retry 429/5xx.
        if (ex is GeminiException)
        {
            return IsRateLimit(ex) || IsServerError(ex);
        }

        return ex is HttpRequestException || ex is TaskCanceledException;
    }

    private static bool IsRateLimit(Exception ex)
    {
        foreach (var current in EnumerateExceptions(ex))
        {
            if (current is GeminiHttpException http && http.StatusCode == 429)
            {
                return true;
            }

            var msg = current.Message;
            if (msg.IndexOf("429", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("too many", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("rate", StringComparison.OrdinalIgnoreCase) >= 0 && msg.IndexOf("limit", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("RESOURCE_EXHAUSTED", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCredentialError(Exception ex)
    {
        foreach (var current in EnumerateExceptions(ex))
        {
            if (current is GeminiHttpException http && http.StatusCode is 401 or 403)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsServerError(Exception ex)
    {
        foreach (var current in EnumerateExceptions(ex))
        {
            if (current is GeminiHttpException http && http.StatusCode is >= 500 and <= 599)
            {
                return true;
            }

            var msg = current.Message;
            if (msg.IndexOf("HTTP 500", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("HTTP 502", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("HTTP 503", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("HTTP 504", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsApiKeyFailoverError(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        if (IsCredentialError(ex) || IsRateLimit(ex) || IsServerError(ex))
        {
            return true;
        }

        foreach (var current in EnumerateExceptions(ex))
        {
            if (current is HttpRequestException || current is TaskCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetRetryAfter(Exception ex, out TimeSpan retryAfter)
    {
        foreach (var current in EnumerateExceptions(ex))
        {
            if (current is GeminiHttpException http && http.RetryAfter is { } ra && ra > TimeSpan.Zero)
            {
                retryAfter = ra;
                return true;
            }
        }

        retryAfter = default;
        return false;
    }

    private static TimeSpan AddJitter(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return delay;
        }

        var ms = delay.TotalMilliseconds;
        var maxExtraMs = Math.Min(5000, ms * 0.20);
        var extraMs = Random.Shared.NextDouble() * maxExtraMs;
        var totalMs = Math.Min(ms + extraMs, 10 * 60 * 1000); // cap at 10 minutes
        return TimeSpan.FromMilliseconds(totalMs);
    }

    private static bool IsOutputValidationError(Exception ex)
    {
        foreach (var msg in EnumerateExceptionMessages(ex))
        {
            if (msg.IndexOf("Missing token in translation", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("Token sequence mismatch", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("Unexpected token in translation", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("Token count mismatch", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("Missing placeholder token", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("Missing glossary token", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("Model output did not contain", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("Batch size mismatch", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("Model JSON missing", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            if (msg.IndexOf("JsonReaderException", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
        }

        return ex is System.Text.Json.JsonException;
    }

    private static IEnumerable<Exception> EnumerateExceptions(Exception ex)
    {
        Exception? current = ex;
        var depth = 0;
        while (current != null && depth < 6)
        {
            yield return current;
            current = current.InnerException;
            depth++;
        }
    }

    private static IEnumerable<string> EnumerateExceptionMessages(Exception ex)
    {
        foreach (var current in EnumerateExceptions(ex))
        {
            yield return current.Message;
        }
    }

    private static string FormatError(Exception ex)
    {
        var sb = new StringBuilder();
        Exception? current = ex;
        var depth = 0;

        while (current != null && depth < 6)
        {
            if (depth > 0)
            {
                sb.Append(" | ");
            }
            sb.Append(current.GetType().Name);
            sb.Append(": ");
            sb.Append(current.Message);

            current = current.InnerException;
            depth++;
        }

        var s = sb.ToString();
        return s.Length <= 800 ? s : s[..800];
    }
}
