using System;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XTranslatorAi.App.ViewModels.Tabs;

public sealed class PromptTabViewModel : ObservableObject
{
    private readonly IPromptTabHost _host;

    public PromptTabViewModel(IPromptTabHost host)
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

    public string BasePromptText => _host.BasePromptText;

    public bool UseCustomPrompt
    {
        get => _host.UseCustomPrompt;
        set => _host.UseCustomPrompt = value;
    }

    public bool UseRecStyleHints
    {
        get => _host.UseRecStyleHints;
        set => _host.UseRecStyleHints = value;
    }

    public string CustomPromptText
    {
        get => _host.CustomPromptText;
        set => _host.CustomPromptText = value;
    }

    public bool HasPromptLintIssues => _host.HasPromptLintIssues;

    public bool HasPromptLintBlockingIssues => _host.HasPromptLintBlockingIssues;

    public string PromptLintSummary => _host.PromptLintSummary;

    public string PromptLintDetails => _host.PromptLintDetails;
}
