using XTranslatorAi.App.Services;

namespace XTranslatorAi.Tests;

public class ProjectOperationTrackerTests
{
    [Fact]
    public async Task Suspend_CancelsEveryConcurrentToolAndWaitsForCleanup()
    {
        var tracker = new ProjectOperationTracker();
        var canceled = new[] { Signal(), Signal(), Signal() };
        var cleanup = Signal();
        var tasks = canceled.Select(observed => tracker.RunAsync(async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            finally { observed.TrySetResult(); await cleanup.Task; }
        })).ToArray();

        var stopping = tracker.SuspendAndStopAsync();
        await Task.WhenAll(canceled.Select(signal => signal.Task)).WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(stopping.IsCompleted);
        var startedWhileSuspended = false;
        await tracker.RunAsync(_ => { startedWhileSuspended = true; return Task.CompletedTask; });
        Assert.False(startedWhileSuspended);
        cleanup.SetResult();
        await stopping;
        foreach (var task in tasks) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(tracker.IsRunning);
        tracker.Resume();
        await tracker.RunAsync(token => { Assert.False(token.IsCancellationRequested); return Task.CompletedTask; });
    }

    [Fact]
    public async Task Cancellation_PreventsSubsequentCompareSlotsFromStarting()
    {
        var tracker = new ProjectOperationTracker();
        var entered = Signal();
        var slotsStarted = 0;
        var work = tracker.RunAsync(async token =>
        {
            for (var slot = 1; slot <= 3; slot++)
            {
                token.ThrowIfCancellationRequested();
                slotsStarted++;
                entered.TrySetResult();
                await Task.Delay(Timeout.Infinite, token);
            }
        });
        await entered.Task;
        await tracker.SuspendAndStopAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.Equal(1, slotsStarted);
    }

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
