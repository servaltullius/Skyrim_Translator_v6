using CommunityToolkit.Mvvm.ComponentModel;

namespace XTranslatorAi.App.ViewModels;

public partial class TranslationMemoryEntryViewModel : ObservableObject
{
    public long Id { get; }

    private int _suppressDirtyTracking;

    [ObservableProperty] private string _sourceText = "";
    [ObservableProperty] private string _destText = "";
    [ObservableProperty] private string _updatedAt = "";
    [ObservableProperty] private bool _isDirty;

    public TranslationMemoryEntryViewModel(long id)
    {
        Id = id;
    }

    public void BeginUpdate() => _suppressDirtyTracking++;

    public void EndUpdate()
    {
        if (_suppressDirtyTracking > 0)
        {
            _suppressDirtyTracking--;
        }
    }

    public void MarkClean() => IsDirty = false;

    partial void OnSourceTextChanged(string value) => MarkDirty();
    partial void OnDestTextChanged(string value) => MarkDirty();

    private void MarkDirty()
    {
        if (_suppressDirtyTracking > 0)
        {
            return;
        }

        IsDirty = true;
    }
}

