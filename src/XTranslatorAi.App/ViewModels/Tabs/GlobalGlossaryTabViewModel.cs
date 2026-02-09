using System;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Collections;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels.Tabs;

public sealed class GlobalGlossaryTabViewModel : ObservableObject
{
    private readonly IGlobalGlossaryTabHost _host;

    public GlobalGlossaryTabViewModel(IGlobalGlossaryTabHost host)
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

    public string GlobalGlossaryCategory
    {
        get => _host.GlobalGlossaryCategory;
        set => _host.GlobalGlossaryCategory = value;
    }

    public string GlobalGlossarySourceTerm
    {
        get => _host.GlobalGlossarySourceTerm;
        set => _host.GlobalGlossarySourceTerm = value;
    }

    public string GlobalGlossaryTargetTerm
    {
        get => _host.GlobalGlossaryTargetTerm;
        set => _host.GlobalGlossaryTargetTerm = value;
    }

    public Array GlossaryMatchModeValues => _host.GlossaryMatchModeValues;

    public GlossaryMatchMode GlobalGlossaryMatchMode
    {
        get => _host.GlobalGlossaryMatchMode;
        set => _host.GlobalGlossaryMatchMode = value;
    }

    public Array GlossaryForceModeValues => _host.GlossaryForceModeValues;

    public GlossaryForceMode GlobalGlossaryForceMode
    {
        get => _host.GlobalGlossaryForceMode;
        set => _host.GlobalGlossaryForceMode = value;
    }

    public int GlobalGlossaryPriority
    {
        get => _host.GlobalGlossaryPriority;
        set => _host.GlobalGlossaryPriority = value;
    }

    public IAsyncRelayCommand AddGlobalGlossaryCommand => _host.AddGlobalGlossaryCommand;
    public IAsyncRelayCommand SaveGlobalGlossaryChangesCommand => _host.SaveGlobalGlossaryChangesCommand;
    public IAsyncRelayCommand DeleteGlobalGlossaryEntryCommand => _host.DeleteGlobalGlossaryEntryCommand;
    public IAsyncRelayCommand ImportGlobalGlossaryCommand => _host.ImportGlobalGlossaryCommand;
    public IAsyncRelayCommand ExportGlobalGlossaryCommand => _host.ExportGlobalGlossaryCommand;
    public IRelayCommand OpenGlossaryFolderCommand => _host.OpenGlossaryFolderCommand;

    public ObservableRangeCollection<string> GlobalGlossaryCategoryFilterValues => _host.GlobalGlossaryCategoryFilterValues;

    public string GlobalGlossaryFilterCategory
    {
        get => _host.GlobalGlossaryFilterCategory;
        set => _host.GlobalGlossaryFilterCategory = value;
    }

    public string GlobalGlossaryFilterText
    {
        get => _host.GlobalGlossaryFilterText;
        set => _host.GlobalGlossaryFilterText = value;
    }

    public ICollectionView GlobalGlossaryView => _host.GlobalGlossaryView;

    public GlossaryEntryViewModel? SelectedGlobalGlossaryEntry
    {
        get => _host.SelectedGlobalGlossaryEntry;
        set => _host.SelectedGlobalGlossaryEntry = value;
    }
}
