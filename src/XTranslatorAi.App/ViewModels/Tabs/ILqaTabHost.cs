using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels.Tabs;

public interface ILqaTabHost : INotifyPropertyChanged
{
    IAsyncRelayCommand ScanLqaCommand { get; }
    IRelayCommand ClearLqaCommand { get; }

    string LqaFilterText { get; set; }
    ICollectionView LqaIssuesView { get; }

    LqaIssueViewModel? SelectedLqaIssue { get; set; }
    StringEntryViewModel? SelectedEntry { get; set; }

    IAsyncRelayCommand SaveSelectedDestCommand { get; }
}
