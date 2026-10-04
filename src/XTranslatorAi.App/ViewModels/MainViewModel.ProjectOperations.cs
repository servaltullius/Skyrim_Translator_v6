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

    public bool IsWorkspaceInteractive => !_isSwitchingProject && !_isClosing && !IsPluginIoBusy;

    private void NotifyWorkspaceAvailability()
    {
        OnPropertyChanged(nameof(IsWorkspaceInteractive));
        OpenXmlCommand.NotifyCanExecuteChanged();
        OpenPluginCommand.NotifyCanExecuteChanged();
        StartTranslationCommand.NotifyCanExecuteChanged();
        EstimateCostCommand.NotifyCanExecuteChanged();
        GenerateProjectContextCommand.NotifyCanExecuteChanged();
        SaveProjectContextCommand.NotifyCanExecuteChanged();
        ClearProjectContextCommand.NotifyCanExecuteChanged();
        ExportXmlCommand.NotifyCanExecuteChanged();
        ExportPluginCommand.NotifyCanExecuteChanged();
        RetranslateSelectedCommand.NotifyCanExecuteChanged();
        RetranslateVisibleCommand.NotifyCanExecuteChanged();
        ImportPreviousTranslationCommand.NotifyCanExecuteChanged();
        ClearPreviousTranslationCommand.NotifyCanExecuteChanged();
        NotifyProjectOperationCommands();
    }

    private void NotifyProjectOperationCommands()
    {
        RunCompare1Command.NotifyCanExecuteChanged();
        RunCompare2Command.NotifyCanExecuteChanged();
        RunCompare3Command.NotifyCanExecuteChanged();
        RunCompareAllCommand.NotifyCanExecuteChanged();
        RefreshModelsCommand.NotifyCanExecuteChanged();
    }

    // Project tools do not start during a run or while a project is being switched or closed. Compare and the
    // model list "새로고침" have no other condition, and used to stay enabled and silently do nothing then.
    private bool CanStartProjectOperation() => IsWorkspaceInteractive && !IsTranslating;

    private async Task RunProjectOperationAsync(string name, Func<CancellationToken, Task> action)
    {
        if (!CanStartProjectOperation()) return;
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
                               && property.Name != nameof(OpenXmlCommand)
                               && property.Name != nameof(OpenPluginCommand))
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
        // Closing used to drop an edit that was never saved with "번역문 저장". If the DB refuses the save,
        // let the user decide rather than either losing the edit silently or never being able to close.
        if (!await TryCommitPendingDestEditsAsync()
            && _uiInteractionService.ShowMessage(
                "번역문 수정을 저장하지 못했습니다.\n\n저장하지 않고 닫을까요?",
                "번역문 저장",
                UiMessageBoxButton.YesNo,
                UiMessageBoxImage.Warning,
                UiMessageBoxResult.No
            ) != UiMessageBoxResult.Yes)
        {
            _isClosing = false;
            NotifyWorkspaceAvailability();
            return false;
        }

        if (!await TrySaveListEditsBeforeReloadAsync(EditableLists.All, "창을 닫으면", closing: true))
        {
            _isClosing = false;
            NotifyWorkspaceAvailability();
            return false;
        }

        _projectLoadCancellation?.Cancel();
        try
        {
            await StopAllProjectOperationsAsync();
            foreach (var opening in new[] { OpenXmlCommand.ExecutionTask, OpenPluginCommand.ExecutionTask, _droppedFileOpen })
            {
                if (opening is { IsCompleted: false })
                {
                    await opening;
                }
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
