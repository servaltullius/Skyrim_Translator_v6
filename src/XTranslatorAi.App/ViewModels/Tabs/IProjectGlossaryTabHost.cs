using System;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Collections;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels.Tabs;

public interface IProjectGlossaryTabHost : INotifyPropertyChanged
{
    string GlossaryCategory { get; set; }
    string GlossarySourceTerm { get; set; }
    string GlossaryTargetTerm { get; set; }
    Array GlossaryMatchModeValues { get; }
    GlossaryMatchMode GlossaryMatchMode { get; set; }
    Array GlossaryForceModeValues { get; }
    GlossaryForceMode GlossaryForceMode { get; set; }
    int GlossaryPriority { get; set; }

    IAsyncRelayCommand AddGlossaryCommand { get; }
    IAsyncRelayCommand SaveGlossaryChangesCommand { get; }
    IAsyncRelayCommand DeleteGlossaryEntryCommand { get; }
    IAsyncRelayCommand ImportGlossaryCommand { get; }
    IAsyncRelayCommand ExportGlossaryCommand { get; }
    IRelayCommand OpenGlossaryFolderCommand { get; }

    ObservableRangeCollection<string> GlossaryCategoryFilterValues { get; }
    string GlossaryFilterCategory { get; set; }
    string GlossaryFilterText { get; set; }

    ICollectionView GlossaryView { get; }
    GlossaryEntryViewModel? SelectedGlossaryEntry { get; set; }
}
