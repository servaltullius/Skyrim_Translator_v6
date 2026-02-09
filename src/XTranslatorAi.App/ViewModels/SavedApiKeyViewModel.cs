using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace XTranslatorAi.App.ViewModels;

public sealed partial class SavedApiKeyViewModel : ObservableObject
{
    [ObservableProperty] private string _name = "";

    [ObservableProperty] private string _apiKey = "";

    public string DisplayLabel
    {
        get
        {
            var label = string.IsNullOrWhiteSpace(Name) ? "Key" : Name.Trim();
            return $"{label} ({Mask(ApiKey)})";
        }
    }

    partial void OnNameChanged(string value) => OnPropertyChanged(nameof(DisplayLabel));

    partial void OnApiKeyChanged(string value) => OnPropertyChanged(nameof(DisplayLabel));

    private static string Mask(string apiKey)
    {
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return "…";
        }

        var trimmed = apiKey.Trim();
        if (trimmed.Length <= 6)
        {
            return "…" + trimmed;
        }

        return "…" + trimmed.Substring(trimmed.Length - 6);
    }
}

