using System;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Collections;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels.Tabs;

public sealed class ProjectGlossaryTabViewModel : ObservableObject
{
    private readonly IProjectGlossaryTabHost _host;

    public ProjectGlossaryTabViewModel(IProjectGlossaryTabHost host)
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

    public string GlossaryCategory
    {
        get => _host.GlossaryCategory;
        set => _host.GlossaryCategory = value;
    }

    public string GlossarySourceTerm
    {
        get => _host.GlossarySourceTerm;
        set => _host.GlossarySourceTerm = value;
    }

    public string GlossaryTargetTerm
    {
        get => _host.GlossaryTargetTerm;
        set => _host.GlossaryTargetTerm = value;
    }

    public Array GlossaryMatchModeValues => _host.GlossaryMatchModeValues;

    public GlossaryMatchMode GlossaryMatchMode
    {
        get => _host.GlossaryMatchMode;
        set => _host.GlossaryMatchMode = value;
    }

    public Array GlossaryForceModeValues => _host.GlossaryForceModeValues;

    public GlossaryForceMode GlossaryForceMode
    {
        get => _host.GlossaryForceMode;
        set => _host.GlossaryForceMode = value;
    }

    public int GlossaryPriority
    {
        get => _host.GlossaryPriority;
        set => _host.GlossaryPriority = value;
    }

    public IAsyncRelayCommand AddGlossaryCommand => _host.AddGlossaryCommand;
    public IAsyncRelayCommand SaveGlossaryChangesCommand => _host.SaveGlossaryChangesCommand;
    public IAsyncRelayCommand DeleteGlossaryEntryCommand => _host.DeleteGlossaryEntryCommand;
    public IAsyncRelayCommand ImportGlossaryCommand => _host.ImportGlossaryCommand;
    public IAsyncRelayCommand ExportGlossaryCommand => _host.ExportGlossaryCommand;
    public IRelayCommand OpenGlossaryFolderCommand => _host.OpenGlossaryFolderCommand;

    public ObservableRangeCollection<string> GlossaryCategoryFilterValues => _host.GlossaryCategoryFilterValues;

    public string GlossaryFilterCategory
    {
        get => _host.GlossaryFilterCategory;
        set => _host.GlossaryFilterCategory = value;
    }

    public string GlossaryFilterText
    {
        get => _host.GlossaryFilterText;
        set => _host.GlossaryFilterText = value;
    }

    public ICollectionView GlossaryView => _host.GlossaryView;

    public GlossaryEntryViewModel? SelectedGlossaryEntry
    {
        get => _host.SelectedGlossaryEntry;
        set => _host.SelectedGlossaryEntry = value;
    }
}
