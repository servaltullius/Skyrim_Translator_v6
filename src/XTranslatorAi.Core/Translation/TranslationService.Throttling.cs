using System;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.Core.Translation;

public sealed partial class TranslationService
{
    private long _globalThrottleUntilUnixMs;

    public void ResetGlobalThrottle()
    {
        Interlocked.Exchange(ref _globalThrottleUntilUnixMs, 0);
    }

    private async Task WaitForGlobalThrottleAsync(CancellationToken cancellationToken)
    {
        var until = Interlocked.Read(ref _globalThrottleUntilUnixMs);
        if (until <= 0)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var remainingMs = until - now;
        if (remainingMs <= 0)
        {
            return;
        }

        await Task.Delay(TimeSpan.FromMilliseconds(remainingMs), cancellationToken);
    }

    /// <summary>
    /// A retry's wait, cut short when another worker stops the run for its key: it waited out its 429 back-off (up to
    /// minutes) and sent the request once more with the old key, holding up the switch.
    /// </summary>
    /// <returns>Whether the run stopped for its key, and the request is not to be sent again.</returns>
    private async Task<bool> WaitUnlessStoppedForKeyAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        var until = DateTimeOffset.UtcNow + delay;
        while (!Ctx.StoppedForKey)
        {
            var left = until - DateTimeOffset.UtcNow;
            if (left <= TimeSpan.Zero)
            {
                return false;
            }

            await Task.Delay(left < TimeSpan.FromSeconds(1) ? left : TimeSpan.FromSeconds(1), cancellationToken);
        }

        return true;
    }

    private void ExtendGlobalThrottle(TimeSpan delay)
    {
        if (delay <= TimeSpan.Zero)
        {
            return;
        }

        var now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        var until = now + (long)Math.Ceiling(delay.TotalMilliseconds);

        while (true)
        {
            var current = Interlocked.Read(ref _globalThrottleUntilUnixMs);
            if (until <= current)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _globalThrottleUntilUnixMs, until, current) == current)
            {
                return;
            }
        }
    }
}
