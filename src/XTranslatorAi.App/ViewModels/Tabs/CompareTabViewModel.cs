using System.Collections;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels.Tabs;

public sealed class CompareTabViewModel : ObservableObject
{
    private readonly ICompareTabHost _host;

    public CompareTabViewModel(ICompareTabHost host)
    {
        _host = host;
        _host.PropertyChanged += HostOnPropertyChanged;
    }

    private void HostOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrWhiteSpace(e.PropertyName))
        {
            OnPropertyChanged(string.Empty);
            return;
        }

        OnPropertyChanged(e.PropertyName);
    }

    public IAsyncRelayCommand RunCompareAllCommand => _host.RunCompareAllCommand;
    public IRelayCommand ClearCompareOutputsCommand => _host.ClearCompareOutputsCommand;

    public bool CompareIncludeProjectGlossary
    {
        get => _host.CompareIncludeProjectGlossary;
        set => _host.CompareIncludeProjectGlossary = value;
    }

    public bool CompareIncludeGlobalGlossary
    {
        get => _host.CompareIncludeGlobalGlossary;
        set => _host.CompareIncludeGlobalGlossary = value;
    }

    public bool CompareIncludeGlobalTranslationMemory
    {
        get => _host.CompareIncludeGlobalTranslationMemory;
        set => _host.CompareIncludeGlobalTranslationMemory = value;
    }

    public string CompareSelectedEntrySummary => _host.CompareSelectedEntrySummary;

    public StringEntryViewModel? SelectedEntry
    {
        get => _host.SelectedEntry;
        set => _host.SelectedEntry = value;
    }

    public IEnumerable Compare1AvailableModels => _host.Compare1AvailableModels;
    public IEnumerable Compare2AvailableModels => _host.Compare2AvailableModels;
    public IEnumerable Compare3AvailableModels => _host.Compare3AvailableModels;

    public string Compare1Model
    {
        get => _host.Compare1Model;
        set => _host.Compare1Model = value;
    }

    public bool Compare1ThinkingOff
    {
        get => _host.Compare1ThinkingOff;
        set => _host.Compare1ThinkingOff = value;
    }

    public string Compare1Status
    {
        get => _host.Compare1Status;
        set => _host.Compare1Status = value;
    }

    public string Compare1Output
    {
        get => _host.Compare1Output;
        set => _host.Compare1Output = value;
    }

    public IAsyncRelayCommand RunCompare1Command => _host.RunCompare1Command;

    public string Compare2Model
    {
        get => _host.Compare2Model;
        set => _host.Compare2Model = value;
    }

    public bool Compare2ThinkingOff
    {
        get => _host.Compare2ThinkingOff;
        set => _host.Compare2ThinkingOff = value;
    }

    public string Compare2Status
    {
        get => _host.Compare2Status;
        set => _host.Compare2Status = value;
    }

    public string Compare2Output
    {
        get => _host.Compare2Output;
        set => _host.Compare2Output = value;
    }

    public IAsyncRelayCommand RunCompare2Command => _host.RunCompare2Command;

    public string Compare3Model
    {
        get => _host.Compare3Model;
        set => _host.Compare3Model = value;
    }

    public bool Compare3ThinkingOff
    {
        get => _host.Compare3ThinkingOff;
        set => _host.Compare3ThinkingOff = value;
    }

    public string Compare3Status
    {
        get => _host.Compare3Status;
        set => _host.Compare3Status = value;
    }

    public string Compare3Output
    {
        get => _host.Compare3Output;
        set => _host.Compare3Output = value;
    }

    public IAsyncRelayCommand RunCompare3Command => _host.RunCompare3Command;
}
