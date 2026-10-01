using System.Threading;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels;

public partial class MainViewModel
{
    [RelayCommand(CanExecute = nameof(CanStopTranslation))]
    private void StopTranslation()
    {
        if (!_translationOperation.IsRunning)
        {
            return;
        }

        _resumeTcs?.TrySetResult(true);
        _resumeTcs = null;
        IsPaused = false;

        _translationOperation.Cancel();
        StatusMessage = "번역을 중지하는 중입니다... 완료된 번역은 보존합니다.";
    }

    public async Task StopTranslationAndWaitAsync()
    {
        StopTranslation();
        await _translationOperation.StopAsync();
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
            StatusMessage = "일시정지했습니다. 진행 중인 묶음이 끝나면 멈춥니다.";
            return;
        }

        _resumeTcs?.TrySetResult(true);
        _resumeTcs = null;
        IsPaused = false;
        StatusMessage = "번역을 다시 시작했습니다.";
    }

    private bool CanTogglePauseTranslation() => IsTranslating;

    public string PauseButtonText => IsPaused ? "계속" : "일시정지";

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
