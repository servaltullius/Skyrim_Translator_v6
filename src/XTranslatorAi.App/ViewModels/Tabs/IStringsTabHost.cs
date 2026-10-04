using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Collections;

namespace XTranslatorAi.App.ViewModels.Tabs;

public interface IStringsTabHost : INotifyPropertyChanged
{
    bool IsProjectLoaded { get; }
    bool IsTranslating { get; }
    string EntryFilterText { get; set; }
    ObservableRangeCollection<string> EntryStatusFilterValues { get; }
    string EntryFilterStatus { get; set; }
    bool EntryFilterTagsOnly { get; set; }
    bool EntryFilterTagMismatchOnly { get; set; }
    ICollectionView EntriesView { get; }
    StringEntryViewModel? SelectedEntry { get; set; }
    IAsyncRelayCommand SaveSelectedDestCommand { get; }
    IAsyncRelayCommand<System.Collections.IList?> RetranslateSelectedCommand { get; }
    IAsyncRelayCommand RetranslateVisibleCommand { get; }

    string GlossaryLookupText { get; set; }
    bool GlossaryLookupIncludeProject { get; set; }
    bool GlossaryLookupIncludeGlobal { get; set; }
    ICollectionView GlossaryLookupResultsView { get; }
}
