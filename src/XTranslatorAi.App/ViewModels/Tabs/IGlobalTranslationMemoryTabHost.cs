using System.ComponentModel;
using System.Windows.Data;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels.Tabs;

public interface IGlobalTranslationMemoryTabHost : INotifyPropertyChanged
{
    string GlobalTranslationMemorySourceText { get; set; }
    string GlobalTranslationMemoryDestText { get; set; }

    IAsyncRelayCommand AddGlobalTranslationMemoryCommand { get; }
    IAsyncRelayCommand SaveGlobalTranslationMemoryChangesCommand { get; }
    IAsyncRelayCommand DeleteGlobalTranslationMemoryEntryCommand { get; }
    IAsyncRelayCommand ReloadGlobalTranslationMemoryCommand { get; }
    IAsyncRelayCommand ImportGlobalTranslationMemoryFromTabCommand { get; }
    IAsyncRelayCommand ExportGlobalTranslationMemoryCommand { get; }

    string GlobalTranslationMemoryFilterText { get; set; }
    ICollectionView GlobalTranslationMemoryView { get; }

    TranslationMemoryEntryViewModel? SelectedGlobalTranslationMemoryEntry { get; set; }
}
