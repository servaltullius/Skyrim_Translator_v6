using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels.Tabs;

public sealed class GlobalTranslationMemoryTabViewModel : ObservableObject
{
    private readonly IGlobalTranslationMemoryTabHost _host;

    public GlobalTranslationMemoryTabViewModel(IGlobalTranslationMemoryTabHost host)
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

    public string GlobalTranslationMemorySourceText
    {
        get => _host.GlobalTranslationMemorySourceText;
        set => _host.GlobalTranslationMemorySourceText = value;
    }

    public string GlobalTranslationMemoryDestText
    {
        get => _host.GlobalTranslationMemoryDestText;
        set => _host.GlobalTranslationMemoryDestText = value;
    }

    public IAsyncRelayCommand AddGlobalTranslationMemoryCommand => _host.AddGlobalTranslationMemoryCommand;
    public IAsyncRelayCommand SaveGlobalTranslationMemoryChangesCommand => _host.SaveGlobalTranslationMemoryChangesCommand;
    public IAsyncRelayCommand DeleteGlobalTranslationMemoryEntryCommand => _host.DeleteGlobalTranslationMemoryEntryCommand;
    public IAsyncRelayCommand ReloadGlobalTranslationMemoryCommand => _host.ReloadGlobalTranslationMemoryCommand;
    public IAsyncRelayCommand ImportGlobalTranslationMemoryFromTabCommand => _host.ImportGlobalTranslationMemoryFromTabCommand;
    public IAsyncRelayCommand ExportGlobalTranslationMemoryCommand => _host.ExportGlobalTranslationMemoryCommand;

    public string GlobalTranslationMemoryFilterText
    {
        get => _host.GlobalTranslationMemoryFilterText;
        set => _host.GlobalTranslationMemoryFilterText = value;
    }

    public ICollectionView GlobalTranslationMemoryView => _host.GlobalTranslationMemoryView;

    public TranslationMemoryEntryViewModel? SelectedGlobalTranslationMemoryEntry
    {
        get => _host.SelectedGlobalTranslationMemoryEntry;
        set => _host.SelectedGlobalTranslationMemoryEntry = value;
    }
}
