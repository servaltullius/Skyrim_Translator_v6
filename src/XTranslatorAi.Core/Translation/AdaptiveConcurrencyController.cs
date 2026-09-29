using System;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.Translation;

internal sealed class AdaptiveConcurrencyController
{
    private int _max = 1;
    private int _limit = 1;
    private int _inFlight;
    private int _successStreak;

    public void Configure(int maxConcurrency)
    {
        var normalized = Math.Max(1, maxConcurrency);
        Interlocked.Exchange(ref _max, normalized);
        Interlocked.Exchange(ref _limit, normalized);
        Interlocked.Exchange(ref _inFlight, 0);
        Interlocked.Exchange(ref _successStreak, 0);
    }

    public void Reset()
    {
        Interlocked.Exchange(ref _max, 1);
        Interlocked.Exchange(ref _limit, 1);
        Interlocked.Exchange(ref _inFlight, 0);
        Interlocked.Exchange(ref _successStreak, 0);
    }

    public bool IsEnabled => Volatile.Read(ref _max) > 1;

    public async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        if (!IsEnabled)
        {
            return;
        }

        const int initialDelayMs = 20;
        const int maxDelayMs = 200;
        var delayMs = initialDelayMs;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var limit = Volatile.Read(ref _limit);
            var inFlight = Volatile.Read(ref _inFlight);
            if (inFlight < limit)
            {
                if (Interlocked.CompareExchange(ref _inFlight, inFlight + 1, inFlight) == inFlight)
                {
                    return;
                }

                // CAS contention - retry immediately without delay
                continue;
            }

            // No slot available - wait with exponential backoff
            await Task.Delay(delayMs, cancellationToken);
            delayMs = Math.Min(delayMs * 2, maxDelayMs);
        }
    }

    public void ReleaseSlot()
    {
        if (!IsEnabled)
        {
            return;
        }

        var remaining = Interlocked.Decrement(ref _inFlight);
        if (remaining < 0)
        {
            Interlocked.Exchange(ref _inFlight, 0);
        }
    }

    public void RegisterRateLimit()
    {
        if (!IsEnabled)
        {
            return;
        }

        while (true)
        {
            var current = Volatile.Read(ref _limit);
            if (current <= 1)
            {
                Interlocked.Exchange(ref _successStreak, 0);
                return;
            }

            var next = Math.Max(1, current / 2);
            if (next >= current)
            {
                next = current - 1;
            }

            if (Interlocked.CompareExchange(ref _limit, next, current) == current)
            {
                Interlocked.Exchange(ref _successStreak, 0);
                return;
            }
        }
    }

    public void RegisterSuccess()
    {
        if (!IsEnabled)
        {
            return;
        }

        var limit = Volatile.Read(ref _limit);
        var max = Volatile.Read(ref _max);
        if (limit >= max)
        {
            Interlocked.Exchange(ref _successStreak, 0);
            return;
        }

        var streak = Interlocked.Increment(ref _successStreak);
        var threshold = Math.Max(8, limit * 8);
        if (streak < threshold)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _successStreak, 0, streak) != streak)
        {
            return;
        }

        while (true)
        {
            limit = Volatile.Read(ref _limit);
            max = Volatile.Read(ref _max);
            if (limit >= max)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _limit, limit + 1, limit) == limit)
            {
                return;
            }
        }
    }
}
