using System;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels.Tabs;

public sealed class GlobalTranslationMemoryTabViewModel : ObservableObject, IDisposable
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

    public string FranchiseTranslationMemorySourceText
    {
        get => _host.FranchiseTranslationMemorySourceText;
        set => _host.FranchiseTranslationMemorySourceText = value;
    }

    public string FranchiseTranslationMemoryDestText
    {
        get => _host.FranchiseTranslationMemoryDestText;
        set => _host.FranchiseTranslationMemoryDestText = value;
    }

    public IAsyncRelayCommand AddFranchiseTranslationMemoryCommand => _host.AddFranchiseTranslationMemoryCommand;
    public IAsyncRelayCommand SaveFranchiseTranslationMemoryChangesCommand => _host.SaveFranchiseTranslationMemoryChangesCommand;
    public IAsyncRelayCommand DeleteFranchiseTranslationMemoryEntryCommand => _host.DeleteFranchiseTranslationMemoryEntryCommand;
    public IAsyncRelayCommand ReloadFranchiseTranslationMemoryCommand => _host.ReloadFranchiseTranslationMemoryCommand;
    public IAsyncRelayCommand ImportFranchiseTranslationMemoryFromTabCommand => _host.ImportFranchiseTranslationMemoryFromTabCommand;
    public IAsyncRelayCommand ExportFranchiseTranslationMemoryCommand => _host.ExportFranchiseTranslationMemoryCommand;

    public string FranchiseTranslationMemoryFilterText
    {
        get => _host.FranchiseTranslationMemoryFilterText;
        set => _host.FranchiseTranslationMemoryFilterText = value;
    }

    public ICollectionView FranchiseTranslationMemoryView => _host.FranchiseTranslationMemoryView;

    public TranslationMemoryEntryViewModel? SelectedFranchiseTranslationMemoryEntry
    {
        get => _host.SelectedFranchiseTranslationMemoryEntry;
        set => _host.SelectedFranchiseTranslationMemoryEntry = value;
    }

    private bool _disposed;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _host.PropertyChanged -= HostOnPropertyChanged;
    }
}
