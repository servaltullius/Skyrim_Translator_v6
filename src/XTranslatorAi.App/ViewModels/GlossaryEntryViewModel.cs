using CommunityToolkit.Mvvm.ComponentModel;
using XTranslatorAi.Core.Text;

namespace XTranslatorAi.App.ViewModels;

public partial class GlossaryEntryViewModel : ObservableObject
{
    public long Id { get; }

    private int _suppressDirtyTracking;

    [ObservableProperty] private string _category = "";
    [ObservableProperty] private string _sourceTerm = "";
    [ObservableProperty] private string _targetTerm = "";
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private GlossaryMatchMode _matchMode = GlossaryMatchMode.WordBoundary;
    [ObservableProperty] private GlossaryForceMode _forceMode = GlossaryForceMode.ForceToken;
    [ObservableProperty] private int _priority = 0;
    [ObservableProperty] private string? _note;
    [ObservableProperty] private bool _isDirty;

    public GlossaryEntryViewModel(long id)
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

    partial void OnCategoryChanged(string value) => MarkDirty();
    partial void OnSourceTermChanged(string value) => MarkDirty();
    partial void OnTargetTermChanged(string value) => MarkDirty();
    partial void OnEnabledChanged(bool value) => MarkDirty();
    partial void OnMatchModeChanged(GlossaryMatchMode value) => MarkDirty();
    partial void OnForceModeChanged(GlossaryForceMode value) => MarkDirty();
    partial void OnPriorityChanged(int value) => MarkDirty();
    partial void OnNoteChanged(string? value) => MarkDirty();

    private void MarkDirty()
    {
        if (_suppressDirtyTracking > 0)
        {
            return;
        }

        IsDirty = true;
    }
}
