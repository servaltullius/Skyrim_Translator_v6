using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Threading.Tasks;
using System.Windows.Controls;
using XTranslatorAi.App.ViewModels;

namespace XTranslatorAi.App;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;
    private bool _closeRequested;
    private bool _closeCompleted;

    public MainWindow() : this(null) { }

    public MainWindow(MainViewModel? viewModel)
    {
        InitializeComponent();
        var build = System.Reflection.CustomAttributeExtensions.GetCustomAttribute<System.Reflection.AssemblyInformationalVersionAttribute>(typeof(App).Assembly)?.InformationalVersion;
        Title = $"Tullius Translator · {build?.Split('+')[0] ?? "개발 빌드"}";
        if (viewModel != null)
        {
            DataContext = viewModel;
        }

        HookViewModel(DataContext as MainViewModel);
        DataContextChanged += (_, _) => HookViewModel(DataContext as MainViewModel);
        Loaded += (_, _) => SyncPasswordBoxesFromViewModel();

        // Preview (tunneling) events: text boxes would otherwise take a dragged file and refuse it.
        PreviewDragOver += OnWindowDragOver;
        PreviewDrop += OnWindowDrop;
        DragLeave += OnWindowDragLeave;
    }

    // A plugin over the previous-translation row is left to that row when it can be linked there.
    private bool LeaveToDropZone(DragEventArgs e, string? path)
        => FileDrop.IsInsideDropZone(e.OriginalSource as DependencyObject) && _vm?.CanLinkDroppedPreviousTranslation(path) == true;

    private void OnWindowDragOver(object sender, DragEventArgs e)
    {
        var path = FileDrop.SingleFile(e.Data);
        if (_vm == null || !FileDrop.HasFiles(e.Data) || LeaveToDropZone(e, path))
        {
            DropOverlay.Visibility = Visibility.Collapsed;
            return;
        }

        var canOpen = _vm.CanOpenDroppedFile(path);
        e.Effects = canOpen ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
        if (canOpen)
        {
            ShowDropOverlay("놓으면 엽니다", Path.GetFileName(path!));
        }
        else if (path == null)
        {
            ShowDropOverlay("파일을 하나만 끌어다 놓으세요", "ESP·ESM·ESL 플러그인이나 xTranslator XML을 열 수 있습니다.");
        }
        else if (!MainViewModel.IsPluginFile(path) && !MainViewModel.IsXmlFile(path))
        {
            ShowDropOverlay("열 수 없는 파일입니다", "ESP·ESM·ESL 플러그인이나 xTranslator XML을 끌어다 놓으세요.");
        }
        else
        {
            ShowDropOverlay("지금은 열 수 없습니다", "번역이나 다른 작업이 끝난 뒤 다시 끌어다 놓으세요.");
        }
    }

    private async void OnWindowDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
        var path = FileDrop.SingleFile(e.Data);
        if (_vm == null || !FileDrop.HasFiles(e.Data) || LeaveToDropZone(e, path))
        {
            return;
        }

        e.Handled = true;
        if (!_vm.CanOpenDroppedFile(path))
        {
            return;
        }

        Activate();
        try
        {
            await _vm.OpenDroppedFileAsync(path!);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "파일 열기 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnWindowDragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when the pointer moves between controls; hide only when it leaves the window.
        var point = e.GetPosition(this);
        if (point.X <= 0 || point.Y <= 0 || point.X >= ActualWidth || point.Y >= ActualHeight)
        {
            DropOverlay.Visibility = Visibility.Collapsed;
        }
    }

    private void ShowDropOverlay(string title, string detail)
    {
        DropOverlayTitle.Text = title;
        DropOverlayDetail.Text = detail;
        DropOverlay.Visibility = Visibility.Visible;
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private void ApiKeyBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is PasswordBox pb)
        {
            Vm.ApiKey = pb.Password;
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= Vm_OnPropertyChanged;
        }

        base.OnClosed(e);
    }

    protected override void OnClosing(CancelEventArgs e)
    {
        if (_closeCompleted || _vm == null)
        {
            base.OnClosing(e);
            return;
        }

        // Keep the dispatcher alive while HTTP cancellation and DB cleanup finish.
        e.Cancel = true;
        base.OnClosing(e);
        if (_closeRequested) return;
        _closeRequested = true;
        IsEnabled = false;
        _ = CompleteCloseAsync();
    }

    private async Task CompleteCloseAsync()
    {
        // Never call Close recursively inside the initial Closing event.
        await Task.Yield();
        try
        {
            if (_vm != null && !await _vm.TryCloseWorkspaceAsync())
            {
                _closeRequested = false;
                IsEnabled = true;
                return;
            }
            _closeCompleted = true;
            Close();
        }
        catch (Exception ex)
        {
            _closeRequested = false;
            IsEnabled = true;
            MessageBox.Show(this, ex.Message, "프로젝트 종료 오류", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void HookViewModel(MainViewModel? vm)
    {
        if (_vm != null)
        {
            _vm.PropertyChanged -= Vm_OnPropertyChanged;
        }

        _vm = vm;
        if (_vm != null)
        {
            _vm.PropertyChanged += Vm_OnPropertyChanged;
        }

        SyncPasswordBoxesFromViewModel();
        // Workspace controls disable themselves while I/O runs; the status-bar Cancel stays usable.
        IsEnabled = !_closeRequested;
    }

    private void Vm_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(MainViewModel.ApiKey), System.StringComparison.Ordinal)
        )
        {
            return;
        }

        SyncPasswordBoxesFromViewModel();
    }

    private void SyncPasswordBoxesFromViewModel()
    {
        if (_vm == null)
        {
            return;
        }

        var desiredGemini = _vm.ApiKey ?? "";
        if (ApiKeyBox.Password != desiredGemini)
        {
            ApiKeyBox.Password = desiredGemini;
        }
    }
}
