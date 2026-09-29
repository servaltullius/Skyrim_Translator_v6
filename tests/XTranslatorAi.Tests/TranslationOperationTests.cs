using XTranslatorAi.App.Services;

namespace XTranslatorAi.Tests;

public class TranslationOperationTests
{
    [Fact]
    public async Task Stop_WaitsForCanceledRunCleanupBeforeReturning()
    {
        var operation = new TranslationOperation();
        var cancellationObserved = NewSignal();
        var finishCleanup = NewSignal();
        var cleanedUp = false;
        var run = operation.RunAsync(async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            finally
            {
                cancellationObserved.TrySetResult();
                await finishCleanup.Task;
                cleanedUp = true;
            }
        });

        var stopping = operation.StopAsync();
        await cancellationObserved.Task.WaitAsync(TimeSpan.FromSeconds(10));
        Assert.False(stopping.IsCompleted);
        Assert.True(operation.IsRunning);
        finishCleanup.SetResult();
        await stopping.WaitAsync(TimeSpan.FromSeconds(10));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
        Assert.True(cleanedUp);
        Assert.False(operation.IsRunning);
    }

    [Fact]
    public async Task FailedPreparation_ReleasesLifetimeAndAllowsAnotherRun()
    {
        var operation = new TranslationOperation();
        await Assert.ThrowsAsync<IOException>(() => operation.RunAsync(_ => throw new IOException("fake DB failure")));
        Assert.False(operation.IsRunning);
        var completed = false;
        await operation.RunAsync(_ => { completed = true; return Task.CompletedTask; });
        Assert.True(completed);
    }

    [Fact]
    public async Task AnotherRun_CannotStartWhileCancellationCleanupIsPending()
    {
        var operation = new TranslationOperation();
        var finish = NewSignal();
        var first = operation.RunAsync(_ => finish.Task);
        operation.Cancel();
        await Assert.ThrowsAsync<InvalidOperationException>(() => operation.RunAsync(_ => Task.CompletedTask));
        finish.SetResult();
        await first;
        await operation.RunAsync(_ => Task.CompletedTask);
    }

    private static TaskCompletionSource NewSignal() => new(TaskCreationOptions.RunContinuationsAsynchronously);
}
