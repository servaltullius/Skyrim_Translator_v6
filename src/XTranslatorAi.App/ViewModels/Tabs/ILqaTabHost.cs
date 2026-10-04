using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels.Tabs;

public interface ILqaTabHost : INotifyPropertyChanged
{
    IAsyncRelayCommand ScanLqaCommand { get; }
    IRelayCommand ClearLqaCommand { get; }

    string LqaFilterText { get; set; }
    bool LqaHideInfo { get; set; }
    string LqaVisibleSummary { get; }
    ICollectionView LqaIssuesView { get; }

    LqaIssueViewModel? SelectedLqaIssue { get; set; }
    StringEntryViewModel? SelectedEntry { get; set; }
    bool IsTranslating { get; }

    IAsyncRelayCommand SaveSelectedDestCommand { get; }
}
