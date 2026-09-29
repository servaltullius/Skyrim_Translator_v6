using System;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.App.Services;

/// <summary>Owns cancellation and completion together so a project cannot close a running operation's DB.</summary>
public sealed class TranslationOperation
{
    private readonly object _sync = new();
    private CancellationTokenSource? _cancellation;
    private Task _completion = Task.CompletedTask;

    public bool IsRunning
    {
        get { lock (_sync) return _cancellation != null; }
    }

    public Task RunAsync(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        CancellationTokenSource cancellation;
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        lock (_sync)
        {
            if (_cancellation != null)
            {
                throw new InvalidOperationException("A translation operation is already running.");
            }

            cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            _completion = completion.Task;
        }

        _ = ExecuteAsync(action, cancellation, completion);
        return completion.Task;
    }

    public void Cancel()
    {
        CancellationTokenSource? cancellation;
        lock (_sync) cancellation = _cancellation;
        try { cancellation?.Cancel(); }
        catch (ObjectDisposedException) { /* The operation completed concurrently. */ }
    }

    public async Task StopAsync()
    {
        Task completion;
        lock (_sync) completion = _completion;
        Cancel();
        try { await completion; }
        catch (OperationCanceledException) { }
    }

    private async Task ExecuteAsync(
        Func<CancellationToken, Task> action,
        CancellationTokenSource cancellation,
        TaskCompletionSource completion)
    {
        Exception? error = null;
        try { await action(cancellation.Token); }
        catch (Exception ex) { error = ex; }
        finally
        {
            lock (_sync)
            {
                if (ReferenceEquals(_cancellation, cancellation)) _cancellation = null;
            }
            cancellation.Dispose();
        }

        if (error is OperationCanceledException canceled) completion.TrySetCanceled(canceled.CancellationToken);
        else if (error != null) completion.TrySetException(error);
        else completion.TrySetResult();
    }
}
