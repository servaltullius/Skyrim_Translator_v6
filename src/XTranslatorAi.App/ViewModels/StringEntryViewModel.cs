using System;
using CommunityToolkit.Mvvm.ComponentModel;
using XTranslatorAi.Core.Diagnostics;
using XTranslatorAi.Core.Models;

namespace XTranslatorAi.App.ViewModels;

public partial class StringEntryViewModel : ObservableObject
{
    public long Id { get; }
    public int OrderIndex { get; }

    [ObservableProperty] private string? _edid;
    [ObservableProperty] private string? _rec;
    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private string _destText = "";
    [ObservableProperty] private StringEntryStatus _status;
    [ObservableProperty] private string? _errorMessage;
    [ObservableProperty] private bool _isTranslationMemoryApplied;

    public StringEntryViewModel(long id, int orderIndex)
    {
        Id = id;
        OrderIndex = orderIndex;
    }

    public string StatusText => Status.ToString();

    partial void OnStatusChanged(StringEntryStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(UserFacingErrorMessage));
    }

    public string UserFacingErrorMessage
    {
        get
        {
            if (Status != StringEntryStatus.Error)
            {
                return "";
            }

            var error = UserFacingErrorClassifier.ClassifyErrorMessage(ErrorMessage);
            if (error.Code == "E000")
            {
                return "";
            }

            var suffix = error.DetailsInApiLogs ? " (API Logs)" : "";
            return $"{error.Code}: {error.Message}{suffix}";
        }
    }

    partial void OnErrorMessageChanged(string? value)
    {
        OnPropertyChanged(nameof(UserFacingErrorMessage));
    }

    public string SourcePreview => Preview(SourceText);
    public string DestPreview => Preview(DestText);

    partial void OnSourceTextChanged(string value) => OnPropertyChanged(nameof(SourcePreview));
    partial void OnDestTextChanged(string value) => OnPropertyChanged(nameof(DestPreview));

    private static string Preview(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return "";
        }
        var s = text.Replace("\r", "").Replace("\n", " ");
        return s.Length <= 120 ? s : s[..120] + "…";
    }
}
