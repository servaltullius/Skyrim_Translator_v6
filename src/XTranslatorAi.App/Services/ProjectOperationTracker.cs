using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace XTranslatorAi.App.Services;

/// <summary>Tracks concurrent project tools and prevents new work while switching or closing a project.</summary>
public sealed class ProjectOperationTracker
{
    private readonly object _sync = new();
    private readonly HashSet<Task> _pending = new();
    private CancellationTokenSource _cancellation = new();
    private bool _suspended;

    public bool IsRunning { get { lock (_sync) return _pending.Count != 0; } }

    public Task RunAsync(Func<CancellationToken, Task> action)
    {
        ArgumentNullException.ThrowIfNull(action);
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        CancellationToken token;
        lock (_sync)
        {
            if (_suspended) return Task.CompletedTask;
            token = _cancellation.Token;
            _pending.Add(completion.Task);
        }
        _ = ExecuteAsync(action, token, completion);
        return completion.Task;
    }

    public async Task SuspendAndStopAsync()
    {
        Task[] pending;
        CancellationTokenSource cancellation;
        lock (_sync)
        {
            _suspended = true;
            cancellation = _cancellation;
            pending = _pending.ToArray();
        }
        cancellation.Cancel();
        try { await Task.WhenAll(pending); }
        catch (OperationCanceledException) { }
    }

    public void Resume()
    {
        lock (_sync)
        {
            if (!_suspended) return;
            if (_pending.Count != 0) throw new InvalidOperationException("Project operations have not finished yet.");
            _cancellation.Dispose();
            _cancellation = new CancellationTokenSource();
            _suspended = false;
        }
    }

    private async Task ExecuteAsync(Func<CancellationToken, Task> action, CancellationToken token, TaskCompletionSource completion)
    {
        Exception? error = null;
        try { await action(token); }
        catch (Exception ex) { error = ex; }
        lock (_sync) _pending.Remove(completion.Task);
        if (error is OperationCanceledException canceled && token.IsCancellationRequested)
            completion.TrySetCanceled(canceled.CancellationToken);
        else if (error != null) completion.TrySetException(error);
        else completion.TrySetResult();
    }
}
