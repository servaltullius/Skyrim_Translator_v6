using System.ComponentModel;
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
                IsEnabled = _vm.IsWorkspaceInteractive;
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
        IsEnabled = !_closeRequested && (_vm?.IsWorkspaceInteractive ?? true);
    }

    private void Vm_OnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(MainViewModel.IsWorkspaceInteractive))
        {
            IsEnabled = !_closeRequested && (_vm?.IsWorkspaceInteractive ?? true);
            return;
        }
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
