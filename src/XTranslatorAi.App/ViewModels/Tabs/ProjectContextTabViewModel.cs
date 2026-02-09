using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace XTranslatorAi.App.ViewModels.Tabs;

public sealed class ProjectContextTabViewModel : ObservableObject
{
    private readonly IProjectContextTabHost _host;

    public ProjectContextTabViewModel(IProjectContextTabHost host)
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

    public bool EnableProjectContext
    {
        get => _host.EnableProjectContext;
        set => _host.EnableProjectContext = value;
    }

    public IAsyncRelayCommand GenerateProjectContextCommand => _host.GenerateProjectContextCommand;
    public IAsyncRelayCommand SaveProjectContextCommand => _host.SaveProjectContextCommand;
    public IAsyncRelayCommand ClearProjectContextCommand => _host.ClearProjectContextCommand;

    public string ProjectContextPreview
    {
        get => _host.ProjectContextPreview;
        set => _host.ProjectContextPreview = value;
    }
}
