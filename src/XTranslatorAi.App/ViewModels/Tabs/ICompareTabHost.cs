using System.Collections;
using System.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels.Tabs;

public interface ICompareTabHost : INotifyPropertyChanged
{
    IAsyncRelayCommand RunCompareAllCommand { get; }
    IRelayCommand ClearCompareOutputsCommand { get; }

    bool CompareIncludeProjectGlossary { get; set; }
    bool CompareIncludeGlobalGlossary { get; set; }
    bool CompareIncludeGlobalTranslationMemory { get; set; }

    string CompareSelectedEntrySummary { get; }
    StringEntryViewModel? SelectedEntry { get; set; }

    IEnumerable Compare1AvailableModels { get; }
    IEnumerable Compare2AvailableModels { get; }
    IEnumerable Compare3AvailableModels { get; }

    string Compare1Model { get; set; }
    bool Compare1ThinkingOff { get; set; }
    string Compare1Status { get; set; }
    string Compare1Output { get; set; }
    IAsyncRelayCommand RunCompare1Command { get; }

    string Compare2Model { get; set; }
    bool Compare2ThinkingOff { get; set; }
    string Compare2Status { get; set; }
    string Compare2Output { get; set; }
    IAsyncRelayCommand RunCompare2Command { get; }

    string Compare3Model { get; set; }
    bool Compare3ThinkingOff { get; set; }
    string Compare3Status { get; set; }
    string Compare3Output { get; set; }
    IAsyncRelayCommand RunCompare3Command { get; }
}
