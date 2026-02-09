using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;
using XTranslatorAi.App.Collections;

namespace XTranslatorAi.App.ViewModels.Tabs;

public interface IStringsTabHost : INotifyPropertyChanged
{
    string EntryFilterText { get; set; }
    ObservableRangeCollection<string> EntryStatusFilterValues { get; }
    string EntryFilterStatus { get; set; }
    bool EntryFilterTagsOnly { get; set; }
    bool EntryFilterTagMismatchOnly { get; set; }
    ICollectionView EntriesView { get; }
    StringEntryViewModel? SelectedEntry { get; set; }
    IAsyncRelayCommand SaveSelectedDestCommand { get; }

    string GlossaryLookupText { get; set; }
    bool GlossaryLookupIncludeProject { get; set; }
    bool GlossaryLookupIncludeGlobal { get; set; }
    ICollectionView GlossaryLookupResultsView { get; }
}
