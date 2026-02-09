using System;
using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Collections;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels.Tabs;

public interface IGlobalGlossaryTabHost : INotifyPropertyChanged
{
    string GlobalGlossaryCategory { get; set; }
    string GlobalGlossarySourceTerm { get; set; }
    string GlobalGlossaryTargetTerm { get; set; }
    Array GlossaryMatchModeValues { get; }
    GlossaryMatchMode GlobalGlossaryMatchMode { get; set; }
    Array GlossaryForceModeValues { get; }
    GlossaryForceMode GlobalGlossaryForceMode { get; set; }
    int GlobalGlossaryPriority { get; set; }

    IAsyncRelayCommand AddGlobalGlossaryCommand { get; }
    IAsyncRelayCommand SaveGlobalGlossaryChangesCommand { get; }
    IAsyncRelayCommand DeleteGlobalGlossaryEntryCommand { get; }
    IAsyncRelayCommand ImportGlobalGlossaryCommand { get; }
    IAsyncRelayCommand ExportGlobalGlossaryCommand { get; }
    IRelayCommand OpenGlossaryFolderCommand { get; }

    ObservableRangeCollection<string> GlobalGlossaryCategoryFilterValues { get; }
    string GlobalGlossaryFilterCategory { get; set; }
    string GlobalGlossaryFilterText { get; set; }

    ICollectionView GlobalGlossaryView { get; }
    GlossaryEntryViewModel? SelectedGlobalGlossaryEntry { get; set; }
}
