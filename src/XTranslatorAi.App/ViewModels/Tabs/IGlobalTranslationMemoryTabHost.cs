using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels.Tabs;

public interface IGlobalTranslationMemoryTabHost : INotifyPropertyChanged
{
    string FranchiseTranslationMemorySourceText { get; set; }
    string FranchiseTranslationMemoryDestText { get; set; }

    IAsyncRelayCommand AddFranchiseTranslationMemoryCommand { get; }
    IAsyncRelayCommand SaveFranchiseTranslationMemoryChangesCommand { get; }
    IAsyncRelayCommand DeleteFranchiseTranslationMemoryEntryCommand { get; }
    IAsyncRelayCommand ReloadFranchiseTranslationMemoryCommand { get; }
    IAsyncRelayCommand ImportFranchiseTranslationMemoryFromTabCommand { get; }
    IAsyncRelayCommand ExportFranchiseTranslationMemoryCommand { get; }

    string FranchiseTranslationMemoryFilterText { get; set; }
    ICollectionView FranchiseTranslationMemoryView { get; }

    TranslationMemoryEntryViewModel? SelectedFranchiseTranslationMemoryEntry { get; set; }
}
