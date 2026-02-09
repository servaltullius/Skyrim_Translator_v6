using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Collections;

namespace XTranslatorAi.App.ViewModels.Tabs;

public sealed class StringsTabViewModel : ObservableObject
{
    private readonly IStringsTabHost _host;

    public StringsTabViewModel(IStringsTabHost host)
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

    public string EntryFilterText
    {
        get => _host.EntryFilterText;
        set => _host.EntryFilterText = value;
    }

    public ObservableRangeCollection<string> EntryStatusFilterValues => _host.EntryStatusFilterValues;

    public string EntryFilterStatus
    {
        get => _host.EntryFilterStatus;
        set => _host.EntryFilterStatus = value;
    }

    public bool EntryFilterTagsOnly
    {
        get => _host.EntryFilterTagsOnly;
        set => _host.EntryFilterTagsOnly = value;
    }

    public bool EntryFilterTagMismatchOnly
    {
        get => _host.EntryFilterTagMismatchOnly;
        set => _host.EntryFilterTagMismatchOnly = value;
    }

    public ICollectionView EntriesView => _host.EntriesView;

    public StringEntryViewModel? SelectedEntry
    {
        get => _host.SelectedEntry;
        set => _host.SelectedEntry = value;
    }

    public IAsyncRelayCommand SaveSelectedDestCommand => _host.SaveSelectedDestCommand;

    public string GlossaryLookupText
    {
        get => _host.GlossaryLookupText;
        set => _host.GlossaryLookupText = value;
    }

    public bool GlossaryLookupIncludeProject
    {
        get => _host.GlossaryLookupIncludeProject;
        set => _host.GlossaryLookupIncludeProject = value;
    }

    public bool GlossaryLookupIncludeGlobal
    {
        get => _host.GlossaryLookupIncludeGlobal;
        set => _host.GlossaryLookupIncludeGlobal = value;
    }

    public ICollectionView GlossaryLookupResultsView => _host.GlossaryLookupResultsView;
}
