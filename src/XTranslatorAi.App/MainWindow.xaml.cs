using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using XTranslatorAi.App.ViewModels;

namespace XTranslatorAi.App;

public partial class MainWindow : Window
{
    private MainViewModel? _vm;

    public MainWindow() : this(null) { }

    public MainWindow(MainViewModel? viewModel)
    {
        InitializeComponent();
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
