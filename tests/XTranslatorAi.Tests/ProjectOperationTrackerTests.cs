using XTranslatorAi.App.Services;

namespace XTranslatorAi.Tests;

/// <summary>
/// The bodies run on the thread pool: under xUnit's synchronization context every continuation of the tools waits
/// for one of its few worker slots, which other test classes hold while they run. On a CI runner the canceled tools
/// once did not get a slot within 10 seconds (release 1.11, passed on the rerun).
/// </summary>
public class ProjectOperationTrackerTests
{
    private static readonly TimeSpan Patience = TimeSpan.FromSeconds(30);

    [Fact]
    public Task Suspend_CancelsEveryConcurrentToolAndWaitsForCleanup() => Task.Run(async () =>
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
        await Task.WhenAll(canceled.Select(signal => signal.Task)).WaitAsync(Patience);
        Assert.False(stopping.IsCompleted);
        var startedWhileSuspended = false;
        await tracker.RunAsync(_ => { startedWhileSuspended = true; return Task.CompletedTask; });
        Assert.False(startedWhileSuspended);
        cleanup.SetResult();
        await stopping.WaitAsync(Patience);
        foreach (var task in tasks) await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
        Assert.False(tracker.IsRunning);
        tracker.Resume();
        await tracker.RunAsync(token => { Assert.False(token.IsCancellationRequested); return Task.CompletedTask; });
    });

    [Fact]
    public Task Cancellation_PreventsSubsequentCompareSlotsFromStarting() => Task.Run(async () =>
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
        await entered.Task.WaitAsync(Patience);
        await tracker.SuspendAndStopAsync().WaitAsync(Patience);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => work);
        Assert.Equal(1, slotsStarted);
    });

    private static TaskCompletionSource Signal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
