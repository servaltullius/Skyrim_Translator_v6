using System;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private void ConfigureAdaptiveConcurrency(int maxConcurrency)
    {
        var normalized = Math.Max(1, maxConcurrency);
        Interlocked.Exchange(ref _adaptiveConcurrencyMax, normalized);
        Interlocked.Exchange(ref _adaptiveConcurrencyLimit, normalized);
        Interlocked.Exchange(ref _adaptiveInFlight, 0);
        Interlocked.Exchange(ref _adaptiveSuccessStreak, 0);
    }

    private void ResetAdaptiveConcurrency()
    {
        Interlocked.Exchange(ref _adaptiveConcurrencyMax, 1);
        Interlocked.Exchange(ref _adaptiveConcurrencyLimit, 1);
        Interlocked.Exchange(ref _adaptiveInFlight, 0);
        Interlocked.Exchange(ref _adaptiveSuccessStreak, 0);
    }

    private bool IsAdaptiveConcurrencyEnabled()
    {
        return Volatile.Read(ref _adaptiveConcurrencyMax) > 1;
    }

    private async Task WaitForAdaptiveConcurrencySlotAsync(CancellationToken cancellationToken)
    {
        if (!IsAdaptiveConcurrencyEnabled())
        {
            return;
        }

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var limit = Volatile.Read(ref _adaptiveConcurrencyLimit);
            var inFlight = Volatile.Read(ref _adaptiveInFlight);
            if (inFlight < limit)
            {
                if (Interlocked.CompareExchange(ref _adaptiveInFlight, inFlight + 1, inFlight) == inFlight)
                {
                    return;
                }

                continue;
            }

            await Task.Delay(20, cancellationToken);
        }
    }

    private void ReleaseAdaptiveConcurrencySlot()
    {
        if (!IsAdaptiveConcurrencyEnabled())
        {
            return;
        }

        var remaining = Interlocked.Decrement(ref _adaptiveInFlight);
        if (remaining < 0)
        {
            Interlocked.Exchange(ref _adaptiveInFlight, 0);
        }
    }

    private void RegisterAdaptiveRateLimit()
    {
        if (!IsAdaptiveConcurrencyEnabled())
        {
            return;
        }

        while (true)
        {
            var current = Volatile.Read(ref _adaptiveConcurrencyLimit);
            if (current <= 1)
            {
                Interlocked.Exchange(ref _adaptiveSuccessStreak, 0);
                return;
            }

            var next = Math.Max(1, current / 2);
            if (next >= current)
            {
                next = current - 1;
            }

            if (Interlocked.CompareExchange(ref _adaptiveConcurrencyLimit, next, current) == current)
            {
                Interlocked.Exchange(ref _adaptiveSuccessStreak, 0);
                return;
            }
        }
    }

    private void RegisterAdaptiveRequestSuccess()
    {
        if (!IsAdaptiveConcurrencyEnabled())
        {
            return;
        }

        var limit = Volatile.Read(ref _adaptiveConcurrencyLimit);
        var max = Volatile.Read(ref _adaptiveConcurrencyMax);
        if (limit >= max)
        {
            Interlocked.Exchange(ref _adaptiveSuccessStreak, 0);
            return;
        }

        var streak = Interlocked.Increment(ref _adaptiveSuccessStreak);
        var threshold = Math.Max(8, limit * 8);
        if (streak < threshold)
        {
            return;
        }

        if (Interlocked.CompareExchange(ref _adaptiveSuccessStreak, 0, streak) != streak)
        {
            return;
        }

        while (true)
        {
            limit = Volatile.Read(ref _adaptiveConcurrencyLimit);
            max = Volatile.Read(ref _adaptiveConcurrencyMax);
            if (limit >= max)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _adaptiveConcurrencyLimit, limit + 1, limit) == limit)
            {
                return;
            }
        }
    }
}
