using System;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Services;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    private readonly ProjectOperationTracker _projectOperations = new();
    private CancellationTokenSource? _projectLoadCancellation;
    private bool _isClosing;

    public bool IsWorkspaceInteractive => !_isSwitchingProject && !_isClosing;

    private void NotifyWorkspaceAvailability()
    {
        OnPropertyChanged(nameof(IsWorkspaceInteractive));
        StartTranslationCommand.NotifyCanExecuteChanged();
        EstimateCostCommand.NotifyCanExecuteChanged();
        GenerateProjectContextCommand.NotifyCanExecuteChanged();
    }

    private async Task RunProjectOperationAsync(string name, Func<CancellationToken, Task> action)
    {
        if (!IsWorkspaceInteractive || IsTranslating) return;
        var task = _projectOperations.RunAsync(action);
        NotifyWorkspaceAvailability();
        try { await task; }
        catch (OperationCanceledException)
        {
            if (IsWorkspaceInteractive) StatusMessage = $"{name} 작업을 중지했습니다.";
        }
        catch (Exception ex) { SetUserFacingError(name, ex); }
        finally { NotifyWorkspaceAvailability(); }
    }

    private async Task StopAllProjectOperationsAsync()
    {
        await Task.WhenAll(StopTranslationAndWaitAsync(), _projectOperations.SuspendAndStopAsync());
        // Short DB edits and exports are allowed to finish before their DB is disposed.
        // Discover generated async commands so newly added commands receive the same boundary.
        var tasks = GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public)
            .Where(property => typeof(IAsyncRelayCommand).IsAssignableFrom(property.PropertyType)
                               && property.Name != nameof(OpenXmlCommand))
            .Select(property => (IAsyncRelayCommand?)property.GetValue(this))
            .Select(command => command?.ExecutionTask)
            .Where(task => task != null && !task.IsCompleted)
            .Cast<Task>()
            .ToArray();
        try { await Task.WhenAll(tasks); }
        catch (OperationCanceledException) { }
    }

    public async Task<bool> TryCloseWorkspaceAsync()
    {
        _isClosing = true;
        NotifyWorkspaceAvailability();
        _projectLoadCancellation?.Cancel();
        try
        {
            await StopAllProjectOperationsAsync();
            if (OpenXmlCommand.ExecutionTask is { IsCompleted: false } opening)
            {
                await opening;
            }
            await DisposeProjectDbAsync();
            ResetProjectState();
            return true;
        }
        catch (Exception ex)
        {
            SetUserFacingError("프로젝트 종료", ex);
            _isClosing = false;
            _projectOperations.Resume();
            NotifyWorkspaceAvailability();
            return false;
        }
    }
}
