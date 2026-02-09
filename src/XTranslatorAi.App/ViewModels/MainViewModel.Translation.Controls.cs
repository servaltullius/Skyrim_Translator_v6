using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanStopTranslation))]
    private void StopTranslation()
    {
        if (_translationCts == null)
        {
            return;
        }

        _resumeTcs?.TrySetResult(true);
        _resumeTcs = null;
        IsPaused = false;

        _translationCts.Cancel();
        StatusMessage = "Translation stopped.";
    }

    private bool CanStopTranslation() => IsTranslating;

    [RelayCommand(CanExecute = nameof(CanTogglePauseTranslation))]
    private void TogglePauseTranslation()
    {
        if (!IsTranslating)
        {
            return;
        }

        if (!IsPaused)
        {
            _resumeTcs ??= new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            IsPaused = true;
            StatusMessage = "Paused (will pause between batches).";
            return;
        }

        _resumeTcs?.TrySetResult(true);
        _resumeTcs = null;
        IsPaused = false;
        StatusMessage = "Resumed.";
    }

    private bool CanTogglePauseTranslation() => IsTranslating;

    public string PauseButtonText => IsPaused ? "Resume" : "Pause";

    partial void OnIsPausedChanged(bool value) => OnPropertyChanged(nameof(PauseButtonText));

    private Task WaitIfPausedAsync(CancellationToken cancellationToken)
    {
        var tcs = _resumeTcs;
        if (tcs == null)
        {
            return Task.CompletedTask;
        }

        return tcs.Task.WaitAsync(cancellationToken);
    }
}
