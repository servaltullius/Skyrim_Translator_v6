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
