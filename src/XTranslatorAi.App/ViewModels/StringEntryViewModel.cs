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

    public string StatusText => StringEntryStatusLabels.ToLabel(Status);

    partial void OnStatusChanged(StringEntryStatus value)
    {
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(UserFacingErrorMessage));
        OnPropertyChanged(nameof(RowToolTip));
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
        OnPropertyChanged(nameof(RowToolTip));
    }

    public string SourcePreview => Preview(SourceText);
    public string DestPreview => Preview(DestText);

    // Loading, translation results, retranslate and the fix-up tools set DestText to what they write to the
    // project DB; only the editor types text the DB has not received. Without this distinction an edit stayed
    // in the grid while XML/ESP export, reopening and the next run read the old DB text.
    private string _savedDestText = "";
    private bool _isEditorChange;

    /// <summary>The translation as the editor shows it. Text typed here is unsaved until committed.</summary>
    public string EditableDestText
    {
        get => DestText;
        set
        {
            _isEditorChange = true;
            try
            {
                DestText = value ?? "";
            }
            finally
            {
                _isEditorChange = false;
            }
        }
    }

    /// <summary>True when the editor changed the translation away from the text last read from or saved to the DB.</summary>
    public bool HasUnsavedDestEdit => !string.Equals(DestText, _savedDestText, StringComparison.Ordinal);

    /// <summary>Records that the project DB now holds <paramref name="savedText"/> for this row.</summary>
    public void MarkDestTextSaved(string savedText) => _savedDestText = savedText;

    partial void OnSourceTextChanged(string value) => OnPropertyChanged(nameof(SourcePreview));

    partial void OnDestTextChanged(string value)
    {
        if (!_isEditorChange)
        {
            _savedDestText = value;
        }

        OnPropertyChanged(nameof(DestPreview));
        OnPropertyChanged(nameof(EditableDestText));
    }

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
