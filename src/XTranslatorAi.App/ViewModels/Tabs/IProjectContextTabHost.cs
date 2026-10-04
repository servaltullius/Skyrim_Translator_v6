using System.ComponentModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels.Tabs;

public interface IProjectContextTabHost : INotifyPropertyChanged
{
    bool EnableProjectContext { get; set; }
    IAsyncRelayCommand GenerateProjectContextCommand { get; }
    IAsyncRelayCommand SaveProjectContextCommand { get; }
    IAsyncRelayCommand ClearProjectContextCommand { get; }
    string ProjectContextPreview { get; set; }
    string PreviousTranslationSummary { get; }
    IAsyncRelayCommand ImportPreviousTranslationCommand { get; }
    IAsyncRelayCommand ClearPreviousTranslationCommand { get; }
    bool CanLinkDroppedPreviousTranslation(string? path);
    Task LinkDroppedPreviousTranslationAsync(string path);
}
