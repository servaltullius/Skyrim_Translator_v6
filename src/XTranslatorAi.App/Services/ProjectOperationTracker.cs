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
        await WaitIgnoringFailuresAsync(pending);
    }

    /// <summary>
    /// Waits for the tasks to end however they end. Each operation reported its own failure; one that failed while
    /// being stopped (a network error in a model refresh or Compare) made the stop throw, and opening another XML then
    /// reported "XML 로드" after closing the open project, without opening the new one.
    /// </summary>
    public static async Task WaitIgnoringFailuresAsync(IReadOnlyCollection<Task> tasks)
    {
        try { await Task.WhenAll(tasks); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            AppLog.Write($"WARN 멈추는 동안 끝난 작업의 오류: {ex.Message}");
        }
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
