using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels.Tabs;

public interface IProjectContextTabHost : INotifyPropertyChanged
{
    bool EnableProjectContext { get; set; }
    IAsyncRelayCommand GenerateProjectContextCommand { get; }
    IAsyncRelayCommand SaveProjectContextCommand { get; }
    IAsyncRelayCommand ClearProjectContextCommand { get; }
    string ProjectContextPreview { get; set; }
}
