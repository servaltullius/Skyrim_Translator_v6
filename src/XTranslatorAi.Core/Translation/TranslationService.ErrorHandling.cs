using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using XTranslatorAi.Core.Diagnostics;

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
        foreach (var current in ExceptionTraversal.Enumerate(ex))
        {
            if (current is GeminiHttpException http && GeminiErrorKinds.IsRateLimit(http))
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// With API-key failover off, a spent daily quota made every remaining batch wait through its rate-limit retries
    /// and end as Error, which took hours on a large project. After this many batches or rows in a row end on a
    /// rate limit, with no successful generation call between them, the run stops and leaves the rest Pending.
    /// </summary>
    internal const int MaxConsecutiveRateLimitFailures = 3;

    /// <summary>Called where a batch or a row has failed for good, before it is marked Error.</summary>
    private void ThrowIfRateLimitStreakReached(Exception ex, bool apiKeyFailover)
    {
        // A lost connection or a timeout is the same for every key: without this, failover off marked every
        // remaining row Error within seconds (connection refused) or hours (15-minute timeouts).
        var connection = IsConnectionFailure(ex);

        // With failover on, the first rate limit already stops the run to switch keys.
        if (!connection && (apiKeyFailover || !IsRateLimit(ex)))
        {
            return;
        }

        var streak = Interlocked.Increment(ref Ctx.ConsecutiveRateLimitFailures);
        if (streak < MaxConsecutiveRateLimitFailures && !Ctx.RateLimitAborted)
        {
            return;
        }

        // Batches still in flight that end on a rate limit stop too, so their rows go back to Pending as well.
        Ctx.RateLimitAborted = true;
        throw new TranslationRateLimitAbortException(MaxConsecutiveRateLimitFailures, ex, connection);
    }

    /// <summary>The request timed out (HttpClient.Timeout), as opposed to the user stopping the run.</summary>
    private static bool IsTimeout(Exception ex, CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
        {
            return false;
        }

        foreach (var current in ExceptionTraversal.Enumerate(ex))
        {
            if (current is TimeoutException or TaskCanceledException)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>No answer from the API at all: the connection failed or the request timed out.</summary>
    private static bool IsConnectionFailure(Exception ex)
    {
        var sawHttpRequest = false;
        foreach (var current in ExceptionTraversal.Enumerate(ex))
        {
            if (current is GeminiHttpException)
            {
                return false; // The API answered with an error status.
            }

            sawHttpRequest |= current is HttpRequestException or TimeoutException;
        }

        return sawHttpRequest;
    }

    private void ResetRateLimitStreak() => Interlocked.Exchange(ref Ctx.ConsecutiveRateLimitFailures, 0);

    private static bool IsRateLimitAbort(Exception ex)
    {
        foreach (var current in ExceptionTraversal.Enumerate(ex))
        {
            if (current is TranslationRateLimitAbortException)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsCredentialError(Exception ex)
    {
        foreach (var current in ExceptionTraversal.Enumerate(ex))
        {
            if (current is GeminiHttpException http && GeminiErrorKinds.IsInvalidApiKey(http))
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsServerError(Exception ex)
    {
        foreach (var current in ExceptionTraversal.Enumerate(ex))
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

        foreach (var current in ExceptionTraversal.Enumerate(ex))
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
        foreach (var current in ExceptionTraversal.Enumerate(ex))
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
        foreach (var msg in ExceptionTraversal.EnumerateMessages(ex))
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
