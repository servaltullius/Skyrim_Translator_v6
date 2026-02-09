using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels.Tabs;

public sealed class LqaTabViewModel : ObservableObject
{
    private readonly ILqaTabHost _host;

    public LqaTabViewModel(ILqaTabHost host)
    {
        _host = host;
        _host.PropertyChanged += HostOnPropertyChanged;
    }

    private void HostOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName))
        {
            OnPropertyChanged(string.Empty);
            return;
        }

        OnPropertyChanged(e.PropertyName);
    }

    public IAsyncRelayCommand ScanLqaCommand => _host.ScanLqaCommand;
    public IRelayCommand ClearLqaCommand => _host.ClearLqaCommand;

    public string LqaFilterText
    {
        get => _host.LqaFilterText;
        set => _host.LqaFilterText = value;
    }

    public ICollectionView LqaIssuesView => _host.LqaIssuesView;

    public LqaIssueViewModel? SelectedLqaIssue
    {
        get => _host.SelectedLqaIssue;
        set => _host.SelectedLqaIssue = value;
    }

    public StringEntryViewModel? SelectedEntry
    {
        get => _host.SelectedEntry;
        set => _host.SelectedEntry = value;
    }

    public IAsyncRelayCommand SaveSelectedDestCommand => _host.SaveSelectedDestCommand;
}
